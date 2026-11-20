using System.Xml.Linq;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Software;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class WorkspaceTests
{
    [Theory]
    [InlineData("Google Chrome", ApplicationGroup.Internet)]
    [InlineData("Microsoft Edge WebView2 Runtime", ApplicationGroup.Utilities)]
    [InlineData("Microsoft Edge", ApplicationGroup.Internet)]
    [InlineData("Microsoft Office Professional Plus 2024", ApplicationGroup.Office)]
    [InlineData("VLC media player", ApplicationGroup.Media)]
    [InlineData("Epic Games Launcher", ApplicationGroup.Games)]
    [InlineData("Microsoft Visual Studio Code (User)", ApplicationGroup.Development)]
    [InlineData("Microsoft .NET SDK 10.0", ApplicationGroup.Development)]
    [InlineData("Microsoft .NET Runtime 10.0", ApplicationGroup.Utilities)]
    [InlineData("AVG TuneUp", ApplicationGroup.Utilities)]
    [InlineData("AVG AntiVirus FREE", ApplicationGroup.Security)]
    [InlineData("7-Zip 24.09 (x64)", ApplicationGroup.Utilities)]
    [InlineData("Git 2.50.0", ApplicationGroup.Development)]
    [InlineData("GitHub-not-the-Git-package", ApplicationGroup.Other)]
    [InlineData("Bravery Simulator", ApplicationGroup.Other)]
    [InlineData("Unknown Microsoft Product", ApplicationGroup.Other)]
    public void SuggestedAppGroupsAreSpecificAndUnknownsRemainOther(string name, ApplicationGroup expected) =>
        Assert.Equal(expected, ApplicationGroups.Suggest(name));

    private static XElement Event(int id, string data, string date = "2026-11-17T10:00:00Z", string provider = "Microsoft-Windows-Diagnostics-Performance") =>
        XElement.Parse($"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='{provider}'/><EventID>{id}</EventID><TimeCreated SystemTime='{date}'/></System><EventData>{data}</EventData></Event>");

    [Fact]
    public void StartupParserKeepsLatestReportsAndUsesActualNamedFields()
    {
        var report = StartupMeasurements.Parse([
            Event(100, "<Data Name='BootTime'>41000</Data>"),
            Event(101, @"<Data Name='Name'>C:\Program Files\Browser\browser.exe</Data><Data Name='TotalTime'>2500</Data><Data Name='DegradationTime'>700</Data>"),
            Event(101, "<Data Name='Name'>browser.exe</Data><Data Name='TotalTime'>5000</Data>", "2026-11-16T10:00:00Z"),
        ]);
        Assert.Equal(41000, report.Boot!.DurationMs);
        var app = Assert.Single(report.Applications);
        Assert.Equal("browser.exe", app.Executable);
        Assert.Equal(2500, app.DurationMs);
        Assert.Equal(700, app.DelayMs);
    }

    [Theory]
    [InlineData("<Data Name='Name'>app.exe</Data>")]
    [InlineData("<Data Name='Name'>app.exe</Data><Data Name='TotalTime'>-1</Data>")]
    [InlineData("<Data Name='Name'>app.exe</Data><Data Name='TotalTime'>unknown</Data>")]
    [InlineData("<Data Name='Name'>app.exe</Data><Data Name='TotalTime'>9999999999999</Data>")]
    [InlineData("<Data Name='Name'>app.exe</Data><Data Name='TotalTime'>1</Data><Data Name='TotalTime'>2</Data>")]
    [InlineData("<Data Name='Name'>app.dll</Data><Data Name='TotalTime'>200</Data>")]
    public void MalformedOrMissingMeasurementsAreNotInvented(string data) =>
        Assert.Empty(StartupMeasurements.Parse([Event(101, data)]).Applications);

    [Fact]
    public void EventsFromAnotherProviderCannotProduceStartupTimings() => Assert.Empty(StartupMeasurements.Parse([
        Event(101, "<Data Name='Name'>app.exe</Data><Data Name='TotalTime'>200</Data>", provider: "Other")]).Applications);

    [Fact]
    public void ReportsMatchOnlyAnUnambiguousExecutableAndKeepUnmeasuredRegistrations()
    {
        StartupRegistration[] registrations = [
            new(new("Browser", "HKCU-Run", "Browser"), "browser.exe"),
            new(new("Chat", "HKCU-Run", "Chat"), "chat.exe"),
            new(new("Chat other account", "HKLM-Run", "Chat"), "chat.exe"),
            new(new("Unmeasured", "UserFolder", "App.lnk"), null),
        ];
        var now = DateTimeOffset.UtcNow;
        var rows = StartupMeasurements.Match(registrations, new([
            new("BROWSER.EXE", 2000, null, now), new("chat.exe", 4000, null, now), new("system.exe", 500, null, now)], null));

        Assert.Equal(6, rows.Count);
        Assert.Equal("chat.exe", rows[0].Name);
        Assert.Null(rows[0].Entry);
        Assert.Equal(2000, rows.Single(r => r.Name == "Browser").Measurement!.DurationMs);
        Assert.Null(rows.Single(r => r.Name == "Chat").Measurement);
        Assert.Null(rows.Single(r => r.Name == "Unmeasured").Measurement);
        Assert.Null(rows.Single(r => r.Name == "system.exe").Entry);
    }

    [Fact]
    public void DisabledRegistrationRemainsVisibleButDoesNotCountAsEnabled()
    {
        var entry = new StartupEntry("Disabled app", "HKCU-Run", "Disabled") { Enabled = false };
        Assert.Empty(new StartupSnapshot([entry]).EnabledEntries);
        var row = Assert.Single(StartupMeasurements.Match([new(entry, "app.exe")], new([], null)));
        Assert.False(row.Entry!.Enabled);
        Assert.Null(row.Measurement);
    }
}

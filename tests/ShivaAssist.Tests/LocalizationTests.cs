using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Reporting;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class LocalizationTests
{
    private static readonly TextCatalog Dutch = TextCatalog.For("nl");

    [Fact]
    public void EnglishAndDutchAreAvailableEnglishFirst()
    {
        var languages = TextCatalog.Available();
        Assert.Equal("en", languages[0].Code);
        Assert.Contains(languages, l => l.Code == "nl" && l.Name == "Nederlands");
    }

    [Fact]
    public void UnknownLanguageFallsBackToEnglish() => Assert.Equal("en", TextCatalog.For("xx").Language.Code);

    [Fact]
    public void DutchUsesItsOwnNumberFormat()
    {
        var finding = Ctx.Single(
            new StorageCheck(new Fixed<StorageSnapshot>(null!)).Evaluate(new([new Volume("C:", "NTFS", 500_000_000_000, 7_000_000_000, true)], []), Ctx.User),
            "STORAGE-SPACE-001");
        Assert.Equal("The Windows drive is getting full (7.0 GB free)", finding.Title.ToString());
        Assert.Equal("De Windows-schijf raakt vol (7,0 GB vrij)", finding.Title.Render(Dutch));
    }

    [Fact]
    public void EveryDutchTranslationKeepsThePlaceholders()
    {
        foreach (var key in Dutch.Keys)
        {
            Assert.Equal(Message.Placeholders(key).Order(), Message.Placeholders(Dutch.Translate(key)).Order());
        }
    }

    [Fact]
    public void ReportJsonKeepsEnglishPlainStrings()
    {
        var finding = new Finding
        {
            Id = "TEST-TOPIC-001",
            Module = "test",
            Category = Category.Hardware,
            Severity = Severity.Warning,
            Title = new Message("Disk has {free:gb} free", ("free", 2_500_000_000L)),
            Evidence = [new Evidence("test", "evidence")],
        };
        Assert.Equal("Disk has 2.5 GB free", finding.Title.ToString());
        Assert.Empty(FindingValidator.Validate(finding));
    }

    [Fact]
    public void MissingTranslationFallsBackToEnglish() =>
        Assert.Equal("A sentence nobody translated", new Message("A sentence nobody translated").Render(Dutch));

    [Fact]
    public void TextReportCanBeDutch()
    {
        var report = new ScanReport
        {
            Product = "t",
            StartedAt = Ctx.Now,
            DurationSeconds = 1.5,
            System = new ScannedSystem("Windows 11", "25H2", false),
            Findings = [.. new PrivilegeCheck().RunAsync(Ctx.User, default).Result],
        };
        var lines = ReportText.Render(report, details: false, Dutch);
        Assert.Contains(lines, l => l.Text.Contains("zonder beheerdersrechten", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Text.StartsWith("Samenvatting:", StringComparison.Ordinal) && l.Text.Contains("1,5 s", StringComparison.Ordinal));
    }
}

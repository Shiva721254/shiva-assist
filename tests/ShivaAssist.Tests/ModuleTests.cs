using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using Xunit;

namespace ShivaAssist.Tests;

/// <summary>Rules per module, with the scenarios from "20 Windows problems in the Netherlands" where they apply.</summary>
public sealed class WindowsVersionTests
{
    private static Finding Version(string version, string edition, int build = 26100) =>
        Ctx.Single(new OsCheck(new Fixed<OsSnapshot>(new("Windows 10 Pro", edition, version, build, 1, TimeSpan.FromDays(1)))).Evaluate(
            new("Windows 10 Pro", edition, version, build, 1, TimeSpan.FromDays(1)), Ctx.User), "OS-VERSION-001");

    [Fact]
    public void Windows11_24H2HomeAndProAreOutOfSupport()
    {
        var finding = Version("24H2", "Professional");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("no longer receives security updates", finding.Title.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Windows11_24H2EnterpriseIsStillSupported() => Assert.Equal(Severity.Healthy, Version("24H2", "Enterprise").Severity);

    [Fact]
    public void Windows11_25H2IsSupported() => Assert.Equal(Severity.Healthy, Version("25H2", "Core", 26200).Severity);

    [Fact]
    public void Windows11_23H2EnterpriseEndedLastWeek() =>
        Assert.Equal(Severity.Warning, Version("23H2", "Enterprise", 22631).Severity); // ended 10 Nov 2026

    [Fact]
    public void UnknownNewerVersionIsTreatedAsSupportedWithMediumConfidence()
    {
        var finding = Version("27H2", "Professional", 30000);
        Assert.Equal(Severity.Healthy, finding.Severity);
        Assert.Equal(Confidence.Medium, finding.Confidence);
    }

    [Fact]
    public void Windows10IsReportedAsUnsupported()
    {
        var finding = Version("22H2", "Professional", 19045);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.StartsWith("Windows 10", finding.Title.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsServerIsNotJudgedByTheWindows11Table()
    {
        var finding = Version("24H2", "ServerDatacenter");
        Assert.Equal(Severity.Unknown, finding.Severity);
        Assert.Equal(UnknownReason.Unsupported, finding.UnknownReason);
    }

    [Fact]
    public void LongUptimeSuggestsARestart()
    {
        var findings = new OsCheck(new Fixed<OsSnapshot>(null!)).Evaluate(new("Windows 10 Pro", "Professional", "25H2", 26200, 1, TimeSpan.FromDays(21)), Ctx.User);
        Assert.Equal(Severity.Information, Ctx.Single(findings, "OS-UPTIME-001").Severity);
    }
}

public sealed class StorageTests
{
    private const long Gb = 1_000_000_000;

    private static IReadOnlyList<Finding> Scan(long free, IReadOnlyList<PhysicalDisk>? disks = null) =>
        new StorageCheck(new Fixed<StorageSnapshot>(null!)).Evaluate(
            new([new Volume("C:", "NTFS", 500 * Gb, free, true)], disks ?? [new PhysicalDisk("Samsung SSD", "SSD", "NVMe", 500 * Gb, DiskHealth.Healthy)]),
            Ctx.User);

    [Fact]
    public void ResearchScenario_7GbFreeOn500GbIsAWarning() => Assert.Equal(Severity.Warning, Ctx.Single(Scan(7 * Gb), "STORAGE-SPACE-001").Severity);

    [Fact]
    public void UnderTwoGigabytesIsCritical() => Assert.Equal(Severity.Critical, Ctx.Single(Scan((long)(1.5 * Gb)), "STORAGE-SPACE-001").Severity);

    [Fact]
    public void PlentyOfSpaceIsHealthy() => Assert.Equal(Severity.Healthy, Ctx.Single(Scan(120 * Gb), "STORAGE-SPACE-001").Severity);

    [Fact]
    public void FailingDiskIsCriticalAndAdvisesBackup()
    {
        var finding = Ctx.Single(Scan(120 * Gb, [new PhysicalDisk("Old HDD", "HDD", "SATA", 1000 * Gb, DiskHealth.Unhealthy)]), "STORAGE-HEALTH-001");
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("Back up", finding.Recommendation!.Summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableHealthIsUnknownNeverHealthy()
    {
        Assert.Equal(Severity.Unknown, Ctx.Single(Scan(120 * Gb, []), "STORAGE-HEALTH-001").Severity);
        Assert.Equal(Severity.Unknown, Ctx.Single(Scan(120 * Gb, [new PhysicalDisk("USB disk", "disk", "USB", 64 * Gb, DiskHealth.Unknown)]), "STORAGE-HEALTH-001").Severity);
    }
}

public sealed class PerformanceTests
{
    private const long Gb = 1_000_000_000;

    private static IReadOnlyList<Finding> Memory(long total, long available, long commitLimit = 0, long commitUsed = 0) =>
        new MemoryCheck(new Fixed<MemorySnapshot>(null!)).Evaluate(new(total, available, commitLimit, commitUsed), Ctx.User);

    [Fact]
    public void ResearchScenario_8GbWithHeavyPagingShowsMemoryPressure()
    {
        var findings = Memory((long)(7.8 * Gb), (long)(0.6 * Gb), 12 * Gb, (long)(11.4 * Gb));
        Assert.Equal(Severity.Warning, Ctx.Single(findings, "MEMORY-COMMIT-001").Severity);
        Assert.Equal(Severity.Healthy, Ctx.Single(findings, "MEMORY-CAPACITY-001").Severity);
    }

    [Fact]
    public void TooLittleMemoryForWindows11() => Assert.Equal(Severity.Warning, Ctx.Single(Memory((long)(3.5 * Gb), Gb), "MEMORY-CAPACITY-001").Severity);

    [Fact]
    public void SingleBusyMomentIsNotADefect()
    {
        var finding = Ctx.Single(new ProcessorCheck(new Fixed<ProcessorSnapshot>(null!)).Evaluate(new("Intel Core i5", 4, 8, [95, 30, 20]), Ctx.User), "CPU-LOAD-001");
        Assert.Equal(Severity.Healthy, finding.Severity);
    }

    [Fact]
    public void SustainedFullLoadIsAWarningWithMediumConfidence()
    {
        var finding = Ctx.Single(new ProcessorCheck(new Fixed<ProcessorSnapshot>(null!)).Evaluate(new("Intel Core i5", 4, 8, [97, 95, 99]), Ctx.User), "CPU-LOAD-001");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(Confidence.Medium, finding.Confidence);
    }

    [Theory]
    [InlineData(5, Severity.Healthy)]
    [InlineData(10, Severity.Information)]
    [InlineData(18, Severity.Warning)]
    public void StartupProgramThresholds(int count, Severity expected)
    {
        var names = Enumerable.Range(1, count).Select(i => $"App{i}").ToList();
        Assert.Equal(expected, Ctx.Single(new StartupCheck(new Fixed<StartupSnapshot>(null!)).Evaluate(new(names), Ctx.User), "PERF-STARTUP-001").Severity);
    }
}

public sealed class SecurityTests
{
    [Theory]
    [InlineData(397568, true, true)]   // 0x061100: Microsoft Defender on, definitions up to date
    [InlineData(393472, false, true)]  // 0x060100: switched off
    [InlineData(397584, true, false)]  // 0x061110: on, definitions out of date
    public void DecodesSecurityCenterProductState(int state, bool enabled, bool upToDate) =>
        Assert.Equal((enabled, upToDate), SecurityCheck.DecodeProductState(state));

    private static IReadOnlyList<Finding> Scan(IReadOnlyList<AntivirusProduct>? av, IReadOnlyList<FirewallProfile>? fw) =>
        new SecurityCheck(new Fixed<SecuritySnapshot>(null!)).Evaluate(new(av, fw), Ctx.User);

    private static readonly FirewallProfile[] AllOn = [new("Domain", true), new("Private", true), new("Public", true)];

    [Fact]
    public void NoActiveAntivirusIsCritical() => Assert.Equal(Severity.Critical, Ctx.Single(Scan([new("Microsoft Defender Antivirus", false, true)], AllOn), "SECURITY-AV-001").Severity);

    [Fact]
    public void OutdatedDefinitionsAreAWarning() => Assert.Equal(Severity.Warning, Ctx.Single(Scan([new("Microsoft Defender Antivirus", true, false)], AllOn), "SECURITY-AV-001").Severity);

    [Fact]
    public void FirewallOffOnPublicNetworksIsAWarning()
    {
        var finding = Ctx.Single(Scan([new("Microsoft Defender Antivirus", true, true)], [new("Domain", true), new("Private", true), new("Public", false)]), "SECURITY-FIREWALL-001");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("Public", finding.Title.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySecurityCenterFallsBackToDefender()
    {
        Assert.Equal(Severity.Healthy, Ctx.Single(new SecurityCheck(new Fixed<SecuritySnapshot>(null!)).Evaluate(new([], AllOn, new DefenderStatus(true, true, 1)), Ctx.User), "SECURITY-AV-001").Severity);
        Assert.Equal(Severity.Critical, Ctx.Single(new SecurityCheck(new Fixed<SecuritySnapshot>(null!)).Evaluate(new([], AllOn, new DefenderStatus(true, false, 1)), Ctx.User), "SECURITY-AV-001").Severity);
        Assert.Equal(Severity.Warning, Ctx.Single(new SecurityCheck(new Fixed<SecuritySnapshot>(null!)).Evaluate(new([], AllOn, new DefenderStatus(true, true, 20)), Ctx.User), "SECURITY-AV-001").Severity);
    }

    [Fact]
    public void NoEvidenceOfAntivirusIsUnknownNotCritical() =>
        Assert.Equal(Severity.Unknown, Ctx.Single(Scan([], AllOn), "SECURITY-AV-001").Severity);

    [Fact]
    public void NoSecurityCenterIsUnknown() => Assert.Equal(Severity.Unknown, Ctx.Single(Scan(null, AllOn), "SECURITY-AV-001").Severity);
}

public sealed class NetworkTests
{
    private static Finding Scan(LayerResult adapter, LayerResult ip, LayerResult gateway, LayerResult dns, LayerResult internet, bool captive = false) =>
        Assert.Single(new NetworkCheck(new Fixed<NetworkSnapshot>(null!)).Evaluate(new(["Wi-Fi (Wi-Fi)"], adapter, ip, gateway, dns, internet, captive, false), Ctx.User));

    [Fact]
    public void WorkingConnectionIsHealthy() =>
        Assert.Equal(Severity.Healthy, Scan(LayerResult.Passed, LayerResult.Passed, LayerResult.Passed, LayerResult.Passed, LayerResult.Passed).Severity);

    [Fact]
    public void ResearchScenario_DnsFailsWhileInternetWorks()
    {
        var finding = Scan(LayerResult.Passed, LayerResult.Passed, LayerResult.Passed, LayerResult.Failed, LayerResult.Passed);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("DNS", finding.Title.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoAddressFromRouter()
    {
        var finding = Scan(LayerResult.Passed, LayerResult.Failed, LayerResult.NotTested, LayerResult.NotTested, LayerResult.NotTested);
        Assert.Contains("valid network address", finding.Title.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CaptivePortalIsInformationNotAFault() =>
        Assert.Equal(Severity.Information, Scan(LayerResult.Passed, LayerResult.Passed, LayerResult.Passed, LayerResult.Passed, LayerResult.Passed, captive: true).Severity);

    [Fact]
    public void BlockedGatewayPingAloneLowersConfidence()
    {
        var finding = Scan(LayerResult.Passed, LayerResult.Passed, LayerResult.Failed, LayerResult.Failed, LayerResult.Failed);
        Assert.Equal(Confidence.Medium, finding.Confidence);
    }
}

public sealed class DeviceAndStabilityTests
{
    [Fact]
    public void MissingDriverIsAWarningButDisabledAndUnpluggedAreNot()
    {
        var findings = new DeviceCheck(new Fixed<DeviceSnapshot>(null!)).Evaluate(new(180,
        [
            new ProblemDevice("Integrated Webcam", "Camera", 28),
            new ProblemDevice("Bluetooth Adapter", "Bluetooth", 22),
            new ProblemDevice("USB Stick", "DiskDrive", 45),
        ]), Ctx.User);

        var problem = Ctx.Single(findings, "DEVICE-PROBLEM-001");
        Assert.Equal(Severity.Warning, problem.Severity);
        Assert.Contains("Integrated Webcam", problem.Title.ToString(), StringComparison.Ordinal);
        Assert.Equal(Severity.Information, Ctx.Single(findings, "DEVICE-DISABLED-001").Severity);
        Assert.DoesNotContain(problem.Evidence, e => e.Observation.Contains("USB Stick", StringComparison.Ordinal));
    }

    private static IReadOnlyList<Finding> Stability(int shutdowns, int bluescreens, int whea, Dictionary<string, int> crashes) =>
        new StabilityCheck(new Fixed<StabilitySnapshot>(null!)).Evaluate(new(14, shutdowns, bluescreens, whea, crashes), Ctx.User);

    [Fact]
    public void ResearchScenario_Event41WithoutBugcheckIsAnObservationNotACause()
    {
        var findings = Stability(1, 0, 0, []);
        Assert.Equal(Severity.Information, Ctx.Single(findings, "SYSTEM-SHUTDOWN-001").Severity);
        Assert.Equal(Severity.Healthy, Ctx.Single(findings, "SYSTEM-BUGCHECK-001").Severity);
    }

    [Fact]
    public void RepeatedBlueScreensAreCritical() => Assert.Equal(Severity.Critical, Ctx.Single(Stability(3, 3, 0, []), "SYSTEM-BUGCHECK-001").Severity);

    [Fact]
    public void CrashingProgramIsNamed()
    {
        var finding = Ctx.Single(Stability(0, 0, 0, new() { ["Accounting.exe"] = 9, ["chrome.exe"] = 2 }), "APP-CRASH-001");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains(finding.Evidence, e => e.Observation.StartsWith("Accounting.exe: 9", StringComparison.Ordinal));
    }

    [Fact]
    public void BatteryAtHalfCapacityIsAWarningAndDesktopsGetNoFinding()
    {
        var battery = new BatteryCheck(new Fixed<BatterySnapshot>(null!));
        Assert.Equal(Severity.Warning, Assert.Single(battery.Evaluate(new(true, 60_000, 28_000, 400), Ctx.User)).Severity);
        Assert.Equal(Severity.Information, Assert.Single(battery.Evaluate(new(true, 60_000, 32_000, 400), Ctx.User)).Severity); // research example: 53%
        Assert.Empty(battery.Evaluate(new(false, null, null, null), Ctx.User));
        Assert.Equal(Severity.Unknown, Assert.Single(battery.Evaluate(new(true, null, 30_000, null), Ctx.User)).Severity);
    }
}

public sealed class ParserTests
{
    [Theory]
    [InlineData("Deployment Image Servicing and Management tool\r\nNo component store corruption detected.\r\nThe operation completed successfully.", ComponentStoreState.NoCorruption)]
    [InlineData("The component store is repairable.\r\nThe operation completed successfully.", ComponentStoreState.Repairable)]
    [InlineData("The component store cannot be repaired.", ComponentStoreState.NotRepairable)]
    [InlineData("Error: 87\r\nThe parameter is incorrect.", ComponentStoreState.Unrecognised)]
    public void ClassifiesDismOutput(string output, ComponentStoreState expected) => Assert.Equal(expected, DismOutput.Classify(output).State);

    [Fact]
    public void RepairableComponentStoreIsCritical() =>
        Assert.Equal(Severity.Critical, Assert.Single(new IntegrityCheck(new Fixed<IntegritySnapshot>(null!)).Evaluate(DismOutput.Classify("The component store is repairable."), Ctx.Admin)).Severity);

    [Fact]
    public void ParsesWevtutilEventsAndProgramNames()
    {
        const string Xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>1000</EventID></System><EventData><Data>Accounting.exe</Data><Data>1.2.3</Data></EventData></Event>
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>1000</EventID></System><EventData><Data>chrome.exe</Data></EventData></Event>
            """;
        var events = EventXml.ParseEvents(Xml);
        Assert.Equal(2, events.Count);
        Assert.Equal("Accounting.exe", EventXml.AppName(events[0]));
        Assert.Empty(EventXml.ParseEvents("  "));
    }
}

using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Reporting;
using ShivaAssist.Core.Scanning;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class FindingContractTests
{
    private static Finding Valid => new()
    {
        Id = "STORAGE-SPACE-001",
        Module = "storage",
        Category = Category.Hardware,
        Severity = Severity.Warning,
        Title = "The Windows drive is getting full",
        Evidence = [new Evidence("drive", "C: 7 GB free")],
    };

    [Fact]
    public void ValidFindingPasses() => Assert.Empty(FindingValidator.Validate(Valid));

    [Theory]
    [InlineData("storage-space-001")]
    [InlineData("STORAGE-SPACE-1")]
    [InlineData("STORAGE_SPACE_001")]
    public void IdMustFollowModuleTopicNumber(string id) => Assert.NotEmpty(FindingValidator.Validate(Valid with { Id = id }));

    [Fact]
    public void EvidenceIsRequiredUnlessUnknown() => Assert.NotEmpty(FindingValidator.Validate(Valid with { Evidence = [] }));

    [Fact]
    public void UnknownNeedsAReason()
    {
        Assert.NotEmpty(FindingValidator.Validate(Valid with { Severity = Severity.Unknown, Evidence = [] }));
        Assert.Empty(FindingValidator.Validate(Valid with { Severity = Severity.Unknown, Evidence = [], UnknownReason = UnknownReason.Timeout }));
    }

    [Fact]
    public void ReasonOnlyWithUnknown() => Assert.NotEmpty(FindingValidator.Validate(Valid with { UnknownReason = UnknownReason.Error }));

    [Fact]
    public void NoRepairOperationsBeforeTheCatalogueExists() =>
        Assert.NotEmpty(FindingValidator.Validate(Valid with { Recommendation = new("Clean up") { Operations = ["WIN.DISK.CLEANUP"] } }));
}

public sealed class ScanRunnerTests
{
    private sealed class SlowCheck(bool honourCancellation) : ICheck
    {
        public string Module => "slow";

        public Category Category => Category.Performance;

        public string FailureId => "SLOW-CHECK-000";

        public TimeSpan TimeLimit => TimeSpan.FromMilliseconds(200);

        public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), honourCancellation ? cancellationToken : CancellationToken.None);
            return [];
        }
    }

    private sealed class InvalidCheck : ICheck
    {
        public string Module => "broken";

        public Category Category => Category.Hardware;

        public string FailureId => "BROKEN-CHECK-000";

        public TimeSpan TimeLimit => TimeSpan.FromSeconds(1);

        public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Finding>>([new Finding { Id = "bad id", Module = "broken", Category = Category.Hardware, Severity = Severity.Critical, Title = "No evidence" }]);
    }

    private static readonly ScanEnvironment Environment = new("Shiva Assist test", "Windows 11 Pro", "25H2 (build 26200.1)", IsElevated: false);

    private static Task<ScanReport> Run(params ICheck[] checks) => new ScanRunner(checks).RunAsync(Environment);

    [Fact]
    public async Task ThrowingCheckBecomesUnknownWithReason()
    {
        var report = await Run(new StorageCheck(new Failing<StorageSnapshot>(new InvalidOperationException("boom"))));
        var finding = Assert.Single(report.Findings);
        Assert.Equal(Severity.Unknown, finding.Severity);
        Assert.Equal(UnknownReason.Error, finding.UnknownReason);
        Assert.Equal("STORAGE-CHECK-000", finding.Id);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), UnknownReason.AccessDenied)]
    [InlineData(typeof(PlatformNotSupportedException), UnknownReason.Unsupported)]
    [InlineData(typeof(RequiresAdminException), UnknownReason.RequiresAdmin)]
    public async Task FailuresMapToContractReasons(Type exception, UnknownReason expected)
    {
        var report = await Run(new SecurityCheck(new Failing<SecuritySnapshot>((Exception)Activator.CreateInstance(exception)!)));
        Assert.Equal(expected, Assert.Single(report.Findings).UnknownReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckPastItsTimeLimitBecomesTimeout(bool honoursCancellation)
    {
        var started = DateTime.UtcNow;
        var report = await Run(new SlowCheck(honoursCancellation));
        Assert.Equal(UnknownReason.Timeout, Assert.Single(report.Findings).UnknownReason);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "the scan must not wait for a hung check");
    }

    [Fact]
    public async Task InvalidFindingIsReplacedByUnknown()
    {
        var finding = Assert.Single((await Run(new InvalidCheck())).Findings);
        Assert.Equal("BROKEN-CHECK-000", finding.Id);
        Assert.Equal(UnknownReason.Error, finding.UnknownReason);
    }

    [Fact]
    public async Task UnprivilegedRunIsReported()
    {
        var report = await Run(new PrivilegeCheck(), new IntegrityCheck(new Failing<IntegritySnapshot>(new RequiresAdminException())));
        Assert.Equal(Severity.Information, Ctx.Single(report.Findings, "SCAN-PRIVILEGE-001").Severity);
        Assert.Equal(UnknownReason.RequiresAdmin, Ctx.Single(report.Findings, "WIN-INTEGRITY-001").UnknownReason);
    }

    [Fact]
    public async Task TwoScansOfTheSameSystemGiveTheSameFindings()
    {
        ICheck[] Checks() =>
        [
            new StorageCheck(new Fixed<StorageSnapshot>(new([new Volume("C:", "NTFS", 500_000_000_000, 7_000_000_000, true)], []))),
            new StartupCheck(new Fixed<StartupSnapshot>(new(["OneDrive", "Teams", "Spotify"]))),
            new PrivilegeCheck(),
        ];

        var first = await new ScanRunner(Checks()).RunAsync(Environment);
        var second = await new ScanRunner(Checks()).RunAsync(Environment);
        Assert.Equal(first.Findings.Select(f => (f.Id, f.Severity)), second.Findings.Select(f => (f.Id, f.Severity)));
    }

    [Fact]
    public async Task MostSeriousFindingsComeFirst()
    {
        var report = await Run(
            new PrivilegeCheck(),
            new StorageCheck(new Fixed<StorageSnapshot>(new([new Volume("C:", "NTFS", 500_000_000_000, 1_000_000_000, true)], null))));
        Assert.Equal(Severity.Critical, report.Findings[0].Severity);
    }
}

public sealed class ReportJsonTests
{
    [Fact]
    public async Task ReportUsesContractSpelling()
    {
        var report = await new ScanRunner([new PrivilegeCheck(), new IntegrityCheck(new Failing<IntegritySnapshot>(new RequiresAdminException()))])
            .RunAsync(new ScanEnvironment("Shiva Assist test", "Windows 11", "25H2", false));
        var json = ReportJson.Serialize(report);

        Assert.Contains("\"schema\": \"shiva.assist.report/1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"schema\": \"shiva.assist.finding/1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unknownReason\": \"requires-admin\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category\": \"windows-system\"", json, StringComparison.Ordinal);
        Assert.Contains("\"severity\": \"information\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportRoundTrips()
    {
        var report = await new ScanRunner([new PrivilegeCheck()]).RunAsync(new ScanEnvironment("Shiva Assist test", "Windows 11", "25H2", true));
        var back = ReportJson.Deserialize(ReportJson.Serialize(report));
        Assert.NotNull(back);
        Assert.Equal(report.Findings.Select(f => f.Id), back!.Findings.Select(f => f.Id));
        Assert.Equal(Severity.Healthy, back.Findings[0].Severity);
    }

    [Fact]
    public async Task HumanReportShowsAdviceOnlyForProblems()
    {
        var report = await new ScanRunner([new PrivilegeCheck()]).RunAsync(new ScanEnvironment("t", "Windows 11", "25H2", false));
        var lines = ReportText.Render(report, details: false);
        Assert.Contains(lines, l => l.Text.Contains("Run as administrator", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Heading && l.Text.StartsWith("Summary:", StringComparison.Ordinal));
    }
}

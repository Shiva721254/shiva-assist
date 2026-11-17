using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Scanning;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class RepairTests
{
    private const long Gb = 1_000_000_000;

    private static Finding Space(long free) => Ctx.Single(
        new StorageCheck(new Fixed<StorageSnapshot>(null!)).Evaluate(new([new Volume("C:", "NTFS", 500 * Gb, free, true)], []), Ctx.User),
        "STORAGE-SPACE-001");

    [Fact]
    public void FullDriveOffersTheThreeCleanups()
    {
        var ids = RepairCatalogue.For(Space(3 * Gb)).Select(o => o.Id).ToList();
        Assert.Equal([RepairCatalogue.EmptyRecycleBin, RepairCatalogue.CleanTempFiles, RepairCatalogue.CleanComponentStore], ids);
    }

    [Fact]
    public void HealthyFindingsGetNoRepairs() => Assert.Empty(RepairCatalogue.For(Space(200 * Gb)));

    [Fact]
    public void StartupFindingOffersSwitchingOff()
    {
        var finding = Ctx.Single(new StartupCheck(new Fixed<StartupSnapshot>(null!)).Evaluate(new([.. Enumerable.Range(1, 12).Select(i => $"App{i}")]), Ctx.User), "PERF-STARTUP-001");
        var operation = Assert.Single(RepairCatalogue.For(finding));
        Assert.True(operation.NeedsSelection);
        Assert.Equal(RiskClass.Low, operation.Risk);
    }

    [Fact]
    public void ReportListsCatalogueOperationsAndTheyValidate()
    {
        var finding = ScanRunner.WithRepairs(Space(3 * Gb));
        Assert.Equal(3, finding.Recommendation!.Operations.Count);
        Assert.Empty(FindingValidator.Validate(finding));
        Assert.NotEmpty(FindingValidator.Validate(finding with { Recommendation = finding.Recommendation with { Operations = ["FORMAT.C.DRIVE"] } }));
    }

    [Fact]
    public void EveryCatalogueOperationIsComplete()
    {
        Assert.All(RepairCatalogue.All, o =>
        {
            Assert.Matches("^[A-Z]+(\\.[A-Z]+)+$", o.Id);
            Assert.False(string.IsNullOrWhiteSpace(o.Description.Template));
            Assert.True(o.Minutes.Min <= o.Minutes.Max);
        });
        Assert.Equal(RepairCatalogue.All.Count, RepairCatalogue.All.Select(o => o.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(3, 200, RepairVerdict.Fixed)]        // critical → healthy
    [InlineData(3, 12, RepairVerdict.Unresolved)]    // critical → still a warning
    [InlineData(3, 3, RepairVerdict.Unresolved)]     // nothing changed
    public void VerdictComparesBeforeAndAfter(long beforeGb, long afterGb, RepairVerdict expected)
    {
        var done = new RepairResult { JobId = "j", OperationId = RepairCatalogue.CleanTempFiles, Completed = true };
        Assert.Equal(expected, RepairVerification.Judge(Space(beforeGb * Gb), Space(afterGb * Gb), done));
    }

    [Fact]
    public void FailedOrUncheckedRepairIsInconclusive()
    {
        var failed = new RepairResult { JobId = "j", OperationId = RepairCatalogue.CleanTempFiles, Completed = false };
        Assert.Equal(RepairVerdict.Inconclusive, RepairVerification.Judge(Space(3 * Gb), Space(200 * Gb), failed));
        var done = failed with { Completed = true };
        Assert.Equal(RepairVerdict.Inconclusive, RepairVerification.Judge(Space(3 * Gb), null, done));
    }

    [Fact]
    public void JobsRoundTripThroughJson()
    {
        var job = new RepairJob { JobId = "abc", OperationId = RepairCatalogue.DisableStartupPrograms, ApprovedAt = Ctx.Now, Items = ["HKCU-Run|OneDrive"] };
        var back = RepairJson.Deserialize<RepairJob>(RepairJson.Serialize(job));
        Assert.NotNull(back);
        Assert.Equal(job.Items, back!.Items);
        Assert.Equal(RepairJob.SchemaId, back.Schema);
    }

    [Fact]
    public void StartupEntryReferenceIdentifiesItsLocation() =>
        Assert.Equal("UserFolder|Spotify.lnk", new StartupEntry("Spotify", "UserFolder", "Spotify.lnk").Reference);
}

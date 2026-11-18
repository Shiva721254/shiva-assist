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
        Assert.Equal([RepairCatalogue.EmptyRecycleBin, RepairCatalogue.CleanTempFiles, RepairCatalogue.CleanBrowserCache, RepairCatalogue.CleanUpdateCache, RepairCatalogue.CleanComponentStore], ids);
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
        Assert.Equal(5, finding.Recommendation!.Operations.Count);
        Assert.Empty(FindingValidator.Validate(finding));
        Assert.NotEmpty(FindingValidator.Validate(finding with { Recommendation = finding.Recommendation with { Operations = ["FORMAT.C.DRIVE"] } }));
    }

    private static Finding Network(LayerResult ip, LayerResult gateway, LayerResult dns, LayerResult internet, bool portal = false) => Ctx.Single(
        new NetworkCheck(new Fixed<NetworkSnapshot>(null!)).Evaluate(new(["Wi-Fi"], LayerResult.Passed, ip, gateway, dns, internet, portal, false), Ctx.User),
        "NETWORK-CONNECT-001");

    [Fact]
    public void NetworkRepairsFollowTheFailingLayer()
    {
        var pass = LayerResult.Passed;
        var fail = LayerResult.Failed;
        var skip = LayerResult.NotTested;
        Assert.Equal([RepairCatalogue.FlushDns], RepairCatalogue.For(Network(pass, pass, fail, pass)).Select(o => o.Id));
        Assert.Equal([RepairCatalogue.RenewAddress], RepairCatalogue.For(Network(fail, skip, skip, skip)).Select(o => o.Id));
        Assert.Equal([RepairCatalogue.RenewAddress, RepairCatalogue.FlushDns], RepairCatalogue.For(Network(pass, fail, fail, fail)).Select(o => o.Id));
        Assert.Empty(RepairCatalogue.For(Network(pass, pass, pass, fail, portal: true)));
        Assert.Empty(RepairCatalogue.For(Network(pass, pass, pass, pass)));
    }

    [Fact]
    public void OutdatedDefenderOffersAnUpdateButDisabledDefenderDoesNot()
    {
        Finding Av(DefenderStatus status) => Ctx.Single(
            new SecurityCheck(new Fixed<SecuritySnapshot>(null!)).Evaluate(new([], [new("Domain", true), new("Private", true), new("Public", true)], status), Ctx.User), "SECURITY-AV-001");
        Assert.Equal([RepairCatalogue.UpdateDefender], RepairCatalogue.For(Av(new DefenderStatus(true, true, 20))).Select(o => o.Id));
        Assert.Empty(RepairCatalogue.For(Av(new DefenderStatus(true, false, 1))));
    }

    [Fact]
    public void UpdateRepairsAreVerifiedLater()
    {
        var findings = new UpdateCheck(new Fixed<UpdateSnapshot>(null!)).Evaluate(new(true, ["Component Based Servicing"], DateOnly.FromDateTime(Ctx.Now.UtcDateTime.AddDays(-120)), "KB1"), Ctx.User);
        var restart = Assert.Single(RepairCatalogue.For(Ctx.Single(findings, "UPDATE-RESTART-001")));
        var reset = Assert.Single(RepairCatalogue.For(Ctx.Single(findings, "UPDATE-RECENT-001")));
        Assert.Equal(RepairCatalogue.RestartNow, restart.Id);
        Assert.True(restart.VerifyLater && restart.RestartRequired && !restart.NeedsRestorePoint);
        Assert.Equal(RepairCatalogue.ResetUpdateComponents, reset.Id);
        Assert.True(reset.VerifyLater && reset.NeedsRestorePoint && reset.RequiresAdmin);
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
    [InlineData(3, 200, 0, RepairVerdict.Fixed)]     // critical → healthy
    [InlineData(3, 12, 0, RepairVerdict.Improved)]   // critical → still a warning, but better
    [InlineData(12, 14, 2, RepairVerdict.Improved)]  // still a warning, but space was freed
    [InlineData(3, 3, 0, RepairVerdict.Unresolved)]  // nothing changed
    public void VerdictComparesBeforeAndAfter(long beforeGb, long afterGb, long freedGb, RepairVerdict expected)
    {
        var done = new RepairResult { JobId = "j", OperationId = RepairCatalogue.CleanTempFiles, Completed = true, BytesFreed = freedGb * Gb };
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
    public void DamagedSystemFilesOfferTheMediumRepairOnlyWhenRepairable()
    {
        var check = new IntegrityCheck(new Fixed<IntegritySnapshot>(null!));
        var repairable = Assert.Single(check.Evaluate(DismOutput.Classify("The component store is repairable."), Ctx.Admin));
        var operation = Assert.Single(RepairCatalogue.For(repairable));
        Assert.Equal(RepairCatalogue.RepairSystemFiles, operation.Id);
        Assert.True(operation.NeedsRestorePoint);
        Assert.True(operation.RequiresAdmin);

        var lost = Assert.Single(check.Evaluate(DismOutput.Classify("The component store cannot be repaired."), Ctx.Admin));
        Assert.Empty(RepairCatalogue.For(lost));
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

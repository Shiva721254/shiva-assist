using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class ClockAndWearTests
{
    private const long Gb = 1_000_000_000;

    private static Finding Clock(TimeServiceState state, double? offset) =>
        Ctx.Single(new ClockCheck(new Fixed<TimeSnapshot>(null!)).Evaluate(new TimeSnapshot(state, offset, "time.windows.com"), Ctx.Admin), ClockCheck.FindingId);

    [Theory]
    [InlineData(400.0)]
    [InlineData(-3600.0)]
    public void ClockFarOffIsAWarningWithTheSyncRepair(double offset)
    {
        var finding = Clock(TimeServiceState.Running, offset);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(RepairCatalogue.SyncClock, Assert.Single(RepairCatalogue.For(finding)).Id);
    }

    [Fact]
    public void DisabledTimeServiceIsRepairableEvenWhenTheClockIsStillRight()
    {
        var finding = Clock(TimeServiceState.Disabled, 3.2);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.NotEmpty(RepairCatalogue.For(finding));
    }

    [Fact]
    public void SmallDriftIsHealthyAndNoAnswerIsUnknown()
    {
        Assert.Equal(Severity.Healthy, Clock(TimeServiceState.Running, 0.8).Severity);
        Assert.Equal(Severity.Unknown, Clock(TimeServiceState.Running, null).Severity);
        Assert.Empty(RepairCatalogue.For(Clock(TimeServiceState.Running, null)));
    }

    private static Finding Wear(DiskReliability? reliability, bool admin = true) => Ctx.Single(
        new StorageCheck(new Fixed<StorageSnapshot>(null!)).Evaluate(
            new([new Volume("C:", "NTFS", 500 * Gb, 200 * Gb, true)], [new PhysicalDisk("Samsung SSD", "SSD", "NVMe", 500 * Gb, DiskHealth.Healthy, reliability)]),
            admin ? Ctx.Admin : Ctx.User),
        "STORAGE-WEAR-001");

    [Fact]
    public void UncorrectableErrorsAndWornOutSsdsAreCriticalWithoutAnyRepair()
    {
        var errors = Wear(new DiskReliability(10, 40, 12000, 3));
        Assert.Equal(Severity.Critical, errors.Severity);
        Assert.Empty(RepairCatalogue.For(errors)); // a failing disk is never "repaired" by cleanups
        Assert.Equal(Severity.Critical, Wear(new DiskReliability(95, 40, 30000, 0)).Severity);
        Assert.Equal(Severity.Warning, Wear(new DiskReliability(75, 40, 30000, 0)).Severity);
        Assert.Equal(Severity.Warning, Wear(new DiskReliability(5, 74, 100, 0)).Severity);
    }

    [Fact]
    public void NormalCountersAreHealthyAndMissingCountersStayUnknown()
    {
        Assert.Equal(Severity.Healthy, Wear(new DiskReliability(3, 35, 1500, 0)).Severity);
        var notReported = Wear(null);
        Assert.Equal(Severity.Unknown, notReported.Severity);
        Assert.Equal(UnknownReason.RequiresAdmin, Wear(null, admin: false).UnknownReason);
        Assert.Equal(UnknownReason.Unsupported, notReported.UnknownReason);
    }
}

public sealed class CleanupAreaTests
{
    [Fact]
    public void EveryCleanupAreaIsALowRiskCatalogueRepairWithoutSelection()
    {
        Assert.Equal(5, ShivaAssist.Core.Cleanup.CleanupAreas.All.Count);
        Assert.All(ShivaAssist.Core.Cleanup.CleanupAreas.All, area =>
        {
            var operation = RepairCatalogue.Find(area.OperationId);
            Assert.NotNull(operation);
            Assert.Equal(RiskClass.Low, operation!.Risk);
            Assert.False(operation.NeedsSelection || operation.RestartRequired || operation.IsUndo);
        });
    }

    [Fact]
    public void TotalIgnoresAreasThatCouldNotBeMeasured() => Assert.Equal(300, ShivaAssist.Core.Cleanup.CleanupAreas.Total(
        [new("a", 100, 1), new("b", null, 0), new("c", 200, 3)]));
}

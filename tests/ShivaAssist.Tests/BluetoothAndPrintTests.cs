using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class BluetoothAndPrintTests
{
    [Theory]
    [InlineData(true, ServiceRunState.Stopped, "manual", Severity.Warning, true)]
    [InlineData(true, ServiceRunState.Stopped, "automatic", Severity.Warning, true)]
    [InlineData(true, ServiceRunState.Running, "manual", Severity.Healthy, false)]
    [InlineData(true, ServiceRunState.Stopped, "disabled", Severity.Information, false)]
    [InlineData(true, ServiceRunState.Missing, "unknown", Severity.Unknown, false)]
    [InlineData(false, ServiceRunState.Stopped, "manual", Severity.Healthy, false)]
    [InlineData(null, ServiceRunState.Stopped, "manual", Severity.Unknown, false)]
    public void BluetoothServiceRulesAndRepairEligibility(bool? adapter, ServiceRunState state, string start, Severity expected, bool offersRepair)
    {
        var snapshot = new BluetoothSnapshot(adapter, new ServiceInfo("bthserv", state, start));
        var finding = Ctx.Single(new BluetoothCheck(new Fixed<BluetoothSnapshot>(snapshot)).Evaluate(snapshot, Ctx.User), BluetoothCheck.FindingId);
        Assert.Equal(expected, finding.Severity);
        Assert.Equal(offersRepair, RepairCatalogue.For(finding).Any(o => o.Id == RepairCatalogue.RepairBluetoothService));
    }

    [Fact]
    public void PausedOrOfflinePrinterOffersQueueResumeAndVerifiesFixed()
    {
        var blockedSnapshot = new PrintSnapshot(SpoolerState.Running, "automatic", 2, 0, 0, ["HP LaserJet: paused, work offline"]);
        var blockedFindings = new PrintCheck(new Fixed<PrintSnapshot>(blockedSnapshot)).Evaluate(blockedSnapshot, Ctx.Admin);
        var queue = Ctx.Single(blockedFindings, PrintCheck.QueueFindingId);
        Assert.Equal(Severity.Warning, queue.Severity);

        var op = Assert.Single(RepairCatalogue.For(queue));
        Assert.Equal(RepairCatalogue.ResumePrinterQueues, op.Id);
        Assert.Equal(RiskClass.Low, op.Risk);
        Assert.True(op.RequiresAdmin);
        Assert.False(op.NeedsSelection || op.RestartRequired || op.VerifyLater || op.IsUndo);

        var healthySnapshot = new PrintSnapshot(SpoolerState.Running, "automatic", 2, 0, 0, []);
        var healthyQueue = Ctx.Single(new PrintCheck(new Fixed<PrintSnapshot>(healthySnapshot)).Evaluate(healthySnapshot, Ctx.Admin), PrintCheck.QueueFindingId);
        Assert.Equal(Severity.Healthy, healthyQueue.Severity);
        Assert.Empty(RepairCatalogue.For(healthyQueue));

        var done = new RepairResult { JobId = "pq1", OperationId = RepairCatalogue.ResumePrinterQueues, Completed = true };
        Assert.Equal(RepairVerdict.Fixed, RepairVerification.Judge(queue, healthyQueue, done));
    }
}

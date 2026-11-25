using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class ProxyAndAdapterTests
{
    private static Finding CheckProxy(bool enabled, bool reachable, bool loopback) => Ctx.Single(
        new ProxyCheck(new Fixed<ProxySnapshot>(null!)).Evaluate(new ProxySnapshot(enabled, reachable, loopback), Ctx.User),
        ProxyCheck.FindingId);

    [Fact]
    public void BrokenProxyOffersReversibleResetAndWorkingProxyDoesNot()
    {
        var broken = CheckProxy(enabled: true, reachable: false, loopback: true);
        Assert.Equal(Severity.Warning, broken.Severity);
        var op = Assert.Single(RepairCatalogue.For(broken));
        Assert.Equal(RepairCatalogue.ResetProxy, op.Id);
        Assert.Equal(RiskClass.Low, op.Risk);
        Assert.False(op.RequiresAdmin || op.NeedsSelection || op.RestartRequired);

        var undo = RepairCatalogue.UndoFor(RepairCatalogue.ResetProxy);
        Assert.NotNull(undo);
        Assert.Equal(RepairCatalogue.RestoreProxy, undo!.Id);
        Assert.True(undo.IsUndo);

        var working = CheckProxy(enabled: true, reachable: true, loopback: false);
        Assert.Equal(Severity.Information, working.Severity);
        Assert.Empty(RepairCatalogue.For(working));

        var off = CheckProxy(enabled: false, reachable: true, loopback: false);
        Assert.Equal(Severity.Healthy, off.Severity);
        Assert.Empty(RepairCatalogue.For(off));

        var done = new RepairResult { JobId = "px1", OperationId = RepairCatalogue.ResetProxy, Completed = true, Rollback = ["ProxyEnable=1"] };
        Assert.Equal(RepairVerdict.Fixed, RepairVerification.Judge(broken, off, done));
    }
}

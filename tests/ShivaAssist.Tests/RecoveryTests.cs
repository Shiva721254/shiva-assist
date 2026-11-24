using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class RecoveryTests
{
    private static IReadOnlyList<Finding> Scan(bool? protection, bool policy, BitLockerState bitLocker, bool needsAdmin = false) =>
        new RecoveryCheck(new Fixed<RecoverySnapshot>(null!)).Evaluate(new RecoverySnapshot(protection, policy, bitLocker, needsAdmin), Ctx.Admin);

    [Fact]
    public void DisabledSystemProtectionOffersAutoEnableUnlessPolicyBlocksIt()
    {
        var off = Ctx.Single(Scan(false, false, BitLockerState.Off), RecoveryCheck.ProtectionId);
        Assert.Equal(Severity.Warning, off.Severity);
        var op = Assert.Single(RepairCatalogue.For(off));
        Assert.Equal(RepairCatalogue.EnableSystemProtection, op.Id);
        Assert.Equal(RiskClass.Low, op.Risk);
        Assert.True(op.RequiresAdmin);
        Assert.False(op.NeedsSelection || op.RestartRequired);

        var byPolicy = Ctx.Single(Scan(false, true, BitLockerState.Off), RecoveryCheck.ProtectionId);
        Assert.Equal(Severity.Information, byPolicy.Severity);
        Assert.Empty(RepairCatalogue.For(byPolicy));

        var on = Ctx.Single(Scan(true, false, BitLockerState.Off), RecoveryCheck.ProtectionId);
        Assert.Equal(Severity.Healthy, on.Severity);
        Assert.Empty(RepairCatalogue.For(on));

        var unknown = Ctx.Single(Scan(null, false, BitLockerState.Off), RecoveryCheck.ProtectionId);
        Assert.Equal(Severity.Unknown, unknown.Severity);
        Assert.Empty(RepairCatalogue.For(unknown));

        var done = new RepairResult { JobId = "sr1", OperationId = RepairCatalogue.EnableSystemProtection, Completed = true };
        Assert.Equal(RepairVerdict.Fixed, RepairVerification.Judge(off, on, done));
    }

    [Fact]
    public void BitLockerRemainsReadOnlyAndReportsUnknownWhenNonAdmin()
    {
        var on = Ctx.Single(Scan(true, false, BitLockerState.On), RecoveryCheck.BitLockerId);
        Assert.Equal(Severity.Information, on.Severity);
        Assert.Empty(RepairCatalogue.For(on));

        var needsAdmin = Ctx.Single(Scan(true, false, BitLockerState.Unknown, needsAdmin: true), RecoveryCheck.BitLockerId);
        Assert.Equal(Severity.Unknown, needsAdmin.Severity);
        Assert.Equal(UnknownReason.RequiresAdmin, needsAdmin.UnknownReason);
        Assert.Empty(RepairCatalogue.For(needsAdmin));
    }
}

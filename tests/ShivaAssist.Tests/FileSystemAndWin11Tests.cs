using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class FileSystemAndWin11Tests
{
    private const long Gb = 1_000_000_000;

    [Fact]
    public void DirtyFileSystemOffersScheduledRestartRepairAndCleanDriveIsHealthy()
    {
        var dirty = Ctx.Single(
            new FileSystemCheck(new Fixed<FileSystemSnapshot>(null!)).Evaluate(new("C:", true, true, false, "dirty bit SET"), Ctx.Admin),
            FileSystemCheck.FindingId);
        Assert.Equal(Severity.Warning, dirty.Severity);

        var op = Assert.Single(RepairCatalogue.For(dirty));
        Assert.Equal(RepairCatalogue.ScheduleFileSystemRepair, op.Id);
        Assert.True(op.RestartRequired && op.VerifyLater && op.NeedsRestorePoint && op.RequiresAdmin);

        var clean = Ctx.Single(
            new FileSystemCheck(new Fixed<FileSystemSnapshot>(null!)).Evaluate(new("C:", false, true, false, "dirty bit clear"), Ctx.Admin),
            FileSystemCheck.FindingId);
        Assert.Equal(Severity.Healthy, clean.Severity);
        Assert.Empty(RepairCatalogue.For(clean));

        var needsAdmin = Ctx.Single(
            new FileSystemCheck(new Fixed<FileSystemSnapshot>(null!)).Evaluate(new("C:", null, null, true, "admin"), Ctx.User),
            FileSystemCheck.FindingId);
        Assert.Equal(Severity.Unknown, needsAdmin.Severity);
        Assert.Equal(UnknownReason.RequiresAdmin, needsAdmin.UnknownReason);
    }

    [Fact]
    public void Win11ReadinessDistinguishesWin11IneligibleUnknownAndReadyWin10()
    {
        Finding Eval(Win11ReadinessSnapshot s) => Ctx.Single(
            new Win11ReadinessCheck(new Fixed<Win11ReadinessSnapshot>(s)).Evaluate(s, Ctx.User),
            Win11ReadinessCheck.FindingId);

        var win11 = Eval(new(true, true, true, true, 16 * Gb, 500 * Gb));
        Assert.Equal(Severity.Healthy, win11.Severity);
        Assert.Empty(FindingValidator.Validate(win11));

        var missingTpm = Eval(new(false, true, false, true, 8 * Gb, 256 * Gb));
        Assert.Equal(Severity.Information, missingTpm.Severity);
        Assert.Contains("TPM 2.0", missingTpm.Title.ToString(), StringComparison.Ordinal);

        var unknown = Eval(new(false, true, null, true, 8 * Gb, 256 * Gb));
        Assert.Equal(Severity.Unknown, unknown.Severity);

        var ready = Eval(new(false, true, true, true, 8 * Gb, 256 * Gb));
        Assert.Equal(Severity.Information, ready.Severity);
        Assert.Contains("basic Windows 11", ready.Title.ToString(), StringComparison.Ordinal);
        Assert.Empty(RepairCatalogue.For(ready));
    }
}

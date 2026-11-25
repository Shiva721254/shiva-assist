using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class PrintTests
{
    private static Finding Scan(PrintSnapshot snapshot) =>
        Ctx.Single(new PrintCheck(new Fixed<PrintSnapshot>(null!)).Evaluate(snapshot, Ctx.User), PrintCheck.FindingId);

    [Fact]
    public void StoppedSpoolerWithPrintersIsAWarningWithTheSpoolerRepair()
    {
        var finding = Scan(new PrintSnapshot(SpoolerState.Stopped, "disabled", 2, 0, 0));
        Assert.Equal(Severity.Warning, finding.Severity);
        var repair = Assert.Single(RepairCatalogue.For(finding));
        Assert.Equal(RepairCatalogue.ResetPrintSpooler, repair.Id);
        Assert.True(repair.RequiresAdmin);
        Assert.False(repair.NeedsSelection || repair.RestartRequired || repair.NeedsRestorePoint);
    }

    [Fact]
    public void StuckJobsAreAWarning()
    {
        var finding = Scan(new PrintSnapshot(SpoolerState.Running, "automatic", 1, 3, 95));
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("3", finding.Title.Render(ShivaAssist.Core.Localization.TextCatalog.English), StringComparison.Ordinal);
        Assert.NotEmpty(RepairCatalogue.For(finding));
    }

    [Theory]
    [InlineData(SpoolerState.Running, 2, 0)]
    [InlineData(SpoolerState.Stopped, 0, 0)]
    [InlineData(SpoolerState.Missing, 0, 0)]
    public void HealthyCasesOfferNoRepair(SpoolerState state, int printers, int stuck)
    {
        var finding = Scan(new PrintSnapshot(state, "manual", printers, stuck, 0));
        Assert.Equal(Severity.Healthy, finding.Severity);
        Assert.Empty(RepairCatalogue.For(finding));
    }
}

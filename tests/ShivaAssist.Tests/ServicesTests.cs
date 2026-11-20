using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class ServicesTests
{
    private static ServiceInfo Running(string name) => new(name, ServiceRunState.Running, "automatic");

    private static IReadOnlyList<Finding> Scan(params ServiceInfo[] services) =>
        new ServicesCheck(new Fixed<ServicesSnapshot>(null!)).Evaluate(new ServicesSnapshot(services), Ctx.Admin);

    [Fact]
    public void AllRunningIsHealthyWithoutRepairs()
    {
        var findings = Scan(Running("AudioEndpointBuilder"), Running("Audiosrv"), Running("WSearch"));
        Assert.All(findings, f => Assert.Equal(Severity.Healthy, f.Severity));
        Assert.All(findings, f => Assert.Empty(RepairCatalogue.For(f)));
    }

    [Fact]
    public void StoppedAudioOffersTheAudioRepairInFixItAll()
    {
        var audio = Ctx.Single(Scan(Running("AudioEndpointBuilder"), new("Audiosrv", ServiceRunState.Stopped, "disabled"), Running("WSearch")), ServicesCheck.AudioId);
        Assert.Equal(Severity.Warning, audio.Severity);
        var repair = Assert.Single(RepairCatalogue.For(audio));
        Assert.Equal(RepairCatalogue.RepairAudioService, repair.Id);
        Assert.False(repair.NeedsSelection || repair.RestartRequired);
    }

    [Fact]
    public void DisabledSearchIsRepairableAndMissingSearchIsFine()
    {
        var search = Ctx.Single(Scan(Running("AudioEndpointBuilder"), Running("Audiosrv"), new("WSearch", ServiceRunState.Stopped, "disabled")), ServicesCheck.SearchId);
        Assert.Equal(Severity.Warning, search.Severity);
        Assert.Equal(RepairCatalogue.RepairSearchService, Assert.Single(RepairCatalogue.For(search)).Id);

        var missing = Ctx.Single(Scan(Running("AudioEndpointBuilder"), Running("Audiosrv")), ServicesCheck.SearchId);
        Assert.Equal(Severity.Healthy, missing.Severity);
    }

    [Fact]
    public void NetworkStackResetNeedsARestartAndARestorePointSoItStaysOutOfFixItAll()
    {
        var reset = RepairCatalogue.Find(RepairCatalogue.ResetNetworkStack)!;
        Assert.True(reset.RestartRequired && reset.NeedsRestorePoint && reset.VerifyLater);
    }
}

public sealed class FailedUpdateTests
{
    private static Finding Failed(IReadOnlyList<string> failing, int attempts) => Ctx.Single(
        new UpdateCheck(new Fixed<UpdateSnapshot>(null!)).Evaluate(
            new UpdateSnapshot(false, [], DateOnly.FromDateTime(Ctx.Now.UtcDateTime.AddDays(-3)), "KB1", failing, attempts), Ctx.Admin),
        "UPDATE-FAILED-001");

    [Fact]
    public void RepeatedFailuresOfAnUninstalledUpdateOfferTheUpdateReset()
    {
        var finding = Failed(["2026-11 Cumulative Update for Windows 11 (KB5099999)"], 3);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(RepairCatalogue.ResetUpdateComponents, Assert.Single(RepairCatalogue.For(finding)).Id);
    }

    [Fact]
    public void ASingleFailureOrNoneIsHealthy()
    {
        Assert.Equal(Severity.Healthy, Failed(["Some update"], 1).Severity);
        Assert.Equal(Severity.Healthy, Failed([], 0).Severity);
    }
}

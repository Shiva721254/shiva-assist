using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class WifiServiceTests
{
    [Theory]
    [InlineData(true, true, ServiceRunState.Stopped, "automatic", Severity.Warning, true)]
    [InlineData(true, false, ServiceRunState.Running, "automatic", Severity.Healthy, false)]
    [InlineData(true, true, ServiceRunState.Stopped, "disabled", Severity.Information, false)]
    [InlineData(true, true, ServiceRunState.Stopped, "manual", Severity.Unknown, false)]
    [InlineData(true, false, ServiceRunState.Stopped, "automatic", Severity.Unknown, false)]
    [InlineData(false, true, ServiceRunState.Stopped, "automatic", Severity.Unknown, false)]
    [InlineData(null, true, ServiceRunState.Stopped, "automatic", Severity.Unknown, false)]
    [InlineData(true, false, ServiceRunState.Missing, "unknown", Severity.Unknown, false)]
    public void OnlyConfirmedAutomaticStoppedWifiServiceOffersRepair(bool? adapter, bool stopped, ServiceRunState state, string start, Severity severity, bool repair)
    {
        var snapshot = new ServicesSnapshot([new ServiceInfo("WlanSvc", state, start)], adapter, stopped);
        var finding = Ctx.Single(new ServicesCheck(new Fixed<ServicesSnapshot>(snapshot)).Evaluate(snapshot, Ctx.User), ServicesCheck.WifiId);
        Assert.Equal(severity, finding.Severity);
        Assert.Equal(repair, RepairCatalogue.For(finding).Any(o => o.Id == RepairCatalogue.RepairWifiService));
    }
}

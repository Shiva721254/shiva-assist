using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Reporting;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class AppModuleTests
{
    private static Finding Of(string id) => new()
    {
        Id = id,
        Module = "test",
        Category = Category.Hardware,
        Severity = Severity.Warning,
        Title = "Test",
    };

    [Theory]
    [InlineData("CPU-LOAD-001", AppModules.ResultsKey)]
    [InlineData("CPU-CHECK-000", AppModules.ResultsKey)]
    [InlineData("MEMORY-COMMIT-001", AppModules.ResultsKey)]
    [InlineData("MEMORY-CAPACITY-001", AppModules.ResultsKey)]
    [InlineData("STORAGE-SPACE-001", AppModules.ResultsKey)]
    [InlineData("STORAGE-SPACE-002", AppModules.ResultsKey)]
    [InlineData("STORAGE-HEALTH-001", AppModules.ResultsKey)]
    [InlineData("PERF-STARTUP-001", AppModules.ResultsKey)]
    [InlineData("WIN-INTEGRITY-001", AppModules.ResultsKey)]
    [InlineData("HARDWARE-WHEA-001", AppModules.ResultsKey)]
    [InlineData("NETWORK-CONNECT-001", AppModules.ResultsKey)]
    [InlineData("SECURITY-AV-001", AppModules.ResultsKey)]
    [InlineData("SCAN-PRIVILEGE-001", AppModules.ResultsKey)]
    public void FindingsLandInTheRightModule(string id, string module) => Assert.Equal(module, AppModules.KeyFor(Of(id)));

    [Fact]
    public void ModulesAreUniqueAndLaterPhasesHaveNoFindings()
    {
        Assert.Equal(AppModules.All.Count, AppModules.All.Select(m => m.Key).Distinct().Count());
        Assert.Equal(AppModules.OverviewKey, AppModules.All[0].Key);
        Assert.All(AppModules.All.Where(m => !m.Available), m => Assert.Empty(m.IdPrefixes));
        Assert.Equal(5, AppModules.All.Count); // home, findings, repairs, apps, history/help
    }
}

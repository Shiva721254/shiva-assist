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
    [InlineData("CPU-LOAD-001", "performance")]
    [InlineData("CPU-CHECK-000", "hardware")]
    [InlineData("MEMORY-COMMIT-001", "performance")]
    [InlineData("MEMORY-CAPACITY-001", "hardware")]
    [InlineData("STORAGE-SPACE-001", "cleaner")]
    [InlineData("STORAGE-SPACE-002", "cleaner")]
    [InlineData("STORAGE-HEALTH-001", "storage-health")]
    [InlineData("PERF-STARTUP-001", "startup")]
    [InlineData("WIN-INTEGRITY-001", "windows")]
    [InlineData("HARDWARE-WHEA-001", "hardware")]
    [InlineData("NETWORK-CONNECT-001", "network")]
    [InlineData("SECURITY-AV-001", "security")]
    [InlineData("SCAN-PRIVILEGE-001", AppModules.OverviewKey)]
    public void FindingsLandInTheRightModule(string id, string module) => Assert.Equal(module, AppModules.KeyFor(Of(id)));

    [Fact]
    public void ModulesAreUniqueAndLaterPhasesHaveNoFindings()
    {
        Assert.Equal(AppModules.All.Count, AppModules.All.Select(m => m.Key).Distinct().Count());
        Assert.Equal(AppModules.OverviewKey, AppModules.All[0].Key);
        Assert.All(AppModules.All.Where(m => !m.Available), m => Assert.Empty(m.IdPrefixes));
        Assert.Equal(14, AppModules.All.Count); // overview, results and the twelve blueprint modules
    }
}

using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;

namespace ShivaAssist.Core.Reporting;

/// <summary>
/// One of the app's five main pages. Diagnostic categories are collected on PC health.
/// </summary>
public sealed record AppModule
{
    public required string Key { get; init; }

    public required Message Name { get; init; }

    public required Message Description { get; init; }

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets code point.</summary>
    public required string Glyph { get; init; }

    public IReadOnlyList<string> IdPrefixes { get; init; } = [];

    public bool Available { get; init; } = true;
}

public static class AppModules
{
    public const string OverviewKey = "overview";
    public const string ResultsKey = "results";
    public const string HistoryKey = "history";

    public const string RepairsKey = "repairs";

    public static IReadOnlyList<AppModule> All { get; } =
    [
        new() { Key = OverviewKey, Name = "Overview",
            Description = "Choose the areas to check and start a new scan.", Glyph = "\uE80F" },
        new() { Key = ResultsKey, Name = "PC health",
            Description = "Hardware, storage, performance, startup, Windows, network and security findings.", Glyph = "\uE9D9" },
        new() { Key = RepairsKey, Name = "Optimize",
            Description = "Review performance and storage improvements.", Glyph = "\uE90F" },
        new() { Key = "software", Name = "Updates and apps",
            Description = "Windows Update, driver updates, program updates and uninstaller.", Glyph = "\uE71D" },
        new() { Key = HistoryKey, Name = "History and help",
            Description = "Review previous repairs, undo available changes or contact Shiva Computers for help.", Glyph = "\uE81C" },
    ];

    /// <summary>All diagnostic findings remain accessible on PC health.</summary>
    public static string KeyFor(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return ResultsKey;
    }
}

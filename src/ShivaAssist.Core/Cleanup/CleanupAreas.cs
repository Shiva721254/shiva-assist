using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Repairs;

namespace ShivaAssist.Core.Cleanup;

/// <summary>
/// One area the cleanup preview measures before anything is deleted. Each area is exactly one catalogue repair,
/// so the cleanup itself keeps the normal validation, audit log and verification.
/// </summary>
public sealed record CleanupArea(string OperationId, Message Name, Message Description, string Glyph);

/// <summary>What one area would free, measured read-only. Bytes is null when it could not be measured.</summary>
public sealed record CleanupEstimate(string OperationId, long? Bytes, int Files);

public static class CleanupAreas
{
    public static IReadOnlyList<CleanupArea> All { get; } =
    [
        new(RepairCatalogue.CleanTempFiles, new Message("Old temporary files"),
            new Message("Files programs left behind more than 7 days ago. Files in use are skipped."), ""),
        new(RepairCatalogue.CleanBrowserCache, new Message("Browser cache"),
            new Message("Stored copies of web pages in Edge, Chrome and Firefox. Passwords, cookies, history and bookmarks stay. Close the browser for the best result."), ""),
        new(RepairCatalogue.EmptyRecycleBin, new Message("Recycle Bin"),
            new Message("Files you deleted earlier. After emptying, they cannot be restored."), ""),
        new(RepairCatalogue.CleanUpdateCache, new Message("Windows Update downloads and crash reports"),
            new Message("Update files Windows already installed, and old crash dump files."), ""),
        new(RepairCatalogue.CleanShaderCache, new Message("Graphics shader cache"),
            new Message("Temporary graphics cache files older than 7 days (DirectX, NVIDIA, AMD, Intel). Windows and games recreate them automatically."), "\uE7F4"),
    ];

    /// <summary>Total of the areas that are selected and could be measured.</summary>
    public static long Total(IEnumerable<CleanupEstimate> estimates) =>
        estimates?.Sum(e => e.Bytes ?? 0) ?? 0;
}

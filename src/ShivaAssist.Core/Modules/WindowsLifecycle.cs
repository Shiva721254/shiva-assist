namespace ShivaAssist.Core.Modules;

/// <summary>
/// End-of-updates dates per Windows 11 feature update, from Microsoft's Windows 11 release information and
/// lifecycle pages (checked 16 November 2026). Home and Pro get about 24 months, Enterprise and Education 36.
/// Update this table when Microsoft publishes a new version; unknown newer versions are treated as supported.
/// </summary>
public static class WindowsLifecycle
{
    public const string TableVersion = "2026-11-16";

    private static readonly Dictionary<string, (DateOnly HomePro, DateOnly? Enterprise)> Windows11 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["21H2"] = (new DateOnly(2023, 10, 10), new DateOnly(2024, 10, 8)),
        ["22H2"] = (new DateOnly(2024, 10, 8), new DateOnly(2025, 10, 14)),
        ["23H2"] = (new DateOnly(2025, 11, 11), new DateOnly(2026, 11, 10)),
        ["24H2"] = (new DateOnly(2026, 10, 13), new DateOnly(2027, 10, 12)),
        ["25H2"] = (new DateOnly(2027, 10, 12), new DateOnly(2028, 10, 10)),
        ["26H1"] = (new DateOnly(2028, 3, 14), new DateOnly(2029, 3, 13)),
    };

    /// <summary>Windows 10 Home and Pro: end of regular support.</summary>
    public static readonly DateOnly Windows10EndOfSupport = new(2025, 10, 14);

    /// <summary>Enterprise-type editions follow the longer servicing schedule.</summary>
    public static bool IsEnterpriseLike(string edition) =>
        edition.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase)
        || edition.StartsWith("Education", StringComparison.OrdinalIgnoreCase)
        || edition.StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase);

    /// <summary>End of updates for a Windows 11 version and edition, or null when the version is not in the table.</summary>
    public static DateOnly? EndOfUpdates(string displayVersion, string edition)
    {
        ArgumentNullException.ThrowIfNull(edition);
        if (!Windows11.TryGetValue(displayVersion, out var dates))
        {
            return null;
        }

        return IsEnterpriseLike(edition) ? dates.Enterprise ?? dates.HomePro : dates.HomePro;
    }
}

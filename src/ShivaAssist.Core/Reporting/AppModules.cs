using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;

namespace ShivaAssist.Core.Reporting;

/// <summary>
/// One page in the app's menu (blueprint: twelve modules). A finding belongs to exactly one module, chosen by the
/// first matching ID prefix; modules that are not built yet are shown as "soon".
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
    public const string HistoryKey = "history";

    public static IReadOnlyList<AppModule> All { get; } =
    [
        new()
        {
            Key = OverviewKey,
            Name = "Overview",
            Description = "The results of the check-up for all areas together.",
            Glyph = "\uE80F",
        },
        new()
        {
            Key = "hardware",
            Name = "Hardware",
            Description = "Processor, memory, battery, devices and hardware errors reported by Windows.",
            Glyph = "\uE7F8",
            IdPrefixes = ["MEMORY-CAPACITY", "MEMORY-CHECK", "CPU-CHECK", "BATTERY-", "DEVICE-", "HARDWARE-"],
        },
        new()
        {
            Key = "storage-health",
            Name = "Storage health",
            Description = "The health of the hard disks and SSDs, as reported by the drives themselves.",
            Glyph = "\uEDA2",
            IdPrefixes = ["STORAGE-HEALTH", "STORAGE-CHECK"],
        },
        new()
        {
            Key = "cleaner",
            Name = "Cleaner",
            Description = "Free space on the Windows drive and safe cleanups: Recycle Bin, temporary files and old update files.",
            Glyph = "\uE74D",
            IdPrefixes = ["STORAGE-SPACE"],
        },
        new()
        {
            Key = "performance",
            Name = "Performance",
            Description = "Processor load, memory pressure and how long the PC has been running without a restart.",
            Glyph = "\uE9D9",
            IdPrefixes = ["CPU-LOAD", "MEMORY-USAGE", "MEMORY-COMMIT", "OS-UPTIME"],
        },
        new()
        {
            Key = "startup",
            Name = "Startup",
            Description = "Programs that start together with Windows, and switching off the ones you do not need.",
            Glyph = "\uE768",
            IdPrefixes = ["PERF-STARTUP"],
        },
        new()
        {
            Key = "windows",
            Name = "Windows repair",
            Description = "Windows version and support, updates, system files, crashes and unexpected shutdowns.",
            Glyph = "\uE90F",
            IdPrefixes = ["OS-", "UPDATE-", "WIN-", "SYSTEM-", "APP-CRASH"],
        },
        new()
        {
            Key = "network",
            Name = "Network",
            Description = "Network adapter, address, router, DNS and internet, tested layer by layer.",
            Glyph = "\uE701",
            IdPrefixes = ["NETWORK-"],
        },
        new()
        {
            Key = "security",
            Name = "Security",
            Description = "Antivirus and firewall protection.",
            Glyph = "\uEA18",
            IdPrefixes = ["SECURITY-"],
        },
        new()
        {
            Key = "software",
            Name = "Software",
            Description = "Browse the programs registered in Windows and see their publisher, version, estimated size and install date.",
            Glyph = "\uE71D",
        },
        new()
        {
            Key = HistoryKey,
            Name = "History and undo",
            Description = "Every repair with its result, and undoing the changes that can be reversed.",
            Glyph = "\uE81C",
        },
        new()
        {
            Key = "recovery",
            Name = "Emergency recovery",
            Description = "When Windows no longer starts: rescue your files and repair the start-up with Shiva Rescue.",
            Glyph = "\uE72C",
            Available = false,
        },
        new()
        {
            Key = "remote",
            Name = "Remote help",
            Description = "A Shiva Computers technician helps on this PC, only with your permission and only for one job.",
            Glyph = "\uE77B",
            Available = false,
        },
    ];

    /// <summary>The module a finding is shown under; findings without a module only appear in the overview.</summary>
    public static string KeyFor(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        foreach (var module in All)
        {
            if (module.IdPrefixes.Any(p => finding.Id.StartsWith(p, StringComparison.Ordinal)))
            {
                return module.Key;
            }
        }

        return OverviewKey;
    }
}

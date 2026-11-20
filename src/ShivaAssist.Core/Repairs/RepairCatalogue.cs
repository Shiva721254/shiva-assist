using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Modules;

namespace ShivaAssist.Core.Repairs;

/// <summary>Risk class of a repair (architecture: repair risk L/M/H). Medium and High require a restore point.</summary>
public enum RiskClass
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One allowed repair. Nothing outside <see cref="RepairCatalogue"/> can run: the elevated helper refuses any
/// operation ID it does not know.
/// </summary>
public sealed record RepairOperation
{
    public required string Id { get; init; }

    public required Message Name { get; init; }

    /// <summary>What exactly happens, shown before the customer agrees.</summary>
    public required Message Description { get; init; }

    public required RiskClass Risk { get; init; }

    public bool RequiresAdmin { get; init; }

    public bool RestartRequired { get; init; }

    public required (int Min, int Max) Minutes { get; init; }

    /// <summary>The finding whose check is repeated afterwards to verify the repair.</summary>
    public required string VerifiesFinding { get; init; }

    /// <summary>Asks the user to pick items first (e.g. which startup programs to switch off).</summary>
    public bool NeedsSelection { get; init; }

    /// <summary>
    /// The result can only be confirmed later (after a restart or once Windows has worked in the background), so the
    /// check is not repeated straight away and the verdict is <see cref="RepairVerdict.Pending"/>.
    /// </summary>
    public bool VerifyLater { get; init; }

    /// <summary>Reverses an earlier repair; started from the history page, never offered for a finding.</summary>
    public bool IsUndo { get; init; }

    public bool NeedsRestorePoint => Risk >= RiskClass.Medium && !IsUndo;
}

/// <summary>The signed repair catalogue (stages 3a and 3b). IDs follow the contract: <c>AREA.TOPIC.ACTION</c>.</summary>
public static class RepairCatalogue
{
    public const string EmptyRecycleBin = "DISK.RECYCLEBIN.EMPTY";
    public const string CleanTempFiles = "DISK.TEMP.CLEAN";
    public const string CleanComponentStore = "DISK.COMPONENTS.CLEANUP";
    public const string DisableStartupPrograms = "STARTUP.PROGRAMS.DISABLE";
    public const string RepairSystemFiles = "WIN.SYSTEMFILES.REPAIR";
    public const string CleanUpdateCache = "DISK.UPDATECACHE.CLEAN";
    public const string FlushDns = "NET.DNS.FLUSH";
    public const string RenewAddress = "NET.ADDRESS.RENEW";
    public const string UpdateDefender = "SECURITY.DEFENDER.UPDATE";
    public const string RestartNow = "UPDATE.RESTART.NOW";
    public const string ResetUpdateComponents = "UPDATE.COMPONENTS.RESET";
    public const string RestoreStartupPrograms = "STARTUP.PROGRAMS.RESTORE";
    public const string UpdateSoftware = "SOFTWARE.PACKAGE.UPDATE";
    public const string UninstallSoftware = "SOFTWARE.PROGRAM.UNINSTALL";
    public const string CleanSoftwareLeftovers = "SOFTWARE.LEFTOVERS.CLEAN";
    public const string RestoreSoftwareLeftovers = "SOFTWARE.LEFTOVERS.RESTORE";
    public const string ResetPrintSpooler = "PRINT.SPOOLER.RESET";
    public const string SyncClock = "TIME.SYNC.REPAIR";
    public const string CleanBrowserCache = "DISK.BROWSERCACHE.CLEAN";
    public const string RepairAudioService = "AUDIO.SERVICE.REPAIR";
    public const string RepairSearchService = "SEARCH.SERVICE.REPAIR";
    public const string RepairWifiService = "NET.WIFI.START";
    public const string ResetNetworkStack = "NET.STACK.RESET";

    public static IReadOnlyList<RepairOperation> All { get; } =
    [
        new()
        {
            Id = UninstallSoftware,
            Name = "Uninstall selected program",
            Description = "Runs the program's registered uninstaller after creating a restore point. Save your work first. Follow the uninstaller's prompts. A restart may be required. Reinstalling the program may be needed to reverse its removal. Leftover cleanup requires separate approval.",
            Risk = RiskClass.Medium,
            RequiresAdmin = true,
            Minutes = (1, 30),
            VerifiesFinding = "SOFTWARE-UNINSTALL-001",
            NeedsSelection = true,
        },
        new()
        {
            Id = CleanSoftwareLeftovers,
            Name = "Clean selected leftovers",
            Description = "Moves only selected, unchanged installation-folder leftovers to recovery storage and removes selected empty folders. Personal files, settings, shared folders and registry keys are excluded. Recovery copies still use disk space. Undo is available in History.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 5),
            VerifiesFinding = "SOFTWARE-LEFTOVERS-001",
            NeedsSelection = true,
        },
        new()
        {
            Id = RestoreSoftwareLeftovers,
            Name = "Restore cleaned leftovers",
            Description = "Restores the leftover files and empty folders from this cleanup's recovery storage. Existing files are never overwritten. This does not reinstall the program.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            IsUndo = true,
            Minutes = (1, 5),
            VerifiesFinding = "SOFTWARE-LEFTOVERS-001",
        },
        new()
        {
            Id = UpdateSoftware,
            Name = "Update selected program",
            Description = "Updates one selected program through Windows Package Manager. Save your work and close that program first. Its installer may ask for permission or licence acceptance.",
            Risk = RiskClass.Low,
            Minutes = (1, 20),
            VerifiesFinding = "SOFTWARE-UPDATE-001",
            NeedsSelection = true,
        },
        new()
        {
            Id = EmptyRecycleBin,
            Name = "Empty the Recycle Bin",
            Description = "Permanently deletes the files in the Recycle Bin. Files that are not in the Recycle Bin are not touched.",
            Risk = RiskClass.Low,
            Minutes = (1, 2),
            VerifiesFinding = "STORAGE-SPACE-001",
        },
        new()
        {
            Id = CleanTempFiles,
            Name = "Delete old temporary files",
            Description = "Deletes temporary files that programs left behind more than 7 days ago. Files that are in use are skipped. Your documents, photos and downloads are not touched.",
            Risk = RiskClass.Low,
            Minutes = (1, 5),
            VerifiesFinding = "STORAGE-SPACE-001",
        },
        new()
        {
            Id = CleanComponentStore,
            Name = "Remove old Windows update files",
            Description = "Lets Windows remove superseded versions of its own system files with its built-in cleanup (DISM /StartComponentCleanup). Installed updates keep working.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (5, 20),
            VerifiesFinding = "STORAGE-SPACE-001",
        },
        new()
        {
            Id = DisableStartupPrograms,
            Name = "Switch off startup programs",
            Description = "Stops the programs you select from starting with Windows, the same way Task Manager does. The programs stay installed and can be switched on again at any time.",
            Risk = RiskClass.Low,
            Minutes = (1, 1),
            VerifiesFinding = "PERF-STARTUP-001",
            NeedsSelection = true,
        },
        new()
        {
            Id = RepairSystemFiles,
            Name = "Repair Windows system files",
            Description = "Repairs the damaged Windows component store from Windows Update (DISM /RestoreHealth), then checks and restores the protected system files (sfc /scannow). A restore point is made first. Keep the PC switched on and connected to the charger.",
            Risk = RiskClass.Medium,
            RequiresAdmin = true,
            Minutes = (15, 45),
            VerifiesFinding = "WIN-INTEGRITY-001",
        },
        new()
        {
            Id = CleanUpdateCache,
            Name = "Remove downloaded update files and crash reports",
            Description = "Deletes update files Windows already installed (the download cache and Delivery Optimization cache) and old crash dump files. Windows downloads again whatever it still needs.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 5),
            VerifiesFinding = "STORAGE-SPACE-001",
        },
        new()
        {
            Id = FlushDns,
            Name = "Reset the DNS cache",
            Description = "Clears the list of website addresses Windows remembers, so they are looked up again (ipconfig /flushdns). Safe at any time.",
            Risk = RiskClass.Low,
            Minutes = (1, 1),
            VerifiesFinding = "NETWORK-CONNECT-001",
        },
        new()
        {
            Id = RenewAddress,
            Name = "Request a new network address",
            Description = "Releases the current network address and asks the router for a new one (ipconfig /release and /renew). The connection drops for a few seconds.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 2),
            VerifiesFinding = "NETWORK-CONNECT-001",
        },
        new()
        {
            Id = UpdateDefender,
            Name = "Update Microsoft Defender",
            Description = "Downloads the latest virus definitions for Microsoft Defender from Microsoft.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 5),
            VerifiesFinding = "SECURITY-AV-001",
        },
        new()
        {
            Id = RestartNow,
            Name = "Restart the PC to finish updates",
            Description = "Restarts the PC in 2 minutes so Windows can finish installing updates. Save your work and close your programs first.",
            Risk = RiskClass.Low,
            RestartRequired = true,
            VerifyLater = true,
            Minutes = (2, 15),
            VerifiesFinding = "UPDATE-RESTART-001",
        },
        new()
        {
            Id = ResetUpdateComponents,
            Name = "Reset Windows Update",
            Description = "Stops the update services, sets aside the Windows Update download folder and its signature store (they are renamed, not deleted), and starts the services again. Windows then rebuilds them and checks for updates. A restore point is made first.",
            Risk = RiskClass.Medium,
            RequiresAdmin = true,
            VerifyLater = true,
            Minutes = (5, 15),
            VerifiesFinding = "UPDATE-RECENT-001",
        },
        new()
        {
            Id = RestoreStartupPrograms,
            Name = "Switch startup programs on again",
            Description = "Puts the startup programs that Shiva Assist switched off back the way they were.",
            Risk = RiskClass.Low,
            Minutes = (1, 1),
            VerifiesFinding = "PERF-STARTUP-001",
            IsUndo = true,
        },
        new()
        {
            Id = ResetPrintSpooler,
            Name = "Restart printing and clear stuck print jobs",
            Description = "Restarts the Windows print service (Print Spooler) and removes print jobs that are stuck in the queue. Your documents are not affected: print them again afterwards. If the print service was switched off, it is set to start automatically again.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 2),
            VerifiesFinding = PrintCheck.FindingId,
        },
        new()
        {
            Id = SyncClock,
            Name = "Set the clock right",
            Description = "Switches automatic time synchronisation back on and sets the clock to the exact time from Microsoft's internet time server (time.windows.com).",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 1),
            VerifiesFinding = ClockCheck.FindingId,
        },
        new()
        {
            Id = CleanBrowserCache,
            Name = "Clear the browser cache",
            Description = "Deletes the stored copies of web pages that Edge, Chrome and Firefox keep. Passwords, cookies, history and bookmarks are not touched. Pages may load a little slower the first time afterwards.",
            Risk = RiskClass.Low,
            Minutes = (1, 3),
            VerifiesFinding = "STORAGE-SPACE-001",
        },
        new()
        {
            Id = RepairAudioService,
            Name = "Switch the sound service back on",
            Description = "Switches the Windows Audio services back to automatic start and restarts them. Sound usually works again straight away; programs that were playing may need to be restarted.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 1),
            VerifiesFinding = ServicesCheck.AudioId,
        },
        new()
        {
            Id = RepairSearchService,
            Name = "Switch Windows Search back on",
            Description = "Sets the Windows Search service back to automatic (delayed) start, as Windows ships it, and starts it. Windows then updates its search index in the background.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 1),
            VerifiesFinding = ServicesCheck.SearchId,
        },
        new()
        {
            Id = RepairWifiService,
            Name = "Start the Windows Wi-Fi service",
            Description = "Starts the stopped Windows Wi-Fi service without changing network settings or startup type.",
            Risk = RiskClass.Low,
            RequiresAdmin = true,
            Minutes = (1, 1),
            VerifiesFinding = ServicesCheck.WifiId,
        },
        new()
        {
            Id = ResetNetworkStack,
            Name = "Reset the network settings of Windows",
            Description = "Resets the Windows network components (Winsock and TCP/IP) to their default settings, which fixes most damage left by VPNs, security software or malware. A restore point is made first. Fixed IP addresses and VPN adapters may need to be set up again. A restart is needed afterwards.",
            Risk = RiskClass.Medium,
            RequiresAdmin = true,
            RestartRequired = true,
            VerifyLater = true,
            Minutes = (2, 5),
            VerifiesFinding = "NETWORK-CONNECT-001",
        },
    ];

    /// <summary>The operation that reverses another one, when the original recorded what it changed.</summary>
    public static RepairOperation? UndoFor(string operationId) => operationId switch
    {
        DisableStartupPrograms => Find(RestoreStartupPrograms),
        CleanSoftwareLeftovers => Find(RestoreSoftwareLeftovers),
        _ => null,
    };

    public static RepairOperation? Find(string id) => All.FirstOrDefault(o => o.Id == id);

    public static bool Contains(string id) => Find(id) is not null;

    /// <summary>Repairs offered for a finding. Only problems get repairs; healthy or unknown findings never do.</summary>
    public static IReadOnlyList<RepairOperation> For(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (finding.Severity is not (Severity.Critical or Severity.Warning or Severity.Information))
        {
            return [];
        }

        return finding.Id switch
        {
            "STORAGE-SPACE-001" when finding.Severity != Severity.Information => [Find(EmptyRecycleBin)!, Find(CleanTempFiles)!, Find(CleanBrowserCache)!, Find(CleanUpdateCache)!, Find(CleanComponentStore)!],
            "NETWORK-CONNECT-001" => NetworkRepairs(finding),
            "SECURITY-AV-001" when finding.Severity == Severity.Warning && Observed(finding, "antivirus on, real-time protection on") => [Find(UpdateDefender)!],
            "UPDATE-RESTART-001" when finding.Severity == Severity.Warning => [Find(RestartNow)!],
            "UPDATE-RECENT-001" when finding.Severity == Severity.Warning => [Find(ResetUpdateComponents)!],
            "UPDATE-FAILED-001" when finding.Severity == Severity.Warning => [Find(ResetUpdateComponents)!],
            "PERF-STARTUP-001" => [Find(DisableStartupPrograms)!],
            PrintCheck.FindingId when finding.Severity == Severity.Warning => [Find(ResetPrintSpooler)!],
            ClockCheck.FindingId when finding.Severity == Severity.Warning => [Find(SyncClock)!],
            ServicesCheck.AudioId when finding.Severity == Severity.Warning => [Find(RepairAudioService)!],
            ServicesCheck.WifiId when finding.Severity == Severity.Warning => [Find(RepairWifiService)!],
            ServicesCheck.SearchId when finding.Severity == Severity.Warning => [Find(RepairSearchService)!],
            "WIN-INTEGRITY-001" when finding.Evidence.Any(e => e.Observation == DismOutput.RepairableSummary) => [Find(RepairSystemFiles)!],
            _ => [],
        };
    }

    /// <summary>Network repairs follow the failing layer (see NetworkCheck): no address → renew; DNS → flush.</summary>
    private static List<RepairOperation> NetworkRepairs(Finding finding)
    {
        if (finding.Severity == Severity.Information || Observed(finding, "adapter failed"))
        {
            return []; // Captive portal, or no network at all: nothing to repair on the PC.
        }

        var operations = new List<RepairOperation>();
        if (Observed(finding, "IP address failed") || Observed(finding, "gateway failed"))
        {
            operations.Add(Find(RenewAddress)!);
        }

        if (Observed(finding, "DNS failed"))
        {
            operations.Add(Find(FlushDns)!);
        }

        // The heavier reset (restart needed) comes last, for when the quick repairs did not help.
        if (operations.Count > 0)
        {
            operations.Add(Find(ResetNetworkStack)!);
        }

        return operations;
    }

    private static bool Observed(Finding finding, string text) =>
        finding.Evidence.Any(e => e.Observation.Contains(text, StringComparison.Ordinal));
}

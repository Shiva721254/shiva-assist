namespace ShivaAssist.Core.Modules;

// Plain data that the Windows sources collect. No judgement here: the checks decide what it means.
// Nothing in these records identifies the person or the PC (no names, paths or serial numbers).

public sealed record OsSnapshot(string ProductName, string Edition, string DisplayVersion, int Build, int Revision, TimeSpan Uptime);

public sealed record ProcessorSnapshot(string Name, int Cores, int LogicalProcessors, IReadOnlyList<int> LoadSamples);


/// <summary>Physical memory plus the commit charge (memory promised to programs, backed by RAM and the page file).</summary>
public sealed record MemorySnapshot(long TotalBytes, long AvailableBytes, long CommitLimitBytes = 0, long CommitUsedBytes = 0);

public sealed record Volume(string Name, string FileSystem, long SizeBytes, long FreeBytes, bool IsSystem);

/// <summary>Health as Windows Storage reports it (MSFT_PhysicalDisk.HealthStatus).</summary>
public enum DiskHealth
{
    Healthy,
    Warning,
    Unhealthy,
    Unknown,
}

public sealed record PhysicalDisk(string Model, string MediaType, string BusType, long SizeBytes, DiskHealth Health, DiskReliability? Reliability = null);

/// <summary>
/// The disk's own reliability counters (SMART / NVMe health log via MSFT_StorageReliabilityCounter). Every value is
/// optional: many disks and USB enclosures report only some of them, and reading them needs administrator rights.
/// </summary>
public sealed record DiskReliability(int? WearPercent, int? TemperatureC, long? PowerOnHours, long? UncorrectedErrors)
{
    public bool HasAny => WearPercent is not null || TemperatureC is not null || PowerOnHours is not null || UncorrectedErrors is not null;
}

public sealed record StorageSnapshot(IReadOnlyList<Volume> Volumes, IReadOnlyList<PhysicalDisk>? Disks);

public sealed record BatterySnapshot(bool HasBattery, long? DesignCapacity, long? FullChargeCapacity, int? CycleCount);

/// <summary>Result of <c>DISM /Online /Cleanup-Image /CheckHealth</c>, classified from its English output.</summary>
public enum ComponentStoreState
{
    NoCorruption,
    Repairable,
    NotRepairable,
    Unrecognised,
}

public sealed record IntegritySnapshot(ComponentStoreState State, string Summary);

/// <summary>
/// Restart markers, the last installed update and, from the Windows Update event log of the last 30 days, the updates
/// that failed to install and were not installed successfully afterwards (FailedAttempts counts every failure event).
/// </summary>
public sealed record UpdateSnapshot(bool RestartPending, IReadOnlyList<string> RestartReasons, DateOnly? LastUpdateInstalled, string? LastUpdateId,
    IReadOnlyList<string>? StillFailing = null, int FailedAttempts = 0);

public sealed record AntivirusProduct(string Name, bool Enabled, bool UpToDate);

public sealed record FirewallProfile(string Name, bool Enabled);

/// <summary>Microsoft Defender's own status, used when Security Center lists no antivirus (null when Defender is not present).</summary>
public sealed record DefenderStatus(bool AntivirusEnabled, bool RealTimeProtection, int SignatureAgeDays);

public sealed record SecuritySnapshot(IReadOnlyList<AntivirusProduct>? Antivirus, IReadOnlyList<FirewallProfile>? Firewall, DefenderStatus? Defender = null);

/// <summary>
/// One program that starts with Windows. <see cref="Location"/> is where it is registered
/// (<c>HKCU-Run</c>, <c>HKLM-Run</c>, <c>HKLM-Run32</c>, <c>UserFolder</c>, <c>CommonFolder</c>) and <see cref="Key"/> the
/// registry value or shortcut file name; together they identify it for switching it off.
/// </summary>
public sealed record StartupEntry(string Name, string Location, string Key)
{
    public bool Enabled { get; init; } = true;
    /// <summary>Compact form for repair jobs: <c>Location|Key</c>.</summary>
    public string Reference => Location + "|" + Key;
}

public sealed record StartupSnapshot(IReadOnlyList<StartupEntry> Entries)
{
    public StartupSnapshot(IReadOnlyList<string> names)
        : this(names.Select(n => new StartupEntry(n, "HKCU-Run", n)).ToList())
    {
    }

    public IReadOnlyList<string> EnabledEntries => [.. Entries.Where(e => e.Enabled).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>Counts of reliability events in the look-back window.</summary>
public sealed record StabilitySnapshot(
    int Days,
    int UnexpectedShutdowns,
    int BlueScreens,
    int HardwareErrors,
    IReadOnlyDictionary<string, int> AppCrashesByApp);

/// <summary>A device that Windows reports with a problem code (Device Manager's yellow triangle).</summary>
public sealed record ProblemDevice(string Name, string DeviceClass, int ProblemCode);

public sealed record DeviceSnapshot(int PresentDevices, IReadOnlyList<ProblemDevice> Problems);

/// <summary>State of the Windows Print Spooler service of the print queue.</summary>
public enum SpoolerState
{
    Running,
    Stopped,
    Missing,
}

/// <summary>
/// Printing (top-20 problem "printer does not print"): the Print Spooler service, how many printers are set up and
/// how many jobs are stuck (error, deleting or blocked, or waiting for 30 minutes or longer).
/// </summary>
public sealed record PrintSnapshot(SpoolerState Spooler, string StartType, int Printers, int StuckJobs, int OldestStuckMinutes);

/// <summary>Result of one network layer test.</summary>
public enum LayerResult
{
    Passed,
    Failed,
    NotTested,
}

/// <summary>
/// Network connectivity, layer by layer, as in the Shiva Network concept: adapter → IP address → gateway → DNS → internet.
/// A failed gateway ping is weak evidence (many routers ignore ping); a failed DNS lookup with working internet IP is strong.
/// </summary>
public sealed record NetworkSnapshot(
    IReadOnlyList<string> ActiveAdapters,
    LayerResult Adapter,
    LayerResult IpAddress,
    LayerResult Gateway,
    LayerResult Dns,
    LayerResult Internet,
    bool CaptivePortal,
    bool ProxyConfigured);

/// <summary>State of the Windows Time service (W32Time).</summary>
public enum TimeServiceState
{
    Running,
    Stopped,
    Disabled,
    Missing,
}

/// <summary>
/// The PC clock compared with an internet time server (w32tm /stripchart, one sample). A wrong clock breaks
/// secure websites ("your clock is ahead"), sign-ins and updates. Offset is null when no server answered.
/// </summary>
public sealed record TimeSnapshot(TimeServiceState Service, double? OffsetSeconds, string Server);

/// <summary>Whether a Windows service runs.</summary>
public enum ServiceRunState
{
    Running,
    Stopped,
    Missing,
}

/// <summary>One Windows service: run state and start type ("automatic", "manual", "disabled", ...).</summary>
public sealed record ServiceInfo(string Name, ServiceRunState State, string StartType)
{
    public bool Disabled => string.Equals(StartType, "disabled", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The Windows services behind common "it suddenly stopped working" problems: sound and search.</summary>
public sealed record ServicesSnapshot(IReadOnlyList<ServiceInfo> Services)
{
    public ServiceInfo Get(string name) =>
        Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? new ServiceInfo(name, ServiceRunState.Missing, "unknown");
}


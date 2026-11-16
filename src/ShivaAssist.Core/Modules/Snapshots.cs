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

public sealed record PhysicalDisk(string Model, string MediaType, string BusType, long SizeBytes, DiskHealth Health);

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

public sealed record UpdateSnapshot(bool RestartPending, IReadOnlyList<string> RestartReasons, DateOnly? LastUpdateInstalled, string? LastUpdateId);

public sealed record AntivirusProduct(string Name, bool Enabled, bool UpToDate);

public sealed record FirewallProfile(string Name, bool Enabled);

/// <summary>Microsoft Defender's own status, used when Security Center lists no antivirus (null when Defender is not present).</summary>
public sealed record DefenderStatus(bool AntivirusEnabled, bool RealTimeProtection, int SignatureAgeDays);

public sealed record SecuritySnapshot(IReadOnlyList<AntivirusProduct>? Antivirus, IReadOnlyList<FirewallProfile>? Firewall, DefenderStatus? Defender = null);

public sealed record StartupSnapshot(IReadOnlyList<string> EnabledEntries);

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

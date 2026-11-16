using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Drive space (DriveInfo) and the health Windows Storage reports per physical disk.</summary>
public sealed class StorageSource : ISnapshotSource<StorageSnapshot>
{
    private const string Script = """
        $disks = Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_PhysicalDisk |
            ForEach-Object {
                [pscustomobject]@{
                    model = "$($_.FriendlyName)".Trim()
                    media = [int]$_.MediaType
                    bus = [int]$_.BusType
                    size = [long]$_.Size
                    health = [int]$_.HealthStatus
                }
            }
        [pscustomobject]@{ disks = @($disks) } | ConvertTo-Json -Compress -Depth 3
        """;

    public async Task<StorageSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var volumes = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new Volume(
                d.Name.TrimEnd('\\'),
                d.DriveFormat,
                d.TotalSize,
                d.AvailableFreeSpace,
                string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        IReadOnlyList<PhysicalDisk>? disks;
        try
        {
            var json = await QueryAsync(Script, cancellationToken);
            disks = [.. Items(json, "disks").Select(d => new PhysicalDisk(
                Str(d, "model"),
                Long(d, "media") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "disk" },
                Long(d, "bus") switch { 7 => "USB", 11 => "SATA", 17 => "NVMe", 8 => "RAID", 10 => "SAS", _ => "other" },
                Long(d, "size"),
                Long(d, "health") switch { 0 => DiskHealth.Healthy, 1 => DiskHealth.Warning, 2 => DiskHealth.Unhealthy, _ => DiskHealth.Unknown }))];
        }
        catch (PlatformNotSupportedException)
        {
            // Older or trimmed Windows without the Storage namespace: report space, health stays Unknown.
            disks = null;
        }

        return new StorageSnapshot(volumes, disks);
    }
}

/// <summary>Battery capacities from root\wmi (laptops only).</summary>
public sealed class BatterySource : ISnapshotSource<BatterySnapshot>
{
    private const string Script = """
        $present = @(Get-CimInstance Win32_Battery).Count -gt 0
        $design = $null; $full = $null; $cycles = $null
        if ($present) {
            try { $design = (Get-CimInstance -Namespace root/wmi -ClassName BatteryStaticData | Select-Object -First 1).DesignedCapacity } catch { }
            try { $full = (Get-CimInstance -Namespace root/wmi -ClassName BatteryFullChargedCapacity | Select-Object -First 1).FullChargedCapacity } catch { }
            try { $cycles = (Get-CimInstance -Namespace root/wmi -ClassName BatteryCycleCount | Select-Object -First 1).CycleCount } catch { }
        }
        [pscustomobject]@{ present = $present; design = $design; full = $full; cycles = $cycles } | ConvertTo-Json -Compress
        """;

    public async Task<BatterySnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        long? Nullable(string name) => Long(json, name) is > 0 and var n ? n : null;
        var cycles = Nullable("cycles");
        return new BatterySnapshot(Bool(json, "present"), Nullable("design"), Nullable("full"), cycles is null ? null : (int)cycles.Value);
    }
}

/// <summary>Devices with a Device Manager problem code.</summary>
public sealed class DeviceSource : ISnapshotSource<DeviceSnapshot>
{
    private const string Script = """
        $all = @(Get-CimInstance Win32_PnPEntity -Property Name, PNPClass, ConfigManagerErrorCode, Present)
        $problems = $all | Where-Object { $_.ConfigManagerErrorCode -ne 0 -and $null -ne $_.ConfigManagerErrorCode } |
            ForEach-Object {
                [pscustomobject]@{
                    name = if ($_.Name) { "$($_.Name)".Trim() } else { 'Unknown device' }
                    cls = if ($_.PNPClass) { "$($_.PNPClass)" } else { 'Other' }
                    code = [int]$_.ConfigManagerErrorCode
                }
            }
        [pscustomobject]@{ present = $all.Count; problems = @($problems) } | ConvertTo-Json -Compress -Depth 3
        """;

    public async Task<DeviceSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        return new DeviceSnapshot(
            (int)Long(json, "present"),
            [.. Items(json, "problems").Select(p => new ProblemDevice(Str(p, "name"), Str(p, "cls"), (int)Long(p, "code")))]);
    }
}

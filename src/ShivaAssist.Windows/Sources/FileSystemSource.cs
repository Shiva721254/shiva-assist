using System.IO;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using ShivaAssist.Windows.Infrastructure;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Read-only NTFS dirty bit and volume health check for the Windows system drive.</summary>
public sealed class FileSystemSource : ISnapshotSource<FileSystemSnapshot>
{
    public async Task<FileSystemSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var drive = Environment.GetEnvironmentVariable("SystemDrive");
        if (string.IsNullOrWhiteSpace(drive) || drive.Length != 2 || drive[1] != ':')
        {
            drive = "C:";
        }

        if (!context.IsElevated)
        {
            return new FileSystemSnapshot(drive, null, null, NeedsAdmin: true, "Administrator permission required");
        }

        var (exit, output, error) = await ToolRunner.RunAsync(ToolRunner.System32("fsutil.exe"), ["dirty", "query", drive], cancellationToken);
        bool? dirty = exit == 0
            ? (output.Contains("NOT Dirty", StringComparison.OrdinalIgnoreCase) ? false
                : output.Contains("is Dirty", StringComparison.OrdinalIgnoreCase) ? true : null)
            : null;

        bool? volumeHealthy = null;
        var volumeSummary = string.Empty;
        try
        {
            var letter = drive[0];
            var json = await QueryAsync($$"""
                $v = Get-Volume -DriveLetter '{{letter}}' -ErrorAction Stop
                [pscustomobject]@{ health = "$($v.HealthStatus)"; op = "$($v.OperationalStatus)" } | ConvertTo-Json -Compress
                """, cancellationToken);
            var health = Str(json, "health");
            var op = Str(json, "op");
            if (health.Length > 0)
            {
                volumeHealthy = string.Equals(health, "Healthy", StringComparison.OrdinalIgnoreCase);
                volumeSummary = $"volume {health} ({op})";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            volumeHealthy = null;
        }

        var dirtySummary = dirty switch { false => "dirty bit clear", true => "dirty bit SET", null => $"fsutil exit {exit} {error}".Trim() };
        var summary = string.IsNullOrEmpty(volumeSummary) ? dirtySummary : $"{dirtySummary}; {volumeSummary}";
        return new FileSystemSnapshot(drive, dirty, volumeHealthy, NeedsAdmin: false, summary);
    }
}

/// <summary>Checks baseline Windows 11 hardware requirements on Windows 10 PCs (read-only).</summary>
public sealed class Win11ReadinessSource : ISnapshotSource<Win11ReadinessSnapshot>
{
    private const string Script = """
        $tpm2 = $null
        try {
            $t = Get-CimInstance -Namespace root/CIMV2/Security/MicrosoftTpm -ClassName Win32_Tpm -ErrorAction Stop | Select-Object -First 1
            if ($null -eq $t) { $tpm2 = $false }
            else { $tpm2 = "$($t.SpecVersion)" -match '^2\.' }
        } catch { $tpm2 = $null }
        $sb = $null
        try { $sb = [bool](Confirm-SecureBootUEFI -ErrorAction Stop) } catch { $sb = $null }
        $ram = [long](Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty TotalPhysicalMemory)
        [pscustomobject]@{ tpm2 = $tpm2; sb = $sb; ram = $ram } | ConvertTo-Json -Compress
        """;

    public async Task<Win11ReadinessSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        if (Environment.OSVersion.Version.Build >= OsCheck.FirstWindows11Build)
        {
            return new Win11ReadinessSnapshot(true, Environment.Is64BitOperatingSystem, true, true, 0, 0);
        }

        var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var totalDisk = new DriveInfo(drive).TotalSize;
        var json = await QueryAsync(Script, cancellationToken);
        return new Win11ReadinessSnapshot(
            AlreadyWindows11: false,
            Is64BitOs: Environment.Is64BitOperatingSystem,
            Tpm20Present: TryBool(json, "tpm2"),
            SecureBootEnabled: TryBool(json, "sb"),
            RamBytes: Long(json, "ram"),
            SystemDriveTotalBytes: totalDisk);
    }

    private static bool? TryBool(System.Text.Json.JsonElement json, string property) =>
        json.TryGetProperty(property, out var prop) && prop.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            ? prop.GetBoolean()
            : null;
}

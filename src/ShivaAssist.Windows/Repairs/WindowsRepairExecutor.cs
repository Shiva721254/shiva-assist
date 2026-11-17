using System.Globalization;
using System.Text;
using Microsoft.Win32;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Windows.Infrastructure;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.Windows.Repairs;

/// <summary>
/// Executes catalogue repairs on Windows. Every operation validates its own input again, because the same code
/// runs inside the elevated helper, which must not trust the job file blindly.
/// </summary>
public sealed class WindowsRepairExecutor(bool isElevated) : IRepairExecutor
{
    private static readonly TimeSpan TempAge = TimeSpan.FromDays(7);

    public async Task<RepairResult> ExecuteAsync(RepairJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var operation = RepairCatalogue.Find(job.OperationId)
            ?? throw new InvalidOperationException($"Operation '{job.OperationId}' is not in the repair catalogue.");
        if (operation.RequiresAdmin && !isElevated)
        {
            throw new UnauthorizedAccessException($"{operation.Id} needs administrator rights.");
        }

        if (operation.NeedsRestorePoint)
        {
            await EnsureOnMainsPowerAsync(cancellationToken);
            await CreateRestorePointAsync(operation, cancellationToken);
        }

        var freeBefore = SystemDriveFree();
        var result = operation.Id switch
        {
            RepairCatalogue.EmptyRecycleBin => await EmptyRecycleBinAsync(job, cancellationToken),
            RepairCatalogue.CleanTempFiles => CleanTempFiles(job),
            RepairCatalogue.CleanComponentStore => await CleanComponentStoreAsync(job, cancellationToken),
            RepairCatalogue.DisableStartupPrograms => DisableStartupPrograms(job),
            RepairCatalogue.RepairSystemFiles => await RepairSystemFilesAsync(job, cancellationToken),
            _ => throw new InvalidOperationException($"No implementation for {operation.Id}."),
        };

        return operation.Id.StartsWith("DISK.", StringComparison.Ordinal)
            ? result with { BytesFreed = Math.Max(0, SystemDriveFree() - freeBefore) }
            : result;
    }

    /// <summary>A long repair on battery can end in a half-repaired Windows; require the charger.</summary>
    private static async Task EnsureOnMainsPowerAsync(CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $b = Get-CimInstance Win32_Battery | Select-Object -First 1
            [pscustomobject]@{ battery = [bool]$b; status = if ($b) { [int]$b.BatteryStatus } else { 0 } } | ConvertTo-Json -Compress
            """, cancellationToken);

        // BatteryStatus 1 = discharging (running on battery).
        if (PowerShell.Bool(json, "battery") && PowerShell.Long(json, "status") == 1)
        {
            throw new InvalidOperationException("Connect the charger first: this repair must not run on battery.");
        }
    }

    /// <summary>
    /// Restore point before every Medium/High repair. Windows makes at most one per 24 hours by default; a recent one
    /// is then kept, which still protects the repair. When System Restore is switched off, the repair stops.
    /// </summary>
    private static async Task CreateRestorePointAsync(RepairOperation operation, CancellationToken cancellationToken)
    {
        var description = "Shiva Assist: " + operation.Id;
        var json = await PowerShell.QueryAsync($$"""
            $before = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue).Count
            try { Checkpoint-Computer -Description '{{description}}' -RestorePointType MODIFY_SETTINGS -WarningAction SilentlyContinue -ErrorAction Stop; $ok = $true }
            catch { $ok = $false }
            $points = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue)
            $recent = $points | Where-Object { $_.ConvertToDateTime($_.CreationTime) -gt (Get-Date).AddHours(-24) }
            [pscustomobject]@{ ok = $ok; recent = [bool]$recent } | ConvertTo-Json -Compress
            """, cancellationToken);

        if (!PowerShell.Bool(json, "recent"))
        {
            throw new InvalidOperationException("No restore point could be made. Turn on System Protection for the Windows drive first (Control Panel → System → System Protection).");
        }
    }

    /// <summary>DISM /RestoreHealth, then sfc /scannow. Both are Microsoft's supported repair tools.</summary>
    private static async Task<RepairResult> RepairSystemFilesAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var (dismExit, dismOutput, _) = await ToolRunner.RunAsync(
            ToolRunner.System32("dism.exe"), ["/Online", "/Cleanup-Image", "/RestoreHealth", "/English"], cancellationToken);
        var dismOk = dismExit == 0 && dismOutput.Contains("restore operation completed successfully", StringComparison.OrdinalIgnoreCase);

        // sfc writes UTF-16; its language follows Windows, so the exit code and known English phrases are both kept.
        var (sfcExit, sfcOutput, _) = await ToolRunner.RunAsync(ToolRunner.System32("sfc.exe"), ["/scannow"], cancellationToken, Encoding.Unicode);
        var sfc = sfcOutput.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase) ? "no integrity violations"
            : sfcOutput.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase) ? "corrupt files repaired"
            : sfcOutput.Contains("unable to fix", StringComparison.OrdinalIgnoreCase) ? "some files could not be repaired"
            : string.Create(CultureInfo.InvariantCulture, $"exit code {sfcExit}");

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = dismOk,
            Detail = string.Create(CultureInfo.InvariantCulture, $"DISM RestoreHealth: {(dismOk ? "completed" : $"exit code {dismExit}")}; SFC: {sfc}"),
        };
    }

    private static long SystemDriveFree() => new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace;

    private static async Task<RepairResult> EmptyRecycleBinAsync(RepairJob job, CancellationToken cancellationToken)
    {
        // Clear-RecycleBin fails on an already empty bin; that is not an error.
        await PowerShell.QueryAsync("""
            try { Clear-RecycleBin -Force -ErrorAction Stop } catch { if ($_.FullyQualifiedErrorId -notmatch 'FailedToClearRecycleBin') { throw } }
            [pscustomobject]@{ ok = $true } | ConvertTo-Json -Compress
            """, cancellationToken);
        return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = true, Detail = "Recycle Bin emptied" };
    }

    /// <summary>Deletes files older than 7 days in the user's temp folder. Never follows links, skips locked files.</summary>
    private static RepairResult CleanTempFiles(RepairJob job)
    {
        var folder = job.Items.Count == 1 ? job.Items[0] : throw new ArgumentException("Exactly one temp folder expected.");
        if (!IsUserTempFolder(folder))
        {
            throw new ArgumentException("Only a user's AppData\\Local\\Temp folder may be cleaned.");
        }

        var cutoff = DateTime.UtcNow - TempAge;
        int deleted = 0, skipped = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            if (file.LastWriteTimeUtc > cutoff || file.CreationTimeUtc > cutoff)
            {
                continue;
            }

            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // in use or protected: leave it
            }
        }

        // Remove folders that are now empty and old; deepest first.
        foreach (var dir in new DirectoryInfo(folder).EnumerateDirectories("*", options).OrderByDescending(d => d.FullName.Length))
        {
            try
            {
                if (dir.LastWriteTimeUtc < cutoff && !dir.EnumerateFileSystemInfos().Any())
                {
                    dir.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave it.
            }
        }

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = true,
            Detail = string.Create(CultureInfo.InvariantCulture, $"Deleted {deleted} temporary files older than 7 days; skipped {skipped} in use"),
        };
    }

    /// <summary>A real temp folder: ...\Users\&lt;name&gt;\AppData\Local\Temp, not a link, on a fixed drive.</summary>
    public static bool IsUserTempFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            var info = new DirectoryInfo(full);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            var parts = full.Split('\\');
            return parts.Length >= 6
                && parts[^1].Equals("Temp", StringComparison.OrdinalIgnoreCase)
                && parts[^2].Equals("Local", StringComparison.OrdinalIgnoreCase)
                && parts[^3].Equals("AppData", StringComparison.OrdinalIgnoreCase)
                && parts[^5].Equals("Users", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<RepairResult> CleanComponentStoreAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var (exit, output, _) = await ToolRunner.RunAsync(
            ToolRunner.System32("dism.exe"), ["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/English"], cancellationToken);
        var completed = exit == 0 && output.Contains("completed successfully", StringComparison.OrdinalIgnoreCase);
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = completed,
            Detail = completed ? "DISM StartComponentCleanup completed" : string.Create(CultureInfo.InvariantCulture, $"DISM exit code {exit}"),
        };
    }

    /// <summary>
    /// Writes Task Manager's "disabled" marker for each selected entry. The previous value is returned as rollback
    /// data, so the exact earlier state can be restored.
    /// </summary>
    private RepairResult DisableStartupPrograms(RepairJob job)
    {
        var rollback = new List<string>();
        int disabled = 0;
        foreach (var reference in job.Items)
        {
            var (hive, subKey, valueName) = StartupSource.ApprovedLocation(reference)
                ?? throw new ArgumentException($"Unknown startup entry '{reference}'.");
            if (hive == Registry.LocalMachine && !isElevated)
            {
                throw new UnauthorizedAccessException("Programs registered for all users need administrator rights.");
            }

            using var key = hive.CreateSubKey(subKey, writable: true);
            var previous = key.GetValue(valueName) as byte[];
            rollback.Add(reference + "|" + (previous is null ? "none" : Convert.ToHexString(previous)));

            var marker = new byte[12];
            marker[0] = 0x03; // odd first byte = disabled; bytes 4-11 = when (FILETIME), as Task Manager writes it
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(marker, 4);
            key.SetValue(valueName, marker, RegistryValueKind.Binary);
            disabled++;
        }

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = disabled > 0,
            Detail = string.Create(CultureInfo.InvariantCulture, $"Switched off {disabled} startup program(s)"),
            Rollback = rollback,
        };
    }
}

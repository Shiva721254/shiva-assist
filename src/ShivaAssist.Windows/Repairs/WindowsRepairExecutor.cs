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
public sealed partial class WindowsRepairExecutor(bool isElevated) : IRepairExecutor
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
            RepairCatalogue.CleanUpdateCache => await CleanUpdateCacheAsync(job, cancellationToken),
            RepairCatalogue.FlushDns => await RunToolAsync(job, ToolRunner.System32("ipconfig.exe"), ["/flushdns"], "DNS cache cleared", cancellationToken),
            RepairCatalogue.RenewAddress => await RenewAddressAsync(job, cancellationToken),
            RepairCatalogue.UpdateDefender => await RunToolAsync(job, DefenderCommandLine(), ["-SignatureUpdate"], "Microsoft Defender definitions updated", cancellationToken),
            RepairCatalogue.RestartNow => await RunToolAsync(job, ToolRunner.System32("shutdown.exe"), ["/r", "/t", "120", "/c", "Shiva Assist: restarting in 2 minutes to finish Windows updates."], "Restart scheduled in 2 minutes", cancellationToken),
            RepairCatalogue.ResetUpdateComponents => await ResetUpdateComponentsAsync(job, cancellationToken),
            RepairCatalogue.RestoreStartupPrograms => RestoreStartupPrograms(job),
            RepairCatalogue.UpdateSoftware => await UpdateSoftwareAsync(job, cancellationToken),
            RepairCatalogue.UninstallSoftware => await UninstallSoftwareAsync(job, cancellationToken),
            RepairCatalogue.CleanSoftwareLeftovers => UninstallerService.Clean(job),
            RepairCatalogue.RestoreSoftwareLeftovers => UninstallerService.Restore(job),
            RepairCatalogue.ResetPrintSpooler => await ResetPrintSpoolerAsync(job, cancellationToken),
            RepairCatalogue.SyncClock => await SyncClockAsync(job, cancellationToken),
            RepairCatalogue.CleanBrowserCache => CleanBrowserCache(job),
            RepairCatalogue.RepairAudioService => await RepairServicesAsync(job, ["AudioEndpointBuilder", "Audiosrv"], "Automatic", cancellationToken),
            RepairCatalogue.RepairSearchService => await RepairServicesAsync(job, ["WSearch"], "AutomaticDelayed", cancellationToken),
            RepairCatalogue.ResetNetworkStack => await ResetNetworkStackAsync(job, cancellationToken),
            _ => throw new InvalidOperationException($"No implementation for {operation.Id}."),
        };

        result = result with { Undoes = job.Undoes };
        return operation.Id.StartsWith("DISK.", StringComparison.Ordinal)
            ? result with { BytesFreed = Math.Max(result.BytesFreed, SystemDriveFree() - freeBefore) } // Windows may use space meanwhile

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
    /// Restore point before every Medium/High repair. When System Protection is off for the Windows drive, it is
    /// switched on first (the customer approved the repair, and the restore point is part of it). Windows makes at
    /// most one restore point per 24 hours; a recent one is kept, which still protects the repair.
    /// </summary>
    private static async Task CreateRestorePointAsync(RepairOperation operation, CancellationToken cancellationToken)
    {
        var description = "Shiva Assist: " + operation.Id;
        var json = await PowerShell.QueryAsync($$"""
            $drive = $env:SystemDrive + '\'
            # Idempotent: switches System Protection on for the Windows drive, or leaves it on.
            try { Enable-ComputerRestore -Drive $drive -ErrorAction Stop } catch { }
            try { Checkpoint-Computer -Description '{{description}}' -RestorePointType MODIFY_SETTINGS -WarningAction SilentlyContinue -ErrorAction Stop } catch { }
            $points = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue)
            $recent = $points | Where-Object { $_.ConvertToDateTime($_.CreationTime) -gt (Get-Date).AddHours(-24) }
            [pscustomobject]@{ recent = [bool]$recent } | ConvertTo-Json -Compress
            """, cancellationToken);

        if (!PowerShell.Bool(json, "recent"))
        {
            throw new InvalidOperationException("No restore point could be made, so the repair was stopped. Nothing was changed.");
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

    /// <summary>Runs one Microsoft tool; exit code 0 means done.</summary>
    private static async Task<RepairResult> RunToolAsync(RepairJob job, string executable, string[] arguments, string done, CancellationToken cancellationToken)
    {
        var (exit, output, error) = await ToolRunner.RunAsync(executable, arguments, cancellationToken);
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = exit == 0,
            Detail = exit == 0 ? done : string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(executable)} ended with exit code {exit}: {Shorten(error.Length > 0 ? error : output)}"),
        };
    }

    private static string DefenderCommandLine() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");

    private static string Shorten(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    /// <summary>ipconfig /release then /renew: the router hands out a fresh address.</summary>
    /// <summary>
    /// Stops the Print Spooler, removes only queued job files (*.SPL, *.SHD, *.TMP) from the system spool folder,
    /// sets a disabled spooler back to automatic start and starts it again. Verified by repeating the print check.
    /// </summary>
    private static async Task<RepairResult> ResetPrintSpoolerAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $svc = Get-Service -Name Spooler -ErrorAction Stop
            $previous = "$($svc.StartType)"
            if ($svc.StartType -eq 'Disabled') { Set-Service -Name Spooler -StartupType Automatic }
            Stop-Service -Name Spooler -Force -ErrorAction SilentlyContinue
            try { (Get-Service -Name Spooler).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20)) } catch { }
            $queue = Join-Path $env:SystemRoot 'System32\spool\PRINTERS'
            $removed = 0
            Get-ChildItem -LiteralPath $queue -File -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in '.SPL', '.SHD', '.TMP' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } |
                ForEach-Object {
                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
                    if (-not (Test-Path -LiteralPath $_.FullName)) { $removed++ }
                }
            Start-Service -Name Spooler -ErrorAction SilentlyContinue
            try { (Get-Service -Name Spooler).WaitForStatus('Running', [TimeSpan]::FromSeconds(20)) } catch { }
            $running = (Get-Service -Name Spooler).Status -eq 'Running'
            [pscustomobject]@{ previous = $previous; removed = $removed; running = $running } | ConvertTo-Json -Compress
            """, cancellationToken);
        var running = PowerShell.Bool(json, "running");
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = running,
            Detail = string.Create(CultureInfo.InvariantCulture,
                $"Print Spooler {(running ? "running again" : "did not start")}; removed {PowerShell.Long(json, "removed")} queued job file(s); start type was {PowerShell.Str(json, "previous")}"),
        };
    }

    /// <summary>
    /// Re-enables W32Time (manual start, as Windows ships it) when disabled, starts it and forces a resync. Only when
    /// Windows has no time source at all is the standard Microsoft server configured. Verified by measuring again.
    /// </summary>
    private static async Task<RepairResult> SyncClockAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $w32tm = "$env:SystemRoot\System32\w32tm.exe"
            $svc = Get-Service -Name W32Time -ErrorAction Stop
            $previous = "$($svc.StartType)"
            if ($previous -eq 'Disabled') { Set-Service -Name W32Time -StartupType Manual }
            if ($svc.Status -ne 'Running') { Start-Service -Name W32Time -ErrorAction Stop }
            $out = & $w32tm /resync /force 2>&1
            $ok = $LASTEXITCODE -eq 0
            $configured = $false
            if (-not $ok) {
                & $w32tm /config /manualpeerlist:time.windows.com,0x9 /syncfromflags:manual /update 2>&1 | Out-Null
                $configured = $true
                Start-Sleep -Seconds 2
                $out = & $w32tm /resync /force 2>&1
                $ok = $LASTEXITCODE -eq 0
            }
            [pscustomobject]@{ ok = $ok; previous = $previous; configured = $configured } | ConvertTo-Json -Compress
            """, cancellationToken);
        var ok = PowerShell.Bool(json, "ok");
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = ok,
            Detail = string.Create(CultureInfo.InvariantCulture,
                $"w32tm /resync {(ok ? "succeeded" : "failed")}; service start type was {PowerShell.Str(json, "previous")}{(PowerShell.Bool(json, "configured") ? "; time source set to time.windows.com" : string.Empty)}"),
        };
    }

    /// <summary>
    /// Switches fixed, built-in Windows services back on (start type as Windows ships it) and (re)starts them in the
    /// given order. The service names come from the catalogue switch above, never from the job file.
    /// </summary>
    private static async Task<RepairResult> RepairServicesAsync(RepairJob job, string[] services, string startType, CancellationToken cancellationToken)
    {
        var list = string.Join(",", services.Select(name => "'" + name + "'"));
        var json = await PowerShell.QueryAsync($$"""
            $previous = @()
            foreach ($name in @({{list}})) {
                $svc = Get-Service -Name $name -ErrorAction Stop
                $previous += "$($name)=$($svc.StartType)"
                if ("$($svc.StartType)" -eq 'Disabled') {
                    if ('{{startType}}' -eq 'AutomaticDelayed') { & "$env:SystemRoot\System32\sc.exe" config $name start= delayed-auto | Out-Null }
                    else { Set-Service -Name $name -StartupType Automatic }
                }
            }
            foreach ($name in @({{list}})) {
                $svc = Get-Service -Name $name
                if ($svc.Status -eq 'Running') { Restart-Service -Name $name -Force -ErrorAction SilentlyContinue } else { Start-Service -Name $name -ErrorAction SilentlyContinue }
                try { (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(20)) } catch { }
            }
            $running = @(foreach ($name in @({{list}})) { (Get-Service -Name $name).Status -eq 'Running' }) -notcontains $false
            [pscustomobject]@{ running = $running; previous = ($previous -join '; ') } | ConvertTo-Json -Compress
            """, cancellationToken);
        var running = PowerShell.Bool(json, "running");
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = running,
            Detail = string.Create(CultureInfo.InvariantCulture, $"{string.Join(", ", services)} {(running ? "running" : "did not start")}; previous start types: {PowerShell.Str(json, "previous")}"),
        };
    }

    /// <summary>netsh winsock reset + netsh int ip reset (Microsoft's documented network reset). Takes effect after a restart.</summary>
    private static async Task<RepairResult> ResetNetworkStackAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var netsh = ToolRunner.System32("netsh.exe");
        var (winsockExit, winsockOutput, _) = await ToolRunner.RunAsync(netsh, ["winsock", "reset"], cancellationToken);
        var (ipExit, ipOutput, _) = await ToolRunner.RunAsync(netsh, ["int", "ip", "reset"], cancellationToken);
        var ok = winsockExit == 0 && ipExit == 0;
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = ok,
            Detail = string.Create(CultureInfo.InvariantCulture,
                $"netsh winsock reset: exit {winsockExit}; netsh int ip reset: exit {ipExit}; restart needed. {Shorten(winsockOutput + " " + ipOutput)}"),
        };
    }

    private static async Task<RepairResult> RenewAddressAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var ipconfig = ToolRunner.System32("ipconfig.exe");
        await ToolRunner.RunAsync(ipconfig, ["/release"], cancellationToken);
        return await RunToolAsync(job, ipconfig, ["/renew"], "New network address received", cancellationToken);
    }

    /// <summary>
    /// Update download cache, Delivery Optimization cache and crash dumps. Update services that were running are
    /// stopped while their folder is emptied and started again afterwards.
    /// </summary>
    private static async Task<RepairResult> CleanUpdateCacheAsync(RepairJob job, CancellationToken cancellationToken)
    {
        await PowerShell.QueryAsync("""
            $running = @(Get-Service wuauserv, bits -ErrorAction SilentlyContinue | Where-Object Status -eq 'Running')
            try {
                $running | Stop-Service -Force -ErrorAction SilentlyContinue
                Get-ChildItem "$env:SystemRoot\SoftwareDistribution\Download" -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            } finally {
                $running | Start-Service -ErrorAction SilentlyContinue
            }
            try { Delete-DeliveryOptimizationCache -Force -ErrorAction Stop | Out-Null } catch { }
            $dumps = @(Get-ChildItem "$env:SystemRoot\Minidump", "$env:SystemRoot\LiveKernelReports" -Filter *.dmp -Recurse -Force -ErrorAction SilentlyContinue)
            $dumps += @(Get-Item "$env:SystemRoot\MEMORY.DMP" -Force -ErrorAction SilentlyContinue)
            $dumps | Remove-Item -Force -ErrorAction SilentlyContinue
            [pscustomobject]@{ ok = $true } | ConvertTo-Json -Compress
            """, cancellationToken);
        return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = true, Detail = "Update download cache, Delivery Optimization cache and crash dumps removed" };
    }

    /// <summary>
    /// The classic Windows Update reset: stop the services, rename SoftwareDistribution and catroot2 (kept for
    /// rollback), start the services and ask Windows to scan again.
    /// </summary>
    private static async Task<RepairResult> ResetUpdateComponentsAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $services = 'wuauserv', 'bits', 'cryptsvc'
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $renamed = @()
            try {
                foreach ($folder in "$env:SystemRoot\SoftwareDistribution", "$env:SystemRoot\System32\catroot2") {
                    if (-not (Test-Path $folder)) { continue }
                    $target = "$(Split-Path $folder -Leaf).shiva-$stamp"
                    # The services restart themselves on demand, so stop them again right before each rename.
                    for ($try = 1; $try -le 3; $try++) {
                        Stop-Service $services -Force -ErrorAction SilentlyContinue
                        try { Rename-Item $folder $target -ErrorAction Stop; $renamed += "$folder -> $target"; break }
                        catch { if ($try -eq 3) { throw } ; Start-Sleep -Seconds 2 }
                    }
                }
            } finally {
                Start-Service $services -ErrorAction SilentlyContinue
            }
            $uso = "$env:SystemRoot\System32\UsoClient.exe"
            if (Test-Path $uso) { & $uso StartScan | Out-Null }
            [pscustomobject]@{ renamed = $renamed } | ConvertTo-Json -Compress
            """, cancellationToken);
        var renamed = PowerShell.Items(json, "renamed").Select(e => e.GetString() ?? string.Empty).Where(r => r.Length > 0).ToList();
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = renamed.Count > 0,
            Detail = renamed.Count > 0 ? "Windows Update reset; it now rebuilds its folders and checks for updates" : "No Windows Update folder could be set aside",
            Rollback = renamed,
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
    /// <summary>
    /// Deletes files inside the fixed browser cache folders only (see <see cref="BrowserCaches"/>). Files in use
    /// (an open browser) are skipped; folders themselves stay so the browser keeps working.
    /// </summary>
    private static RepairResult CleanBrowserCache(RepairJob job)
    {
        int deleted = 0, skipped = 0;
        long bytes = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        var folders = BrowserCaches.Folders();
        foreach (var folder in folders)
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                try
                {
                    var length = file.Length;
                    file.Delete();
                    deleted++;
                    bytes += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = true,
            Detail = string.Create(CultureInfo.InvariantCulture, $"Browser cache: deleted {deleted} files in {folders.Count} cache folders; skipped {skipped} in use"),
            BytesFreed = bytes,
        };
    }

    private static RepairResult CleanTempFiles(RepairJob job)
    {
        var folder = job.Items.Count == 1 ? job.Items[0] : throw new ArgumentException("Exactly one temp folder expected.");
        if (!IsUserTempFolder(folder))
        {
            throw new ArgumentException("Only a user's AppData\\Local\\Temp folder may be cleaned.");
        }

        var cutoff = DateTime.UtcNow - TempAge;
        int deleted = 0, skipped = 0;
        long bytes = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            if (file.LastWriteTimeUtc > cutoff || file.CreationTimeUtc > cutoff)
            {
                continue;
            }

            try
            {
                var length = file.Length;
                file.Delete();
                deleted++;
                bytes += length;
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
            BytesFreed = bytes,
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
        var enabled = StartupSource.ReadEntries(includeDisabled: false).Entries.Select(e => e.Reference).ToHashSet(StringComparer.Ordinal);
        if (job.Items.Count == 0 || job.Items.Distinct(StringComparer.Ordinal).Count() != job.Items.Count
            || job.Items.Any(reference => !enabled.Contains(reference)))
        {
            throw new InvalidOperationException("The selected startup entry changed. Refresh the startup list before trying again.");
        }

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

        var verified = false;
        try
        {
            var remaining = StartupSource.ReadEntries(includeDisabled: false).Entries.Select(e => e.Reference).ToHashSet(StringComparer.Ordinal);
            verified = job.Items.All(reference => !remaining.Contains(reference));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Keep the recorded rollback even if a subsequent registry read fails.
        }
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = disabled > 0,
            Verdict = verified ? RepairVerdict.Fixed : RepairVerdict.Inconclusive,
            Detail = string.Create(CultureInfo.InvariantCulture, $"Switched off {disabled} startup program(s)"),
            Rollback = rollback,
        };
    }

    /// <summary>
    /// Undo of <see cref="DisableStartupPrograms"/>: each item is "Location|Name|previous value" as recorded in
    /// its rollback list; the previous StartupApproved value is written back, or removed when there was none.
    /// </summary>
    private RepairResult RestoreStartupPrograms(RepairJob job)
    {
        int restored = 0;
        foreach (var item in job.Items)
        {
            var cut = item.LastIndexOf('|');
            var reference = cut > 0 ? item[..cut] : throw new ArgumentException($"Unknown rollback entry '{item}'.");
            var previous = item[(cut + 1)..];
            var (hive, subKey, valueName) = StartupSource.ApprovedLocation(reference)
                ?? throw new ArgumentException($"Unknown startup entry '{reference}'.");
            if (hive == Registry.LocalMachine && !isElevated)
            {
                throw new UnauthorizedAccessException("Programs registered for all users need administrator rights.");
            }

            using var key = hive.CreateSubKey(subKey, writable: true);
            if (previous == "none")
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(valueName, Convert.FromHexString(previous), RegistryValueKind.Binary);
            }

            restored++;
        }

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = restored > 0,
            Detail = string.Create(CultureInfo.InvariantCulture, $"Switched {restored} startup program(s) on again"),
        };
    }
}

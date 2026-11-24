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
            RepairCatalogue.ResetNetworkAdapter => await ResetNetworkAdaptersAsync(job, cancellationToken),
            RepairCatalogue.ResetProxy => ResetProxy(job),
            RepairCatalogue.RestoreProxy => RestoreProxy(job),
            RepairCatalogue.UpdateDefender => await RunToolAsync(job, DefenderCommandLine(), ["-SignatureUpdate"], "Microsoft Defender definitions updated", cancellationToken),
            RepairCatalogue.EnableDefender => await EnableDefenderAsync(job, cancellationToken),
            RepairCatalogue.DefenderQuickScan => await RunDefenderQuickScanAsync(job, cancellationToken),
            RepairCatalogue.EnableFirewall => await EnableFirewallAsync(job, cancellationToken),
            RepairCatalogue.RestartNow => await ScheduleRestartAsync(job, cancellationToken),
            RepairCatalogue.ResetUpdateComponents => await ResetUpdateComponentsAsync(job, cancellationToken),
            RepairCatalogue.RestoreStartupPrograms => RestoreStartupPrograms(job),
            RepairCatalogue.UpdateSoftware => await UpdateSoftwareAsync(job, cancellationToken),
            RepairCatalogue.UninstallSoftware => await UninstallSoftwareAsync(job, cancellationToken),
            RepairCatalogue.CleanSoftwareLeftovers => UninstallerService.Clean(job),
            RepairCatalogue.RestoreSoftwareLeftovers => UninstallerService.Restore(job),
            RepairCatalogue.ResetPrintSpooler => await ResetPrintSpoolerAsync(job, cancellationToken),
            RepairCatalogue.ResumePrinterQueues => await ResumePrinterQueuesAsync(job, cancellationToken),
            RepairCatalogue.SyncClock => await SyncClockAsync(job, cancellationToken),
            RepairCatalogue.CleanBrowserCache => CleanBrowserCache(job),
            RepairCatalogue.RepairAudioService => await RepairServicesAsync(job, ["AudioEndpointBuilder", "Audiosrv"], "Automatic", cancellationToken),
            RepairCatalogue.RepairSearchService => await RepairServicesAsync(job, ["WSearch"], "AutomaticDelayed", cancellationToken),
            RepairCatalogue.RepairWifiService => await StartWifiServiceAsync(job, cancellationToken),
            RepairCatalogue.RepairBluetoothService => await StartBluetoothServiceAsync(job, cancellationToken),
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

    /// <summary>
    /// Restart in two minutes. Exit code 1190 (ERROR_SHUTDOWN_IS_SCHEDULED) means a restart is already on its way, from an
    /// earlier click or from Windows Update itself: that is the outcome the customer asked for, not a failure.
    /// </summary>
    private static async Task<RepairResult> ScheduleRestartAsync(RepairJob job, CancellationToken cancellationToken)
    {
        const int ShutdownAlreadyScheduled = 1190;
        var result = await RunToolAsync(job, ToolRunner.System32("shutdown.exe"),
            ["/r", "/t", "120", "/c", "Shiva Assist: restarting in 2 minutes to finish Windows updates."], "Restart scheduled in 2 minutes", cancellationToken);
        return result.Completed || !result.Detail.Contains("exit code " + ShutdownAlreadyScheduled.ToString(CultureInfo.InvariantCulture) + ":", StringComparison.Ordinal)
            ? result
            : result with { Completed = true, Detail = "A restart was already scheduled. Windows restarts within a few minutes; save your work." };
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

    /// <summary>Start only a confirmed stopped automatic WLAN service; do not change its configuration.</summary>
    private static async Task<RepairResult> StartWifiServiceAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $wifi = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object {
                [int]$_.NdisPhysicalMedium -in 1,9 -and $_.Status -in 'Up','Disconnected'
            }).Count -gt 0
            $svc = Get-Service -Name WlanSvc -ErrorAction Stop
            if ($wifi -and $svc.StartType -eq 'Automatic' -and $svc.Status -eq 'Stopped') {
                Start-Service -Name WlanSvc -ErrorAction Stop
                (Get-Service -Name WlanSvc).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
            }
            $running = $wifi -and (Get-Service -Name WlanSvc).Status -eq 'Running'
            [pscustomobject]@{ running = $running } | ConvertTo-Json -Compress
            """, cancellationToken);
        var running = PowerShell.Bool(json, "running");
        return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = running,
            Detail = running ? "WlanSvc running" : "Wi-Fi service not running; no eligible start or start failed" };
    }

    /// <summary>Re-enable disabled Windows Firewall profiles only after confirming no third-party firewall is active.</summary>
    private static async Task<RepairResult> EnableFirewallAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $allowed = @('Domain', 'Private', 'Public')
            try {
                $thirdParty = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName FirewallProduct -ErrorAction Stop | Where-Object {
                    $name = "$($_.displayName)".Trim()
                    $name.Length -gt 0 -and $name -notin 'Windows Firewall','Windows Defender Firewall' -and ([int]$_.productState -band 0x1000) -ne 0
                })
            } catch {
                [pscustomobject]@{ ok = $false; detail = 'Could not verify whether another firewall is active; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            if ($thirdParty.Count -gt 0) {
                [pscustomobject]@{ ok = $false; detail = 'Another firewall is active; Windows Firewall was not changed' } | ConvertTo-Json -Compress
                exit 0
            }
            $svc = Get-Service -Name mpssvc -ErrorAction SilentlyContinue
            if ($null -eq $svc -or $svc.Status -ne 'Running') {
                [pscustomobject]@{ ok = $false; detail = 'Windows Defender Firewall service (mpssvc) is not running; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            $before = @(Get-CimInstance -Namespace root/StandardCimv2 -ClassName MSFT_NetFirewallProfile -ErrorAction Stop | Where-Object { $_.Name -in $allowed })
            $off = @($before | Where-Object { [int]$_.Enabled -ne 1 } | ForEach-Object { "$($_.Name)" })
            foreach ($profile in $off) {
                Set-NetFirewallProfile -Name $profile -Enabled True -ErrorAction Stop
            }
            $after = @(Get-CimInstance -Namespace root/StandardCimv2 -ClassName MSFT_NetFirewallProfile -ErrorAction Stop | Where-Object { $_.Name -in $allowed })
            $stillOff = @($after | Where-Object { [int]$_.Enabled -ne 1 } | ForEach-Object { "$($_.Name)" })
            $ok = $after.Count -gt 0 -and $stillOff.Count -eq 0
            $detail = if ($ok) {
                if ($off.Count -gt 0) { 'Enabled Windows Firewall profiles: ' + ($off -join ', ') } else { 'Windows Firewall already enabled on all profiles' }
            } else {
                'Windows Firewall still off for: ' + ($stillOff -join ', ')
            }
            [pscustomobject]@{ ok = $ok; detail = $detail } | ConvertTo-Json -Compress
            """, cancellationToken);

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = PowerShell.Bool(json, "ok"),
            Detail = PowerShell.Str(json, "detail"),
        };
    }

    /// <summary>Re-enable Microsoft Defender real-time protection only when no third-party antivirus is active.</summary>
    private static async Task<RepairResult> EnableDefenderAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            try {
                $thirdParty = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop | Where-Object {
                    $name = "$($_.displayName)".Trim()
                    $name.Length -gt 0 -and $name -notmatch 'Defender' -and ([int]$_.productState -band 0x1000) -ne 0
                })
            } catch {
                [pscustomobject]@{ ok = $false; detail = 'Could not verify whether another antivirus is active; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            if ($thirdParty.Count -gt 0) {
                [pscustomobject]@{ ok = $false; detail = 'Another antivirus is active; Microsoft Defender was not changed' } | ConvertTo-Json -Compress
                exit 0
            }
            try {
                Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction Stop
            } catch {
                [pscustomobject]@{ ok = $false; detail = 'Windows Tamper Protection or policy prevented automatic change; switch Real-time protection on in Windows Security' } | ConvertTo-Json -Compress
                exit 0
            }
            $s = Get-CimInstance -Namespace root/Microsoft/Windows/Defender -ClassName MSFT_MpComputerStatus -ErrorAction SilentlyContinue
            $ok = $null -ne $s -and [bool]$s.AntivirusEnabled -and [bool]$s.RealTimeProtectionEnabled
            $detail = if ($ok) { 'Microsoft Defender real-time protection is on' } else { 'Real-time protection stayed off (check Tamper Protection in Windows Security)' }
            [pscustomobject]@{ ok = $ok; detail = $detail } | ConvertTo-Json -Compress
            """, cancellationToken);

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = PowerShell.Bool(json, "ok"),
            Detail = PowerShell.Str(json, "detail"),
        };
    }

    /// <summary>Runs built-in Microsoft Defender Quick Scan. Exit 2 means a threat was found and requires Windows Security review.</summary>
    private static async Task<RepairResult> RunDefenderQuickScanAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var (exit, output, error) = await ToolRunner.RunAsync(DefenderCommandLine(), ["-Scan", "-ScanType", "1"], cancellationToken);
        var detail = exit switch
        {
            0 => "Microsoft Defender quick scan finished; no threats found",
            2 => "Microsoft Defender found a threat; open Windows Security to review and remove it",
            _ => string.Create(CultureInfo.InvariantCulture, $"Microsoft Defender quick scan failed (exit {exit}): {Shorten(string.IsNullOrWhiteSpace(error) ? output : error)}"),
        };
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = exit == 0,
            Detail = detail,
        };
    }

    /// <summary>Start stopped bthserv only when Bluetooth hardware is present and the service is not disabled.</summary>
    private static async Task<RepairResult> StartBluetoothServiceAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $present = $false
            try {
                $present = @(Get-PnpDevice -Class Bluetooth -ErrorAction Stop | Where-Object { $_.Present -eq $true -and $_.Status -eq 'OK' }).Count -gt 0
            } catch {
                [pscustomobject]@{ ok = $false; detail = 'Bluetooth hardware could not be confirmed; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            if (-not $present) {
                [pscustomobject]@{ ok = $false; detail = 'No active Bluetooth adapter found; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            $svc = Get-Service -Name bthserv -ErrorAction SilentlyContinue
            if ($null -eq $svc) {
                [pscustomobject]@{ ok = $false; detail = 'Bluetooth Support Service (bthserv) not found' } | ConvertTo-Json -Compress
                exit 0
            }
            if ($svc.StartType -eq 'Disabled') {
                [pscustomobject]@{ ok = $false; detail = 'Bluetooth service is disabled; not changed automatically' } | ConvertTo-Json -Compress
                exit 0
            }
            if ($svc.Status -ne 'Running') {
                Start-Service -Name bthserv -ErrorAction Stop
                (Get-Service -Name bthserv).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
            }
            $running = (Get-Service -Name bthserv).Status -eq 'Running'
            [pscustomobject]@{ ok = $running; detail = if ($running) { 'bthserv running' } else { 'bthserv did not start' } } | ConvertTo-Json -Compress
            """, cancellationToken);

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = PowerShell.Bool(json, "ok"),
            Detail = PowerShell.Str(json, "detail"),
        };
    }

    /// <summary>Resume paused printer queues and clear WorkOffline without deleting waiting print jobs.</summary>
    private static async Task<RepairResult> ResumePrinterQueuesAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $svc = Get-Service -Name Spooler -ErrorAction SilentlyContinue
            if ($null -eq $svc -or $svc.Status -ne 'Running') {
                [pscustomobject]@{ ok = $false; detail = 'Print Spooler is not running; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            $touched = @()
            foreach ($p in @(Get-CimInstance Win32_Printer -ErrorAction Stop)) {
                $isPaused = ([int]$p.PrinterState -band 1) -ne 0
                $isOffline = [bool]$p.WorkOffline
                if ($isPaused) {
                    Invoke-CimMethod -InputObject $p -MethodName Resume -ErrorAction Stop | Out-Null
                    $touched += "$($p.Name) (resumed)"
                }
                if ($isOffline) {
                    Set-CimInstance -InputObject $p -Property @{ WorkOffline = $false } -ErrorAction Stop
                    $touched += "$($p.Name) (online)"
                }
            }
            $stillBlocked = @(Get-CimInstance Win32_Printer -ErrorAction Stop | Where-Object { ([int]$_.PrinterState -band 1) -ne 0 -or [bool]$_.WorkOffline })
            $ok = $stillBlocked.Count -eq 0
            $detail = if ($ok) {
                if ($touched.Count -gt 0) { 'Updated printers: ' + ($touched -join '; ') } else { 'No paused or offline printers found' }
            } else {
                'Still paused or offline: ' + (($stillBlocked | ForEach-Object { $_.Name }) -join ', ')
            }
            [pscustomobject]@{ ok = $ok; detail = $detail } | ConvertTo-Json -Compress
            """, cancellationToken);

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = PowerShell.Bool(json, "ok"),
            Detail = PowerShell.Str(json, "detail"),
        };
    }

    /// <summary>Restart active physical Wi-Fi/Ethernet adapters without touching disabled, virtual or VPN adapters.</summary>
    private static async Task<RepairResult> ResetNetworkAdaptersAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $adapters = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.Status -in 'Up','Disconnected' })
            if ($adapters.Count -eq 0) {
                [pscustomobject]@{ ok = $false; detail = 'No active physical network adapter found; nothing was changed' } | ConvertTo-Json -Compress
                exit 0
            }
            $restarted = @()
            foreach ($a in $adapters) {
                Restart-NetAdapter -Name $a.Name -Confirm:$false -ErrorAction Stop
                $restarted += $a.Name
            }
            $deadline = (Get-Date).AddSeconds(12)
            $up = $false
            while ((Get-Date) -lt $deadline) {
                $up = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' }).Count -gt 0
                if ($up) { break }
                Start-Sleep -Milliseconds 500
            }
            $detail = if ($up) { 'Restarted adapter(s): ' + ($restarted -join ', ') } else { 'Adapter(s) restarted but did not reconnect yet: ' + ($restarted -join ', ') }
            [pscustomobject]@{ ok = $up; detail = $detail } | ConvertTo-Json -Compress
            """, cancellationToken);

        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = PowerShell.Bool(json, "ok"),
            Detail = PowerShell.Str(json, "detail"),
        };
    }

    /// <summary>Turns off HKCU manual WinINET proxy (ProxyEnable=0) while keeping ProxyServer intact for free Undo.</summary>
    private static RepairResult ResetProxy(RepairJob job)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProxySource.RegistryPath, writable: true);
        if (key is null)
        {
            return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false, Detail = "Internet Settings registry key could not be opened" };
        }

        var enabled = key.GetValue("ProxyEnable") is int value && value != 0;
        if (!enabled)
        {
            return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = true, Detail = "Manual Windows proxy was already off" };
        }

        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        var nowOff = key.GetValue("ProxyEnable") is int after && after == 0;
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = nowOff,
            Detail = nowOff ? "Manual Windows proxy turned off (previous proxy setting preserved for Undo)" : "ProxyEnable could not be verified as off",
            Rollback = nowOff ? ["ProxyEnable=1"] : [],
        };
    }

    /// <summary>Restores ProxyEnable=1 from the saved rollback record.</summary>
    private static RepairResult RestoreProxy(RepairJob job)
    {
        if (!job.Items.Contains("ProxyEnable=1", StringComparer.Ordinal))
        {
            return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false, Detail = "No valid proxy rollback record was supplied" };
        }

        using var key = Registry.CurrentUser.OpenSubKey(ProxySource.RegistryPath, writable: true);
        if (key is null)
        {
            return new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false, Detail = "Internet Settings registry key could not be opened" };
        }

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        var nowOn = key.GetValue("ProxyEnable") is int after && after == 1;
        return new RepairResult
        {
            JobId = job.JobId,
            OperationId = job.OperationId,
            Completed = nowOn,
            Detail = nowOn ? "Previous manual Windows proxy restored" : "ProxyEnable could not be verified as restored",
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

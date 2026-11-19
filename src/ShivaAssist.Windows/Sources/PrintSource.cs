using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>
/// Print Spooler service state, number of printers and stuck print jobs. Printers are counted from the registry, because
/// Windows cannot list printers through the spooler while the spooler is stopped (that would hide the very problem). A job counts as stuck when Windows marks it
/// as error, deleting or blocked, or when it has been waiting for 30 minutes or longer. Read-only.
/// </summary>
public sealed class PrintSource : ISnapshotSource<PrintSnapshot>
{
    private const string Script = """
        $svc = Get-Service -Name Spooler -ErrorAction SilentlyContinue
        $state = if (-not $svc) { 'missing' } elseif ($svc.Status -eq 'Running') { 'running' } else { 'stopped' }
        $start = if ($svc) { "$($svc.StartType)".ToLowerInvariant() } else { 'unknown' }
        $printers = @(Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers' -ErrorAction SilentlyContinue).Count
        $stuck = 0; $oldest = 0
        if ($state -eq 'running') {
            $now = Get-Date
            foreach ($job in @(Get-CimInstance Win32_PrintJob -ErrorAction SilentlyContinue)) {
                $age = 0
                if ($job.TimeSubmitted) { $age = [int][math]::Max(0, ($now - $job.TimeSubmitted).TotalMinutes) }
                # StatusMask: 2 = error, 4 = deleting, 512 = blocked.
                $failed = ([int]$job.StatusMask -band (2 -bor 4 -bor 512)) -ne 0
                if ($failed -or $age -ge 30) { $stuck++; if ($age -gt $oldest) { $oldest = $age } }
            }
        }
        [pscustomobject]@{ state = $state; start = $start; printers = $printers; stuck = $stuck; oldest = $oldest } | ConvertTo-Json -Compress
        """;

    public async Task<PrintSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        var state = Str(json, "state") switch
        {
            "running" => SpoolerState.Running,
            "missing" => SpoolerState.Missing,
            _ => SpoolerState.Stopped,
        };
        return new PrintSnapshot(state, Str(json, "start"), (int)Long(json, "printers"), (int)Long(json, "stuck"), (int)Long(json, "oldest"));
    }
}

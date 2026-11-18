using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Windows Time service state and one clock sample against time.windows.com (read-only).</summary>
public sealed class TimeSource : ISnapshotSource<TimeSnapshot>
{
    public const string Server = "time.windows.com";

    private const string Script = """
        $svc = Get-Service -Name W32Time -ErrorAction SilentlyContinue
        $state = if (-not $svc) { 'missing' } elseif ("$($svc.StartType)" -eq 'Disabled') { 'disabled' } elseif ($svc.Status -eq 'Running') { 'running' } else { 'stopped' }
        $offset = $null
        $out = & "$env:SystemRoot\System32\w32tm.exe" /stripchart /computer:time.windows.com /samples:1 /dataonly 2>&1
        foreach ($line in $out) {
            if ("$line" -match ',\s*([+-]\d+[\.,]\d+)s') { $offset = [double]::Parse(($Matches[1] -replace ',', '.'), [Globalization.CultureInfo]::InvariantCulture) }
        }
        [pscustomobject]@{ state = $state; measured = ($null -ne $offset); offset = if ($null -ne $offset) { $offset } else { 0 } } | ConvertTo-Json -Compress
        """;

    public async Task<TimeSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        var state = Str(json, "state") switch
        {
            "running" => TimeServiceState.Running,
            "disabled" => TimeServiceState.Disabled,
            "missing" => TimeServiceState.Missing,
            _ => TimeServiceState.Stopped,
        };
        double? offset = Bool(json, "measured") && json.TryGetProperty("offset", out var value) && value.TryGetDouble(out var seconds) ? seconds : null;
        return new TimeSnapshot(state, offset, Server);
    }
}

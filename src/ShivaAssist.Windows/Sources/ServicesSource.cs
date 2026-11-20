using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Run state and start type of the sound, search and Wi-Fi services (read-only).</summary>
public sealed class ServicesSource : ISnapshotSource<ServicesSnapshot>
{
    private const string Script = """
        $names = 'AudioEndpointBuilder', 'Audiosrv', 'WSearch', 'WlanSvc'
        $list = foreach ($name in $names) {
            $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
            if ($svc) {
                [pscustomobject]@{ name = $name; state = if ($svc.Status -eq 'Running') { 'running' } else { 'stopped' }; start = "$($svc.StartType)".ToLowerInvariant() }
            }
        }
        $wifi = $false
        try {
            $wifi = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object {
                [int]$_.NdisPhysicalMedium -in 1,9 -and $_.Status -in 'Up','Disconnected'
            }).Count -gt 0
        } catch { $wifi = $false }
        $wlan = Get-Service -Name WlanSvc -ErrorAction SilentlyContinue
        [pscustomobject]@{ services = @($list); wifi = $wifi; wifiStopped = ($null -ne $wlan -and $wlan.Status -eq 'Stopped') } | ConvertTo-Json -Compress -Depth 3
        """;

    public async Task<ServicesSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        return new ServicesSnapshot([.. Items(json, "services").Select(s => new ServiceInfo(
            Str(s, "name"),
            Str(s, "state") == "running" ? ServiceRunState.Running : ServiceRunState.Stopped,
            Str(s, "start")))], Bool(json, "wifi"), Bool(json, "wifiStopped"));
    }
}

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

/// <summary>Read-only Bluetooth radio presence and bthserv state (never records personal device names).</summary>
public sealed class BluetoothSource : ISnapshotSource<BluetoothSnapshot>
{
    private const string Script = """
        $present = $null
        try {
            $present = @(Get-PnpDevice -Class Bluetooth -ErrorAction Stop | Where-Object { $_.Present -eq $true -and $_.Status -eq 'OK' }).Count -gt 0
        } catch {
            $present = $null
        }
        $svc = Get-Service -Name bthserv -ErrorAction SilentlyContinue
        $state = if (-not $svc) { 'missing' } elseif ($svc.Status -eq 'Running') { 'running' } else { 'stopped' }
        $start = if ($svc) { "$($svc.StartType)".ToLowerInvariant() } else { 'unknown' }
        [pscustomobject]@{ present = $present; state = $state; start = $start } | ConvertTo-Json -Compress
        """;

    public async Task<BluetoothSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        bool? present = json.TryGetProperty("present", out var prop) && prop.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            ? prop.GetBoolean()
            : null;
        var state = Str(json, "state") switch
        {
            "running" => ServiceRunState.Running,
            "missing" => ServiceRunState.Missing,
            _ => ServiceRunState.Stopped,
        };
        return new BluetoothSnapshot(present, new ServiceInfo("bthserv", state, Str(json, "start")));
    }
}

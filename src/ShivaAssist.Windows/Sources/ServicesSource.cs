using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Run state and start type of the sound and search services (read-only).</summary>
public sealed class ServicesSource : ISnapshotSource<ServicesSnapshot>
{
    private const string Script = """
        $names = 'AudioEndpointBuilder', 'Audiosrv', 'WSearch'
        $list = foreach ($name in $names) {
            $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
            if ($svc) {
                [pscustomobject]@{ name = $name; state = if ($svc.Status -eq 'Running') { 'running' } else { 'stopped' }; start = "$($svc.StartType)".ToLowerInvariant() }
            }
        }
        [pscustomobject]@{ services = @($list) } | ConvertTo-Json -Compress -Depth 3
        """;

    public async Task<ServicesSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        return new ServicesSnapshot([.. Items(json, "services").Select(s => new ServiceInfo(
            Str(s, "name"),
            Str(s, "state") == "running" ? ServiceRunState.Running : ServiceRunState.Stopped,
            Str(s, "start")))]);
    }
}

using System.Globalization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Core.Modules;

/// <summary>
/// "No sound" and "search finds nothing" are often a stopped or switched-off Windows service. Both are repaired by
/// switching the service back on and starting it (catalogue AUDIO.SERVICE.REPAIR and SEARCH.SERVICE.REPAIR),
/// verified by checking the service again.
/// </summary>
public sealed class ServicesCheck(ISnapshotSource<ServicesSnapshot> source) : SnapshotCheck<ServicesSnapshot>(source)
{
    public const string WifiId = "NETWORK-WIFI-SERVICE-001";
    public const string AudioId = "AUDIO-SERVICE-001";
    public const string SearchId = "SEARCH-SERVICE-001";
    public static readonly IReadOnlyList<string> AudioServices = ["AudioEndpointBuilder", "Audiosrv"];
    public const string SearchService = "WSearch";

    public override string Module => "services";

    public override Message Name => "Check of sound and search";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "SERVICES-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(ServicesSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [Audio(snapshot), Search(snapshot), Wifi(snapshot)];
    }

    private Finding Wifi(ServicesSnapshot snapshot)
    {
        var service = snapshot.Get("WlanSvc");
        if (snapshot.WifiAdapterPresent != true || service.State == ServiceRunState.Missing)
            return Finding.Unknown(WifiId, Module, Category.Network, "Wi-Fi service could not be assessed", UnknownReason.Unsupported, "No confirmed enabled wireless adapter or service");

        if (service.Disabled)
            return new Finding { Id = WifiId, Module = Module, Category = Category.Network, Severity = Severity.Information,
                Title = "The Wi-Fi service is disabled", Recommendation = new("Check whether Wi-Fi was deliberately disabled before changing its settings."), Evidence = [Describe(service)] };

        if (service.State == ServiceRunState.Running)
            return new Finding { Id = WifiId, Module = Module, Category = Category.Network, Severity = Severity.Healthy,
                Title = "The Windows Wi-Fi service is running", Evidence = [Describe(service)] };

        if (!snapshot.WifiServiceStopped || !string.Equals(service.StartType, "automatic", StringComparison.OrdinalIgnoreCase))
            return Finding.Unknown(WifiId, Module, Category.Network, "Wi-Fi service could not be assessed", UnknownReason.Error, "Service is not confirmed stopped with automatic startup");

        return new Finding { Id = WifiId, Module = Module, Category = Category.Network, Severity = Severity.Warning,
            Title = "The Windows Wi-Fi service has stopped", Recommendation = new("Start the Windows Wi-Fi service again."),
            Evidence = [Describe(service)] };
    }

    private static Evidence Describe(ServiceInfo service) => new("Get-Service", string.Create(CultureInfo.InvariantCulture,
        $"{service.Name}: {service.State.ToString().ToLowerInvariant()}, start type {service.StartType}"));

    private Finding Audio(ServicesSnapshot snapshot)
    {
        var services = AudioServices.Select(snapshot.Get).ToList();
        var evidence = services.Select(Describe).ToList();
        if (services.Any(s => s.State == ServiceRunState.Missing))
        {
            return Finding.Unknown("AUDIO-SERVICE-001", Module, Category.Drivers, "The sound service could not be checked on this PC", UnknownReason.Unsupported, "Windows audio services not found");
        }

        if (services.Any(s => s.State != ServiceRunState.Running))
        {
            return new Finding
            {
                Id = AudioId,
                Module = Module,
                Category = Category.Drivers,
                Severity = Severity.Warning,
                Title = "The Windows sound service is not running, so there is no sound",
                SuspectedCause = "The Windows Audio service stopped or was switched off, for example after an update or by a 'speed-up' tool.",
                Recommendation = new("Switch the Windows sound service back on and start it."),
                Evidence = evidence,
            };
        }

        return new Finding { Id = AudioId, Module = Module, Category = Category.Drivers, Severity = Severity.Healthy, Title = "The Windows sound service is running", Evidence = evidence };
    }

    private Finding Search(ServicesSnapshot snapshot)
    {
        var search = snapshot.Get(SearchService);
        var evidence = new[] { Describe(search) };
        if (search.State == ServiceRunState.Missing)
        {
            return new Finding { Id = SearchId, Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Windows Search is not installed on this PC", Evidence = evidence };
        }

        if (search.State != ServiceRunState.Running)
        {
            return new Finding
            {
                Id = SearchId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = search.Disabled
                    ? new Message("Windows Search is switched off, so finding files and e-mails is slow or incomplete")
                    : new Message("Windows Search is not running, so finding files and e-mails is slow or incomplete"),
                SuspectedCause = "The Windows Search service stopped or was switched off, sometimes by a 'speed-up' tool.",
                Recommendation = new("Switch Windows Search back on and start it. Windows then rebuilds its index in the background."),
                Evidence = evidence,
            };
        }

        return new Finding { Id = SearchId, Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Windows Search is running", Evidence = evidence };
    }
}

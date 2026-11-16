using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Core.Modules.Text;

namespace ShivaAssist.Core.Modules;

/// <summary>
/// Finds the failing network layer, so the advice fits the cause: this PC, the router, or the internet provider
/// (research problem 4). Reports one finding for the first layer that fails, not one per symptom.
/// </summary>
public sealed class NetworkCheck(ISnapshotSource<NetworkSnapshot> source) : SnapshotCheck<NetworkSnapshot>(source)
{
    public override string Module => "network";

    public override Category Category => Category.Network;

    public override string FailureId => "NETWORK-CHECK-000";

    public override TimeSpan TimeLimit => TimeSpan.FromSeconds(30);

    public override IReadOnlyList<Finding> Evaluate(NetworkSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var layers = new Evidence("network layer tests", string.Join(" · ",
            $"adapter {Word(snapshot.Adapter)}",
            $"IP address {Word(snapshot.IpAddress)}",
            $"gateway {Word(snapshot.Gateway)}",
            $"DNS {Word(snapshot.Dns)}",
            $"internet {Word(snapshot.Internet)}"));
        var adapters = snapshot.ActiveAdapters.Count > 0
            ? new Evidence("network adapters", "Connected: " + string.Join(", ", snapshot.ActiveAdapters))
            : new Evidence("network adapters", "No connected network adapter");
        Evidence[] evidence = [layers, adapters];

        Finding Problem(Severity severity, string title, string cause, string advice, Confidence confidence = Confidence.High) => new()
        {
            Id = "NETWORK-CONNECT-001",
            Module = Module,
            Category = Category,
            Severity = severity,
            Confidence = confidence,
            Title = title,
            Evidence = evidence,
            SuspectedCause = cause,
            Recommendation = new(advice),
        };

        if (snapshot.Adapter == LayerResult.Failed)
        {
            return [Problem(Severity.Warning, "This PC is not connected to any network",
                "Wi-Fi may be off or out of range, airplane mode may be on, or the network cable is unplugged.",
                "Turn on Wi-Fi (or plug in the cable) and connect to your network.")];
        }

        if (snapshot.IpAddress == LayerResult.Failed)
        {
            return [Problem(Severity.Warning, "The PC is connected but did not get a valid network address",
                "The router did not answer the request for an address (DHCP), or the PC has a wrong fixed address.",
                "Disconnect and reconnect to the network. If other devices also fail, restart the router.")];
        }

        if (snapshot.CaptivePortal)
        {
            return [Problem(Severity.Information, "This network asks you to sign in first",
                "Hotel, train and guest networks show a sign-in page before allowing internet access.",
                "Open a browser and complete the network's sign-in page.")];
        }

        if (snapshot.Internet == LayerResult.Passed && snapshot.Dns == LayerResult.Passed)
        {
            return [new Finding { Id = "NETWORK-CONNECT-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The internet connection works", Evidence = evidence }];
        }

        if (snapshot.Dns == LayerResult.Failed && snapshot.Internet == LayerResult.Passed)
        {
            return [Problem(Severity.Warning, "Internet works, but website names cannot be looked up (DNS)",
                "The DNS server set on this PC does not answer, sometimes after installing a VPN.",
                "Disconnect any VPN and check the DNS settings of the network adapter.")];
        }

        if (snapshot.Dns == LayerResult.Failed && snapshot.Gateway == LayerResult.Passed)
        {
            return [Problem(Severity.Warning, "The router answers, but the internet is not reachable",
                snapshot.ProxyConfigured
                    ? "A proxy is configured on this PC and may be blocking traffic, or the internet provider has an outage."
                    : "The router may have lost its internet connection, or the internet provider has an outage.",
                "Check whether other devices have internet. If none do, restart the router or contact your internet provider.")];
        }

        return [Problem(Severity.Warning, "No internet connection",
            "The router may not be reachable, or both the router and DNS fail to answer.",
            "Restart the router; if other devices work, restart this PC and reconnect.",
            snapshot.Gateway == LayerResult.Failed ? Confidence.Medium : Confidence.High)];
    }

    private static string Word(LayerResult result) => result switch
    {
        LayerResult.Passed => "passed",
        LayerResult.Failed => "failed",
        _ => "not tested",
    };
}

/// <summary>Devices that Windows reports with a problem (research problem 10).</summary>
public sealed class DeviceCheck(ISnapshotSource<DeviceSnapshot> source) : SnapshotCheck<DeviceSnapshot>(source)
{
    public override string Module => "devices";

    public override Category Category => Category.Drivers;

    public override string FailureId => "DEVICE-CHECK-000";

    /// <summary>Device Manager problem codes in plain language (CM_PROB_*).</summary>
    public static string Explain(int code) => code switch
    {
        1 or 18 => "the driver is not configured correctly",
        3 => "the driver may be damaged or Windows is low on memory",
        10 => "the device cannot start",
        12 => "not enough free resources for this device",
        14 => "a restart is needed",
        19 => "the device's settings in the registry are damaged",
        21 or 24 => "the device is not present or not working properly",
        22 => "the device is switched off in Device Manager",
        28 => "no driver is installed",
        31 => "Windows cannot load the driver",
        32 => "the driver's service is disabled",
        39 => "the driver is missing or damaged",
        43 => "the device reported a problem and was stopped",
        45 => "the device is not connected",
        52 => "the driver's digital signature could not be verified",
        _ => $"problem code {N(code)}",
    };

    public override IReadOnlyList<Finding> Evaluate(DeviceSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Switched off on purpose (22) or simply unplugged (45) are not faults.
        var faults = snapshot.Problems.Where(d => d.ProblemCode is not (22 or 45)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var disabled = snapshot.Problems.Where(d => d.ProblemCode == 22).ToList();
        var findings = new List<Finding>();

        findings.Add(faults.Count > 0
            ? new Finding
            {
                Id = "DEVICE-PROBLEM-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = faults.Count == 1 ? $"Windows reports a problem with {faults[0].Name}" : $"Windows reports problems with {N(faults.Count)} devices",
                Evidence = [.. faults.Select(d => new Evidence("Win32_PnPEntity", $"{d.Name} ({d.DeviceClass}): {Explain(d.ProblemCode)}"))],
                SuspectedCause = "A missing, outdated or damaged driver, or a loose cable or port.",
                Recommendation = new("Install the driver from the PC or device manufacturer's website. For USB devices, first try another port or cable."),
            }
            : new Finding { Id = "DEVICE-PROBLEM-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "All devices work without reported problems", Evidence = [new Evidence("Win32_PnPEntity", $"{N(snapshot.PresentDevices)} devices checked")] });

        if (disabled.Count > 0)
        {
            findings.Add(new Finding
            {
                Id = "DEVICE-DISABLED-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = $"{N(disabled.Count)} device(s) are switched off in Device Manager",
                Evidence = [.. disabled.Select(d => new Evidence("Win32_PnPEntity", $"{d.Name} ({d.DeviceClass})"))],
                Recommendation = new("If one of these should work (for example the webcam or Wi-Fi), switch it on in Device Manager."),
            });
        }

        return findings;
    }
}

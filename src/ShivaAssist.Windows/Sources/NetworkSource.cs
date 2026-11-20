using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Windows.Sources;

/// <summary>
/// Tests connectivity layer by layer. The internet test uses Windows' own connectivity check
/// (www.msftconnecttest.com), the same request Windows makes to decide whether a network has internet,
/// so no extra party learns anything about the PC.
/// </summary>
public sealed class NetworkSource : ISnapshotSource<NetworkSnapshot>
{
    private const string ProbeHost = "www.msftconnecttest.com";
    private const string ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private const string ProbeBody = "Microsoft Connect Test";

    /// <summary>A well-known address used only when DNS fails, to tell "DNS broken" from "internet down".</summary>
    private static readonly IPAddress DnsIndependentProbe = IPAddress.Parse("1.1.1.1");

    public async Task<NetworkSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();

        // Virtual adapters (Hyper-V, VPN, VirtualBox) are up without a real network behind them.
        var real = up.Where(n => n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any))).ToList();
        var names = real.Select(n => $"{n.Name} ({Kind(n.NetworkInterfaceType)})").Order(StringComparer.OrdinalIgnoreCase).ToList();
        var proxy = HasProxy();

        if (up.Count == 0)
        {
            return new NetworkSnapshot(names, LayerResult.Failed, LayerResult.NotTested, LayerResult.NotTested, LayerResult.NotTested, LayerResult.NotTested, false, proxy);
        }

        var addresses = real.SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        var validIp = addresses.Any(a => !IsAutoConfigured(a.Address));
        if (real.Count == 0 || !validIp)
        {
            return new NetworkSnapshot(names, LayerResult.Passed, LayerResult.Failed, LayerResult.NotTested, LayerResult.NotTested, LayerResult.NotTested, false, proxy);
        }

        var gateway = real.SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        var gatewayResult = gateway is null ? LayerResult.NotTested
            : await PingAsync(gateway, cancellationToken) || await PingAsync(gateway, cancellationToken) ? LayerResult.Passed : LayerResult.Failed;

        // One retry per layer: a single lost packet or slow answer (common on Wi-Fi and in virtual machines) must not
        // be reported as a broken connection.
        var dns = await ResolvesAsync(cancellationToken) || await RetryAfterPauseAsync(ResolvesAsync, cancellationToken);
        var (internet, captive) = dns ? await ProbeAsync(cancellationToken) : (await PingAsync(DnsIndependentProbe, cancellationToken), false);
        if (!internet)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            (internet, captive) = dns ? await ProbeAsync(cancellationToken) : (await PingAsync(DnsIndependentProbe, cancellationToken), false);
        }

        return new NetworkSnapshot(
            names,
            LayerResult.Passed,
            LayerResult.Passed,
            gatewayResult,
            dns ? LayerResult.Passed : LayerResult.Failed,
            internet ? LayerResult.Passed : LayerResult.Failed,
            captive,
            proxy);
    }

    private static async Task<bool> RetryAfterPauseAsync(Func<CancellationToken, Task<bool>> test, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return await test(cancellationToken);
    }

    /// <summary>169.254.x.x means Windows gave itself an address because no router answered (no DHCP).</summary>
    private static bool IsAutoConfigured(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static string Kind(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "cable",
        NetworkInterfaceType.Wman or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 or NetworkInterfaceType.Ppp => "mobile",
        _ => "other",
    };

    private static bool HasProxy()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return key?.GetValue("ProxyEnable") is int enabled && enabled != 0;
    }

    private static async Task<bool> PingAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    private static async Task<bool> ResolvesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return (await Dns.GetHostAddressesAsync(ProbeHost, timeout.Token)).Length > 0;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Downloads Windows' 22-byte connectivity test. A different answer means a sign-in page intercepted it.</summary>
    private static async Task<(bool Internet, bool Captive)> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await http.GetAsync(new Uri(ProbeUrl), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var ok = response.StatusCode == HttpStatusCode.OK && body.StartsWith(ProbeBody, StringComparison.Ordinal);
            return ok ? (true, false) : (true, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (false, false);
        }
    }
}

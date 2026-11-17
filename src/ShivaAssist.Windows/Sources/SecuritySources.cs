using System.Globalization;
using System.Xml.Linq;
using Microsoft.Win32;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using ShivaAssist.Windows.Infrastructure;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Antivirus (Windows Security Center) and firewall profiles.</summary>
public sealed class SecuritySource : ISnapshotSource<SecuritySnapshot>
{
    private const string AntivirusScript = """
        $av = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct |
            ForEach-Object { [pscustomobject]@{ name = "$($_.displayName)"; state = [int]$_.productState } }
        [pscustomobject]@{ products = @($av) } | ConvertTo-Json -Compress -Depth 3
        """;

    private const string FirewallScript = """
        $fw = Get-CimInstance -Namespace root/StandardCimv2 -ClassName MSFT_NetFirewallProfile |
            ForEach-Object { [pscustomobject]@{ name = "$($_.Name)"; enabled = ([int]$_.Enabled -eq 1) } }
        [pscustomobject]@{ profiles = @($fw) } | ConvertTo-Json -Compress -Depth 3
        """;

    private const string DefenderScript = """
        $s = Get-CimInstance -Namespace root/Microsoft/Windows/Defender -ClassName MSFT_MpComputerStatus
        [pscustomobject]@{
            av = [bool]$s.AntivirusEnabled
            rtp = [bool]$s.RealTimeProtectionEnabled
            age = [int]$s.AntivirusSignatureAge
        } | ConvertTo-Json -Compress
        """;

    public async Task<SecuritySnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var antivirus = await TryAsync(AntivirusScript, json => (IReadOnlyList<AntivirusProduct>)[.. Items(json, "products").Select(p =>
        {
            var (enabled, upToDate) = SecurityCheck.DecodeProductState((int)Long(p, "state"));
            return new AntivirusProduct(Str(p, "name"), enabled, upToDate);
        })], cancellationToken);

        var firewall = await TryAsync(FirewallScript, json => (IReadOnlyList<FirewallProfile>)[.. Items(json, "profiles")
            .Select(p => new FirewallProfile(Str(p, "name"), Bool(p, "enabled")))], cancellationToken);

        DefenderStatus? defender = null;
        if (antivirus is null || antivirus.Count == 0)
        {
            try
            {
                defender = await TryAsync(DefenderScript, json => new DefenderStatus(Bool(json, "av"), Bool(json, "rtp"), (int)Long(json, "age")), cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                defender = null;
            }
        }

        return new SecuritySnapshot(antivirus, firewall, defender);
    }

    /// <summary>One half failing (e.g. no Security Center on Windows Server) must not hide the other half.</summary>
    private static async Task<T?> TryAsync<T>(string script, Func<System.Text.Json.JsonElement, T> map, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return map(await QueryAsync(script, cancellationToken));
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// Programs that start with Windows: the Run keys and Startup folders, minus entries switched off in
/// Task Manager (StartupApproved). Paths are never kept (they contain the user name); only the display name
/// and the registry value or shortcut name needed to switch an entry off.
/// </summary>
public sealed class StartupSource : ISnapshotSource<StartupSnapshot>
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public Task<StartupSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var entries = new List<StartupEntry>();
        AddRunKey(Registry.CurrentUser, RunKey, "HKCU-Run", entries);
        AddRunKey(Registry.LocalMachine, RunKey, "HKLM-Run", entries);
        AddRunKey(Registry.LocalMachine, Run32Key, "HKLM-Run32", entries);
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "UserFolder", entries);
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "CommonFolder", entries);
        return Task.FromResult(new StartupSnapshot([.. entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]));
    }

    /// <summary>Where Task Manager keeps the on/off marker for an entry (<c>Location|Key</c>); null for anything else.</summary>
    public static (RegistryKey Hive, string SubKey, string ValueName)? ApprovedLocation(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var parts = reference.Split('|');
        if (parts.Length != 2 || parts[1].Length == 0 || parts[1].Contains('\\', StringComparison.Ordinal) || parts[1].Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        return parts[0] switch
        {
            "HKCU-Run" => (Registry.CurrentUser, Approved + "Run", parts[1]),
            "HKLM-Run" => (Registry.LocalMachine, Approved + "Run", parts[1]),
            "HKLM-Run32" => (Registry.LocalMachine, Approved + "Run32", parts[1]),
            "UserFolder" => (Registry.CurrentUser, Approved + "StartupFolder", parts[1]),
            "CommonFolder" => (Registry.LocalMachine, Approved + "StartupFolder", parts[1]),
            _ => null,
        };
    }

    private static void AddRunKey(RegistryKey hive, string path, string location, List<StartupEntry> entries)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
        {
            if (IsEnabled(location + "|" + name))
            {
                entries.Add(new StartupEntry(name, location, name));
            }
        }
    }

    private static void AddFolder(string folder, string location, List<StartupEntry> entries)
    {
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
            .Where(f => !string.Equals(f, "desktop.ini", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsEnabled(location + "|" + file))
            {
                entries.Add(new StartupEntry(Path.GetFileNameWithoutExtension(file), location, file));
            }
        }
    }

    /// <summary>StartupApproved stores 12 bytes per entry; an odd first byte (0x03, 0x01…) means switched off.</summary>
    private static bool IsEnabled(string reference)
    {
        if (ApprovedLocation(reference) is not { } at)
        {
            return true;
        }

        using var key = at.Hive.OpenSubKey(at.SubKey);
        return key?.GetValue(at.ValueName) is not byte[] { Length: > 0 } data || data[0] % 2 == 0;
    }
}

/// <summary>Reliability events from the System and Application logs via <c>wevtutil</c> (read-only).</summary>
public sealed class StabilitySource : ISnapshotSource<StabilitySnapshot>
{
    public const int Days = 14;

    public async Task<StabilitySnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var window = (long)TimeSpan.FromDays(Days).TotalMilliseconds;
        string Query(string provider, int eventId) =>
            $"*[System[Provider[@Name='{provider}'] and (EventID={eventId.ToString(CultureInfo.InvariantCulture)}) and TimeCreated[timediff(@SystemTime) <= {window.ToString(CultureInfo.InvariantCulture)}]]]";

        var shutdowns = QueryAsync("System", Query("Microsoft-Windows-Kernel-Power", 41), cancellationToken);
        var bugchecks = QueryAsync("System", Query("Microsoft-Windows-WER-SystemErrorReporting", 1001), cancellationToken);
        var whea = QueryAsync("System", $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= {window.ToString(CultureInfo.InvariantCulture)}]]]", cancellationToken);
        var crashes = QueryAsync("Application", Query("Application Error", 1000), cancellationToken);
        await Task.WhenAll(shutdowns, bugchecks, whea, crashes);

        var byApp = (await crashes)
            .Select(EventXml.AppName)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new StabilitySnapshot(Days, (await shutdowns).Count, (await bugchecks).Count, (await whea).Count, byApp);
    }

    private static async Task<IReadOnlyList<XElement>> QueryAsync(string log, string xpath, CancellationToken cancellationToken)
    {
        var (exit, output, error) = await ToolRunner.RunAsync(
            ToolRunner.System32("wevtutil.exe"),
            ["qe", log, "/q:" + xpath, "/f:xml", "/c:500", "/rd:true"],
            cancellationToken);

        if (exit == 5 || error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Event log access denied.");
        }

        return exit == 0 ? EventXml.ParseEvents(output) : throw new InvalidOperationException($"wevtutil failed (exit {exit}).");
    }
}

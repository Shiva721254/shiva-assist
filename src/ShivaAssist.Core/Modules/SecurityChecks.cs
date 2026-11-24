using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Core.Modules.Text;

namespace ShivaAssist.Core.Modules;

/// <summary>Antivirus and firewall status as Windows Security reports it.</summary>
public sealed class SecurityCheck(ISnapshotSource<SecuritySnapshot> source) : SnapshotCheck<SecuritySnapshot>(source)
{
    public const string FirewallId = "SECURITY-FIREWALL-001";
    public const string NoThirdPartyFirewallObservation = "No active third-party firewall registered";

    public override string Module => "security";

    public override Message Name => "Check of antivirus and firewall";

    public override Category Category => Category.Security;

    public override string FailureId => "SECURITY-CHECK-000";

    /// <summary>
    /// Decodes <c>AntiVirusProduct.productState</c> from Windows Security Center. The middle byte holds the
    /// on/off state (0x10 = on); the low byte holds the signature state (0x00 = up to date, 0x10 = out of date).
    /// </summary>
    public static (bool Enabled, bool UpToDate) DecodeProductState(int productState) =>
        ((productState & 0x1000) != 0, (productState & 0x10) == 0);

    public static bool IsWindowsFirewallName(string name) =>
        string.Equals(name, "Windows Firewall", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Windows Defender Firewall", StringComparison.OrdinalIgnoreCase);

    public override IReadOnlyList<Finding> Evaluate(SecuritySnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [Antivirus(snapshot.Antivirus, snapshot.Defender), Firewall(snapshot.Firewall, snapshot.ThirdPartyFirewalls)];
    }

    private Finding Antivirus(IReadOnlyList<AntivirusProduct>? products, DefenderStatus? defender)
    {
        // Security Center is the primary source. When it lists nothing (or is absent, as on Windows Server),
        // Defender's own status decides; without either, the honest answer is Unknown, never "no protection".
        if (products is null || products.Count == 0)
        {
            return defender is null
                ? Finding.Unknown("SECURITY-AV-001", Module, Category, "Antivirus status could not be read", UnknownReason.Unsupported, products is null ? "Windows Security Center is not available" : "No antivirus registered with Windows Security Center")
                : FromDefender(defender);
        }

        var evidence = products.Select(p => new Evidence("SecurityCenter2", $"{p.Name}: {(p.Enabled ? "on" : "off")}, definitions {(p.UpToDate ? "up to date" : "out of date")}")).ToList();
        var active = products.Where(p => p.Enabled).ToList();

        if (active.Count == 0)
        {
            return new Finding
            {
                Id = "SECURITY-AV-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Critical,
                Title = "No antivirus protection is switched on",
                Evidence = evidence.Count > 0 ? evidence : [new Evidence("SecurityCenter2", "No antivirus product registered")],
                Recommendation = new("Turn on Microsoft Defender: Start → Windows Security → Virus & threat protection."),
            };
        }

        if (active.Any(p => !p.UpToDate))
        {
            return new Finding
            {
                Id = "SECURITY-AV-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = "The antivirus is on, but its virus definitions are out of date",
                Evidence = evidence,
                Recommendation = new("Open your antivirus and update it, or check Windows Update."),
            };
        }

        return new Finding { Id = "SECURITY-AV-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Antivirus protection is on and up to date", Evidence = evidence };
    }

    private Finding FromDefender(DefenderStatus defender)
    {
        var evidence = new Evidence("MSFT_MpComputerStatus", $"Microsoft Defender: antivirus {(defender.AntivirusEnabled ? "on" : "off")}, real-time protection {(defender.RealTimeProtection ? "on" : "off")}, definitions {N(defender.SignatureAgeDays)} days old");
        if (!defender.AntivirusEnabled || !defender.RealTimeProtection)
        {
            return new Finding
            {
                Id = "SECURITY-AV-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Critical,
                Title = defender.AntivirusEnabled ? "Real-time virus protection is switched off" : "No antivirus protection is switched on",
                Evidence = [evidence],
                Recommendation = new("Turn on real-time protection: Start → Windows Security → Virus & threat protection → Manage settings."),
            };
        }

        return defender.SignatureAgeDays > 7
            ? new Finding { Id = "SECURITY-AV-001", Module = Module, Category = Category, Severity = Severity.Warning, Title = "Microsoft Defender is on, but its virus definitions are out of date", Evidence = [evidence], Recommendation = new("Check for updates in Windows Security → Virus & threat protection updates.") }
            : new Finding { Id = "SECURITY-AV-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Microsoft Defender protection is on and up to date", Evidence = [evidence] };
    }

    private Finding Firewall(IReadOnlyList<FirewallProfile>? profiles, IReadOnlyList<FirewallProduct>? thirdPartyFirewalls)
    {
        if (profiles is null || profiles.Count == 0)
        {
            return Finding.Unknown(FirewallId, Module, Category, "Firewall status could not be read", UnknownReason.Unsupported, "No firewall profiles reported");
        }

        var evidence = profiles.Select(p => new Evidence("MSFT_NetFirewallProfile", $"{p.Name}: {(p.Enabled ? "on" : "off")}")).ToList();
        var activeThirdParty = thirdPartyFirewalls?
            .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Name) && !IsWindowsFirewallName(f.Name))
            .ToList() ?? [];

        if (thirdPartyFirewalls is not null)
        {
            if (activeThirdParty.Count > 0)
            {
                evidence.AddRange(activeThirdParty.Select(f => new Evidence("SecurityCenter2", $"{f.Name}: on")));
            }
            else
            {
                evidence.Add(new Evidence("SecurityCenter2", NoThirdPartyFirewallObservation));
            }
        }

        var off = profiles.Where(p => !p.Enabled).Select(p => p.Name).ToList();
        if (off.Count == 0)
        {
            return new Finding { Id = FirewallId, Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The Windows firewall is on for every network type", Evidence = evidence };
        }

        if (activeThirdParty.Count > 0)
        {
            return new Finding
            {
                Id = FirewallId,
                Module = Module,
                Category = Category,
                Severity = Severity.Healthy,
                Title = new Message("Another firewall is active: {names}", ("names", string.Join(", ", activeThirdParty.Select(f => f.Name)))),
                Evidence = evidence,
            };
        }

        return new Finding
        {
            Id = FirewallId,
            Module = Module,
            Category = Category,
            Severity = Severity.Warning,
            Title = new Message("The Windows firewall is switched off for: {profiles}", ("profiles", string.Join(", ", off))),
            Evidence = evidence,
            SuspectedCause = "It may have been switched off by hand or by another security program.",
            Recommendation = new("Turn the firewall on: Start → Windows Security → Firewall & network protection."),
        };
    }
}

/// <summary>Programs that start automatically with Windows.</summary>
public sealed class StartupCheck(ISnapshotSource<StartupSnapshot> source) : SnapshotCheck<StartupSnapshot>(source)
{
    public override string Module => "startup";

    public override Message Name => "Check of startup programs";

    public override Category Category => Category.Performance;

    public override string FailureId => "PERF-STARTUP-001";

    public override IReadOnlyList<Finding> Evaluate(StartupSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var count = snapshot.EnabledEntries.Count;
        var names = snapshot.EnabledEntries.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var evidence = new Evidence("Run keys and Startup folders", count == 0 ? "No programs start automatically" : $"{N(count)} programs: {string.Join(", ", names)}");

        return [count switch
        {
            > 15 => new Finding
            {
                Id = "PERF-STARTUP-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("{count} programs start automatically with Windows", ("count", count)),
                Evidence = [evidence],
                SuspectedCause = "Many startup programs make the PC slow to start and use memory in the background.",
                Recommendation = new("Switch off programs you do not need at startup: Task Manager → Startup apps."),
            },
            > 8 => new Finding
            {
                Id = "PERF-STARTUP-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = new Message("{count} programs start automatically with Windows", ("count", count)),
                Evidence = [evidence],
                Recommendation = new("Check Task Manager → Startup apps for programs you do not need."),
            },
            _ => new Finding { Id = "PERF-STARTUP-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Only a few programs start automatically", Evidence = [evidence] },
        }];
    }
}

/// <summary>Unexpected shutdowns, blue screens, hardware error reports and program crashes from the event logs.</summary>
public sealed class StabilityCheck(ISnapshotSource<StabilitySnapshot> source) : SnapshotCheck<StabilitySnapshot>(source)
{
    public override string Module => "stability";

    public override Message Name => "Check of crashes and shutdowns";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "SYSTEM-CHECK-000";

    public override TimeSpan TimeLimit => TimeSpan.FromSeconds(45);

    public override IReadOnlyList<Finding> Evaluate(StabilitySnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var window = $"in the last {N(snapshot.Days)} days";

        var shutdowns = new Evidence("System log: Kernel-Power 41", $"{N(snapshot.UnexpectedShutdowns)} unexpected shutdowns {window}");
        var shutdownFinding = snapshot.UnexpectedShutdowns switch
        {
            >= 3 => new Finding
            {
                Id = "SYSTEM-SHUTDOWN-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("The PC switched off unexpectedly {count} times in the last {days} days", ("count", snapshot.UnexpectedShutdowns), ("days", snapshot.Days)),
                Evidence = [shutdowns],
                SuspectedCause = "Possibly a power or battery problem, overheating, or the PC being switched off with the power button.",
                Recommendation = new("If you did not switch it off yourself, have a Shiva technician check power supply and temperature."),
            },
            > 0 => new Finding { Id = "SYSTEM-SHUTDOWN-001", Module = Module, Category = Category, Severity = Severity.Information, Title = new Message("The PC switched off unexpectedly {count} time(s) in the last {days} days", ("count", snapshot.UnexpectedShutdowns), ("days", snapshot.Days)), Evidence = [shutdowns] },
            _ => new Finding { Id = "SYSTEM-SHUTDOWN-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "No unexpected shutdowns", Evidence = [shutdowns] },
        };

        var bugchecks = new Evidence("System log: BugCheck 1001", $"{N(snapshot.BlueScreens)} blue screens {window}");
        var blueScreenFinding = snapshot.BlueScreens switch
        {
            > 0 => new Finding
            {
                Id = "SYSTEM-BUGCHECK-001",
                Module = Module,
                Category = Category,
                Severity = snapshot.BlueScreens >= 3 ? Severity.Critical : Severity.Warning,
                Title = new Message("Windows crashed with a blue screen {count} time(s) in the last {days} days", ("count", snapshot.BlueScreens), ("days", snapshot.Days)),
                Evidence = [bugchecks],
                SuspectedCause = "Often a faulty driver, sometimes failing memory or disk.",
                Recommendation = new("Install all Windows updates and driver updates; if it keeps happening, contact a Shiva technician."),
            },
            _ => new Finding { Id = "SYSTEM-BUGCHECK-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "No blue screens", Evidence = [bugchecks] },
        };

        var whea = new Evidence("System log: WHEA-Logger", $"{N(snapshot.HardwareErrors)} hardware error reports {window}");
        // Single corrected errors are common and often harmless; only a pattern is a warning.
        var hardwareFinding = snapshot.HardwareErrors > 0
            ? new Finding
            {
                Id = "HARDWARE-WHEA-001",
                Module = Module,
                Category = Category.Hardware,
                Severity = snapshot.HardwareErrors >= 3 ? Severity.Warning : Severity.Information,
                Confidence = Confidence.Medium,
                Title = new Message("Windows recorded {count} hardware error report(s) in the last {days} days", ("count", snapshot.HardwareErrors), ("days", snapshot.Days)),
                Evidence = [whea],
                SuspectedCause = "The processor, memory or a connected device reported errors. Single corrected errors can be harmless.",
                Recommendation = new("If the PC is also unstable, have a Shiva technician test the memory and hardware."),
            }
            : new Finding { Id = "HARDWARE-WHEA-001", Module = Module, Category = Category.Hardware, Severity = Severity.Healthy, Title = "No hardware errors recorded", Evidence = [whea] };

        var crashes = snapshot.AppCrashesByApp.Values.Sum();
        var top = snapshot.AppCrashesByApp
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(kv => new Evidence("Application log: Application Error 1000", $"{kv.Key}: {N(kv.Value)} crashes"))
            .ToList();
        var total = new Evidence("Application log: Application Error 1000", $"{N(crashes)} program crashes {window}");
        var appFinding = crashes switch
        {
            >= 10 => new Finding
            {
                Id = "APP-CRASH-001",
                Module = Module,
                Category = Category.Applications,
                Severity = Severity.Warning,
                Title = new Message("Programs crashed {count} times in the last {days} days", ("count", crashes), ("days", snapshot.Days)),
                Evidence = [total, .. top],
                Recommendation = new("Update or reinstall the program that crashes most often."),
            },
            >= 3 => new Finding { Id = "APP-CRASH-001", Module = Module, Category = Category.Applications, Severity = Severity.Information, Title = new Message("Programs crashed {count} times in the last {days} days", ("count", crashes), ("days", snapshot.Days)), Evidence = [total, .. top] },
            _ => new Finding { Id = "APP-CRASH-001", Module = Module, Category = Category.Applications, Severity = Severity.Healthy, Title = "Programs run without frequent crashes", Evidence = [total] },
        };

        return [shutdownFinding, blueScreenFinding, hardwareFinding, appFinding];
    }
}

using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Core.Modules.Text;

namespace ShivaAssist.Core.Modules;

/// <summary>Free space on the drives and the health that Windows reports for each disk.</summary>
public sealed class StorageCheck(ISnapshotSource<StorageSnapshot> source) : SnapshotCheck<StorageSnapshot>(source)
{
    private const long OneGb = 1_000_000_000;

    public override string Module => "storage";

    public override Message Name => "Check of the drives and disks";

    public override Category Category => Category.Hardware;

    public override string FailureId => "STORAGE-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(StorageSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var findings = new List<Finding>();

        if (snapshot.Volumes.FirstOrDefault(v => v.IsSystem) is { SizeBytes: > 0 } system)
        {
            findings.Add(SystemSpace(system));
        }
        else
        {
            findings.Add(Finding.Unknown("STORAGE-SPACE-001", Module, Category, "Free space on the Windows drive could not be read", UnknownReason.Error, "The system drive was not found"));
        }

        var others = snapshot.Volumes.Where(v => !v.IsSystem && v.SizeBytes > 0).ToList();
        if (others.Count > 0)
        {
            var low = others.Where(v => FreeFraction(v) < 0.1).ToList();
            findings.Add(low.Count > 0
                ? new Finding
                {
                    Id = "STORAGE-SPACE-002",
                    Module = Module,
                    Category = Category,
                    Severity = Severity.Information,
                    Title = "Another drive is almost full",
                    Evidence = [.. low.Select(VolumeEvidence)],
                    Recommendation = new("Move or delete files you no longer need from that drive."),
                }
                : new Finding { Id = "STORAGE-SPACE-002", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The other drives have enough free space", Evidence = [.. others.Select(VolumeEvidence)] });
        }

        findings.Add(DiskHealthFinding(snapshot.Disks));
        findings.Add(WearFinding(snapshot.Disks, context));
        return findings;
    }

    private static double FreeFraction(Volume v) => (double)v.FreeBytes / v.SizeBytes;

    private static Evidence VolumeEvidence(Volume v) =>
        new("drive", $"{v.Name} ({v.FileSystem}): {Gb(v.FreeBytes)} free of {Gb(v.SizeBytes)} ({Percent(FreeFraction(v))})");

    private Finding SystemSpace(Volume system)
    {
        var free = FreeFraction(system);
        var evidence = VolumeEvidence(system);

        // Absolute space matters most: Windows updates need several GB whatever the drive size.
        if (system.FreeBytes < 4 * OneGb)
        {
            return new Finding
            {
                Id = "STORAGE-SPACE-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Critical,
                Title = new Message("The Windows drive is almost full ({free:gb} free)", ("free", system.FreeBytes)),
                Evidence = [evidence],
                SuspectedCause = "Downloads, temporary files, old updates or the Recycle Bin often take up the space. With this little space, updates can fail and Windows becomes slow or unstable.",
                Recommendation = new("Free up space now: empty the Recycle Bin, run Disk Cleanup, and remove programs or files you do not need."),
            };
        }

        if (free < 0.10 || system.FreeBytes < 15 * OneGb)
        {
            return new Finding
            {
                Id = "STORAGE-SPACE-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("The Windows drive is getting full ({free:gb} free)", ("free", system.FreeBytes)),
                Evidence = [evidence],
                SuspectedCause = "Downloads, temporary files, old updates or the Recycle Bin often take up the space.",
                Recommendation = new("Free up space: Settings → System → Storage → Cleanup recommendations."),
            };
        }

        return new Finding { Id = "STORAGE-SPACE-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The Windows drive has enough free space", Evidence = [evidence] };
    }

    private Finding DiskHealthFinding(IReadOnlyList<PhysicalDisk>? disks)
    {
        if (disks is null || disks.Count == 0)
        {
            return Finding.Unknown("STORAGE-HEALTH-001", Module, Category, "Disk health could not be read", UnknownReason.Unsupported, "Windows Storage reported no physical disks");
        }

        var evidence = disks.Select(d => new Evidence("MSFT_PhysicalDisk", $"{d.Model} ({d.MediaType}, {d.BusType}, {Gb(d.SizeBytes)}): {d.Health}")).ToList();
        var unhealthy = disks.Any(d => d.Health == DiskHealth.Unhealthy);
        var warning = disks.Any(d => d.Health == DiskHealth.Warning);

        if (unhealthy || warning)
        {
            return new Finding
            {
                Id = "STORAGE-HEALTH-001",
                Module = Module,
                Category = Category,
                Severity = unhealthy ? Severity.Critical : Severity.Warning,
                Title = unhealthy ? "A disk reports that it is failing" : "A disk reports early signs of wear or errors",
                Evidence = evidence,
                SuspectedCause = "The disk's own health monitoring reports a problem.",
                Recommendation = new("Back up your important files today, then have the disk checked or replaced by a Shiva technician."),
            };
        }

        if (disks.All(d => d.Health == DiskHealth.Unknown))
        {
            return Finding.Unknown("STORAGE-HEALTH-001", Module, Category, "The disks do not report their health", UnknownReason.Unsupported, "HealthStatus is Unknown for every disk");
        }

        return new Finding { Id = "STORAGE-HEALTH-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "All disks report that they are healthy", Evidence = evidence };
    }

    /// <summary>
    /// Wear, temperature and uncorrectable errors from the disk's own counters. Uncorrectable errors or an SSD past
    /// 90 % of its rated life mean data is at risk: back up first, never "repair" a failing disk with cleanups.
    /// </summary>
    private Finding WearFinding(IReadOnlyList<PhysicalDisk>? disks, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var measured = disks?.Where(d => d.Reliability is { HasAny: true }).ToList() ?? [];
        if (measured.Count == 0)
        {
            return context.IsElevated
                ? Finding.Unknown("STORAGE-WEAR-001", Module, Category, "The disks do not report wear or error counters", UnknownReason.Unsupported, "MSFT_StorageReliabilityCounter returned no values")
                : Finding.Unknown("STORAGE-WEAR-001", Module, Category, "Disk wear and error counters need administrator rights", UnknownReason.RequiresAdmin);
        }

        var evidence = measured.Select(d => new Evidence("MSFT_StorageReliabilityCounter", string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{d.Model}: wear {Show(d.Reliability!.WearPercent, "%")}, temperature {Show(d.Reliability.TemperatureC, " °C")}, power-on {Show(d.Reliability.PowerOnHours, " h")}, uncorrected errors {Show(d.Reliability.UncorrectedErrors, string.Empty)}"))).ToList();

        Finding Problem(Severity severity, Message title, Message cause) => new()
        {
            Id = WearId,
            Module = Module,
            Category = Category,
            Severity = severity,
            Title = title,
            Evidence = evidence,
            SuspectedCause = cause,
            Recommendation = new("Back up your important files today, then have the disk checked or replaced by a Shiva technician."),
        };

        if (measured.FirstOrDefault(d => d.Reliability!.UncorrectedErrors > 0) is { } failing)
        {
            return Problem(Severity.Critical,
                new Message("{disk} could not read or write some data (uncorrectable errors)", ("disk", failing.Model)),
                new Message("The disk could not recover data from some areas. This is a typical early sign of a failing disk."));
        }

        if (measured.FirstOrDefault(d => d.Reliability!.WearPercent >= 90) is { } wornOut)
        {
            return Problem(Severity.Critical,
                new Message("{disk} has used {wear} % of its rated life", ("disk", wornOut.Model), ("wear", wornOut.Reliability!.WearPercent!.Value)),
                new Message("SSDs can only be written a limited number of times. Near the end of that life, errors and sudden failure become likely."));
        }

        if (measured.FirstOrDefault(d => d.Reliability!.WearPercent >= 70) is { } worn)
        {
            return Problem(Severity.Warning,
                new Message("{disk} has used {wear} % of its rated life", ("disk", worn.Model), ("wear", worn.Reliability!.WearPercent!.Value)),
                new Message("SSDs can only be written a limited number of times. Plan a replacement while the disk still works."));
        }

        if (measured.FirstOrDefault(d => d.Reliability!.TemperatureC >= 70) is { } hot)
        {
            return Problem(Severity.Warning,
                new Message("{disk} is very warm ({temp} °C)", ("disk", hot.Model), ("temp", hot.Reliability!.TemperatureC!.Value)),
                new Message("Blocked air vents, dust or heavy use can overheat a disk. Long periods of heat shorten its life."));
        }

        return new Finding { Id = WearId, Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Disk wear and error counters are normal", Evidence = evidence };
    }

    private const string WearId = "STORAGE-WEAR-001";

    private static string Show<T>(T? value, string unit)
        where T : struct => value is { } v ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v}{unit}") : "not reported";
}

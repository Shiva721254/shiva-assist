using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Core.Modules.Text;

namespace ShivaAssist.Core.Modules;

/// <summary>Processor model and load. Device driver problems (graphics included) are reported by <see cref="DeviceCheck"/>.</summary>
public sealed class ProcessorCheck(ISnapshotSource<ProcessorSnapshot> source) : SnapshotCheck<ProcessorSnapshot>(source)
{
    public override string Module => "processor";

    public override Message Name => "Check of the processor";

    public override Category Category => Category.Hardware;

    public override string FailureId => "CPU-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(ProcessorSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var cpu = snapshot;
        var findings = new List<Finding>();

        var info = new Evidence("Win32_Processor", $"{cpu.Name}, {N(cpu.Cores)} cores, {N(cpu.LogicalProcessors)} threads");
        if (cpu.LoadSamples.Count == 0)
        {
            findings.Add(Finding.Unknown("CPU-LOAD-001", Module, Category.Performance, "Processor load could not be measured", UnknownReason.Unsupported, "Win32_Processor reported no load"));
        }
        else
        {
            var average = (int)Math.Round(cpu.LoadSamples.Average());
            var load = new Evidence("Win32_Processor.LoadPercentage", $"{string.Join(", ", cpu.LoadSamples.Select(N))}% over {N(cpu.LoadSamples.Count)} samples (average {N(average)}%)");
            findings.Add(average >= 90
                ? new Finding
                {
                    Id = "CPU-LOAD-001",
                    Module = Module,
                    Category = Category.Performance,
                    Severity = Severity.Warning,
                    Confidence = Confidence.Medium,
                    Title = new Message("The processor was almost fully busy during the scan ({load}%)", ("load", average)),
                    Evidence = [info, load],
                    SuspectedCause = "A program or background task may be using the processor heavily. This alone does not mean the hardware is faulty.",
                    Recommendation = new("Open Task Manager (Ctrl+Shift+Esc) and sort by CPU to see which program is busy."),
                }
                : new Finding { Id = "CPU-LOAD-001", Module = Module, Category = Category.Performance, Severity = Severity.Healthy, Title = new Message("Processor load is normal ({load}%)", ("load", average)), Evidence = [info, load] });
        }

        return findings;
    }
}

/// <summary>Installed memory and how much of it is in use.</summary>
public sealed class MemoryCheck(ISnapshotSource<MemorySnapshot> source) : SnapshotCheck<MemorySnapshot>(source)
{
    private const long OneGb = 1_000_000_000;

    public override string Module => "memory";

    public override Message Name => "Check of the memory";

    public override Category Category => Category.Hardware;

    public override string FailureId => "MEMORY-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(MemorySnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var installed = new Evidence("Win32_OperatingSystem", $"{Gb(snapshot.TotalBytes)} usable memory");

        // Usable memory is a little below the installed amount (hardware reserved), hence the margins.
        var capacity = snapshot.TotalBytes switch
        {
            < (long)(3.8 * OneGb) => new Finding
            {
                Id = "MEMORY-CAPACITY-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = "This PC has less memory than Windows 11 requires (4 GB)",
                Evidence = [installed],
                Recommendation = new("Adding memory will make the PC noticeably faster; a Shiva technician can check what fits."),
            },
            < (long)(7.5 * OneGb) => new Finding
            {
                Id = "MEMORY-CAPACITY-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = "This PC has little memory for today's programs",
                Evidence = [installed],
                Recommendation = new("8 GB or more is recommended if the PC feels slow with several programs open."),
            },
            _ => new Finding { Id = "MEMORY-CAPACITY-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The PC has enough memory", Evidence = [installed] },
        };

        if (snapshot.TotalBytes <= 0)
        {
            return [capacity];
        }

        var used = 1 - ((double)snapshot.AvailableBytes / snapshot.TotalBytes);
        var usage = new Evidence("Win32_OperatingSystem", $"{Percent(used)} in use, {Gb(snapshot.AvailableBytes)} free");
        var load = used >= 0.9
            ? new Finding
            {
                Id = "MEMORY-USAGE-001",
                Module = Module,
                Category = Category.Performance,
                Severity = Severity.Warning,
                Confidence = Confidence.Medium,
                Title = new Message("Memory was almost full during the scan ({used:pct})", ("used", used)),
                Evidence = [usage],
                SuspectedCause = "Many open programs or browser tabs, or a program that keeps using more memory.",
                Recommendation = new("Close programs you are not using, or restart the PC."),
            }
            : new Finding { Id = "MEMORY-USAGE-001", Module = Module, Category = Category.Performance, Severity = Severity.Healthy, Title = new Message("Memory use is normal ({used:pct})", ("used", used)), Evidence = [usage] };

        if (snapshot.CommitLimitBytes <= 0)
        {
            return [capacity, load];
        }

        var commit = (double)snapshot.CommitUsedBytes / snapshot.CommitLimitBytes;
        var commitEvidence = new Evidence("Win32_OperatingSystem (virtual memory)", $"{Gb(snapshot.CommitUsedBytes)} of {Gb(snapshot.CommitLimitBytes)} committed ({Percent(commit)})");
        var pressure = commit >= 0.9
            ? new Finding
            {
                Id = "MEMORY-COMMIT-001",
                Module = Module,
                Category = Category.Performance,
                Severity = Severity.Warning,
                Confidence = Confidence.Medium,
                Title = "Programs have claimed almost all available memory, so Windows is using the slow page file",
                Evidence = [commitEvidence, usage],
                SuspectedCause = "More programs or browser tabs are open than the installed memory can hold.",
                Recommendation = new("Close memory-heavy programs and tabs. If this happens during normal work, more memory (RAM) will help."),
            }
            : new Finding { Id = "MEMORY-COMMIT-001", Module = Module, Category = Category.Performance, Severity = Severity.Healthy, Title = "There is no memory pressure", Evidence = [commitEvidence] };

        return [capacity, load, pressure];
    }
}

/// <summary>Battery wear: full-charge capacity compared with the design capacity.</summary>
public sealed class BatteryCheck(ISnapshotSource<BatterySnapshot> source) : SnapshotCheck<BatterySnapshot>(source)
{
    public override string Module => "battery";

    public override Message Name => "Check of the battery";

    public override Category Category => Category.Hardware;

    public override string FailureId => "BATTERY-WEAR-001";

    public override IReadOnlyList<Finding> Evaluate(BatterySnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        if (!snapshot.HasBattery)
        {
            return [];
        }

        if (snapshot.DesignCapacity is not > 0 || snapshot.FullChargeCapacity is not > 0)
        {
            // Windows often shares the capacities only with administrators; say so rather than "not available".
            return [Finding.Unknown("BATTERY-WEAR-001", Module, Category, "Battery wear could not be measured",
                context.IsElevated ? UnknownReason.Unsupported : UnknownReason.RequiresAdmin, "The battery does not report its capacities")];
        }

        var health = Math.Min(1.0, (double)snapshot.FullChargeCapacity.Value / snapshot.DesignCapacity.Value);
        var cycles = snapshot.CycleCount is > 0 ? $", {N(snapshot.CycleCount.Value)} charge cycles" : string.Empty;
        var evidence = new Evidence("root\\wmi battery data", $"Holds {Percent(health)} of its original capacity{cycles}");
        return [health switch
        {
            < 0.5 => new Finding
            {
                Id = "BATTERY-WEAR-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("The battery holds only {health:pct} of its original charge", ("health", health)),
                Evidence = [evidence],
                Recommendation = new("Replace the battery; a Shiva technician can order the right one."),
            },
            < 0.8 => new Finding { Id = "BATTERY-WEAR-001", Module = Module, Category = Category, Severity = Severity.Information, Title = new Message("The battery is worn and holds {health:pct} of its original charge", ("health", health)), Evidence = [evidence] },
            _ => new Finding { Id = "BATTERY-WEAR-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The battery is in good condition", Evidence = [evidence] },
        }];
    }
}

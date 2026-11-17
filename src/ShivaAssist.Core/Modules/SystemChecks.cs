using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Core.Modules.Text;

namespace ShivaAssist.Core.Modules;

/// <summary>Windows version and time since the last restart.</summary>
public sealed class OsCheck(ISnapshotSource<OsSnapshot> source) : SnapshotCheck<OsSnapshot>(source)
{
    /// <summary>First Windows 11 build. Windows 11 still reports "Windows 10" as its product name.</summary>
    public const int FirstWindows11Build = 22000;

    public override string Module => "windows-version";

    public override Message Name => "Check of the Windows version";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "OS-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(OsSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        var windows11 = snapshot.Build >= FirstWindows11Build;
        var name = windows11 ? snapshot.ProductName.Replace("Windows 10", "Windows 11", StringComparison.Ordinal) : snapshot.ProductName;
        var version = new Evidence("registry: Windows NT\\CurrentVersion", $"{name} {snapshot.DisplayVersion}, build {N(snapshot.Build)}.{N(snapshot.Revision)}");
        var today = DateOnly.FromDateTime(context.Now.UtcDateTime);
        var os = snapshot.Edition.StartsWith("Server", StringComparison.OrdinalIgnoreCase)
            ? Finding.Unknown("OS-VERSION-001", Module, Category, "Windows Server is not covered by the Windows lifecycle check", UnknownReason.Unsupported, $"{snapshot.ProductName} ({snapshot.Edition}) {snapshot.DisplayVersion}")
            : windows11 ? Windows11Support(snapshot, name, version, today) : Windows10Support(version, today);

        var days = (int)snapshot.Uptime.TotalDays;
        var uptimeEvidence = new Evidence("system uptime", $"Running for {N(days)} days since the last restart");
        var uptime = days >= 14
            ? new Finding
            {
                Id = "OS-UPTIME-001",
                Module = Module,
                Category = Category.Performance,
                Severity = Severity.Information,
                Title = new Message("The PC has not restarted for {days} days", ("days", days)),
                Evidence = [uptimeEvidence],
                SuspectedCause = "Fast Startup or sleep may keep Windows from restarting fully, which can postpone updates.",
                Recommendation = new("Restart the PC (Start → Power → Restart)."),
            }
            : new Finding { Id = "OS-UPTIME-001", Module = Module, Category = Category.Performance, Severity = Severity.Healthy, Title = "The PC was restarted recently", Evidence = [uptimeEvidence] };

        return [os, uptime];
    }

    private Finding Windows11Support(OsSnapshot snapshot, string name, Evidence version, DateOnly today)
    {
        var end = WindowsLifecycle.EndOfUpdates(snapshot.DisplayVersion, snapshot.Edition);
        if (end is null)
        {
            // A version newer than the table: assume supported, but say the date is not known to this release.
            return new Finding { Id = "OS-VERSION-001", Module = Module, Category = Category, Severity = Severity.Healthy, Confidence = Confidence.Medium, Title = new Message("{windows} {version} is a current Windows version", ("windows", name), ("version", snapshot.DisplayVersion)), Evidence = [version] };
        }

        var lifecycle = new Evidence("Microsoft lifecycle (" + WindowsLifecycle.TableVersion + ")", $"Version {snapshot.DisplayVersion} ({snapshot.Edition}) receives updates until {Date(end.Value)}");
        var daysLeft = end.Value.DayNumber - today.DayNumber;
        if (daysLeft < 0)
        {
            return new Finding
            {
                Id = "OS-VERSION-001",
                Module = Module,
                Category = Category.Security,
                Severity = Severity.Warning,
                Title = new Message("Windows 11 {version} no longer receives security updates", ("version", snapshot.DisplayVersion)),
                Evidence = [version, lifecycle],
                SuspectedCause = "The feature update to a newer Windows 11 version has not been installed yet.",
                Recommendation = new("Install the latest Windows 11 version: Settings → Windows Update → Check for updates. Make a backup first."),
            };
        }

        return daysLeft <= 60
            ? new Finding
            {
                Id = "OS-VERSION-001",
                Module = Module,
                Category = Category.Security,
                Severity = Severity.Information,
                Title = new Message("Windows 11 {version} stops receiving updates in {days} days", ("version", snapshot.DisplayVersion), ("days", daysLeft)),
                Evidence = [version, lifecycle],
                Recommendation = new("Plan the update to the latest Windows 11 version: Settings → Windows Update."),
            }
            : new Finding { Id = "OS-VERSION-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = new Message("{windows} {version} is supported and receives security updates", ("windows", name), ("version", snapshot.DisplayVersion)), Evidence = [version, lifecycle] };
    }

    private Finding Windows10Support(Evidence version, DateOnly today) => new()
    {
        Id = "OS-VERSION-001",
        Module = Module,
        Category = Category.Security,
        Severity = Severity.Warning,
        Title = "Windows 10 no longer receives regular security updates",
        Evidence = [version, new Evidence("Microsoft lifecycle", $"Regular support for Windows 10 ended on {Date(WindowsLifecycle.Windows10EndOfSupport)} ({N(today.DayNumber - WindowsLifecycle.Windows10EndOfSupport.DayNumber)} days ago)")],
        SuspectedCause = "Updates only continue on PCs enrolled in Extended Security Updates (ESU).",
        Recommendation = new("Upgrade to Windows 11 if this PC supports it; otherwise check whether it is enrolled in Extended Security Updates."),
    };
}

/// <summary>Pending restarts and how recently updates were installed.</summary>
public sealed class UpdateCheck(ISnapshotSource<UpdateSnapshot> source) : SnapshotCheck<UpdateSnapshot>(source)
{
    public override string Module => "updates";

    public override Message Name => "Check of Windows updates";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "UPDATE-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(UpdateSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        var restart = snapshot.RestartPending
            ? new Finding
            {
                Id = "UPDATE-RESTART-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = "Windows is waiting for a restart to finish installing updates",
                Evidence = [.. snapshot.RestartReasons.Select(r => new Evidence("registry", r))],
                Recommendation = new("Restart the PC to complete the updates."),
            }
            : new Finding { Id = "UPDATE-RESTART-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "No restart is pending for updates", Evidence = [new Evidence("registry", "No reboot-pending markers found")] };

        if (snapshot.LastUpdateInstalled is not { } last)
        {
            return [restart, Finding.Unknown("UPDATE-RECENT-001", Module, Category, "The date of the last installed update is not known", UnknownReason.Unsupported, "Windows reported no installed updates with a date")];
        }

        var age = DateOnly.FromDateTime(context.Now.UtcDateTime).DayNumber - last.DayNumber;
        var evidence = new Evidence("Get-HotFix", $"Most recent update {snapshot.LastUpdateId ?? "?"} installed on {Date(last)} ({N(age)} days ago)");
        var recent = age switch
        {
            > 60 => new Finding
            {
                Id = "UPDATE-RECENT-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("No Windows update has been installed for {days} days", ("days", age)),
                Evidence = [evidence],
                SuspectedCause = "Updates may be paused, failing, or blocked by too little disk space.",
                Recommendation = new("Open Settings → Windows Update and select Check for updates."),
            },
            > 35 => new Finding { Id = "UPDATE-RECENT-001", Module = Module, Category = Category, Severity = Severity.Information, Title = new Message("The last Windows update was installed {days} days ago", ("days", age)), Evidence = [evidence], Recommendation = new("Check Settings → Windows Update for new updates.") },
            _ => new Finding { Id = "UPDATE-RECENT-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Windows updates were installed recently", Evidence = [evidence] },
        };

        return [restart, recent];
    }
}

/// <summary>Read-only component store check (DISM /CheckHealth). Needs administrator rights.</summary>
public sealed class IntegrityCheck(ISnapshotSource<IntegritySnapshot> source) : SnapshotCheck<IntegritySnapshot>(source)
{
    public override string Module => "windows-integrity";

    public override Message Name => "Check of Windows system files";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "WIN-INTEGRITY-001";

    public override TimeSpan TimeLimit => TimeSpan.FromSeconds(120);

    public override IReadOnlyList<Finding> Evaluate(IntegritySnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var evidence = new Evidence("DISM /Online /Cleanup-Image /CheckHealth", snapshot.Summary);
        return snapshot.State switch
        {
            ComponentStoreState.NoCorruption => [new Finding { Id = "WIN-INTEGRITY-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "Windows system files show no damage", Evidence = [evidence] }],
            ComponentStoreState.Repairable => [new Finding
            {
                Id = "WIN-INTEGRITY-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Critical,
                Title = "Windows system files are damaged, but can be repaired",
                Evidence = [evidence],
                SuspectedCause = "Possibly an interrupted update or a failing disk.",
                Recommendation = new("Repair the component store (DISM /RestoreHealth), then check the protected files (sfc /scannow).", RequiresAdmin: true),
            }],
            ComponentStoreState.NotRepairable => [new Finding
            {
                Id = "WIN-INTEGRITY-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Critical,
                Title = "Windows system files are damaged and cannot be repaired automatically",
                Evidence = [evidence],
                Recommendation = new("Back up your files and contact a Shiva technician; an in-place repair of Windows is likely needed."),
            }],
            _ => [Finding.Unknown("WIN-INTEGRITY-001", Module, Category, "The result of the Windows file check was not recognised", UnknownReason.Error, snapshot.Summary)],
        };
    }
}

/// <summary>Reports whether the scan had administrator rights, so the reader knows which checks were limited.</summary>
public sealed class PrivilegeCheck : ICheck
{
    public string Module => "scan";

    public Message Name => "Check of administrator rights";

    public Category Category => Category.WindowsSystem;

    public string FailureId => "SCAN-PRIVILEGE-001";

    public TimeSpan TimeLimit => TimeSpan.FromSeconds(1);

    public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Finding finding = context.IsElevated
            ? new() { Id = "SCAN-PRIVILEGE-001", Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The scan ran with administrator rights", Evidence = [new Evidence("process token", "Elevated")] }
            : new()
            {
                Id = "SCAN-PRIVILEGE-001",
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = "The scan ran without administrator rights, so some checks were skipped",
                Evidence = [new Evidence("process token", "Not elevated")],
                Recommendation = new("For a complete scan, right-click Shiva Assist and choose Run as administrator."),
            };
        return Task.FromResult<IReadOnlyList<Finding>>([finding]);
    }
}

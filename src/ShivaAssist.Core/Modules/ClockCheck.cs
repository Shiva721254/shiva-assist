using System.Globalization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Core.Modules;

/// <summary>
/// "Websites say my connection is not private" and failing sign-ins are often just a wrong clock. Compares the
/// clock with an internet time server; two minutes or more off, or time sync switched off, is repairable
/// (catalogue TIME.SYNC.REPAIR) and verified by measuring again.
/// </summary>
public sealed class ClockCheck(ISnapshotSource<TimeSnapshot> source) : SnapshotCheck<TimeSnapshot>(source)
{
    public const string FindingId = "CLOCK-SYNC-001";

    /// <summary>Two minutes: well past normal drift, early enough to catch it before sign-ins fail.</summary>
    public const double MaxOffsetSeconds = 120;

    public override string Module => "clock";

    public override Message Name => "Check of the clock";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "CLOCK-CHECK-000";

    public override TimeSpan TimeLimit => TimeSpan.FromSeconds(30);

    public override IReadOnlyList<Finding> Evaluate(TimeSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var evidence = new Evidence("w32tm", string.Create(CultureInfo.InvariantCulture,
            $"Windows Time service {snapshot.Service.ToString().ToLowerInvariant()}; offset to {snapshot.Server}: {(snapshot.OffsetSeconds is { } o ? o.ToString("0.###", CultureInfo.InvariantCulture) + " s" : "no answer")}"));

        if (snapshot.Service == TimeServiceState.Missing)
        {
            return [Finding.Unknown("CLOCK-SYNC-001", Module, Category, "The clock could not be checked on this PC", UnknownReason.Unsupported, "Windows Time service not found")];
        }

        if (snapshot.OffsetSeconds is { } offset && Math.Abs(offset) >= MaxOffsetSeconds)
        {
            var minutes = (int)Math.Round(Math.Abs(offset) / 60);
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("The PC clock is about {minutes} minutes off", ("minutes", Math.Max(2, minutes))),
                SuspectedCause = "Windows did not synchronise its clock with the internet, for example because time synchronisation is switched off or the clock battery is weak.",
                Recommendation = new("Synchronise the clock with the internet time server."),
                Evidence = [evidence],
            }];
        }

        if (snapshot.Service == TimeServiceState.Disabled)
        {
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = "Automatic clock synchronisation is switched off",
                SuspectedCause = "The Windows Time service is disabled, so the clock slowly drifts and is never corrected.",
                Recommendation = new("Synchronise the clock with the internet time server."),
                Evidence = [evidence],
            }];
        }

        if (snapshot.OffsetSeconds is null)
        {
            return [Finding.Unknown("CLOCK-SYNC-001", Module, Category, "The clock could not be compared with an internet time server", UnknownReason.Error, "No answer from the time server (offline or blocked)")];
        }

        return [new Finding { Id = FindingId, Module = Module, Category = Category, Severity = Severity.Healthy, Title = "The PC clock is correct", Evidence = [evidence] }];
    }
}

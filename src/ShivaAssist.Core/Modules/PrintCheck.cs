using System.Globalization;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Core.Modules;

/// <summary>
/// "The printer does not print": a stopped Print Spooler or stuck print jobs block every document behind them.
/// Both are repaired by restarting the spooler and clearing the queue (catalogue PRINT.SPOOLER.RESET).
/// </summary>
public sealed class PrintCheck(ISnapshotSource<PrintSnapshot> source) : SnapshotCheck<PrintSnapshot>(source)
{
    public const string FindingId = "PRINT-SPOOLER-001";

    public override string Module => "printing";

    public override Message Name => "Check of printing";

    public override Category Category => Category.Drivers;

    public override string FailureId => "PRINT-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(PrintSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var evidence = new Evidence("Print Spooler", string.Create(CultureInfo.InvariantCulture,
            $"service {snapshot.Spooler.ToString().ToLowerInvariant()}, start type {snapshot.StartType}, {snapshot.Printers} printer(s), {snapshot.StuckJobs} stuck job(s), oldest {snapshot.OldestStuckMinutes} min"));

        Finding Healthy(Message title) => new()
        {
            Id = FindingId, Module = Module, Category = Category, Severity = Severity.Healthy, Title = title, Evidence = [evidence],
        };

        if (snapshot.Spooler == SpoolerState.Missing)
        {
            return [Healthy(new Message("Printing is not installed on this PC"))];
        }

        if (snapshot.Printers == 0 && snapshot.Spooler != SpoolerState.Running)
        {
            return [Healthy(new Message("No printers are set up on this PC"))];
        }

        if (snapshot.Spooler != SpoolerState.Running)
        {
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = "The print service is not running, so printing does not work",
                SuspectedCause = "The Print Spooler service stopped or was switched off. A faulty printer driver or a stuck print job can make it stop.",
                Recommendation = new("Restart the print service and clear stuck print jobs."),
                Evidence = [evidence],
            }];
        }

        if (snapshot.StuckJobs > 0)
        {
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("{count} print jobs are stuck in the print queue", ("count", snapshot.StuckJobs)),
                SuspectedCause = "A print job that fails or waits too long blocks every document behind it.",
                Recommendation = new("Restart the print service and clear stuck print jobs."),
                Evidence = [evidence],
            }];
        }

        return [Healthy(new Message("Printing works ({count} printers set up)", ("count", snapshot.Printers)))];
    }
}

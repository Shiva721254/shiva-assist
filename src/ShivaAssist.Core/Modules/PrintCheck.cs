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
    public const string QueueFindingId = "PRINT-QUEUE-001";

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

        var findings = new List<Finding>();
        if (snapshot.StuckJobs > 0)
        {
            findings.Add(new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = new Message("{count} print jobs are stuck in the print queue", ("count", snapshot.StuckJobs)),
                SuspectedCause = "A print job that fails or waits too long blocks every document behind it.",
                Recommendation = new("Restart the print service and clear stuck print jobs."),
                Evidence = [evidence],
            });
        }
        else
        {
            findings.Add(Healthy(new Message("Printing works ({count} printers set up)", ("count", snapshot.Printers))));
        }

        if (snapshot.PausedOrOfflinePrinters is { Count: > 0 } blocked)
        {
            findings.Add(new Finding
            {
                Id = QueueFindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = blocked.Count == 1
                    ? new Message("A printer is paused or set to work offline")
                    : new Message("{count} printers are paused or set to work offline", ("count", blocked.Count)),
                SuspectedCause = "When a printer is paused or switched to offline mode in Windows, new documents stay waiting in the queue.",
                Recommendation = new("Resume the paused or offline printer queue so waiting documents can print."),
                Evidence = [.. blocked.Select(p => new Evidence("Win32_Printer", p))],
            });
        }
        else if (snapshot.PausedOrOfflinePrinters is not null && snapshot.Printers > 0)
        {
            findings.Add(new Finding
            {
                Id = QueueFindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Healthy,
                Title = "No printers are paused or set to work offline",
                Evidence = [new Evidence("Win32_Printer", string.Create(CultureInfo.InvariantCulture, $"{snapshot.Printers} printer(s) checked, none paused or offline"))],
            });
        }

        return findings;
    }
}

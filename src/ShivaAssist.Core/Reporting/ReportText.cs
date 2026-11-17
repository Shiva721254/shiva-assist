using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Core.Reporting;

/// <summary>One line of the human-readable report; the severity lets the host colour it.</summary>
public sealed record ReportLine(string Text, Severity? Severity = null, bool Heading = false);

/// <summary>
/// Turns a report into plain-language lines (architecture 5.3 "progressive detail"): layer 1 is the title and one
/// recommended action; layer 2, with <c>details</c>, adds the evidence and suspected cause.
/// </summary>
public static class ReportText
{
    public static Message CategoryName(Category category) => category switch
    {
        Category.Hardware => "Hardware",
        Category.WindowsSystem => "Windows system",
        Category.Drivers => "Drivers and devices",
        Category.Security => "Security",
        Category.Network => "Network and internet",
        Category.Performance => "Performance",
        Category.Applications => "Programs",
        Category.BootAndRecovery => "Boot and recovery",
        _ => new Message(category.ToString()),
    };

    public static Message SeverityName(Severity severity) => severity switch
    {
        Severity.Critical => "Critical",
        Severity.Warning => "Warning",
        Severity.Unknown => "Unknown",
        Severity.Information => "Information",
        _ => "OK",
    };

    public static string Tag(Severity severity) => severity switch
    {
        Severity.Critical => "CRITICAL",
        Severity.Warning => "WARNING ",
        Severity.Unknown => "UNKNOWN ",
        Severity.Information => "INFO    ",
        _ => "OK      ",
    };

    public static Message ReasonText(UnknownReason reason) => reason switch
    {
        UnknownReason.RequiresAdmin => "needs administrator rights",
        UnknownReason.Unsupported => "not available on this PC",
        UnknownReason.Timeout => "took too long",
        UnknownReason.AccessDenied => "access denied by Windows",
        _ => "an error occurred",
    };

    public static IReadOnlyList<ReportLine> Render(ScanReport report, bool details, TextCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var t = catalog ?? TextCatalog.English;
        var lines = new List<ReportLine>();

        foreach (var group in report.Findings
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Min(f => ScanRunner.SeverityRank(f.Severity)))
            .ThenBy(g => g.Key))
        {
            lines.Add(new ReportLine(CategoryName(group.Key).Render(t), Heading: true));
            foreach (var finding in group)
            {
                var title = finding.Severity == Severity.Unknown && finding.UnknownReason is { } reason
                    ? $"{finding.Title.Render(t)} ({ReasonText(reason).Render(t)})"
                    : finding.Title.Render(t);
                lines.Add(new ReportLine($"  {Tag(finding.Severity)} {title}", finding.Severity));

                if (finding.Severity is not Severity.Healthy && finding.Recommendation is { } advice)
                {
                    lines.Add(new ReportLine($"           → {advice.Summary.Render(t)}"));
                }

                if (details)
                {
                    lines.AddRange(finding.Evidence.Select(e => new ReportLine($"           · {e.Observation}  [{e.Source}]")));
                    if (finding.SuspectedCause is { } cause)
                    {
                        lines.Add(new ReportLine($"           ? {new Message("Possible cause: {cause}", ("cause", cause.Render(t))).Render(t)}"));
                    }

                    lines.Add(new ReportLine($"           # {finding.Id}"));
                }
            }

            lines.Add(new ReportLine(string.Empty));
        }

        var counts = report.CountBySeverity;
        lines.Add(new ReportLine(
            new Message(
                "Summary: critical {critical} · warnings {warning} · info {info} · OK {ok} · unknown {unknown} ({seconds} s)",
                ("critical", counts[Severity.Critical]),
                ("warning", counts[Severity.Warning]),
                ("info", counts[Severity.Information]),
                ("ok", counts[Severity.Healthy]),
                ("unknown", counts[Severity.Unknown]),
                ("seconds", Math.Round(report.DurationSeconds, 1))).Render(t),
            Heading: true));
        return lines;
    }
}

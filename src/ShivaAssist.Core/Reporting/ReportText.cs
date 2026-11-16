using System.Globalization;
using ShivaAssist.Core.Findings;
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
    public static string CategoryName(Category category) => category switch
    {
        Category.Hardware => "Hardware",
        Category.WindowsSystem => "Windows system",
        Category.Drivers => "Drivers and devices",
        Category.Security => "Security",
        Category.Network => "Network and internet",
        Category.Performance => "Performance",
        Category.Applications => "Programs",
        Category.BootAndRecovery => "Boot and recovery",
        _ => category.ToString(),
    };

    public static string Tag(Severity severity) => severity switch
    {
        Severity.Critical => "CRITICAL",
        Severity.Warning => "WARNING ",
        Severity.Unknown => "UNKNOWN ",
        Severity.Information => "INFO    ",
        _ => "OK      ",
    };

    public static string ReasonText(UnknownReason reason) => reason switch
    {
        UnknownReason.RequiresAdmin => "needs administrator rights",
        UnknownReason.Unsupported => "not available on this PC",
        UnknownReason.Timeout => "took too long",
        UnknownReason.AccessDenied => "access denied by Windows",
        _ => "an error occurred",
    };

    public static IReadOnlyList<ReportLine> Render(ScanReport report, bool details)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<ReportLine>();

        foreach (var group in report.Findings
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Min(f => ScanRunner.SeverityRank(f.Severity)))
            .ThenBy(g => g.Key))
        {
            lines.Add(new ReportLine(CategoryName(group.Key), Heading: true));
            foreach (var finding in group)
            {
                var title = finding.Severity == Severity.Unknown && finding.UnknownReason is { } reason
                    ? $"{finding.Title} ({ReasonText(reason)})"
                    : finding.Title;
                lines.Add(new ReportLine($"  {Tag(finding.Severity)} {title}", finding.Severity));

                if (finding.Severity is not Severity.Healthy && finding.Recommendation is { } advice)
                {
                    lines.Add(new ReportLine($"           → {advice.Summary}"));
                }

                if (details)
                {
                    lines.AddRange(finding.Evidence.Select(e => new ReportLine($"           · {e.Observation}  [{e.Source}]")));
                    if (finding.SuspectedCause is { } cause)
                    {
                        lines.Add(new ReportLine($"           ? Possible cause: {cause}"));
                    }

                    lines.Add(new ReportLine($"           # {finding.Id}"));
                }
            }

            lines.Add(new ReportLine(string.Empty));
        }

        var counts = report.CountBySeverity;
        string C(Severity s) => counts[s].ToString(CultureInfo.InvariantCulture);
        lines.Add(new ReportLine(
            $"Summary: {C(Severity.Critical)} critical · {C(Severity.Warning)} warnings · {C(Severity.Information)} info · {C(Severity.Healthy)} OK · {C(Severity.Unknown)} unknown  ({report.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s)",
            Heading: true));
        return lines;
    }
}

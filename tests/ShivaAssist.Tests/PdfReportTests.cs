using System.Text;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class PdfReportTests
{
    internal static ScanReport Sample(int extra = 0) => new()
    {
        Product = "Shiva Assist test",
        StartedAt = Ctx.Now,
        DurationSeconds = 12.3,
        System = new ScannedSystem("Windows 11 Pro", "24H2 (build 26100)", true),
        Findings =
        [
            new() { Id = "STORAGE-SPACE-001", Module = "storage", Category = Category.Hardware, Severity = Severity.Critical, Title = "The Windows drive is almost full (3.0 GB free)", SuspectedCause = "Downloads, (old) updates and temporary files.", Recommendation = new("Free up space: Settings → System → Storage.") },
            new() { Id = "PERF-STARTUP-001", Module = "startup", Category = Category.Performance, Severity = Severity.Warning, Title = "17 programs start automatically with Windows" },
            new() { Id = "BATTERY-WEAR-001", Module = "battery", Category = Category.Hardware, Severity = Severity.Unknown, UnknownReason = UnknownReason.Unsupported, Title = "Battery wear could not be measured" },
            new() { Id = "SECURITY-AV-001", Module = "security", Category = Category.Security, Severity = Severity.Healthy, Title = "Microsoft Defender protection is on and up to date" },
            .. Enumerable.Range(1, extra).Select(i => new Finding { Id = $"APP-CRASH-{i:000}", Module = "stability", Category = Category.Applications, Severity = Severity.Warning, Title = $"Program {i} crashed several times this week" }),
        ],
    };

    [Fact]
    public void ProducesAValidPdfWithXrefAndFooter()
    {
        var pdf = PdfReport.Render(Sample(), TextCatalog.English);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("/Count 1", text, StringComparison.Ordinal);
        Assert.Contains("Needs your attention", text, StringComparison.Ordinal);
        Assert.Contains("page 1 of 1", text, StringComparison.Ordinal);
        var startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", text[startxref..], StringComparison.Ordinal);
    }

    [Fact]
    public void LongReportsGetMorePagesAndDutchAccentsSurvive()
    {
        var pdf = PdfReport.Render(Sample(extra: 40), TextCatalog.For("nl"));
        var text = Encoding.Latin1.GetString(pdf);
        Assert.DoesNotContain("/Count 1 ", text, StringComparison.Ordinal);
        Assert.Contains("pagina", text, StringComparison.Ordinal);
    }
}

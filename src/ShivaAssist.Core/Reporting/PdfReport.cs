using System.Globalization;
using System.Text;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;

namespace ShivaAssist.Core.Reporting;

/// <summary>
/// The scan report as a customer-friendly A4 PDF, written without external libraries: standard Helvetica fonts
/// (WinAnsi encoding, so Dutch accents work), solid colour bands and one card per finding. Problems first, then
/// what is good to know, then a short list of what is OK.
/// </summary>
public static class PdfReport
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 48;
    private const double ContentWidth = PageWidth - (2 * Margin);

    private static readonly Rgb Green = new(0x2E, 0x9E, 0x5B);
    private static readonly Rgb GreenDark = new(0x1F, 0x7A, 0x44);
    private static readonly Rgb Ink = new(0x2F, 0x3B, 0x33);
    private static readonly Rgb Muted = new(0x6E, 0x6A, 0x5E);
    private static readonly Rgb Line = new(0xE8, 0xE0, 0xCF);
    private static readonly Rgb Cream = new(0xFB, 0xF7, 0xEE);

    public static byte[] Render(ScanReport report, TextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(catalog);
        var writer = new PageWriter(catalog);
        var culture = CultureInfo.GetCultureInfo(catalog.Language.Code);
        string T(Message m) => m.Render(catalog);

        // Header band
        writer.Rect(0, PageHeight - 96, PageWidth, 96, Green);
        writer.Text(Margin, PageHeight - 46, "Shiva Assist", 22, bold: true, Rgb.White);
        writer.Text(Margin, PageHeight - 66, T(new Message("PC check-up report")), 12, bold: false, new Rgb(0xE3, 0xF5, 0xE9));
        writer.Y = PageHeight - 124;
        var when = report.StartedAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm", culture);
        writer.Paragraph(T(new Message("Checked on {when} · {system}", ("when", when), ("system", $"{report.System.OsName} {report.System.OsVersion}"))), 10, false, Muted);
        writer.Y -= 10;

        // Summary tiles: solid colours, one per severity
        var counts = report.CountBySeverity;
        (Severity Severity, Message Label)[] tiles =
        [
            (Severity.Critical, new Message("Critical")),
            (Severity.Warning, new Message("Warnings")),
            (Severity.Information, new Message("Information")),
            (Severity.Healthy, new Message("OK")),
        ];
        var tileWidth = (ContentWidth - 24) / 4;
        for (var i = 0; i < tiles.Length; i++)
        {
            var x = Margin + (i * (tileWidth + 8));
            writer.Rect(x, writer.Y - 54, tileWidth, 54, Colour(tiles[i].Severity));
            writer.Text(x + 12, writer.Y - 26, counts[tiles[i].Severity].ToString(CultureInfo.InvariantCulture), 20, bold: true, Rgb.White);
            writer.Text(x + 12, writer.Y - 44, T(tiles[i].Label), 10, bold: false, Rgb.White);
        }

        writer.Y -= 78;

        Section(writer, T(new Message("Needs your attention")), report.Findings.Where(f => f.Severity is Severity.Critical or Severity.Warning), catalog);
        Section(writer, T(new Message("Good to know")), report.Findings.Where(f => f.Severity is Severity.Information or Severity.Unknown), catalog);

        var healthy = report.Findings.Where(f => f.Severity == Severity.Healthy).ToList();
        if (healthy.Count > 0)
        {
            writer.Heading(T(new Message("What is OK")));
            foreach (var finding in healthy)
            {
                writer.Ensure(16);
                writer.Rect(Margin, writer.Y - 8, 6, 6, Green);
                writer.Paragraph(finding.Title.Render(catalog), 10, false, Ink, indent: 14);
            }
        }

        writer.Y -= 12;
        writer.Ensure(40);
        writer.Rect(Margin, writer.Y - 34, ContentWidth, 34, Cream);
        writer.Text(Margin + 12, writer.Y - 21, T(new Message("Need help? Shiva Computers can repair it for you: shivacomputers.nl")), 10, bold: true, GreenDark);
        return writer.Finish();
    }

    private static void Section(PageWriter writer, string heading, IEnumerable<Finding> findings, TextCatalog catalog)
    {
        var list = findings.OrderBy(f => Scanning.ScanRunner.SeverityRank(f.Severity)).ToList();
        if (list.Count == 0)
        {
            return;
        }

        writer.Heading(heading);
        foreach (var finding in list)
        {
            var colour = Colour(finding.Severity);
            var badge = ReportText.SeverityName(finding.Severity).Render(catalog);
            var title = finding.Title.Render(catalog);
            if (finding.Severity == Severity.Unknown && finding.UnknownReason is { } reason)
            {
                title += $" ({ReportText.ReasonText(reason).Render(catalog)})";
            }

            var titleLines = PageWriter.Wrap(title, 11.5, ContentWidth - 30);
            var advice = finding.Recommendation is { } r ? PageWriter.Wrap("» " + r.Summary.Render(catalog), 10, ContentWidth - 30) : [];
            var cause = finding.SuspectedCause is { } c
                ? PageWriter.Wrap(new Message("Possible cause: {cause}", ("cause", c.Render(catalog))).Render(catalog), 9, ContentWidth - 30)
                : [];
            var height = 30 + (titleLines.Count * 15) + (advice.Count * 13) + (cause.Count * 12) + (advice.Count > 0 ? 4 : 0) + (cause.Count > 0 ? 4 : 0);

            writer.Ensure(height + 8);
            var top = writer.Y;
            writer.Rect(Margin, top - height, ContentWidth, height, Rgb.White, Line);
            writer.Rect(Margin, top - height, 5, height, colour);

            var badgeWidth = PageWriter.Measure(badge, 8.5, bold: true) + 12;
            writer.Rect(Margin + 18, top - 21, badgeWidth, 14, colour);
            writer.Text(Margin + 24, top - 17, badge, 8.5, bold: true, Rgb.White);

            var y = top - 37;
            foreach (var line in titleLines)
            {
                writer.Text(Margin + 18, y, line, 11.5, bold: true, Ink);
                y -= 15;
            }

            y -= advice.Count > 0 ? 2 : 0;
            foreach (var line in advice)
            {
                writer.Text(Margin + 18, y, line, 10, bold: false, Ink);
                y -= 13;
            }

            y -= cause.Count > 0 ? 3 : 0;
            foreach (var line in cause)
            {
                writer.Text(Margin + 18, y, line, 9, bold: false, Muted);
                y -= 12;
            }

            writer.Y = top - height - 10;
        }

        writer.Y -= 6;
    }

    /// <summary>Bright, solid colours per severity (same family as the app).</summary>
    private static Rgb Colour(Severity severity) => severity switch
    {
        Severity.Critical => new Rgb(0xD9, 0x47, 0x2B),
        Severity.Warning => new Rgb(0xE8, 0x89, 0x0C),
        Severity.Information => new Rgb(0x2B, 0x8F, 0xB3),
        Severity.Unknown => new Rgb(0x8A, 0x85, 0x77),
        _ => Green,
    };

    private readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb White => new(0xFF, 0xFF, 0xFF);

        public string Fill => string.Create(CultureInfo.InvariantCulture, $"{R / 255.0:0.###} {G / 255.0:0.###} {B / 255.0:0.###}");
    }

    /// <summary>Collects drawing operators per page and assembles the PDF file.</summary>
    private sealed class PageWriter(TextCatalog catalog)
    {
        private readonly List<StringBuilder> pages = [new()];

        public double Y { get; set; }

        private StringBuilder Current => pages[^1];

        public void Rect(double x, double y, double width, double height, Rgb fill, Rgb? stroke = null)
        {
            Current.Append(Inv($"{fill.Fill} rg "));
            if (stroke is { } s)
            {
                Current.Append(Inv($"{s.Fill} RG 0.8 w {x:0.##} {y:0.##} {width:0.##} {height:0.##} re B\n"));
            }
            else
            {
                Current.Append(Inv($"{x:0.##} {y:0.##} {width:0.##} {height:0.##} re f\n"));
            }
        }

        public void Text(double x, double y, string text, double size, bool bold, Rgb colour) =>
            Current.Append(Inv($"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {colour.Fill} rg {x:0.##} {y:0.##} Td ({Escape(text)}) Tj ET\n"));

        public void Paragraph(string text, double size, bool bold, Rgb colour, double indent = 0)
        {
            foreach (var line in Wrap(text, size, ContentWidth - indent))
            {
                Ensure(size + 4);
                Text(Margin + indent, Y - size, line, size, bold, colour);
                Y -= size + 4;
            }
        }

        public void Heading(string text)
        {
            Ensure(60);
            Text(Margin, Y - 15, text, 15, bold: true, GreenDark);
            Rect(Margin, Y - 23, 36, 3, Green);
            Y -= 36;
        }

        /// <summary>Starts a new page when the next block does not fit.</summary>
        public void Ensure(double height)
        {
            if (Y - height >= Margin + 24)
            {
                return;
            }

            pages.Add(new StringBuilder());
            Y = PageHeight - Margin;
        }

        public byte[] Finish()
        {
            var footer = new Message("Shiva Assist by Shiva Computers · page {page} of {total}");
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                string.Empty, // pages, filled below
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            };
            var kids = new List<int>();
            for (var i = 0; i < pages.Count; i++)
            {
                var text = new Message(footer.Template, ("page", i + 1), ("total", pages.Count)).Render(catalog);
                var content = pages[i] + Inv($"BT /F1 8 Tf {Muted.Fill} rg {Margin:0.##} 28 Td ({Escape(text)}) Tj ET\n");
                objects.Add(Inv($"<< /Length {Encode(content).Length} >>\nstream\n{content}endstream"));
                var contentId = objects.Count;
                objects.Add(Inv($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>"));
                kids.Add(objects.Count);
            }

            objects[1] = Inv($"<< /Type /Pages /Kids [{string.Join(' ', kids.Select(k => Inv($"{k} 0 R")))}] /Count {kids.Count} >>");

            using var output = new MemoryStream();
            void Write(string s)
            {
                var bytes = Encode(s);
                output.Write(bytes, 0, bytes.Length);
            }

            Write("%PDF-1.4\n%âãÏÓ\n");
            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                Write(Inv($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"));
            }

            var xref = output.Position;
            Write(Inv($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
            foreach (var offset in offsets)
            {
                Write(Inv($"{offset:0000000000} 00000 n \n"));
            }

            Write(Inv($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
            return output.ToArray();
        }

        /// <summary>Greedy word wrap using approximate Helvetica widths.</summary>
        public static List<string> Wrap(string text, double size, double width)
        {
            var lines = new List<string>();
            var line = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(candidate, size, bold: false) > width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }

            if (line.Length > 0)
            {
                lines.Add(line.ToString());
            }

            return lines;
        }

        public static double Measure(string text, double size, bool bold)
        {
            double em = 0;
            foreach (var ch in text)
            {
                em += ch switch
                {
                    ' ' or '.' or ',' or ':' or ';' or '\'' or '!' or '|' => 0.28,
                    'i' or 'l' or 'j' or 't' or 'f' or 'r' or 'I' or '(' or ')' or '-' => 0.33,
                    'm' or 'w' or 'M' or 'W' => 0.85,
                    _ when char.IsUpper(ch) || char.IsDigit(ch) => 0.66,
                    _ => 0.55,
                };
            }

            return em * size * (bold ? 1.07 : 1.0);
        }

        private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

        /// <summary>WinAnsi (Windows-1252) bytes; characters outside it become close equivalents or '?'.</summary>
        private static byte[] Encode(string text)
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                bytes[i] = ch switch
                {
                    '€' => 0x80,
                    '…' => 0x85,
                    '‘' => 0x91,
                    '’' => 0x92,
                    '“' => 0x93,
                    '”' => 0x94,
                    '•' => 0x95,
                    '–' => 0x96,
                    '—' => 0x97,
                    '→' => 0xBB,
                    _ when ch < 0x80 || (ch >= 0xA0 && ch <= 0xFF) => (byte)ch,
                    _ => (byte)'?',
                };
            }

            return bytes;
        }
    }
}

using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;

namespace ShivaAssist.Cli;

/// <summary>Console presentation: colours, progress and the closing advice. All wording comes from <see cref="ReportText"/>.</summary>
internal sealed class ConsoleView(bool plain, TextCatalog catalog)
{
    public const string HelpUrl = "https://shivacomputers.nl/en/contact";

    public void Banner(string product)
    {
        Write(product, ConsoleColor.Cyan);
        Console.WriteLine(" · read-only PC health check by Shiva Computers");
        Console.WriteLine("Nothing on this PC will be changed. The scan takes about a minute.");
        Console.WriteLine();
    }

    public void Progress(int done, int total)
    {
        if (!plain)
        {
            Console.Write($"\rChecking… {done}/{total}   ");
        }
    }

    public void EndProgress()
    {
        if (!plain)
        {
            Console.Write("\r                         \r");
        }
    }

    public void Report(ScanReport report, bool details)
    {
        ArgumentNullException.ThrowIfNull(report);
        Console.WriteLine($"{report.System.OsName} {report.System.OsVersion}{(report.System.Elevated ? " · administrator" : string.Empty)}");
        Console.WriteLine();

        foreach (var line in ReportText.Render(report, details, catalog))
        {
            if (line.Heading)
            {
                Write(line.Text + Environment.NewLine, ConsoleColor.White);
            }
            else if (line.Severity is { } severity)
            {
                Write(line.Text + Environment.NewLine, Colour(severity));
            }
            else
            {
                Console.WriteLine(line.Text);
            }
        }
    }

    public static void Saved(string path)
    {
        Console.WriteLine();
        Console.WriteLine($"Report saved: {path}");
    }

    public void NextStep(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Console.WriteLine();
        if (report.NeedsAttention)
        {
            Console.WriteLine("Follow the → advice above, starting at the top. Need a hand?");
            Console.WriteLine($"Shiva Computers can help remotely or on site: {HelpUrl}");
        }
        else
        {
            Write("Your PC looks healthy. Nothing needs attention right now." + Environment.NewLine, ConsoleColor.Green);
        }
    }

    public static void WaitForKey()
    {
        Console.WriteLine();
        Console.Write("Press any key to close…");
        Console.ReadKey(intercept: true);
    }

    private static ConsoleColor Colour(Severity severity) => severity switch
    {
        Severity.Critical => ConsoleColor.Red,
        Severity.Warning => ConsoleColor.Yellow,
        Severity.Unknown => ConsoleColor.DarkGray,
        Severity.Information => ConsoleColor.Cyan,
        _ => ConsoleColor.Green,
    };

    private void Write(string text, ConsoleColor colour)
    {
        if (plain)
        {
            Console.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }
}

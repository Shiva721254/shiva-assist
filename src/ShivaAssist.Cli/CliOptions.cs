namespace ShivaAssist.Cli;

internal sealed record CliOptions
{
    public bool Help { get; init; }

    public bool Version { get; init; }

    public bool Details { get; init; }

    public bool Plain { get; init; }

    public bool NoReport { get; init; }

    /// <summary>Keep the window open at the end; on by default when started by double-click.</summary>
    public bool Pause { get; init; }

    public string? ReportPath { get; init; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CliOptions { Pause = args.Count == 0 && !Console.IsInputRedirected };
        for (var i = 0; i < args.Count; i++)
        {
            options = args[i].ToLowerInvariant() switch
            {
                "-h" or "--help" or "/?" => options with { Help = true },
                "--version" => options with { Version = true },
                "-d" or "--details" => options with { Details = true },
                "--plain" => options with { Plain = true },
                "--no-report" => options with { NoReport = true },
                "--pause" => options with { Pause = true },
                "--report" when i + 1 < args.Count => options with { ReportPath = args[++i] },
                _ => throw new ArgumentException($"Unknown option: {args[i]}"),
            };
        }

        return options;
    }

    public static string HelpText(string product) => $"""
        {product}: a read-only health check for this Windows PC. It changes nothing.

        Usage: ShivaAssist [options]

          -d, --details      Show the evidence and possible cause for every finding
              --report FILE  Save the JSON report to FILE
                             (default: Documents\Shiva Assist\Reports\scan-<date>.json)
              --no-report    Do not save a report
              --plain        No colours (for logs)
              --pause        Wait for a key at the end
              --version      Show the version
          -h, --help         Show this help

        Run as administrator for the complete scan (system file check included).
        Exit codes: 0 = nothing needs attention, 2 = warnings or critical findings, 1 = invalid option.

        More information: https://assist.shivacomputers.nl
        """;
}

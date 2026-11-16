using System.Globalization;
using System.Reflection;
using System.Text;
using ShivaAssist.Cli;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;
using ShivaAssist.Core.Scanning;
using ShivaAssist.Windows;

// Shiva Assist, stage 1: a read-only scan. Nothing on the PC is changed; the only file written is the report.

var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
var product = $"Shiva Assist {version}";

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Run 'ShivaAssist --help' for the options.");
    return 1;
}

if (options.Help)
{
    Console.WriteLine(CliOptions.HelpText(product));
    return 0;
}

if (options.Version)
{
    Console.WriteLine(product);
    return 0;
}

Console.OutputEncoding = Encoding.UTF8;
var view = new ConsoleView(options.Plain || Console.IsOutputRedirected, TextCatalog.For(options.Language));
view.Banner(product);

var checks = WindowsPlatform.CreateChecks();
var runner = new ScanRunner(checks);
var done = 0;
runner.CheckCompleted += _ => view.Progress(Interlocked.Increment(ref done), checks.Count);

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

ScanReport report;
try
{
    report = await runner.RunAsync(WindowsPlatform.DescribeEnvironment(product), cancel.Token);
}
catch (OperationCanceledException)
{
    view.EndProgress();
    Console.WriteLine("Scan cancelled. Nothing was changed.");
    return 130;
}

view.EndProgress();
view.Report(report, options.Details);

if (!options.NoReport)
{
    var path = options.ReportPath ?? DefaultReportPath(report.StartedAt);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, ReportJson.Serialize(report));
        ConsoleView.Saved(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"The report could not be saved: {ex.Message}");
    }
}

view.NextStep(report);
if (options.Pause)
{
    ConsoleView.WaitForKey();
}

return report.Findings.Any(f => f.Severity is Severity.Critical or Severity.Warning) ? 2 : 0;

static string DefaultReportPath(DateTimeOffset started) => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "Shiva Assist",
    "Reports",
    $"scan-{started.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");

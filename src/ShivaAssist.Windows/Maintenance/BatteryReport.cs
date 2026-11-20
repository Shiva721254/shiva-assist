using ShivaAssist.Windows.Infrastructure;

namespace ShivaAssist.Windows.Maintenance;

/// <summary>Windows' own battery report (powercfg /batteryreport), written to Shiva Assist's folder. Read-only.</summary>
public static class BatteryReport
{
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiva Assist", "battery-report.html");

    /// <summary>True when the report was written; false on PCs without a battery.</summary>
    public static async Task<bool> CreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var (exit, _, _) = await ToolRunner.RunAsync(ToolRunner.System32("powercfg.exe"), ["/batteryreport", "/output", Path], cancellationToken);
        return exit == 0 && File.Exists(Path);
    }
}

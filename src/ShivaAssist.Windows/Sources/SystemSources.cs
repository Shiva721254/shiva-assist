using System.Globalization;
using Microsoft.Win32;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using ShivaAssist.Windows.Infrastructure;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>Windows version from the registry (read-only).</summary>
public sealed class OsSource : ISnapshotSource<OsSnapshot>
{
    public Task<OsSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?? throw new PlatformNotSupportedException("Windows version key not found.");

        string Text(string name) => key.GetValue(name)?.ToString() ?? string.Empty;
        int Number(string name) => key.GetValue(name) is int i ? i : int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        var version = Text("DisplayVersion");
        return Task.FromResult(new OsSnapshot(
            ProductName: Text("ProductName"),
            Edition: Text("EditionID"),
            DisplayVersion: version.Length > 0 ? version : Text("ReleaseId"),
            Build: Number("CurrentBuildNumber"),
            Revision: Number("UBR"),
            Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64)));
    }
}

/// <summary>Processor model and load, sampled three times one second apart.</summary>
public sealed class ProcessorSource : ISnapshotSource<ProcessorSnapshot>
{
    private const string Script = """
        $samples = @()
        for ($i = 0; $i -lt 3; $i++) {
            $load = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
            if ($null -ne $load) { $samples += [int][math]::Round($load) }
            if ($i -lt 2) { Start-Sleep -Seconds 1 }
        }
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        [pscustomobject]@{
            name = "$($cpu.Name)".Trim()
            cores = [int](Get-CimInstance Win32_Processor | Measure-Object -Property NumberOfCores -Sum).Sum
            threads = [int](Get-CimInstance Win32_Processor | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum
            samples = @($samples)
        } | ConvertTo-Json -Compress
        """;

    public async Task<ProcessorSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        return new ProcessorSnapshot(
            Str(json, "name"),
            (int)Long(json, "cores"),
            (int)Long(json, "threads"),
            [.. Items(json, "samples").Select(e => e.GetInt32())]);
    }
}

/// <summary>Physical memory and commit charge from Win32_OperatingSystem (values in kilobytes).</summary>
public sealed class MemorySource : ISnapshotSource<MemorySnapshot>
{
    private const string Script = """
        $os = Get-CimInstance Win32_OperatingSystem
        [pscustomobject]@{
            total = [long]$os.TotalVisibleMemorySize
            free = [long]$os.FreePhysicalMemory
            commitLimit = [long]$os.TotalVirtualMemorySize
            commitFree = [long]$os.FreeVirtualMemory
        } | ConvertTo-Json -Compress
        """;

    public async Task<MemorySnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var json = await QueryAsync(Script, cancellationToken);
        var limit = Long(json, "commitLimit") * 1024;
        return new MemorySnapshot(
            Long(json, "total") * 1024,
            Long(json, "free") * 1024,
            limit,
            limit - (Long(json, "commitFree") * 1024));
    }
}

/// <summary>Pending restarts (registry) and the most recent installed update (Get-HotFix).</summary>
public sealed class UpdateSource : ISnapshotSource<UpdateSnapshot>
{
    private const string Script = """
        $last = Get-HotFix | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending | Select-Object -First 1
        [pscustomobject]@{
            id = if ($last) { "$($last.HotFixID)" } else { $null }
            date = if ($last) { $last.InstalledOn.ToString('yyyy-MM-dd') } else { $null }
        } | ConvertTo-Json -Compress
        """;

    private static readonly (string Path, string Reason)[] RestartMarkers =
    [
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", "Component Based Servicing: restart pending"),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", "Windows Update: restart required"),
    ];

    public async Task<UpdateSnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        foreach (var (path, reason) in RestartMarkers)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is not null)
            {
                reasons.Add(reason);
            }
        }

        var json = await QueryAsync(Script, cancellationToken);
        var date = Str(json, "date");
        DateOnly? installed = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var id = Str(json, "id");
        return new UpdateSnapshot(reasons.Count > 0, reasons, installed, id.Length > 0 ? id : null);
    }
}

/// <summary>
/// <c>DISM /Online /Cleanup-Image /CheckHealth</c>: a quick, read-only check of the component store.
/// <c>/English</c> makes the output independent of the Windows display language.
/// </summary>
public sealed class IntegritySource : ISnapshotSource<IntegritySnapshot>
{
    public async Task<IntegritySnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsElevated)
        {
            throw new RequiresAdminException();
        }

        var (_, output, _) = await ToolRunner.RunAsync(ToolRunner.System32("dism.exe"), ["/Online", "/Cleanup-Image", "/CheckHealth", "/English"], cancellationToken);
        return DismOutput.Classify(output);
    }
}

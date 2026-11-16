using System.Security.Principal;
using Microsoft.Win32;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.Windows;

/// <summary>Composition of the stage 1 scan on Windows: every module with its Windows data source.</summary>
public static class WindowsPlatform
{
    public static IReadOnlyList<ICheck> CreateChecks() =>
    [
        new PrivilegeCheck(),
        new OsCheck(new OsSource()),
        new UpdateCheck(new UpdateSource()),
        new IntegrityCheck(new IntegritySource()),
        new SecurityCheck(new SecuritySource()),
        new StorageCheck(new StorageSource()),
        new MemoryCheck(new MemorySource()),
        new ProcessorCheck(new ProcessorSource()),
        new StartupCheck(new StartupSource()),
        new NetworkCheck(new NetworkSource()),
        new DeviceCheck(new DeviceSource()),
        new BatteryCheck(new BatterySource()),
        new StabilityCheck(new StabilitySource()),
    ];

    public static ScanEnvironment DescribeEnvironment(string product)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var name = key?.GetValue("ProductName")?.ToString() ?? "Windows";
        var build = int.TryParse(key?.GetValue("CurrentBuildNumber")?.ToString(), out var b) ? b : 0;
        if (build >= OsCheck.FirstWindows11Build)
        {
            name = name.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var version = $"{key?.GetValue("DisplayVersion")} (build {build}.{key?.GetValue("UBR")})";
        return new ScanEnvironment(product, name, version, IsElevated());
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

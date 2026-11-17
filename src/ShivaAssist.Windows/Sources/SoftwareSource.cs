using System.Security;
using Microsoft.Win32;
using ShivaAssist.Core.Software;

namespace ShivaAssist.Windows.Sources;

public sealed record SoftwareInventoryResult(IReadOnlyList<InstalledProgram> Programs, int UnreadableEntries);

/// <summary>Reads both registry views and per-user entries without invoking Win32_Product.</summary>
public static class SoftwareSource
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static SoftwareInventoryResult Read(CancellationToken cancellationToken)
    {
        var programs = new List<InstalledProgram>();
        var unreadable = 0;
        RegistryView[] views = Environment.Is64BitOperatingSystem
            ? [RegistryView.Registry64, RegistryView.Registry32] : [RegistryView.Registry32];
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in views)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(UninstallPath);
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            using var entry = uninstall.OpenSubKey(name);
                            if (entry is null || entry.GetValue("SystemComponent") is 1)
                            {
                                continue;
                            }

                            var displayName = entry.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName))
                            {
                                continue;
                            }

                            long? size = entry.GetValue("EstimatedSize") switch
                            {
                                int kilobytes => (long)unchecked((uint)kilobytes) * 1024,
                                long kilobytes when kilobytes >= 0 && kilobytes <= long.MaxValue / 1024 => kilobytes * 1024,
                                _ => null,
                            };
                            programs.Add(new InstalledProgram(displayName,
                                entry.GetValue("Publisher") as string ?? string.Empty,
                                entry.GetValue("DisplayVersion") as string ?? string.Empty,
                                size, SoftwareInventory.ParseInstallDate(entry.GetValue("InstallDate") as string)));
                        }
                        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                        {
                            unreadable++;
                        }
                    }
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    unreadable++;
                }
            }
        }

        return new SoftwareInventoryResult(SoftwareInventory.Normalize(programs), unreadable);
    }
}

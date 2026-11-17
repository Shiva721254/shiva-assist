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
                                size, SoftwareInventory.ParseInstallDate(entry.GetValue("InstallDate") as string))
                            {
                                Registration = (hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU") + "|"
                                    + (view == RegistryView.Registry64 ? "64" : "32") + "|" + name,
                                InstallLocation = entry.GetValue("InstallLocation") as string ?? string.Empty,
                            });
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

    public static RegisteredUninstaller ReadUninstaller(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var parts = reference.Split('|');
        if (parts.Length != 3 || parts[0] is not ("HKLM" or "HKCU") || parts[1] is not ("32" or "64")
            || string.IsNullOrWhiteSpace(parts[2]) || parts[2].Length > 256 || parts[2].IndexOfAny(['\\', '/', '\0']) >= 0)
        {
            throw new ArgumentException("Invalid program registration.");
        }

        using var root = RegistryKey.OpenBaseKey(parts[0] == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
            parts[1] == "64" ? RegistryView.Registry64 : RegistryView.Registry32);
        using var entry = root.OpenSubKey(UninstallPath + "\\" + parts[2]);
        if (entry is null || entry.GetValue("SystemComponent") is 1 || entry.GetValue("NoRemove") is 1
            || entry.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("This program has no supported registered uninstaller. Use Windows Settings instead.");
        }

        return new RegisteredUninstaller(reference, name, entry.GetValue("DisplayVersion") as string ?? string.Empty,
            entry.GetValue("Publisher") as string ?? string.Empty,
            entry.GetValue("InstallLocation") as string ?? string.Empty,
            entry.GetValue("UninstallString") as string ?? string.Empty);
    }

}

public sealed record RegisteredUninstaller(string Reference, string Name, string Version, string Publisher, string InstallLocation, string Command);

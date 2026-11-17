using Microsoft.Win32;

// A disposable uninstaller fixture, never part of the customer package.
const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ShivaAssistUninstallFixture";
var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Shiva Assist Uninstaller Fixture");
if (args.Length != 1 || args[0] != "--uninstall"
    || !string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), expected, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("This fixture only runs from its dedicated Program Files folder with --uninstall.");
    return 64;
}

Console.WriteLine("Shiva Assist test uninstaller. Type YES to uninstall, or press Enter to cancel.");
if (Console.ReadLine() != "YES")
{
    return 1602;
}

using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
using var registration = registry.OpenSubKey(key);
var scenario = registration?.GetValue("FixtureScenario") as string ?? "Normal";
if (scenario == "Failure")
{
    return 1603;
}

registry.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
// Deliberately retain the executable, logs, settings, and a personal document to test leftover classification.
Console.WriteLine("Fixture removed its registration. Leftovers were deliberately retained for testing.");
return scenario == "Restart" ? 3010 : 0;

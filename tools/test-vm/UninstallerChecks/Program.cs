using System.Diagnostics;
using Microsoft.Win32;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Windows;
using ShivaAssist.Windows.Repairs;
using ShivaAssist.Windows.Sources;

// Windows CI integration test: only the disposable fixture, never an existing application.
if (args.Length != 1 || !File.Exists(args[0]) || !WindowsPlatform.IsElevated())
{
    throw new InvalidOperationException("Provide the published fixture executable and run elevated in the disposable CI VM.");
}

const string subkey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ShivaAssistUninstallFixture";
const string reference = "HKLM|64|ShivaAssistUninstallFixture";
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Shiva Assist Uninstaller Fixture");
using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
if (Directory.Exists(root) || registry.OpenSubKey(subkey) is not null)
{
    throw new InvalidOperationException("The fixture already exists; refusing to overwrite it.");
}

var sessions = new List<string>();
var recoveries = new List<string>();
Directory.CreateDirectory(root);
try
{
    var executable = Path.Combine(root, "ShivaUninstallFixture.exe");
    File.Copy(Path.GetFullPath(args[0]), executable);
    var cache = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
    var log = Path.Combine(cache, "trace.log");
    var settings = Path.Combine(root, "settings.json");
    File.WriteAllText(log, "must survive undo");
    File.WriteAllText(settings, "personal preferences");
    Directory.CreateDirectory(Path.Combine(root, "empty-folder"));
    Register();
    var descriptor = SoftwareSource.ReadUninstaller(reference);
    Check(descriptor.Name == "Shiva Assist Uninstaller Fixture", "Registry identity");
    const string productCode = "{12345678-1234-1234-1234-1234567890AB}";
    var msi = descriptor with { Reference = "HKLM|64|" + productCode, Command = "MsiExec.exe /I" + productCode };
    var msiLaunch = UninstallerService.LaunchInfo(msi);
    Check(msiLaunch.FileName == Path.Combine(Environment.SystemDirectory, "msiexec.exe")
        && msiLaunch.ArgumentList.SequenceEqual(new[] { "/x", productCode, "/norestart" }), "MSI executable and validated arguments");
    MustBlock(() => UninstallerService.LaunchInfo(msi with { Reference = reference }), "MSI identity mismatch blocked");

    var cancelled = Prepare(descriptor);
    var cancelExit = await Run(descriptor, string.Empty);
    UninstallerService.Finish(cancelled, cancelExit);
    Check(cancelExit == 1602, "Native cancellation exit");
    Check(!UninstallerService.RemovalVerified(cancelled), "Cancelled uninstall remains registered");
    MustBlock(() => UninstallerService.Scan(cancelled.Id), "Cancellation blocks cleanup");

    using (var key = registry.OpenSubKey(subkey, writable: true))
    {
        key!.SetValue("FixtureScenario", "Failure");
    }
    var failed = Prepare(descriptor);
    var failExit = await Run(descriptor, "YES");
    UninstallerService.Finish(failed, failExit);
    Check(failExit == 1603, "Native failure exit");
    MustBlock(() => UninstallerService.Scan(failed.Id), "Failure blocks cleanup");

    using (var key = registry.OpenSubKey(subkey, writable: true))
    {
        key!.SetValue("FixtureScenario", "Restart");
    }
    var pending = Prepare(descriptor);
    var pendingExit = await Run(descriptor, "YES");
    UninstallerService.Finish(pending, pendingExit);
    Check(pendingExit == 3010, "Restart exit code");
    MustBlock(() => UninstallerService.Scan(pending.Id), "Pending restart blocks cleanup");

    Register();
    descriptor = SoftwareSource.ReadUninstaller(reference);
    var completed = Prepare(descriptor);
    var exit = await Run(descriptor, "YES");
    UninstallerService.Finish(completed, exit);
    Check(exit == 0 && UninstallerService.RemovalVerified(completed), "Normal native uninstall verified");
    Check(UninstallerService.Sessions().Any(s => s.Id == completed.Id), "Session survives reload");
    var scan = UninstallerService.Scan(completed.Id);
    Check(scan.Candidates.Any(e => e.RelativePath == Path.Combine("cache", "trace.log")), "Leftover log detected");
    Check(!scan.Candidates.Any(e => e.RelativePath == "settings.json"), "Settings preserved");
    var cleanupId = Guid.NewGuid().ToString("N");
    recoveries.Add(cleanupId);
    var cleanup = UninstallerService.Clean(new RepairJob
    {
        JobId = cleanupId, OperationId = RepairCatalogue.CleanSoftwareLeftovers, ApprovedAt = DateTimeOffset.UtcNow,
        Items = [completed.Id, .. scan.Candidates.Where(e => e.RelativePath.StartsWith("cache", StringComparison.Ordinal)).Select(e => e.Key)],
    });
    Check(cleanup.Completed && cleanup.Verdict == RepairVerdict.Fixed, "Cleanup verified");
    Check(!File.Exists(log) && File.Exists(settings), "Only selected leftovers moved");
    var restored = UninstallerService.Restore(new RepairJob
    {
        JobId = Guid.NewGuid().ToString("N"), OperationId = RepairCatalogue.RestoreSoftwareLeftovers,
        ApprovedAt = DateTimeOffset.UtcNow, Items = cleanup.Rollback,
    });
    Check(restored.Completed && File.ReadAllText(log) == "must survive undo", "Persistent receipt undo");
    Check(!SoftwareSource.Read(CancellationToken.None).Programs.Any(p => p.Registration == reference), "Undo did not reinstall program");
    Console.WriteLine("Windows uninstaller integration checks passed.");
}
finally
{
    registry.DeleteSubKeyTree(subkey, throwOnMissingSubKey: false);
    Directory.Delete(root, recursive: true);
    var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Shiva Assist Uninstaller");
    foreach (var id in sessions)
    {
        File.Delete(Path.Combine(store, id + ".session.json"));
    }
    foreach (var id in recoveries)
    {
        var path = Path.Combine(store, id + ".recovery");
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

void Register()
{
    using var key = registry.CreateSubKey(subkey);
    key.SetValue("DisplayName", "Shiva Assist Uninstaller Fixture");
    key.SetValue("DisplayVersion", "1.0.0");
    key.SetValue("Publisher", "Shiva Computers test fixture");
    key.SetValue("InstallLocation", root);
    key.SetValue("UninstallString", "\"" + Path.Combine(root, "ShivaUninstallFixture.exe") + "\" --uninstall");
}

UninstallSession Prepare(RegisteredUninstaller program)
{
    var id = Guid.NewGuid().ToString("N");
    sessions.Add(id);
    return UninstallerService.Prepare(new RepairJob
    {
        JobId = id, OperationId = RepairCatalogue.UninstallSoftware, ApprovedAt = DateTimeOffset.UtcNow,
        Items = [reference, program.Name, program.Version, program.Publisher, UninstallerService.CommandHash(program)],
    });
}

static async Task<int> Run(RegisteredUninstaller program, string answer)
{
    var start = UninstallerService.LaunchInfo(program);
    start.RedirectStandardInput = true;
    start.RedirectStandardOutput = true;
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Fixture launch failed.");
    var output = process.StandardOutput.ReadToEndAsync();
    await process.StandardInput.WriteLineAsync(answer);
    process.StandardInput.Close();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await process.WaitForExitAsync(timeout.Token);
    Console.WriteLine(await output);
    return process.ExitCode;
}

static void Check(bool passed, string description)
{
    if (!passed)
    {
        throw new InvalidOperationException("Integration check failed: " + description);
    }
    Console.WriteLine("PASS: " + description);
}

static void MustBlock(Action action, string description)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine("PASS: " + description);
        return;
    }
    throw new InvalidOperationException("Expected cleanup to be blocked: " + description);
}

using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Software;
using ShivaAssist.Windows.Infrastructure;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.Windows.Repairs;

public sealed record UninstallSession(string Id, RegisteredUninstaller Program, LeftoverSnapshot? Before,
    DateTimeOffset PreparedAt, DateTimeOffset BootStartedAt, int? ExitCode);

/// <summary>Registered uninstallers and bounded, journalled cleanup. Never executes a shell command.</summary>
public static class UninstallerService
{
    private static string Store => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Shiva Assist Uninstaller");

    public static string CommandHash(RegisteredUninstaller program) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(program.Command)));

    public static ProcessStartInfo LaunchInfo(RegisteredUninstaller program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var command = UninstallPolicy.ParseCommand(Environment.ExpandEnvironmentVariables(program.Command))
            ?? throw new InvalidOperationException("This program has no supported registered uninstaller. Use Windows Settings instead.");
        if (command.ProductCode is { } product)
        {
            if (!program.Reference.EndsWith("|" + product, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The MSI product code does not match this program's registration. Use Windows Settings instead.");
            }

            var start = new ProcessStartInfo(ToolRunner.System32("msiexec.exe")) { UseShellExecute = false };
            start.ArgumentList.Add("/x");
            start.ArgumentList.Add(product);
            start.ArgumentList.Add("/norestart");
            return start;
        }

        var executable = Path.GetFullPath(command.Executable);
        if (!File.Exists(executable) || !ProgramRoots().Any(root => IsWithin(executable, root))
            || executable.Contains("\\Common Files\\", StringComparison.OrdinalIgnoreCase))
        {
            // Do not turn an editable per-user registry command into an arbitrary elevated executable.
            throw new InvalidOperationException("This uninstaller needs the Windows Settings fallback. Integrated uninstall currently supports MSI packages and desktop uninstallers inside Program Files.");
        }

        LeftoverFiles.EnsureNoLinks(executable);
        var name = Path.GetFileNameWithoutExtension(executable);
        string[] blocked = ["cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "reg", "python", "pythonw", "node", "dotnet", "msiexec"];
        if (blocked.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This program has no supported registered uninstaller. Use Windows Settings instead.");
        }

        return new ProcessStartInfo(executable)
        {
            Arguments = command.Arguments, UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
    }

    public static IReadOnlyList<UninstallSession> Sessions()
    {
        if (!Directory.Exists(Store))
        {
            return [];
        }

        ProtectStore();
        var sessions = new List<UninstallSession>();
        foreach (var path in Directory.EnumerateFiles(Store, "*.session.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(200))
        {
            try
            {
                LeftoverFiles.EnsureNoLinks(path);
                if (new FileInfo(path).Length <= 4 * 1024 * 1024
                    && RepairJson.Deserialize<UninstallSession>(File.ReadAllText(path)) is { } session
                    && IsId(session.Id) && session.Program is not null && path == SessionPath(session.Id))
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }

        return sessions.OrderByDescending(s => s.PreparedAt).ToArray();
    }

    public static LeftoverScan Scan(string sessionId)
    {
        var session = ReadSession(sessionId);
        EnsureRemoved(session);
        var snapshot = session.Before
            ?? throw new InvalidOperationException("No isolated installation folder was recorded. Automatic leftover cleanup is unavailable for this program.");
        if (!SafeInstallRoot(snapshot.Root, session.Program.Reference))
        {
            throw new InvalidOperationException("The installation folder is shared, protected or no longer safe to clean.");
        }

        return LeftoverFiles.Scan(snapshot);
    }

    internal static UninstallSession Prepare(RepairJob job)
    {
        if (!IsId(job.JobId) || job.Items.Count != 5)
        {
            throw new ArgumentException("Select one registered program.");
        }

        var program = SoftwareSource.ReadUninstaller(job.Items[0]);
        if (program.Name != job.Items[1] || program.Version != job.Items[2] || program.Publisher != job.Items[3]
            || CommandHash(program) != job.Items[4])
        {
            throw new InvalidOperationException("The selected program changed. Refresh the program list before uninstalling.");
        }

        _ = LaunchInfo(program);
        ProtectStore();
        var root = program.InstallLocation.Trim().Trim('"');
        var before = SafeInstallRoot(root, program.Reference) ? LeftoverFiles.Capture(root) : null;
        // Executable commands are not stored in sessions or the audit log.
        var session = new UninstallSession(job.JobId, program with { Command = string.Empty }, before,
            DateTimeOffset.UtcNow, BootStarted(), null);
        using var stream = new FileStream(SessionPath(job.JobId), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(RepairJson.Serialize(session));
        writer.Flush();
        stream.Flush(flushToDisk: true);
        return session;
    }

    internal static void Finish(UninstallSession session, int exitCode)
    {
        var path = SessionPath(session.Id);
        LeftoverFiles.EnsureNoLinks(path);
        File.WriteAllText(path, RepairJson.Serialize(session with { ExitCode = exitCode }));
    }

    internal static bool RemovalVerified(UninstallSession session)
    {
        var inventory = SoftwareSource.Read(CancellationToken.None);
        return inventory.UnreadableEntries == 0 && !inventory.Programs.Any(p => p.Registration == session.Program.Reference
            || (p.Name.Equals(session.Program.Name, StringComparison.OrdinalIgnoreCase)
                && p.Publisher.Equals(session.Program.Publisher, StringComparison.OrdinalIgnoreCase)));
    }

    internal static RepairResult Clean(RepairJob job)
    {
        if (!IsId(job.JobId) || job.Items.Count < 2)
        {
            throw new ArgumentException("Select the leftover items to clean.");
        }

        var session = ReadSession(job.Items[0]);
        var scan = Scan(session.Id);
        var keys = job.Items.Skip(1).ToArray();
        var entries = scan.Candidates.Where(e => keys.Contains(e.Key, StringComparer.Ordinal)).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || entries.Length != keys.Length)
        {
            throw new InvalidOperationException("The leftovers changed. Scan again before cleaning them.");
        }

        var receipt = new LeftoverReceipt(session.Before!, entries);
        var folder = RecoveryPath(job.JobId);
        LeftoverFiles.EnsureNoLinks(folder);
        Directory.CreateDirectory(folder);
        var receiptPath = Path.Combine(folder, "receipt.json");
        using (var file = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(RepairJson.Serialize(receipt));
            file.Write(bytes);
            file.Flush(flushToDisk: true); // Journal before any change, including a partial failure.
        }

        RepairAudit.Append(Result(job, true, RepairVerdict.Pending,
            "Cleanup journal prepared. If interrupted, use Undo to restore any moved files.", [job.JobId]));

        try
        {
            var count = LeftoverFiles.Quarantine(receipt, folder);
            return Result(job, true, RepairVerdict.Fixed, "Quarantined selected leftovers and removed selected empty folders: "
                + count.ToString(CultureInfo.InvariantCulture), [job.JobId]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var changed = Directory.EnumerateFiles(folder, "*.bin").Any()
                || entries.Where(e => e.IsDirectory).Any(e => !Directory.Exists(Path.Combine(receipt.Snapshot.Root, e.RelativePath)));
            return Result(job, changed, RepairVerdict.Inconclusive, "Cleanup stopped; recovery receipt retained. " + ex.GetType().Name,
                changed ? [job.JobId] : []);
        }
    }

    internal static RepairResult Restore(RepairJob job)
    {
        if (job.Items.Count != 1 || !IsId(job.Items[0]))
        {
            throw new ArgumentException("Invalid cleanup recovery reference.");
        }

        ProtectStore();
        var folder = RecoveryPath(job.Items[0]);
        LeftoverFiles.EnsureNoLinks(folder);
        var path = Path.Combine(folder, "receipt.json");
        LeftoverFiles.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
        {
            throw new IOException("Invalid recovery receipt.");
        }

        var receipt = RepairJson.Deserialize<LeftoverReceipt>(File.ReadAllText(path))
            ?? throw new IOException("Missing recovery receipt.");
        if (!SafeInstallRoot(receipt.Snapshot.Root, string.Empty))
        {
            throw new IOException("The recovery destination is shared or protected. No files were restored.");
        }

        LeftoverFiles.Restore(receipt, folder);
        return Result(job, true, RepairVerdict.Fixed, "Restored quarantined leftovers; program was not reinstalled.", []);
    }

    internal static RepairResult Result(RepairJob job, bool completed, RepairVerdict verdict, string detail, IReadOnlyList<string> rollback) => new()
    {
        JobId = job.JobId, OperationId = job.OperationId, Completed = completed, Verdict = verdict,
        Detail = detail, Rollback = rollback,
    };

    private static UninstallSession ReadSession(string id)
    {
        ProtectStore();
        var path = SessionPath(id);
        LeftoverFiles.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
        {
            throw new IOException("Invalid uninstall session.");
        }

        var session = RepairJson.Deserialize<UninstallSession>(File.ReadAllText(path));
        return session is not null && session.Id == id && session.Program is not null ? session : throw new IOException("Invalid uninstall session.");
    }

    private static void EnsureRemoved(UninstallSession session)
    {
        var restarted = BootStarted() > session.BootStartedAt;
        if ((session.ExitCode != 0 && !(restarted && session.ExitCode is null or 3010 or 1641)) || !RemovalVerified(session))
        {
            throw new InvalidOperationException("Removal is not verified, or a restart is still required. Finish the uninstaller, restart if requested, then check again. Nothing was cleaned.");
        }
    }

    private static bool SafeInstallRoot(string root, string ownRegistration)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            return false;
        }

        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var parent = ProgramRoots().FirstOrDefault(p => IsWithin(root, p));
            if (parent is null || root.AsSpan(2).Contains(':') || IsWithin(AppContext.BaseDirectory, root))
            {
                return false;
            }

            var first = Path.GetRelativePath(parent, root).Split(Path.DirectorySeparatorChar)[0];
            string[] shared = ["Common Files", "Microsoft", "WindowsApps", "dotnet", "Reference Assemblies", "MSBuild", "Internet Explorer"];
            if (shared.Contains(first, StringComparer.OrdinalIgnoreCase) || first.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            LeftoverFiles.EnsureNoLinks(root);
            var inventory = SoftwareSource.Read(CancellationToken.None);
            if (inventory.UnreadableEntries != 0)
            {
                return false;
            }

            return !inventory.Programs.Where(p => p.Registration != ownRegistration && Path.IsPathFullyQualified(p.InstallLocation))
                .Any(p => Overlap(root, Path.GetFullPath(p.InstallLocation)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool Overlap(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(first),
        Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase) || IsWithin(first, second) || IsWithin(second, first);

    private static bool IsWithin(string path, string root) => path.StartsWith(Path.TrimEndingDirectorySeparator(root)
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ProgramRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    }.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset BootStarted()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var value = PowerShell.QueryAsync("""
            $os = Get-CimInstance Win32_OperatingSystem
            [pscustomobject]@{ boot = $os.LastBootUpTime.ToUniversalTime().ToString('o') } | ConvertTo-Json -Compress
            """, timeout.Token).GetAwaiter().GetResult();
        return DateTimeOffset.TryParse(PowerShell.Str(value, "boot"), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var boot) ? boot : throw new InvalidOperationException("Windows restart could not be verified. Nothing was cleaned.");
    }
    private static bool IsId(string id) => Guid.TryParseExact(id, "N", out _);
    private static string SessionPath(string id) => IsId(id) ? Path.Combine(Store, id + ".session.json") : throw new ArgumentException("Invalid session ID.");
    private static string RecoveryPath(string id) => IsId(id) ? Path.Combine(Store, id + ".recovery") : throw new ArgumentException("Invalid cleanup ID.");

    private static void ProtectStore()
    {
        if (!WindowsPlatform.IsElevated())
        {
            throw new UnauthorizedAccessException("Administrator permission is required for integrated uninstall.");
        }

        LeftoverFiles.EnsureNoLinks(Store);
        var security = new DirectorySecurity();
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        var directory = new DirectoryInfo(Store);
        if (directory.Exists)
        {
            var existing = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            if (!administrators.Equals(existing.GetOwner(typeof(SecurityIdentifier)))
                && !system.Equals(existing.GetOwner(typeof(SecurityIdentifier))))
            {
                throw new UnauthorizedAccessException("The uninstall recovery folder has an unexpected owner.");
            }

            foreach (FileSystemAccessRule rule in existing.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                const FileSystemRights writable = FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.ChangePermissions
                    | FileSystemRights.TakeOwnership | FileSystemRights.DeleteSubdirectoriesAndFiles;
                if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & writable) != 0
                    && !administrators.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference))
                {
                    throw new UnauthorizedAccessException("The uninstall recovery folder is writable by an unexpected account.");
                }
            }
        }
        else
        {
            directory.Create(security);
        }
    }
}

public sealed partial class WindowsRepairExecutor
{
    private static async Task<RepairResult> UninstallSoftwareAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var session = UninstallerService.Prepare(job);
        var program = SoftwareSource.ReadUninstaller(job.Items[0]);
        if (UninstallerService.CommandHash(program) != job.Items[4])
        {
            throw new InvalidOperationException("The selected program changed. Refresh the program list before uninstalling.");
        }

        using var process = Process.Start(UninstallerService.LaunchInfo(program))
            ?? throw new InvalidOperationException("The registered uninstaller could not be started.");
        // Never kill an uninstaller on timeout/cancellation: it may be changing a shared installer database.
        await process.WaitForExitAsync(cancellationToken);
        var exit = process.ExitCode;
        UninstallerService.Finish(session, exit);
        if (exit is 3010 or 1641)
        {
            return UninstallerService.Result(job, true, RepairVerdict.Pending, "The uninstaller requested a Windows restart. Check this session after restarting.", []);
        }

        var verified = exit == 0 && UninstallerService.RemovalVerified(session);
        return UninstallerService.Result(job, verified, verified ? RepairVerdict.Fixed : RepairVerdict.Inconclusive,
            verified ? "Program registration removed; leftover cleanup requires separate approval."
                : "Removal not verified. Uninstaller exit code: " + exit.ToString(CultureInfo.InvariantCulture), []);
    }
}

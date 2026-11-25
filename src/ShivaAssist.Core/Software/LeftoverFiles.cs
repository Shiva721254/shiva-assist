using System.Security.Cryptography;

namespace ShivaAssist.Core.Software;

public sealed record LeftoverEntry(string RelativePath, bool IsDirectory, long Bytes, string Hash)
{
    public string Key => (IsDirectory ? "D|" : "F|") + RelativePath;
}

public sealed record LeftoverSnapshot(string Root, bool Complete, IReadOnlyList<LeftoverEntry> Entries);
public sealed record LeftoverScan(IReadOnlyList<LeftoverEntry> Candidates, int PreservedFiles, int ChangedFiles);
public sealed record LeftoverReceipt(LeftoverSnapshot Snapshot, IReadOnlyList<LeftoverEntry> Selected);

/// <summary>
/// Installation-folder-only cleanup. Never follows links or deletes recursively. Only files captured before
/// uninstall, still unchanged afterwards, are candidates. All choices start unselected in the UI.
/// </summary>
public static class LeftoverFiles
{
    private const int MaxEntries = 5000;
    private const long MaxFileBytes = 64 * 1024 * 1024;
    private const long MaxTotalBytes = 256 * 1024 * 1024;

    public static LeftoverSnapshot Capture(string root)
    {
        root = Path.GetFullPath(root);
        var entries = new List<LeftoverEntry>();
        try
        {
            EnsureNoLinks(root);
            if (!Directory.Exists(root))
            {
                return new(root, true, entries);
            }

            long bytes = 0;
            var count = 0;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                var relativeDirectory = Path.GetRelativePath(root, directory);
                entries.Add(new(relativeDirectory == "." ? string.Empty : relativeDirectory, true, 0, string.Empty));
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++count > MaxEntries)
                    {
                        return new(root, false, []);
                    }

                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(path);
                        continue;
                    }

                    var relative = Path.GetRelativePath(root, path);
                    if (!UninstallPolicy.IsDisposableFile(relative))
                    {
                        continue;
                    }

                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes)
                    {
                        continue; // Large binaries are preserved; do not hash gigabytes during an uninstall.
                    }

                    if (bytes + info.Length > MaxTotalBytes)
                    {
                        return new(root, false, []);
                    }

                    var entry = new LeftoverEntry(relative, false, info.Length, Hash(path));
                    entries.Add(entry);
                    bytes += entry.Bytes;
                }
            }

            return new(root, true, entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(root, false, []);
        }
    }

    public static LeftoverScan Scan(LeftoverSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Complete)
        {
            throw new InvalidOperationException("The installation-folder snapshot was incomplete. Leftover cleanup is unavailable.");
        }

        EnsureNoLinks(snapshot.Root);
        var candidates = new List<LeftoverEntry>();
        var changed = 0;
        foreach (var entry in snapshot.Entries.Where(e => !e.IsDirectory))
        {
            var path = Resolve(snapshot.Root, entry);
            if (!File.Exists(path))
            {
                continue;
            }

            if (Matches(snapshot.Root, entry))
            {
                candidates.Add(entry);
            }
            else
            {
                changed++;
            }
        }

        var candidatePaths = candidates.Select(e => Resolve(snapshot.Root, e)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preserved = 0;
        foreach (var entry in snapshot.Entries.Where(e => e.IsDirectory).OrderByDescending(e => e.RelativePath.Length))
        {
            var directory = Resolve(snapshot.Root, entry);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            EnsureNoLinks(directory);
            var children = Directory.EnumerateFileSystemEntries(directory).ToArray();
            preserved += children.Count(path => !Directory.Exists(path) && !candidatePaths.Contains(path));
            if (children.All(candidatePaths.Contains))
            {
                candidates.Add(entry);
                candidatePaths.Add(directory);
            }
        }

        return new(candidates, preserved, changed);
    }

    /// <summary>Call after writing the receipt durably. Each item is verified again immediately before moving it.</summary>
    public static int Quarantine(LeftoverReceipt receipt, string recoveryDirectory)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceipt(receipt);
        EnsureNoLinks(recoveryDirectory);
        Directory.CreateDirectory(recoveryDirectory);
        var current = Scan(receipt.Snapshot).Candidates.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        if (receipt.Selected.Any(e => !current.Contains(e.Key)))
        {
            throw new InvalidOperationException("The leftovers changed. Scan again before cleaning them.");
        }

        var selectedPaths = receipt.Selected.Select(e => Resolve(receipt.Snapshot.Root, e)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (receipt.Selected.Where(e => e.IsDirectory).Any(e => Directory.EnumerateFileSystemEntries(Resolve(receipt.Snapshot.Root, e))
                .Any(path => !selectedPaths.Contains(path))))
        {
            throw new InvalidOperationException("Select the folder's listed contents too, or leave the folder unselected.");
        }

        var moved = 0;
        foreach (var entry in receipt.Selected.OrderBy(e => e.IsDirectory).ThenByDescending(e => e.RelativePath.Length))
        {
            var source = Resolve(receipt.Snapshot.Root, entry);
            EnsureNoLinks(source);
            if (entry.IsDirectory)
            {
                Directory.Delete(source, recursive: false);
            }
            else
            {
                if (!Matches(receipt.Snapshot.Root, entry))
                {
                    throw new InvalidOperationException("The leftovers changed. Scan again before cleaning them.");
                }

                File.Move(source, BackupPath(recoveryDirectory, receipt, entry), overwrite: false);
                var backup = BackupPath(recoveryDirectory, receipt, entry);
                if (File.Exists(source) || new FileInfo(backup).Length != entry.Bytes || Hash(backup) != entry.Hash)
                {
                    throw new IOException("The cleaned file could not be verified in recovery storage.");
                }
            }

            moved++;
        }

        return moved;
    }

    /// <summary>Restores only this receipt's files; never overwrites an existing file or follows a link.</summary>
    public static void Restore(LeftoverReceipt receipt, string recoveryDirectory)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceipt(receipt);
        EnsureNoLinks(recoveryDirectory);
        foreach (var entry in receipt.Selected.Where(e => !e.IsDirectory))
        {
            var destination = Resolve(receipt.Snapshot.Root, entry);
            var backup = BackupPath(recoveryDirectory, receipt, entry);
            EnsureNoLinks(destination);
            EnsureNoLinks(backup);
            if (File.Exists(backup))
            {
                if (File.Exists(destination) || Directory.Exists(destination) || Hash(backup) != entry.Hash)
                {
                    throw new IOException("A recovery destination already exists or its backup changed. No file will be overwritten.");
                }
            }
            else if (!Matches(receipt.Snapshot.Root, entry))
            {
                throw new IOException("A recovery file is missing. The undo could not be verified.");
            }
        }

        foreach (var entry in receipt.Selected.Where(e => e.IsDirectory).OrderBy(e => e.RelativePath.Length))
        {
            var destination = Resolve(receipt.Snapshot.Root, entry);
            EnsureNoLinks(destination);
            Directory.CreateDirectory(destination);
        }

        foreach (var entry in receipt.Selected.Where(e => !e.IsDirectory))
        {
            var backup = BackupPath(recoveryDirectory, receipt, entry);
            if (!File.Exists(backup))
            {
                continue; // A previous partial undo may have restored this file already.
            }

            var destination = Resolve(receipt.Snapshot.Root, entry);
            EnsureNoLinks(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(backup, destination, overwrite: false);
            if (!Matches(receipt.Snapshot.Root, entry))
            {
                throw new IOException("The restored file could not be verified.");
            }
        }
    }

    public static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // GetAttributes also detects a broken symbolic link, unlike File.Exists.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Linked files and folders are excluded from uninstall cleanup.");
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool Matches(string root, LeftoverEntry entry)
    {
        var path = Resolve(root, entry);
        EnsureNoLinks(path);
        return File.Exists(path) && new FileInfo(path).Length == entry.Bytes && Hash(path) == entry.Hash;
    }

    private static string Resolve(string root, LeftoverEntry entry)
    {
        if (entry.IsDirectory && entry.RelativePath.Length == 0)
        {
            return Path.GetFullPath(root);
        }

        if (!UninstallPolicy.IsSafeRelativePath(entry.RelativePath) || (!entry.IsDirectory && !UninstallPolicy.IsDisposableFile(entry.RelativePath)))
        {
            throw new ArgumentException("Invalid leftover path.");
        }

        return Path.GetFullPath(Path.Combine(root, entry.RelativePath));
    }

    private static void ValidateReceipt(LeftoverReceipt receipt)
    {
        if (!receipt.Snapshot.Complete || receipt.Selected.Count == 0 || receipt.Selected.Count > MaxEntries + 1
            || receipt.Selected.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() != receipt.Selected.Count
            || receipt.Selected.Any(e => !receipt.Snapshot.Entries.Contains(e)))
        {
            throw new ArgumentException("Invalid leftover selection.");
        }
    }

    private static string BackupPath(string directory, LeftoverReceipt receipt, LeftoverEntry entry) =>
        Path.Combine(directory, receipt.Selected.ToList().IndexOf(entry).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin");

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

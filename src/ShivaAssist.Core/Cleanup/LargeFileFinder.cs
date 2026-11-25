using System.IO;

namespace ShivaAssist.Core.Cleanup;

/// <summary>One large personal file discovered during a read-only scan.</summary>
public sealed record LargeFileEntry(
    string Name,
    string FolderLabel,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastModified);

/// <summary>Bounded read-only scan result for large personal files.</summary>
public sealed record LargeFileScanResult(
    IReadOnlyList<LargeFileEntry> Files,
    int ScannedEntries,
    bool Truncated);

/// <summary>
/// Strictly read-only finder for large files in the user's personal folders.
/// Never deletes, modifies or moves files; skips reparse points and bounds traversal so large drives stay responsive.
/// </summary>
public static class LargeFileFinder
{
    public const long DefaultMinimumBytes = 100L * 1024 * 1024; // 100 MB
    public const int DefaultMaxResults = 50;
    public const int DefaultMaxEntries = 25_000;

    public static LargeFileScanResult Scan(
        IEnumerable<(string Label, string Path)> roots,
        long minimumBytes = DefaultMinimumBytes,
        int maxResults = DefaultMaxResults,
        int maxEntries = DefaultMaxEntries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);

        var found = new List<LargeFileEntry>();
        var scanned = 0;
        var truncated = false;
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (label, rootPath) in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                continue;
            }

            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(rootPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
            {
                continue;
            }

            if (!seenRoots.Add(normalizedRoot))
            {
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(normalizedRoot);

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (scanned >= maxEntries)
                {
                    truncated = true;
                    break;
                }

                var current = pending.Pop();
                DirectoryInfo dirInfo;
                try
                {
                    dirInfo = new DirectoryInfo(current);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    continue;
                }

                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = dirInfo.EnumerateFileSystemInfos();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    scanned++;
                    if (scanned > maxEntries)
                    {
                        truncated = true;
                        break;
                    }

                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if (entry is DirectoryInfo subDir)
                        {
                            pending.Push(subDir.FullName);
                        }
                        else if (entry is FileInfo file && file.Length >= minimumBytes)
                        {
                            found.Add(new LargeFileEntry(
                                file.Name,
                                label,
                                file.FullName,
                                file.Length,
                                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        // Skip inaccessible entries without aborting the scan.
                    }
                }
            }

            if (truncated)
            {
                break;
            }
        }

        var top = found
            .OrderByDescending(f => f.SizeBytes)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();

        return new LargeFileScanResult(top, Math.Min(scanned, maxEntries), truncated);
    }
}

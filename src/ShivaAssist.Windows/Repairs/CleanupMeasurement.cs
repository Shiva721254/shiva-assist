using System.Globalization;
using ShivaAssist.Core.Cleanup;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Windows.Infrastructure;

namespace ShivaAssist.Windows.Repairs;

/// <summary>
/// The fixed browser cache folders that may be cleared: only cache data (pages, scripts, images), never profile
/// files such as cookies, passwords, history or bookmarks. Derived from the current user's LocalAppData by the
/// executor itself; a job file cannot add folders.
/// </summary>
public static class BrowserCaches
{
    private static readonly string[] ChromiumCacheNames = ["Cache", "Code Cache", "GPUCache"];

    public static IReadOnlyList<string> Folders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folders = new List<string>();
        foreach (var userData in new[] { Path.Combine(local, "Microsoft", "Edge", "User Data"), Path.Combine(local, "Google", "Chrome", "User Data") })
        {
            if (!Directory.Exists(userData))
            {
                continue;
            }

            foreach (var profile in SafeDirectories(userData).Where(p => Path.GetFileName(p) is "Default" || Path.GetFileName(p).StartsWith("Profile ", StringComparison.Ordinal)))
            {
                folders.AddRange(ChromiumCacheNames.Select(name => Path.Combine(profile, name)).Where(Directory.Exists));
            }
        }

        var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefox))
        {
            folders.AddRange(SafeDirectories(firefox).Select(profile => Path.Combine(profile, "cache2")).Where(Directory.Exists));
        }

        return folders.Where(f => !new DirectoryInfo(f).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList();
    }

    private static string[] SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>Fixed per-user DirectX/GPU shader cache folders derived from LocalAppData (never follows links).</summary>
public static class ShaderCaches
{
    public static IReadOnlyList<string> Folders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(local, "D3DSCache"),
            Path.Combine(local, "NVIDIA", "DXCache"),
            Path.Combine(local, "AMD", "DxCache"),
            Path.Combine(local, "Intel", "ShaderCache"),
        ];
        return candidates
            .Where(Directory.Exists)
            .Where(f => !new DirectoryInfo(f).Attributes.HasFlag(FileAttributes.ReparsePoint))
            .ToList();
    }
}

/// <summary>Measures, read-only, what each cleanup area would free. Uses the same rules as the cleanup itself.</summary>
public static class CleanupMeasurement
{
    private static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };

    public static async Task<IReadOnlyList<CleanupEstimate>> MeasureAsync(CancellationToken cancellationToken)
    {
        var results = new List<CleanupEstimate>();
        foreach (var area in CleanupAreas.All)
        {
            try
            {
                results.Add(area.OperationId switch
                {
                    RepairCatalogue.CleanTempFiles => Measure(area.OperationId, [Path.GetTempPath()], DateTime.UtcNow - TimeSpan.FromDays(7)),
                    RepairCatalogue.CleanBrowserCache => Measure(area.OperationId, BrowserCaches.Folders(), null),
                    RepairCatalogue.EmptyRecycleBin => await RecycleBinAsync(cancellationToken),
                    RepairCatalogue.CleanUpdateCache => Measure(area.OperationId, UpdateCacheFolders(), null),
                    RepairCatalogue.CleanShaderCache => Measure(area.OperationId, ShaderCaches.Folders(), DateTime.UtcNow - TimeSpan.FromDays(7)),
                    _ => new CleanupEstimate(area.OperationId, null, 0),
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                results.Add(new CleanupEstimate(area.OperationId, null, 0));
            }
        }

        return results;
    }

    private static CleanupEstimate Measure(string operationId, IEnumerable<string> folders, DateTime? olderThanUtc)
    {
        long bytes = 0;
        var files = 0;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Walk))
            {
                if (olderThanUtc is { } cutoff && (file.LastWriteTimeUtc > cutoff || file.CreationTimeUtc > cutoff))
                {
                    continue;
                }

                bytes += file.Length;
                files++;
            }
        }

        return new CleanupEstimate(operationId, bytes, files);
    }

    private static IEnumerable<string> UpdateCacheFolders()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return
        [
            Path.Combine(windows, "SoftwareDistribution", "Download"),
            Path.Combine(windows, "Minidump"),
            Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
        ];
    }

    private static async Task<CleanupEstimate> RecycleBinAsync(CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync("""
            $items = @((New-Object -ComObject Shell.Application).NameSpace(10).Items())
            $size = 0
            foreach ($item in $items) { try { $size += [long]$item.ExtendedProperty('Size') } catch { } }
            [pscustomobject]@{ bytes = $size; files = $items.Count } | ConvertTo-Json -Compress
            """, cancellationToken);
        return new CleanupEstimate(RepairCatalogue.EmptyRecycleBin, PowerShell.Long(json, "bytes"), (int)PowerShell.Long(json, "files"));
    }

    /// <summary>Plain-text size for logs; the app formats sizes with the customer's language.</summary>
    public static string Describe(CleanupEstimate estimate) => string.Create(CultureInfo.InvariantCulture,
        $"{estimate.OperationId}: {(estimate.Bytes is { } b ? b.ToString(CultureInfo.InvariantCulture) + " bytes" : "not measured")} in {estimate.Files} files");
}

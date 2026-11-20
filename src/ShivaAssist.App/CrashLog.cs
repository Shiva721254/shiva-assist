using System.Globalization;
using System.IO;
using System.Reflection;

namespace ShivaAssist.App;

/// <summary>
/// Writes unexpected errors to %LOCALAPPDATA%\Shiva Assist\crash-log.txt so a technician can see what happened.
/// Exception text only (no scan data, no secrets); the file is capped at 1 MB by starting over.
/// </summary>
internal static class CrashLog
{
    private const long MaxBytes = 1024 * 1024;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiva Assist", "crash-log.txt");

    public static void Write(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
            {
                File.Delete(Path);
            }

            var version = typeof(CrashLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            File.AppendAllText(Path, string.Create(CultureInfo.InvariantCulture,
                $"--- {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · {source} · Shiva Assist {version} · {Environment.OSVersion}{Environment.NewLine}{exception}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never cause a second failure.
        }
    }
}

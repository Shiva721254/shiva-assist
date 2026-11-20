using System.Text;
using ShivaAssist.Core.Software;
using ShivaAssist.Windows.Infrastructure;

namespace ShivaAssist.Windows.Sources;

public static class PackageUpdateSource
{
    /// <summary>The per-user Windows App Installer alias; never resolve an executable from the working directory.</summary>
    private static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "winget.exe");

    public static async Task<IReadOnlyList<PackageUpdate>> ReadAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output, _) = await RunAsync(
            ["upgrade", "--source", "winget", "--disable-interactivity"], cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Windows Package Manager could not check updates. Open WinGet once to review its source agreements, then try again.");
        }

        var updates = PackageUpdates.Parse(output);
        if (updates.Count == 0)
        {
            throw new InvalidOperationException("No actionable updates were returned. This does not confirm that all programs are up to date.");
        }

        return updates;
    }

    internal static Task<(int ExitCode, string Output, string Error)> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(Executable))
        {
            throw new PlatformNotSupportedException("Windows Package Manager is unavailable. Install or update App Installer from Microsoft Store.");
        }

        return ToolRunner.RunAsync(Executable, arguments, cancellationToken, Encoding.UTF8);
    }
}

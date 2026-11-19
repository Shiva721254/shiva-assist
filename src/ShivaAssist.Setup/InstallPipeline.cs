using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ShivaAssist.Setup;

internal sealed class InstallResult
{
    public string Application { get; set; } = string.Empty;
    public string ApplicationHash { get; set; } = string.Empty;
    public bool RestartRequired { get; set; }
}

internal sealed class InstallProgress
{
    public string Text { get; set; } = string.Empty;
    public int Percent { get; set; }
    public bool Installing { get; set; }
}

internal static class InstallPipeline
{
    public static string PublisherIdentity => typeof(InstallPipeline).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "PublisherIdentityOid")?.Value ?? string.Empty;

    public static async Task<InstallResult> RunAsync(Action<InstallProgress> progress, CancellationToken cancellationToken,
        string? localPackage = null, string? expectedHash = null, bool quiet = false)
    {
#if !SETUP_LAB
        if (localPackage != null || quiet) throw new InvalidOperationException("Local package installation is only available in the separate lab build.");
        if (!InstallerPolicy.ValidIdentityOid(PublisherIdentity))
            throw new InvalidOperationException("This setup build is not configured for a signed public release. Contact Shiva Computers for the current installer.");
#endif
        var architecture = WindowsInstallerApi.Architecture();
        var workspace = WindowsInstallerApi.CreatePrivateWorkspace();
        var downloaded = Path.Combine(workspace, "payload.msi");
        try
        {
            SetupPackage selected;
            string version;
            if (localPackage != null)
            {
                if (!InstallerPolicy.ValidDigest(expectedHash)) throw new InvalidOperationException("The local test package needs its expected SHA-256 hash.");
                InstallerPolicy.EnsureNoLinks(localPackage);
                var source = new FileInfo(localPackage);
                if (!source.Exists || !source.Extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
                    || source.Length <= 0 || source.Length > InstallerPolicy.MaximumPackageBytes) throw new IOException("Invalid local test package.");
                File.Copy(source.FullName, downloaded, overwrite: false);
                version = WindowsInstallerApi.Property(downloaded, "ProductVersion");
                selected = new SetupPackage { Architecture = architecture, Sha256 = expectedHash!, Size = source.Length };
            }
            else
            {
                progress(new InstallProgress { Text = "Checking the latest release…" });
                using var client = InstallerDownloads.CreateClient();
                var manifest = await InstallerDownloads.ManifestAsync(client, cancellationToken).ConfigureAwait(false);
                var serializer = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 8 };
                var root = serializer.Deserialize<Dictionary<string, object>>(new UTF8Encoding(false, true).GetString(manifest))
                    ?? throw new InvalidOperationException("The release information is not valid.");
                if (!root.TryGetValue("packages", out var packageItems) || !(packageItems is object[] items)
                    || items.Any(item => !(item is Dictionary<string, object>)))
                    throw new InvalidOperationException("The release information is not valid.");
                version = Text(root, "version");
                var packages = items.Cast<Dictionary<string, object>>().Select(item => new SetupPackage
                {
                    Architecture = Text(item, "architecture"), Url = Text(item, "url"), Sha256 = Text(item, "sha256"),
                    AppSha256 = Text(item, "appSha256"), Size = item.TryGetValue("size", out var size) ? size is int small ? small : size is long large ? large : -1 : -1,
                });
                selected = InstallerPolicy.Select(Text(root, "schema"), version, packages, architecture);
                await InstallerDownloads.DownloadAsync(client, selected, downloaded,
                    percent => progress(new InstallProgress { Text = "Downloading Shiva Assist…", Percent = percent }), cancellationToken).ConfigureAwait(false);
            }

            progress(new InstallProgress { Text = "Verifying the downloaded package…", Percent = 100 });
            using var locked = new FileStream(downloaded, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!string.Equals(InstallerDownloads.Hash(locked), selected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The package verification failed. Nothing was installed.");
            if (localPackage == null && !WindowsInstallerApi.TrustedPublisher(downloaded, locked, PublisherIdentity))
                throw new IOException("The package does not have the expected trusted Shiva Computers signature. Nothing was installed.");
            if (!InstallerPolicy.ValidVersion(version) || WindowsInstallerApi.Property(downloaded, "ProductVersion") != version
                || WindowsInstallerApi.Property(downloaded, "Manufacturer") != "Shiva Computers"
                || !string.Equals(WindowsInstallerApi.Property(downloaded, "UpgradeCode"), UpgradeCode(architecture), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The package does not match this release or processor type. Nothing was installed.");
            var productCode = WindowsInstallerApi.Property(downloaded, "ProductCode");
            if (!Guid.TryParseExact(productCode, "B", out _)) throw new IOException("Invalid Windows Installer product identity.");
            InstallerPolicy.EnsureNoLinks(WindowsInstallerApi.InstalledDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            progress(new InstallProgress { Text = "Installing Shiva Assist. Follow any Windows Installer prompts…", Installing = true });
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
            {
                UseShellExecute = false, Arguments = "/i \"" + downloaded + "\" " + (quiet ? "/qn" : "/qb") + " /norestart"
                    + (WindowsInstallerApi.Installed(productCode) ? " REINSTALL=ALL REINSTALLMODE=amus" : string.Empty),
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer could not be started.");
            await Task.Run(() => process.WaitForExit()).ConfigureAwait(false); // Never kill an installation on cancellation.
            var exit = process.ExitCode;
            if (exit == 1602) throw new OperationCanceledException("Installation was cancelled by the user.");
            if (exit != 0 && exit != 3010 && exit != 1641)
                throw new InvalidOperationException("Windows Installer ended with code " + exit.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            var application = Path.Combine(WindowsInstallerApi.InstalledDirectory, "ShivaAssist.exe");
            InstallerPolicy.EnsureNoLinks(application);
            if (!WindowsInstallerApi.Installed(productCode) || !File.Exists(application))
                throw new IOException("Installation finished but could not be verified. Please retry or contact Shiva Computers.");
            using var installed = new FileStream(application, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (localPackage == null && (!string.Equals(InstallerDownloads.Hash(installed), selected.AppSha256, StringComparison.OrdinalIgnoreCase)
                || !WindowsInstallerApi.TrustedPublisher(application, installed, PublisherIdentity)))
                throw new IOException("The installed application could not be verified. It was not opened.");
            return new InstallResult { Application = application, ApplicationHash = InstallerDownloads.Hash(installed),
                RestartRequired = exit == 3010 || exit == 1641 };
        }
        finally
        {
            try { File.Delete(downloaded); Directory.Delete(workspace, recursive: false); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }

    public static void Open(InstallResult result)
    {
        InstallerPolicy.EnsureNoLinks(result.Application);
        using var file = new FileStream(result.Application, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (InstallerDownloads.Hash(file) != result.ApplicationHash) throw new IOException("The installed application changed. Please run setup again.");
        using var process = Process.Start(new ProcessStartInfo(result.Application)
        {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(result.Application), Arguments = "--technician",
        }) ?? throw new InvalidOperationException("Shiva Assist could not be opened.");
    }

    public static string UpgradeCode(string architecture) => architecture == "win-x64"
        ? "{4A67A327-3B49-44B7-BBBB-776A5EAD0D68}" : "{04D720CC-7994-4C3C-A69A-CA52BBA20741}";

    private static string Text(Dictionary<string, object> item, string key) => item.TryGetValue(key, out var value)
        && value is string text ? text : throw new InvalidOperationException("The release information is not valid.");
}

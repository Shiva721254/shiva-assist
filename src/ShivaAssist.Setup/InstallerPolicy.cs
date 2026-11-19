#pragma warning disable CA2249 // net48 lacks string.Contains(string, StringComparison).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ShivaAssist.Setup;

internal sealed class SetupPackage
{
    public string Architecture { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string AppSha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}

/// <summary>Shared by the net48 bootstrapper and platform-neutral tests. Manifest values never become commands.</summary>
internal static class InstallerPolicy
{
    public const string ManifestUrl = "https://assist.shivacomputers.nl/downloads/shiva-assist/releases.json";
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    private static readonly char[] ForbiddenUrlCharacters = { '%', '\\', '\r', '\n' };
    private static readonly Regex Digest = new Regex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    // Azure Artifact Signing: certificates renew daily, but every certificate carries the same identity-validation EKU,
    // 1.3.6.1.4.1.311.97.<arcs unique to the validated publisher>. That OID is pinned instead of a certificate hash.
    private static readonly Regex IdentityOid = new Regex(@"\A1\.3\.6\.1\.4\.1\.311\.97(\.(0|[1-9][0-9]{0,9})){2,8}\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new Regex(@"\A(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,4})\z", RegexOptions.CultureInvariant);

    public static bool ValidDigest(string? digest) => digest != null && Digest.IsMatch(digest);

    public static bool ValidIdentityOid(string? oid) => oid != null && IdentityOid.IsMatch(oid);

    public static bool ValidVersion(string? version)
    {
        if (version == null || !VersionPattern.IsMatch(version)) return false;
        var parts = version.Split('.');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) <= 255
            && int.Parse(parts[1], CultureInfo.InvariantCulture) <= 255
            && int.Parse(parts[2], CultureInfo.InvariantCulture) <= 65535;
    }

    public static string Architecture(ushort nativeMachine) => nativeMachine switch
    {
        0x8664 => "win-x64", 0xAA64 => "win-arm64",
        _ => throw new InvalidOperationException("This installer requires a 64-bit Intel/AMD or ARM64 Windows PC."),
    };

    public static Uri PackageUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Host == "shivacomputers.nl" || uri.Host == "www.shivacomputers.nl" || uri.Host == "assist.shivacomputers.nl")
            || !uri.AbsolutePath.StartsWith("/downloads/shiva-assist/", StringComparison.Ordinal)
            || !uri.AbsolutePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            || url.IndexOfAny(ForbiddenUrlCharacters) >= 0 || url.IndexOf("/../", StringComparison.Ordinal) >= 0
            || url.IndexOf("/./", StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("The release contains an unsupported download address.");
        }
        return uri;
    }

    public static SetupPackage Select(string schema, string version, IEnumerable<SetupPackage> packages, string architecture)
    {
        if (schema != "shiva.assist.release/1" || !ValidVersion(version))
            throw new InvalidOperationException("The release information is not valid.");
        var list = packages.ToArray();
        if (list.Length != 2 || list.Any(p => p.Architecture != "win-x64" && p.Architecture != "win-arm64")
            || list.Select(p => p.Architecture).Distinct(StringComparer.Ordinal).Count() != list.Length)
            throw new InvalidOperationException("The release must contain one package for each supported processor type.");
        foreach (var package in list)
        {
            _ = PackageUri(package.Url);
            if (!ValidDigest(package.Sha256) || !ValidDigest(package.AppSha256)
                || package.Size <= 0 || package.Size > MaximumPackageBytes)
                throw new InvalidOperationException("The release has invalid package verification details.");
        }
        return list.Single(p => p.Architecture == architecture);
    }

    public static void EnsureNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked setup paths are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}

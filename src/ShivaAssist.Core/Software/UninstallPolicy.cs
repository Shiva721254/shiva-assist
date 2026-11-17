using System.Text.RegularExpressions;

namespace ShivaAssist.Core.Software;

public sealed record UninstallCommand(string Executable, string Arguments, string? ProductCode = null);

/// <summary>Fail-closed command and leftover rules shared with the platform-neutral tests.</summary>
public static partial class UninstallPolicy
{
    public static UninstallCommand? ParseCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return null;
        }

        var text = command.Trim();
        // MSI maintenance strings often use /I. Uninstall always uses the validated product GUID with /X.
        var msi = MsiPattern().Match(text);
        if (msi.Success)
        {
            return new UninstallCommand("msiexec.exe", string.Empty, msi.Groups[1].Value.ToUpperInvariant());
        }

        var native = NativePattern().Match(text);
        if (!native.Success)
        {
            return null;
        }

        var executable = native.Groups[1].Success ? native.Groups[1].Value : native.Groups[2].Value;
        return new UninstallCommand(executable, native.Groups[3].Value.Trim());
    }

    public static bool IsSafeRelativePath(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 240 && !value.StartsWith('/') && !value.StartsWith('\\')
        && value.IndexOfAny([':', '\0', '*', '?', '"', '<', '>', '|']) < 0
        && value.Split(['/', '\\']).All(part => part.Length > 0 && part is not ("." or "..")
            && !part.EndsWith('.') && !part.EndsWith(' '));

    /// <summary>Only disposable-file candidates, not documents, databases, profiles, binaries or registry keys.</summary>
    public static bool IsDisposableFile(string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            return false;
        }

        return relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".cache", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\\A(?:msiexec(?:\\.exe)?|\"[A-Za-z]:\\\\Windows\\\\(?:System32|SysWOW64)\\\\msiexec\\.exe\"|[A-Za-z]:\\\\Windows\\\\(?:System32|SysWOW64)\\\\msiexec\\.exe)\\s+/[IX]\\s*(\\{[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\\})\\s*\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MsiPattern();

    // Quoted full paths may contain spaces. An unquoted path with spaces is ambiguous and is refused.
    [GeneratedRegex("\\A(?:\"([A-Za-z]:\\\\[^\"\\r\\n]+\\.exe)\"|([A-Za-z]:\\\\[^\\s\"]+\\.exe))(?:\\s+(.*))?\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativePattern();
}

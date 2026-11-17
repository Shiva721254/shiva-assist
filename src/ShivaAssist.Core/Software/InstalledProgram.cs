using System.Globalization;

namespace ShivaAssist.Core.Software;

/// <summary>Display metadata from Windows' registered programs. Contains no executable commands.</summary>
public sealed record InstalledProgram(string Name, string Publisher, string Version, long? SizeBytes, DateOnly? InstalledOn)
{
    /// <summary>Opaque registry identity, never an executable command.</summary>
    public string Registration { get; init; } = string.Empty;
    public string InstallLocation { get; init; } = string.Empty;
}

public static class SoftwareInventory
{
    public static IReadOnlyList<InstalledProgram> Normalize(IEnumerable<InstalledProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        return programs.Where(program => !string.IsNullOrWhiteSpace(program.Name))
            .Select(program => program with
            {
                Name = program.Name.Trim(),
                Publisher = program.Publisher.Trim(),
                Version = program.Version.Trim(),
                SizeBytes = program.SizeBytes is >= 0 ? program.SizeBytes : null,
            })
            .Distinct()
            .OrderBy(program => program.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(program => program.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static DateOnly? ParseInstallDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

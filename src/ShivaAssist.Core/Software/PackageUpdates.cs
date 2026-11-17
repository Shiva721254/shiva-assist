using System.Text.RegularExpressions;

namespace ShivaAssist.Core.Software;

public sealed record PackageUpdate(string Name, string Id, string InstalledVersion, string AvailableVersion);

/// <summary>Conservative parsing of WinGet's human-readable tables. Ambiguous rows are never actionable.</summary>
public static partial class PackageUpdates
{
    public static bool IsValidId(string? id) => id is { Length: > 2 and <= 200 }
        && PackageIdPattern().IsMatch(id);

    public static IReadOnlyList<PackageUpdate> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var result = new List<PackageUpdate>();
        foreach (var columns in Rows(output))
        {
            if (columns.Length is not (4 or 5) || !IsValidId(columns[1]) || columns.Any(string.IsNullOrWhiteSpace)
                || columns.Any(column => column.Contains('…'))
                || (columns.Length == 5 && columns[4] != "winget"))
            {
                continue;
            }

            result.Add(new PackageUpdate(columns[0], columns[1], columns[2], columns[3]));
        }

        // An ID appearing twice is ambiguous; require a fresh, unambiguous query before updating it.
        return result.GroupBy(update => update.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
    }

    public static string? InstalledVersion(string output, string id)
    {
        ArgumentNullException.ThrowIfNull(output);
        var matches = Rows(output).Where(columns => columns.Length >= 3 && columns[1] == id).ToArray();
        return matches.Length == 1 && !matches[0][2].Contains('…') ? matches[0][2] : null;
    }

    private static IEnumerable<string[]> Rows(string output)
    {
        int[]? starts = null;
        var previous = string.Empty;
        foreach (var line in output.Split('\n'))
        {
            var clean = AnsiPattern().Replace(line, string.Empty).TrimEnd();
            if (clean.Length > 5 && clean.All(character => character == '-'))
            {
                var headings = HeaderPattern().Matches(previous);
                starts = headings.Count is >= 3 and <= 5 ? headings.Select(match => match.Index).ToArray() : null;
                continue;
            }

            if (starts is not null && clean.Length > starts[^1])
            {
                var columns = new string[starts.Length];
                for (var index = 0; index < starts.Length; index++)
                {
                    var end = index + 1 < starts.Length ? Math.Min(starts[index + 1], clean.Length) : clean.Length;
                    columns[index] = clean[starts[index]..end].Trim();
                }

                yield return columns;
            }

            previous = clean;
        }
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_-]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiPattern();
}

using System.Globalization;
using System.Xml.Linq;

namespace ShivaAssist.Core.Modules;

public sealed record StartupRegistration(StartupEntry Entry, string? Executable);
public sealed record StartupMeasurement(string Executable, long DurationMs, long? DelayMs, DateTimeOffset ObservedAt);
public sealed record StartupBoot(long DurationMs, DateTimeOffset ObservedAt);
public sealed record StartupPerformance(IReadOnlyList<StartupMeasurement> Applications, StartupBoot? Boot);
public sealed record StartupApplication(StartupEntry? Entry, string Name, string? Executable, StartupMeasurement? Measurement);

/// <summary>Windows reports are observations, not predicted savings or exhaustive boot measurements.</summary>
public static class StartupMeasurements
{
    public static StartupPerformance Parse(IEnumerable<XElement> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var applications = new List<StartupMeasurement>();
        var boots = new List<StartupBoot>();
        foreach (var item in events)
        {
            XNamespace ns = item.Name.Namespace;
            var system = item.Element(ns + "System");
            if (system?.Element(ns + "Provider")?.Attribute("Name")?.Value != "Microsoft-Windows-Diagnostics-Performance"
                || !DateTimeOffset.TryParse(system.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var observed))
            {
                continue;
            }

            var data = item.Element(ns + "EventData")?.Elements(ns + "Data")
                .Where(d => d.Attribute("Name") is not null).GroupBy(d => d.Attribute("Name")!.Value, StringComparer.Ordinal)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Value, StringComparer.Ordinal) ?? [];
            long? Number(string key) => data.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 86_400_000 ? number : null;
            var id = system.Element(ns + "EventID")?.Value;
            if (id == "100" && Number("BootTime") is { } duration)
            {
                boots.Add(new(duration, observed));
            }
            else if (id == "101" && data.TryGetValue("Name", out var name) && ExecutableName(name) is { } executable
                && Number("TotalTime") is { } total)
            {
                applications.Add(new(executable, total, Number("DegradationTime"), observed));
            }
        }

        return new(applications.GroupBy(a => a.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(a => a.ObservedAt)!).ToArray(), boots.MaxBy(b => b.ObservedAt));
    }

    public static string? ExecutableName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return null;
        }

        var name = path.Replace('\\', '/').Split('/')[^1].Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4
            && name.IndexOfAny([':', '"', '<', '>', '|']) < 0 ? name : null;
    }

    public static IReadOnlyList<StartupApplication> Match(IReadOnlyList<StartupRegistration> registrations, StartupPerformance performance)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(performance);
        var rows = registrations.Select(r => new StartupApplication(r.Entry, r.Entry.Name, r.Executable, null)).ToList();
        foreach (var measurement in performance.Applications)
        {
            var matching = registrations.Where(r => string.Equals(r.Executable, measurement.Executable, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length == 1)
            {
                var index = rows.FindIndex(r => r.Entry?.Reference == matching[0].Entry.Reference);
                rows[index] = rows[index] with { Measurement = measurement };
            }
            else
            {
                // An executable shared by two registrations cannot attribute time to either one reliably.
                rows.Add(new(null, measurement.Executable, measurement.Executable, measurement));
            }
        }

        return rows.OrderByDescending(r => r.Measurement?.DurationMs ?? -1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

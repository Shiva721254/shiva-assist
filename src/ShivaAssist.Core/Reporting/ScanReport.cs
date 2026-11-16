using System.Text.Json;
using System.Text.Json.Serialization;
using ShivaAssist.Core.Findings;

namespace ShivaAssist.Core.Reporting;

public sealed record ScannedSystem(string OsName, string OsVersion, bool Elevated);

/// <summary>
/// One scan, report schema <c>shiva.assist.report/1.0</c>. Contains no computer name, user name or serial numbers.
/// </summary>
public sealed record ScanReport
{
    public const string SchemaId = "shiva.assist.report/1.0";

    public string Schema { get; init; } = SchemaId;

    public required string Product { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required double DurationSeconds { get; init; }

    public required ScannedSystem System { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }

    public IReadOnlyDictionary<Severity, int> CountBySeverity =>
        Enum.GetValues<Severity>().ToDictionary(s => s, s => Findings.Count(f => f.Severity == s));

    /// <summary>True when something is Critical or a Warning.</summary>
    [JsonIgnore]
    public bool NeedsAttention => Findings.Any(f => f.Severity is Severity.Critical or Severity.Warning);
}

/// <summary>Writes and reads reports. Enum values use the contract spelling: <c>critical</c>, <c>requires-admin</c>, <c>windows-system</c>.</summary>
public static class ReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static string Serialize(ScanReport report) => JsonSerializer.Serialize(report, Options);

    public static ScanReport? Deserialize(string json) => JsonSerializer.Deserialize<ScanReport>(json, Options);
}

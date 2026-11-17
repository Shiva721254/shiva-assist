using System.Text.Json;
using System.Text.Json.Serialization;
using ShivaAssist.Core.Findings;

namespace ShivaAssist.Core.Repairs;

/// <summary>
/// One approved repair, handed to the executor (or, when administrator rights are needed, to the elevated helper
/// as a JSON file). Parameters are plain strings and are validated again by the helper.
/// </summary>
public sealed record RepairJob
{
    public const string SchemaId = "shiva.assist.repair-job/1.0";

    public string Schema { get; init; } = SchemaId;

    public required string JobId { get; init; }

    public required string OperationId { get; init; }

    public required DateTimeOffset ApprovedAt { get; init; }

    /// <summary>For example the selected startup programs, or the user's temp folder.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Where the elevated helper writes its result.</summary>
    public string? ResultPath { get; init; }
}

/// <summary>Verification outcome in the contract: <c>fixed | unresolved | inconclusive</c>.</summary>
public enum RepairVerdict
{
    Fixed,
    Unresolved,
    Inconclusive,
}

/// <summary>What the executor did; the verdict is added after the check runs again.</summary>
public sealed record RepairResult
{
    public required string JobId { get; init; }

    public required string OperationId { get; init; }

    public required bool Completed { get; init; }

    /// <summary>English technical detail for the audit log, e.g. "Deleted 1,204 files".</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Space freed on the Windows drive, measured before and after (disk repairs only).</summary>
    public long BytesFreed { get; init; }

    /// <summary>What is needed to undo the repair exactly (e.g. previous startup settings); empty when not reversible.</summary>
    public IReadOnlyList<string> Rollback { get; init; } = [];

    public RepairVerdict? Verdict { get; init; }

    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Runs one approved job. Implementations live in the platform project.</summary>
public interface IRepairExecutor
{
    Task<RepairResult> ExecuteAsync(RepairJob job, CancellationToken cancellationToken);
}

/// <summary>Decides the verdict by comparing the finding before and after the repair.</summary>
public static class RepairVerification
{
    public static RepairVerdict Judge(Finding before, Finding? after, RepairResult result)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Completed || after is null || after.Severity == Severity.Unknown)
        {
            return RepairVerdict.Inconclusive;
        }

        // Fixed = healthy now, or clearly better and no longer a warning or critical finding.
        var noLongerAProblem = after.Severity is not (Severity.Warning or Severity.Critical);
        return after.Severity == Severity.Healthy || (noLongerAProblem && Rank(after.Severity) > Rank(before.Severity))
            ? RepairVerdict.Fixed
            : RepairVerdict.Unresolved;
    }

    private static int Rank(Severity severity) => severity switch
    {
        Severity.Critical => 0,
        Severity.Warning => 1,
        Severity.Information => 2,
        _ => 3,
    };
}

public static class RepairJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

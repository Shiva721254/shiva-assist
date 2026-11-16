using ShivaAssist.Core.Localization;

namespace ShivaAssist.Core.Findings;

/// <summary>How serious a finding is. <see cref="Unknown"/> means the check could not decide; it is never "healthy".</summary>
public enum Severity
{
    Critical,
    Warning,
    Information,
    Healthy,
    Unknown,
}

/// <summary>How sure the check is about its conclusion.</summary>
public enum Confidence
{
    High,
    Medium,
    Low,
}

/// <summary>Why a check ended as <see cref="Severity.Unknown"/> (finding contract, Appendix A).</summary>
public enum UnknownReason
{
    RequiresAdmin,
    Unsupported,
    Timeout,
    AccessDenied,
    Error,
}

/// <summary>Dashboard categories (architecture section 5.1).</summary>
public enum Category
{
    Hardware,
    WindowsSystem,
    Drivers,
    Security,
    Network,
    Performance,
    Applications,
    BootAndRecovery,
}

/// <summary>One measured fact behind a finding. Values are redacted before they get here.</summary>
public sealed record Evidence(string Source, string Observation);

/// <summary>
/// What to do about a finding. In stage 1 nothing is repaired, so <see cref="Operations"/> stays empty:
/// the contract only allows IDs from the signed repair catalogue, which arrives in stage 3.
/// </summary>
public sealed record Recommendation(Message Summary, bool RequiresAdmin = false)
{
    public IReadOnlyList<string> Operations { get; init; } = [];
}

/// <summary>
/// A single finding in the shared contract <c>shiva.assist.finding/1.0</c>. Reports, the dashboard and later
/// the repair planner all read this structure.
/// </summary>
public sealed record Finding
{
    public const string SchemaId = "shiva.assist.finding/1.0";

    public string Schema { get; init; } = SchemaId;

    /// <summary>Stable per finding type (<c>MODULE-TOPIC-NNN</c>): the same condition always yields the same ID.</summary>
    public required string Id { get; init; }

    public required string Module { get; init; }

    public required Category Category { get; init; }

    public required Severity Severity { get; init; }

    public Confidence Confidence { get; init; } = Confidence.High;

    /// <summary>One plain-language sentence: what was measured, not what it might mean.</summary>
    public required Message Title { get; init; }

    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    /// <summary>Always worded as a possibility, never as a fact.</summary>
    public Message? SuspectedCause { get; init; }

    public Recommendation? Recommendation { get; init; }

    /// <summary>Required when <see cref="Severity"/> is <see cref="Severity.Unknown"/>, absent otherwise.</summary>
    public UnknownReason? UnknownReason { get; init; }

    /// <summary>Set by the scan runner when the report is assembled.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    public static Finding Unknown(string id, string module, Category category, Message title, UnknownReason reason, string? detail = null) => new()
    {
        Id = id,
        Module = module,
        Category = category,
        Severity = Severity.Unknown,
        Confidence = Confidence.Low,
        Title = title,
        UnknownReason = reason,
        Evidence = detail is null ? [] : [new Evidence("check", detail)],
    };
}

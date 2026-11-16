using ShivaAssist.Core.Findings;

namespace ShivaAssist.Core.Scanning;

/// <summary>Facts about the scan itself that checks may need.</summary>
public sealed record ScanContext(bool IsElevated, DateTimeOffset Now);

/// <summary>
/// Reads one kind of system data, read-only. Implementations live in the platform project (WMI, registry,
/// event logs); tests use fakes. A source signals problems with exceptions, which the runner maps to Unknown:
/// <see cref="UnauthorizedAccessException"/> → access-denied, <see cref="PlatformNotSupportedException"/> → unsupported,
/// <see cref="RequiresAdminException"/> → requires-admin.
/// </summary>
public interface ISnapshotSource<T>
{
    Task<T> ReadAsync(ScanContext context, CancellationToken cancellationToken);
}

/// <summary>Thrown by a source whose data Windows only gives to administrators.</summary>
public sealed class RequiresAdminException : Exception
{
    public RequiresAdminException()
        : base("This information is only available to administrators.")
    {
    }

    public RequiresAdminException(string message)
        : base(message)
    {
    }

    public RequiresAdminException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>One diagnostic module. Checks never change the system.</summary>
public interface ICheck
{
    /// <summary>Module name in the finding contract, e.g. <c>storage</c>.</summary>
    string Module { get; }

    Category Category { get; }

    /// <summary>Finding ID used when the whole check fails (Unknown), e.g. <c>STORAGE-CHECK-000</c>.</summary>
    string FailureId { get; }

    /// <summary>Hard limit; the runner reports Unknown (timeout) when it is exceeded.</summary>
    TimeSpan TimeLimit { get; }

    Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A check in two halves: a platform source collects a snapshot, and a pure function turns it into findings.
/// The pure half holds all the judgement and is tested on any OS.
/// </summary>
public abstract class SnapshotCheck<T>(ISnapshotSource<T> source) : ICheck
{
    public abstract string Module { get; }

    public abstract Category Category { get; }

    public abstract string FailureId { get; }

    public virtual TimeSpan TimeLimit => TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var snapshot = await source.ReadAsync(context, cancellationToken);
        return Evaluate(snapshot, context);
    }

    public abstract IReadOnlyList<Finding> Evaluate(T snapshot, ScanContext context);
}

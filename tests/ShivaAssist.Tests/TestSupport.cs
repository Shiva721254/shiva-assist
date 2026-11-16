using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Tests;

/// <summary>A source that returns fixed data, so rules are tested without Windows.</summary>
internal sealed class Fixed<T>(T value) : ISnapshotSource<T>
{
    public Task<T> ReadAsync(ScanContext context, CancellationToken cancellationToken) => Task.FromResult(value);
}

/// <summary>A source that fails the way a Windows source can.</summary>
internal sealed class Failing<T>(Exception error) : ISnapshotSource<T>
{
    public Task<T> ReadAsync(ScanContext context, CancellationToken cancellationToken) => Task.FromException<T>(error);
}

internal static class Ctx
{
    /// <summary>Fixed "now" for date-dependent rules: 16 November 2026, the date the lifecycle table was checked.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public static ScanContext User => new(IsElevated: false, Now);

    public static ScanContext Admin => new(IsElevated: true, Now);

    public static Finding Single(IReadOnlyList<Finding> findings, string id) => findings.Single(f => f.Id == id);
}

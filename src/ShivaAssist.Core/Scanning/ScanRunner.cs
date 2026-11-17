using ShivaAssist.Core.Localization;
using System.Diagnostics;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Reporting;

namespace ShivaAssist.Core.Scanning;

/// <summary>
/// Runs every check in parallel, each under its own time limit, and assembles the report.
/// A check that throws, times out or produces an invalid finding never breaks the scan:
/// it becomes an Unknown finding with a reason (acceptance criteria AC-3 and AC-8).
/// </summary>
public sealed class ScanRunner(IReadOnlyList<ICheck> checks, TimeProvider? time = null)
{
    private readonly TimeProvider time = time ?? TimeProvider.System;

    public event Action<ICheck>? CheckCompleted;

    public async Task<ScanReport> RunAsync(ScanEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var started = time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var context = new ScanContext(environment.IsElevated, started);

        var results = await Task.WhenAll(checks.Select(check => RunOneAsync(check, context, cancellationToken)));

        var findings = results
            .SelectMany(f => f)
            .Select(WithRepairs)
            .Select(f => f with { Timestamp = started })
            .OrderBy(f => SeverityRank(f.Severity))
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        return new ScanReport
        {
            Product = environment.Product,
            StartedAt = started,
            DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1),
            System = new ScannedSystem(environment.OsName, environment.OsVersion, environment.IsElevated),
            Findings = findings,
        };
    }

    /// <summary>Lists the catalogue repairs for a finding in its recommendation (contract field <c>operations</c>).</summary>
    public static Finding WithRepairs(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var operations = Repairs.RepairCatalogue.For(finding).Select(o => o.Id).ToList();
        return operations.Count == 0 || finding.Recommendation is not { } advice
            ? finding
            : finding with { Recommendation = advice with { Operations = operations } };
    }

    /// <summary>Order in reports: what needs attention first.</summary>
    public static int SeverityRank(Severity severity) => severity switch
    {
        Severity.Critical => 0,
        Severity.Warning => 1,
        Severity.Unknown => 2,
        Severity.Information => 3,
        _ => 4,
    };

    private async Task<IReadOnlyList<Finding>> RunOneAsync(ICheck check, ScanContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(check.TimeLimit);
        try
        {
            // Task.Run keeps a check that blocks synchronously from stalling the others. The linked token stops
            // external processes at the time limit; WaitAsync is the backstop for a check that ignores it.
            findings = await Task.Run(() => check.RunAsync(context, limit.Token), limit.Token)
                .WaitAsync(check.TimeLimit + TimeSpan.FromSeconds(5), time, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            findings = [Failed(check, UnknownReason.Timeout, $"No result within {check.TimeLimit.TotalSeconds:0} seconds.")];
        }
        catch (RequiresAdminException)
        {
            findings = [Failed(check, UnknownReason.RequiresAdmin, "Run Shiva Assist as administrator to include this check.")];
        }
        catch (UnauthorizedAccessException)
        {
            findings = [Failed(check, UnknownReason.AccessDenied, "Windows denied access to this information.")];
        }
        catch (PlatformNotSupportedException)
        {
            findings = [Failed(check, UnknownReason.Unsupported, "This PC or Windows version does not provide this information.")];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One faulty check must never end the whole scan.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            findings = [Failed(check, UnknownReason.Error, ex.GetType().Name)];
        }

        var valid = findings.Select(f => FindingValidator.Validate(f).Count == 0
            ? f
            : Failed(check, UnknownReason.Error, $"Check produced an invalid finding ({f.Id}).")).ToList();

        CheckCompleted?.Invoke(check);
        return valid;
    }

    private static Finding Failed(ICheck check, UnknownReason reason, string detail) =>
        Finding.Unknown(check.FailureId, check.Module, check.Category, check.Name, reason, detail);
}

/// <summary>What the host knows about itself before the scan starts.</summary>
public sealed record ScanEnvironment(string Product, string OsName, string OsVersion, bool IsElevated);

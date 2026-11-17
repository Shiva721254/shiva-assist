using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Windows.Repairs;

/// <summary>
/// Runs an approved repair end to end: in-process when the current rights suffice, otherwise through the elevated
/// helper (one Windows permission prompt per job). Then repeats the check, decides the verdict and writes the
/// audit log.
/// </summary>
public sealed class RepairRunner(bool isElevated)
{
    public const string AgentFileName = "ShivaAssistAgent.exe";

    public static string DataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiva Assist");

    /// <summary>True when this job needs administrator rights (the operation itself, or entries registered for all users).</summary>
    public static bool NeedsAdmin(RepairOperation operation, RepairJob job)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(job);
        return operation.RequiresAdmin || job.Items.Any(i => i.StartsWith("HKLM-", StringComparison.Ordinal) || i.StartsWith("CommonFolder|", StringComparison.Ordinal));
    }

    public async Task<(RepairResult Result, Finding? After)> RunAsync(RepairOperation operation, RepairJob job, Finding before, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(before);

        RepairResult result;
        try
        {
            result = NeedsAdmin(operation, job) && !isElevated
                ? await RunElevatedAsync(job, cancellationToken)
                : await new WindowsRepairExecutor(isElevated).ExecuteAsync(job, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Win32Exception or JsonException)
        {
            result = new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false, Detail = ex.Message };
        }

        var after = result.Completed ? await RecheckAsync(before, cancellationToken) : null;
        result = result with { Verdict = RepairVerification.Judge(before, after, result) };
        RepairAudit.Append(result);
        return (result, after);
    }

    /// <summary>Runs the module that produced the finding again and returns the same finding.</summary>
    public async Task<Finding?> RecheckAsync(Finding before, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(before);
        var check = WindowsPlatform.CreateChecks().FirstOrDefault(c => c.Module == before.Module);
        if (check is null)
        {
            return null;
        }

        var report = await new ScanRunner([check]).RunAsync(new ScanEnvironment("recheck", string.Empty, string.Empty, isElevated), cancellationToken);
        return report.Findings.FirstOrDefault(f => f.Id == before.Id);
    }

    private static async Task<RepairResult> RunElevatedAsync(RepairJob job, CancellationToken cancellationToken)
    {
        var jobs = Path.Combine(DataFolder, "jobs");
        Directory.CreateDirectory(jobs);
        var jobPath = Path.Combine(jobs, job.JobId + ".job.json");
        var resultPath = Path.Combine(jobs, job.JobId + ".result.json");
        await File.WriteAllTextAsync(jobPath, RepairJson.Serialize(job with { ResultPath = resultPath }), cancellationToken);

        try
        {
            var agent = Path.Combine(AppContext.BaseDirectory, AgentFileName);
            if (!File.Exists(agent))
            {
                throw new InvalidOperationException($"{AgentFileName} was not found next to Shiva Assist.");
            }

            // runas shows the Windows permission prompt; declining throws Win32Exception (handled by the caller).
            using var process = Process.Start(new ProcessStartInfo(agent) { UseShellExecute = true, Verb = "runas", Arguments = "\"" + jobPath + "\"", WindowStyle = ProcessWindowStyle.Hidden })
                ?? throw new InvalidOperationException("The repair helper did not start.");
            await process.WaitForExitAsync(cancellationToken);

            return File.Exists(resultPath)
                ? RepairJson.Deserialize<RepairResult>(await File.ReadAllTextAsync(resultPath, cancellationToken)) ?? throw new JsonException("Empty result.")
                : throw new InvalidOperationException($"The repair helper ended without a result (exit code {process.ExitCode}).");
        }
        finally
        {
            File.Delete(jobPath);
            File.Delete(resultPath);
        }
    }
}

/// <summary>Append-only record of every repair: operation, outcome, freed space and rollback data. No personal data.</summary>
public static class RepairAudit
{
    public static string LogPath { get; } = Path.Combine(RepairRunner.DataFolder, "repair-log.jsonl");

    public static void Append(RepairResult result)
    {
        try
        {
            Directory.CreateDirectory(RepairRunner.DataFolder);
            File.AppendAllText(LogPath, RepairJson.Serialize(result).ReplaceLineEndings(" ") + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The repair itself succeeded or failed independently; a full or locked log must not hide that.
        }
    }
}

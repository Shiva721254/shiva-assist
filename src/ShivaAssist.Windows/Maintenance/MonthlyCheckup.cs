using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Reporting;
using ShivaAssist.Windows.Infrastructure;

namespace ShivaAssist.Windows.Maintenance;

/// <summary>
/// Optional automatic check-up (blueprint: automatic maintenance, only with consent). A Windows scheduled task runs
/// the read-only command-line scan every four weeks and saves the report; nothing is repaired automatically. Shiva
/// Assist shows the result the next time it opens. The customer switches it on and off on the help page.
/// </summary>
public static class MonthlyCheckup
{
    public const string TaskFolder = @"\Shiva Computers\";
    public const string TaskName = "Shiva Assist monthly check-up";

    public static string ReportPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiva Assist", "monthly-report.json");

    private static string CliPath => Path.Combine(AppContext.BaseDirectory, "ShivaAssistCli.exe");

    public static bool Available => File.Exists(CliPath);

    public static async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var json = await PowerShell.QueryAsync($$"""
            $task = Get-ScheduledTask -TaskPath '{{TaskFolder}}' -TaskName '{{TaskName}}' -ErrorAction SilentlyContinue
            [pscustomobject]@{ enabled = [bool]$task } | ConvertTo-Json -Compress
            """, cancellationToken);
        return PowerShell.Bool(json, "enabled");
    }

    /// <summary>Every 4 weeks on Monday 10:00; runs later when the PC was off (StartWhenAvailable), as the signed-in user, in the background.</summary>
    public static async Task EnableAsync(CancellationToken cancellationToken)
    {
        if (!Available)
        {
            throw new InvalidOperationException("ShivaAssistCli.exe was not found next to Shiva Assist.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        await PowerShell.QueryAsync($$"""
            $action = New-ScheduledTaskAction -Execute '{{Quote(CliPath)}}' -Argument '--plain --report "{{Quote(ReportPath)}}"'
            $trigger = New-ScheduledTaskTrigger -Weekly -WeeksInterval 4 -DaysOfWeek Monday -At '10:00'
            $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
            # S4U: runs in the background without a console window popping up, with normal (not administrator) rights.
            $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited
            Register-ScheduledTask -TaskPath '{{TaskFolder}}' -TaskName '{{TaskName}}' -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
                -Description 'Read-only PC check-up by Shiva Assist. Changes nothing on the PC.' -Force | Out-Null
            [pscustomobject]@{ ok = $true } | ConvertTo-Json -Compress
            """, cancellationToken);
    }

    public static async Task DisableAsync(CancellationToken cancellationToken) =>
        await PowerShell.QueryAsync($$"""
            Unregister-ScheduledTask -TaskPath '{{TaskFolder}}' -TaskName '{{TaskName}}' -Confirm:$false -ErrorAction SilentlyContinue
            [pscustomobject]@{ ok = $true } | ConvertTo-Json -Compress
            """, cancellationToken);

    /// <summary>The last automatic report, or null when there is none or it cannot be read.</summary>
    public static (DateTimeOffset When, int Problems)? ReadLatest()
    {
        try
        {
            if (!File.Exists(ReportPath))
            {
                return null;
            }

            var report = ReportJson.Deserialize(File.ReadAllText(ReportPath));
            return report is null ? null : (report.StartedAt, report.Findings.Count(f => f.Severity is Severity.Critical or Severity.Warning));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Paths go into single-quoted PowerShell strings: double any single quote.</summary>
    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

using ShivaAssist.Windows.Infrastructure;

namespace ShivaAssist.Windows.Sources;

public sealed record WindowsUpdateOutcome(long Installed, long Failed, long Skipped, bool RestartRequired);

public static class WindowsUpdateInstaller
{
    public static async Task<WindowsUpdateOutcome> RunAsync()
    {
        if (!WindowsPlatform.IsElevated())
        {
            throw new UnauthorizedAccessException();
        }

        // Use the configured Windows Update service and respect its policy and update assignments.
        // Avoid interrupting the installer once it starts, and let the customer choose when to restart.
        var result = await PowerShell.QueryAsync("""
            $session = New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID = 'Shiva Assist'
            $installed = $failed = $skipped = 0
            $systemInfo = New-Object -ComObject Microsoft.Update.SystemInfo
            $restart = $systemInfo.RebootRequired
            if (-not $restart) {
                $searcher = $session.CreateUpdateSearcher()
                $search = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software' and IsAssigned=1 and BrowseOnly=0")
                if ($search.ResultCode -ne 2) { throw 'Windows Update search did not fully succeed.' }
                $downloadable = New-Object -ComObject Microsoft.Update.UpdateColl
                foreach ($update in $search.Updates) {
                    if (-not $update.EulaAccepted -or $update.InstallationBehavior.CanRequestUserInput) { $skipped++; continue }
                    [void]$downloadable.Add($update)
                }
                if ($downloadable.Count -gt 0) {
                    $downloader = $session.CreateUpdateDownloader()
                    $downloader.Updates = $downloadable
                    $download = $downloader.Download()
                    $ready = New-Object -ComObject Microsoft.Update.UpdateColl
                    for ($i = 0; $i -lt $downloadable.Count; $i++) {
                        $update = $downloadable.Item($i)
                        if ($download.GetUpdateResult($i).ResultCode -eq 2 -and $update.IsDownloaded) {
                            [void]$ready.Add($update)
                        } else { $failed++ }
                    }
                    if ($ready.Count -gt 0) {
                        $installer = $session.CreateUpdateInstaller()
                        $installer.Updates = $ready
                        if ($installer.RebootRequiredBeforeInstallation -or $installer.IsBusy) {
                            $restart = $installer.RebootRequiredBeforeInstallation
                            $skipped += $ready.Count
                        } else {
                            $installer.AllowSourcePrompts = $false
                            $installer.ForceQuiet = $true
                            $result = $installer.Install()
                            for ($i = 0; $i -lt $ready.Count; $i++) {
                                if ($result.GetUpdateResult($i).ResultCode -eq 2) { $installed++ } else { $failed++ }
                            }
                            $restart = $result.RebootRequired -or $systemInfo.RebootRequired
                        }
                    }
                }
            }
            [pscustomobject]@{ installed = $installed; failed = $failed; skipped = $skipped; restart = $restart } | ConvertTo-Json -Compress
            """, CancellationToken.None);
        return new(PowerShell.Long(result, "installed"), PowerShell.Long(result, "failed"),
            PowerShell.Long(result, "skipped"), PowerShell.Bool(result, "restart"));
    }
}

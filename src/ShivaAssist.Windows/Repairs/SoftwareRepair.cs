using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Software;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.Windows.Repairs;

public sealed partial class WindowsRepairExecutor
{
    private static async Task<RepairResult> UpdateSoftwareAsync(RepairJob job, CancellationToken cancellationToken)
    {
        if (job.Items.Count != 3 || !PackageUpdates.IsValidId(job.Items[0]))
        {
            throw new ArgumentException("Select one valid package ID and its checked versions.");
        }

        var id = job.Items[0];
        // Revalidate the package against the current source instead of trusting a saved or edited job file.
        var updates = await PackageUpdateSource.ReadAsync(cancellationToken);
        var selected = updates.SingleOrDefault(update => update.Id == id
            && update.InstalledVersion == job.Items[1] && update.AvailableVersion == job.Items[2])
            ?? throw new InvalidOperationException("The selected update has changed. Check updates again before proceeding.");
        var (exit, _, _) = await PackageUpdateSource.RunAsync(
            ["upgrade", "--id", id, "--exact", "--source", "winget", "--version", selected.AvailableVersion,
                "--interactive", "--disable-interactivity"], cancellationToken);
        if (exit != 0)
        {
            return new RepairResult
            {
                JobId = job.JobId, OperationId = job.OperationId, Completed = false,
                Verdict = RepairVerdict.Inconclusive, Detail = "WinGet ended with code " + exit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }

        var (checkExit, output, _) = await PackageUpdateSource.RunAsync(
            ["list", "--id", id, "--exact", "--source", "winget", "--disable-interactivity"], cancellationToken);
        var verified = checkExit == 0 && PackageUpdates.InstalledVersion(output, id) == selected.AvailableVersion;
        return new RepairResult
        {
            JobId = job.JobId, OperationId = job.OperationId, Completed = true,
            Verdict = verified ? RepairVerdict.Fixed : RepairVerdict.Inconclusive,
            Detail = verified ? "Installed version verified for " + id : "Installer finished; installed version could not be verified for " + id,
        };
    }
}

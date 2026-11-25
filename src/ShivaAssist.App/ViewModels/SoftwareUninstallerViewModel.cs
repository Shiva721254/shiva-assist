using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Software;
using ShivaAssist.Windows;
using ShivaAssist.Windows.Repairs;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.App.ViewModels;

public sealed partial class SoftwareViewModel
{
    private SoftwareItem? selectedProgram;
    private UninstallSessionItem? selectedSession;
    private bool uninstallerExpanded;
    private bool changingChoices;
    private string uninstallStatus = string.Empty;

    public RelayCommand UninstallCommand { get; }
    public RelayCommand ScanLeftoversCommand { get; }
    public RelayCommand CleanLeftoversCommand { get; }
    public ObservableCollection<UninstallSessionItem> UninstallSessions { get; } = [];
    public ObservableCollection<LeftoverChoice> Leftovers { get; } = [];
    public bool CanManagePrograms => !loading && !checkingUpdates && !repairBusy();
    public bool HasLeftovers => Leftovers.Count > 0;
    public bool HasUninstallSessions => UninstallSessions.Count > 0;
    public string UninstallStatus => uninstallStatus;
    public string TextUninstall => T("Uninstall selected program");
    public string TextWindowsUninstall => T("Open Windows uninstaller instead");
    public string TextScanLeftovers => T("Check removal and scan leftovers");
    public string TextCleanLeftovers => T("Clean selected leftovers");
    public string TextUninstallScope => T("Select a program in the list below, then uninstall it here. A restore point is required. This version supports MSI packages and registered desktop uninstallers inside Program Files. Other programs can use the Windows uninstaller fallback.");
    public string TextLeftoverScope => T("Cleanup reviews only the recorded installation folder. Unchanged program binaries, logs, temporary files, caches and empty folders may be offered. Documents, saved settings, user profiles, registry keys, links, shared folders and unrecognised files are preserved. Nothing is selected automatically. Files move to recovery storage; disk space is not reclaimed. Undo is available in History.");
    public string TextRemovalSession => T("Previous removal");
    public string TextFolder => T("Folder");
    public string TextFile => T("File");
    public string TextPath => T("Location");
    public string TextType => T("Type");
    public string TextSelect => T("Select");
    public string TextSelectedProgram => selectedProgram is null ? T("No program selected")
        : new Message("Selected: {name} · {version}", ("name", selectedProgram.Name), ("version", selectedProgram.Version)).Render(catalog());

    public bool UninstallerExpanded
    {
        get => uninstallerExpanded;
        set => Set(ref uninstallerExpanded, value);
    }

    public SoftwareItem? SelectedProgram
    {
        get => selectedProgram;
        set
        {
            if (Set(ref selectedProgram, value))
            {
                OnPropertyChanged(nameof(TextSelectedProgram));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public UninstallSessionItem? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (Set(ref selectedSession, value))
            {
                Leftovers.Clear();
                OnPropertyChanged(nameof(HasLeftovers));
                SetUninstallStatus(T("Check the selected removal before reviewing leftovers."));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private async Task UninstallAsync()
    {
        var selected = selectedProgram;
        if (!technicianMode || selected is null || !CanManagePrograms)
        {
            return;
        }

        UninstallerExpanded = true;
        SetSoftwareBusy(true);
        try
        {
            var program = await Task.Run(() => SoftwareSource.ReadUninstaller(selected.Program.Registration));
            if (program.Name != selected.Name || program.Version != selected.Version || program.Publisher != selected.Publisher)
            {
                SetUninstallStatus(T("The selected program changed. Refresh the program list before uninstalling."));
                return;
            }

            var launch = UninstallerService.LaunchInfo(program); // Validate before asking or making changes.
            var operation = RepairCatalogue.Find(RepairCatalogue.UninstallSoftware)!;
            var prompt = new Message("Uninstall {name} ({version}) by {publisher}?", ("name", program.Name),
                ("version", program.Version), ("publisher", program.Publisher)).Render(catalog())
                + Environment.NewLine + Environment.NewLine + operation.Description.Render(catalog())
                + Environment.NewLine + Environment.NewLine + launch.FileName;
            if (MessageBox.Show(prompt, operation.Name.Render(catalog()), MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            var job = new RepairJob
            {
                JobId = Guid.NewGuid().ToString("N"), OperationId = operation.Id, ApprovedAt = DateTimeOffset.UtcNow,
                Items = [program.Reference, program.Name, program.Version, program.Publisher, UninstallerService.CommandHash(program)],
            };
            SetUninstallStatus(T("Uninstalling the selected program. Follow its normal prompts. Do not turn off the PC."));
            // No timeout that kills the uninstaller while it is changing the installation.
            var result = await ExecuteSoftwareJobAsync(job);
            await LoadAsync();
            SelectedSession = UninstallSessions.FirstOrDefault(item => item.Session.Id == job.JobId);
            SetUninstallStatus(result.Verdict switch
            {
                RepairVerdict.Fixed => T("Removal verified. Check for leftovers when you are ready; cleanup needs separate approval."),
                RepairVerdict.Pending => T("The uninstaller requested a restart. Restart Windows, reopen Shiva Assist and check this removal again before cleaning leftovers."),
                _ => T("Removal was cancelled, failed or could not be verified. Finish any uninstaller prompts and check again. Nothing was cleaned."),
            });
            if (result.Verdict == RepairVerdict.Inconclusive)
            {
                SetUninstallStatus(UninstallStatus + Environment.NewLine + catalog().Translate(result.Detail));
            }
        }
        catch (Exception ex) when (UninstallError(ex))
        {
            ShowUninstallError(ex);
        }
        finally
        {
            SetSoftwareBusy(false);
            RefreshAll();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ScanLeftoversAsync()
    {
        var session = selectedSession;
        if (!technicianMode || session is null || !CanManagePrograms)
        {
            return;
        }

        SetSoftwareBusy(true);
        Leftovers.Clear();
        SetUninstallStatus(T("Verifying removal and checking the recorded installation folder…"));
        try
        {
            var scan = await Task.Run(() => UninstallerService.Scan(session.Session.Id));
            foreach (var entry in scan.Candidates.OrderBy(e => e.IsDirectory).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var choice = new LeftoverChoice(entry, session.Session.Before!.Root, () => catalog());
                choice.PropertyChanged += (_, _) => ChangeLeftoverSelection(choice);
                Leftovers.Add(choice);
            }

            SetUninstallStatus(new Message("{count} leftover items available for review · {preserved} files preserved · {changed} changed files excluded",
                ("count", scan.Candidates.Count), ("preserved", scan.PreservedFiles), ("changed", scan.ChangedFiles)).Render(catalog()));
        }
        catch (Exception ex) when (UninstallError(ex))
        {
            ShowUninstallError(ex);
        }
        finally
        {
            SetSoftwareBusy(false);
            OnPropertyChanged(nameof(HasLeftovers));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task CleanLeftoversAsync()
    {
        var session = selectedSession;
        var selected = Leftovers.Where(item => item.Selected).Select(item => item.Entry).ToArray();
        if (!technicianMode || session is null || selected.Length == 0 || !CanManagePrograms)
        {
            return;
        }

        var operation = RepairCatalogue.Find(RepairCatalogue.CleanSoftwareLeftovers)!;
        var prompt = new Message("Clean {count} selected leftover items?", ("count", selected.Length)).Render(catalog())
            + Environment.NewLine + Environment.NewLine + operation.Description.Render(catalog());
        if (MessageBox.Show(prompt, operation.Name.Render(catalog()), MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetSoftwareBusy(true);
        try
        {
            var job = new RepairJob
            {
                JobId = Guid.NewGuid().ToString("N"), OperationId = operation.Id, ApprovedAt = DateTimeOffset.UtcNow,
                Items = [session.Session.Id, .. selected.Select(entry => entry.Key)],
            };
            SetUninstallStatus(T("Cleaning the selected leftovers into recovery storage…"));
            var result = await ExecuteSoftwareJobAsync(job);
            Leftovers.Clear(); // Require another scan before any further changes.
            SetUninstallStatus(result.Verdict == RepairVerdict.Fixed
                ? T("Selected leftovers were cleaned. Recovery copies remain on disk. Use History to undo this cleanup; this does not reinstall the program.")
                : T("Cleanup did not fully complete. Check History for the result and any available Undo, then scan again."));
        }
        catch (Exception ex) when (UninstallError(ex))
        {
            ShowUninstallError(ex);
        }
        finally
        {
            SetSoftwareBusy(false);
            OnPropertyChanged(nameof(HasLeftovers));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static async Task<RepairResult> ExecuteSoftwareJobAsync(RepairJob job)
    {
        RepairResult result;
        try
        {
            result = await Task.Run(() => new WindowsRepairExecutor(WindowsPlatform.IsElevated()).ExecuteAsync(job, CancellationToken.None));
        }
        catch (Exception ex) when (UninstallError(ex))
        {
            result = new RepairResult
            {
                JobId = job.JobId, OperationId = job.OperationId, Completed = false,
                Verdict = RepairVerdict.Inconclusive, Detail = ex.Message,
            };
        }

        RepairAudit.Append(result);
        return result;
    }

    private void LoadUninstallSessions()
    {
        var previous = selectedSession?.Session.Id;
        UninstallSessions.Clear();
        try
        {
            foreach (var session in UninstallerService.Sessions())
            {
                UninstallSessions.Add(new(session, session.PreparedAt.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(catalog().Language.Code))
                    + " · " + session.Program.Name + " · " + session.Program.Version));
            }

            SelectedSession = UninstallSessions.FirstOrDefault(item => item.Session.Id == previous) ?? UninstallSessions.FirstOrDefault();
        }
        catch (Exception ex) when (UninstallError(ex))
        {
            ShowUninstallError(ex);
        }

        OnPropertyChanged(nameof(HasUninstallSessions));
    }

    private void RefreshUninstallLanguage()
    {
        foreach (var item in Leftovers)
        {
            item.Refresh();
        }
    }

    private void ChangeLeftoverSelection(LeftoverChoice changed)
    {
        if (changingChoices)
        {
            return;
        }

        changingChoices = true;
        try
        {
            foreach (var item in Leftovers.Where(item => item != changed))
            {
                if (changed.Entry.IsDirectory && IsChildOf(item.Entry, changed.Entry))
                {
                    item.Selected = changed.Selected;
                }
                else if (!changed.Selected && item.Entry.IsDirectory && IsChildOf(changed.Entry, item.Entry))
                {
                    item.Selected = false;
                }
            }
        }
        finally
        {
            changingChoices = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static bool IsChildOf(LeftoverEntry child, LeftoverEntry folder) => folder.RelativePath.Length == 0
        ? child.RelativePath.Length > 0
        : child.RelativePath.StartsWith(folder.RelativePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void SetSoftwareBusy(bool busy)
    {
        setRepairBusy(busy);
        RefreshAll();
        CommandManager.InvalidateRequerySuggested();
    }

    public void RefreshBusyState() => OnPropertyChanged(nameof(CanManagePrograms));

    private void SetUninstallStatus(string message)
    {
        uninstallStatus = message;
        OnPropertyChanged(nameof(UninstallStatus));
    }

    private void ShowUninstallError(Exception exception) => SetUninstallStatus(new Message("Uninstaller: {reason}",
        ("reason", catalog().Translate(exception.Message))).Render(catalog()));

    private static bool UninstallError(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or Win32Exception or JsonException or System.Security.SecurityException or NotSupportedException or OperationCanceledException;
}

public sealed record UninstallSessionItem(UninstallSession Session, string Label);

public sealed class LeftoverChoice(LeftoverEntry entry, string root, Func<TextCatalog> catalog) : Observable
{
    private bool selected;
    public LeftoverEntry Entry { get; } = entry;
    public string PathLabel => Entry.RelativePath.Length == 0 ? root : Path.Combine(root, Entry.RelativePath);
    public string Kind => catalog().Translate(Entry.IsDirectory ? "Folder" : "File");
    public string Size => Entry.IsDirectory ? string.Empty : (Entry.Bytes / 1048576d).ToString("N2", CultureInfo.GetCultureInfo(catalog().Language.Code)) + " MB";
    public bool Selected { get => selected; set => Set(ref selected, value); }
    public void Refresh() => RefreshAll();
}

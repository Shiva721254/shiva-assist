using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.ComponentModel;
using System.Windows;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Software;
using ShivaAssist.Windows;
using ShivaAssist.Windows.Repairs;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.App.ViewModels;

public sealed partial class SoftwareViewModel : Observable
{
    private readonly Func<TextCatalog> catalog;
    private IReadOnlyList<InstalledProgram> programs = [];
    private string search = string.Empty;
    private bool loading;
    private bool loaded;
    private bool failed;
    private int unreadable;
    private readonly bool technicianMode;
    private readonly Func<bool> repairBusy;
    private readonly Action<bool> setRepairBusy;
    private bool checkingUpdates;
    private string? updateStatus;
    private PackageUpdate? selectedUpdate;

    public SoftwareViewModel(Func<TextCatalog> catalog, bool technicianMode, Func<bool> repairBusy, Action<bool> setRepairBusy)
    {
        this.catalog = catalog;
        this.technicianMode = technicianMode;
        this.repairBusy = repairBusy;
        this.setRepairBusy = setRepairBusy;
        SelectGroupCommand = new RelayCommand(group => SelectGroup(group as ApplicationGroupFilter));
        System.Windows.Data.CollectionViewSource.GetDefaultView(Items).GroupDescriptions?.Add(
            new System.Windows.Data.PropertyGroupDescription(nameof(SoftwareItem.Group)));
        UninstallCommand = new RelayCommand(async _ => await UninstallAsync(), _ => technicianMode && selectedProgram is not null && CanManagePrograms);
        ScanLeftoversCommand = new RelayCommand(async _ => await ScanLeftoversAsync(), _ => technicianMode && selectedSession is not null && CanManagePrograms);
        CleanLeftoversCommand = new RelayCommand(async _ => await CleanLeftoversAsync(), _ => technicianMode && Leftovers.Any(item => item.Selected) && CanManagePrograms);
        InstallWindowsUpdatesCommand = new RelayCommand(async _ => await InstallUpdatesAsync(includePrograms: false), _ => CanInstallUpdates);
        InstallWindowsAndAppsCommand = new RelayCommand(async _ => await InstallUpdatesAsync(includePrograms: true), _ => CanInstallUpdates);
        OpenDriverUpdatesCommand = new RelayCommand(_ => OpenSettings("ms-settings:windowsupdate-optionalupdates"), _ => !repairBusy());
        OpenUninstallerCommand = new RelayCommand(_ => { ActivePanel = "uninstaller"; UninstallerExpanded = true; }, _ => !repairBusy());
        WindowsUninstallCommand = new RelayCommand(_ => OpenSettings("ms-settings:appsfeatures"), _ => !repairBusy());
        RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !loading && !repairBusy());
        CheckUpdatesCommand = new RelayCommand(async _ => await CheckUpdatesAsync(), _ => !checkingUpdates && !repairBusy());
        UpdateCommand = new RelayCommand(async _ => await UpdateAsync(), _ => technicianMode && selectedUpdate is not null && !checkingUpdates && !repairBusy());
        UpdateBatchCommand = new RelayCommand(async _ => await UpdateBatchAsync(),
            _ => technicianMode && UpdateItems.Any(item => item.IsSelected) && !checkingUpdates && !repairBusy());
    }

    public RelayCommand InstallWindowsUpdatesCommand { get; }
    public RelayCommand InstallWindowsAndAppsCommand { get; }
    private bool CanInstallUpdates => technicianMode && !checkingUpdates && !repairBusy();
    public string TextInstallWindows => T("Update Windows");
    public string TextInstallWindowsAndApps => T("Update Windows and apps");
    public string? InstallStatus { get; private set; }
    public RelayCommand OpenDriverUpdatesCommand { get; }
    public RelayCommand OpenUninstallerCommand { get; }
    public RelayCommand WindowsUninstallCommand { get; }
    public string TextWindowsUpdate => T("Windows Update");
    public string TextDriverUpdates => T("Driver updates");
    public string TextUninstaller => T("Uninstaller");
    public string TextWindowsTools => T("Install recommended Windows updates here. For optional drivers, use Windows Settings or your PC manufacturer's official support tools.");

    private void OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            SetUpdateStatus(T("Windows Settings could not be opened. Open Settings from the Start menu and try again."));
            MessageBox.Show(UpdateStatus, T("Windows Settings"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand UpdateBatchCommand { get; }
    public ObservableCollection<PackageUpdate> Updates { get; } = [];
    public ObservableCollection<PackageUpdateItem> UpdateItems { get; } = [];
    public bool ShowUpdateAction => technicianMode;
    public string TextCheckUpdates => T("Check program updates");
    public string TextUpdateSelected => T("Update selected program");
    public string TextUpdateChecked => SelectedUpdateCount switch
    {
        0 => T("Update selected programs"),
        1 => T("Update 1 selected program"),
        var count => new Message("Update {count} selected programs", ("count", count)).Render(catalog()),
    };
    private int SelectedUpdateCount => UpdateItems.Count(item => item.IsSelected);
    public string TextAvailableVersion => T("Available version");
    public string TextUpdateScope => T("Checks the WinGet community source over the internet. Only programs recognised by Windows Package Manager can appear here.");
    public string? UpdateStatus => updateStatus;
    public PackageUpdate? SelectedUpdate
    {
        get => selectedUpdate;
        set
        {
            Set(ref selectedUpdate, value);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public ObservableCollection<SoftwareItem> Items { get; } = [];
    public string TextSearch => T("Search installed programs");
    public string TextRefresh => T("Refresh programs");
    public string TextScope => T("Programs registered in Windows are listed here. Store apps and portable programs may be missing. Sizes are estimates supplied by the publisher.");
    public string TextName => T("Program");
    public string TextPublisher => T("Publisher");
    public string TextVersion => T("Version");
    public string TextSize => T("Estimated size");
    public string TextDate => T("Installed on");
    public string Status => loading ? T("Reading installed programs…")
        : failed ? T("The program list could not be read. Please try again.")
        : unreadable > 0 ? T("Some program entries could not be read. The list may be incomplete.")
        : loaded ? new Message("{count} programs shown", ("count", Items.Count)).Render(catalog()) : string.Empty;

    public string Search
    {
        get => search;
        set
        {
            if (Set(ref search, value))
            {
                ApplyFilter();
            }
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (!loaded && !loading)
        {
            await LoadAsync();
        }
    }

    public void RefreshLanguage()
    {
        ApplyFilter();
        RefreshUninstallLanguage();
        RefreshAll();
    }

    private async Task CheckUpdatesAsync()
    {
        checkingUpdates = true;
        RefreshAll();
        Updates.Clear();
        UpdateItems.Clear();
        SelectedUpdate = null;
        SetUpdateStatus(T("Checking program updates…"));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var updates = await PackageUpdateSource.ReadAsync(timeout.Token);
            foreach (var update in updates)
            {
                Updates.Add(update);
                var row = new PackageUpdateItem(update);
                row.PropertyChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(TextUpdateChecked));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                };
                UpdateItems.Add(row);
            }

            SetUpdateStatus(updates.Count == 0
                ? T("All supported programs are up to date.")
                : T("Untick any program you want to skip, then update the selected programs."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or IOException or Win32Exception or UnauthorizedAccessException)
        {
            SetUpdateStatus(catalog().Translate(ex.Message));
        }
        catch (OperationCanceledException)
        {
            SetUpdateStatus(T("The update check timed out. Please try again."));
        }
        finally
        {
            checkingUpdates = false;
            RefreshAll();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task UpdateBatchAsync()
    {
        var chosen = UpdateItems.Where(item => item.IsSelected).ToList();
        if (!technicianMode || chosen.Count == 0 || checkingUpdates || repairBusy())
        {
            return;
        }

        if (chosen.Count == 1)
        {
            SelectedUpdate = chosen[0].Update;
            await UpdateAsync();
            return;
        }

        var operation = RepairCatalogue.Find(RepairCatalogue.UpdateSoftware)!;
        var explanation = new Message(
            "Update {count} selected programs? Save your work and close those programs first. Some installers may ask for administrator permission or licence acceptance.",
            ("count", chosen.Count)).Render(catalog());
        if (MessageBox.Show(Application.Current.MainWindow, explanation, operation.Name.Render(catalog()),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetSoftwareBusy(true);
        var verified = 0;
        try
        {
            for (var index = 0; index < chosen.Count; index++)
            {
                var item = chosen[index];
                SetUpdateStatus(new Message("Updating {done} of {total}: {name}…",
                    ("done", index + 1), ("total", chosen.Count), ("name", item.Name)).Render(catalog()));
                if (await InstallProgramAsync(item.Update))
                {
                    verified++;
                    UpdateItems.Remove(item);
                    Updates.Remove(item.Update);
                }
            }

            SelectedUpdate = null;
            SetUpdateStatus(new Message("App updates: {verified} verified, {unverified} could not be verified.",
                ("verified", verified), ("unverified", chosen.Count - verified)).Render(catalog()));
        }
        finally
        {
            SetSoftwareBusy(false);
            OnPropertyChanged(nameof(TextUpdateChecked));
        }

        await LoadAsync();
    }

    private async Task UpdateAsync()
    {
        var selected = selectedUpdate;
        if (!technicianMode || selected is null || repairBusy())
        {
            return;
        }

        var operation = RepairCatalogue.Find(RepairCatalogue.UpdateSoftware)!;
        var explanation = new Message("Update {name} from {current} to {available}? Save your work and close the program first. Its installer may ask for administrator permission or licence acceptance.",
            ("name", selected.Name), ("current", selected.InstalledVersion), ("available", selected.AvailableVersion)).Render(catalog());
        if (MessageBox.Show(explanation, operation.Name.Render(catalog()), MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetSoftwareBusy(true);
        SetUpdateStatus(T("Updating the selected program. Follow any installer prompts."));
        try
        {
            var verified = await InstallProgramAsync(selected);
            Updates.Remove(selected);
            var matchingItem = UpdateItems.FirstOrDefault(item => item.Update == selected);
            if (matchingItem is not null)
            {
                UpdateItems.Remove(matchingItem);
            }

            SelectedUpdate = null;
            OnPropertyChanged(nameof(TextUpdateChecked));
            SetUpdateStatus(verified
                ? T("The program was updated and its installed version was verified.")
                : T("The update could not be verified. Check the program and refresh the update list before trying again."));
        }
        finally
        {
            SetSoftwareBusy(false);
        }
        await LoadAsync();
    }

    private static async Task<bool> InstallProgramAsync(PackageUpdate selected)
    {
        var operation = RepairCatalogue.Find(RepairCatalogue.UpdateSoftware)!;
        var job = new RepairJob
        {
            JobId = Guid.NewGuid().ToString("N"), OperationId = operation.Id, ApprovedAt = DateTimeOffset.UtcNow,
            Items = [selected.Id, selected.InstalledVersion, selected.AvailableVersion],
        };
        RepairResult result;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            result = await new WindowsRepairExecutor(WindowsPlatform.IsElevated()).ExecuteAsync(job, timeout.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or IOException
            or Win32Exception or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            result = new RepairResult
            {
                JobId = job.JobId, OperationId = job.OperationId, Completed = false,
                Verdict = RepairVerdict.Inconclusive, Detail = ex.GetType().Name,
            };
        }
        RepairAudit.Append(result);
        return result.Verdict == RepairVerdict.Fixed;
    }

    private async Task InstallUpdatesAsync(bool includePrograms)
    {
        if (!CanInstallUpdates)
        {
            return;
        }

        var question = includePrograms
            ? T("Install recommended Windows updates and supported app updates? Save your work and close your apps first. Some app installers may ask for your input.")
            : T("Install recommended Windows updates? Save your work first. Shiva Assist will tell you if a restart is needed.");
        if (MessageBox.Show(Application.Current.MainWindow, question, includePrograms ? TextInstallWindowsAndApps : TextInstallWindows,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetSoftwareBusy(true);
        var summary = new List<string>();
        try
        {
            ShowInstallStatus(T("Checking and installing Windows updates…"));
            try
            {
                var outcome = await WindowsUpdateInstaller.RunAsync();
                summary.Add(new Message("Windows updates: {installed} installed, {failed} failed, {skipped} skipped.",
                    ("installed", outcome.Installed), ("failed", outcome.Failed), ("skipped", outcome.Skipped)).Render(catalog()));
                if (outcome.RestartRequired)
                {
                    summary.Add(T("Restart Windows to finish updating."));
                }
            }
            catch (Exception ex) when (IsUpdateError(ex))
            {
                summary.Add(T("Windows Update did not report a completed result. Try again later."));
            }

            if (includePrograms)
            {
                ShowInstallStatus(T("Checking supported app updates…"));
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    var available = await PackageUpdateSource.ReadAsync(timeout.Token);
                    int verified = 0;
                    foreach (var update in available)
                    {
                        ShowInstallStatus(new Message("Updating {name}…", ("name", update.Name)).Render(catalog()));
                        if (await InstallProgramAsync(update))
                        {
                            verified++;
                        }
                    }

                    Updates.Clear();
                    UpdateItems.Clear();
                    SelectedUpdate = null;
                    summary.Add(new Message("App updates: {verified} verified, {unverified} could not be verified.",
                        ("verified", verified), ("unverified", available.Count - verified)).Render(catalog()));
                }
                catch (Exception ex) when (IsUpdateError(ex))
                {
                    summary.Add(T("App updating could not finish. Check program updates before trying again."));
                }
            }
        }
        finally
        {
            ShowInstallStatus(string.Join(Environment.NewLine, summary));
            SetSoftwareBusy(false);
        }
    }

    private static bool IsUpdateError(Exception ex) => ex is InvalidOperationException or PlatformNotSupportedException
        or IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException;

    private void ShowInstallStatus(string message)
    {
        InstallStatus = message;
        OnPropertyChanged(nameof(InstallStatus));
    }

    private void SetUpdateStatus(string value)
    {
        updateStatus = value;
        OnPropertyChanged(nameof(UpdateStatus));
    }

    private async Task LoadAsync()
    {
        loading = true;
        failed = false;
        RefreshAll();
        try
        {
            var result = await Task.Run(() => SoftwareSource.Read(CancellationToken.None));
            programs = result.Programs;
            unreadable = result.UnreadableEntries;
            loaded = true;
            ApplyFilter();
            LoadUninstallSessions();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            failed = true;
        }
        finally
        {
            loading = false;
            RefreshAll();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        SelectedProgram = null;
        var query = search.Trim();
        var culture = CultureInfo.GetCultureInfo(catalog().Language.Code);
        var matching = programs.Where(program => program.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || program.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        BuildGroups(matching);
        foreach (var program in matching.Where(program => activeGroup is null || ApplicationGroups.Suggest(program.Name) == activeGroup))
        {
            Items.Add(new SoftwareItem(program.Name, program.Publisher, program.Version,
                program.SizeBytes is long bytes ? (bytes / 1048576d).ToString("N1", culture) + " MB" : "—",
                program.InstalledOn?.ToString("d", culture) ?? "—", program)
            {
                Group = ApplicationGroups.Name(ApplicationGroups.Suggest(program.Name)).Render(catalog()),
            });
        }

        OnPropertyChanged(nameof(Status));
    }

    private string T(string text) => catalog().Translate(text);
}

public sealed record SoftwareItem(string Name, string Publisher, string Version, string Size, string Date, InstalledProgram Program)
{
    public required string Group { get; init; }
    public string Glyph => ApplicationGroups.Glyph(ApplicationGroups.Suggest(Name));
}

/// <summary>One available program update with a checkbox for batch updating.</summary>
public sealed class PackageUpdateItem(PackageUpdate update) : Observable
{
    private bool isSelected = true;

    public PackageUpdate Update { get; } = update;

    public string Name => Update.Name;

    public string Id => Update.Id;

    public string InstalledVersion => Update.InstalledVersion;

    public string AvailableVersion => Update.AvailableVersion;

    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }
}

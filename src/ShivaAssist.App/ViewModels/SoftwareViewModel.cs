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

public sealed class SoftwareViewModel : Observable
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
        OpenWindowsUpdateCommand = new RelayCommand(_ => OpenSettings("ms-settings:windowsupdate"), _ => !repairBusy());
        OpenDriverUpdatesCommand = new RelayCommand(_ => OpenSettings("ms-settings:windowsupdate-optionalupdates"), _ => !repairBusy());
        OpenUninstallerCommand = new RelayCommand(_ => OpenSettings("ms-settings:appsfeatures"), _ => !repairBusy());
        RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !loading && !repairBusy());
        CheckUpdatesCommand = new RelayCommand(async _ => await CheckUpdatesAsync(), _ => !checkingUpdates && !repairBusy());
        UpdateCommand = new RelayCommand(async _ => await UpdateAsync(), _ => technicianMode && selectedUpdate is not null && !checkingUpdates && !repairBusy());
    }

    public RelayCommand OpenWindowsUpdateCommand { get; }
    public RelayCommand OpenDriverUpdatesCommand { get; }
    public RelayCommand OpenUninstallerCommand { get; }
    public string TextWindowsUpdate => T("Windows Update");
    public string TextDriverUpdates => T("Driver updates");
    public string TextUninstaller => T("Uninstaller");
    public string TextWindowsTools => T("Open Windows Settings to review Windows updates, optional driver updates or uninstall a program. Choose and confirm changes in Windows; then refresh or scan again. These actions are separate from Fix it all.");

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
    public ObservableCollection<PackageUpdate> Updates { get; } = [];
    public bool ShowUpdateAction => technicianMode;
    public string TextCheckUpdates => T("Check program updates");
    public string TextUpdateSelected => T("Update selected program");
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
        RefreshAll();
    }

    private async Task CheckUpdatesAsync()
    {
        checkingUpdates = true;
        Updates.Clear();
        SelectedUpdate = null;
        SetUpdateStatus(T("Checking program updates…"));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var updates = await PackageUpdateSource.ReadAsync(timeout.Token);
            foreach (var update in updates)
            {
                Updates.Add(update);
            }

            SetUpdateStatus(T("Select a program to review its update."));
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
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
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

        setRepairBusy(true);
        SetUpdateStatus(T("Updating the selected program. Follow any installer prompts."));
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
        finally
        {
            setRepairBusy(false);
        }

        RepairAudit.Append(result);
        Updates.Remove(selected);
        SelectedUpdate = null;
        SetUpdateStatus(result.Verdict == RepairVerdict.Fixed
            ? T("The program was updated and its installed version was verified.")
            : T("The update could not be verified. Check the program and refresh the update list before trying again."));
        await LoadAsync();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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
        var query = search.Trim();
        var culture = CultureInfo.GetCultureInfo(catalog().Language.Code);
        foreach (var program in programs.Where(program => program.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || program.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            Items.Add(new SoftwareItem(program.Name, program.Publisher, program.Version,
                program.SizeBytes is long bytes ? (bytes / 1048576d).ToString("N1", culture) + " MB" : "—",
                program.InstalledOn?.ToString("d", culture) ?? "—"));
        }

        OnPropertyChanged(nameof(Status));
    }

    private string T(string text) => catalog().Translate(text);
}

public sealed record SoftwareItem(string Name, string Publisher, string Version, string Size, string Date);

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Windows;
using ShivaAssist.Windows.Repairs;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.App.ViewModels;

public sealed class StartupViewModel : Observable
{
    private readonly Func<TextCatalog> catalog;
    private readonly bool technicianMode;
    private readonly Func<bool> busy;
    private readonly Action<bool> setBusy;
    private bool loading;
    private bool loaded;
    private string status = string.Empty;
    private StartupDashboardData? data;

    public StartupViewModel(Func<TextCatalog> catalog, bool technicianMode, Func<bool> busy, Action<bool> setBusy)
    {
        this.catalog = catalog;
        this.technicianMode = technicianMode;
        this.busy = busy;
        this.setBusy = setBusy;
        RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !loading && !busy());
        DisableCommand = new RelayCommand(async row => await DisableAsync((StartupRow)row!),
            row => technicianMode && row is StartupRow { Application.Entry.Enabled: true } && !loading && !busy());
    }

    public ObservableCollection<StartupRow> Items { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand DisableCommand { get; }
    public string Status => status;
    public string TextTitle => T("Startup apps and recorded timings");
    public string TextRefresh => T("Refresh startup apps");
    public string TextDisable => T("Turn off startup");
    public string TextScope => T("Bars compare the latest application startup durations reported by Windows in the last 14 days. They are not predicted time savings, and should not be added together. Some apps have no timing report. Turning off startup keeps the app installed and does not stop its services or currently running processes. Undo is available in History.");
    public string TextBoot => data?.Boot is { } boot
        ? new Message("Latest Windows boot report: {seconds} s · {when}", ("seconds", (boot.DurationMs / 1000d).ToString("N2", Culture)),
            ("when", boot.ObservedAt.ToLocalTime().ToString("g", Culture))).Render(catalog())
        : T("Windows has not provided a recent boot-duration report.");
    private CultureInfo Culture => CultureInfo.GetCultureInfo(catalog().Language.Code);

    public async Task EnsureLoadedAsync()
    {
        if (!loaded && !loading && !busy())
        {
            await LoadAsync();
        }
    }

    public void RefreshLanguage()
    {
        BuildRows();
        RefreshAll();
    }

    private async Task LoadAsync()
    {
        loading = true;
        loaded = false;
        data = null;
        Items.Clear();
        SetStatus(T("Reading startup entries and Windows timing reports…"));
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            data = await Task.Run(() => StartupDashboardSource.ReadAsync(timeout.Token));
            loaded = true;
            BuildRows();
            SetStatus(data.TimingAvailable
                ? new Message("{count} startup rows · missing measurements remain unknown", ("count", Items.Count)).Render(catalog())
                : T("Windows timing reports are unavailable. Startup registrations are shown without estimated timings."));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OperationCanceledException or System.Security.SecurityException)
        {
            SetStatus(T("Startup information could not be read. Try refreshing; administrator permission may be needed."));
        }
        finally
        {
            loading = false;
            RefreshAll();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void BuildRows()
    {
        Items.Clear();
        if (data is null)
        {
            return;
        }

        var maximum = Math.Max(1, data.Applications.MaxBy(a => a.Measurement?.DurationMs ?? 0)?.Measurement?.DurationMs ?? 1);
        foreach (var application in data.Applications)
        {
            Items.Add(new StartupRow(application, maximum, catalog, DisableCommand, technicianMode));
        }
    }

    private async Task DisableAsync(StartupRow row)
    {
        var entry = row.Application.Entry;
        if (!technicianMode || entry is not { Enabled: true } || busy() || loading)
        {
            return;
        }

        var prompt = new Message("Stop {name} from starting with Windows? It stays installed. You can reverse this change in History.",
            ("name", entry.Name)).Render(catalog());
        if (MessageBox.Show(prompt, TextDisable, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        setBusy(true);
        var job = new RepairJob
        {
            JobId = Guid.NewGuid().ToString("N"), OperationId = RepairCatalogue.DisableStartupPrograms,
            ApprovedAt = DateTimeOffset.UtcNow, Items = [entry.Reference],
        };
        RepairResult result;
        try
        {
            result = await Task.Run(() => new WindowsRepairExecutor(WindowsPlatform.IsElevated()).ExecuteAsync(job, CancellationToken.None));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Security.SecurityException)
        {
            result = new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false,
                Verdict = RepairVerdict.Inconclusive, Detail = ex.GetType().Name };
        }
        finally
        {
            setBusy(false);
        }

        RepairAudit.Append(result);
        await LoadAsync();
        SetStatus(result.Verdict == RepairVerdict.Fixed
            ? T("Startup was turned off and verified. Use History to undo. Refresh the main PC scan to update its findings.")
            : T("The startup change could not be verified. Review History and refresh before trying again."));
    }

    private void SetStatus(string message) { status = message; OnPropertyChanged(nameof(Status)); }
    private string T(string text) => catalog().Translate(text);
}

public sealed class StartupRow(StartupApplication application, long maximum, Func<TextCatalog> catalog, RelayCommand disable, bool technicianMode)
{
    public StartupApplication Application { get; } = application;
    public string Name => Application.Name;
    public long Duration => Application.Measurement?.DurationMs ?? 0;
    public long Maximum => maximum;
    public bool HasTiming => Application.Measurement is not null;
    public bool ShowAction => technicianMode && Application.Entry is not null;
    public RelayCommand DisableCommand => disable;
    public string Glyph => Application.Entry is null ? "\uE9D9" : "\uE768";
    public Brush BarColour => HasTiming ? Palette.For(Core.Findings.Severity.Information) : Palette.Muted;
    public string State => catalog().Translate(Application.Entry is null ? "Windows report only"
        : Application.Entry.Enabled ? "On at startup" : "Off at startup");
    public string Timing => Application.Measurement is { } timing
        ? new Message("Last reported: {seconds} s · {when}", ("seconds", (timing.DurationMs / 1000d).ToString("N2", CultureInfo.GetCultureInfo(catalog().Language.Code))),
            ("when", timing.ObservedAt.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(catalog().Language.Code)))).Render(catalog())
        : catalog().Translate("Not measured by Windows");
}

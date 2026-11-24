using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ShivaAssist.Core.Cleanup;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Windows;
using ShivaAssist.Windows.Repairs;

namespace ShivaAssist.App.ViewModels;

/// <summary>
/// Cleanup with a preview (roadmap step 3): measure every area first, show what it would free, let the customer
/// untick areas, then run the selected catalogue cleanups one by one and measure again.
/// Also provides safe on-demand quick Windows fixes (Explorer restart, icon cache rebuild, Store cache reset).
/// </summary>
public sealed class CleanupViewModel : Observable
{
    private readonly Func<TextCatalog> catalog;
    private readonly Func<bool> busy;
    private readonly Action<bool> setBusy;
    private bool working;
    private bool loaded;
    private string status = string.Empty;
    private string quickFixStatus = string.Empty;

    public CleanupViewModel(Func<TextCatalog> catalog, bool technicianMode, Func<bool> busy, Action<bool> setBusy)
    {
        this.catalog = catalog;
        this.busy = busy;
        this.setBusy = setBusy;
        TechnicianMode = technicianMode;
        MeasureCommand = new RelayCommand(async _ => await MeasureAsync(), _ => !working && !busy());
        CleanCommand = new RelayCommand(async _ => await CleanAsync(),
            _ => technicianMode && !working && !busy() && Items.Any(i => i.IsChecked && i.Bytes > 0));
        RunQuickFixCommand = new RelayCommand(async item => await RunQuickFixAsync(item as QuickFixItem),
            item => technicianMode && item is QuickFixItem && !working && !busy());

        AddQuickFix(RepairCatalogue.RestartExplorer, "\uE72C");
        AddQuickFix(RepairCatalogue.RebuildIconCache, "\uE8A5");
        AddQuickFix(RepairCatalogue.ResetStoreCache, "\uE719");
    }

    public bool TechnicianMode { get; }

    public ObservableCollection<CleanupItem> Items { get; } = [];

    public ObservableCollection<QuickFixItem> QuickFixes { get; } = [];

    public RelayCommand MeasureCommand { get; }

    public RelayCommand CleanCommand { get; }

    public RelayCommand RunQuickFixCommand { get; }

    public bool Working => working;

    public string Status => status;

    public string QuickFixStatus => quickFixStatus;

    public string TextTitle => T("Free up space");

    public string TextScope => T("Shiva Assist first measures what can be removed safely. Untick anything you want to keep, then clean. Your documents, photos and downloads are never touched.");

    public string TextMeasure => T("Measure again");

    public string TextQuickFixesTitle => T("Quick Windows fixes");

    public string TextQuickFixesScope => T("Safe one-click fixes for a frozen taskbar, blank desktop icons, or a stuck Microsoft Store. Your files and settings stay untouched.");

    public string TextRunFix => T("Run fix");

    public string TextClean => SelectedBytes > 0
        ? new Message("Clean selected ({size:gb})", ("size", SelectedBytes)).Render(catalog())
        : T("Nothing selected to clean");

    public string TextTotal => new Message("Can be freed: {size:gb}", ("size", Items.Sum(i => i.Bytes))).Render(catalog());

    private long SelectedBytes => Items.Where(i => i.IsChecked).Sum(i => i.Bytes);

    public async Task EnsureLoadedAsync()
    {
        if (!loaded && !working && !busy())
        {
            await MeasureAsync();
        }
    }

    public void RefreshLanguage()
    {
        foreach (var item in Items)
        {
            item.Refresh();
        }

        foreach (var fix in QuickFixes)
        {
            fix.Refresh();
        }

        RefreshAll();
    }

    private void AddQuickFix(string operationId, string glyph)
    {
        var operation = RepairCatalogue.Find(operationId);
        if (operation is not null)
        {
            QuickFixes.Add(new QuickFixItem(operation, glyph, catalog));
        }
    }

    private async Task RunQuickFixAsync(QuickFixItem? item)
    {
        if (item is null || !TechnicianMode || working || busy())
        {
            return;
        }

        var question = item.Description + Environment.NewLine + Environment.NewLine + T("Do you want to run this fix now?");
        if (MessageBox.Show(Application.Current.MainWindow, question, item.Name,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        quickFixStatus = new Message("Running: {fix}…", ("fix", item.Name)).Render(catalog());
        SetWorking(true, null);
        try
        {
            var job = new RepairJob
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = item.Operation.Id,
                ApprovedAt = DateTimeOffset.UtcNow,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var result = await Task.Run(() => new WindowsRepairExecutor(WindowsPlatform.IsElevated()).ExecuteAsync(job, timeout.Token));
            RepairAudit.Append(result with { Verdict = result.Completed ? RepairVerdict.Fixed : RepairVerdict.Inconclusive });
            quickFixStatus = result.Completed
                ? new Message("Done: {fix}.", ("fix", item.Name)).Render(catalog())
                : new Message("{fix} could not finish. See History for details.", ("fix", item.Name)).Render(catalog());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or PlatformNotSupportedException or Win32Exception or OperationCanceledException)
        {
            quickFixStatus = new Message("{fix} could not finish. See History for details.", ("fix", item.Name)).Render(catalog());
        }
        finally
        {
            SetWorking(false, null);
        }
    }

    private async Task MeasureAsync()
    {
        SetWorking(true, T("Measuring what can be cleaned…"));
        try
        {
            var estimates = await Task.Run(() => CleanupMeasurement.MeasureAsync(CancellationToken.None));
            Items.Clear();
            foreach (var area in CleanupAreas.All)
            {
                var estimate = estimates.FirstOrDefault(e => e.OperationId == area.OperationId) ?? new CleanupEstimate(area.OperationId, null, 0);
                var item = new CleanupItem(area, estimate, catalog);
                item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TextClean));
                Items.Add(item);
            }

            loaded = true;
            status = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status = T("The cleanup could not be measured. Try again in a moment.");
        }
        finally
        {
            SetWorking(false, null);
        }
    }

    private async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsChecked && i.Bytes > 0).ToList();
        long freed = 0;
        var failed = 0;
        SetWorking(true, T("Cleaning…"));
        try
        {
            foreach (var item in selected)
            {
                status = new Message("Cleaning: {area}…", ("area", item.Name)).Render(catalog());
                OnPropertyChanged(nameof(Status));
                var job = new RepairJob
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    OperationId = item.Area.OperationId,
                    ApprovedAt = DateTimeOffset.UtcNow,
                    Items = item.Area.OperationId == RepairCatalogue.CleanTempFiles ? [Path.GetTempPath().TrimEnd('\\')] : [],
                };
                try
                {
                    var result = await Task.Run(() => new WindowsRepairExecutor(isElevated: true).ExecuteAsync(job, CancellationToken.None));
                    RepairAudit.Append(result with { Verdict = !result.Completed ? RepairVerdict.Inconclusive : result.BytesFreed > 0 ? RepairVerdict.Fixed : RepairVerdict.Unresolved });
                    freed += result.BytesFreed;
                    failed += result.Completed ? 0 : 1;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException)
                {
                    failed++;
                    RepairAudit.Append(new RepairResult { JobId = job.JobId, OperationId = job.OperationId, Completed = false, Detail = ex.Message, Verdict = RepairVerdict.Inconclusive });
                }
            }
        }
        finally
        {
            SetWorking(false, null);
        }

        await MeasureAsync();
        status = failed == 0
            ? new Message("Done. Space recovered: {freed:gb}.", ("freed", freed)).Render(catalog())
            : new Message("Space recovered: {freed:gb}. Some areas could not be cleaned completely; see History.", ("freed", freed)).Render(catalog());
        RefreshAll();
    }

    private void SetWorking(bool value, string? message)
    {
        working = value;
        if (message is not null)
        {
            status = message;
        }

        setBusy(value);
        RefreshAll();
        CommandManager.InvalidateRequerySuggested();
    }

    private string T(string template) => new Message(template).Render(catalog());
}

/// <summary>One measured cleanup area with its checkbox.</summary>
public sealed class CleanupItem(CleanupArea area, CleanupEstimate estimate, Func<TextCatalog> catalog) : Observable
{
    private bool isChecked = estimate.Bytes > 0;

    public CleanupArea Area { get; } = area;

    public long Bytes => estimate.Bytes ?? 0;

    public bool Measured => estimate.Bytes is not null;

    public bool CanClean => Bytes > 0;

    public string Name => Area.Name.Render(catalog());

    public string Description => Area.Description.Render(catalog());

    public string Glyph => Area.Glyph;

    public string Size => !Measured ? new Message("Could not be measured").Render(catalog())
        : Bytes == 0 ? new Message("Already clean").Render(catalog())
        : new Message("{size:gb}", ("size", Bytes)).Render(catalog());

    public bool IsChecked
    {
        get => isChecked;
        set => Set(ref isChecked, value && CanClean);
    }

    public void Refresh() => RefreshAll();
}

/// <summary>One safe on-demand Windows quick fix backed by a catalogue operation.</summary>
public sealed class QuickFixItem(RepairOperation operation, string glyph, Func<TextCatalog> catalog) : Observable
{
    public RepairOperation Operation { get; } = operation;

    public string Name => Operation.Name.Render(catalog());

    public string Description => Operation.Description.Render(catalog());

    public string Glyph { get; } = glyph;

    public void Refresh() => RefreshAll();
}

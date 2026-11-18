using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Reporting;
using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Scanning;
using ShivaAssist.Windows;
using ShivaAssist.Windows.Repairs;
using ShivaAssist.Windows.Sources;

namespace ShivaAssist.App.ViewModels;

public enum ScreenState
{
    Ready,
    Scanning,
    Results,
}

/// <summary>
/// The dashboard (architecture 5.1): start a read-only scan, see categories at a glance, read each finding
/// with its advice, and save the report. Every visible text goes through the translation catalog.
/// </summary>
public sealed partial class MainViewModel : Observable
{
    public const string HelpUrl = "https://shivacomputers.nl/{lang}/contact";

    private readonly string product;
    private TextCatalog catalog;
    private ScreenState state = ScreenState.Ready;
    private int progress;
    private int progressMax = 1;
    private bool showHealthy;
    private CategoryTile? selectedTile;
    private NavItem selectedModule;
    private ScanReport? report;
    private string? status;
    private bool repairing;
    private bool reviewingRepairs;
    private readonly Dictionary<string, RepairResult> outcomes = new(StringComparer.Ordinal);
    private readonly RepairContext? repairContext;

    public MainViewModel(string product, bool technicianMode = false)
    {
        this.product = product;
        TechnicianMode = technicianMode;
        repairContext = technicianMode ? new RepairContext(RepairAsync, OutcomeText, OutcomeColour, () => repairing || reviewingRepairs || State == ScreenState.Scanning) : null;
        Languages = TextCatalog.Available();
        catalog = TextCatalog.For(Settings.LoadLanguage() ?? DefaultLanguage());
        Software = new SoftwareViewModel(() => catalog, technicianMode,
            () => repairing || reviewingRepairs || State == ScreenState.Scanning, value => repairing = value);
        Startup = new StartupViewModel(() => catalog, technicianMode,
            () => repairing || reviewingRepairs || State == ScreenState.Scanning,
            value => { repairing = value; RefreshAll(); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); });
        PropertyChanged += (_, _) => Software.RefreshBusyState();
        IsElevated = WindowsPlatform.IsElevated();
        foreach (var module in AppModules.All)
        {
            var item = new NavItem(module, () => catalog);
            Modules.Add(item);
        }

        selectedModule = Modules[0];
        selectedModule.Selected = true;
        NavigateCommand = new RelayCommand(async item =>
        {
            Navigate(item as NavItem);
            if (IsSoftwarePage)
            {
                await Software.EnsureLoadedAsync();
            }
            if (IsStartupSection)
            {
                await Startup.EnsureLoadedAsync();
            }
        });

        foreach (var category in WindowsPlatform.CreateChecks().Select(c => c.Category).Distinct().Order())
        {
            var area = new ScanArea(category, () => catalog);
            area.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AllAreasSelected));
            ScanAreas.Add(area);
        }

        StartScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => State != ScreenState.Scanning && !repairing && !reviewingRepairs);
        CleanTemporaryFilesCommand = new RelayCommand(async _ => await CleanTemporaryFilesAsync(),
            _ => TechnicianMode && !repairing && !reviewingRepairs && State != ScreenState.Scanning);
        FixAllCommand = new RelayCommand(async _ => await FixAllAsync(),
            _ => TechnicianMode && HasResults && AutomaticRepairs.Any() && !repairing && !reviewingRepairs && State != ScreenState.Scanning);
        ConfigureScanCommand = new RelayCommand(_ => Navigate(Modules.First(module => module.IsOverview)));
        SaveReportCommand = new RelayCommand(_ => SaveReport(), _ => report is not null);
        HelpCommand = new RelayCommand(_ => Open(HelpUrl.Replace("{lang}", catalog.Language.Code == "nl" ? "nl" : "en", StringComparison.Ordinal)));
        RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdmin(TechnicianMode),
            _ => !IsElevated && State != ScreenState.Scanning && !repairing && !reviewingRepairs);
        SelectTileCommand = new RelayCommand(tile => SelectTile(tile as CategoryTile));
        SectionCommand = new RelayCommand(async section => await SelectSectionAsync(section as WorkspaceSection));
        RebuildSections();
    }

    public string Product => product;

    public IReadOnlyList<Language> Languages { get; }

    public Language SelectedLanguage
    {
        get => catalog.Language;
        set
        {
            if (value is null || value.Code == catalog.Language.Code)
            {
                return;
            }

            catalog = TextCatalog.For(value.Code);
            Settings.SaveLanguage(value.Code);
            foreach (var tile in Tiles)
            {
                tile.Refresh();
            }

            foreach (var area in ScanAreas)
            {
                area.Refresh();
            }

            foreach (var module in Modules)
            {
                module.Refresh();
            }

            foreach (var item in AllFindings)
            {
                item.Refresh();
            }

            RefreshAll();
            Software.RefreshLanguage();
            Startup.RefreshLanguage();
            RebuildSections();
            ApplyFilter();
        }
    }

    public bool IsElevated { get; }

    public bool ShowAdminHint => !IsElevated;

    /// <summary>Repair actions are available unless the desktop app was opened in read-only mode.</summary>
    public bool TechnicianMode { get; }

    public bool ShowReadOnlyBadge => !TechnicianMode;

    public string TextTechnicianMode => T("Technician mode · repairs enabled");

    public ScreenState State
    {
        get => state;
        private set
        {
            if (Set(ref state, value))
            {
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public bool IsReady => State != ScreenState.Scanning;

    public bool IsScanning => State == ScreenState.Scanning;

    public bool HasResults => State == ScreenState.Results;

    public int Progress
    {
        get => progress;
        private set => Set(ref progress, value);
    }

    public int ProgressMax
    {
        get => progressMax;
        private set => Set(ref progressMax, value);
    }

    public string? Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public ObservableCollection<CategoryTile> Tiles { get; } = [];

    /// <summary>The menu: overview first, then every module (blueprint: twelve modules).</summary>
    public ObservableCollection<NavItem> Modules { get; } = [];

    /// <summary>Overview tiles: every module except the overview itself.</summary>

    public RelayCommand NavigateCommand { get; }

    public NavItem SelectedModule => selectedModule;

    public bool IsOverviewPage => selectedModule.IsOverview;

    public bool IsResultsPage => selectedModule.Module.Key == AppModules.ResultsKey;

    public bool ResultsEmpty => IsResultsPage && !HasResults && !IsStartupSection;

    public string TextResultsEmpty => T("Start a check-up from Overview to see your results here.");

    public string TextScanAgain => T("Scan again");

    public RelayCommand ConfigureScanCommand { get; }

    public RelayCommand CleanTemporaryFilesCommand { get; }
    public string TextCleanTemporaryFiles => T("Clean old temporary files");
    public string TextTemporaryCleanupScope => T("Remove temporary files older than 7 days. Files in use are skipped.");
    public string? TemporaryCleanupStatus { get; private set; }

    private async Task CleanTemporaryFilesAsync()
    {
        repairing = true;
        TemporaryCleanupStatus = T("Cleaning old temporary files…");
        RefreshAll();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        try
        {
            var job = new RepairJob
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = RepairCatalogue.CleanTempFiles,
                ApprovedAt = DateTimeOffset.UtcNow,
                Items = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")],
            };
            var result = await Task.Run(() => new WindowsRepairExecutor(IsElevated).ExecuteAsync(job, CancellationToken.None));
            RepairAudit.Append(result);
            TemporaryCleanupStatus = !result.Completed
                ? T("Temporary file cleanup could not finish.")
                : result.BytesFreed > 0
                    ? new Message("Temporary files cleaned. Space recovered: {freed:gb}.", ("freed", result.BytesFreed)).Render(catalog)
                    : T("Cleanup finished. No disk space was recovered.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            TemporaryCleanupStatus = T("Temporary file cleanup could not finish.");
        }
        finally
        {
            repairing = false;
            RefreshAll();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public RelayCommand FixAllCommand { get; }

    public bool ShowRepairReview => HasResults;

    public string TextFixAll => T("Fix it all");
    public string TextRepairAndCleanup => T("Repair and cleanup");

    public string TextProblemCount => new Message("{count} problems found", ("count", CountOf(Severity.Critical) + CountOf(Severity.Warning))).Render(catalog);

    public bool RepairsEmpty => IsModulePage && HasResults && Findings.Count == 0;

    private IEnumerable<RepairButton> AutomaticRepairs => AllFindings.SelectMany(item => item.Repairs)
        .Where(repair => !repair.Operation.NeedsSelection && !repair.Operation.RestartRequired && !repair.Operation.IsUndo);

    public string TextRepairCoverage => !AutomaticRepairs.Any()
        ? T("No one-click repairs are available. Open a finding to see its options.")
        : AutomaticRepairs.Any(repair => repair.Operation.Id == RepairCatalogue.EmptyRecycleBin)
            ? T("Fix it all starts supported repairs now and may permanently empty the Recycle Bin.")
            : T("Fix it all starts supported repairs now. Actions that need a choice or restart are available on their findings.");

    public bool IsModulePage => selectedModule.Module.Key == AppModules.RepairsKey;

    public bool IsSoftwarePage => selectedModule.Module.Key == "software";

    public SoftwareViewModel Software { get; }

    public bool IsHistoryPage => selectedModule.Module.Key == AppModules.HistoryKey;

    /// <summary>Every repair and undo from the audit log, newest first.</summary>
    public ObservableCollection<HistoryItem> History { get; } = [];

    public bool HistoryEmpty => IsHistoryPage && !IsHelpSection && History.Count == 0;

    public string TextHistoryEmpty => IsUndoSection
        ? T("No repairs currently have an available undo. All repair records remain in Repair history.")
        : T("No repairs yet. Every repair you run appears here with its result, so you can always see what was changed.");

    public string TextUndo => T("Undo");

    public string TextOpenSystemRestore => T("Open System Restore");

    public string TextRestorePointHint => T("A restore point was made before this repair. Windows System Restore can take the PC back to that moment.");

    public bool IsSoonPage => selectedModule.IsSoon;

    public bool ModuleNeedsScan => IsModulePage && State != ScreenState.Results;

    public bool ModuleHasResults => IsModulePage && State == ScreenState.Results;

    public string TextModuleNeedsScan => T("Start the check-up to see the results for this area.");

    public string TextSoon => T("This part of Shiva Assist is being built and comes in a later version. Until then, a Shiva Computers technician can help: use the button at the bottom right.");

    /// <summary>The areas the customer can include in the check-up; all are on by default.</summary>
    public ObservableCollection<ScanArea> ScanAreas { get; } = [];

    public bool AllAreasSelected
    {
        get => ScanAreas.All(a => a.IsChecked);
        set
        {
            foreach (var area in ScanAreas)
            {
                area.IsChecked = value;
            }
        }
    }

    public string TextAreasTitle => T("What the check-up covers");

    public string TextSelectAll => T("Select all");

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public bool ShowHealthy
    {
        get => showHealthy;
        set
        {
            if (Set(ref showHealthy, value))
            {
                ApplyFilter();
            }
        }
    }

    public RelayCommand StartScanCommand { get; }

    public RelayCommand SaveReportCommand { get; }

    public RelayCommand HelpCommand { get; }

    public RelayCommand RestartAsAdminCommand { get; }

    public RelayCommand SelectTileCommand { get; }

    // ---- Texts (re-rendered on a language switch) ----

    public string TextTagline => TechnicianMode ? T("PC health check and repair by Shiva Computers") : T("Read-only PC health check by Shiva Computers");

    public string TextLanguage => T("Language");

    public string TextIntroTitle => T("Let's give your PC a quick check-up");

    public string TextIntro => TechnicianMode
        ? T("Check your PC for problems. The scan makes no changes. Afterward, use Fix it all to run supported repairs.")
        : T("In about a minute, Shiva Assist looks at how your PC is doing (storage, updates, security, internet and more) and explains what it finds in plain language, with clear next steps. It only reads information: nothing on your PC is changed.");

    public string TextStart => T("Start the check-up");

    public string TextDuration => T("It takes about a minute.");

    public string TextAdminHint => T("Some checks need administrator rights.");

    public string TextRestartAsAdmin => T("Restart as administrator");

    public string TextScanning => T("Checking your PC…");

    public string TextProgress => new Message("{done} of {total} checks done", ("done", Progress), ("total", ProgressMax)).Render(catalog);

    public string TextShowHealthy => T("Also show what is OK");

    public string TextAllCategories => T("All categories");

    public string TextDetails => T("Details");

    public string TextSave => T("Save as PDF…");

    public string TextHelp => T("Need help? Contact Shiva Computers");

    public string TextHeadline => report is null ? string.Empty : (CountOf(Severity.Critical) + CountOf(Severity.Warning)) switch
    {
        0 when CountOf(Severity.Unknown) > 0 => T("Some checks could not be completed"),
        0 when CountOf(Severity.Information) > 0 => T("No warnings found; review the information below"),
        0 => T("Good news: your PC looks healthy"),
        1 => T("1 thing needs your attention"),
        var n => new Message("{count} things need your attention", ("count", n)).Render(catalog),
    };

    public string TextSummary => report is null
        ? string.Empty
        : new Message(
            "Critical {critical} · Warnings {warning} · Info {info} · OK {ok} · Unknown {unknown} · {seconds} s",
            ("critical", CountOf(Severity.Critical)),
            ("warning", CountOf(Severity.Warning)),
            ("info", CountOf(Severity.Information)),
            ("ok", CountOf(Severity.Healthy)),
            ("unknown", CountOf(Severity.Unknown)),
            ("seconds", Math.Round(report.DurationSeconds, 1))).Render(catalog);

    public string TextSystem => report is null ? string.Empty : $"{report.System.OsName} {report.System.OsVersion}";

    public string TextEvidence => T("Evidence (technical, in English)");

    public string TextReadOnlyBadge => T("Read-only · changes nothing");

    private List<FindingItem> AllFindings { get; } = [];

    private string T(string template) => new Message(template).Render(catalog);

    private int CountOf(Severity severity) => report?.Findings.Count(f => f.Severity == severity) ?? 0;

    private static string DefaultLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl" ? "nl" : TextCatalog.DefaultLanguage;

    private async Task ScanAsync()
    {
        var areas = ScanAreas.Where(a => a.IsChecked).Select(a => a.Category).ToHashSet();
        var checks = WindowsPlatform.CreateChecks().Where(c => areas.Contains(c.Category)).ToList();
        if (checks.Count == 0)
        {
            Status = T("Select at least one area to check.");
            return;
        }

        Navigate(Modules[0]);
        ProgressMax = checks.Count;
        Progress = 0;
        Status = null;
        State = ScreenState.Scanning;
        OnPropertyChanged(nameof(TextProgress));

        var runner = new ScanRunner(checks);
        var dispatcher = Application.Current.Dispatcher;
        runner.CheckCompleted += _ => dispatcher.Invoke(() =>
        {
            Progress++;
            OnPropertyChanged(nameof(TextProgress));
        });

        report = await Task.Run(() => runner.RunAsync(WindowsPlatform.DescribeEnvironment(product)));
        ShowResults();
        Navigate(Modules.First(module => module.Module.Key == AppModules.ResultsKey));
    }

    private void ShowResults()
    {
        if (report is null)
        {
            return;
        }

        AllFindings.Clear();
        AllFindings.AddRange(report.Findings.Select(f => new FindingItem(f, () => catalog, repairContext)));

        Tiles.Clear();
        foreach (var group in report.Findings.GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            Tiles.Add(new CategoryTile(group.Key, [.. group], () => catalog));
        }

        foreach (var module in Modules.Where(m => m.Module.Key is AppModules.ResultsKey or AppModules.RepairsKey))
        {
            module.Update([.. report.Findings.Where(f => module.Module.Key == AppModules.ResultsKey || RepairCatalogue.For(f).Count > 0)]);
        }

        selectedTile = null;
        State = ScreenState.Results;
        RebuildSections();
        ApplyFilter();
        RefreshAll();
    }

    private void SelectTile(CategoryTile? tile)
    {
        selectedTile = tile is null || tile == selectedTile ? null : tile;
        foreach (var t in Tiles)
        {
            t.Selected = t == selectedTile;
        }

        ApplyFilter();
    }

    private void Navigate(NavItem? item)
    {
        if (item is null || item == selectedModule)
        {
            return;
        }

        selectedModule.Selected = false;
        selectedModule = item;
        item.Selected = true;
        RebuildSections();
        if (IsHistoryPage)
        {
            LoadHistory();
        }

        ApplyFilter();
        RefreshAll();
    }

    /// <summary>Overview: problems (and, on request, what is OK). Module page: everything the module found.</summary>
    private void ApplyFilter()
    {
        Findings.Clear();
        foreach (var item in AllFindings.Where(f => MatchesSection(f.Finding) && (IsModulePage || IsRepairSection
            ? f.Repairs.Count > 0
            : (ShowHealthy || f.Severity != Severity.Healthy) && (selectedTile is null || f.Category == selectedTile.Category))))
        {
            Findings.Add(item);
        }
        FindingGroups.Clear();
        foreach (var group in Findings.GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            FindingGroups.Add(new(ReportText.CategoryName(group.Key).Render(catalog), group.ToArray()));
        }
        OnPropertyChanged(nameof(SectionHasNoFindings));
        OnPropertyChanged(nameof(RepairsEmpty));
    }

    private void SaveReport()
    {
        if (report is null)
        {
            return;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Shiva Assist", "Reports");
        Directory.CreateDirectory(folder);
        var dialog = new SaveFileDialog
        {
            InitialDirectory = folder,
            FileName = $"Shiva Assist {report.StartedAt.ToLocalTime():yyyy-MM-dd HHmm}",
            Filter = T("PDF report") + " (*.pdf)|*.pdf|" + T("Technical report") + " (*.json)|*.json",
            DefaultExt = ".pdf",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                if (dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(dialog.FileName, ReportJson.Serialize(report));
                }
                else
                {
                    File.WriteAllBytes(dialog.FileName, PdfReport.Render(report, catalog));
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }

                Status = new Message("Report saved: {file}", ("file", dialog.FileName)).Render(catalog);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                Status = new Message("The report could not be saved: {reason}", ("reason", ex.Message)).Render(catalog);
            }
        }
    }

    // ---- Repairs (stage 3a): explain → ask → repair → verify → log ----

    private IReadOnlyList<(RepairOperation Operation, RepairResult? Result)> lastRepairBatch = [];

    public bool HasRepairSummary => lastRepairBatch.Count > 0;
    public string TextScanResultsTitle => CountOf(Severity.Critical) + CountOf(Severity.Warning) == 0
        ? TextHeadline : T("Scan results");
    public string TextRepairSummary => T("Last repair summary");
    public IEnumerable<string> RepairSummaryDetails => lastRepairBatch.Select(entry =>
        entry.Operation.Name.Render(catalog) + " — " + (entry.Result is { } result ? VerdictText(result) : T("The repair did not complete")));

    public IReadOnlyList<RepairSummaryBar> RepairSummaryBars
    {
        get
        {
            var fixedCount = lastRepairBatch.Count(entry => entry.Result is { Completed: true, Verdict: RepairVerdict.Fixed });
            var improved = lastRepairBatch.Count(entry => entry.Result is { Completed: true, Verdict: RepairVerdict.Improved });
            var remaining = lastRepairBatch.Count - fixedCount - improved;
            return
            [
                new(T("Fixed"), fixedCount, lastRepairBatch.Count, Palette.For(Severity.Healthy)),
                new(T("Improved"), improved, lastRepairBatch.Count, Palette.For(Severity.Information)),
                new(T("Needs attention or verification"), remaining, lastRepairBatch.Count, Palette.For(Severity.Warning)),
            ];
        }
    }

    private async Task FixAllAsync()
    {
        if (repairing || reviewingRepairs || !TechnicianMode)
        {
            return;
        }

        var batch = AutomaticRepairs.ToArray();
        var completed = new List<(RepairOperation Operation, RepairResult? Result)>();
        reviewingRepairs = true;
        Status = T("Working on your PC…");
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        try
        {
            foreach (var selected in batch)
            {
                // Earlier repairs can resolve a finding, so recheck that each operation still applies.
                var current = AllFindings.FirstOrDefault(item => item.Id == selected.Item.Id);
                var repair = current?.Repairs.FirstOrDefault(button => button.Operation.Id == selected.Operation.Id);
                if (repair is not null)
                {
                    IReadOnlyList<string> approvedItems = repair.Operation.Id == RepairCatalogue.CleanTempFiles
                        ? [Path.GetTempPath().TrimEnd('\\')]
                        : [];
                    completed.Add((repair.Operation, await RepairAsync(repair, approvedItems)));
                }
            }
            lastRepairBatch = completed.ToArray();
            Status = T("Repairs finished. See the summary in Overview.");
            RefreshAll();
        }
        finally
        {
            reviewingRepairs = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private Task RepairAsync(RepairButton button) => RepairAsync(button, null);

    private async Task<RepairResult?> RepairAsync(RepairButton button, IReadOnlyList<string>? approvedItems)
    {
        try
        {
            return await RepairCoreAsync(button, approvedItems);
        }
#pragma warning disable CA1031 // A failed repair must end in a message, never in a crashed window.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            repairing = false;
            Status = new Message("The repair did not complete: {reason}", ("reason", ex.Message)).Render(catalog);
            return null;
        }
    }

    private async Task<RepairResult?> RepairCoreAsync(RepairButton button, IReadOnlyList<string>? approvedItems)
    {
        var operation = button.Operation;
        var before = button.Item.Finding;
        var items = approvedItems ?? await ChooseItemsAsync(operation);
        if (items is null)
        {
            return null;
        }

        if (approvedItems is null)
        {
            var risk = operation.Risk switch { RiskClass.Low => T("Low"), RiskClass.Medium => T("Medium"), _ => T("High") };
            var question = string.Join(Environment.NewLine + Environment.NewLine,
                operation.Description.Render(catalog),
                operation.NeedsRestorePoint ? T("A restore point is made before the repair starts. If System Protection is off, it is switched on first.") : string.Empty,
                new Message("Risk: {risk} · Takes {min}–{max} minutes", ("risk", risk), ("min", operation.Minutes.Min), ("max", operation.Minutes.Max)).Render(catalog),
                T("Do you want to run this repair now?")).Replace(Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine, Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
            if (MessageBox.Show(question, operation.Name.Render(catalog), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return null;
            }
        }

        repairing = true;
        if (approvedItems is null)
        {
            Status = new Message("Running: {repair}… This can take up to {max} minutes; keep the PC switched on.", ("repair", operation.Name.Render(catalog)), ("max", operation.Minutes.Max)).Render(catalog);
        }
        var job = new RepairJob { JobId = Guid.NewGuid().ToString("N"), OperationId = operation.Id, ApprovedAt = DateTimeOffset.UtcNow, Items = items };
        var (result, after) = await Task.Run(() => new RepairRunner(IsElevated).RunAsync(operation, job, before, CancellationToken.None));
        repairing = false;

        outcomes[before.Id] = result;
        if (after is not null && report is not null)
        {
            report = report with { Findings = [.. report.Findings.Select(f => f.Id == after.Id ? ScanRunner.WithRepairs(after) : f)] };
        }

        if (approvedItems is null)
        {
            Status = OutcomeText(before.Id);
        }
        ShowResults();
        return result;
    }

    /// <summary>Items the repair needs: the temp folder, or the startup programs the technician selects.</summary>
    private async Task<IReadOnlyList<string>?> ChooseItemsAsync(RepairOperation operation)
    {
        if (operation.Id == RepairCatalogue.CleanTempFiles)
        {
            return [Path.GetTempPath().TrimEnd('\\')];
        }

        if (!operation.NeedsSelection)
        {
            return [];
        }

        var snapshot = await new StartupSource().ReadAsync(new ScanContext(IsElevated, DateTimeOffset.UtcNow), CancellationToken.None);
        var picker = new StartupPicker(snapshot.Entries, T("Switch off startup programs"), T("Select the programs that should no longer start with Windows. They stay installed."), T("Switch off"), T("Cancel"))
        {
            Owner = Application.Current.MainWindow,
        };
        return picker.ShowDialog() == true && picker.Selected.Count > 0 ? picker.Selected : null;
    }

    private string? OutcomeText(string findingId) => outcomes.TryGetValue(findingId, out var result) ? VerdictText(result) : null;

    private string VerdictText(RepairResult result)
    {
        var verdict = result.Verdict switch
        {
            RepairVerdict.Fixed => T("Fixed"),
            RepairVerdict.Unresolved => T("Repair done, but the problem is still there"),
            RepairVerdict.Improved => T("Improved"),
            RepairVerdict.Pending => T("Done. Windows confirms the result after a restart or within a few hours."),
            _ => T("Result unclear"),
        };
        if (!result.Completed)
        {
            verdict = string.IsNullOrWhiteSpace(result.Detail)
                ? T("The repair did not complete")
                : new Message("The repair did not complete: {reason}", ("reason", catalog.Translate(result.Detail))).Render(catalog);
        }

        if (result.Completed && result.Verdict == RepairVerdict.Improved && result.BytesFreed > 0)
        {
            return new Message("Improved · {freed:gb} freed. More space is needed: try the next cleanup.", ("freed", result.BytesFreed)).Render(catalog);
        }

        return result.BytesFreed > 0
            ? new Message("{verdict} · {freed:gb} freed", ("verdict", verdict), ("freed", result.BytesFreed)).Render(catalog)
            : verdict;
    }

    private static bool Succeeded(RepairResult r) => r.Completed && r.Verdict is RepairVerdict.Fixed or RepairVerdict.Improved or RepairVerdict.Pending;

    // ---- History and undo (roadmap step 2) ----

    private void LoadHistory()
    {
        History.Clear();
        IReadOnlyList<RepairResult> entries;
        try
        {
            entries = File.Exists(RepairAudit.LogPath) ? RepairHistory.Parse(File.ReadAllLines(RepairAudit.LogPath)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries = [];
            Status = new Message("The repair history could not be read: {reason}", ("reason", ex.Message)).Render(catalog);
        }

        var culture = System.Globalization.CultureInfo.GetCultureInfo(catalog.Language.Code);
        foreach (var entry in entries)
        {
            if (IsUndoSection && !RepairHistory.CanUndo(entry, entries))
            {
                continue;
            }
            var operation = RepairCatalogue.Find(entry.OperationId);
            var name = operation?.Name.Render(catalog) ?? entry.OperationId;
            var outcome = operation?.IsUndo == true
                ? (entry.Completed ? T("Undone: everything is back the way it was") : new Message("The undo did not complete: {reason}", ("reason", catalog.Translate(entry.Detail))).Render(catalog))
                : VerdictText(entry);
            if (RepairHistory.WasUndone(entry, entries))
            {
                outcome += " · " + T("undone later");
            }

            var item = entry;
            History.Add(new HistoryItem(
                entry,
                entry.FinishedAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm", culture),
                name,
                outcome,
                Succeeded(entry) ? Palette.For(Severity.Healthy) : Palette.For(Severity.Warning),
                RepairHistory.CanUndo(entry, entries),
                entry.Completed && operation?.NeedsRestorePoint == true,
                new RelayCommand(async _ => await UndoAsync(item), _ => !repairing),
                new RelayCommand(_ => OpenSystemRestore())));
        }

        OnPropertyChanged(nameof(HistoryEmpty));
    }

    private async Task UndoAsync(RepairResult original)
    {
        var undo = RepairCatalogue.UndoFor(original.OperationId);
        if (undo is null || MessageBox.Show(
                undo.Description.Render(catalog) + Environment.NewLine + Environment.NewLine + T("Do you want to undo this repair now?"),
                undo.Name.Render(catalog), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        repairing = true;
        try
        {
            var result = await Task.Run(() => new RepairRunner(IsElevated).UndoAsync(original, CancellationToken.None));
            Status = result.Completed
                ? T("Undone: everything is back the way it was")
                : new Message("The undo did not complete: {reason}", ("reason", catalog.Translate(result.Detail))).Render(catalog);
        }
        finally
        {
            repairing = false;
        }

        LoadHistory();
    }

    /// <summary>Windows' own System Restore: the way back for repairs that made a restore point.</summary>
    private void OpenSystemRestore()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "rstrui.exe")) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            Status = new Message("System Restore could not be opened: {reason}", ("reason", ex.Message)).Render(catalog);
        }
    }

    private SolidColorBrush OutcomeColour(string findingId) => outcomes.TryGetValue(findingId, out var r) && Succeeded(r)
        ? Palette.For(Severity.Healthy)
        : Palette.For(Severity.Warning);

    private static void RestartAsAdmin(bool technicianMode)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = technicianMode ? "--technician" : "--read-only" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // The user declined the Windows permission prompt: carry on without administrator rights.
        }
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// <summary>Remembers the chosen language between sessions (per user, in the registry).</summary>
internal static class Settings
{
    private const string Key = @"Software\Shiva Computers\Shiva Assist";

    public static string? LoadLanguage()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("Language") as string;
    }

    public static void SaveLanguage(string code)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        key.SetValue("Language", code);
    }
}

public sealed record RepairSummaryBar(string Label, int Count, int Total, Brush Color);

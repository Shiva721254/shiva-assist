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
public sealed class MainViewModel : Observable
{
    public const string HelpUrl = "https://shivacomputers.nl/{lang}/contact";

    private readonly string product;
    private TextCatalog catalog;
    private ScreenState state = ScreenState.Ready;
    private int progress;
    private int progressMax = 1;
    private bool showHealthy;
    private CategoryTile? selectedTile;
    private ScanReport? report;
    private string? status;
    private bool repairing;
    private readonly Dictionary<string, RepairResult> outcomes = new(StringComparer.Ordinal);
    private readonly RepairContext? repairContext;

    public MainViewModel(string product, bool technicianMode = false)
    {
        this.product = product;
        TechnicianMode = technicianMode;
        repairContext = technicianMode ? new RepairContext(RepairAsync, OutcomeText, OutcomeColour, () => repairing || State == ScreenState.Scanning) : null;
        Languages = TextCatalog.Available();
        catalog = TextCatalog.For(Settings.LoadLanguage() ?? DefaultLanguage());
        IsElevated = WindowsPlatform.IsElevated();

        StartScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => State != ScreenState.Scanning);
        SaveReportCommand = new RelayCommand(_ => SaveReport(), _ => report is not null);
        HelpCommand = new RelayCommand(_ => Open(HelpUrl.Replace("{lang}", catalog.Language.Code == "nl" ? "nl" : "en", StringComparison.Ordinal)));
        RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdmin(), _ => !IsElevated && State != ScreenState.Scanning);
        SelectTileCommand = new RelayCommand(tile => SelectTile(tile as CategoryTile));
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

            foreach (var item in AllFindings)
            {
                item.Refresh();
            }

            RefreshAll();
        }
    }

    public bool IsElevated { get; }

    public bool ShowAdminHint => !IsElevated;

    /// <summary>Repairs are offered only in technician mode (start with --technician) during stage 3.</summary>
    public bool TechnicianMode { get; }

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

    public bool IsReady => State == ScreenState.Ready;

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

    public string TextTagline => T("Read-only PC health check by Shiva Computers");

    public string TextLanguage => T("Language");

    public string TextIntroTitle => T("Let's give your PC a quick check-up");

    public string TextIntro => T("In about a minute, Shiva Assist looks at how your PC is doing (storage, updates, security, internet and more) and explains what it finds in plain language, with clear next steps. It only reads information: nothing on your PC is changed.");

    public string TextStart => State == ScreenState.Results ? T("Scan again") : T("Start the check-up");

    public string TextDuration => T("It takes about a minute.");

    public string TextAdminHint => T("Some checks need administrator rights.");

    public string TextRestartAsAdmin => T("Restart as administrator");

    public string TextScanning => T("Checking your PC…");

    public string TextProgress => new Message("{done} of {total} checks done", ("done", Progress), ("total", ProgressMax)).Render(catalog);

    public string TextShowHealthy => T("Also show what is OK");

    public string TextAllCategories => T("All categories");

    public string TextDetails => T("Details");

    public string TextSave => T("Save report…");

    public string TextHelp => T("Need help? Contact Shiva Computers");

    public string TextHeadline => report is null ? string.Empty : (CountOf(Severity.Critical) + CountOf(Severity.Warning)) switch
    {
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
        var checks = WindowsPlatform.CreateChecks();
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

        selectedTile = null;
        State = ScreenState.Results;
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

    private void ApplyFilter()
    {
        Findings.Clear();
        foreach (var item in AllFindings.Where(f =>
            (ShowHealthy || f.Severity != Severity.Healthy) && (selectedTile is null || f.Category == selectedTile.Category)))
        {
            Findings.Add(item);
        }
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
            FileName = $"scan-{report.StartedAt.ToLocalTime():yyyyMMdd-HHmmss}.json",
            Filter = "Shiva Assist report (*.json)|*.json",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, ReportJson.Serialize(report));
                Status = new Message("Report saved: {file}", ("file", dialog.FileName)).Render(catalog);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = new Message("The report could not be saved: {reason}", ("reason", ex.Message)).Render(catalog);
            }
        }
    }

    // ---- Repairs (stage 3a): explain → ask → repair → verify → log ----

    private async Task RepairAsync(RepairButton button)
    {
        try
        {
            await RepairCoreAsync(button);
        }
#pragma warning disable CA1031 // A failed repair must end in a message, never in a crashed window.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            repairing = false;
            Status = new Message("The repair did not complete: {reason}", ("reason", ex.Message)).Render(catalog);
        }
    }

    private async Task RepairCoreAsync(RepairButton button)
    {
        var operation = button.Operation;
        var before = button.Item.Finding;
        var items = await ChooseItemsAsync(operation);
        if (items is null)
        {
            return;
        }

        var risk = operation.Risk switch { RiskClass.Low => T("Low"), RiskClass.Medium => T("Medium"), _ => T("High") };
        var question = string.Join(Environment.NewLine + Environment.NewLine,
            operation.Description.Render(catalog),
            new Message("Risk: {risk} · Takes {min}–{max} minutes", ("risk", risk), ("min", operation.Minutes.Min), ("max", operation.Minutes.Max)).Render(catalog),
            T("Do you want to run this repair now?"));
        if (MessageBox.Show(question, operation.Name.Render(catalog), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        repairing = true;
        Status = new Message("Running: {repair}…", ("repair", operation.Name.Render(catalog))).Render(catalog);
        var job = new RepairJob { JobId = Guid.NewGuid().ToString("N"), OperationId = operation.Id, ApprovedAt = DateTimeOffset.UtcNow, Items = items };
        var (result, after) = await Task.Run(() => new RepairRunner(IsElevated).RunAsync(operation, job, before, CancellationToken.None));
        repairing = false;

        outcomes[before.Id] = result;
        if (after is not null && report is not null)
        {
            report = report with { Findings = [.. report.Findings.Select(f => f.Id == after.Id ? ScanRunner.WithRepairs(after) : f)] };
        }

        Status = OutcomeText(before.Id);
        ShowResults();
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

    private string? OutcomeText(string findingId)
    {
        if (!outcomes.TryGetValue(findingId, out var result))
        {
            return null;
        }

        var verdict = result.Verdict switch
        {
            RepairVerdict.Fixed => T("Fixed"),
            RepairVerdict.Unresolved => T("Repair done, but the problem is still there"),
            _ => T("Result unclear"),
        };
        if (!result.Completed)
        {
            verdict = T("The repair did not complete");
        }

        return result.BytesFreed > 0
            ? new Message("{verdict} · {freed:gb} freed", ("verdict", verdict), ("freed", result.BytesFreed)).Render(catalog)
            : verdict;
    }

    private Brush OutcomeColour(string findingId) => outcomes.TryGetValue(findingId, out var r) && r.Completed && r.Verdict == RepairVerdict.Fixed
        ? Palette.For(Severity.Healthy)
        : Palette.For(Severity.Warning);

    private static void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
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

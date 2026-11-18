using System.Collections.ObjectModel;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;

namespace ShivaAssist.App.ViewModels;

/// <summary>Main navigation selects a workspace; cards inside it select a focused section.</summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, string> sectionByModule = new(StringComparer.Ordinal);
    public ObservableCollection<WorkspaceSection> Sections { get; } = [];
    public ObservableCollection<FindingGroup> FindingGroups { get; } = [];
    public StartupViewModel Startup { get; }
    public RelayCommand SectionCommand { get; }
    public string ActiveSection => sectionByModule.GetValueOrDefault(selectedModule.Module.Key, "all");
    public bool ShowWorkspaceSections => !IsOverviewPage;
    public bool IsRepairSection => IsResultsPage && ActiveSection == "repairs";
    public bool IsStartupSection => IsResultsPage && ActiveSection == "startup";
    public bool ShowFindingResults => HasResults && !IsStartupSection;
    public bool IsHelpSection => IsHistoryPage && ActiveSection == "help";
    public bool IsUndoSection => IsHistoryPage && ActiveSection == "undo";
    public bool ShowHistory => IsHistoryPage && !IsHelpSection;
    public bool SectionHasNoFindings => IsResultsPage && HasResults && !IsStartupSection && Findings.Count == 0;
    public string TextSectionEmpty => IsRepairSection ? T("No supported repairs are available for this scan.") : T("No findings to show in this section. Show OK results or include this area in your next scan.");
    public string TextRepairSectionEmpty => T("No supported optimization actions match this section.");
    public string TextSectionHint => T("Choose a section");
    public string TextSupportScope => T("Contact Shiva Computers for help interpreting a result or arranging support. Remote control and emergency recovery are not yet integrated into this application.");

    private void RebuildSections()
    {
        Sections.Clear();
        void Add(string key, string title, string description, string glyph, string? target = null)
        {
            var status = string.Empty;
            if ((IsResultsPage || IsModulePage) && target is null && key != "startup" && (key != "all" || IsModulePage))
            {
                var findings = AllFindings.Where(item => MatchesSection(key, item.Finding, IsModulePage));
                status = !HasResults ? T("Not checked yet") : IsModulePage || key == "repairs"
                    ? new Message("{count} available repairs", ("count", findings.Sum(item => item.Repairs.Count))).Render(catalog)
                    : new Message("{count} problems · {unknown} unknown", ("count", findings.Count(item => item.Severity is Severity.Warning or Severity.Critical)),
                        ("unknown", findings.Count(item => item.Severity == Severity.Unknown))).Render(catalog);
            }
            Sections.Add(new(key, catalog.Translate(title), catalog.Translate(description), glyph,
                target ?? selectedModule.Module.Key, key == ActiveSection && (target is null || target == selectedModule.Module.Key), status));
        }

        switch (selectedModule.Module.Key)
        {
            case AppModules.ResultsKey:
                Add("all", "All findings", "Results across all checked areas", "\uE9D9");
                Add("repairs", "Repair and cleanup", "Supported actions from your latest scan", "\uE90F");
                Add("hardware", "Hardware and storage", "Components, battery and drive health", "\uE7F8");
                Add("performance", "Performance", "Processor, memory and startup findings", "\uE9D9");
                Add("windows", "Windows and security", "System files, updates and protection", "\uEA18");
                Add("network", "Network", "Connectivity and internet findings", "\uE701");
                Add("startup", "Startup apps", "Recorded timings and startup controls", "\uE768");
                break;
            case AppModules.RepairsKey:
                Add("all", "Performance and storage", "Review performance and storage improvements.", "\uE90F");
                Add("cleanup", "Free up space", "Review supported storage cleanups", "\uE74D");
                Add("startup", "Startup apps", "Recorded timings and startup controls", "\uE768", AppModules.ResultsKey);
                break;
            case "software":
                Add("all", "Installed apps", "Browse applications by category", "\uE71D");
                Add("uninstaller", "Uninstaller", "Remove an app, then review leftovers", "\uE74D");
                Add("updates", "Program updates", "Check supported program updates", "\uE895");
                Add("windows", "Windows Update", "Review updates in Windows Settings", "\uE777");
                Add("drivers", "Driver updates", "Review optional updates in Windows", "\uE713");
                break;
            case AppModules.HistoryKey:
                Add("all", "Repair history", "Results of previous repairs", "\uE81C");
                Add("undo", "Undo changes", "Repairs with an available undo", "\uE7A7");
                Add("help", "Get help", "Contact Shiva Computers", "\uE77B");
                break;
        }
    }

    private async Task SelectSectionAsync(WorkspaceSection? section)
    {
        if (section is null)
        {
            return;
        }

        sectionByModule[section.TargetModule] = section.Key;
        Navigate(Modules.First(module => module.Module.Key == section.TargetModule));
        RebuildSections();
        if (IsSoftwarePage)
        {
            Software.ActivePanel = section.Key;
            await Software.EnsureLoadedAsync();
        }
        if (IsStartupSection)
        {
            await Startup.EnsureLoadedAsync();
        }
        if (IsHistoryPage)
        {
            LoadHistory();
        }
        ApplyFilter();
        RefreshAll();
    }

    private bool MatchesSection(Finding finding) => MatchesSection(ActiveSection, finding, IsModulePage);

    private static bool MatchesSection(string section, Finding finding, bool optimize) => section switch
    {
        "all" when optimize => finding.Category == Category.Performance || finding.Id.StartsWith("STORAGE-SPACE", StringComparison.Ordinal),
        "hardware" => finding.Category is Category.Hardware or Category.Drivers || finding.Id.StartsWith("STORAGE-", StringComparison.Ordinal),
        "performance" => finding.Category == Category.Performance,
        "windows" => finding.Category is Category.WindowsSystem or Category.Security or Category.BootAndRecovery,
        "network" => finding.Category == Category.Network,
        "cleanup" => finding.Id.StartsWith("STORAGE-SPACE", StringComparison.Ordinal),
        "startup" => false,
        _ => true,
    };
}

public sealed record WorkspaceSection(string Key, string Title, string Description, string Glyph, string TargetModule, bool Selected, string Status)
{
    public string Summary => string.IsNullOrEmpty(Status) ? Description : Status;
}
public sealed record FindingGroup(string Name, IReadOnlyList<FindingItem> Items);

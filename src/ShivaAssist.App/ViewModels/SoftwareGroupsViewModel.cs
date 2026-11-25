using System.Collections.ObjectModel;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Software;

namespace ShivaAssist.App.ViewModels;

public sealed partial class SoftwareViewModel
{
    private ApplicationGroup? activeGroup;
    private string activePanel = "all";
    public ObservableCollection<ApplicationGroupFilter> Groups { get; } = [];
    public RelayCommand SelectGroupCommand { get; }
    public string TextGroupScope => T("Suggested categories are based on recognised application names. Unknown programs stay in Other applications. Grouping does not decide whether an app is safe or needs removal.");
    public bool ShowInventory => activePanel is "all" or "uninstaller";
    public bool ShowUninstaller => activePanel == "uninstaller";
    public bool ShowProgramUpdates => activePanel == "updates";
    public bool ShowWindowsTools => activePanel is "windows" or "drivers";
    public bool ShowWindowsUpdate => activePanel == "windows";
    public bool ShowDriverUpdates => activePanel == "drivers";
    public string TextPanelTitle => activePanel switch
    {
        "uninstaller" => TextUninstaller, "updates" => TextCheckUpdates,
        "windows" => TextWindowsUpdate, "drivers" => TextDriverUpdates, _ => T("Installed apps"),
    };

    public string ActivePanel
    {
        get => activePanel;
        set
        {
            if (Set(ref activePanel, value))
            {
                UninstallerExpanded = ShowUninstaller;
                RefreshAll();
            }
        }
    }

    private void SelectGroup(ApplicationGroupFilter? group)
    {
        if (group is not null)
        {
            activeGroup = group.Group;
            ApplyFilter();
        }
    }

    private void BuildGroups(InstalledProgram[] matching)
    {
        Groups.Clear();
        Groups.Add(new(null, T("All applications"), matching.Length, "\uE71D", activeGroup is null));
        foreach (var group in Enum.GetValues<ApplicationGroup>())
        {
            var count = matching.Count(program => ApplicationGroups.Suggest(program.Name) == group);
            if (count > 0 || activeGroup == group)
            {
                Groups.Add(new(group, ApplicationGroups.Name(group).Render(catalog()), count, ApplicationGroups.Glyph(group), activeGroup == group));
            }
        }
    }
}

public sealed record ApplicationGroupFilter(ApplicationGroup? Group, string Name, int Count, string Glyph, bool Selected);

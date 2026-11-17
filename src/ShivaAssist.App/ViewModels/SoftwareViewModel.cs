using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Software;
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

    public SoftwareViewModel(Func<TextCatalog> catalog)
    {
        this.catalog = catalog;
        RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !loading);
    }

    public RelayCommand RefreshCommand { get; }
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

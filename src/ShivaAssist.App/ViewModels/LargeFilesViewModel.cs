using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using ShivaAssist.Core.Cleanup;
using ShivaAssist.Core.Localization;

namespace ShivaAssist.App.ViewModels;

/// <summary>
/// Read-only finder for large personal files (>= 100 MB) in Downloads, Videos, Desktop and Documents.
/// Lets the customer inspect what takes space and open the containing folder in File Explorer; never deletes files.
/// </summary>
public sealed class LargeFilesViewModel : Observable
{
    private readonly Func<TextCatalog> catalog;
    private readonly Func<bool> busy;
    private bool working;
    private bool loaded;
    private bool truncated;
    private string status = string.Empty;

    public LargeFilesViewModel(Func<TextCatalog> catalog, Func<bool> busy)
    {
        this.catalog = catalog;
        this.busy = busy;
        ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !working && !busy());
        OpenFolderCommand = new RelayCommand(item => OpenFolder(item as LargeFileItem), item => item is LargeFileItem);
    }

    public ObservableCollection<LargeFileItem> Items { get; } = [];

    public RelayCommand ScanCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public bool Working => working;

    public bool Loaded => loaded;

    public bool HasItems => Items.Count > 0;

    public bool ShowEmpty => loaded && !working && Items.Count == 0;

    public string Status => status;

    public string TextTitle => T("Large personal files");

    public string TextScope => T("Finds files of 100 MB or larger in Downloads, Videos, Desktop and Documents so you can review them yourself. Shiva Assist never deletes your personal files.");

    public string TextScan => T("Find large files");

    public string TextOpenFolder => T("Open folder");

    public string TextEmpty => T("No files of 100 MB or larger were found in your personal folders.");

    public string TextFile => T("File");

    public string TextFolder => T("Folder");

    public string TextSize => T("Size");

    public string TextModified => T("Last modified");

    public async Task EnsureLoadedAsync()
    {
        if (!loaded && !working && !busy())
        {
            await ScanAsync();
        }
    }

    public void RefreshLanguage()
    {
        foreach (var item in Items)
        {
            item.Refresh();
        }

        if (loaded && !working)
        {
            UpdateSummaryStatus();
        }

        RefreshAll();
    }

    private async Task ScanAsync()
    {
        working = true;
        status = T("Looking for large files…");
        RefreshAll();
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var roots = GetPersonalRoots();
            var result = await Task.Run(() => LargeFileFinder.Scan(roots));
            Items.Clear();
            foreach (var entry in result.Files)
            {
                Items.Add(new LargeFileItem(entry, catalog));
            }

            truncated = result.Truncated;
            loaded = true;
            UpdateSummaryStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status = T("Large files could not be checked. Try again in a moment.");
        }
        finally
        {
            working = false;
            RefreshAll();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void UpdateSummaryStatus()
    {
        if (Items.Count == 0)
        {
            status = TextEmpty;
            return;
        }

        var totalBytes = Items.Sum(i => i.Entry.SizeBytes);
        status = truncated
            ? new Message("Showing the {count} largest files found ({size:gb}). Some deep folders were skipped to keep the check fast.",
                ("count", Items.Count), ("size", totalBytes)).Render(catalog())
            : new Message("{count} large files found ({size:gb}).",
                ("count", Items.Count), ("size", totalBytes)).Render(catalog());
    }

    private void OpenFolder(LargeFileItem? item)
    {
        if (item is null || !File.Exists(item.Entry.FullPath))
        {
            status = T("That file is no longer in this folder. Refresh the list to update it.");
            OnPropertyChanged(nameof(Status));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Entry.FullPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            status = T("File Explorer could not be opened for this file.");
            OnPropertyChanged(nameof(Status));
        }
    }

    private static IReadOnlyList<(string Label, string Path)> GetPersonalRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = string.IsNullOrWhiteSpace(profile) ? string.Empty : Path.Combine(profile, "Downloads");
        return
        [
            ("Downloads", downloads),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ];
    }

    private string T(string template) => new Message(template).Render(catalog());
}

/// <summary>One large personal file row.</summary>
public sealed class LargeFileItem(LargeFileEntry entry, Func<TextCatalog> catalog) : Observable
{
    public LargeFileEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public string Folder => Entry.FolderLabel switch
    {
        "Downloads" => new Message("Downloads").Render(catalog()),
        "Videos" => new Message("Videos").Render(catalog()),
        "Desktop" => new Message("Desktop").Render(catalog()),
        "Documents" => new Message("Documents").Render(catalog()),
        _ => Entry.FolderLabel,
    };

    public string Size => new Message("{size:gb}", ("size", Entry.SizeBytes)).Render(catalog());

    public string Modified => Entry.LastModified.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo(catalog().Language.Code));

    public void Refresh() => RefreshAll();
}

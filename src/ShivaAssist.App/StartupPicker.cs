using System.Windows;
using System.Windows.Controls;
using ShivaAssist.Core.Modules;

namespace ShivaAssist.App;

/// <summary>Lets the technician tick the startup programs to switch off. Built in code: it is a small dialog.</summary>
public sealed class StartupPicker : Window
{
    private readonly List<(CheckBox Box, StartupEntry Entry)> boxes = [];

    public StartupPicker(IReadOnlyList<StartupEntry> entries, string title, string intro, string confirm, string cancel)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var entry in entries)
        {
            var box = new CheckBox { Content = entry.Name, Margin = new Thickness(0, 3, 0, 3) };
            boxes.Add((box, entry));
            list.Children.Add(box);
        }

        var ok = new Button { Content = confirm, IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var no = new Button { Content = cancel, IsCancel = true, Padding = new Thickness(16, 6, 16, 6) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(no);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>References (<c>Location|Key</c>) of the ticked entries.</summary>
    public IReadOnlyList<string> Selected => [.. boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Entry.Reference)];
}

using System.Windows;
using System.Windows.Controls;
using ShivaAssist.App.ViewModels;
using ShivaAssist.Core.Localization;

namespace ShivaAssist.App;

/// <summary>Shows the exact catalogue operations and collects approval for the selected batch.</summary>
public sealed class RepairPicker : Window
{
    private readonly List<(CheckBox Box, RepairButton Repair)> choices = [];

    public RepairPicker(IReadOnlyList<RepairButton> repairs, TextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(repairs);
        ArgumentNullException.ThrowIfNull(catalog);
        Title = catalog.Translate("Review available repairs");
        Width = 600;
        Height = 560;
        MinWidth = 440;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        var root = new DockPanel { Margin = new Thickness(20) };
        var intro = new TextBlock
        {
            Text = catalog.Translate("Choose the repairs to run and approve them together. Some repairs need you to choose what to change before the batch starts. Repairs run one at a time and their results are checked."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var confirm = new Button
        {
            Content = catalog.Translate("Approve and run selected repairs"), IsEnabled = false,
            Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 12, 8, 0),
        };
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(confirm);
        buttons.Children.Add(new Button
        {
            Content = catalog.Translate("Cancel"), IsCancel = true,
            Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 12, 0, 0),
        });
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var list = new StackPanel();
        foreach (var repair in repairs)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = repair.Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = repair.Item.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            content.Children.Add(new TextBlock { Text = repair.Tooltip, TextWrapping = TextWrapping.Wrap });
            if (repair.Operation.NeedsRestorePoint)
            {
                content.Children.Add(new TextBlock { Text = catalog.Translate("A restore point is made before the repair starts. If System Protection is off, it is switched on first."), TextWrapping = TextWrapping.Wrap });
            }

            var box = new CheckBox { Content = content, Margin = new Thickness(0, 0, 12, 18) };
            box.Checked += (_, _) => confirm.IsEnabled = choices.Any(choice => choice.Box.IsChecked == true);
            box.Unchecked += (_, _) => confirm.IsEnabled = choices.Any(choice => choice.Box.IsChecked == true);
            choices.Add((box, repair));
            list.Children.Add(box);
        }

        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    public IReadOnlyList<RepairButton> Selected => choices.Where(choice => choice.Box.IsChecked == true)
        .Select(choice => choice.Repair).ToArray();
}

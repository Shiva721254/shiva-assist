using System.ComponentModel;
using System.Reflection;
using System.Windows;
using ShivaAssist.App.ViewModels;

namespace ShivaAssist.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        var technician = !Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--read-only", StringComparison.OrdinalIgnoreCase));
        DataContext = new MainViewModel($"Shiva Assist {version}", technician);
    }

    /// <summary>Closing during a check-up or repair needs a deliberate second "yes"; the safe answer is the default.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (DataContext is MainViewModel { IsBusy: true } vm
            && MessageBox.Show(this, vm.TextCloseWhileBusy, "Shiva Assist", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}

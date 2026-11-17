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
        var technician = Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--technician", StringComparison.OrdinalIgnoreCase));
        DataContext = new MainViewModel($"Shiva Assist {version}", technician);
    }
}

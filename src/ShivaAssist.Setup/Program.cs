using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShivaAssist.Setup;

internal static class Program
{
    private static readonly Dictionary<string, object> Dutch = LoadDutch();
    public static string T(string text) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl"
        && Dutch.TryGetValue(text, out var value) && value is string translated ? translated : text;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                var architecture = WindowsInstallerApi.Architecture();
                if (architecture != "win-x64" && architecture != "win-arm64") return 1;
                using var abc = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
                if (InstallerDownloads.Hash(abc) != "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad") return 1;
                using var own = File.OpenRead(typeof(Program).Assembly.Location);
                if (WindowsInstallerApi.TrustedPublisher(typeof(Program).Assembly.Location, own, new string('0', 64))) return 1;
                return 0;
            }
            if (args.Length == 4 && args[0] == "--verify-package" && args[2] == "--sha256")
            {
                InstallerPolicy.EnsureNoLinks(args[1]);
                using var file = File.OpenRead(args[1]);
                return InstallerPolicy.ValidDigest(args[3]) && InstallerDownloads.Hash(file).Equals(args[3], StringComparison.OrdinalIgnoreCase)
                    && WindowsInstallerApi.TrustedPublisher(args[1], file, InstallPipeline.PublisherPin) ? 0 : 1;
            }
#if SETUP_LAB
            if (args.Length == 5 && args[0] == "--local-package" && args[2] == "--sha256" && args[4] == "--test-install")
            {
                var result = Task.Run(() => InstallPipeline.RunAsync(_ => { }, CancellationToken.None, args[1], args[3], quiet: true)).GetAwaiter().GetResult();
                return result.RestartRequired ? 3010 : 0;
            }
#endif
            if (args.Length > 0 && !(args.Length == 1 && args[0] == "--ui-smoke")) return 64;
            var app = new Application();
            var window = new SetupWindow();
            if (args.Length == 1)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                window.Loaded += (_, _) => timer.Start();
            }
            return app.Run(window);
        }
        catch (Exception ex) when (Expected(ex))
        {
            if (args.Length != 0) Console.Error.WriteLine(ex);
            if (args.Length == 0) MessageBox.Show(T(ex.Message), "Shiva Assist Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    public static bool Expected(Exception exception) => exception is IOException || exception is UnauthorizedAccessException
        || exception is InvalidOperationException || exception is ArgumentException || exception is System.ComponentModel.Win32Exception
        || exception is HttpRequestException || exception is System.Security.SecurityException || exception is System.Security.Cryptography.CryptographicException || exception is OperationCanceledException
        || exception is System.Runtime.InteropServices.ExternalException || exception is KeyNotFoundException || exception is InvalidCastException;

    private static Dictionary<string, object> LoadDutch()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("ShivaAssist.Setup.nl.json");
        using var reader = new StreamReader(stream ?? throw new InvalidOperationException("Missing setup language resource."));
        var root = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        return (Dictionary<string, object>)root["texts"];
    }
}

// The per-operation token source is disposed in InstallAsync.finally; closing is blocked while running.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Token source lifetime is bounded by the awaited install operation.")]
internal sealed class SetupWindow : Window
{
    private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 12) };
    private readonly ProgressBar progress = new ProgressBar { Height = 12, Maximum = 100, Margin = new Thickness(0, 8, 0, 18) };
    private readonly Button install = new Button { Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 10, 0), MinWidth = 135 };
    private readonly Button cancel = new Button { Padding = new Thickness(18, 8, 18, 8), MinWidth = 90 };
    private readonly CheckBox open = new CheckBox { IsChecked = true, Margin = new Thickness(0, 8, 0, 16) };
    private CancellationTokenSource? cancellation;
    private bool running;
    private bool installing;
    private InstallResult? result;

    public SetupWindow()
    {
        Title = "Shiva Assist Setup";
        Width = 570; Height = 520; MinWidth = 500; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(251, 247, 238));
        var green = new SolidColorBrush(Color.FromRgb(46, 158, 91));
        progress.Foreground = green;
        install.Background = green; install.Foreground = Brushes.White;
        install.Content = Program.T("Download and install");
        cancel.Content = Program.T("Cancel");
        open.Content = Program.T("Open Shiva Assist when setup finishes");
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Shiva Assist", FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = green });
        panel.Children.Add(new TextBlock { Text = Program.T("Download, verify and install the correct version for this PC."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
#if SETUP_LAB
        status.Text = Program.T("LOCAL TEST BUILD. Choose the MSI and its matching .sha256 file from the test download. Do not distribute this installer to customers.");
        install.Content = Program.T("Choose test package");
#else
        status.Text = Program.T("Setup needs an internet connection. You can cancel the download. Windows Installer handles installation and rollback.");
#endif
        panel.Children.Add(status); panel.Children.Add(progress); panel.Children.Add(open);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(install); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        install.Click += async (_, _) => await InstallAsync();
        cancel.Click += (_, _) => { if (running) cancellation?.Cancel(); else Close(); };
        Closing += (_, e) => { if (running) { e.Cancel = true; if (!installing) cancellation?.Cancel(); } };
    }

    private async Task InstallAsync()
    {
        if (result != null && !result.RestartRequired)
        {
            try { InstallPipeline.Open(result); Close(); }
            catch (Exception ex) when (Program.Expected(ex)) { status.Text = Program.T(ex.Message); }
            return;
        }
        string? local = null, expected = null;
#if SETUP_LAB
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Windows Installer (*.msi)|*.msi", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        local = picker.FileName;
        try { expected = File.ReadAllText(local + ".sha256").Trim(); }
        catch (IOException) { status.Text = Program.T("Place the matching .sha256 file beside the local test package."); return; }
#endif
        running = true; installing = false; install.IsEnabled = false; open.IsEnabled = false;
        cancellation = new CancellationTokenSource();
        try
        {
            result = await Task.Run(() => InstallPipeline.RunAsync(update => Dispatcher.Invoke(() =>
            {
                installing = update.Installing;
                cancel.IsEnabled = !installing;
                status.Text = Program.T(update.Text);
                progress.IsIndeterminate = installing;
                progress.Value = update.Percent;
            }), cancellation.Token, local, expected));
            status.Text = Program.T(result.RestartRequired
                ? "Shiva Assist is installed. Restart Windows before opening it. Setup will not restart your PC automatically."
                : "Shiva Assist was installed and verified. Your shortcut is available in the Start menu.");
            progress.IsIndeterminate = false; progress.Value = 100;
            install.Content = Program.T("Open Shiva Assist");
            install.IsEnabled = !result.RestartRequired;
            if (!result.RestartRequired && open.IsChecked == true) InstallPipeline.Open(result);
        }
        catch (OperationCanceledException)
        {
            result = null;
            status.Text = Program.T("Setup was cancelled. You can retry when you are ready.");
            install.Content = Program.T("Retry"); install.IsEnabled = true;
        }
        catch (Exception ex) when (Program.Expected(ex))
        {
            result = null;
            status.Text = Program.T(ex.Message);
            install.Content = Program.T("Retry"); install.IsEnabled = true;
        }
        finally
        {
            running = false; installing = false; cancel.IsEnabled = true;
            cancel.Content = Program.T("Close"); progress.IsIndeterminate = false;
            open.IsEnabled = true;
            cancellation.Dispose(); cancellation = null;
        }
    }
}

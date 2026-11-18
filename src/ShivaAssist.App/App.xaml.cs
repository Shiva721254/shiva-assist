using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using ShivaAssist.Core.Localization;
using ShivaAssist.Windows;

namespace ShivaAssist.App;

public partial class App : Application
{
    private static Mutex? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (WindowsPlatform.IsElevated())
        {
            // One window at a time: two copies repairing the same PC would get in each other's way.
            instance = new Mutex(initiallyOwned: true, @"Local\ShivaAssist.App", out var created);
            if (!created)
            {
                instance.Dispose();
                instance = null;
                MessageBox.Show(StartupCatalog().Translate("Shiva Assist is already open. Use the window that is already running."),
                    "Shiva Assist", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // An unexpected error must never close the window in the middle of a check-up or repair.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                CrashLog.Write(args.Exception, "background task");
                args.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    CrashLog.Write(ex, "process");
                }
            };
            base.OnStartup(e);
            return;
        }

        // Request UAC before creating the dashboard. The elevated process handles the whole scan and repair job.
        var catalog = StartupCatalog();
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
            // Also support launching the framework-dependent DLL with 'dotnet ShivaAssist.dll'.
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
            }

            foreach (var argument in e.Args)
            {
                start.ArgumentList.Add(argument);
            }

            using var elevated = Process.Start(start) ?? throw new InvalidOperationException("The administrator process did not start.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(catalog.Translate("Administrator permission was declined. Open Shiva Assist again when you are ready to approve the Windows permission prompt."),
                "Shiva Assist", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(catalog.Translate("Shiva Assist could not start with administrator permission. Right-click Shiva Assist and choose Run as administrator."),
                "Shiva Assist", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (instance is not null)
        {
            instance.ReleaseMutex();
            instance.Dispose();
        }

        base.OnExit(e);
    }

    private static TextCatalog StartupCatalog()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return TextCatalog.For(language == "nl" ? "nl" : TextCatalog.DefaultLanguage);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception, "window");
        e.Handled = true;
        MessageBox.Show(StartupCatalog().Translate("Something unexpected went wrong. Shiva Assist keeps running. If this happens again, please send the file crash-log.txt from the Shiva Assist folder in your AppData\\Local folder to Shiva Computers."),
            "Shiva Assist", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

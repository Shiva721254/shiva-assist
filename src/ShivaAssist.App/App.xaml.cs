using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ShivaAssist.Core.Localization;
using ShivaAssist.Windows;

namespace ShivaAssist.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (WindowsPlatform.IsElevated())
        {
            base.OnStartup(e);
            return;
        }

        // Request UAC before creating the dashboard. The elevated process handles the whole scan and repair job.
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var catalog = TextCatalog.For(language == "nl" ? "nl" : TextCatalog.DefaultLanguage);
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
}

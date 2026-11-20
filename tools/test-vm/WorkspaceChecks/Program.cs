using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ShivaAssist.App;
using ShivaAssist.App.ViewModels;
using ShivaAssist.Windows;

namespace ShivaAssist.WorkspaceChecks;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!WindowsPlatform.IsElevated())
        {
            throw new InvalidOperationException("Run the WPF workspace smoke test in the elevated disposable Windows CI runner.");
        }

        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        using var listener = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        var app = new ShivaAssist.App.App();
        app.InitializeComponent();
        app.StartupUri = new Uri("/ShivaAssist;component/MainWindow.xaml", UriKind.Relative);
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                var window = (MainWindow)app.MainWindow;
                var vm = (MainViewModel)window.DataContext;
                if (vm.Modules.Count != 5)
                {
                    throw new InvalidOperationException("Expected exactly five workspaces.");
                }
                foreach (var module in vm.Modules)
                {
                    vm.NavigateCommand.Execute(module);
                    if (vm.IsSoftwarePage)
                    {
                        await WaitForAsync(() => vm.Software.Groups.Count > 0);
                    }
                    var sections = vm.Sections.ToArray();
                    if ((sections.Length == 0 && !vm.IsOverviewPage) || sections.Any(s => !vm.Modules.Any(m => m.Module.Key == s.TargetModule)))
                    {
                        throw new InvalidOperationException("Missing section or invalid shortcut target.");
                    }
                    foreach (var section in sections.Where(s => s.TargetModule == module.Module.Key))
                    {
                        vm.SectionCommand.Execute(section);
                        if (vm.IsStartupSection)
                        {
                            await WaitForAsync(() => vm.Startup.Status.Length > 0
                                && !vm.Startup.Status.Contains('…'));
                        }
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();
                        if (vm.Sections.Count(s => s.Selected) != 1)
                        {
                            throw new InvalidOperationException("A workspace must show exactly one selected section.");
                        }
                    }
                    Console.WriteLine("PASS: workspace sections rendered: " + module.Module.Key);
                }

                var software = vm.Modules.Single(m => m.Module.Key == "software");
                vm.NavigateCommand.Execute(software);
                vm.SectionCommand.Execute(vm.Sections.Single(s => s.Key == "all"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach (var group in vm.Software.Groups.ToArray())
                {
                    vm.Software.SelectGroupCommand.Execute(group);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
                // Read-only scan so the Fix it all hero renders with real findings. No repair is started here.
                vm.NavigateCommand.Execute(vm.Modules.First(m => m.IsOverview));
                vm.StartScanCommand.Execute(null);
                await WaitForAsync(() => vm.HasResults, TimeSpan.FromMinutes(4));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                if (!vm.ShowRepairReview || vm.IsFixingAll || !vm.ShowFixAllButton || vm.TextProblemCount.Length == 0)
                {
                    throw new InvalidOperationException("The Fix it all hero did not render after a scan.");
                }

                Console.WriteLine("PASS: read-only scan and Fix it all hero (" + vm.FixAllPlan.Count.ToString(CultureInfo.InvariantCulture) + " one-click repairs offered)");
                window.Width = 760;
                window.Height = 560;
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                listener.Flush();
                if (errors.GetStringBuilder().Length != 0)
                {
                    throw new InvalidOperationException("WPF binding errors: " + errors);
                }
                Console.WriteLine("PASS: grouping, small-window layout and workspace bindings");
                app.Shutdown(0);
            }
#pragma warning disable CA1031 // Report every UI smoke-test failure and shut down the dispatcher deterministically.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine(ex);
                app.Shutdown(1);
            }
        }));
        return app.Run();
    }

    private static Task WaitForAsync(Func<bool> ready) => WaitForAsync(ready, TimeSpan.FromSeconds(40));

    private static async Task WaitForAsync(Func<bool> ready, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready())
        {
            if (elapsed.Elapsed > timeout)
            {
                throw new TimeoutException("Read-only workspace load timed out.");
            }
            await Task.Delay(100);
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;
using ShivaAssist.Windows.Maintenance;

namespace ShivaAssist.App.ViewModels;

/// <summary>
/// History and help → Get help: contact, the optional monthly check-up, the battery report, emergency recovery and
/// remote help. Everything here is read-only or only changes Shiva Assist's own scheduled task.
/// </summary>
public sealed partial class MainViewModel
{
    public const string RescueUrl = "https://rescue.shivacomputers.nl/";

    /// <summary>Windows 11's built-in Quick Assist (also on updated Windows 10).</summary>
    public const string QuickAssistUri = "ms-quick-assist:";

    /// <summary>Microsoft Store page of Quick Assist, for PCs where it was removed.</summary>
    public const string QuickAssistStoreUri = "ms-windows-store://pdp/?productid=9P7BP5VNWKX5";

    private bool monthlyEnabled;
    private bool monthlyBusy;
    private string? helpStatus;
    private readonly Lazy<(DateTimeOffset When, int Problems)?> monthlyLatest = new(() => MonthlyCheckup.ReadLatest());

    private RelayCommand? toggleMonthlyCommand;
    private RelayCommand? batteryReportCommand;
    private RelayCommand? rescueCommand;
    private RelayCommand? remoteHelpCommand;
    private RelayCommand? quickAssistCommand;
    private RelayCommand? openRemoteHelpCommand;

    public RelayCommand ToggleMonthlyCommand => toggleMonthlyCommand ??= new RelayCommand(async _ => await ToggleMonthlyAsync(), _ => !monthlyBusy && MonthlyCheckup.Available);

    public RelayCommand BatteryReportCommand => batteryReportCommand ??= new RelayCommand(async _ => await BatteryReportAsync());

    public RelayCommand RescueCommand => rescueCommand ??= new RelayCommand(_ => Open(RescueUrl));

    public RelayCommand RemoteHelpCommand => remoteHelpCommand ??= new RelayCommand(_ => Open(HelpUrl.Replace("{lang}", catalog.Language.Code == "nl" ? "nl" : "en", StringComparison.Ordinal) + "?topic=remote-help"));

    public RelayCommand QuickAssistCommand => quickAssistCommand ??= new RelayCommand(_ => StartQuickAssist());

    /// <summary>Opens History and help -> Get help so the customer can start Quick Assist or request remote help.</summary>
    public RelayCommand OpenRemoteHelpCommand => openRemoteHelpCommand ??= new RelayCommand(async _ =>
        await SelectSectionAsync(new WorkspaceSection("help", T("Get help"), T("Contact Shiva Computers"), "", AppModules.HistoryKey, true, string.Empty)));

    /// <summary>Shown in the hero card when problems exist (or remain after Fix it all) that cannot be fixed automatically.</summary>
    public bool ShowHeroRemoteHelp => HasResults && !IsFixingAll && ProblemCount > 0 && !HasFixAllPlan;

    public string TextGetRemoteHelp => T("Get remote help");

    public string TextHeroRemoteHelpHint => T("Could not be fixed automatically? Connect with a Shiva Computers technician for safe, consent-based remote help.");

    public string TextHeroRemoteHelpTooltip => T("Open Quick Assist or request remote help from Shiva Computers");

    public string? HelpStatus => helpStatus;

    public string TextHelpShort => T("Contact us");

    public string TextMonthlyTitle => T("Monthly check-up");

    public string TextMonthlyScope => T("Every four weeks Shiva Assist checks this PC in the background. It only reads information and never repairs anything by itself. The next time you open Shiva Assist, you see what it found.");

    public string TextMonthlyState => monthlyEnabled ? T("On: the next check-up runs within four weeks.") : T("Off");

    public string TextMonthlyToggle => monthlyEnabled ? T("Switch off") : T("Switch on");

    public string TextBatteryTitle => T("Battery report");

    public string TextBatteryScope => T("For laptops: Windows' own detailed battery report, with the original and current capacity and recent use.");

    public string TextBatteryOpen => T("Open battery report");

    public string TextRescueTitle => T("Windows does not start any more?");

    public string TextRescueScope => T("Shiva Rescue starts the PC from a USB stick, rescues your files first and then tries to repair the start-up. Prepare the stick on another PC.");

    public string TextRescueOpen => T("About Shiva Rescue");

    public string TextRemoteTitle => T("Remote help");

    public string TextRemoteScope => T("A Shiva Computers technician can look at this PC with you over the internet. You approve every session yourself, it is only for that one job, and you can stop it at any moment.");

    public string TextRemoteOpen => T("Request remote help");

    public string TextQuickAssist => T("Start Quick Assist");

    public string TextQuickAssistSteps => T("Already on the phone with Shiva Computers? Click Start Quick Assist, choose \"Get help\" and type the security code the technician gives you. You see everything the technician does and can stop at any moment.");

    /// <summary>The last automatic check-up, shown on Overview until a new scan is made.</summary>
    public string? MonthlyNote
    {
        get
        {
            if (HasResults || monthlyLatest.Value is not { } latest)
            {
                return null;
            }

            var when = latest.When.ToLocalTime().ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo(catalog.Language.Code));
            return latest.Problems == 0
                ? new Message("Monthly check-up of {date}: no problems found.", ("date", when)).Render(catalog)
                : new Message("Monthly check-up of {date}: {count} problems found. Start the check-up to see and fix them.", ("date", when), ("count", latest.Problems)).Render(catalog);
        }
    }

    public bool HasMonthlyNote => MonthlyNote is not null;

    /// <summary>Reads whether the scheduled task exists; called when the help section opens.</summary>
    private async Task LoadMonthlyStateAsync()
    {
        try
        {
            monthlyEnabled = MonthlyCheckup.Available && await MonthlyCheckup.IsEnabledAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            monthlyEnabled = false;
        }

        OnPropertyChanged(nameof(TextMonthlyState));
        OnPropertyChanged(nameof(TextMonthlyToggle));
    }

    private async Task ToggleMonthlyAsync()
    {
        monthlyBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            if (monthlyEnabled)
            {
                await MonthlyCheckup.DisableAsync(CancellationToken.None);
                helpStatus = T("The monthly check-up is switched off.");
            }
            else
            {
                await MonthlyCheckup.EnableAsync(CancellationToken.None);
                helpStatus = T("The monthly check-up is switched on.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            helpStatus = new Message("The monthly check-up could not be changed: {reason}", ("reason", ex.Message)).Render(catalog);
        }
        finally
        {
            monthlyBusy = false;
        }

        await LoadMonthlyStateAsync();
        OnPropertyChanged(nameof(HelpStatus));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Windows' battery report, opened in the browser.</summary>
    private async Task BatteryReportAsync()
    {
        try
        {
            if (await BatteryReport.CreateAsync(CancellationToken.None))
            {
                Process.Start(new ProcessStartInfo(BatteryReport.Path) { UseShellExecute = true });
                helpStatus = null;
            }
            else
            {
                helpStatus = T("This PC has no battery, so there is no battery report.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            helpStatus = new Message("The battery report could not be made: {reason}", ("reason", ex.Message)).Render(catalog);
        }

        OnPropertyChanged(nameof(HelpStatus));
    }

    /// <summary>
    /// Opens Windows' own Quick Assist. Shiva Assist does not share the screen itself: the customer enters the
    /// technician's code and approves the session in Quick Assist, which keeps consent per job with the customer.
    /// </summary>
    private void StartQuickAssist()
    {
        try
        {
            Open(QuickAssistUri);
            helpStatus = T("Quick Assist is opening. Choose \"Get help\" and type the code the technician gives you.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            try
            {
                Open(QuickAssistStoreUri);
                helpStatus = T("Quick Assist is not installed on this PC. The Microsoft Store opens so you can install it for free; then click Start Quick Assist again.");
            }
            catch (Exception inner) when (inner is Win32Exception or InvalidOperationException)
            {
                helpStatus = new Message("Quick Assist could not be opened: {reason}", ("reason", inner.Message)).Render(catalog);
            }
        }

        OnPropertyChanged(nameof(HelpStatus));
    }
}

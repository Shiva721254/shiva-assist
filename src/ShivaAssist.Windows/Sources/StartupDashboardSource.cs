using ShivaAssist.Core.Modules;
using ShivaAssist.Windows.Infrastructure;

namespace ShivaAssist.Windows.Sources;

public sealed record StartupDashboardData(IReadOnlyList<StartupApplication> Applications, StartupBoot? Boot, bool TimingAvailable);

public static class StartupDashboardSource
{
    public static async Task<StartupDashboardData> ReadAsync(CancellationToken cancellationToken)
    {
        var registrations = StartupSource.ReadEntries(includeDisabled: true).Entries
            .Select(entry => new StartupRegistration(entry, StartupSource.ExecutableName(entry))).ToArray();
        var performance = new StartupPerformance([], null);
        var available = false;
        try
        {
            var (exit, xml, _) = await ToolRunner.RunAsync(ToolRunner.System32("wevtutil.exe"),
                ["qe", "Microsoft-Windows-Diagnostics-Performance/Operational",
                    "/q:*[System[(EventID=100 or EventID=101) and TimeCreated[timediff(@SystemTime) <= 1209600000]]]",
                    "/f:xml", "/c:200", "/rd:true"], cancellationToken);
            if (exit == 0)
            {
                performance = StartupMeasurements.Parse(EventXml.ParseEvents(xml));
                available = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or System.Xml.XmlException)
        {
            // The registration list remains useful when the performance log is unavailable or disabled.
        }

        return new(StartupMeasurements.Match(registrations, performance), performance.Boot, available);
    }
}

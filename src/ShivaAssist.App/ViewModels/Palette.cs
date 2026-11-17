using System.Windows.Media;
using ShivaAssist.Core.Findings;

namespace ShivaAssist.App.ViewModels;

/// <summary>Bright app palette: solid green on cream, with a solid colour per severity (same as the PDF report). Mirrors App.xaml.</summary>
public static class Palette
{
    public static readonly SolidColorBrush Ink = Freeze(0x2F, 0x3B, 0x33);
    public static readonly SolidColorBrush Green = Freeze(0x2E, 0x9E, 0x5B);
    public static readonly SolidColorBrush GreenSoft = Freeze(0xDD, 0xF3, 0xE4);
    public static readonly SolidColorBrush Muted = Freeze(0x6E, 0x6A, 0x5E);

    public static SolidColorBrush For(Severity severity) => severity switch
    {
        Severity.Critical => Critical,
        Severity.Warning => Warning,
        Severity.Information => Information,
        Severity.Unknown => Unknown,
        _ => Healthy,
    };

    public static SolidColorBrush Background(Severity severity) => severity switch
    {
        Severity.Critical => CriticalSoft,
        Severity.Warning => WarningSoft,
        Severity.Information => InformationSoft,
        Severity.Unknown => UnknownSoft,
        _ => HealthySoft,
    };

    private static readonly SolidColorBrush Information = Freeze(0x2B, 0x8F, 0xB3);
    private static readonly SolidColorBrush InformationSoft = Freeze(0xDC, 0xEF, 0xF6);
    private static readonly SolidColorBrush Critical = Freeze(0xD9, 0x47, 0x2B);
    private static readonly SolidColorBrush Warning = Freeze(0xE8, 0x89, 0x0C);
    private static readonly SolidColorBrush Unknown = Freeze(0x8A, 0x85, 0x77);
    private static readonly SolidColorBrush Healthy = Freeze(0x2E, 0x9E, 0x5B);
    private static readonly SolidColorBrush CriticalSoft = Freeze(0xF9, 0xE1, 0xDA);
    private static readonly SolidColorBrush WarningSoft = Freeze(0xFC, 0xEB, 0xD3);
    private static readonly SolidColorBrush UnknownSoft = Freeze(0xEE, 0xEB, 0xE4);
    private static readonly SolidColorBrush HealthySoft = Freeze(0xDD, 0xF3, 0xE4);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

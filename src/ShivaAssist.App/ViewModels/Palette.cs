using System.Windows.Media;
using ShivaAssist.Core.Findings;

namespace ShivaAssist.App.ViewModels;

/// <summary>Warm app palette: matte green on cream, with soft earthy colours per severity. Mirrors App.xaml.</summary>
public static class Palette
{
    public static readonly SolidColorBrush Ink = Freeze(0x2F, 0x3B, 0x33);
    public static readonly SolidColorBrush Green = Freeze(0x4F, 0x7A, 0x5A);
    public static readonly SolidColorBrush GreenSoft = Freeze(0xE6, 0xEE, 0xE3);
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

    private static readonly SolidColorBrush Information = Freeze(0x4A, 0x7A, 0x88);
    private static readonly SolidColorBrush InformationSoft = Freeze(0xE4, 0xEE, 0xF0);
    private static readonly SolidColorBrush Critical = Freeze(0xB4, 0x53, 0x3A);
    private static readonly SolidColorBrush Warning = Freeze(0xB0, 0x74, 0x1C);
    private static readonly SolidColorBrush Unknown = Freeze(0x85, 0x80, 0x72);
    private static readonly SolidColorBrush Healthy = Freeze(0x4F, 0x7A, 0x5A);
    private static readonly SolidColorBrush CriticalSoft = Freeze(0xF8, 0xE7, 0xE1);
    private static readonly SolidColorBrush WarningSoft = Freeze(0xFA, 0xEF, 0xDA);
    private static readonly SolidColorBrush UnknownSoft = Freeze(0xEF, 0xEB, 0xE2);
    private static readonly SolidColorBrush HealthySoft = Freeze(0xE6, 0xEE, 0xE3);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

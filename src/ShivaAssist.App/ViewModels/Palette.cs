using System.Windows.Media;
using ShivaAssist.Core.Findings;

namespace ShivaAssist.App.ViewModels;

/// <summary>Shiva Computers colours (website specification) and one colour per severity.</summary>
public static class Palette
{
    public static readonly SolidColorBrush Navy = Freeze(0x10, 0x23, 0x3E);
    public static readonly SolidColorBrush Blue = Freeze(0x14, 0x65, 0xDF);
    public static readonly SolidColorBrush LightBlue = Freeze(0xEF, 0xF6, 0xFF);

    public static SolidColorBrush For(Severity severity) => severity switch
    {
        Severity.Critical => Critical,
        Severity.Warning => Warning,
        Severity.Information => Blue,
        Severity.Unknown => Unknown,
        _ => Healthy,
    };

    public static SolidColorBrush Background(Severity severity) => severity switch
    {
        Severity.Critical => CriticalSoft,
        Severity.Warning => WarningSoft,
        Severity.Information => LightBlue,
        Severity.Unknown => UnknownSoft,
        _ => HealthySoft,
    };

    private static readonly SolidColorBrush Critical = Freeze(0xC6, 0x28, 0x28);
    private static readonly SolidColorBrush Warning = Freeze(0xB2, 0x5E, 0x00);
    private static readonly SolidColorBrush Unknown = Freeze(0x6B, 0x72, 0x80);
    private static readonly SolidColorBrush Healthy = Freeze(0x2E, 0x7D, 0x32);
    private static readonly SolidColorBrush CriticalSoft = Freeze(0xFD, 0xEC, 0xEC);
    private static readonly SolidColorBrush WarningSoft = Freeze(0xFF, 0xF4, 0xE5);
    private static readonly SolidColorBrush UnknownSoft = Freeze(0xF3, 0xF4, 0xF6);
    private static readonly SolidColorBrush HealthySoft = Freeze(0xED, 0xF7, 0xED);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

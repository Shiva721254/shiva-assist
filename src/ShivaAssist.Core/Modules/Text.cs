using System.Globalization;

namespace ShivaAssist.Core.Modules;

/// <summary>Formatting shared by the checks, culture-independent so that reports are identical everywhere.</summary>
internal static class Text
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Gb(long bytes) => (bytes / 1e9).ToString(bytes >= 100e9 ? "0" : "0.0", Inv) + " GB";

    public static string Percent(double fraction) => (fraction * 100).ToString("0", Inv) + "%";

    public static string N(int value) => value.ToString(Inv);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Inv);
}

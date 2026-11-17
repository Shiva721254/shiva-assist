namespace ShivaAssist.Core.Modules;

/// <summary>Parses the English output of <c>DISM /Online /Cleanup-Image /CheckHealth /English</c>.</summary>
public static class DismOutput
{
    public const string RepairableSummary = "The component store is repairable.";

    /// <summary>Classifies DISM's English summary line.</summary>
    public static IntegritySnapshot Classify(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        static bool Has(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);

        if (Has(output, "No component store corruption detected"))
        {
            return new(ComponentStoreState.NoCorruption, "No component store corruption detected.");
        }

        if (Has(output, "component store is repairable"))
        {
            return new(ComponentStoreState.Repairable, RepairableSummary);
        }

        if (Has(output, "component store cannot be repaired"))
        {
            return new(ComponentStoreState.NotRepairable, "The component store cannot be repaired.");
        }

        var last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no output";
        return new(ComponentStoreState.Unrecognised, last.Length > 160 ? last[..160] : last);
    }
}

using System.Xml.Linq;

namespace ShivaAssist.Core.Modules;

/// <summary>Parses event records as printed by <c>wevtutil qe … /f:xml</c>.</summary>
public static class EventXml
{
    /// <summary>Application Error 1000: the first data field is the program's file name.</summary>
    public static string AppName(XElement e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var first = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "Data")?.Value.Trim();
        return string.IsNullOrEmpty(first) ? "unknown program" : first;
    }

    /// <summary>Parses wevtutil's XML output (a sequence of Event elements without a root).</summary>
    public static IReadOnlyList<XElement> ParseEvents(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        return [.. XElement.Parse("<Events>" + xml + "</Events>").Elements()];
    }
}

using ShivaAssist.Core.Localization;

namespace ShivaAssist.Core.Software;

public enum ApplicationGroup { Internet, Office, Media, Games, Development, Security, Utilities, Other }

/// <summary>Suggested display groups only. Unknown software stays Other; grouping never authorises a repair.</summary>
public static class ApplicationGroups
{
    public static Message Name(ApplicationGroup group) => group switch
    {
        ApplicationGroup.Internet => "Internet and browsers",
        ApplicationGroup.Office => "Office and productivity",
        ApplicationGroup.Media => "Media and design",
        ApplicationGroup.Games => "Games and launchers",
        ApplicationGroup.Development => "Development tools",
        ApplicationGroup.Security => "Security applications",
        ApplicationGroup.Utilities => "Utilities and components",
        _ => "Other applications",
    };

    public static string Glyph(ApplicationGroup group) => group switch
    {
        ApplicationGroup.Internet => "\uE774", ApplicationGroup.Office => "\uE8A5",
        ApplicationGroup.Media => "\uE8B9", ApplicationGroup.Games => "\uE7FC",
        ApplicationGroup.Development => "\uE943", ApplicationGroup.Security => "\uEA18",
        ApplicationGroup.Utilities => "\uE90F", _ => "\uE71D",
    };

    private static readonly (ApplicationGroup Group, string[] Names)[] Rules =
    [
        (ApplicationGroup.Utilities, ["AVG TuneUp", "7-Zip", "WinRAR", "PowerToys", "Shiva Assist", "IObit Uninstaller", "Revo Uninstaller", "Microsoft Visual C++", "Microsoft .NET Runtime", "Microsoft .NET Host", "Microsoft .NET Core Runtime", "Microsoft Windows Desktop Runtime", "Microsoft Edge WebView2"]),
        (ApplicationGroup.Internet, ["Google Chrome", "Microsoft Edge", "Mozilla Firefox", "Opera", "Brave", "Thunderbird"]),
        (ApplicationGroup.Office, ["Microsoft 365", "Microsoft Office", "LibreOffice", "OpenOffice", "Notion", "Obsidian", "Microsoft OneNote", "Microsoft Teams", "Zoom"]),
        (ApplicationGroup.Media, ["VLC", "Spotify", "Audacity", "OBS Studio", "Adobe Photoshop", "Adobe Premiere", "GIMP", "Blender", "Paint.NET"]),
        (ApplicationGroup.Games, ["Steam", "Epic Games Launcher", "GOG GALAXY", "Ubisoft Connect", "EA app", "Battle.net"]),
        (ApplicationGroup.Development, ["Microsoft Visual Studio", "Visual Studio Code", "Python", "Git", "Node.js", "Docker", "JetBrains", "IntelliJ", "Android Studio", "Microsoft .NET SDK"]),
        (ApplicationGroup.Security, ["Microsoft Defender", "Windows Security", "Bitdefender", "ESET", "Avast", "AVG AntiVirus", "AVG Internet Security", "Malwarebytes", "Norton", "Kaspersky"]),
    ];

    public static ApplicationGroup Suggest(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var rule in Rules)
        {
            if (rule.Names.Any(known => name.Equals(known, StringComparison.OrdinalIgnoreCase)
                || (name.StartsWith(known, StringComparison.OrdinalIgnoreCase) && name.Length > known.Length
                    && (char.IsWhiteSpace(name[known.Length]) || name[known.Length] is '(' or '-' or '.'))))
            {
                return rule.Group;
            }
        }

        return ApplicationGroup.Other;
    }
}

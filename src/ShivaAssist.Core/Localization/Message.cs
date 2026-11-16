using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShivaAssist.Core.Localization;

/// <summary>
/// A user-facing text: an English template plus values. The English template is also the translation key
/// (as in gettext), so the code stays readable and a missing translation falls back to English.
/// Placeholders: <c>{name}</c>, or with a format: <c>{free:gb}</c>, <c>{used:pct}</c>, <c>{when:date}</c>.
/// </summary>
[JsonConverter(typeof(MessageJsonConverter))]
public sealed class Message : IEquatable<Message>
{
    public Message(string template, params (string Name, object Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(template);
        Template = template;
        Args = args ?? [];
    }

    public string Template { get; }

    public IReadOnlyList<(string Name, object Value)> Args { get; }

    /// <summary>A fixed sentence needs no ceremony: <c>Title = "The PC was restarted recently"</c>.</summary>
    public static implicit operator Message(string template) => new(template);

    public static Message FromString(string template) => new(template);

    /// <summary>Text in the given language; unknown or missing translations fall back to English.</summary>
    public string Render(TextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return Fill(catalog.Translate(Template), catalog.Culture);
    }

    /// <summary>The English text, used in the JSON report and logs.</summary>
    public override string ToString() => Fill(Template, CultureInfo.InvariantCulture);

    public bool Equals(Message? other) => other is not null && ToString() == other.ToString();

    public override bool Equals(object? obj) => Equals(obj as Message);

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    /// <summary>Placeholder names in a template, used to check that translations keep every value.</summary>
    public static IReadOnlySet<string> Placeholders(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '{')
            {
                start = i;
            }
            else if (template[i] == '}' && start >= 0)
            {
                names.Add(template[(start + 1)..i]);
                start = -1;
            }
        }

        return names;
    }

    private string Fill(string template, CultureInfo culture)
    {
        if (Args.Count == 0 || !template.Contains('{', StringComparison.Ordinal))
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            var close = open < 0 ? -1 : template.IndexOf('}', open);
            if (open < 0 || close < 0)
            {
                result.Append(template, i, template.Length - i);
                break;
            }

            result.Append(template, i, open - i);
            var spec = template[(open + 1)..close];
            var colon = spec.IndexOf(':', StringComparison.Ordinal);
            var name = colon < 0 ? spec : spec[..colon];
            var format = colon < 0 ? null : spec[(colon + 1)..];
            var match = Args.FirstOrDefault(a => a.Name == name);
            result.Append(match.Name is null ? "{" + spec + "}" : Format(match.Value, format, culture));
            i = close + 1;
        }

        return result.ToString();
    }

    private static string Format(object value, string? format, CultureInfo culture) => (value, format) switch
    {
        (long bytes, "gb") => (bytes / 1e9).ToString(bytes >= 100e9 ? "0" : "0.0", culture) + " GB",
        (double fraction, "pct") => (fraction * 100).ToString("0", culture) + "%",
        (DateOnly date, "date") => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        (IFormattable formattable, _) => formattable.ToString(null, culture),
        _ => value.ToString() ?? string.Empty,
    };
}

/// <summary>Reports keep the contract's plain strings: a message is written as its English text.</summary>
public sealed class MessageJsonConverter : JsonConverter<Message>
{
    public override Message Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, Message value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value?.ToString());
    }
}

using System.Globalization;
using System.Text.Json;

namespace ShivaAssist.Core.Localization;

/// <summary>A language the application can show, as listed in its translation file.</summary>
public sealed record Language(string Code, string Name);

/// <summary>
/// Translations for one language, loaded from <c>Localization/{code}.json</c> (embedded). Each file maps the
/// English template to its translation and names its own language, so adding a language means adding one
/// file, no code. English needs no file: the templates are English.
/// </summary>
public sealed class TextCatalog
{
    public const string DefaultLanguage = "en";

    private readonly IReadOnlyDictionary<string, string> texts;

    private TextCatalog(Language language, IReadOnlyDictionary<string, string> texts)
    {
        Language = language;
        this.texts = texts;
        Culture = CultureInfo.GetCultureInfo(language.Code);
    }

    public static TextCatalog English { get; } = new(new Language(DefaultLanguage, "English"), new Dictionary<string, string>());

    public Language Language { get; }

    public CultureInfo Culture { get; }

    public IReadOnlyCollection<string> Keys => texts.Keys.ToList();

    /// <summary>English plus every embedded translation file, English first.</summary>
    public static IReadOnlyList<Language> Available() =>
        [English.Language, .. ResourceNames().Select(Load).Select(c => c.Language).OrderBy(l => l.Name, StringComparer.Ordinal)];

    /// <summary>The catalog for a language code; unknown codes give English.</summary>
    public static TextCatalog For(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return English;
        }

        var resource = ResourceNames().FirstOrDefault(r => r.EndsWith($".{code.ToLowerInvariant()}.json", StringComparison.Ordinal));
        return resource is null ? English : Load(resource);
    }

    public string Translate(string template) => texts.TryGetValue(template, out var text) ? text : template;

    /// <summary>Parses a translation file: <c>{ "language": { "code", "name" }, "texts": { English: translation } }</c>.</summary>
    public static TextCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        var language = root.GetProperty("language");
        var texts = root.GetProperty("texts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name, StringComparer.Ordinal);
        return new TextCatalog(new Language(language.GetProperty("code").GetString()!, language.GetProperty("name").GetString()!), texts);
    }

    private static IEnumerable<string> ResourceNames() =>
        typeof(TextCatalog).Assembly.GetManifestResourceNames().Where(n => n.Contains(".Localization.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal));

    private static TextCatalog Load(string resource)
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing resource {resource}.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}

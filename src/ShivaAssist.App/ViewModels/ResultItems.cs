using System.Windows.Media;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;

namespace ShivaAssist.App.ViewModels;

/// <summary>One finding as a card: layer 1 (title, advice) always, layer 2 and 3 (cause, evidence, ID) on request.</summary>
public sealed class FindingItem(Finding finding, Func<TextCatalog> catalog) : Observable
{
    private bool expanded;

    public Finding Finding { get; } = finding;

    public Severity Severity => Finding.Severity;

    public Category Category => Finding.Category;

    public string Badge => ReportText.SeverityName(Finding.Severity).Render(catalog());

    public string Title => Finding.Severity == Severity.Unknown && Finding.UnknownReason is { } reason
        ? $"{Finding.Title.Render(catalog())} ({ReportText.ReasonText(reason).Render(catalog())})"
        : Finding.Title.Render(catalog());

    public string? Advice => Finding.Severity == Severity.Healthy ? null : Finding.Recommendation?.Summary.Render(catalog());

    public bool HasAdvice => Advice is not null;

    public string? Cause => Finding.SuspectedCause is { } cause ? new Message("Possible cause: {cause}", ("cause", cause.Render(catalog()))).Render(catalog()) : null;

    public bool HasCause => Cause is not null;

    /// <summary>Technical evidence stays in English: it quotes Windows and is meant for technicians.</summary>
    public IReadOnlyList<string> Evidence => [.. Finding.Evidence.Select(e => $"{e.Observation}  [{e.Source}]")];

    public string Id => Finding.Id;

    public bool Expanded
    {
        get => expanded;
        set => Set(ref expanded, value);
    }

    public Brush Accent => Palette.For(Finding.Severity);

    public Brush Soft => Palette.Background(Finding.Severity);

    public void Refresh() => RefreshAll();
}

/// <summary>A dashboard tile: one category, its worst severity and how many findings need attention.</summary>
public sealed class CategoryTile(Category category, IReadOnlyList<Finding> findings, Func<TextCatalog> catalog) : Observable
{
    private bool selected;

    public Category Category { get; } = category;

    public string Name => ReportText.CategoryName(Category).Render(catalog());

    public Severity Worst { get; } = findings.MinBy(f => Core.Scanning.ScanRunner.SeverityRank(f.Severity))?.Severity ?? Severity.Healthy;

    public int Attention { get; } = findings.Count(f => f.Severity is Severity.Critical or Severity.Warning);

    public string Status => Attention > 0
        ? new Message("{count} to check", ("count", Attention)).Render(catalog())
        : ReportText.SeverityName(Worst == Severity.Information ? Severity.Healthy : Worst).Render(catalog());

    public Brush Accent => Palette.For(Worst);

    public bool Selected
    {
        get => selected;
        set => Set(ref selected, value);
    }

    public void Refresh() => RefreshAll();
}

using System.Windows.Media;
using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Reporting;
using ShivaAssist.Core.Repairs;

namespace ShivaAssist.App.ViewModels;

/// <summary>One finding as a card: layer 1 (title, advice) always, layer 2 and 3 (cause, evidence, ID) on request.</summary>
public sealed class FindingItem(Finding finding, Func<TextCatalog> catalog, RepairContext? repairs = null) : Observable
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

    /// <summary>Repair buttons; only in technician mode and only for repairs from the catalogue.</summary>
    public IReadOnlyList<RepairButton> Repairs => repairs is null
        ? []
        : [.. RepairCatalogue.For(Finding).Select(o => new RepairButton(o, this, repairs, catalog))];

    public bool HasRepairs => Repairs.Count > 0;

    public string? Outcome => repairs?.Outcome(Finding.Id);

    public bool HasOutcome => Outcome is not null;

    public Brush OutcomeColour => repairs?.OutcomeColour(Finding.Id) ?? Palette.Navy;

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

/// <summary>What finding cards need from the dashboard to offer repairs.</summary>
public sealed class RepairContext(Func<RepairButton, Task> run, Func<string, string?> outcome, Func<string, Brush> outcomeColour, Func<bool> busy)
{
    public Task RunAsync(RepairButton button) => run(button);

    public string? Outcome(string findingId) => outcome(findingId);

    public Brush OutcomeColour(string findingId) => outcomeColour(findingId);

    public bool Busy => busy();
}

/// <summary>One repair offered on a finding card.</summary>
public sealed class RepairButton(RepairOperation operation, FindingItem item, RepairContext context, Func<TextCatalog> catalog)
{
    public RepairOperation Operation { get; } = operation;

    public FindingItem Item { get; } = item;

    public string Label => Operation.Name.Render(catalog());

    public string Tooltip => Operation.Description.Render(catalog());

    public RelayCommand Command => new(async _ => await context.RunAsync(this), _ => !context.Busy);
}

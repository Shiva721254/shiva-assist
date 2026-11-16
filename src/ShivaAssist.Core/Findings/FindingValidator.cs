using System.Text.RegularExpressions;

namespace ShivaAssist.Core.Findings;

/// <summary>Checks a finding against the field rules of the finding contract (Appendix A).</summary>
public static partial class FindingValidator
{
    [GeneratedRegex("^[A-Z]+(-[A-Z]+)*-[0-9]{3}$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var errors = new List<string>();

        if (!IdPattern().IsMatch(finding.Id))
        {
            errors.Add($"id '{finding.Id}' does not match MODULE-TOPIC-NNN");
        }

        if (string.IsNullOrWhiteSpace(finding.Module))
        {
            errors.Add("module is empty");
        }

        if (string.IsNullOrWhiteSpace(finding.Title))
        {
            errors.Add("title is empty");
        }

        if (finding.Severity == Severity.Unknown)
        {
            if (finding.UnknownReason is null)
            {
                errors.Add("unknownReason is required when severity is unknown");
            }
        }
        else
        {
            if (finding.UnknownReason is not null)
            {
                errors.Add("unknownReason is only allowed when severity is unknown");
            }

            if (finding.Evidence.Count == 0)
            {
                errors.Add("evidence is required for any severity other than unknown");
            }
        }

        if (finding.Recommendation is { Operations.Count: > 0 })
        {
            errors.Add("operations must stay empty until the signed repair catalogue exists (stage 3)");
        }

        return errors;
    }
}

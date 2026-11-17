namespace ShivaAssist.Core.Repairs;

/// <summary>
/// Reads the repair audit log (one JSON result per line, oldest first) for the history page, and decides which
/// entries can still be undone: the repair has an undo operation, recorded its previous state, and was not
/// undone yet.
/// </summary>
public static class RepairHistory
{
    public static IReadOnlyList<RepairResult> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var results = new List<RepairResult>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (RepairJson.Deserialize<RepairResult>(line) is { } result)
                {
                    results.Add(result);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A damaged line (for example a write cut off by a power loss) must not hide the rest.
            }
        }

        results.Reverse(); // newest first
        return results;
    }

    public static bool CanUndo(RepairResult result, IReadOnlyList<RepairResult> history)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(history);
        return result.Completed
            && result.Rollback.Count > 0
            && RepairCatalogue.UndoFor(result.OperationId) is not null
            && !history.Any(h => h.Completed && h.Undoes == result.JobId);
    }

    public static bool WasUndone(RepairResult result, IReadOnlyList<RepairResult> history)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(history);
        return history.Any(h => h.Completed && h.Undoes == result.JobId);
    }
}

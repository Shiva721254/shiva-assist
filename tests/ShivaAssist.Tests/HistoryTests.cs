using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class HistoryTests
{
    private static string Line(RepairResult r) => RepairJson.Serialize(r).ReplaceLineEndings(" ");

    private static readonly RepairResult Startup = new()
    {
        JobId = "a1",
        OperationId = RepairCatalogue.DisableStartupPrograms,
        Completed = true,
        Rollback = ["HKCU-Run|ShivaTest Sync|none"],
        Verdict = RepairVerdict.Fixed,
    };

    [Fact]
    public void ParsesNewestFirstAndSkipsDamagedLines()
    {
        var bin = new RepairResult { JobId = "b2", OperationId = RepairCatalogue.EmptyRecycleBin, Completed = true };
        var history = RepairHistory.Parse([Line(Startup), "{ broken", string.Empty, Line(bin)]);
        Assert.Equal(["b2", "a1"], history.Select(h => h.JobId));
    }

    [Fact]
    public void OnlyRecordedRepairsWithAnUndoCanBeUndoneAndOnlyOnce()
    {
        var bin = new RepairResult { JobId = "b2", OperationId = RepairCatalogue.EmptyRecycleBin, Completed = true };
        IReadOnlyList<RepairResult> history = [Startup, bin];
        Assert.True(RepairHistory.CanUndo(Startup, history));
        Assert.False(RepairHistory.CanUndo(bin, history));

        var undo = new RepairResult { JobId = "c3", OperationId = RepairCatalogue.RestoreStartupPrograms, Completed = true, Undoes = "a1" };
        IReadOnlyList<RepairResult> after = [undo, Startup, bin];
        Assert.False(RepairHistory.CanUndo(Startup, after));
        Assert.True(RepairHistory.WasUndone(Startup, after));
    }

    [Fact]
    public void UndoOperationsAreNeverOfferedForFindingsAndNeedNoRestorePoint()
    {
        var restore = RepairCatalogue.Find(RepairCatalogue.RestoreStartupPrograms)!;
        Assert.True(restore.IsUndo);
        Assert.False(restore.NeedsRestorePoint);
        Assert.Same(restore, RepairCatalogue.UndoFor(RepairCatalogue.DisableStartupPrograms));
        Assert.Null(RepairCatalogue.UndoFor(RepairCatalogue.EmptyRecycleBin));
    }
}

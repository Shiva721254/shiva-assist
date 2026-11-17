using ShivaAssist.Core.Repairs;
using ShivaAssist.Core.Software;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class UninstallerTests : IDisposable
{
    private readonly string fixture = Path.Combine(Path.GetTempPath(), "shiva-uninstaller-test-" + Guid.NewGuid().ToString("N"));
    private string Install => Path.Combine(fixture, "App");
    private string Recovery => Path.Combine(fixture, "Recovery");

    public UninstallerTests() => Directory.CreateDirectory(Install);

    [Theory]
    [InlineData("MsiExec.exe /I{12345678-1234-1234-1234-1234567890ab}")]
    [InlineData("msiexec /x {12345678-1234-1234-1234-1234567890ab}")]
    [InlineData("\"C:\\Windows\\System32\\msiexec.exe\" /X{12345678-1234-1234-1234-1234567890ab}")]
    public void MsiMaintenanceCommandsProduceOnlyTheValidatedProductCode(string command)
    {
        var parsed = UninstallPolicy.ParseCommand(command);
        Assert.NotNull(parsed);
        Assert.Equal("{12345678-1234-1234-1234-1234567890AB}", parsed.ProductCode);
        Assert.Empty(parsed.Arguments);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Example\\uninstall.exe\" /uninstall")]
    [InlineData("C:\\Example\\uninstall.exe /remove")]
    public void NativeCommandsKeepTheExecutableSeparateFromArguments(string command)
    {
        var parsed = UninstallPolicy.ParseCommand(command);
        Assert.NotNull(parsed);
        Assert.EndsWith("uninstall.exe", parsed.Executable, StringComparison.Ordinal);
        Assert.Null(parsed.ProductCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("uninstall.exe")]
    [InlineData("C:\\Program Files\\App\\uninstall.exe /S")]
    [InlineData("cmd /c uninstall.exe")]
    [InlineData("https://example.com/uninstall.exe")]
    [InlineData("\\\\server\\app\\uninstall.exe")]
    [InlineData("C:\\App\\remove.cmd")]
    [InlineData("\"C:\\App\\uninstall.exe\"\ncalc.exe")]
    public void AmbiguousShellRelativeAndNetworkCommandsAreRejected(string? command) =>
        Assert.Null(UninstallPolicy.ParseCommand(command));

    [Theory]
    [InlineData("../outside.log")]
    [InlineData("cache/../../outside.log")]
    [InlineData("/outside.log")]
    [InlineData("C:\\outside.log")]
    [InlineData("cache.log:stream")]
    [InlineData("cache/./data.log")]
    [InlineData("cache./data.log")]
    [InlineData("cache /data.log")]
    public void UnsafeLeftoverPathsAreRejected(string path) => Assert.False(UninstallPolicy.IsSafeRelativePath(path));

    [Theory]
    [InlineData("settings.json")]
    [InlineData("saved-game.dat")]
    [InlineData("passwords.db")]
    [InlineData("documents.txt")]
    [InlineData("preferences.ini")]
    [InlineData("data.bak")]
    public void PersonalDataAndSettingsAreNeverCandidates(string path) => Assert.False(UninstallPolicy.IsDisposableFile(path));

    [Fact]
    public void CleanupRequiresSnapshotAndDoesNotOfferFilesCreatedOrChangedDuringUninstall()
    {
        File.WriteAllText(Path.Combine(Install, "old.log"), "before");
        File.WriteAllText(Path.Combine(Install, "app.dll"), "binary");
        File.WriteAllText(Path.Combine(Install, "settings.json"), "keep");
        var before = LeftoverFiles.Capture(Install);
        File.WriteAllText(Path.Combine(Install, "old.log"), "changed");
        File.WriteAllText(Path.Combine(Install, "new.log"), "new");

        var scan = LeftoverFiles.Scan(before);

        Assert.Equal("app.dll", Assert.Single(scan.Candidates).RelativePath);
        Assert.Equal(1, scan.ChangedFiles);
        Assert.True(scan.PreservedFiles >= 2);
    }

    [Fact]
    public void SelectedFilesAndEmptyFoldersCanBeCleanedAndRestoredWithoutTouchingPersonalData()
    {
        var cache = Directory.CreateDirectory(Path.Combine(Install, "cache")).FullName;
        File.WriteAllText(Path.Combine(cache, "trace.log"), "diagnostic log");
        File.WriteAllText(Path.Combine(Install, "settings.json"), "personal preference");
        var snapshot = LeftoverFiles.Capture(Install);
        var scan = LeftoverFiles.Scan(snapshot);
        var receipt = new LeftoverReceipt(snapshot, scan.Candidates);

        Assert.Equal(2, LeftoverFiles.Quarantine(receipt, Recovery));
        Assert.False(Directory.Exists(cache));
        Assert.True(File.Exists(Path.Combine(Install, "settings.json")));
        Assert.Single(Directory.GetFiles(Recovery));

        LeftoverFiles.Restore(receipt, Recovery);
        Assert.Equal("diagnostic log", File.ReadAllText(Path.Combine(cache, "trace.log")));
        Assert.Equal("personal preference", File.ReadAllText(Path.Combine(Install, "settings.json")));
        Assert.Empty(Directory.GetFiles(Recovery));
    }

    [Fact]
    public void ChangedFilesStopCleanupBeforeAnyMove()
    {
        var path = Path.Combine(Install, "trace.log");
        File.WriteAllText(path, "before");
        var snapshot = LeftoverFiles.Capture(Install);
        var receipt = new LeftoverReceipt(snapshot, LeftoverFiles.Scan(snapshot).Candidates);
        File.WriteAllText(path, "after");

        Assert.Throws<InvalidOperationException>(() => LeftoverFiles.Quarantine(receipt, Recovery));
        Assert.Equal("after", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Recovery));
    }

    [Fact]
    public void SelectingAFolderWithoutItsContentsStopsBeforeAnyMove()
    {
        File.WriteAllText(Path.Combine(Install, "trace.log"), "before");
        var snapshot = LeftoverFiles.Capture(Install);
        var folder = LeftoverFiles.Scan(snapshot).Candidates.Single(e => e.IsDirectory);
        Assert.Throws<InvalidOperationException>(() => LeftoverFiles.Quarantine(new(snapshot, [folder]), Recovery));
        Assert.True(File.Exists(Path.Combine(Install, "trace.log")));
    }

    [Fact]
    public void UndoNeverOverwritesAReinstalledProgramsFiles()
    {
        var path = Path.Combine(Install, "app.dll");
        File.WriteAllText(path, "old");
        var snapshot = LeftoverFiles.Capture(Install);
        var receipt = new LeftoverReceipt(snapshot, LeftoverFiles.Scan(snapshot).Candidates.Where(e => !e.IsDirectory).ToArray());
        LeftoverFiles.Quarantine(receipt, Recovery);
        File.WriteAllText(path, "new install");

        Assert.Throws<IOException>(() => LeftoverFiles.Restore(receipt, Recovery));
        Assert.Equal("new install", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Recovery));
    }

    [Fact]
    public void ALinkInsertedAfterTheScanCannotRedirectCleanup()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Install, "cache")).FullName;
        File.WriteAllText(Path.Combine(directory, "trace.log"), "before");
        var snapshot = LeftoverFiles.Capture(Install);
        var receipt = new LeftoverReceipt(snapshot, LeftoverFiles.Scan(snapshot).Candidates);
        var outside = Directory.CreateDirectory(Path.Combine(fixture, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "trace.log"), "must remain");
        Directory.Delete(directory, recursive: true);
        try
        {
            Directory.CreateSymbolicLink(directory, outside);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            return; // Windows needs Developer Mode or administrator rights for creating this fixture link.
        }

        Assert.Throws<IOException>(() => LeftoverFiles.Quarantine(receipt, Recovery));
        Assert.Equal("must remain", File.ReadAllText(Path.Combine(outside, "trace.log")));
        Directory.Delete(directory); // Remove just the link before fixture cleanup.
    }

    [Fact]
    public void ForgedPathsNotInTheSnapshotAndDuplicateSelectionsAreRejected()
    {
        File.WriteAllText(Path.Combine(Install, "trace.log"), "before");
        var snapshot = LeftoverFiles.Capture(Install);
        var file = LeftoverFiles.Scan(snapshot).Candidates.First(e => !e.IsDirectory);
        Assert.Throws<ArgumentException>(() => LeftoverFiles.Quarantine(new(snapshot, [file, file]), Recovery));
        Assert.Throws<ArgumentException>(() => LeftoverFiles.Quarantine(new(snapshot, [file with { RelativePath = "../outside.log" }]), Recovery));
    }

    [Fact]
    public void InterruptedCleanupCanBeUndoneAndLatestAuditResultReplacesThePendingEntry()
    {
        var pending = new RepairResult { JobId = "cleanup", OperationId = RepairCatalogue.CleanSoftwareLeftovers,
            Completed = true, Verdict = RepairVerdict.Pending, Rollback = ["receipt"] };
        var final = pending with { Verdict = RepairVerdict.Fixed };
        var history = RepairHistory.Parse([RepairJson.Serialize(pending), RepairJson.Serialize(final)]);
        Assert.Equal(RepairVerdict.Fixed, Assert.Single(history).Verdict);
        Assert.True(RepairHistory.CanUndo(pending, [pending]));
        Assert.Equal(RepairCatalogue.RestoreSoftwareLeftovers, RepairCatalogue.UndoFor(pending.OperationId)!.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(fixture))
        {
            Directory.Delete(fixture, recursive: true);
        }
    }
}

using ShivaAssist.Core.Cleanup;
using ShivaAssist.Core.Repairs;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class WaveBTests
{
    [Fact]
    public void WaveBQuickFixesAndShaderCacheExistInCatalogueAsLowRisk()
    {
        string[] ids =
        [
            RepairCatalogue.CleanShaderCache,
            RepairCatalogue.RestartExplorer,
            RepairCatalogue.RebuildIconCache,
            RepairCatalogue.ResetStoreCache,
        ];

        foreach (var id in ids)
        {
            var operation = RepairCatalogue.Find(id);
            Assert.NotNull(operation);
            Assert.Equal(RiskClass.Low, operation.Risk);
            Assert.False(operation.RestartRequired);
            Assert.False(operation.IsUndo);
            Assert.False(string.IsNullOrWhiteSpace(operation.Name.Template));
            Assert.False(string.IsNullOrWhiteSpace(operation.Description.Template));
        }

        Assert.Contains(CleanupAreas.All, area => area.OperationId == RepairCatalogue.CleanShaderCache);
    }

    [Fact]
    public void LargeFileFinderFindsAndSortsFilesAboveThresholdWithoutModifyingThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShivaAssist-LargeFileTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var nested = Path.Combine(root, "Subfolder");
            Directory.CreateDirectory(nested);
            var small = Path.Combine(root, "small.bin");
            var medium = Path.Combine(root, "medium.bin");
            var large = Path.Combine(nested, "large.bin");

            File.WriteAllBytes(small, new byte[128]);
            File.WriteAllBytes(medium, new byte[1024]);
            File.WriteAllBytes(large, new byte[4096]);

            var result = LargeFileFinder.Scan(
                [("Downloads", root), ("Downloads", root), ("Missing", Path.Combine(root, "does-not-exist"))],
                minimumBytes: 512,
                maxResults: 10,
                maxEntries: 100);

            Assert.False(result.Truncated);
            Assert.Equal(2, result.Files.Count);
            Assert.Equal("large.bin", result.Files[0].Name);
            Assert.Equal(4096, result.Files[0].SizeBytes);
            Assert.Equal("Downloads", result.Files[0].FolderLabel);
            Assert.Equal("medium.bin", result.Files[1].Name);
            Assert.Equal(1024, result.Files[1].SizeBytes);

            // Strictly read-only: every test file must still exist untouched.
            Assert.True(File.Exists(small));
            Assert.True(File.Exists(medium));
            Assert.True(File.Exists(large));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LargeFileFinderRespectsMaxEntriesTruncationAndMaxResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShivaAssist-LargeFileCap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 1; i <= 6; i++)
            {
                File.WriteAllBytes(Path.Combine(root, $"file{i}.bin"), new byte[i * 100]);
            }

            var topTwo = LargeFileFinder.Scan([("Documents", root)], minimumBytes: 100, maxResults: 2, maxEntries: 100);
            Assert.False(topTwo.Truncated);
            Assert.Equal(2, topTwo.Files.Count);
            Assert.Equal(600, topTwo.Files[0].SizeBytes);
            Assert.Equal(500, topTwo.Files[1].SizeBytes);

            var capped = LargeFileFinder.Scan([("Documents", root)], minimumBytes: 100, maxResults: 10, maxEntries: 2);
            Assert.True(capped.Truncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

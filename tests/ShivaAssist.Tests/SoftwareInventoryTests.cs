using ShivaAssist.Core.Software;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class SoftwareInventoryTests
{
    [Fact]
    public void InventoryRemovesBlankAndDuplicateEntriesButKeepsDifferentVersions()
    {
        InstalledProgram[] entries =
        [
            new(" Browser ", " Publisher ", " 1 ", 1024, null),
            new("Browser", "Publisher", "1", 1024, null),
            new("Browser", "Publisher", "2", null, null),
            new(" ", "", "", null, null),
            new("Alpha", "", "", -1, null),
        ];

        var programs = SoftwareInventory.Normalize(entries);

        Assert.Equal(3, programs.Count);
        Assert.Equal("Alpha", programs[0].Name);
        Assert.Null(programs[0].SizeBytes);
        Assert.Equal("1", programs[1].Version);
        Assert.Equal("2", programs[2].Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20260230")]
    [InlineData("17/11/2026")]
    public void InvalidOrMissingDatesRemainUnknown(string? value) => Assert.Null(SoftwareInventory.ParseInstallDate(value));

    [Fact]
    public void RegistryDateIsParsedWithoutDependingOnUserCulture() =>
        Assert.Equal(new DateOnly(2026, 11, 17), SoftwareInventory.ParseInstallDate("20261117"));
}

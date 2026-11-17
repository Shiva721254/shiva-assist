using ShivaAssist.Core.Software;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class PackageUpdateTests
{
    [Fact]
    public void ReadsUpdatesAfterLocalisedHeaderAndProgress()
    {
        var updates = PackageUpdates.Parse("""
            Downloading source...
            Naam                    Id                    Versie       Beschikbaar   Bron
            --------------------------------------------------------------------------------
            Example Browser         Example.Browser       1.0          2.0           winget
            Other App               Other.App             3.0          4.0           winget
            2 upgrades available.
            """);
        Assert.Equal(2, updates.Count);
        Assert.Equal("Example.Browser", updates[0].Id);
        Assert.Equal("1.0", updates[0].InstalledVersion);
        Assert.Equal("2.0", updates[0].AvailableVersion);
    }

    [Fact]
    public void TruncatedAmbiguousOrOtherSourceRowsAreNotActionable()
    {
        var updates = PackageUpdates.Parse("""
            Name           Id             Version   Available Source
            ---------------------------------------------------------
            Truncated      Vendor.Pack…   1         2         winget
            Duplicate      Vendor.App     1         2         winget
            Duplicate      Vendor.App     2         3         winget
            Store          Vendor.Store   1         2         msstore
            """);
        Assert.Empty(updates);
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("Vendor.App --all")]
    [InlineData("Vendor.App;command")]
    [InlineData("Vendor.App\n")]
    [InlineData("Vendor.App…")]
    public void RejectsUnsafeOrTruncatedIdentifiers(string id) => Assert.False(PackageUpdates.IsValidId(id));

    [Fact]
    public void VersionVerificationRequiresExactlyOneMatchingPackage()
    {
        const string output = "Name     Id           Version\n---------------------------------\nExample  Example.App  2.0";
        Assert.Equal("2.0", PackageUpdates.InstalledVersion(output, "Example.App"));
        Assert.Null(PackageUpdates.InstalledVersion(output, "Other.App"));
        Assert.Null(PackageUpdates.InstalledVersion(output + "\nExample  Example.App  3.0", "Example.App"));
    }
}

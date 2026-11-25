using System.Net;
using System.Text;
using ShivaAssist.Setup;
using Xunit;

namespace ShivaAssist.Tests;

public sealed class SetupTests
{
    [Theory]
    [InlineData(0x8664, "win-x64")]
    [InlineData(0xAA64, "win-arm64")]
    public void Selects_native_architecture(int machine, string expected) =>
        Assert.Equal(expected, InstallerPolicy.Architecture((ushort)machine));

    [Fact]
    public void Rejects_32_bit_machine() => Assert.Throws<InvalidOperationException>(() => InstallerPolicy.Architecture(0x14c));

    [Theory]
    [InlineData("1.3.6.1.4.1.311.97.990309390.766961637.194916062.941502583", true)]
    [InlineData("1.3.6.1.4.1.311.97.1.0.990309390", true)]
    [InlineData("1.3.6.1.4.1.311.97", false)]
    [InlineData("1.3.6.1.4.1.311.97.1", false)]
    [InlineData("1.3.6.1.5.5.7.3.3", false)]
    [InlineData("1.3.6.1.4.1.311.97.01.2", false)]
    [InlineData("1.3.6.1.4.1.311.97.1.2\n", false)]
    [InlineData("", false)]
    public void Validates_publisher_identity_oid(string oid, bool valid) => Assert.Equal(valid, InstallerPolicy.ValidIdentityOid(oid));

    [Fact]
    public void Manifest_is_fetched_from_the_assist_site() =>
        Assert.Equal("https://assist.shivacomputers.nl/downloads/shiva-assist/releases.json", InstallerPolicy.ManifestUrl);

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("255.255.65535", true)]
    [InlineData("256.1.0", false)]
    [InlineData("1.0.65536", false)]
    [InlineData("1.0.0 --bad", false)]
    [InlineData("1.0", false)]
    [InlineData("01.0.0", false)]
    [InlineData("1.0.0\n", false)]
    public void Validates_msi_version(string version, bool valid) => Assert.Equal(valid, InstallerPolicy.ValidVersion(version));

    [Theory]
    [InlineData("http://shivacomputers.nl/downloads/shiva-assist/a.msi")]
    [InlineData("https://evil.example/downloads/shiva-assist/a.msi")]
    [InlineData("https://shivacomputers.nl.evil.example/downloads/shiva-assist/a.msi")]
    [InlineData("https://shivacomputers.nl:444/downloads/shiva-assist/a.msi")]
    [InlineData("https://user@shivacomputers.nl/downloads/shiva-assist/a.msi")]
    [InlineData("https://shivacomputers.nl/downloads/shiva-assist/a.msi?url=bad")]
    [InlineData("https://shivacomputers.nl/downloads/shiva-assist/a.msi#bad")]
    [InlineData("https://shivacomputers.nl/downloads/shiva-assist/../a.msi")]
    [InlineData("https://shivacomputers.nl/downloads/shiva-assist/%61.msi")]
    [InlineData("https://shivacomputers.nl/downloads/shiva-assist/a.exe")]
    public void Rejects_untrusted_package_addresses(string address) => Assert.Throws<InvalidOperationException>(() => InstallerPolicy.PackageUri(address));

    [Fact]
    public void Manifest_requires_both_architectures_and_verification_fields()
    {
        var x64 = Package("win-x64"); var arm = Package("win-arm64");
        Assert.Same(arm, InstallerPolicy.Select("shiva.assist.release/1", "0.1.0", [x64, arm], "win-arm64"));
        Assert.Throws<InvalidOperationException>(() => InstallerPolicy.Select("shiva.assist.release/1", "0.1.0", [x64, x64], "win-x64"));
        arm.AppSha256 = "bad";
        Assert.Throws<InvalidOperationException>(() => InstallerPolicy.Select("shiva.assist.release/1", "0.1.0", [x64, arm], "win-x64"));
        arm.AppSha256 = new string('a', 64); arm.Size = InstallerPolicy.MaximumPackageBytes + 1;
        Assert.Throws<InvalidOperationException>(() => InstallerPolicy.Select("shiva.assist.release/1", "0.1.0", [x64, arm], "win-x64"));
    }

    [Fact]
    public async Task Download_preserves_exact_bytes_and_reports_completion()
    {
        var bytes = Encoding.UTF8.GetBytes("installer payload");
        using var client = Client(HttpStatusCode.OK, bytes);
        var package = Package("win-x64"); package.Size = bytes.Length;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msi");
        try
        {
            var percent = 0;
            await InstallerDownloads.DownloadAsync(client, package, path, p => percent = p, CancellationToken.None);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None));
            Assert.Equal(100, percent);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public async Task Download_rejects_size_mismatch(long size)
    {
        using var client = Client(HttpStatusCode.OK, Encoding.ASCII.GetBytes("payload"));
        var package = Package("win-x64"); package.Size = size;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msi");
        try { await Assert.ThrowsAsync<IOException>(() => InstallerDownloads.DownloadAsync(client, package, path, _ => { }, CancellationToken.None)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Manifest_rejects_redirects_and_oversized_content()
    {
        using var redirect = Client(HttpStatusCode.Redirect, []);
        await Assert.ThrowsAsync<HttpRequestException>(() => InstallerDownloads.ManifestAsync(redirect, CancellationToken.None));
        using var oversized = Client(HttpStatusCode.OK, new byte[65537]);
        await Assert.ThrowsAsync<IOException>(() => InstallerDownloads.ManifestAsync(oversized, CancellationToken.None));
    }

    [Fact]
    public void Hash_detects_changed_payload()
    {
        using var abc = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", InstallerDownloads.Hash(abc));
        Assert.False(InstallerPolicy.ValidDigest(new string('a', 64) + "\n"));
    }

    private static SetupPackage Package(string architecture) => new()
    {
        Architecture = architecture, Url = "https://shivacomputers.nl/downloads/shiva-assist/a.msi",
        Sha256 = new string('a', 64), AppSha256 = new string('b', 64), Size = 7,
    };

    private static HttpClient Client(HttpStatusCode status, byte[] bytes) => new(new Handler(status, bytes));
    private sealed class Handler(HttpStatusCode status, byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
    }
}

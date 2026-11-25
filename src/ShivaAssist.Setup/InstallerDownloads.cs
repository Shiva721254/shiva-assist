#pragma warning disable CA2016, CA1835, CA1872 // Shared net48 source: these modern framework overloads are unavailable to setup.
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ShivaAssist.Setup;

internal static class InstallerDownloads
{
    public static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 32 };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    }

    public static async Task<byte[]> ManifestAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        cancellationToken = timeout.Token;
        using var response = await client.GetAsync(InstallerPolicy.ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); // Redirects and errors are never followed or treated as packages.
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > 65536) throw new IOException("The release information is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public static async Task DownloadAsync(HttpClient client, SetupPackage package, string destination,
        Action<int> progress, CancellationToken cancellationToken)
    {
        var uri = InstallerPolicy.PackageUri(package.Url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        cancellationToken = timeout.Token;
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long announced && announced != package.Size)
            throw new IOException("The download size does not match the release information.");
        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        var buffer = new byte[65536];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
        {
            received += count;
            if (received > package.Size || received > InstallerPolicy.MaximumPackageBytes)
                throw new IOException("The download exceeded its expected size.");
            await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
            progress((int)(received * 100 / package.Size));
        }
        if (received != package.Size) throw new IOException("The download was incomplete. Please try again.");
        output.Flush(flushToDisk: true);
    }

#pragma warning disable CA1850 // net48 does not provide SHA256.HashData; the same source is tested under net10.
    public static string Hash(Stream stream)
    {
        stream.Position = 0;
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
#pragma warning restore CA1850
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ShivaAssist.Windows.Infrastructure;

/// <summary>
/// Runs built-in Windows tools (PowerShell, DISM, wevtutil) by their full System32 path, never via PATH, so a
/// look-alike program in another folder cannot be started. Output is captured; nothing is written to disk.
/// </summary>
internal static class ToolRunner
{
    public static string System32(string relative) => Path.Combine(Environment.SystemDirectory, relative);

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            // Time limit reached: stop the tool and everything it started.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }
    }
}

/// <summary>
/// Runs a read-only PowerShell script and returns its JSON output. CIM errors are translated to the exceptions
/// the scan runner understands: access denied → <see cref="UnauthorizedAccessException"/>, missing class or
/// namespace → <see cref="PlatformNotSupportedException"/>.
/// </summary>
internal static class PowerShell
{
    private const string Prologue = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        try {
        """;

    private const string Epilogue = """

        } catch {
            $code = "$($_.Exception.NativeErrorCode)$($_.FullyQualifiedErrorId)"
            $kind = if ($code -match 'AccessDenied|UnauthorizedAccess|0x80041003') { 'access-denied' }
                    elseif ($code -match 'InvalidClass|InvalidNamespace|NotSupported|CommandNotFound|0x80041010|0x8004100e') { 'unsupported' }
                    else { 'error' }
            [pscustomobject]@{ shivaError = $kind; detail = $_.Exception.GetType().Name } | ConvertTo-Json -Compress
        }
        """;

    public static async Task<JsonElement> QueryAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Prologue + script + Epilogue));
        var (exit, output, _) = await ToolRunner.RunAsync(
            ToolRunner.System32(@"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            cancellationToken);

        var json = output.Trim();
        if (json.Length == 0)
        {
            throw new InvalidOperationException($"PowerShell returned no data (exit {exit}).");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("shivaError", out var error))
        {
            throw error.GetString() switch
            {
                "access-denied" => new UnauthorizedAccessException("Access denied."),
                "unsupported" => new PlatformNotSupportedException("Not available on this PC."),
                _ => new InvalidOperationException("Query failed: " + (root.TryGetProperty("detail", out var d) ? d.GetString() : "unknown")),
            };
        }

        return root;
    }

    /// <summary>A JSON value that PowerShell may emit as a single object or an array, always as a list.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
    }

    public static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    public static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    public static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

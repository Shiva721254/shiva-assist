# Run ONLY after signing all payload executables, rebuilding MSIs, then signing both MSIs and setup.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,4})$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$PublisherCertificateSha256,
    [string]$PayloadRoot = 'publish',
    [string]$PackageRoot = 'installer-output',
    [string]$SetupPath = 'src/ShivaAssist.Setup/bin/Release/net48/ShivaAssistSetup.exe'
)
$ErrorActionPreference = 'Stop'
function Assert-Signature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) { throw "Missing valid signature: $Path" }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($signature.SignerCertificate.RawData))
    if ($digest -ne $PublisherCertificateSha256) { throw "Unexpected signing certificate: $Path" }
}
Assert-Signature $SetupPath
$packages = foreach ($rid in 'win-x64', 'win-arm64') {
    foreach ($name in 'ShivaAssist.exe', 'ShivaAssistAgent.exe', 'ShivaAssistCli.exe') {
        Assert-Signature (Join-Path $PayloadRoot "$rid/$name")
    }
    $msi = Join-Path $PackageRoot "ShivaAssist-$Version-$rid.msi"
    Assert-Signature $msi
    # The bootstrapper repeats trust, hash and product-identity validation at installation time.
    $check = Start-Process $SetupPath -ArgumentList "--verify-package `"$((Resolve-Path $msi).Path)`" --sha256 $((Get-FileHash $msi -Algorithm SHA256).Hash)" -Wait -PassThru
    if ($check.ExitCode) { throw 'Setup rejected the signed release package or its embedded publisher pin' }
    @{
        architecture = $rid
        url = "https://shivacomputers.nl/downloads/shiva-assist/ShivaAssist-$Version-$rid.msi"
        sha256 = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant()
        appSha256 = (Get-FileHash (Join-Path $PayloadRoot "$rid/ShivaAssist.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
        size = (Get-Item $msi).Length
    }
}
@{ schema = 'shiva.assist.release/1'; version = $Version; packages = @($packages) } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PackageRoot 'releases.json') -Encoding utf8NoBOM
Copy-Item $SetupPath (Join-Path $PackageRoot 'ShivaAssistSetup.exe')

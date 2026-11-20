# Run ONLY after signing all payload executables, rebuilding MSIs, then signing both MSIs and setup.
# Produces the folder to upload to assist.shivacomputers.nl and the entry for the website's data/releases.json:
#
#   <OutputRoot>/website-upload/public/downloads/shiva-assist/ShivaAssist-<version>-win-x64.msi
#   <OutputRoot>/website-upload/public/downloads/shiva-assist/ShivaAssist-<version>-win-arm64.msi
#   <OutputRoot>/website-upload/public/downloads/shiva-assist/ShivaAssistSetup-<version>.exe
#   <OutputRoot>/website-upload/public/downloads/shiva-assist/ShivaAssistSetup.exe   (always the newest)
#   <OutputRoot>/website-upload/public/downloads/shiva-assist/releases.json          (upload LAST)
#   <OutputRoot>/website-upload/data-releases-entry.json                             (add to data/releases.json)
#
# -Rehearsal skips every signature check so the upload can be practised before signing exists. A rehearsal
# release is refused by the public setup by design and must never be uploaded to the live site.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,4})$')][string]$Version,
    [ValidatePattern('^(1\.3\.6\.1\.4\.1\.311\.97(\.(0|[1-9][0-9]{0,9})){2,8})?$')][string]$PublisherIdentityOid = '',
    [switch]$Rehearsal,
    [string]$PayloadRoot = 'publish',
    [string]$PackageRoot = 'installer-output',
    [string]$SetupPath = 'src/ShivaAssist.Setup/bin/Release/net48/ShivaAssistSetup.exe',
    [string]$OutputRoot = 'release-output',
    [string]$BaseUrl = 'https://assist.shivacomputers.nl/downloads/shiva-assist'
)
$ErrorActionPreference = 'Stop'
if (-not $Rehearsal -and -not $PublisherIdentityOid) { throw 'PublisherIdentityOid is required for a real release.' }

function Assert-Signature([string]$Path) {
    if ($Rehearsal) { return }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) { throw "Missing valid signature: $Path" }
    if (-not $signature.TimeStamperCertificate) { throw "Signature has no timestamp: $Path" }
    $usages = @($signature.SignerCertificate.Extensions |
        Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
    if ($usages -notcontains '1.3.6.1.5.5.7.3.3' -or $usages -notcontains $PublisherIdentityOid) {
        throw "Signed by an unexpected identity: $Path"
    }
}

$target = Join-Path $OutputRoot 'website-upload/public/downloads/shiva-assist'
New-Item -ItemType Directory -Force $target | Out-Null
Assert-Signature $SetupPath
$packages = foreach ($rid in 'win-x64', 'win-arm64') {
    foreach ($name in 'ShivaAssist.exe', 'ShivaAssistAgent.exe', 'ShivaAssistCli.exe') {
        Assert-Signature (Join-Path $PayloadRoot "$rid/$name")
    }
    $msi = Join-Path $PackageRoot "ShivaAssist-$Version-$rid.msi"
    Assert-Signature $msi
    $hash = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $Rehearsal) {
        # The bootstrapper repeats trust, hash and product-identity validation at installation time.
        $check = Start-Process $SetupPath -ArgumentList "--verify-package `"$((Resolve-Path $msi).Path)`" --sha256 $hash" -Wait -PassThru
        if ($check.ExitCode) { throw 'Setup rejected the signed release package or its embedded publisher identity' }
    }
    Copy-Item $msi $target
    [ordered]@{
        architecture = $rid
        url = "$BaseUrl/ShivaAssist-$Version-$rid.msi"
        sha256 = $hash
        appSha256 = (Get-FileHash (Join-Path $PayloadRoot "$rid/ShivaAssist.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
        size = (Get-Item $msi).Length
    }
}
[ordered]@{ schema = 'shiva.assist.release/1'; version = $Version; packages = @($packages) } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $target 'releases.json') -Encoding utf8NoBOM

$setupName = "ShivaAssistSetup-$Version.exe"
Copy-Item $SetupPath (Join-Path $target $setupName)
Copy-Item $SetupPath (Join-Path $target 'ShivaAssistSetup.exe')
$setup = Get-Item (Join-Path $target $setupName)

# Entry for assist-site data/releases.json (schema shiva.assist.releases/1.0), validated there by Release::fromArray.
[ordered]@{
    version = $Version
    channel = if ($Rehearsal) { 'preview' } else { 'stable' }
    published = (Get-Date -AsUTC).ToString('yyyy-MM-dd')
    minimumWindows = '10.0.19045'
    architectures = @('x64', 'arm64')
    downloadUrl = "$BaseUrl/$setupName"
    sha256 = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    sizeBytes = $setup.Length
    notes = [ordered]@{ en = "Shiva Assist $Version."; nl = "Shiva Assist $Version." }
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputRoot 'website-upload/data-releases-entry.json') -Encoding utf8NoBOM

if ($Rehearsal) {
    'REHEARSAL: these files are NOT signed. The public setup refuses them by design. Never upload them to the live site.' |
        Set-Content (Join-Path $OutputRoot 'website-upload/REHEARSAL-NOT-FOR-CUSTOMERS.txt') -Encoding utf8NoBOM
}
Get-ChildItem -Recurse (Join-Path $OutputRoot 'website-upload') | Where-Object { -not $_.PSIsContainer } |
    ForEach-Object { Write-Output "::notice title=Release file::$($_.FullName.Substring((Resolve-Path $OutputRoot).Path.Length + 1)) ($($_.Length) bytes)" }

# Run after publishing the three self-contained executables for both architectures.
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$PayloadRoot = 'publish',
    [string]$OutputRoot = 'installer-output'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,4})$') { throw 'Invalid MSI version' }
$parts = $Version.Split('.')
if ([int]$parts[0] -gt 255 -or [int]$parts[1] -gt 255 -or [int]$parts[2] -gt 65535) { throw 'MSI version outside supported range' }
New-Item -ItemType Directory -Force $OutputRoot | Out-Null
$output = (Resolve-Path $OutputRoot).Path
$tool = Join-Path $output 'tools'
dotnet tool install wix --version 6.0.2 --tool-path $tool
if ($LASTEXITCODE) { throw 'WiX installation failed' }
foreach ($rid in 'win-x64', 'win-arm64') {
    $payload = (Resolve-Path (Join-Path $PayloadRoot $rid)).Path
    foreach ($name in 'ShivaAssist.exe', 'ShivaAssistAgent.exe', 'ShivaAssistCli.exe') {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw "Missing payload: $rid/$name" }
    }
    $arch = if ($rid -eq 'win-x64') { 'x64' } else { 'arm64' }
    $upgrade = if ($rid -eq 'win-x64') { '{4A67A327-3B49-44B7-BBBB-776A5EAD0D68}' } else { '{04D720CC-7994-4C3C-A69A-CA52BBA20741}' }
    $msi = Join-Path $output "ShivaAssist-$Version-$rid.msi"
    & "$tool/wix.exe" build "$PSScriptRoot/Package.wxs" -arch $arch -d "ReleaseVersion=$Version" -d "UpgradeCode=$upgrade" -d "PayloadDir=$payload" -o $msi
    if ($LASTEXITCODE) { throw "MSI build failed for $rid" }
    (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content "$msi.sha256" -Encoding ascii
}

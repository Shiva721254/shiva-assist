# Disposable Windows CI runner / VM only. Installs, repairs and removes Shiva Assist itself.
[CmdletBinding()]
param([string]$PackageRoot = 'installer-output')
$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path 'src/ShivaAssist.Setup/bin/Release/net48/ShivaAssistSetupLab.exe').Path
function Run-Setup([string]$Msi) {
    $path = (Resolve-Path $Msi).Path
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash
    $process = Start-Process $setup -ArgumentList "--local-package `"$path`" --sha256 $hash --test-install" -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) { throw "Setup integration failed: $($process.ExitCode)" }
}
function Product-Code([string]$Msi) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase((Resolve-Path $Msi).Path, 0)
    $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductCode'")
    $view.Execute()
    $record = $view.Fetch()
    return $record.StringData(1)
}
$application = Join-Path $env:ProgramFiles 'Shiva Computers/Shiva Assist/ShivaAssist.exe'
$shortcut = Join-Path $env:ProgramData 'Microsoft/Windows/Start Menu/Programs/Shiva Assist/Shiva Assist.lnk'
$markerFolder = Join-Path $env:LOCALAPPDATA 'Shiva Assist'
New-Item -ItemType Directory -Force $markerFolder | Out-Null
$marker = Join-Path $markerFolder "setup-test-$([guid]::NewGuid()).txt"
Set-Content $marker 'Preserve customer history and preferences'
$product = $null
try {
    $msi = Join-Path $PackageRoot 'ShivaAssist-0.1.0-win-x64.msi'
    $product = Product-Code $msi
    # Wrong hash must fail without installing anything.
    $bad = Start-Process $setup -ArgumentList "--local-package `"$((Resolve-Path $msi).Path)`" --sha256 $('0' * 64) --test-install" -Wait -PassThru
    if ($bad.ExitCode -eq 0 -or (Test-Path $application)) { throw 'Changed package was not rejected' }
    Run-Setup $msi
    foreach ($name in 'ShivaAssist.exe', 'ShivaAssistAgent.exe', 'ShivaAssistCli.exe') {
        $installed = Join-Path (Split-Path $application) $name
        if ((Get-FileHash $installed).Hash -ne (Get-FileHash "publish/win-x64/$name").Hash) { throw "Installed file mismatch: $name" }
    }
    if (-not (Test-Path $shortcut)) { throw 'Missing Start menu shortcut' }
    Run-Setup $msi # Same release can be run again / repaired.
    & "$PSScriptRoot/Build-Packages.ps1" -Version '0.1.1' -OutputRoot "$PackageRoot/upgrade"
    $upgrade = Join-Path $PackageRoot 'upgrade/ShivaAssist-0.1.1-win-x64.msi'
    Run-Setup $upgrade
    $product = Product-Code $upgrade
    $uninstall = Start-Process "$env:SystemRoot/System32/msiexec.exe" -ArgumentList "/x $product /qn /norestart" -Wait -PassThru
    if ($uninstall.ExitCode -notin 0, 3010) { throw "MSI uninstall failed: $($uninstall.ExitCode)" }
    $product = $null
    if ((Test-Path $application) -or (Test-Path $shortcut)) { throw 'Uninstall left installed application or shortcut' }
    if (-not (Test-Path $marker)) { throw 'Uninstall removed customer data' }
    Write-Output 'Installer integration passed: bad hash rejection, install, payload verification, shortcut, reinstall, upgrade, uninstall and data preservation.'
}
finally {
    if ($product) { Start-Process "$env:SystemRoot/System32/msiexec.exe" -ArgumentList "/x $product /qn /norestart" -Wait | Out-Null }
    Remove-Item $marker -ErrorAction SilentlyContinue
}

# Run only inside the disposable Windows VM, from an administrator PowerShell.
param([ValidateSet('Normal', 'Failure', 'Restart')][string]$Scenario = 'Normal')
$ErrorActionPreference = 'Stop'
$computer = Get-CimInstance Win32_ComputerSystem
if (($computer.Manufacturer + ' ' + $computer.Model) -notmatch 'VirtualBox|VMware|Virtual Machine|QEMU|KVM|Xen') {
    throw 'This fixture may only be installed in a disposable virtual machine.'
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in an administrator PowerShell inside the VM.'
}
$project = Join-Path $PSScriptRoot 'UninstallerFixture\UninstallerFixture.csproj'
$buildOutput = Join-Path $env:TEMP ('shiva-uninstall-fixture-' + [Guid]::NewGuid().ToString('N'))
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $buildOutput
if ($LASTEXITCODE) { throw 'Fixture build failed. Install the .NET 10 SDK inside the VM.' }
$target = Join-Path $env:ProgramFiles 'Shiva Assist Uninstaller Fixture'
if (Test-Path $target) { throw 'Fixture folder already exists. Revert the VM snapshot before repeating this test.' }
New-Item -ItemType Directory $target | Out-Null
Copy-Item (Join-Path $buildOutput 'ShivaUninstallFixture.exe') $target
New-Item -ItemType Directory (Join-Path $target 'cache') | Out-Null
New-Item -ItemType Directory (Join-Path $target 'empty-folder') | Out-Null
Set-Content (Join-Path $target 'cache\trace.log') 'Disposable test log'
Set-Content (Join-Path $target 'cache\old.tmp') 'Disposable temporary data'
Set-Content (Join-Path $target 'settings.json') '{"preference":"keep me"}'
Set-Content (Join-Path $target 'personal-document.txt') 'Must never be offered for cleanup'
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
$entry = $registry.CreateSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ShivaAssistUninstallFixture')
try {
    $entry.SetValue('DisplayName', 'Shiva Assist Uninstaller Fixture')
    $entry.SetValue('DisplayVersion', '1.0.0')
    $entry.SetValue('Publisher', 'Shiva Computers test fixture')
    $entry.SetValue('InstallLocation', $target)
    $entry.SetValue('UninstallString', '"' + (Join-Path $target 'ShivaUninstallFixture.exe') + '" --uninstall')
    $entry.SetValue('FixtureScenario', $Scenario)
} finally {
    $entry.Dispose()
    $registry.Dispose()
}
Write-Host "Fixture installed ($Scenario). Open Shiva Assist --technician, refresh programs, select the fixture and uninstall it."

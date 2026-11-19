<#
.SYNOPSIS
    Creates realistic PC problems inside a TEST virtual machine, so Shiva Assist repairs can be tested end to end.

.DESCRIPTION
    Run in an elevated PowerShell inside the VM (VirtualBox, Parallels, Hyper-V or VMware), after taking a snapshot.
    Restore the snapshot to undo, or run with -Undo. The script refuses to run on a physical PC.

    Scenarios:
      Storage   - nearly full C: drive (about 3 GB free), 2 GB in the Recycle Bin, 1 GB of temp files older than 7 days
                  -> STORAGE-SPACE-001 (critical) with the cleanup repairs
      Startup   - 10 extra programs that start with Windows
                  -> PERF-STARTUP-001 (warning) with the startup repair
      Services  - print spooler, Windows Time, Windows Audio and Windows Search switched off, clock 30 minutes behind
                  -> PRINT-SPOOLER-001, CLOCK-SYNC-001, AUDIO-SERVICE-001, SEARCH-SERVICE-001 (all in Fix it all)
      All       - everything above

    Parallels: switch off time synchronisation with the Mac first (VM configuration -> Options -> More Options ->
    Time: Do not sync), otherwise Parallels Tools corrects the clock before Shiva Assist can find it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Inject-Faults.ps1 -Scenario Services
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Inject-Faults.ps1 -Undo
#>
[CmdletBinding()]
param(
    [ValidateSet('Storage', 'Startup', 'Services', 'All')]
    [string]$Scenario = 'All',
    [switch]$Undo
)

$ErrorActionPreference = 'Stop'

# --- Safety: virtual machines only, administrator only ---
$model = (Get-CimInstance Win32_ComputerSystem).Model
if ($model -notmatch 'VirtualBox|Virtual Machine|VMware|Parallels') {
    throw "This is not a virtual machine (model: $model). Fault injection only runs inside a test VM."
}
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in PowerShell as administrator.'
}

$GB = 1GB
$work = Join-Path $env:SystemDrive 'ShivaTest'
New-Item -ItemType Directory -Force -Path $work | Out-Null

function New-FilledFile([string]$Path, [long]$Bytes) {
    & "$env:SystemRoot\System32\fsutil.exe" file createnew $Path $Bytes | Out-Null
}

function Add-StorageFaults {
    Add-Type -AssemblyName Microsoft.VisualBasic

    # 1. Recycle Bin: 4 x 500 MB (small files so they fit within the Recycle Bin size limit).
    Write-Host 'Filling the Recycle Bin with 2 GB...'
    1..4 | ForEach-Object {
        $file = Join-Path $work "old-video-$_.mp4"
        New-FilledFile $file (500MB)
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($file, 'OnlyErrorDialogs', 'SendToRecycleBin')
    }

    # 2. Temp files older than 7 days: 100 x 10 MB in the user's temp folder.
    Write-Host 'Creating 1 GB of old temp files...'
    $temp = Join-Path $env:TEMP 'ShivaTest-old-installer'
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    $old = (Get-Date).AddDays(-30)
    1..100 | ForEach-Object {
        $file = Join-Path $temp "setup-cache-$_.tmp"
        New-FilledFile $file (10MB)
        $item = Get-Item $file
        $item.CreationTime = $old
        $item.LastWriteTime = $old
    }

    # 3. Fill the drive until about 3 GB is free (critical is below 4 GB).
    $free = (Get-PSDrive ($env:SystemDrive.TrimEnd(':'))).Free
    $fill = $free - 3 * $GB
    if ($fill -gt 0) {
        Write-Host ("Filling C: with {0:N1} GB..." -f ($fill / $GB))
        New-FilledFile (Join-Path $work 'disk-filler.bin') $fill
    }
    $free = (Get-PSDrive ($env:SystemDrive.TrimEnd(':'))).Free
    Write-Host ("Free space on C: now {0:N1} GB." -f ($free / $GB))
}

function Add-StartupFaults {
    Write-Host 'Adding 10 startup programs...'
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $names = 'ShivaTest Updater', 'ShivaTest Helper', 'ShivaTest Sync', 'ShivaTest Tray', 'ShivaTest Agent',
             'ShivaTest Launcher', 'ShivaTest Notifier', 'ShivaTest Cloud', 'ShivaTest Scheduler', 'ShivaTest Monitor'
    foreach ($name in $names) {
        # A harmless command that exits immediately, so logging in stays quick.
        New-ItemProperty -Path $run -Name $name -Value "$env:SystemRoot\System32\cmd.exe /c exit" -PropertyType String -Force | Out-Null
    }
}

function Add-ServiceFaults {
    Write-Host 'Switching off the print spooler...'
    Stop-Service -Name Spooler -Force -ErrorAction SilentlyContinue
    Set-Service -Name Spooler -StartupType Disabled

    Write-Host 'Switching off Windows Time and setting the clock 30 minutes back...'
    Stop-Service -Name W32Time -Force -ErrorAction SilentlyContinue
    Set-Service -Name W32Time -StartupType Disabled
    Set-Date -Date (Get-Date).AddMinutes(-30) | Out-Null

    Write-Host 'Switching off Windows Audio...'
    Stop-Service -Name Audiosrv -Force -ErrorAction SilentlyContinue
    Set-Service -Name Audiosrv -StartupType Disabled

    Write-Host 'Switching off Windows Search...'
    Stop-Service -Name WSearch -Force -ErrorAction SilentlyContinue
    Set-Service -Name WSearch -StartupType Disabled
}

function Undo-Faults {
    Write-Host 'Restoring services, clock, startup entries and test files...'
    Set-Service -Name Spooler -StartupType Automatic; Start-Service -Name Spooler -ErrorAction SilentlyContinue
    Set-Service -Name Audiosrv -StartupType Automatic; Start-Service -Name Audiosrv -ErrorAction SilentlyContinue
    & "$env:SystemRoot\System32\sc.exe" config WSearch start= delayed-auto | Out-Null; Start-Service -Name WSearch -ErrorAction SilentlyContinue
    Set-Service -Name W32Time -StartupType Manual; Start-Service -Name W32Time -ErrorAction SilentlyContinue
    & "$env:SystemRoot\System32\w32tm.exe" /resync /force | Out-Null
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    (Get-Item $run).Property | Where-Object { $_ -like 'ShivaTest *' } | ForEach-Object { Remove-ItemProperty -Path $run -Name $_ }
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $env:TEMP 'ShivaTest-old-installer') -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Undo) {
    Undo-Faults
    Write-Host 'Test faults removed.' -ForegroundColor Green
    return
}

if ($Scenario -in 'Storage', 'All') { Add-StorageFaults }
if ($Scenario -in 'Startup', 'All') { Add-StartupFaults }
if ($Scenario -in 'Services', 'All') { Add-ServiceFaults }

Write-Host ''
Write-Host 'Done. Now open Shiva Assist, start the check-up and click Fix it all.' -ForegroundColor Green
Write-Host 'Afterwards: restore the snapshot, or run this script again with -Undo.'

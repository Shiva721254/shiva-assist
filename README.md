# Shiva Assist

**A read-only health check for Windows PCs, by Shiva Computers.**
It finds real problems, explains them in plain language and says what to do next. Stage 1 changes nothing on the PC.

Product page and downloads: **https://assist.shivacomputers.nl**

| | |
|---|---|
| Status | Stage 1 (Milestone 1): read-only diagnostic core, console app |
| Runs on | Windows 10 and 11, x64. No installation, no .NET needed (self-contained `ShivaAssist.exe`) |
| Built with | C# / .NET 10, no external packages in the product |
| Specification | *Shiva Assist PRA v2.0* (architecture), *Shiva Utilities Product Strategy v2.0*, *20 Windows problems in the Netherlands* (research) |

## What it checks

Each module turns measured data into findings in the shared contract `shiva.assist.finding/1.0` (severity, confidence, evidence, possible cause, advice). A check that cannot decide reports **Unknown with a reason**, never "healthy".

| Module | Research problem | Finding IDs | Data source (read-only) |
|---|---|---|---|
| Windows version and lifecycle | 2 · updates and unsupported systems | `OS-VERSION-001`, `OS-UPTIME-001` | Registry; Microsoft lifecycle table per version and edition |
| Updates | 2 | `UPDATE-RESTART-001`, `UPDATE-RECENT-001` | Reboot-pending registry keys, `Get-HotFix` |
| Windows integrity (admin) | 2, 11 | `WIN-INTEGRITY-001` | `DISM /CheckHealth /English` |
| Security | 3 · malware and unsafe settings | `SECURITY-AV-001`, `SECURITY-FIREWALL-001` | Windows Security Center, firewall profiles |
| Network | 4 · Wi-Fi and internet | `NETWORK-CONNECT-001` | Adapter → IP/DHCP → gateway → DNS → Windows connectivity test |
| Storage | 5 · full drive; 18 · disk health | `STORAGE-SPACE-001/002`, `STORAGE-HEALTH-001` | Drive space, `MSFT_PhysicalDisk` health |
| Memory | 1 · slow PC | `MEMORY-CAPACITY-001`, `MEMORY-USAGE-001`, `MEMORY-COMMIT-001` | `Win32_OperatingSystem` (incl. commit charge) |
| Processor | 1 | `CPU-LOAD-001` | Three load samples, one second apart |
| Startup programs | 1 | `PERF-STARTUP-001` | Run keys and Startup folders, minus entries switched off in Task Manager |
| Devices and drivers | 10 · drivers, USB, missing hardware | `DEVICE-PROBLEM-001`, `DEVICE-DISABLED-001` | Device Manager problem codes |
| Battery | 14 · battery | `BATTERY-WEAR-001` | Design vs full-charge capacity |
| Stability | 7 · app crashes; 11 · freezes and blue screens | `SYSTEM-SHUTDOWN-001`, `SYSTEM-BUGCHECK-001`, `HARDWARE-WHEA-001`, `APP-CRASH-001` | System/Application event logs, last 14 days |
| Scan privileges | — | `SCAN-PRIVILEGE-001` | Process token |

This is the research report's **phase 1 read-only MVP** (performance, updates, security posture, connectivity, storage, hardware inventory, lifecycle), plus stability and battery checks that need no extra risk.

## Use it

```powershell
ShivaAssist.exe              # scan, show results, save the report
ShivaAssist.exe --details    # also show evidence and possible causes
ShivaAssist.exe --help
```

Right-click → **Run as administrator** for the complete scan (the system file check needs it). Reports are saved as JSON in `Documents\Shiva Assist\Reports`. They contain no computer name, user name, file paths or serial numbers.

Exit codes: `0` nothing needs attention · `2` warnings or critical findings · `1` invalid option.

## Safety rules (stage 1)

- **Read-only.** No setting, file, service or registry value is changed. The only file written is the report.
- Only built-in Windows tools, started by their full `System32` path: PowerShell (CIM queries), DISM `/CheckHealth`, `wevtutil`.
- Every check has a time limit and is stopped when it runs out; one failing check never stops the scan.
- No fear words, no invented problems: every finding other than Unknown carries evidence. A single busy moment is not reported as a fault.
- The only network traffic is Windows' own connectivity test (`www.msftconnecttest.com`), the same request Windows itself makes.

## Develop

Requires the .NET 10 SDK. On Windows:

```powershell
dotnet build ShivaAssist.slnx
dotnet test tests/ShivaAssist.Tests
dotnet run --project src/ShivaAssist.Cli -- --details
dotnet publish src/ShivaAssist.Cli -c Release -o publish   # → publish\ShivaAssist.exe
```

| Project | Role |
|---|---|
| `ShivaAssist.Core` | Finding contract, scan runner, report, **all diagnostic rules** (platform-neutral, fully tested) |
| `ShivaAssist.Windows` | Read-only data sources: registry, CIM, DISM, event logs, networking |
| `ShivaAssist.Cli` | Console host for stage 1 (`ShivaAssist.exe`) |
| `ShivaAssist.Tests` | Rules, runner, contract and the research scenarios (runs on Windows and Linux) |

Each module is a `SnapshotCheck<T>`: a Windows source collects a snapshot, and a pure `Evaluate` function decides. New modules follow the same pattern; register them in `WindowsPlatform.CreateChecks()`.

CI (GitHub Actions, `windows-latest`) builds, tests, publishes the single-file exe, runs a real scan on the runner, checks the report schema and the 3-minute limit, and computes the SHA-256 for the release manifest.

## How it fits the Shiva Computers sites

| Site | Role for Shiva Assist |
|---|---|
| **assist.shivacomputers.nl** | Product page, download, `releases.json` manifest (version, URL, SHA-256) and `/api/v1/releases/latest` for update checks (stage 4) |
| **rescue.shivacomputers.nl** | Shiva Rescue OS: for PCs that no longer start (the offline counterpart; same finding style) |
| **shivacomputers.nl** | Company site: contact and remote help, linked from the scan's "Need a hand?" advice |

Planned modules from the *Shiva Utilities* strategy (storage manager, network tools, app manager, hardware monitor) are added as modules inside this application, not as separate programs, so they share the same core, report and download page.

## Roadmap

1. **Diagnostic foundation** (this release): read-only scan, JSON report, console
2. Dashboard: WPF window, categories, Dutch and English, report export
3. Repair engine: signed repair catalogue, approval per repair, rollback and verification
4. Website and API: download, update check, consented report upload
5–8. Offline recovery, reinstallation, licensing, advanced features (see the architecture document)

© Shiva Computers. All rights reserved.

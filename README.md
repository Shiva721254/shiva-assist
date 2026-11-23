# Shiva Assist

**Native Windows diagnosis, verified repair and maintenance software by Shiva Computers.**

> **Development / pre-release snapshot — 24 November 2026.** Branch `codex/software-inventory`, commit `3ee6cd9080693a04e895b67123732c12a21b1eb3`, version **0.1.14**. **CI #53 failed** (250 tests pass, 1 fails: `ServicesTests.AllRunningIsHealthyWithoutRepairs`); packaging and install stages were skipped. No signed website installer has been verified. `main` is 40 commits behind this feature branch. Consult `CLAUDE.md`, `HANDOFF.md` and `AI_CONTINUATION_SPEC.md` before continuing.

**Customer experience:** Start the check-up → Fix it all → Review results. The native WPF dashboard has bilingual EN/NL interface, readable finding cards, prominent one-click safe automatic repairs and a graphical Fixed / Improved / needs-attention summary. Individual choices, software removal and restarts use separate customer interactions. Detailed evidence and PDF/JSON exports remain available.

**Technology:** C#/.NET 10, Windows WPF, separate pure Core checks, Windows sources, elevated allowlisted repair Agent, CLI, net48 bootstrapper, architecture-specific x64/ARM64 MSI packaging.

**Covered areas:** OS/updates/security, system integrity, networking and Wi-Fi services, printer/audio/search/time services, disks and SSD wear, CPU/memory, crash/stability history, battery, startup apps, installed software, WinGet update, conservative uninstall/leftovers, measured cleanup, scheduled read-only check-up and Quick Assist launcher. Catalog contains **23 operations including two undo actions**; not all are batch eligible.

**Safety:** Unknown ≠ Healthy; a command exiting successfully ≠ verified Fixed; selective user actions and restarts are never silently bundled. Protect customer data, all credentials and BitLocker recovery keys. Never implement registry-cleaner-style or fake performance claims.

**Current blocker:** [CI run #53](https://github.com/Shiva721254/shiva-assist/actions/runs/44704360055) has 250 passed and one failing service test. Diagnose source/fixture before changing code, then obtain green complete CI for the actual source SHA.

**Documentation navigation:** [AI agent instructions](CLAUDE.md) · [detailed architecture and 23-operation handoff](HANDOFF.md) · [AI continuation specification](AI_CONTINUATION_SPEC.md) · [version history](CHANGELOG.md) · [installer/release rules](tools/installer/README.md).

---
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

**`ShivaAssist.exe`** is the dashboard window: start the scan, see the categories at a glance, read each finding with its advice, open *Details* for the evidence, and save the report. Choose English or Nederlands at the top right; the choice is remembered.

**`ShivaAssistCli.exe`** is the console version for technicians and automated checks:

```powershell
ShivaAssistCli.exe              # scan, show results, save the report
ShivaAssistCli.exe --details    # also show evidence and possible causes
ShivaAssistCli.exe --lang nl    # results in Dutch
ShivaAssistCli.exe --help
```

The dashboard asks for Windows administrator permission when it starts, before opening the scan setup.
Approve the UAC prompt to continue. The console version can still run with ordinary rights; start it as
administrator for the complete scan. Reports are saved as JSON in `Documents\Shiva Assist\Reports`.
They contain no computer name, user name, file paths or serial numbers.

Exit codes: `0` nothing needs attention · `2` warnings or critical findings · `1` invalid option.

## Safety rules for read-only scans (historical Stage 1; repairs additionally subject to catalogue)

- **Read-only.** No system setting, file, service or registry value is changed. The only things written are the report (when you save it) and the chosen language (`HKCU\\Software\\Shiva Computers\\Shiva Assist`).
- Only built-in Windows tools, started by their full `System32` path: PowerShell (CIM queries), DISM `/CheckHealth`, `wevtutil`.
- Every check has a time limit and is stopped when it runs out; one failing check never stops the scan.
- No fear words, no invented problems: every finding other than Unknown carries evidence. A single busy moment is not reported as a fault.
- The only network traffic is Windows' own connectivity test (`www.msftconnecttest.com`), the same request Windows itself makes.

## Historical Stage 3a repair subset (full current catalogue in HANDOFF.md)

Start `ShivaAssist.exe --technician` to see repair buttons on findings that have one. Every repair is a transaction: **explain → ask → repair → check again → log**, and ends as *Fixed*, *Unresolved* or *Inconclusive*. Plan: `claude/shiva-assist-repair-plan.md` in the project.

| Repair | ID | Risk | For |
|---|---|---|---|
| Empty the Recycle Bin | `DISK.RECYCLEBIN.EMPTY` | Low | `STORAGE-SPACE-001` |
| Delete temporary files older than 7 days (user's `AppData\Local\Temp` only, skips files in use, never follows links) | `DISK.TEMP.CLEAN` | Low | `STORAGE-SPACE-001` |
| Remove old Windows update files (`DISM /StartComponentCleanup`, admin) | `DISK.COMPONENTS.CLEANUP` | Low | `STORAGE-SPACE-001` |
| Switch off selected startup programs (Task Manager's own marker, reversible; previous value kept in the log) | `STARTUP.PROGRAMS.DISABLE` | Low | `PERF-STARTUP-001` |
| Repair Windows system files: DISM /RestoreHealth then sfc /scannow; restore point first, charger required | `WIN.SYSTEMFILES.REPAIR` | Medium | `WIN-INTEGRITY-001` (repairable) |

Repairs that need administrator rights run in **`ShivaAssistAgent.exe`**: one Windows permission prompt per job, it accepts only catalogue operations and validates every input again, then exits. Every repair is logged in `%LOCALAPPDATA%\Shiva Assist\repair-log.jsonl` (no personal data).

## Software manager

### Workspaces and application groups

The five main navigation items now have focused cards on the right. Overview has shortcuts to the check-up,
startup apps, installed apps and help. PC health separates hardware/storage, performance, Windows/security,
network and startup apps. Repairs has focused cleanup/Windows/network sections. Updates and apps separates
the grouped inventory, uninstaller, program updates, Windows Update and optional driver updates. History has
all repairs, available undo actions and help. Section selection is retained when returning to a workspace.
Health cards show problem/unknown counts after a scan; findings are grouped below their category headings.

Installed apps have suggested Internet, Office, Media, Games, Development, Security, Utilities and Other groups,
category counts, filters, grouped rows and category icons. These are conservative name-based display suggestions;
unrecognised programs remain Other. Categories are not safety verdicts and never authorise uninstall or updates.

**PC health → Startup apps** reads Run keys/Startup folders and recent Windows Diagnostics-Performance events.
Rows show enabled/disabled state, the latest reported application duration and its timestamp where available,
and comparison bars. Windows-only event rows have no switch-off action. Measurements are associated with a
registration only when its executable filename matches uniquely; shortcuts and ambiguous matches remain
unmeasured. Missing/disabled logs, missing timings and malformed events never become estimated milliseconds or
health scores. The last 14 days are queried with a 200-event cap; reports are historical, not live profiling or
predicted savings, and durations must not be added together. Scheduled-task/Store/service startup is not an
exhaustive part of the registration list. No background service is disabled or put to sleep.

Technicians can turn off an enabled registered startup entry after approval. The executor revalidates the
registration and checks the resulting marker; the existing audit/History undo restores the previous state.
Refresh the main PC check-up afterwards to update its aggregate findings. **Refresh startup apps** updates the
timing/registration view independently. Windows CI also opens the WPF workspaces to check navigation, grouping
and binding errors; manual appearance and keyboard checks remain useful on the customer's display.

Open **Updates and apps** to search the programs registered in Windows. Expand **Check program updates** to query
the WinGet community source. App Installer must be available. If source agreements need review, run
`winget list --source winget` in a normal Windows terminal and review the prompt, then check again.

In technician mode, select an update and choose **Update selected program**. Approval includes the program
name and old/new versions. Save your work and close that program before agreeing. The publisher's installer
may show Windows permission and licence prompts. Shiva Assist verifies the installed version and writes
the outcome in History. Missing or ambiguous WinGet results are reported as unverified. Store and portable
programs may be absent from the registry inventory. This update check makes an explicit internet request.

### Integrated uninstaller

In technician mode, click **Uninstaller**, select a program in the inventory and choose **Uninstall selected
program**. The confirmation identifies the program/publisher and executable. A restore point is required before
running its normal registered uninstaller. MSI product GUIDs run through System32 `msiexec /x /norestart`;
native desktop EXE uninstallers must be full, unambiguous paths inside Program Files. Scripts, network paths,
per-user EXEs and unsupported registrations use the explicit **Open Windows uninstaller instead** fallback.
No uninstaller is downloaded and no arbitrary shell command is executed.

When removal is verified, choose **Check removal and scan leftovers**. The app reviews only a recorded,
isolated installation folder, comparing it with a snapshot taken before uninstall. It offers unchanged
`.exe`, `.dll`, `.pdb`, `.log`, `.tmp`, `.cache` files up to 64 MB each and eligible empty folders. Other files,
settings, profiles, registry keys, shared locations, links and changed/new files are preserved. AppData-wide
search and installation monitoring are not implemented. Missing, shared or overly large snapshots disable
cleanup rather than guessing. Nothing starts selected; selecting a folder selects its listed contents.

**Clean selected leftovers** asks separately, moves the files into administrator-protected recovery storage
and removes only selected empty folders. Recovery copies still consume disk space; no reclaimed-space claim
is made. History provides **Undo**, which verifies hashes and refuses to overwrite existing files. Undoing
cleanup does not reinstall an application. A journal is saved before changes so interrupted cleanup can be
recovered. Removal sessions persist across app restarts; a pending Windows restart blocks cleanup.

Try the disposable VM fixture first: [Windows manual checklist](tools/test-vm/UNINSTALLER-TEST.md).
CI also tests the native fixture's cancellation, failure, pending restart, registry verification, persistent
session, cleanup and recovery. These integration checks bypass restore-point creation; check that separately
in the VM using the actual application.

## Languages

English is the default; Dutch (`nl`) is included. Every user-facing text is an English template (`new Message("…{free:gb}…", ("free", bytes))`); `src/ShivaAssist.Core/Localization/<code>.json` maps it to the translation and names its language. **Adding a language = adding one file**, no code. `python tools/check_translations.py` (also run in CI) fails when a text has no translation or a placeholder is missing. Technical evidence stays in English on purpose: it quotes Windows and is meant for technicians.

## Develop

Requires the .NET 10 SDK. On Windows:

```powershell
dotnet build ShivaAssist.slnx
dotnet test tests/ShivaAssist.Tests
dotnet run --project src/ShivaAssist.App                   # dashboard
dotnet run --project src/ShivaAssist.Cli -- --details      # console
dotnet publish src/ShivaAssist.App -c Release -r win-x64 -o publish   # → publish\ShivaAssist.exe
```

| Project | Role |
|---|---|
| `ShivaAssist.Core` | Finding contract, scan runner, report, **all diagnostic rules** (platform-neutral, fully tested) |
| `ShivaAssist.Windows` | Read-only data sources: registry, CIM, DISM, event logs, networking |
| `ShivaAssist.App` | Dashboard window (WPF, `ShivaAssist.exe`) |
| `ShivaAssist.Cli` | Console version (`ShivaAssistCli.exe`) |
| `ShivaAssist.Agent` | Elevated repair helper (`ShivaAssistAgent.exe`) |
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

## Historical roadmap (see HANDOFF.md for current priorities)

1. **Diagnostic foundation** (this release): read-only scan, JSON report, console
2. **Dashboard** (in progress): WPF window, categories, Dutch and English, report export
3. **Repair engine** (3a done): catalogue, approval per repair, elevated helper, verification, audit log; 3b/3c add network, Defender, DISM/SFC, Update reset, restore points
4. Website and API: download, update check, consented report upload
5–8. Offline recovery, reinstallation, licensing, advanced features (see the architecture document)

© Shiva Computers. All rights reserved.

## Small web installer

The new bootstrapper and signed release workflow are documented in [tools/installer/README.md](tools/installer/README.md). CI provides an unsigned **shiva-assist-installer-lab** for VM testing. Public distribution requires a configured trusted signing certificate and hosted release files; it is not yet live on the website.

## Current repair catalogue IDs (v0.1.14)

The following is the source-defined **23-operation** list, including two Undo operations, **not** a list of 23 automatic repairs. For exact risk, eligibility, flags and verification see `src/ShivaAssist.Core/Repairs/RepairCatalogue.cs`.

1. `DISK.RECYCLEBIN.EMPTY`
2. `DISK.TEMP.CLEAN`
3. `DISK.COMPONENTS.CLEANUP`
4. `STARTUP.PROGRAMS.DISABLE`
5. `WIN.SYSTEMFILES.REPAIR`
6. `DISK.UPDATECACHE.CLEAN`
7. `NET.DNS.FLUSH`
8. `NET.ADDRESS.RENEW`
9. `SECURITY.DEFENDER.UPDATE`
10. `UPDATE.RESTART.NOW`
11. `UPDATE.COMPONENTS.RESET`
12. `STARTUP.PROGRAMS.RESTORE` (Undo)
13. `SOFTWARE.PACKAGE.UPDATE`
14. `SOFTWARE.PROGRAM.UNINSTALL`
15. `SOFTWARE.LEFTOVERS.CLEAN`
16. `SOFTWARE.LEFTOVERS.RESTORE` (Undo)
17. `PRINT.SPOOLER.RESET`
18. `TIME.SYNC.REPAIR`
19. `DISK.BROWSERCACHE.CLEAN`
20. `AUDIO.SERVICE.REPAIR`
21. `SEARCH.SERVICE.REPAIR`
22. `NET.WIFI.START`
23. `NET.STACK.RESET`

## Verification and public release status (24 November 2026)

Run `python tools/check_translations.py`, `dotnet build ShivaAssist.slnx --configuration Release`, `dotnet test tests/ShivaAssist.Tests --configuration Release`. At CI run #53 translations and build succeeded, 250/251 xUnit tests passed, and later Windows integration/installer/smoke steps were skipped. Re-run on a corrected SHA. `release.yml` supports rehearsal or Azure-signed artifacts, **not** automatic deployment. Public code signing, certified publisher identity, clean installed x64/ARM64 validation, secure website upload and end-to-end public download remain unfinished. The bootstrapper must reject unsigned/wrong-publisher payloads; rehearsal files must not be distributed.

## Next AI session

Read `AI_CONTINUATION_SPEC.md`; inspect remote HEAD and local dirty work, reproduce the specific service-state failing test, decide whether the fixture or eligibility code is incorrect, preserve Unknown semantics, request source-change authorization, then apply a minimal correction and verify CI. Continue useful Windows detection/repair/verification rather than commercial release first.

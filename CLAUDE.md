# Shiva Assist handoff

Continue on `codex/software-inventory`. Fetch the latest GitHub changes and inspect local work before editing. Read `HANDOFF.md` for project background and conventions. Use the current code and this file for the latest state when older notes differ.

Shiva Assist is a C#/.NET Windows desktop app using WPF. Keep English and Dutch UI text in sync. The customer flow is **Start the check-up → Fix it all → review the outcome**. All scan areas start selected. The user initiates the scan, and one click on Fix it all starts supported automatic repairs. Keep the batch free of checklists and per-repair approval dialogs. Actions that require an explicit choice, such as selecting startup apps, and actions that restart Windows are handled separately. Count a repair as fixed only when the verification supports it.

After a scan, a Fix it all hero card stays at the top of every page: problem count in the severity colour, an expandable "What Fix it all will do" list, the largest button in the app (`HeroButton` style in `App.xaml`) and live progress while the batch runs. Keep it the single most prominent action. Main buttons are at least 46 px high, secondary buttons 40 px.

Overview places scan options on the left and the large Start the check-up button on the right. It shows the last Fix it all batch with fixed, improved, and attention or verification counts. Keep technical details under Details and avoid duplicating navigation. Repair and cleanup lives under PC health. Optimize has its own sidebar entry and supports cleaning old temporary files without a scan.

The working branch also includes installed app inventory and supported uninstall with recoverable leftover cleanup, startup controls, repair history, and recommended Windows updates plus supported app updates after one confirmation. Third-party installers can still prompt. Optional drivers are separate.

## Last verified build

- Version **0.1.3**, source commit `34186aea46ba8503f4d5981e0e689bbe6f980a26`.
- [Passing Windows CI run #41](https://github.com/Shiva721254/shiva-assist/actions/runs/44292506582)
- [Lab installer ZIP](https://github.com/Shiva721254/shiva-assist/actions/runs/44292506582/artifacts/14423011045)
- Extract the entire ZIP and run `src/ShivaAssist.Setup/bin/Release/net48/ShivaAssistSetupLab.exe`. After Windows administrator approval, setup selects and installs the matching bundled MSI.
- The enlarged button's exact sizing in a live VM and real Windows/app updates still need manual verification. If old per-repair prompts appear, confirm which EXE or Start menu shortcut is running and check its version.
- Run `python tools/check_translations.py` and relevant builds and tests. Verify CI for the specific source commit before sharing a new installer.

## Since the last verified build (0.1.4, needs CI and a VM check)

- Fix it all hero, larger buttons, HeroButton style also on Start the check-up.
- Printing check `PRINT-SPOOLER-001` (`Core/Modules/PrintCheck.cs`, `Windows/Sources/PrintSource.cs`) and repair `PRINT.SPOOLER.RESET` (included in Fix it all). VM test: stop the Print Spooler (`Stop-Service Spooler`) or pause a printer with a queued job older than 30 minutes, scan, Fix it all, expect Fixed.
- WorkspaceChecks (CI) now also runs a read-only scan and checks the hero.

- 0.1.5 UI/UX: finding cards with severity icons and directly visible repair buttons, Overview journey (Check-up → Fix it all → Review), larger sidebar, footer busy indicator, F5/F1. Robustness: single instance (mutex `Local\ShivaAssist.App`), close confirmation while busy, global crash guard writing `crash-log.txt` (`App/CrashLog.cs`).

- 0.1.6: Fix it all skips repairs that already ran on a finding since the last scan (`triedRepairs`) and says when nothing safe is left; "already clean" bucket for cleanups that removed nothing. New checks: clock (`ClockCheck`, `TimeSource`, repair `TIME.SYNC.REPAIR`) and disk wear (`STORAGE-WEAR-001` from `Get-StorageReliabilityCounter`, no repair by design). VM tests: `Set-Date (Get-Date).AddMinutes(-30)` then scan → Fix it all → expect Fixed; `Stop-Service Spooler` likewise.

- 0.1.7: Optimize page "Free up space" (`CleanupViewModel`, `Core/Cleanup/CleanupAreas.cs`, `Windows/Repairs/CleanupMeasurement.cs`): measured preview per area, checkboxes, one big clean button; browser cache cleanup limited to fixed cache folders (`BrowserCaches`).

- 0.1.8: `ServicesCheck` (sound AUDIO-SERVICE-001, search SEARCH-SERVICE-001) with service repairs, UPDATE-FAILED-001 from WindowsUpdateClient events 19/20, NET.STACK.RESET. VM tests: `Stop-Service Audiosrv -Force; Set-Service Audiosrv -StartupType Disabled`, `Set-Service WSearch -StartupType Disabled; Stop-Service WSearch -Force`, then scan → Fix it all → expect Fixed.

- Test machine from 19 Nov 2026: Windows 11 in Parallels Desktop on the owner's 2019 Intel MacBook Pro (10-day trial; x64 Windows, so the x64 build/MSI applies). `tools/test-vm/Inject-Faults.ps1` supports Parallels, has a `Services` scenario (spooler, time, audio, search, clock 30 min back) and `-Undo`. In Parallels set Options → More Options → Time to "Do not sync" before the clock test.

- 0.1.9: Get help section (`HelpViewModel.cs` partial): contact, monthly check-up (`Windows/Maintenance/MonthlyCheckup.cs`, scheduled task, CLI writes monthly-report.json, Overview note), battery report (`BatteryReport.cs`), Shiva Rescue link, remote-help request. Remote control itself (MeshCentral) is not integrated yet.
- 0.1.10: "Start Quick Assist" button on the remote help card (`ms-quick-assist:`, Store page fallback). Shiva Assist never shares the screen itself; consent stays in Quick Assist.
- 0.1.11: `release.yml` (rehearsal via tag `rehearsal-*`; signed needs Azure Artifact Signing secrets/variables, see `tools/installer/README.md`). Setup pins the signing identity EKU `PublisherIdentityOid` (not a cert hash: certs renew daily); manifest on assist.shivacomputers.nl.

## Next development work

Prioritize repair and maintenance functionality. Review the current checks and repair catalog, then extend a useful Windows problem that can be detected, repaired, and verified. Use automated checks where practical so minor UI changes do not require repeated manual VM reinstalls. Public distribution on ShivaComputers.nl is a later milestone and still requires code signing, hosting, and an end-to-end download test.

## Wi-Fi service increment (candidate, CI pending)

Adds NETWORK-WIFI-SERVICE-001 and NET.WIFI.START; starts only a stopped automatic WlanSvc on a confirmed enabled physical wireless adapter. No startup-type or network settings changes. Disabled services are not automatically repaired. Service verification does not establish internet connectivity.

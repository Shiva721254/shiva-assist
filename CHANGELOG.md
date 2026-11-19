# Changelog

## Unreleased

- 0.1.12: fixes from the Parallels test.
  - Printer check: printers are now counted from the registry. Windows cannot list printers while the Print Spooler is
    stopped, so a stopped spooler was reported as "No printers are set up" and PRINT-SPOOLER-001 never appeared.
  - Overview headline: "1 problem found" / "1 problem remains" instead of "1 problems".
- 0.1.11: release pipeline for the website download.
  - New workflow `Release` (`.github/workflows/release.yml`): builds, tests, signs with Azure Artifact Signing (OIDC),
    packages both MSIs and the setup, and produces the `website-upload` folder for assist.shivacomputers.nl plus the entry
    for the site's `data/releases.json`. Rehearsal mode (tag `rehearsal-*`) runs everything except signing.
  - Setup trust: pins the Artifact Signing identity EKU (`PublisherIdentityOid`) instead of one certificate's SHA-256,
    because Artifact Signing certificates renew daily. Still requires a valid, timestamped Authenticode signature.
  - Setup reads the release manifest from `https://assist.shivacomputers.nl/downloads/shiva-assist/releases.json`.
- 0.1.10: Start Quick Assist from Get help.
  - The remote help card has a green "Start Quick Assist" button that opens Windows' built-in Quick Assist
    (`ms-quick-assist:`); the customer chooses "Get help" and types the technician's code. If Quick Assist was
    removed, the Microsoft Store page opens instead. Shiva Assist itself never shares the screen.
- 0.1.9: Get help page and monthly check-up.
  - History and help → Get help now has five cards: contact, monthly check-up (switch on/off), battery report,
    "Windows does not start any more?" (Shiva Rescue) and remote help (request; consent per job is explained).
  - Monthly check-up (optional, off by default): a scheduled task "\\Shiva Computers\\Shiva Assist monthly
    check-up" runs the read-only CLI scan every four weeks in the background (S4U, normal rights) and saves
    `monthly-report.json`; Overview shows its result until a new scan is made. Nothing is repaired automatically.
  - Battery report: Windows' powercfg /batteryreport, opened in the browser.
- 0.1.8: four more problems that are detected, repaired and verified.
  - No sound: AUDIO-SERVICE-001 when the Windows audio services are stopped or disabled; AUDIO.SERVICE.REPAIR
    switches them back to automatic and restarts them (in Fix it all).
  - Search finds nothing: SEARCH-SERVICE-001 when Windows Search is stopped or disabled; SEARCH.SERVICE.REPAIR sets
    it back to delayed-automatic and starts it (in Fix it all).
  - Updates keep failing: UPDATE-FAILED-001 from Windows Update events (failed twice or more in 30 days and never
    installed afterwards); offers the existing Windows Update reset (in Fix it all).
  - Network: NET.STACK.RESET (netsh winsock reset + int ip reset, Medium with restore point, restart needed)
    offered after the quick network repairs; stays out of Fix it all because it needs a restart.
- 0.1.7: Free up space with a preview (roadmap step 3). The Optimize page measures, read-only, what each area
  would free (old temporary files, browser cache, Recycle Bin, Windows Update downloads and crash dumps), shows
  the size per area with a checkbox, and cleans only the ticked areas with one large button, then measures again.
  New catalogue repair DISK.BROWSERCACHE.CLEAN: only the cache folders of Edge, Chrome and Firefox (passwords,
  cookies, history and bookmarks are never touched); also part of the disk-space repairs in Fix it all.
- 0.1.6: honest Fix it all, clock and disk wear.
  - Fix it all no longer offers the same repairs again after they ran on a finding (until the next scan). When
    nothing safe is left, the card says so ("{n} problems remain · Nothing more to fix automatically") and points
    to the cards or a technician. Cleanups that found nothing count as "already clean", not as failures, and the
    footer shows the real outcome instead of "Repairs finished".
  - New check CLOCK-SYNC-001: compares the clock with time.windows.com (w32tm, one sample); two minutes or more
    off, or time sync switched off, is repaired by TIME.SYNC.REPAIR (re-enable W32Time, resync) and verified by
    measuring again. Included in Fix it all.
  - New finding STORAGE-WEAR-001 from the disk's own SMART/NVMe counters (wear, temperature, power-on hours,
    uncorrectable errors). Critical for uncorrectable errors or SSD wear >= 90 %, warning for >= 70 % or >= 70 °C.
    No automatic repair: back up first and get a technician.
- 0.1.5: UI/UX and robustness.
  - Finding cards: severity icon (not colour alone), larger title, advice in normal text colour, and the repair
    buttons visible on the card itself instead of hidden under Details (Details keeps cause and evidence).
  - Overview shows the customer journey: 1 Check-up → 2 Fix it all → 3 Review the result, with the current step
    highlighted and finished steps ticked.
  - Larger sidebar entries (48 px, bigger icons) with the version number at the bottom; busy indicator in the
    footer while a check-up or repair runs; keyboard shortcuts F5 (check-up) and F1 (help).
  - Robustness: only one Shiva Assist window at a time; closing during a check-up or repair asks first (default
    No); unexpected errors are written to `%LOCALAPPDATA%\Shiva Assist\crash-log.txt` and the app keeps running
    instead of closing.
- 0.1.4: Fix it all hero. After a scan a large card shows the problem count in the severity colour, what one click
  will do (expandable list), the biggest button in the app, live progress during the batch ("Repair 2 of 5: …")
  and the last outcome (fixed / improved / needs attention). Buttons are larger throughout (minimum 46 px for main
  actions, 40 px for secondary) with clear pressed and disabled states; Start the check-up uses the same hero style.
- New check and repair: printing. A stopped Print Spooler (with printers set up) or stuck print jobs give
  PRINT-SPOOLER-001; PRINT.SPOOLER.RESET restarts the spooler, removes only queued job files and re-enables a
  disabled spooler, verified by repeating the check. Included in Fix it all.
- CI WorkspaceChecks now runs a read-only scan and fails when the Fix it all hero does not render or has binding
  errors.
- Repairs that need a restore point switch System Protection on for the Windows drive when it is off, instead of
  stopping. The confirmation window says so.
- A repair that does not complete shows the reason on the finding card.
- Technician mode no longer calls the app read-only.
- New warm look: matte green on cream, a large round check-up button, and a choice of which areas to check.
- Stage 3b repairs: reset the DNS cache, request a new network address, update Microsoft Defender, restart to
  finish updates, reset Windows Update (Medium, restore point), remove the update download cache and crash dumps.
  Repairs whose effect shows only later end with the verdict "pending".
- New interface: a menu with the twelve modules of the product blueprint. Each module has its own page with its
  findings and repairs; modules of later phases show what is coming. The overview shows a tile per module.
- Brighter look: solid green header and menu selection, solid severity badges.
- The report is saved as a customer-friendly PDF (problems first, then good to know, then what is OK); the JSON
  report is still available as "technical report".
- History and undo page: every repair and undo from the audit log with its result; switched-off startup
  programs can be switched on again with one click; repairs that made a restore point link to System Restore.
- Test VM fault injection: `tools/test-vm/Inject-Faults.ps1`.

## 0.1.0 (2026-11-16): Stage 1, diagnostic foundation

First read-only release (Milestone 1 of the Shiva Assist architecture, phase 1 of the problem research).

- Finding contract `shiva.assist.finding/1.0` and report `shiva.assist.report/1.0`, with validation.
- Scan runner: checks run in parallel, each with a hard time limit; failures become Unknown with a reason
  (`requires-admin`, `unsupported`, `timeout`, `access-denied`, `error`).
- 13 modules: Windows version and lifecycle (Windows 11 per version and edition), updates, Windows integrity (DISM),
  security (antivirus, firewall), network layers, storage space and disk health, memory (incl. commit pressure),
  processor load, startup programs, devices and drivers, battery wear, stability (shutdowns, blue screens,
  hardware errors, program crashes), scan privileges.
- Console app `ShivaAssist.exe`: colour-coded results by category, plain-language advice, `--details` for evidence,
  JSON report in `Documents\Shiva Assist\Reports`.
- 63 tests, including the scenarios from *20 Windows problems in the Netherlands*.
- No external packages in the product; Windows data is read with built-in tools only.
# Software manager inventory

- Request Windows administrator permission before opening the dashboard, preserving technician mode. Declining
  the UAC prompt closes the application with an explanation.

- Overview retains scan setup while Scan results has its own page. Results now offers a selectable repair review
  queue, with individual approvals, verification and restarts last. Unknown checks get an incomplete-scan headline.

- Check WinGet program updates and approve one selected update in technician mode. Revalidate before installing,
  verify the resulting installed version and record the result in History. Source/package terms remain interactive.
- Open Software to browse registered programs, with publisher, version, estimated size and installation date.
- Search by name or publisher, sort columns and refresh without running the full check-up.
- Read per-user and machine entries in both registry views, with feedback for unreadable entries and missing metadata.
- English and Dutch labels, plus normalization and install-date tests.


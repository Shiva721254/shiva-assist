# Changelog

## Unreleased

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


# Changelog

## Unreleased

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

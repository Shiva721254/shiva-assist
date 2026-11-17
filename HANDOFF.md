# Shiva Assist: developer handoff

Last updated: 17 November 2026 (Software manager inventory added after roadmap step 2).
This file is the starting point for anyone (person or AI assistant) who continues development. Read it fully
before changing code; the rules in "Non-negotiable rules" protect customers' PCs.

## 1. What this is

Shiva Assist is a Windows diagnostic and repair app by **Shiva Computers** (Netherlands, KVK-registered ZZP).
Mission: help Windows users understand what is wrong with their PC, fix supported problems safely, protect their
data, and get a technician when automation is not enough. It is **not** a "cleaner/booster": accurate diagnosis,
explanation, user-approved repair, verification and escalation come first.

- Website (download page later): `assist.shivacomputers.nl`. Sister product: Shiva Rescue OS (parked; separate repo
  `Shiva721254/shiva-rescue-os`).
- Repo: `Shiva721254/shiva-assist` (`main`). Local copy on the owner's PC:
  `C:\Users\shiva\Desktop\Project\ShivaComputers\shiva-assist`. Everything for the business lives under
  `Desktop\Project\ShivaComputers`.
- Product documents: *Shiva Utilities Product Strategy v2*, *Top 20 Windows Problems Netherlands*, and the
  *Competitor Inspiration & Development Blueprint* (12 modules, roadmap, "what not to copy").

## 2. Tech stack and solution layout

C# / .NET 10, no NuGet packages in the product (only xUnit in tests). `Directory.Build.props`: Version 0.1.0,
`TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `InvariantGlobalization=false`,
`NoWarn CA2007;CA1303;CA1848;CA1515;CA1716`. **Every analyzer warning breaks the build** (e.g. CA1859, CA1822,
xUnit1031), so write clean code first time.

| Project | Target | Role |
|---|---|---|
| `src/ShivaAssist.Core` | net10.0 | Pure logic, no Windows calls: finding contract, checks' `Evaluate` rules, repair catalogue, localization, reports (text, JSON, **PDF**), module map, repair history. Fully unit-tested. |
| `src/ShivaAssist.Windows` | net10.0-windows | Data collection (`Sources/*`: CIM via PowerShell `-EncodedCommand`, DISM, wevtutil, registry, .NET networking), `WindowsPlatform.CreateChecks()`, repair executor and runner. |
| `src/ShivaAssist.App` | WPF | `ShivaAssist.exe`, the customer app (MVVM by hand: `ViewModels/*`). |
| `src/ShivaAssist.Cli` | console | `ShivaAssistCli.exe`, text report; used by the CI smoke test. |
| `src/ShivaAssist.Agent` | console, `requireAdministrator` | `ShivaAssistAgent.exe`: the elevated helper. Runs exactly one approved job file from the catalogue and exits. |
| `tests/ShivaAssist.Tests` | xUnit 2.9.3 | ~112 tests: contract, runner, every module's rules, repairs, verdicts, localization, PDF, modules, history. |

## 3. Key concepts (read the code for detail)

**Finding contract** `shiva.assist.finding/1.0` (`Core/Findings/Finding.cs`): `Id` (`MODULE-TOPIC-NNN`), `Module`,
`Category`, `Severity` (critical / warning / information / healthy / unknown), `Title` (a `Message`),
`SuspectedCause?`, `Recommendation(Summary, RequiresAdmin, Operations)`, `Evidence[]`, `Confidence`,
`UnknownReason` (requires-admin, unsupported, timeout, access-denied, error). Enums serialize kebab-case.
**Unknown is never shown as healthy.**

**Checks**: `ICheck` / `SnapshotCheck<T>`: an `ISnapshotSource<T>` (Windows project) collects data, a pure
`Evaluate(snapshot, context)` (Core) decides. `ScanRunner` runs checks in parallel with a time limit each and turns
exceptions into Unknown findings. 13 checks live in `Core/Modules/*`.

**Localization**: `Message` = English template (also the translation key) with placeholders `{x}`, `{x:gb}`,
`{x:pct}`, `{x:date}`. Dutch texts in `Core/Localization/nl.json` (`"texts": {...}`, formal "u", Dutch Windows menu
names). Default language English; Dutch selectable; more languages later = add another JSON file.
`python tools/check_translations.py` (runs in CI) fails when any English template lacks a translation. It finds
templates in: `new Message("…")`, `T("…")`, `Title =`, `SuspectedCause =`, `Recommendation = new("…")`,
`Name = "…"` / `Description = "…"` lines, `Finding.Unknown(…)` titles, network `Problem(…)` texts and the
ReportText switches. **Whenever you add user-facing English text, add its Dutch translation in the same commit.**
Evidence and repair `Detail` strings stay English (technical, for technicians).

**Modules / navigation** (`Core/Reporting/AppModules.cs`): Overview + 12 blueprint modules; each finding belongs to
the first module whose ID prefix matches. `Available = false` shows a "Soon" page.

**Repairs** (`Core/Repairs/*`, `Windows/Repairs/*`):
- `RepairCatalogue` is the allowlist: nothing outside it can run (the agent refuses unknown IDs). Each
  `RepairOperation` has Id (`AREA.TOPIC.ACTION`), Name, Description (shown before consent), `RiskClass`
  (Low / Medium / High), RequiresAdmin, RestartRequired, Minutes, `VerifiesFinding`, NeedsSelection,
  `VerifyLater`, `IsUndo`. `For(finding)` maps findings to offered repairs; `UndoFor(opId)` maps a repair to its undo.
- Flow: explain → ask (MessageBox with risk/duration) → run → re-check the finding → verdict
  (`Fixed`, `Improved`, `Unresolved`, `Inconclusive`, `Pending`) → append to the audit log
  `%LOCALAPPDATA%\Shiva Assist\repair-log.jsonl`.
- Medium/High: charger required on laptops, **restore point required** (System Protection is switched on
  automatically if off; if no restore point can be made, the repair stops and changes nothing).
- Admin repairs from a non-elevated app run via `RepairRunner.RunElevatedAsync`: writes
  `jobs\{id}.job.json`, starts the agent with `runas`, reads `{id}.result.json`, deletes both.
- Current catalogue: Recycle Bin, old temp files, update download cache + crash dumps, component store cleanup,
  switch off startup programs (+ undo), system files (DISM RestoreHealth + SFC, Medium), DNS flush, IP renew,
  Defender definitions, restart to finish updates, Windows Update reset (Medium, folders renamed not deleted).
- **Technician mode**: repairs are only offered when started with `ShivaAssist.exe --technician` (decision: free
  during this phase). "Restart as administrator" keeps the flag.

**UI** (`App/MainWindow.xaml`, `App.xaml`, `ViewModels/*`): bright palette, solid green `#2E9E5B` (dark
`#1F7A44`, soft `#DDF3E4`) on cream `#FBF7EE`; severity colours critical `#D9472B`, warning `#E8890C`,
information `#2B8FB3`, unknown `#8A8577`, OK green. Keep `App.xaml`, `Palette.cs` and `PdfReport.cs` colours in
sync. Icons: Segoe Fluent Icons / Segoe MDL2 Assets code points. Pages: Overview (start → scanning → results),
module page, History and undo, "Soon". Language stored in `HKCU\Software\Shiva Computers\Shiva Assist`.

**PDF report** (`Core/Reporting/PdfReport.cs`): hand-written PDF 1.4, Helvetica WinAnsi (Dutch accents OK),
multi-page; "Save as PDF…" in the app (JSON stays available as "technical report").

## 4. Non-negotiable rules

1. Detection is read-only. Only catalogue repairs change anything, and only after explicit consent.
2. Never log, store, export or send full product keys, passwords, BitLocker recovery keys or other secrets.
   Evidence must be redacted before it reaches a finding.
3. Do not commit customer reports, keys, secrets (e.g. the `SHIVA_HASH_KEY` GitHub secret is set by the owner
   only) or non-redistributable binaries.
4. Never claim a fix without verification; unknown stays unknown; physical faults get "back up + technician".
5. Do not build (blueprint "what not to copy"): registry cleaning, RAM boosting, automatic driver updates,
   automatic service disabling, defragmenting SSDs, misleading health scores, permanent remote access, forced
   repairs, guaranteed-repair claims, aggressive sales pop-ups. Undo/rollback is never paywalled.
6. Use full System32 paths for Windows tools; validate every input again in the executor (the agent must not
   trust the job file).
7. Customer-facing text: warm, plain language, formal Dutch "u".

## 5. Build, test, release

- CI: `.github/workflows/ci.yml` on `windows-latest`: translation check → build (Release) → tests → publish
  App/Cli/Agent self-contained single-file for **win-x64 and win-arm64** → smoke test with `ShivaAssistCli`
  → SHA-256 files → artifacts `shiva-assist-win-x64` / `shiva-assist-win-arm64` (download from the run page,
  login required; Chrome may block the unsigned zip: keep it via Ctrl+J or use Edge).
- Locally on Windows: `dotnet build ShivaAssist.slnx`, `dotnet test tests/ShivaAssist.Tests`,
  `python tools/check_translations.py`.
- Unsigned binaries for now. Plan: Azure Artifact Signing (≈ $9.99/month; eligibility for an eenmanszaak to be
  checked) or an OV certificate. Not decided yet.
- Test VM: VirtualBox `win11-test` (Windows 11 Enterprise evaluation) with snapshot `clean`, Guest Additions,
  shared folder `test-vm` → `\\VBoxSvr\test-vm` (= `ShivaComputers\test-vm`). In an admin PowerShell in the VM:
  `powershell -ExecutionPolicy Bypass -File \\VBoxSvr\test-vm\Inject-Faults.ps1 -Scenario All` creates a nearly
  full C:, Recycle Bin + old temp files, and 10 startup programs (refuses to run on a physical PC). Restore the
  snapshot afterwards. The repo copy is `tools/test-vm/Inject-Faults.ps1`.

## 6. Status (17 Nov 2026)

Owner requested administrator permission at initial startup for nontechnical customers. App.OnStartup now
relaunches with Windows UAC before opening the dashboard when needed, preserving command-line arguments.
Declining UAC closes the app with a clear message. Already-elevated launches continue directly. This also
supports dotnet DLL launches and avoids requiring an administrator developer terminal. CLI privileges stay
optional. Individual repair approvals still apply, and the Windows runtime behaviour needs a VM check.

The Results page now offers Review repairs with the count of catalogue operations. A checkbox dialog starts
with nothing selected. Each selected operation still uses the existing individual explanation, approval,
execution, verification and audit flow. It rechecks applicability between operations and runs restart operations
last. Scan/update actions and other repairs are blocked during the queue. Results with Unknown checks no longer
claim the PC looks healthy. A zero-repair explanation and administrator restart button appear on Results.

Navigation now gives Overview a persistent scan setup screen and adds a separate Scan results menu entry.
Finishing a scan opens Scan results automatically. Scan again returns to Overview with area selections retained.
The latest results remain accessible from the sidebar until another scan replaces them.

Done: stage 1 diagnostics (13 checks), stage 3a/3b repairs, technician mode, English/Dutch, warm start screen,
area selection, module menu (roadmap step 1), bright colours + PDF report, History and undo (step 2).
VM test results: Recycle Bin, temp, component cleanup and startup switch-off verified; update-cache cleanup fixed
(stray cmdlet text before JSON) and needs one more VM run. On the owner's laptop the scan found a repairable
component store; the system-file repair stopped safely because System Protection was off (now handled).

## 7. Next steps (agreed roadmap, see `claude/shiva-assist-module-roadmap.md` in the project)

Software manager inventory is implemented. The Software menu now opens an asynchronous registry inventory
with search, sortable columns, English/Dutch labels, refresh, and partial-access feedback. `SoftwareSource`
reads both HKLM/HKCU registry views. `SoftwareInventory` normalizes display metadata and parses registry dates.
Exact duplicate display rows are collapsed while different versions remain visible. Store and portable apps may
be absent and publisher-provided sizes may be missing or inaccurate. The screen explains these limits.
No uninstall strings are collected. The next increment now includes an explicit WinGet update check and a
technician-only catalogue action to update one selected package after consent. It revalidates package ID and
both versions before running an interactive installer, then checks the installed version with WinGet and logs
Fixed or Inconclusive. The community source is queried without silently accepting source or package terms.
Missing WinGet, source agreement failures, empty/unparseable output and timeouts remain explicit unknowns.
The conservative table parser uses header column positions and refuses truncated IDs and duplicate package
IDs. Test real English/Dutch WinGet output, installer/UAC prompts and History in the VM. Uninstall actions
and selecting multiple software updates remain future work.

Validation for this increment: 126 platform-neutral tests passed, Dutch translation check passed, and the Core,
Windows and WPF projects compile with zero warnings/errors on Linux with Windows targeting. The WPF build
used `-m:1 -p:SelfContained=false -p:RuntimeIdentifier=win-x64`. Run Windows CI for packaging and smoke tests.

3. **Software manager** (module `software`, currently "Soon"): inventory of installed programs (registry Uninstall
   keys, HKLM + HKLM\WOW6432Node + HKCU; name, publisher, version, size, install date), available updates via
   `winget upgrade` (parse output or `--output json` where available; handle winget missing), update one/selected
   app as a Low-risk catalogue repair, uninstall only through the program's own uninstaller after consent.
   **Cleaner with preview**: before deleting, show categories, sizes and counts; let the user untick items;
   browser **cache only** by default (cookies/passwords untouched unless explicitly chosen, with sign-out warning);
   large files/folders view (read-only).
4. Startup times (Diagnostics-Performance events 100/101 → boot duration and apps that slowed boot, shown as
   impact bars), hardware sensors (temperatures where Windows exposes them; otherwise Unknown), SMART/NVMe detail
   (`MSFT_StorageReliabilityCounter`: wear, temperature, power-on hours, errors).
5. Remote help (MeshCentral, self-hosted, consent per job with expiry and revocation) and emergency recovery
   (link to Shiva Rescue).

Also open: a download page on assist.shivacomputers.nl (`releases.json` + SHA-256), code signing,
setting the repo back to private when public CI monitoring is no longer needed.

## 8. How to add things (patterns)

- **New check**: snapshot record in `Core/Modules/Snapshots.cs` → source in `Windows/Sources` → check class with
  pure `Evaluate` in `Core/Modules` → register in `WindowsPlatform.CreateChecks()` → add its ID prefix to
  `AppModules` → tests in `ModuleTests.cs` → Dutch texts in `nl.json`.
- **New repair**: constant + `RepairOperation` in `RepairCatalogue.All` → mapping in `For(finding)` → implementation
  in `WindowsRepairExecutor` (validate inputs; PowerShell output must end with one JSON line) → record rollback
  data if reversible and add `UndoFor` → tests in `RepairTests.cs` → Dutch texts → test in the VM.
- **New page/module**: set `Available = true` in `AppModules`, add a page in `MainWindow.xaml` with a visibility
  flag in `MainViewModel`, reuse the `FindingCard` template.

## 9. Owner preferences

Concise, direct communication; professional tone; explain concept first, then specifics, with examples.
The owner pushes from VS Code and tests on the live CI artifacts; give exact click-by-click steps for Windows,
VirtualBox and GitHub. English is the default product language, Dutch selectable.

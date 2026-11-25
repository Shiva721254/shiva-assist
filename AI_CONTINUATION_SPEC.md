# Shiva Assist — AI Continuation Specification
**Dated handoff:** 24 November 2026 | Repo: `Shiva721254/shiva-assist` | Branch: `codex/software-inventory` | v0.2.0. Fetch remote HEAD every session; current source, CI, catalogue, and tests supersede stale prose.

## Role and goal
Act as a senior C#/.NET 10, WPF, Windows security, quality and release engineer plus disciplined project coordinator. Deliver genuine, safe Windows diagnostics and **detect → eligible repair → independent verification**. Preserve native Windows interface, fast clear UX, English and formal Dutch, privacy and testability. Customer path: **Start the check-up → Fix it all → Review the results**.

## Explicit authority limit
Do a read-only assessment first. Do not merge PR #1, sign, purchase paid services, publish to production websites, expose secrets, or modify infrastructure unless the owner has approved that specific scope. Preserve local uncommitted work. On an approved bounded change, disclose exact changed paths and verification results.

## Current verified status (v0.2.0)
- **CI #56 (`45364000520`):** Resolved former CI #53 failure (`ServicesTests.AllRunningIsHealthyWithoutRepairs`).
- **CI #57 (`45384235415`):** Wave A complete (`SECURITY.FIREWALL.ENABLE`, `DEVICE.BLUETOOTH.REPAIR`, `PRINT.QUEUE.RESUME`, `SECURITY.DEFENDER.ENABLE`, `SECURITY.DEFENDER.QUICKSCAN`, `NET.ADAPTER.RESET`, `NET.PROXY.RESET`/`RESTORE`, `RECOVERY.PROTECTION.ENABLE`, `RECOVERY-BITLOCKER-001`).
- **CI #58 (`45401952310`):** Wave B complete (`STORAGE-FS-001` / `STORAGE.CHKDSK.SCHEDULE`, `OS-UPGRADE-001`, `DISK.SHADERCACHE.CLEAN`, Quick Windows fixes `SHELL.EXPLORER.RESTART`/`SHELL.ICONCACHE.REBUILD`/`APPS.STORE.RESET`, batch multi-select WinGet updates, read-only Large Files finder, and Automatic/Your choice/Needs restart tier badges).
- **Test baseline:** 651 EN/NL templates (0 problems), Release build 0 warnings/0 errors, 272 xUnit tests, disposable `UninstallerChecks`, elevated WPF `WorkspaceChecks` (0 binding errors), x64/ARM64 WiX MSI packaging, bootstrapper self/UI tests, disposable install/repair/upgrade/uninstall, and live CLI smoke scan all passing.

## Critical architecture
- `Core`: finding and report contracts (`shiva.assist.finding/1.0`, `shiva.assist.report/1.0`), severity/Unknown, pure evaluators, `LargeFileFinder`, localization, 37-operation repair catalogue/policy, cleanup policy, PDF/JSON, audit/history.
- `Windows`: `ISnapshotSource<T>`, Windows service/registry/CIM/event log/tool sources, repair executor and verification.
- `App`: customer WPF MVVM (5 workspaces, Fix it all hero, tier badges, Optimize cleanup + Quick fixes + Large files, Software batch updates + uninstaller, History/undo/help).
- `Cli`: technician/automation diagnostic reports.
- `Agent`: elevated one-job allowlist and input revalidation.
- `Setup`: net48 bootstrapper verifying HTTPS manifest (`shiva.assist.release/1`), exact size/SHA-256, Windows Authenticode trust (`WinVerifyTrust`, timestamp-aware), Code Signing EKU (`1.3.6.1.5.5.7.3.3`) and pinned `PublisherIdentityOid` (`1.3.6.1.4.1.311.97.…`).
- `tests/ShivaAssist.Tests` unit; `tools/test-vm` disposable integration fixtures, `WorkspaceChecks`, installer tests and fault-injection scenarios.

## Exact operation IDs (37 total; 3 undo, not all Fix it all eligible)
1. `SOFTWARE.PROGRAM.UNINSTALL` — Medium, selection
2. `SOFTWARE.LEFTOVERS.CLEAN` — Low, selection
3. `SOFTWARE.LEFTOVERS.RESTORE` — Undo
4. `SOFTWARE.PACKAGE.UPDATE` — Low, selection (single or batch)
5. `DISK.RECYCLEBIN.EMPTY`
6. `DISK.TEMP.CLEAN`
7. `DISK.COMPONENTS.CLEANUP`
8. `STARTUP.PROGRAMS.DISABLE` — Low, selection
9. `WIN.SYSTEMFILES.REPAIR` — Medium
10. `DISK.UPDATECACHE.CLEAN`
11. `NET.DNS.FLUSH`
12. `NET.ADDRESS.RENEW`
13. `NET.ADAPTER.RESET`
14. `NET.PROXY.RESET`
15. `NET.PROXY.RESTORE` — Undo
16. `SECURITY.DEFENDER.UPDATE`
17. `SECURITY.DEFENDER.ENABLE`
18. `SECURITY.DEFENDER.QUICKSCAN`
19. `SECURITY.FIREWALL.ENABLE`
20. `UPDATE.RESTART.NOW` — Restart
21. `UPDATE.COMPONENTS.RESET` — Medium
22. `STARTUP.PROGRAMS.RESTORE` — Undo
23. `PRINT.SPOOLER.RESET`
24. `PRINT.QUEUE.RESUME`
25. `TIME.SYNC.REPAIR`
26. `DISK.BROWSERCACHE.CLEAN`
27. `DISK.SHADERCACHE.CLEAN`
28. `AUDIO.SERVICE.REPAIR`
29. `SEARCH.SERVICE.REPAIR`
30. `NET.WIFI.START`
31. `DEVICE.BLUETOOTH.REPAIR`
32. `RECOVERY.PROTECTION.ENABLE`
33. `STORAGE.CHKDSK.SCHEDULE` — Medium, restart
34. `SHELL.EXPLORER.RESTART` — Quick fix
35. `SHELL.ICONCACHE.REBUILD` — Quick fix
36. `APPS.STORE.RESET` — Quick fix
37. `NET.STACK.RESET` — Medium, restart

## Non-negotiable safeguards
- `FindingValidator` requires IDs to match `^[A-Z]+-[A-Z]+-\d{3}$` (letters only in the two segments + 3 digits). Always verify new checks with `FindingValidator.Validate(...)` and a live CLI scan.
- Post-repair verification (`RepairRunner.RecheckAsync`) looks up `report.Findings.FirstOrDefault(f => f.Id == before.Id)`; findings offering a repair must still emit `Severity.Healthy` after repair.
- `Fix it all` auto-executes only currently applicable safe repairs without extra checklists; selection, restart, and undo actions stay separate.
- Medium/high risks require charger and restore point or fail closed. Reversible actions retain Free Undo.
- Elevated agent revalidates all untrusted paths, package IDs, install commands, identity, timestamps, and third-party AV/firewall state. Never log keys, passwords, BitLocker recovery keys, Bluetooth device names, printer `PortName` SIDs, or proxy URLs.
- Keep English and formal Dutch ("u") synchronized in the same commit (`python tools/check_translations.py` = 0 problems).

## Next milestone (Wave C — Go-Live)
1. Run zero-cost `rehearsal-v0.2.0` tag via `.github/workflows/release.yml` to verify `website-upload` layout.
2. Complete Azure Artifact Signing Organization Public Trust validation (NL KVK *eenmanszaak*, Pay-As-You-Go) and configure OIDC secrets/variables + `PUBLISHER_IDENTITY_OID` (see `tools/installer/README.md`).
3. Deploy `assist.shivacomputers.nl` on STRATO (immutable versioned binaries first, `releases.json` last, `assist-site` PHPUnit validation).
4. Validate clean Windows 11 x64 & ARM64 downloads/installs with SmartScreen showing Publisher: Shiva Computers, then merge PR #1 and tag `v0.2.0` on `main`.

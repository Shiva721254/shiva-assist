# Shiva Assist — AI Continuation Specification
**Dated handoff:** 24 November 2026 | Repo: `Shiva721254/shiva-assist` | Branch: `codex/software-inventory` | inspected SHA: `3ee6cd9080693a04e895b67123732c12a21b1eb3` | v0.1.14. This is a snapshot; fetch HEAD every session. Current source, CI, catalogue and tests supersede stale prose.

## Role and goal
Act as a senior C#/.NET 10, WPF, Windows security, quality and release engineer plus disciplined project coordinator. Deliver genuine, safe Windows diagnostics and **detect → eligible repair → independent verification**. Preserve native Windows interface, fast clear UX, English and formal Dutch, privacy and testability. Customer path: **Start the check-up → Fix it all → Review the results**.

## Explicit authority limit
Do a read-only assessment first. Do not edit source, merge, sign, release, buy services, publish a website, expose secrets or modify infrastructure unless the owner has approved that specific scope. Preserve local uncommitted work. Do not mistake approval to maintain documentation for implementation approval. On an approved bounded change, disclose exact changed paths and results. Do not deliberately trigger CI or deploy without authorization.

## First-session procedure
1. Fetch branch `codex/software-inventory`; record branch name, HEAD SHA, worktree dirty status, current version, any PR #1 integration state, and whether `main` differs. Never overwrite dirty local files.
2. Read `CLAUDE.md`, `HANDOFF.md`, `README.md`, `CHANGELOG.md`, current `RepairCatalogue.cs`, `ServicesCheck`, Windows service snapshot/source, service unit tests, CI/release workflows and installer README.
3. Inspect latest commit-specific CI. Last audited [run #53](https://github.com/Shiva721254/shiva-assist/actions/runs/44704360055) FAILED: translation PASS, build PASS, 250/251 unit tests PASS; `ShivaAssist.Tests.ServicesTests.AllRunningIsHealthyWithoutRepairs` expected Healthy but received Unknown for one of three service findings. Post-test integrations and packages were SKIPPED.
4. Reproduce failure with `dotnet test tests/ShivaAssist.Tests --configuration Release --filter FullyQualifiedName~ServicesTests.AllRunningIsHealthyWithoutRepairs`. Compare test fixtures with real service and Wi-Fi adapter eligibility; service status and actual connectivity are distinct. Investigate missing adapter, stopped automatic vs disabled WLAN service, access denied, indeterminate state and absent hardware. Do not manufacture Healthy in unknown conditions.
5. Propose smallest fix and regression matrix; request required implementation approval. Once approved run targeted and full tests, translations, Windows integration and CI under exact commit SHA. If CI still fails, report without papering over.
6. Next, expand another *common* Windows problem with read-only bounded source, pure evaluator, allowlisted repair, fresh agent-side eligibility checks, postcheck verdict, NL/EN UI and tests. Avoid repeated manual VM reinstall for small changes.

## Critical architecture
- `Core`: finding and report contracts, severity/Unknown, pure evaluators, localization, repair catalogue/policy, cleanup policy, PDF/JSON, audit/history.
- `Windows`: `ISnapshotSource<T>`, Windows service/registry/CIM/event log/tool sources, repair executor and verification.
- `App`: customer WPF MVVM, Overview layout, large actions, graphical status.
- `Cli`: technician/automation diagnostic reports.
- `Agent`: elevated one-job allowlist and input revalidation.
- `Setup`: net48 bootstrapper, signed HTTPS manifest, x64/ARM64 MSI, publisher verification; `tools/installer` pins WiX.
- `tests/ShivaAssist.Tests` unit; `tools/test-vm` disposable integration fixtures, WorkspaceChecks, installer and fault-injection scenarios.

Finding schema `shiva.assist.finding/1.0`, report `shiva.assist.report/1.0`, release `shiva.assist.release/1`. Every diagnostic must be read-only. Unknown stays Unknown. Only catalogue actions can modify Windows; each must revalidate at execution time, keep backup/restore controls and produce truthful Fixed/Improved/Unresolved/Inconclusive/Pending outcome.

## Exact operation IDs (23; two undo, not all Fix it all eligible)
1. `DISK.RECYCLEBIN.EMPTY`
2. `DISK.TEMP.CLEAN`
3. `DISK.COMPONENTS.CLEANUP`
4. `STARTUP.PROGRAMS.DISABLE` — needs explicit target selection
5. `WIN.SYSTEMFILES.REPAIR`
6. `DISK.UPDATECACHE.CLEAN`
7. `NET.DNS.FLUSH`
8. `NET.ADDRESS.RENEW`
9. `SECURITY.DEFENDER.UPDATE`
10. `UPDATE.RESTART.NOW` — separate restart
11. `UPDATE.COMPONENTS.RESET`
12. `STARTUP.PROGRAMS.RESTORE` — undo only
13. `SOFTWARE.PACKAGE.UPDATE` — needs explicit target selection
14. `SOFTWARE.PROGRAM.UNINSTALL` — needs explicit target selection
15. `SOFTWARE.LEFTOVERS.CLEAN` — needs explicit target selection
16. `SOFTWARE.LEFTOVERS.RESTORE` — undo only
17. `PRINT.SPOOLER.RESET`
18. `TIME.SYNC.REPAIR`
19. `DISK.BROWSERCACHE.CLEAN`
20. `AUDIO.SERVICE.REPAIR`
21. `SEARCH.SERVICE.REPAIR`
22. `NET.WIFI.START`
23. `NET.STACK.RESET` — separate restart

## Non-negotiable safeguards
- Fix it all auto-executes only currently applicable *safe* repairs without extra checklist. User-selection operations, privileged higher-risk work needing explicit consent, and restart actions remain separate. No forced reboots, silent program removals, auto drivers or blanket service disabling.
- Medium/high risks require charger and restore point or fail safely. Reversible actions retain usable Undo without paywall.
- Elevated agent revalidates all untrusted paths, package IDs, install commands, identity, timestamps and mutable inventory. Do not run arbitrary shell commands or follow links out of safe roots.
- Keep files/preferences/profiles/credentials safe; do not log keys, passwords, BitLocker recovery data or personal identifiers. Avoid registry cleaners, RAM boosters, SSD defragmenting, unsupported remote access or untruthful speed claims.
- English and formal Dutch translated in same change, no duplicated navigation, primary buttons ≥46px and secondary ≥40px. Results: Fixed only with postcheck; Already clean does not equal repaired/failed.

## Acceptance pattern for a repair increment
A. Define user symptom, evidence, finding ID, severity/Unknown rules, supported Windows scope and safe alternatives.
B. Add read-only bounded Windows snapshot collection and pure evaluator tests for healthy, unhealthy, unknown, error and intermittent cases.
C. Add narrow allowlisted repair with correct risk/consent/administrator/restart/selection flags; no action unless source evidence supports it; privileged revalidation.
D. Verify with independent post-action observation; defer if reboot needed; test failure/idempotency/audit/undo.
E. Integrate short understandable UI + full EN/NL; use detail drilldown for technical diagnostics.
F. Run targeted tests, all xUnit, translation check, Windows integration, WPF smoke, packaging and **commit-specific** CI. VM fault injection only on disposable snapshot.
G. Report changed paths, SHA, validation pass/fail, VM limitations, risks and next action.

## CI and distribution
`.github/workflows/ci.yml` specifies translation → build → tests → uninstaller fixture → WPF workspace smoke → x64/arm64 publish → MSI packaging → setup self/UI test → disposable installer install/repair/upgrade/uninstall → read-only CLI report/180s threshold → SHA-256/artifacts. Latest audited run did **not** complete the latter stages.

`.github/workflows/release.yml` can create unsigned rehearsal (never customer facing) or signed Azure Artifact Signing artifacts using OIDC. Need identity-validation eligibility for a Dutch sole proprietor, public trust publisher EKU/OID + timestamp, exact manifest hashes, negative trust tests and **real ARM64** validation. `tools/installer/README.md` introductory certificate-hash text conflicts with later publisher-OID wording; inspect actual code. Hosting `assist.shivacomputers.nl` staging, TLS, EN/NL pages, credentials, immutable signed payloads first and `releases.json` last, rollback, commercial support/privacy/licensing and end-to-end clean customer install are unverified. No auto-deploy.

## Separate future plans
Shiva Rescue OS/WinPE for non-bootable PCs, proprietary license entitlements, own remote-support service are **not currently delivered** by Shiva Assist. Quick Assist launcher exists but does not itself transmit screens. Remote session security direction: explicit consent per job, revocable bounded authorization across reboot, never permanent unattended access. Distinct Microsoft licensing/redistribution review for WinPE.

## Required response/handoff output
Always state verified current branch and SHA, local status, source/test observations, CI run and exact failed step, any modifications and test counts, unknowns, owner decisions, remaining release blockers and next safe incremental task. Never imply background work, successful CI, public deployment or completed signing without proof.

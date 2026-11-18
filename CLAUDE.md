# Shiva Assist handoff

Continue on `codex/software-inventory`. Fetch the latest GitHub changes and inspect local work before editing. Read `HANDOFF.md` for project background and conventions. Use the current code and this file for the latest state when older notes differ.

Shiva Assist is a C#/.NET Windows desktop app using WPF. Keep English and Dutch UI text in sync. The customer flow is **Start the check-up → Fix it all → review the outcome**. All scan areas start selected. The user initiates the scan, and one click on Fix it all starts supported automatic repairs. Keep the batch free of checklists and per-repair approval dialogs. Actions that require an explicit choice, such as selecting startup apps, and actions that restart Windows are handled separately. Count a repair as fixed only when the verification supports it.

Overview places scan options on the left and the large Start the check-up button on the right. It shows the last Fix it all batch with fixed, improved, and attention or verification counts. Keep technical details under Details and avoid duplicating navigation. Repair and cleanup lives under PC health. Optimize has its own sidebar entry and supports cleaning old temporary files without a scan.

The working branch also includes installed app inventory and supported uninstall with recoverable leftover cleanup, startup controls, repair history, and recommended Windows updates plus supported app updates after one confirmation. Third-party installers can still prompt. Optional drivers are separate.

## Last verified build

- Version **0.1.3**, source commit `34186aea46ba8503f4d5981e0e689bbe6f980a26`.
- [Passing Windows CI run #41](https://github.com/Shiva721254/shiva-assist/actions/runs/44292506582)
- [Lab installer ZIP](https://github.com/Shiva721254/shiva-assist/actions/runs/44292506582/artifacts/14423011045)
- Extract the entire ZIP and run `src/ShivaAssist.Setup/bin/Release/net48/ShivaAssistSetupLab.exe`. After Windows administrator approval, setup selects and installs the matching bundled MSI.
- The enlarged button's exact sizing in a live VM and real Windows/app updates still need manual verification. If old per-repair prompts appear, confirm which EXE or Start menu shortcut is running and check its version.
- Run `python tools/check_translations.py` and relevant builds and tests. Verify CI for the specific source commit before sharing a new installer.

## Next development work

Prioritize repair and maintenance functionality. Review the current checks and repair catalog, then extend a useful Windows problem that can be detected, repaired, and verified. Use automated checks where practical so minor UI changes do not require repeated manual VM reinstalls. Public distribution on ShivaComputers.nl is a later milestone and still requires code signing, hosting, and an end-to-end download test.

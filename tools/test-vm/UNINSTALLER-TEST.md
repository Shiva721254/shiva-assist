# Integrated uninstaller: Windows VM check

Use the disposable Windows VM with a clean snapshot and the .NET 10 SDK. Never test your first uninstall on
an application you need. The fixture installer refuses physical hardware and only creates its dedicated folder
and registry entry. Revert the VM snapshot between scenarios; do not clean up the test by running broad scripts.

1. In an administrator PowerShell at the repository root, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/test-vm/Install-UninstallerFixture.ps1
   dotnet run --project src/ShivaAssist.App -- --technician
   ```

2. Open **Updates and apps**, click **Uninstaller**, select **Shiva Assist Uninstaller Fixture** in the inventory,
   then **Uninstall selected program**. Confirm the explanation. A restore point must succeed; otherwise the
   operation stops before invoking the uninstaller. In the fixture console, type `YES`.
3. Expect removal verification and the program disappearing from the inventory. Select its previous-removal
   session and click **Check removal and scan leftovers**. Nothing should be checked automatically.
4. Expect `.log` and `.tmp` files and eligible empty folders. Program binaries up to 64 MB may also be offered;
   larger executables such as the self-contained fixture are preserved. The personal `.txt` document and
   `settings.json` must never appear as candidates. The containing install folder cannot be removed while those
   protected files remain. Selecting a candidate folder also selects its listed contents; unchecking a child
   clears selected ancestors.
5. Select only the log/cache leftovers and approve **Clean selected leftovers**. Expect recovery storage, not a
   promise of freed space. Verify personal/settings files remain under Program Files.
6. Open **History and help**, find **Clean selected leftovers**, choose **Undo** and confirm. Expect the original
   content and empty folders to return. The fixture application must not reappear in the installed-program list:
   undoing cleanup does not reinstall an application.
7. Close/reopen Shiva Assist. The removal session should still be available. Switch English/Dutch and verify the
   program selection, status, buttons, and preview remain usable on a small window.

## Additional scenarios (revert the clean VM snapshot first)

- **Cancel:** press Enter instead of `YES`. Registration must remain and cleanup must be blocked.
- **Failure:** install with `-Scenario Failure`. Type `YES`. Expect unverified removal, registration retained,
  and no cleanup.
- **Restart:** install with `-Scenario Restart`. Type `YES`. This fixture returns 3010 without actually restarting
  Windows. Expect Pending and cleanup blocked. Restart the VM yourself; reopen Shiva Assist and check the saved
  session. Cleanup may proceed only after registration absence is verified.
- **Changed file:** after uninstall, edit a retained `.log` file before scanning. It must not be a candidate.
- **Undo conflict:** after cleanup, create a different file at the old log path. Undo must refuse to overwrite it
  and retain the recovery copy. Move your new test file aside and retry Undo.
- **Shared folder:** register a second test program with the same InstallLocation before uninstalling the first.
  No leftover snapshot/cleanup should be offered for that shared location.
- **Unsupported:** per-user EXE commands, scripts, network paths and ambiguous unquoted executable paths use the
  explicit **Open Windows uninstaller instead** fallback. MSI product-GUID registrations use System32 msiexec
  `/x {GUID} /norestart`, never a shell or downloaded executable.

Automated Core tests cover command parsing, path traversal/ADS rejection, preserved file types, changed files,
folder selection dependencies, symbolic-link substitution, recovery integrity and undo overwrite conflicts.
Real MSI/EXE child-process behaviour, restore points, ACL creation and the WPF layout still need this Windows check.

# Shiva Assist web installer

The small `ShivaAssistSetup.exe` uses Windows' .NET Framework 4.8 (not a bundled .NET 10 runtime).
It requests UAC at launch, detects native x64/ARM64, retrieves a fixed HTTPS release manifest,
streams the correct MSI with progress/cancellation, validates exact size and SHA-256, and checks
Windows Authenticode trust **and the pinned SHA-256 of the signing certificate**. It checks the
MSI manufacturer/version/architecture-specific UpgradeCode before invoking System32/msiexec.
After installation it verifies registration, the installed app's hash and signer before opening it.
The MSI installs the three self-contained executables under Program Files, adds a Start menu
shortcut and a normal Windows uninstall entry. MSI handles rollback, repair and major upgrades;
customer reports, history and recovery data are not part of the MSI and are preserved on removal.
No scheduled tasks, startup services or automatic background application updates are added.

## Test the unsigned lab build in a disposable Windows VM

Download the **shiva-assist-installer-lab** artifact from the branch's successful GitHub CI run.
Extract all files into one folder. Open **ShivaAssistSetupLab.exe**, approve UAC, and wait for setup to install the bundled MSI for the VM’s architecture. Keep the extracted directory structure intact.
Confirm progress, Start menu shortcut, app opening, Windows installed-apps entry and uninstall.
Try reopening setup and installing again.
Use a VM snapshot before testing; this test installs Shiva Assist itself.

The lab build is explicitly labelled, permits a local MSI with a supplied hash and intentionally
omits public signature trust. It is never the website download. The public build does not accept
local packages or a trust-bypass switch; it refuses installation until a publisher pin is configured.
CI tests wrong-hash rejection, install, all payload hashes, shortcut, reinstall, version upgrade,
normal uninstall and preservation of a customer-data marker. UI smoke checks cover opening and
closing the window. Real UAC, network failures, cancellation timing, signing and ARM64 installation
still need manual Windows tests; ARM64 is packaged but not executed on the x64 CI runner.

## Produce a signed public release (Windows / PowerShell 7)

1. Obtain/configure your trusted code-signing identity. Keep its private key outside this repo.
   Record the SHA-256 of the exact signing certificate's DER bytes (not the usual SHA-1 thumbprint).
   The bootstrapper pins this certificate; certificate renewal needs a newly built setup.
2. Set a new three-part MSI version in `Directory.Build.props` (0..255.0..255.0..65535).
3. Publish App, Cli and Agent self-contained into `publish/win-x64` and `publish/win-arm64`, as CI does.
4. Sign **all six published executables** with that identity and an RFC3161 timestamp. Verify trust.
5. Run `./tools/installer/Build-Packages.ps1 -Version '0.1.0'` (use the version from step 2).
6. Sign both resulting MSI files. Never modify the signed payloads or packages afterward.
7. Build public setup, embedding the certificate digest (replace the example variable):

   ```powershell
   dotnet build src/ShivaAssist.Setup -c Release -p:PublisherCertificateSha256=$certificateSha256
   ```

8. Sign `src/ShivaAssist.Setup/bin/Release/net48/ShivaAssistSetup.exe` with the same identity.
9. Generate the release manifest **after signing**, so hashes include signatures:

   ```powershell
   ./tools/installer/New-ReleaseManifest.ps1 -Version '0.1.0' -PublisherCertificateSha256 $certificateSha256
   ```

   This rejects invalid signatures, unexpected signers, or setup compiled with a different pin.
   It does not sign on your behalf. Signing credentials/providers are deliberately not guessed.
10. Stage the two MSIs and setup at `https://shivacomputers.nl/downloads/shiva-assist/`.
    Upload `releases.json` **last**, replacing it atomically. Use exact HTTPS URLs without redirects,
    authentication, HTML challenge pages or content transformation. Serve `.msi` as binary and JSON
    as JSON, with revalidation/no-cache for the manifest. Preserve older versioned MSI URLs.
11. Test a signed install from a clean VM, reinstall/upgrade, cancellation, broken/truncated download,
    wrong signer/hash, uninstall and retained user data. Then link your website Download button to
    `https://shivacomputers.nl/downloads/shiva-assist/ShivaAssistSetup.exe`.

`download-page.html` is an optional bilingual static page to stage alongside the release files.
Nothing in these scripts deploys to STRATO or creates a customer-facing live release automatically.
The public installer depends on hosting and a trusted certificate; development artifacts alone
are not a working website download. Offline MSI packages can be offered separately after signing.

## Release format

Schema `shiva.assist.release/1`, `version`, and exactly two `packages`. Each has `architecture`
(`win-x64` or `win-arm64`), `url`, `size` (bytes), `sha256` (signed MSI) and `appSha256` (signed app).
Packages must be <=512 MiB and hosted on an allowlisted Shiva Computers domain under
`/downloads/shiva-assist/`. Redirects are rejected. Download/manifest streams have bounded size
and time. Installer workspace is restricted to Administrators and SYSTEM and removed afterward.
Installation is not forcibly terminated on cancellation; the native MSI UI handles that stage.
A restart request is reported without automatically restarting Windows.

## Build tool

Packaging pins WiX 6.0.2; see https://docs.firegiant.com/wix/ and its OSMF EULA for the build
operator's applicable terms. It is a build dependency only; customers do not install WiX.

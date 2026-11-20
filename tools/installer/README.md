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

## Produce a public release (GitHub Actions → Release)

The workflow `.github/workflows/release.yml` builds, signs and packages everything. It never deploys to the website.

**Rehearsal (works today, no signing):** push a tag `rehearsal-<anything>` (for example `git tag rehearsal-1` then
`git push origin rehearsal-1`), or, once the workflow is on `main`, run it from Actions with "Rehearsal" ticked. The
artifact `website-upload-<version>-REHEARSAL` has the exact folder layout to upload, so the SFTP upload can be practised
on a test folder. The public setup refuses these unsigned files by design: never put them on the live site.

**Signed release:**

1. Azure Artifact Signing: paid (Pay-As-You-Go) subscription, signing account, completed **Organization** identity
   validation and a **Public Trust** certificate profile.
2. App registration with a federated credential for this repository (OIDC, no stored password) and the role
   **Artifact Signing Certificate Profile Signer** on the signing account.
3. Repository settings → Secrets and variables → Actions:
   secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`;
   variables `AZURE_SIGNING_ENDPOINT` (West Europe: `https://weu.codesigning.azure.net/`), `AZURE_SIGNING_ACCOUNT`,
   `AZURE_CERT_PROFILE` and `PUBLISHER_IDENTITY_OID`.
4. `PUBLISHER_IDENTITY_OID` is the identity-validation EKU in every certificate of the profile
   (`1.3.6.1.4.1.311.97.` followed by numbers unique to the Shiva Computers validation). Read it once from any file signed
   with the profile: Properties → Digital Signatures → Details → View Certificate → Details → Enhanced Key Usage.
   It is public, not a secret. Artifact Signing certificates renew daily and are valid for 72 hours, so the setup pins this
   durable identity value instead of a certificate hash, and requires a valid, timestamped Authenticode signature.
5. Set the new three-part version in `Directory.Build.props`, push, then run **Release** with "Rehearsal" unticked.
   The workflow signs the six programs, builds and signs both MSIs, builds the setup with the identity embedded, signs it,
   and `New-ReleaseManifest.ps1` rejects anything that is unsigned, untimestamped or signed by another identity.
6. Download the artifact `website-upload-<version>` and upload with SFTP to the assist site (document root `public/`):
   - everything in `public/downloads/shiva-assist/` **except** `releases.json`;
   - then `releases.json` **last** (this is the moment the setup offers the new version);
   - add `data-releases-entry.json` to the `releases` list in the site's `data/releases.json`, run the site tests, upload.
   Keep older versioned MSI and setup files; never overwrite a published versioned file.
7. Test on a clean Windows PC: download from https://assist.shivacomputers.nl/en/download, install, reinstall/upgrade,
   uninstall. Check that SmartScreen shows "Shiva Computers" as publisher.

Files (all under `https://assist.shivacomputers.nl/downloads/shiva-assist/`): `ShivaAssistSetup.exe` (newest),
`ShivaAssistSetup-<version>.exe`, `ShivaAssist-<version>-win-x64.msi`, `ShivaAssist-<version>-win-arm64.msi`,
`releases.json` (read by the setup). The site's download page links the versioned setup from `data/releases.json`.

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

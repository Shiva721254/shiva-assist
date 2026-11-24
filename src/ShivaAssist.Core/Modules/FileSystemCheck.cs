using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Core.Modules;

/// <summary>Checks the Windows NTFS system volume dirty bit and health status (read-only).</summary>
public sealed class FileSystemCheck(ISnapshotSource<FileSystemSnapshot> source) : SnapshotCheck<FileSystemSnapshot>(source)
{
    public const string FindingId = "STORAGE-FS-001";

    public override string Module => "filesystem";

    public override Message Name => "Check of the Windows file system";

    public override Category Category => Category.Hardware;

    public override string FailureId => "FILESYSTEM-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(FileSystemSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.NeedsAdmin)
        {
            return [Finding.Unknown(FindingId, Module, Category, "File system check needs administrator rights", UnknownReason.RequiresAdmin, "Run Shiva Assist as administrator to include this check.")];
        }

        if (snapshot.DirtyBit is null && snapshot.VolumeHealthy is null)
        {
            return [Finding.Unknown(FindingId, Module, Category, "The Windows file system could not be checked", UnknownReason.Unsupported, snapshot.Summary)];
        }

        var evidence = new Evidence("fsutil / Get-Volume", $"{snapshot.Drive} {snapshot.Summary}");
        if (snapshot.DirtyBit == true || snapshot.VolumeHealthy == false)
        {
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Warning,
                Title = "Windows flagged file system errors on the system drive",
                SuspectedCause = "An unexpected power cut or crash can leave NTFS file system errors behind.",
                Recommendation = new("Schedule Windows' built-in disk check for the next restart so errors are repaired before Windows loads."),
                Evidence = [evidence],
            }];
        }

        return [new Finding
        {
            Id = FindingId,
            Module = Module,
            Category = Category,
            Severity = Severity.Healthy,
            Title = "No file system errors are flagged on the Windows drive",
            Evidence = [evidence],
        }];
    }
}

/// <summary>Checks baseline Windows 11 hardware eligibility for Windows 10 PCs without forcing any upgrade.</summary>
public sealed class Win11ReadinessCheck(ISnapshotSource<Win11ReadinessSnapshot> source) : SnapshotCheck<Win11ReadinessSnapshot>(source)
{
    public const string FindingId = "OS-WIN11-READY-001";
    private const long MinRamBytes = 3_800_000_000;      // 4 GB nominal (allowing hardware-reserved RAM)
    private const long MinStorageBytes = 60_000_000_000; // 64 GB nominal (base-10 vs GiB tolerance)

    public override string Module => "win11-readiness";

    public override Message Name => "Check of Windows 11 readiness";

    public override Category Category => Category.WindowsSystem;

    public override string FailureId => "WIN11-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(Win11ReadinessSnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.AlreadyWindows11)
        {
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Healthy,
                Title = "This PC already runs Windows 11",
                Evidence = [new Evidence("OS build", "Windows 11 detected")],
            }];
        }

        var failed = new List<string>();
        var unknown = new List<string>();
        if (!snapshot.Is64BitOs) failed.Add("64-bit Windows");
        if (snapshot.RamBytes < MinRamBytes) failed.Add("at least 4 GB RAM");
        if (snapshot.SystemDriveTotalBytes < MinStorageBytes) failed.Add("at least 64 GB system drive");

        if (snapshot.Tpm20Present == false) failed.Add("TPM 2.0");
        else if (snapshot.Tpm20Present is null) unknown.Add("TPM 2.0");

        if (snapshot.SecureBootEnabled == false) failed.Add("Secure Boot");
        else if (snapshot.SecureBootEnabled is null) unknown.Add("Secure Boot");

        var evidence = new Evidence("Windows 11 baseline",
            $"64-bit={snapshot.Is64BitOs}, TPM2={State(snapshot.Tpm20Present)}, SecureBoot={State(snapshot.SecureBootEnabled)}, RAM={snapshot.RamBytes / 1_000_000_000} GB, Drive={snapshot.SystemDriveTotalBytes / 1_000_000_000} GB");

        if (failed.Count > 0)
        {
            return [new Finding
            {
                Id = FindingId,
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = new Message("This PC does not meet all Windows 11 requirements ({missing})", ("missing", string.Join(", ", failed))),
                SuspectedCause = "Older PCs or disabled BIOS security features (TPM 2.0 or Secure Boot) can block the Windows 11 upgrade.",
                Recommendation = new("Contact Shiva Computers to check whether TPM/Secure Boot can be enabled safely or discuss replacement options."),
                Evidence = [evidence],
            }];
        }

        if (unknown.Count > 0)
        {
            return [Finding.Unknown(FindingId, Module, Category, "Windows 11 readiness could not be fully checked", UnknownReason.Unsupported, $"Could not verify: {string.Join(", ", unknown)}")];
        }

        return [new Finding
        {
            Id = FindingId,
            Module = Module,
            Category = Category,
            Severity = Severity.Information,
            Title = "This PC meets the basic Windows 11 hardware checks",
            Recommendation = new("Check Windows Update or Microsoft PC Health Check to confirm processor eligibility before upgrading."),
            Evidence = [evidence],
        }];
    }

    private static string State(bool? value) => value switch { true => "yes", false => "no", null => "unknown" };
}

using ShivaAssist.Core.Findings;
using ShivaAssist.Core.Localization;
using ShivaAssist.Core.Scanning;

namespace ShivaAssist.Core.Modules;

/// <summary>
/// Checks whether Windows System Protection (restore points) is enabled and reminds users to back up
/// their BitLocker recovery key if drive encryption is active. Never reads or logs recovery keys.
/// </summary>
public sealed class RecoveryCheck(ISnapshotSource<RecoverySnapshot> source) : SnapshotCheck<RecoverySnapshot>(source)
{
    public const string ProtectionId = "RECOVERY-PROTECTION-001";
    public const string BitLockerId = "RECOVERY-BITLOCKER-001";

    public override string Module => "recovery";

    public override Message Name => "Check of system protection and recovery";

    public override Category Category => Category.BootAndRecovery;

    public override string FailureId => "RECOVERY-CHECK-000";

    public override IReadOnlyList<Finding> Evaluate(RecoverySnapshot snapshot, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [SystemProtection(snapshot), BitLocker(snapshot)];
    }

    private Finding SystemProtection(RecoverySnapshot snapshot)
    {
        if (snapshot.DisabledByPolicy)
        {
            return new Finding
            {
                Id = ProtectionId,
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = "Windows System Protection is switched off by an administrator policy",
                Recommendation = new("Check with your organisation or system administrator before changing System Protection."),
                Evidence = [new Evidence("SystemRestore", "DisableSR policy is active")],
            };
        }

        if (snapshot.SystemProtectionEnabled is null)
        {
            return Finding.Unknown(ProtectionId, Module, Category, "System Protection status could not be checked", UnknownReason.Unsupported, "SystemRestore registry setting could not be read");
        }

        if (snapshot.SystemProtectionEnabled == true)
        {
            return new Finding
            {
                Id = ProtectionId,
                Module = Module,
                Category = Category,
                Severity = Severity.Healthy,
                Title = "Windows System Protection is switched on",
                Evidence = [new Evidence("SystemRestore", "RPSessionInterval = 1")],
            };
        }

        return new Finding
        {
            Id = ProtectionId,
            Module = Module,
            Category = Category,
            Severity = Severity.Warning,
            Title = "Windows System Protection is switched off",
            SuspectedCause = "Without System Protection, Windows cannot create restore points before updates or repairs.",
            Recommendation = new("Switch Windows System Protection on for the Windows drive."),
            Evidence = [new Evidence("SystemRestore", "RPSessionInterval = 0; not disabled by policy")],
        };
    }

    private Finding BitLocker(RecoverySnapshot snapshot)
    {
        if (snapshot.BitLockerNeedsAdmin)
        {
            return Finding.Unknown(BitLockerId, Module, Category, "BitLocker status needs administrator rights", UnknownReason.RequiresAdmin, "Run Shiva Assist as administrator to include this check.");
        }

        return snapshot.BitLocker switch
        {
            BitLockerState.On => new Finding
            {
                Id = BitLockerId,
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = "BitLocker drive encryption is on: keep your recovery key backed up",
                SuspectedCause = "If Windows ever fails to start after an update or hardware repair, Windows asks for the 48-digit BitLocker recovery key.",
                Recommendation = new("Verify that your BitLocker recovery key is saved in your Microsoft account or printed in a safe place."),
                Evidence = [new Evidence("Win32_EncryptableVolume", "System drive ProtectionStatus = 1 (On)")],
            },
            BitLockerState.Suspended => new Finding
            {
                Id = BitLockerId,
                Module = Module,
                Category = Category,
                Severity = Severity.Information,
                Title = "BitLocker drive encryption is temporarily suspended",
                Recommendation = new("Restart your PC or resume BitLocker protection in Windows Settings if it should be active."),
                Evidence = [new Evidence("Win32_EncryptableVolume", "System drive ProtectionStatus = 2 (Suspended)")],
            },
            BitLockerState.Off => new Finding
            {
                Id = BitLockerId,
                Module = Module,
                Category = Category,
                Severity = Severity.Healthy,
                Title = "BitLocker drive encryption is off",
                Evidence = [new Evidence("Win32_EncryptableVolume", "System drive ProtectionStatus = 0 (Off)")],
            },
            _ => Finding.Unknown(BitLockerId, Module, Category, "BitLocker status could not be checked on this PC", UnknownReason.Unsupported, "BitLocker WMI provider is not available on this Windows edition"),
        };
    }
}

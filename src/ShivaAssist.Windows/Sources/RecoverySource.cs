using Microsoft.Win32;
using ShivaAssist.Core.Modules;
using ShivaAssist.Core.Scanning;
using static ShivaAssist.Windows.Infrastructure.PowerShell;

namespace ShivaAssist.Windows.Sources;

/// <summary>
/// Reads Windows System Protection registry flags and BitLocker ProtectionStatus on the system drive.
/// Never queries or logs any BitLocker recovery key or protector material.
/// </summary>
public sealed class RecoverySource : ISnapshotSource<RecoverySnapshot>
{
    private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string SystemRestorePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    private const string BitLockerScript = """
        try {
            $v = Get-CimInstance -Namespace root/CIMV2/Security/MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -ErrorAction Stop |
                Where-Object { $_.DriveLetter -eq $env:SystemDrive } | Select-Object -First 1
            if ($null -eq $v) {
                [pscustomobject]@{ state = 'unknown' } | ConvertTo-Json -Compress
            } else {
                $st = switch ([int]$v.ProtectionStatus) { 0 { 'off' } 1 { 'on' } 2 { 'suspended' } default { 'unknown' } }
                [pscustomobject]@{ state = $st } | ConvertTo-Json -Compress
            }
        } catch {
            $msg = "$_"
            if ($msg -match 'Access denied|0x80041003') {
                [pscustomobject]@{ state = 'access-denied' } | ConvertTo-Json -Compress
            } else {
                [pscustomobject]@{ state = 'unknown' } | ConvertTo-Json -Compress
            }
        }
        """;

    public async Task<RecoverySnapshot> ReadAsync(ScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (enabled, disabledByPolicy) = ReadSystemProtection();

        if (!context.IsElevated)
        {
            return new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.Unknown, BitLockerNeedsAdmin: true);
        }

        try
        {
            var json = await QueryAsync(BitLockerScript, cancellationToken);
            var raw = Str(json, "state");
            return raw switch
            {
                "access-denied" => new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.Unknown, BitLockerNeedsAdmin: true),
                "on" => new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.On, false),
                "off" => new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.Off, false),
                "suspended" => new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.Suspended, false),
                _ => new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.Unknown, false),
            };
        }
        catch (PlatformNotSupportedException)
        {
            return new RecoverySnapshot(enabled, disabledByPolicy, BitLockerState.Unknown, false);
        }
    }

    private static (bool? Enabled, bool DisabledByPolicy) ReadSystemProtection()
    {
        using var policy = Registry.LocalMachine.OpenSubKey(SystemRestorePolicyKey);
        var policyDisabled = policy?.GetValue("DisableSR") is int p && p != 0;

        using var key = Registry.LocalMachine.OpenSubKey(SystemRestoreKey);
        if (key is null)
        {
            return (null, policyDisabled);
        }

        var localDisabled = key.GetValue("DisableSR") is int d && d != 0;
        if (localDisabled)
        {
            return (false, policyDisabled);
        }

        return key.GetValue("RPSessionInterval") is int interval
            ? (interval > 0, policyDisabled)
            : (null, policyDisabled);
    }
}

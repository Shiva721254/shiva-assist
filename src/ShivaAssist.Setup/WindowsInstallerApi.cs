using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ShivaAssist.Setup;

internal static class WindowsInstallerApi
{
    public static string Architecture()
    {
        try
        {
            if (!IsWow64Process2(GetCurrentProcess(), out _, out var native)) throw new System.ComponentModel.Win32Exception();
            return InstallerPolicy.Architecture(native);
        }
        catch (EntryPointNotFoundException)
        {
            GetNativeSystemInfo(out var system);
            return InstallerPolicy.Architecture(system.Architecture == 9 ? (ushort)0x8664
                : system.Architecture == 12 ? (ushort)0xAA64 : (ushort)0);
        }
    }

    public static string InstalledDirectory => Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Shiva Computers", "Shiva Assist");

    public static string Property(string package, string name)
    {
        if (name != "ProductVersion" && name != "ProductCode" && name != "Manufacturer" && name != "UpgradeCode")
            throw new ArgumentException("Unsupported MSI property.");
        Check(MsiOpenDatabase(package, IntPtr.Zero, out var database));
        uint view = 0, record = 0;
        try
        {
            Check(MsiDatabaseOpenView(database, "SELECT `Value` FROM `Property` WHERE `Property`='" + name + "'", out view));
            Check(MsiViewExecute(view, 0));
            Check(MsiViewFetch(view, out record));
            uint size = 0;
            var code = MsiRecordGetString(record, 1, null, ref size);
            if (code != 234 && code != 0) Check(code);
            var result = new StringBuilder((int)size + 1);
            size++;
            Check(MsiRecordGetString(record, 1, result, ref size));
            return result.ToString();
        }
        finally
        {
            if (record != 0) _ = MsiCloseHandle(record);
            if (view != 0) _ = MsiCloseHandle(view);
            _ = MsiCloseHandle(database);
        }
    }

    public static bool Installed(string productCode) => MsiQueryProductState(productCode) == 5;

    public static bool TrustedPublisher(string path, FileStream lockedFile, string expectedCertificateSha256)
    {
        if (!InstallerPolicy.ValidDigest(expectedCertificateSha256)) return false;
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path,
            Handle = lockedFile.SafeFileHandle.DangerousGetHandle() };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), File = pointer };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, data) != 0) return false;
            var provider = WTHelperProvDataFromStateData(data.State);
            var signerPointer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signerPointer == IntPtr.Zero) return false;
            var signer = Marshal.PtrToStructure<ProviderSigner>(signerPointer);
            if (signer.CertificateCount == 0 || signer.Certificates == IntPtr.Zero) return false;
            var certificate = Marshal.PtrToStructure<ProviderCertificate>(signer.Certificates);
            using var cert = new X509Certificate2(certificate.Certificate);
            using var bytes = new MemoryStream(cert.RawData, writable: false);
            return string.Equals(InstallerDownloads.Hash(bytes), expectedCertificateSha256, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            data.StateAction = 2; // WTD_STATEACTION_CLOSE, including failed verification states.
            _ = WinVerifyTrust(new IntPtr(-1), ref action, data);
            Marshal.DestroyStructure<TrustFile>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    public static string CreatePrivateWorkspace()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ShivaAssistSetup-" + Guid.NewGuid().ToString("N"));
        InstallerPolicy.EnsureNoLinks(path);
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(admin);
        security.SetAccessRuleProtection(true, false);
        foreach (var type in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(type, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (Directory.Exists(path)) throw new IOException("The private setup folder already exists.");
        Directory.CreateDirectory(path, security);
        InstallerPolicy.EnsureNoLinks(path);
        return path;
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException("Windows Installer could not read the package (" + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemInfo
    {
        public ushort Architecture, Reserved;
        public uint PageSize;
        public IntPtr Minimum, Maximum;
        public UIntPtr Mask;
        public uint Processors, Type, Granularity;
        public ushort Level, Revision;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class TrustData
    {
        public uint Size;
        public IntPtr PolicyCallback = IntPtr.Zero, SipData = IntPtr.Zero;
        public uint UiChoice = 2, RevocationChecks = 1, UnionChoice = 1;
        public IntPtr File;
        public uint StateAction = 1;
        public IntPtr State = IntPtr.Zero, UrlReference = IntPtr.Zero;
        public uint ProviderFlags = 0x80, UiContext; // Chain revocation, excluding the root; no trust bypass.
        public IntPtr SignatureSettings = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderSigner
    {
        public uint Size;
        public System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;
        public uint CertificateCount;
        public IntPtr Certificates;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate { public uint Size; public IntPtr Certificate; }

    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern void GetNativeSystemInfo(out SystemInfo info);
    [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint MsiOpenDatabase(string path, IntPtr persist, out uint database);
    [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);
    [DllImport("msi.dll", ExactSpelling = true)] private static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll", ExactSpelling = true)] private static extern uint MsiViewFetch(uint view, out uint record);
    // MSI uses a caller-sized Unicode buffer; this setup is not a hot path.
#pragma warning disable CA1838
    [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint MsiRecordGetString(uint record, uint field, StringBuilder? value, ref uint length);
#pragma warning restore CA1838
    [DllImport("msi.dll", ExactSpelling = true)] private static extern uint MsiCloseHandle(uint handle);
    [DllImport("msi.dll", EntryPoint = "MsiQueryProductStateW", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string productCode);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, [In, Out] TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
}

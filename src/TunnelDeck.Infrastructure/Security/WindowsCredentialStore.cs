using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using TunnelDeck.Core.Abstractions;

namespace TunnelDeck.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;
    private readonly string _prefix;

    public WindowsCredentialStore(string prefix = "TunnelDeck/Credentials")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _prefix = prefix.TrimEnd('/');
    }

    public void Write(Guid reference, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        int byteCount = Encoding.Unicode.GetByteCount(secret);
        if (byteCount > 2560) { throw new ArgumentException("The credential exceeds Windows' storage limit.", nameof(secret)); }

        IntPtr blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = Target(reference),
                CredentialBlobSize = (uint)byteCount,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "TunnelDeck",
            };
            if (!CredWrite(ref credential, 0)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public string? Read(Guid reference)
    {
        if (!CredRead(Target(reference), GenericCredential, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NotFound) { return null; }
            throw new Win32Exception(error);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(Guid reference)
    {
        if (!CredDelete(Target(reference), GenericCredential, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != NotFound) { throw new Win32Exception(error); }
        }
    }

    private string Target(Guid reference)
    {
        if (reference == Guid.Empty) { throw new ArgumentException("A credential reference is required.", nameof(reference)); }
        return $"{_prefix}/{reference:D}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}

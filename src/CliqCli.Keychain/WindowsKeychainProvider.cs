using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CliqCli.Keychain;

/// <summary>
/// Windows Credential Manager implementation of <see cref="IKeychainProvider"/>.
/// Uses advapi32.dll CredWrite / CredRead / CredDelete P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsKeychainProvider : IKeychainProvider
{
    private const uint CredTypeGeneric = 1;
    private const uint PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CredReadW(key, CredTypeGeneric, 0, out IntPtr credPtr))
        {
            int error = Marshal.GetLastWin32Error();
            // ERROR_NOT_FOUND = 1168, ERROR_NO_SUCH_LOGON_SESSION = 1312
            if (error == 1168 || error == 1312)
                return Task.FromResult<string?>(null);
            throw new Win32Exception(error, $"CredReadW failed for key '{key}'.");
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                return Task.FromResult<string?>(null);

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var blob = Encoding.Unicode.GetBytes(value);
        var blobHandle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = key,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobHandle.AddrOfPinnedObject(),
                Persist = PersistLocalMachine,
                UserName = Environment.UserName,
            };

            if (!CredWriteW(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredWriteW failed for key '{key}'.");
        }
        finally
        {
            blobHandle.Free();
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CredDeleteW(key, CredTypeGeneric, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168)
                return Task.FromResult(false);
            throw new Win32Exception(error, $"CredDeleteW failed for key '{key}'.");
        }
        return Task.FromResult(true);
    }
}

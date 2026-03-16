using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CliqCli.Keychain;

/// <summary>
/// macOS Keychain Services implementation of <see cref="IKeychainProvider"/>.
/// Uses Security.framework P/Invoke for SecKeychainAddGenericPassword /
/// SecKeychainFindGenericPassword / SecKeychainItemDelete.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainProvider : IKeychainProvider
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string ServiceName = "cliq-cli";

    // Error codes from Security.framework
    private const int ErrSecSuccess = 0;
    private const int ErrSecDuplicateItem = -25299;
    private const int ErrSecItemNotFound = -25300;

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(SecurityFramework)]
    private static extern int CFRelease(IntPtr cf);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(key);

        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        if (status == ErrSecItemNotFound)
            return Task.FromResult<string?>(null);

        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"SecKeychainFindGenericPassword failed with status {status}.");

        try
        {
            if (passwordData == IntPtr.Zero || passwordLength == 0)
                return Task.FromResult<string?>(null);

            var bytes = new byte[passwordLength];
            Marshal.Copy(passwordData, bytes, 0, (int)passwordLength);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            if (passwordData != IntPtr.Zero)
                SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (itemRef != IntPtr.Zero)
                CFRelease(itemRef);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var passwordBytes = Encoding.UTF8.GetBytes(value);

        // Try to add first
        int status = SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes,
            (uint)passwordBytes.Length, passwordBytes,
            out IntPtr itemRef);

        if (itemRef != IntPtr.Zero)
            CFRelease(itemRef);

        if (status == ErrSecDuplicateItem)
        {
            // Update: delete existing then re-add
            DeleteExisting(serviceBytes, accountBytes);

            status = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length, serviceBytes,
                (uint)accountBytes.Length, accountBytes,
                (uint)passwordBytes.Length, passwordBytes,
                out IntPtr newItemRef);

            if (newItemRef != IntPtr.Zero)
                CFRelease(newItemRef);
        }

        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"SecKeychainAddGenericPassword failed with status {status}.");

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(key);

        bool deleted = DeleteExisting(serviceBytes, accountBytes);
        return Task.FromResult(deleted);
    }

    private bool DeleteExisting(byte[] serviceBytes, byte[] accountBytes)
    {
        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes,
            out _,
            out IntPtr passwordData,
            out IntPtr itemRef);

        if (passwordData != IntPtr.Zero)
            SecKeychainItemFreeContent(IntPtr.Zero, passwordData);

        if (status == ErrSecItemNotFound)
            return false;

        if (status != ErrSecSuccess || itemRef == IntPtr.Zero)
            return false;

        try
        {
            int deleteStatus = SecKeychainItemDelete(itemRef);
            return deleteStatus == ErrSecSuccess;
        }
        finally
        {
            CFRelease(itemRef);
        }
    }
}

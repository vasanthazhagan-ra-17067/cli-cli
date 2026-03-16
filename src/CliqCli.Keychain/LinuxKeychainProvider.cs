using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CliqCli.Keychain;

/// <summary>
/// Linux Secret Service (libsecret) implementation of <see cref="IKeychainProvider"/>.
/// Uses libsecret-1 P/Invoke. On any failure (DllNotFoundException, no D-Bus session,
/// libsecret unavailable), <see cref="TryCreate"/> returns <see langword="false"/> and
/// <see cref="KeychainProviderFactory"/> will activate <see cref="EncryptedFileKeychainProvider"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxKeychainProvider : IKeychainProvider
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string SchemaName = "org.gnome.keyring.Generic";
    private const string AppAttr = "application";
    private const string AppAttrValue = "cliq-cli";
    private const string KeyAttr = "key";

    // SECRET_SCHEMA_NONE = 0, SECRET_SCHEMA_ATTRIBUTE_STRING = 0
    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchemaAttribute
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string? Name;
        public int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchema
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string Name;
        public int Flags;
        // libsecret allows up to 32 attribute definitions; terminate with null name
        public SecretSchemaAttribute Attr0;
        public SecretSchemaAttribute Attr1;
        public SecretSchemaAttribute Attr2; // terminator
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 29)]
        public SecretSchemaAttribute[] Padding;
    }

    // Variadic P/Invoke with fixed 2-attribute call site (application + key)
    // CallingConvention.Cdecl is required for variadic C functions
    [DllImport(LibSecret, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool secret_password_store_sync(
        ref SecretSchema schema,
        [MarshalAs(UnmanagedType.LPStr)] string? collection,
        [MarshalAs(UnmanagedType.LPStr)] string label,
        [MarshalAs(UnmanagedType.LPStr)] string password,
        IntPtr cancellable,
        out IntPtr error,
        [MarshalAs(UnmanagedType.LPStr)] string attr1Name,
        [MarshalAs(UnmanagedType.LPStr)] string attr1Value,
        [MarshalAs(UnmanagedType.LPStr)] string attr2Name,
        [MarshalAs(UnmanagedType.LPStr)] string attr2Value,
        IntPtr terminator);

    [DllImport(LibSecret, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    private static extern string? secret_password_lookup_sync(
        ref SecretSchema schema,
        IntPtr cancellable,
        out IntPtr error,
        [MarshalAs(UnmanagedType.LPStr)] string attr1Name,
        [MarshalAs(UnmanagedType.LPStr)] string attr1Value,
        [MarshalAs(UnmanagedType.LPStr)] string attr2Name,
        [MarshalAs(UnmanagedType.LPStr)] string attr2Value,
        IntPtr terminator);

    [DllImport(LibSecret, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool secret_password_clear_sync(
        ref SecretSchema schema,
        IntPtr cancellable,
        out IntPtr error,
        [MarshalAs(UnmanagedType.LPStr)] string attr1Name,
        [MarshalAs(UnmanagedType.LPStr)] string attr1Value,
        [MarshalAs(UnmanagedType.LPStr)] string attr2Name,
        [MarshalAs(UnmanagedType.LPStr)] string attr2Value,
        IntPtr terminator);

    [DllImport(LibSecret, CallingConvention = CallingConvention.Cdecl)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport(LibSecret, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(IntPtr error);

    private SecretSchema _schema;

    private LinuxKeychainProvider() { }

    /// <summary>
    /// Attempts to create and initialise a <see cref="LinuxKeychainProvider"/>.
    /// Returns <see langword="false"/> if libsecret is unavailable or the Secret Service
    /// cannot be reached (no D-Bus session, daemon not running, etc.).
    /// </summary>
    public static bool TryCreate(out LinuxKeychainProvider? provider)
    {
        try
        {
            var p = new LinuxKeychainProvider();
            if (p.TryInitialize())
            {
                provider = p;
                return true;
            }
            provider = null;
            return false;
        }
        catch (DllNotFoundException)
        {
            provider = null;
            return false;
        }
        catch
        {
            provider = null;
            return false;
        }
    }

    private bool TryInitialize()
    {
        try
        {
            _schema = BuildSchema();

            // Probe: attempt a lookup for a sentinel key to verify the service is reachable.
            // A "not found" result is success — it confirms libsecret loaded and D-Bus responded.
            string? _ = secret_password_lookup_sync(
                ref _schema, IntPtr.Zero, out IntPtr errorPtr,
                AppAttr, AppAttrValue,
                KeyAttr, "__cliq_cli_probe__",
                IntPtr.Zero);

            if (errorPtr != IntPtr.Zero)
            {
                g_error_free(errorPtr);
                // An error on the probe (e.g. no session bus) means the service is unavailable.
                return false;
            }

            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        string? value = secret_password_lookup_sync(
            ref _schema, IntPtr.Zero, out IntPtr errorPtr,
            AppAttr, AppAttrValue,
            KeyAttr, key,
            IntPtr.Zero);

        if (errorPtr != IntPtr.Zero)
        {
            g_error_free(errorPtr);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(value);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        bool result = secret_password_store_sync(
            ref _schema,
            "default",
            $"cliq-cli/{key}",
            value,
            IntPtr.Zero,
            out IntPtr errorPtr,
            AppAttr, AppAttrValue,
            KeyAttr, key,
            IntPtr.Zero);

        if (errorPtr != IntPtr.Zero)
        {
            g_error_free(errorPtr);
            throw new InvalidOperationException($"secret_password_store_sync failed for key '{key}'.");
        }

        if (!result)
            throw new InvalidOperationException($"secret_password_store_sync returned false for key '{key}'.");

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        bool result = secret_password_clear_sync(
            ref _schema, IntPtr.Zero, out IntPtr errorPtr,
            AppAttr, AppAttrValue,
            KeyAttr, key,
            IntPtr.Zero);

        if (errorPtr != IntPtr.Zero)
        {
            g_error_free(errorPtr);
            return Task.FromResult(false);
        }

        return Task.FromResult(result);
    }

    private static SecretSchema BuildSchema()
    {
        return new SecretSchema
        {
            Name = SchemaName,
            Flags = 0,
            Attr0 = new SecretSchemaAttribute { Name = AppAttr, Type = 0 },
            Attr1 = new SecretSchemaAttribute { Name = KeyAttr, Type = 0 },
            Attr2 = new SecretSchemaAttribute { Name = null, Type = 0 },
            Padding = new SecretSchemaAttribute[29],
        };
    }
}

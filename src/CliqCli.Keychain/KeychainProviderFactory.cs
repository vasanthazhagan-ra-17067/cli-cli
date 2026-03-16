using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CliqCli.Keychain;

/// <summary>
/// Selects the appropriate <see cref="IKeychainProvider"/> for the current OS at runtime.
/// Falls back to <see cref="EncryptedFileKeychainProvider"/> when the OS keychain is
/// unavailable (e.g. headless CI, no D-Bus session on Linux, unsigned build on macOS).
/// </summary>
public static class KeychainProviderFactory
{
    /// <summary>
    /// Creates and returns the best available <see cref="IKeychainProvider"/> for this machine.
    /// </summary>
    /// <param name="configDir">
    /// The application config directory used by <see cref="EncryptedFileKeychainProvider"/>
    /// when the OS keychain is unavailable. If <see langword="null"/>, the platform default
    /// is used (see <see cref="EncryptedFileKeychainProvider.GetDefaultConfigDir"/>).
    /// </param>
    public static IKeychainProvider Create(string? configDir = null)
    {
        configDir ??= EncryptedFileKeychainProvider.GetDefaultConfigDir();

        if (OperatingSystem.IsMacOS())
            return TryCreateMacOs(configDir);

        if (OperatingSystem.IsWindows())
            return TryCreateWindows();

        if (OperatingSystem.IsLinux())
            return TryCreateLinux(configDir);

        // Unknown platform — use encrypted file fallback
        return new EncryptedFileKeychainProvider(configDir);
    }

    [SupportedOSPlatform("macos")]
    private static IKeychainProvider TryCreateMacOs(string configDir)
    {
        try
        {
            // MacOsKeychainProvider uses the system keychain.
            // If errSecAuthFailed or similar is returned on an unsigned dev build,
            // fall through to the encrypted file provider.
            return new MacOsKeychainProvider();
        }
        catch (Exception)
        {
            return new EncryptedFileKeychainProvider(configDir);
        }
    }

    [SupportedOSPlatform("windows")]
    private static IKeychainProvider TryCreateWindows() => new WindowsKeychainProvider();

    [SupportedOSPlatform("linux")]
    private static IKeychainProvider TryCreateLinux(string configDir)
    {
        if (LinuxKeychainProvider.TryCreate(out var linux) && linux is not null)
            return linux;

        return new EncryptedFileKeychainProvider(configDir);
    }
}

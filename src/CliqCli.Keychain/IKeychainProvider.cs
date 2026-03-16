namespace CliqCli.Keychain;

/// <summary>
/// Abstraction for OS-native credential storage. Implementations exist for
/// macOS Keychain Services, Windows Credential Manager, Linux Secret Service,
/// and an AES-256-GCM encrypted-file fallback.
/// </summary>
public interface IKeychainProvider
{
    /// <summary>Retrieves a previously stored secret by its key.</summary>
    /// <param name="key">The storage key, e.g. <c>cliq-cli:work:pat</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored secret, or <see langword="null"/> if not found.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores or updates a secret under the given key.</summary>
    /// <param name="key">The storage key, e.g. <c>cliq-cli:work:pat</c>.</param>
    /// <param name="value">The secret value to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Deletes the secret associated with the given key.</summary>
    /// <param name="key">The storage key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the item was deleted; <see langword="false"/> if it did not exist.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

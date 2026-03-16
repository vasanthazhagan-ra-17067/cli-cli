namespace CliqCli.Core.Auth;

/// <summary>
/// Pluggable authentication provider interface (ADR-0005).
/// v1 concrete: <see cref="PatAuthProvider"/>.
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Retrieves the stored token for the named account from the OS keychain.
    /// Throws <see cref="CliqCliException"/> with code <c>AUTH_FAILURE</c> (exit 2) if not found.
    /// </summary>
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);

    /// <summary>
    /// Stores the token in the OS keychain. Applies the ZohoCorp domain block (ADR-0006)
    /// before any write if <paramref name="email"/> is provided.
    /// </summary>
    /// <param name="accountName">Logical account name (used as part of the keychain key).</param>
    /// <param name="token">The PAT or OAuth bearer token to store.</param>
    /// <param name="email">Optional email address. When present, ZohoCorp domain is checked first.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreTokenAsync(string accountName, string token, string? email = null, CancellationToken ct = default);

    /// <summary>Removes the token from the OS keychain for the named account.</summary>
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}

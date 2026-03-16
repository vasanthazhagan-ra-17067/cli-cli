namespace CliqCli.Core.Accounts;

/// <summary>
/// Orchestrates all account management operations.
/// Business logic lives here; command classes stay thin (ADR-0002, ADR-0009).
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Validates the PAT, retrieves the user's email from the Zoho user-info API,
    /// enforces the ZohoCorp domain block, stores the token in the keychain, and
    /// persists account metadata to <c>accounts.json</c>.
    /// </summary>
    Task<AccountAddResult> AddAsync(
        string name,
        string token,
        string domain,
        CancellationToken ct = default);

    /// <summary>Returns all accounts mapped to list DTOs — raw token value never included.</summary>
    Task<IReadOnlyList<AccountListItem>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns full account detail for the named account with token field set to <c>***</c>.</summary>
    Task<AccountShowDetail> ShowAsync(string name, CancellationToken ct = default);

    /// <summary>Removes the named account from both the keychain and <c>accounts.json</c>.</summary>
    Task RemoveAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>is_default = true</c> for the named account and <c>false</c> for all others.
    /// </summary>
    Task SetDefaultAsync(string name, CancellationToken ct = default);
}

namespace CliqCli.Core.Accounts;

/// <summary>
/// Persistent storage for the accounts.json configuration file.
/// </summary>
public interface IAccountStore
{
    /// <summary>Loads all accounts from disk. Returns an empty root if the file does not exist.</summary>
    Task<AccountsRoot> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the entire accounts root to disk with restrictive file permissions (0600 on Unix).</summary>
    Task SaveAsync(AccountsRoot root, CancellationToken ct = default);

    /// <summary>Returns the named account, or <see langword="null"/> if not found.</summary>
    Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Returns the default account (IsDefault=true, or first account if no explicit default exists).
    /// Throws <see cref="CliqCliException"/> with code <c>NO_DEFAULT_ACCOUNT</c> if no accounts are configured.
    /// </summary>
    Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default);
}

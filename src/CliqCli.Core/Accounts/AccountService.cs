using CliqCli.Core.Auth;

namespace CliqCli.Core.Accounts;

/// <summary>
/// Implements <see cref="IAccountService"/> by coordinating
/// <see cref="IAccountStore"/>, <see cref="IAuthProvider"/>, and <see cref="IUserInfoService"/>.
/// All decision logic for account operations lives here; command classes stay thin.
/// </summary>
public sealed class AccountService : IAccountService
{
    // Zoho datacenter domains accepted for the --domain flag (prevents SSRF).
    private static readonly HashSet<string> AllowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "zoho.com", "zoho.eu", "zoho.in", "zoho.com.au", "zoho.jp"
    };

    private readonly IAccountStore _store;
    private readonly IAuthProvider _auth;
    private readonly IUserInfoService _userInfo;

    public AccountService(IAccountStore store, IAuthProvider auth, IUserInfoService userInfo)
    {
        _store = store;
        _auth = auth;
        _userInfo = userInfo;
    }

    // ─── IAccountService ──────────────────────────────────────────────────────

    public async Task<AccountAddResult> AddAsync(
        string name,
        string token,
        string domain,
        CancellationToken ct = default)
    {
        // Guard: only recognised Zoho datacenter domains are allowed (SSRF prevention)
        if (!AllowedDomains.Contains(domain))
            throw new CliqCliException(
                $"Domain '{domain}' is not a recognised Zoho datacenter domain. " +
                "Allowed values: zoho.com, zoho.eu, zoho.in, zoho.com.au, zoho.jp.",
                ErrorCodes.InvalidArgs, 1);

        // (1) Ensure name is unique
        var existing = await _store.FindAsync(name, ct).ConfigureAwait(false);
        if (existing is not null)
            throw new CliqCliException(
                $"Account '{name}' already exists.",
                ErrorCodes.AccountAlreadyExists, 1);

        // (2) Retrieve email from Zoho user-info API
        var email = await _userInfo.GetUserEmailAsync(domain, token, ct).ConfigureAwait(false);

        // (3) Store token in keychain — ZohoCorpGuard fires inside PatAuthProvider.StoreTokenAsync
        await _auth.StoreTokenAsync(name, token, email, ct).ConfigureAwait(false);

        // (4) Persist account metadata
        var root = await _store.LoadAsync(ct).ConfigureAwait(false);
        var isDefault = root.Accounts.Count == 0; // first account becomes default automatically

        var entry = new AccountEntry
        {
            Name = name,
            Domain = domain,
            Email = email,
            TokenType = "pat",
            IsDefault = isDefault,
            NeedsReauth = false
        };

        var updated = root with { Accounts = [.. root.Accounts, entry] };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);

        return new AccountAddResult(name, domain);
    }

    public async Task<IReadOnlyList<AccountListItem>> ListAsync(CancellationToken ct = default)
    {
        var root = await _store.LoadAsync(ct).ConfigureAwait(false);
        return root.Accounts
            .Select(a => new AccountListItem(
                a.Name, a.Domain, a.Email,
                a.Scopes.Count, a.TokenType, a.IsDefault, a.NeedsReauth))
            .ToList()
            .AsReadOnly();
    }

    public async Task<AccountShowDetail> ShowAsync(string name, CancellationToken ct = default)
    {
        var account = await _store.FindAsync(name, ct).ConfigureAwait(false);
        if (account is null)
            throw new CliqCliException(
                $"Account '{name}' not found.",
                ErrorCodes.AccountNotFound, 1);

        return new AccountShowDetail(
            account.Name, account.Domain, account.Email,
            account.Scopes.ToList(), account.TokenType, account.IsDefault, account.NeedsReauth,
            Token: "***");
    }

    public async Task RemoveAsync(string name, CancellationToken ct = default)
    {
        var account = await _store.FindAsync(name, ct).ConfigureAwait(false);
        if (account is null)
            throw new CliqCliException(
                $"Account '{name}' not found.",
                ErrorCodes.AccountNotFound, 1);

        await _auth.ClearTokenAsync(name, ct).ConfigureAwait(false);

        var root = await _store.LoadAsync(ct).ConfigureAwait(false);
        var updated = root with
        {
            Accounts = root.Accounts
                .Where(a => !a.Name.Equals(name, StringComparison.Ordinal))
                .ToList()
        };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
    }

    public async Task SetDefaultAsync(string name, CancellationToken ct = default)
    {
        var root = await _store.LoadAsync(ct).ConfigureAwait(false);

        if (!root.Accounts.Any(a => a.Name.Equals(name, StringComparison.Ordinal)))
            throw new CliqCliException(
                $"Account '{name}' not found.",
                ErrorCodes.AccountNotFound, 1);

        var updated = root with
        {
            Accounts = root.Accounts
                .Select(a => a with { IsDefault = a.Name.Equals(name, StringComparison.Ordinal) })
                .ToList()
        };
        await _store.SaveAsync(updated, ct).ConfigureAwait(false);
    }
}

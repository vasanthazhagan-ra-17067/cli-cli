using CliqCli.Keychain;

namespace CliqCli.Core.Auth;

/// <summary>
/// PAT-based authentication provider (ADR-0005 v1).
/// Stores and retrieves Personal Access Tokens via the OS keychain.
/// Key format: <c>cliq-cli:&lt;accountName&gt;:pat</c>.
/// </summary>
public sealed class PatAuthProvider : IAuthProvider
{
    private readonly IKeychainProvider _keychain;

    public PatAuthProvider(IKeychainProvider keychain) => _keychain = keychain;

    // ─── IAuthProvider ────────────────────────────────────────────────────────

    public async Task<string> GetTokenAsync(string accountName, CancellationToken ct = default)
    {
        var token = await _keychain.GetAsync(KeyFor(accountName), ct).ConfigureAwait(false);
        if (token is null)
            throw new CliqCliException(
                $"No credential found for account '{accountName}'. Run 'cliq-cli account add' to configure it.",
                ErrorCodes.AuthFailure,
                exitCode: 2);
        return token;
    }

    public async Task StoreTokenAsync(
        string accountName,
        string token,
        string? email = null,
        CancellationToken ct = default)
    {
        // ZohoCorp block — must run BEFORE any keychain write (ADR-0006)
        ZohoCorpGuard.AssertNotZohoCorp(email);
        await _keychain.SetAsync(KeyFor(accountName), token, ct).ConfigureAwait(false);
    }

    public async Task ClearTokenAsync(string accountName, CancellationToken ct = default)
    {
        await _keychain.DeleteAsync(KeyFor(accountName), ct).ConfigureAwait(false);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string KeyFor(string accountName) => $"cliq-cli:{accountName}:pat";
}

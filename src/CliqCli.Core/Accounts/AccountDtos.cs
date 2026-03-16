namespace CliqCli.Core.Accounts;

/// <summary>Output DTO for <c>account add</c>. Contains name and domain only — no token.</summary>
public sealed record AccountAddResult(string Name, string Domain);

/// <summary>
/// Output DTO for <c>account list</c>.
/// Token value is intentionally absent; <see cref="ScopeCount"/> is computed from the scope list.
/// </summary>
public sealed record AccountListItem(
    string Name,
    string Domain,
    string? Email,
    int ScopeCount,
    string TokenType,
    bool IsDefault,
    bool NeedsReauth);

/// <summary>
/// Output DTO for <c>account show</c>. Includes the full scope list and a masked token sentinel.
/// </summary>
public sealed record AccountShowDetail(
    string Name,
    string Domain,
    string? Email,
    List<string> Scopes,
    string TokenType,
    bool IsDefault,
    bool NeedsReauth,
    string Token);

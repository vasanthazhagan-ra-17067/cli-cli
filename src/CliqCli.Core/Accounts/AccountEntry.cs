namespace CliqCli.Core.Accounts;

/// <summary>
/// Persisted account record. The token is never stored here — it lives in the OS keychain only.
/// </summary>
public sealed record AccountEntry
{
    public required string Name { get; init; }
    public required string Domain { get; init; }
    public string? Email { get; init; }
    public List<string> Scopes { get; init; } = [];
    public required string TokenType { get; init; }
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }
}

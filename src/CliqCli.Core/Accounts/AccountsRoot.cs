namespace CliqCli.Core.Accounts;

/// <summary>Root DTO for accounts.json.</summary>
public sealed record AccountsRoot
{
    public List<AccountEntry> Accounts { get; init; } = [];
}

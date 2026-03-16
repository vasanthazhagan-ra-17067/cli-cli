# 5. Data Models

## `AccountEntry`

```csharp
public sealed record AccountEntry
{
    public required string Name { get; init; }
    public required string Domain { get; init; }           // e.g. "zoho.com"
    public List<string> Scopes { get; init; } = [];
    public required string TokenType { get; init; }        // "pat" | "oauth"
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }
}
```

## `AccountsRoot` (accounts.json root)

```csharp
public sealed record AccountsRoot
{
    public List<AccountEntry> Accounts { get; init; } = [];
}
```

## `accounts.json` Example

```json
{
  "accounts": [
    {
      "name": "work",
      "domain": "zoho.com",
      "scopes": ["ZohoCliq.Channels.READ", "ZohoCliq.Messages.WRITE"],
      "token_type": "pat",
      "is_default": true,
      "needs_reauth": false
    },
    {
      "name": "personal",
      "domain": "zoho.eu",
      "scopes": [],
      "token_type": "pat",
      "is_default": false,
      "needs_reauth": false
    }
  ]
}
```

JSON property names use `snake_case` (configured via `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`).

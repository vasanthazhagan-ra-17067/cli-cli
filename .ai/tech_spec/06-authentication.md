# 6. Authentication

## Interface

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default);
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}
```

## v1 — `PatAuthProvider`

- Implements `IAuthProvider`.
- `StoreTokenAsync` → writes to OS keychain under key `cliq-cli:<accountName>:pat`.
- `GetTokenAsync` → reads from OS keychain by the same key.
- `ClearTokenAsync` → deletes from OS keychain.
- Token is injected into requests as: `Authorization: Zoho-oauthtoken <token>`.

## Future — `OAuthProvider`

Full OAuth2 implementation. All token exchange happens via the Zoho OAuth API in-terminal — no browser redirect. Deferred to v2.

---

## PAT Add Flow

```
cliq-cli account add --name "work" --token "xxx" [--domain "zoho.com"]
  1. Validate name is unique in accounts.json
  2. Call PatAuthProvider.StoreTokenAsync("work", "xxx")
  3. Append AccountEntry to accounts.json (token never written to JSON)
  4. If no other account exists, set is_default = true
  5. Write accounts.json (permissions: 0600 on Unix)
  6. Output: { "status": "ok", "data": { "name": "work", "domain": "zoho.com" } }
```

## Scope Change + Re-auth Flow

```
cliq-cli scope add --scope "ZohoCliq.Messages.READ" [--account "work"]
  1. Resolve target account (--account or default)
  2. Add scope to AccountEntry.Scopes if not already present
  3. Set AccountEntry.NeedsReauth = true
  4. Write accounts.json
  5. Output: { "status": "ok", "data": { "account": "work", "scopes": [...] } }

Next api call on account "work":
  1. Load AccountEntry for "work"
  2. If NeedsReauth == true → write error to stderr + exit 2

cliq-cli account re-auth --name "work"   [v2 OAuth only]
  1. Exchange new token via Zoho OAuth API
  2. Call OAuthProvider.StoreTokenAsync (or equivalent)
  3. Set AccountEntry.NeedsReauth = false
  4. Write accounts.json
```

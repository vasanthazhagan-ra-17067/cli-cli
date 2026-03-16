# 8. API Client

## `ApiClient`

```csharp
public sealed class ApiClient
{
    // Constructs base URL from account domain:
    //   https://cliq.{domain}/api/v2
    // Prepends /api/v2 to --path if not already present.
    // Injects: Authorization: Zoho-oauthtoken <token>
    // Injects: Content-Type: application/json  (for POST/PUT/PATCH)

    public Task<ApiResponse> CallAsync(ApiRequest request, CancellationToken ct = default);
}

public sealed record ApiRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public string? Body { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
    public Dictionary<string, string> QueryParams { get; init; } = [];
    public required string AccountName { get; init; }
}

public sealed record ApiResponse
{
    public int StatusCode { get; init; }
    public required string Body { get; init; }          // raw JSON string from server
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
```

## Base URL Resolution

```
account.Domain = "zoho.com"
→ BaseUrl = "https://cliq.zoho.com/api/v2"

--path "/api/v2/channels"  → https://cliq.zoho.com/api/v2/channels
--path "/channels"          → https://cliq.zoho.com/api/v2/channels  (prefix auto-inserted)
```

| Datacenter | `domain` field | Base URL |
|------------|----------------|----------|
| US | `zoho.com` | `https://cliq.zoho.com/api/v2` |
| EU | `zoho.eu` | `https://cliq.zoho.eu/api/v2` |
| IN | `zoho.in` | `https://cliq.zoho.in/api/v2` |
| AU | `zoho.com.au` | `https://cliq.zoho.com.au/api/v2` |

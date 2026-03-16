# cliq-cli — Technical Specification

> **Status:** Draft
> **Derived from:** `tasks/cliq-cli/brainstorming.md`
> **Date:** 2026-03-16

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Runtime & Language](#2-runtime--language)
3. [Project Structure](#3-project-structure)
4. [CLI Structure & Commands](#4-cli-structure--commands)
5. [Data Models](#5-data-models)
6. [Authentication](#6-authentication)
7. [Storage](#7-storage)
8. [API Client](#8-api-client)
9. [Output Contract](#9-output-contract)
10. [Error Handling](#10-error-handling)
11. [Dependencies](#11-dependencies)
12. [Non-Goals (v1)](#12-non-goals-v1)

---

## 1. Project Overview

**cliq-cli** is a standalone, multi-platform CLI binary for interacting with Zoho Cliq APIs. It manages multiple Zoho accounts, handles authentication via a pluggable `IAuthProvider` interface, and exposes a general-purpose HTTP API invoker.

**Primary consumer:** AI Agents via GitHub Copilot CLI Skills (`.github/skills/cliq-cli/SKILL.md`).

**Scope:**
- v1: Account management, scope management, REST API invocation (PAT auth)
- Future: OAuth2 re-auth, WebSocket connections, Pex real-time chat

---

## 2. Runtime & Language

| Concern | Choice |
|---------|--------|
| Runtime | .NET 10 |
| Language | C# 13+ |
| Target frameworks | `net10.0` |
| Nullable reference types | Enabled (`<Nullable>enable</Nullable>`) |
| Implicit usings | Enabled |
| Publish | `dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true` |
| Supported RIDs | `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64` |

---

## 3. Project Structure

```
cliq-cli/
├── src/
│   ├── CliqCli/                              ← Entry point + Spectre command wiring
│   │   ├── CliqCli.csproj
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── AccountCommands.cs            ← add, list, remove, show, set-default, re-auth
│   │       ├── ScopeCommands.cs              ← add, remove, list
│   │       └── ApiCommands.cs                ← api call
│   │
│   ├── CliqCli.Core/                         ← Domain logic (no CLI concerns)
│   │   ├── CliqCli.Core.csproj
│   │   ├── Auth/
│   │   │   ├── IAuthProvider.cs
│   │   │   └── PatAuthProvider.cs
│   │   ├── Accounts/
│   │   │   ├── AccountStore.cs               ← JSON config read/write
│   │   │   └── AccountConfig.cs              ← AccountEntry model + AccountsRoot DTO
│   │   └── Api/
│   │       └── ApiClient.cs                  ← HttpClient wrapper; injects auth header
│   │
│   └── CliqCli.Keychain/                     ← OS keychain abstraction
│       ├── CliqCli.Keychain.csproj
│       ├── IKeychainProvider.cs
│       ├── MacOsKeychainProvider.cs           ← macOS Security.framework via P/Invoke
│       ├── WindowsKeychainProvider.cs         ← Windows Credential Manager via P/Invoke
│       ├── LinuxKeychainProvider.cs           ← libsecret / Secret Service via P/Invoke
│       └── EncryptedFileKeychainProvider.cs   ← fallback: AES-encrypted file
│
└── tests/
    └── CliqCli.Tests/
        ├── CliqCli.Tests.csproj
        ├── AccountStoreTests.cs
        ├── PatAuthProviderTests.cs
        └── ApiClientTests.cs
```

### Project References

```
CliqCli  →  CliqCli.Core
CliqCli  →  CliqCli.Keychain
CliqCli.Core  →  CliqCli.Keychain
```

---

## 4. CLI Structure & Commands

### Root

```
cliq-cli [global-flags] <group> <subcommand> [flags]
```

### Global Flags

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--account` | string | active account | Override account for this invocation |
| `--json` | bool | `true` | Force JSON output to stdout |
| `--no-input` | bool | `false` | Never prompt; fail instead |
| `--help` | — | — | Show help for current command/group |
| `--version` | — | — | Print version and exit |

---

### Group: `account`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--name` (req), `--token` (req), `--domain` (default: `zoho.com`) | Add a PAT-backed account |
| `list` | — | List all configured accounts (token masked) |
| `remove` | `--name` (req) | Remove account + clear keychain secret |
| `show` | `--name` (req) | Show account details (token masked as `***`) |
| `set-default` | `--name` (req) | Set active/default account |
| `re-auth` | `--name` (req) | Re-authenticate after scope change *(OAuth — v2)* |

---

### Group: `scope`

All scope subcommands target an account. `--account` defaults to the active account if omitted.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--scope` (req), `[--account]` | Add an OAuth scope to an account |
| `remove` | `--scope` (req), `[--account]` | Remove an OAuth scope from an account |
| `list` | `[--account]` | List all scopes for an account |

Adding or removing a scope sets `needs_reauth = true` on that account.

---

### Group: `api`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `call` | `--method` (req), `--path` (req), `[--body]`, `[--body-file]`, `[--header]` (repeatable), `[--query]` (repeatable), `[--account]` | Invoke a Cliq REST API endpoint |

**`api call` flag details:**

| Flag | Type | Description |
|------|------|-------------|
| `--method` | `GET\|POST\|PUT\|PATCH\|DELETE` | HTTP method |
| `--path` | string | URL path, e.g. `/api/v2/channels` |
| `--body` | string | Inline JSON request body |
| `--body-file` | string | Path to a JSON file to use as request body |
| `--header` | `key:value` | Additional request header (repeatable) |
| `--query` | `key=value` | Query string parameter (repeatable) |
| `--account` | string | Account override for this call |

`--body` and `--body-file` are mutually exclusive. `/api/v2` prefix is inserted automatically if `--path` does not start with `/api/`.

---

### Group: `ws` *(Future)*

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `connect` | `--url` (req), `--name` (req), `[--account]` | Open a named WebSocket connection |
| `send` | `--name` (req), `--message` (req) | Send a message |
| `listen` | `--name` (req) | Stream incoming messages to stdout as JSON lines |
| `close` | `--name` (req) | Close the connection |

---

### Group: `pex` *(Future)*

> Pex is Zoho Cliq's proprietary real-time protocol (ping-pong + chat message delivery).

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `connect` | `--channel` (req), `[--account]` | Open a Pex session |
| `send` | `--session` (req), `--message` (req) | Send a message over Pex |
| `listen` | `--session` (req) | Stream incoming Pex messages to stdout as JSON lines |
| `close` | `--session` (req) | Close the Pex session |

---

## 5. Data Models

### `AccountEntry`

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

### `AccountsRoot` (accounts.json root)

```csharp
public sealed record AccountsRoot
{
    public List<AccountEntry> Accounts { get; init; } = [];
}
```

### `accounts.json` Example

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

---

## 6. Authentication

### Interface

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default);
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}
```

### v1 — `PatAuthProvider`

- Implements `IAuthProvider`.
- `StoreTokenAsync` → writes to OS keychain under key `cliq-cli:<accountName>:pat`.
- `GetTokenAsync` → reads from OS keychain by the same key.
- `ClearTokenAsync` → deletes from OS keychain.
- Token is injected into requests as: `Authorization: Zoho-oauthtoken <token>`.

### PAT Add Flow

```
cliq-cli account add --name "work" --token "xxx" [--domain "zoho.com"]
  1. Validate name is unique in accounts.json
  2. Call PatAuthProvider.StoreTokenAsync("work", "xxx")
  3. Append AccountEntry to accounts.json (token never written to JSON)
  4. If no other account exists, set is_default = true
  5. Write accounts.json (permissions: 0600 on Unix)
  6. Output: { "status": "ok", "data": { "name": "work", "domain": "zoho.com" } }
```

### Scope Change + Re-auth Flow

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
  2. Call PatAuthProvider.StoreTokenAsync (or OAuthProvider equivalent)
  3. Set AccountEntry.NeedsReauth = false
  4. Write accounts.json
```

---

## 7. Storage

### Paths

| Platform | Config Directory |
|----------|-----------------|
| macOS | `~/Library/Application Support/cliq-cli/` |
| Windows | `%LOCALAPPDATA%\cliq-cli\` |
| Linux | `~/.config/cliq-cli/` |

Path resolution uses `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` on macOS/Linux and `Environment.SpecialFolder.LocalApplicationData` on Windows.

### Files

| File | Location | Format |
|------|----------|--------|
| `accounts.json` | `<configDir>/accounts.json` | Plain JSON, `snake_case` |
| Secrets | OS Keychain | OS-managed, never on disk |
| Keychain fallback | `<configDir>/keystore/<accountName>.bin` | AES-256 encrypted |

### Security Rules

- `accounts.json` written with `0600` permissions on Unix (via `File.SetUnixFileMode`).
- On Windows, NTFS ACL restricts read/write to the current user only.
- Tokens are **never** written to `accounts.json`, stdout, or any log.
- Error messages never include raw token values.

### `AccountStore` Contract

```csharp
public interface IAccountStore
{
    Task<AccountsRoot> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AccountsRoot root, CancellationToken ct = default);
    Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default);
    Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default);  // throws if none
}
```

### `IKeychainProvider` Contract

```csharp
public interface IKeychainProvider
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

// Key format: "cliq-cli:<accountName>:<tokenType>"
// e.g.        "cliq-cli:work:pat"
```

**Platform implementations:**

| Platform | Class | Backend |
|----------|-------|---------|
| macOS | `MacOsKeychainProvider` | Security.framework (`SecKeychainAddGenericPassword`) via P/Invoke |
| Windows | `WindowsKeychainProvider` | `CredWrite` / `CredRead` via P/Invoke |
| Linux | `LinuxKeychainProvider` | `libsecret` Secret Service API via P/Invoke |
| Fallback | `EncryptedFileKeychainProvider` | AES-256-GCM, key derived from machine entropy |

Runtime detection via `RuntimeInformation.IsOSPlatform(...)`. Fallback is used when the OS keychain is unavailable.

---

## 8. API Client

### `ApiClient`

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

### Base URL Resolution

```
account.Domain = "zoho.com"
→ BaseUrl = "https://cliq.zoho.com/api/v2"

--path "/api/v2/channels"  → https://cliq.zoho.com/api/v2/channels
--path "/channels"          → https://cliq.zoho.com/api/v2/channels  (prefix inserted)
```

---

## 9. Output Contract

All output goes through a central `IOutputWriter` interface so tests can capture it without console side effects.

### stdout — Success

```json
{ "status": "ok", "data": <raw API response or command result> }
```

For `account list`:
```json
{
  "status": "ok",
  "data": [
    { "name": "work", "domain": "zoho.com", "token_type": "pat", "is_default": true, "needs_reauth": false, "scope_count": 2 },
    { "name": "personal", "domain": "zoho.eu", "token_type": "pat", "is_default": false, "needs_reauth": false, "scope_count": 0 }
  ]
}
```

Token value is **never** included in any output. `account show` displays `"token": "***"`.

### stderr — Error Envelope

```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <0|1|2> }
```

### Error Codes

| Code | Exit | Meaning |
|------|------|---------|
| `ACCOUNT_NOT_FOUND` | 1 | Named account does not exist |
| `ACCOUNT_ALREADY_EXISTS` | 1 | `account add` with duplicate name |
| `NO_DEFAULT_ACCOUNT` | 1 | No active account set and `--account` not provided |
| `AUTH_FAILURE` | 2 | Keychain read failed or token rejected by API |
| `NEEDS_REAUTH` | 2 | Account has pending scope changes |
| `API_ERROR` | 1 | Non-2xx response from Cliq API |
| `INVALID_ARGS` | 1 | Missing or conflicting flags |
| `IO_ERROR` | 1 | File system failure (accounts.json read/write) |
| `KEYCHAIN_ERROR` | 2 | OS keychain operation failed |

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | General / recoverable error |
| `2` | Auth failure / needs-reauth |

---

## 10. Error Handling

- All exceptions are caught at the top-level command executor and converted to the JSON error envelope written to stderr.
- `--no-input` flag: any code path that would prompt must throw `InvalidOperationException` with code `INVALID_ARGS` instead.
- HTTP errors from `ApiClient`: non-2xx responses are converted to `API_ERROR` with the response body included in `"detail"`.
- Cancellation (`Ctrl+C`): graceful cancellation via `CancellationTokenSource`; exit code `1`.

---

## 11. Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Spectre.Console.Cli` | latest stable | CLI command/subcommand/flag framework |
| `System.Text.Json` | (stdlib, .NET 10) | JSON serialization |
| `Microsoft.Extensions.DependencyInjection` | latest stable | DI container |
| `Microsoft.Extensions.Logging` | latest stable | Structured logging |
| `Microsoft.Extensions.Logging.Console` | latest stable | Console log sink |
| `xunit` | latest stable | Unit testing |
| `xunit.runner.visualstudio` | latest stable | Test runner integration |
| `Microsoft.NET.Test.Sdk` | latest stable | Test SDK |

No external keychain NuGet — platform keychain access is implemented via direct P/Invoke to keep the binary self-contained.

---

## 12. Non-Goals (v1)

- No OAuth2 implementation — PAT only.
- No browser-based auth or redirect flows.
- No WebSocket or Pex support.
- No TUI / interactive shell mode.
- No plugin system or extensibility hooks.
- No Native AOT publishing.
- No multi-account simultaneous API calls (one active account per invocation).
- No sync/caching layer — all API calls are live.

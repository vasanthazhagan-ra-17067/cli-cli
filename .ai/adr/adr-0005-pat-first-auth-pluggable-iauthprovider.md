---
title: "ADR-0005: PAT-First Authentication with Pluggable IAuthProvider Interface"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "authentication", "pat", "oauth", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0005: PAT-First Authentication with Pluggable IAuthProvider Interface

## Status

**Proposed**

## Context

`cliq-cli` must authenticate against the Zoho Cliq API on behalf of users. The auth mechanism must:

- Work without a browser, redirect flow, or interactive session — the primary consumer is an AI agent running inside a CI-like environment.
- Not require the user to run a local HTTP server or handle OAuth callback URLs.
- Support future migration to OAuth2 (with `scope` management already being tracked in `accounts.json`) without rewriting the auth-consuming code in `ApiClient` or the command layer.
- Inject the correct auth header (`Authorization: Zoho-oauthtoken <token>`) into every HTTP request transparently.

Zoho Cliq supports two auth mechanisms:
1. **Personal Access Token (PAT)** — a long-lived token generated manually via the Zoho developer console. Simple to issue, no browser flow required.
2. **OAuth 2.0** — token exchange flow requiring a browser redirect or client credentials; supports fine-grained scopes and token refresh.

## Decision

Implement **PAT as the sole v1 authentication mechanism**, exposed through a pluggable `IAuthProvider` interface:

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default);
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}
```

The v1 concrete implementation is `PatAuthProvider`:
- `StoreTokenAsync` → delegates to `IKeychainProvider.SetAsync("cliq-cli:<accountName>:pat", token)`.
- `GetTokenAsync` → delegates to `IKeychainProvider.GetAsync("cliq-cli:<accountName>:pat")`.
- `ClearTokenAsync` → delegates to `IKeychainProvider.DeleteAsync("cliq-cli:<accountName>:pat")`.

`ApiClient` calls `IAuthProvider.GetTokenAsync()` before each request and injects the result as:
```
Authorization: Zoho-oauthtoken <token>
```

The `scope` command group and the `NeedsReauth` / `Scopes` fields on `AccountEntry` are scaffolded now, even though OAuth2 re-auth is not implemented, to avoid a breaking schema change when OAuth2 is added in a future version.

## Consequences

### Positive

- **POS-001**: PAT requires zero runtime browser interaction; `account add --token <pat>` is the entire setup, making it fully scriptable for AI agents and CI workflows.
- **POS-002**: The `IAuthProvider` interface decouples `ApiClient` from any specific auth mechanism; introducing `OAuthProvider` in v2 requires only a new implementation class and a DI registration change — no changes to command or client code.
- **POS-003**: Scaffolding the `scope` group and `NeedsReauth` flag now means `accounts.json` schema is already OAuth2-aware; v2 can implement `re-auth` without migrating existing account data.
- **POS-004**: `IAuthProvider` is injectable and replaceable in unit tests with a mock that returns a fixed token, making `ApiClient` fully testable without any OS keychain interaction.
- **POS-005**: The uniform `Authorization: Zoho-oauthtoken <token>` header format works for both PAT and OAuth2 bearer tokens; `ApiClient` does not need to know which token type it is injecting.

### Negative

- **NEG-001**: PATs are long-lived and do not auto-expire; if a token is compromised, the user must manually revoke it and re-add the account. There is no automatic rotation mechanism in v1.
- **NEG-002**: PATs typically grant broad access; in the absence of OAuth2 scope enforcement at the token level, the `scope` management features in `cliq-cli` are advisory metadata only — they do not restrict what a PAT can actually do.
- **NEG-003**: The `account re-auth` subcommand is exposed in the CLI as a visible but non-functional stub in v1; it must display a clear "not yet supported" error to avoid confusing users.
- **NEG-004**: Scoping all auth through a single `IAuthProvider` instance (resolved via DI) means v1 cannot use different auth mechanisms for different accounts in the same session; this is acceptable now but limits multi-auth-type support if both PAT and OAuth2 accounts co-exist in a future version.

## Alternatives Considered

### OAuth2 Client Credentials in v1

- **ALT-001**: **Description**: Implement the OAuth2 client credentials flow (machine-to-machine, no browser) using the Zoho OAuth2 endpoint. This grants more granular, scope-controlled tokens.
- **ALT-002**: **Rejection Reason**: Client credentials flow requires a client ID and client secret to be provisioned per installation, which introduces an out-of-band registration step not suitable for a self-contained CLI tool. PATs achieve the same zero-browser-interaction goal with a simpler setup story for v1.

### OAuth2 Authorization Code Flow in v1

- **ALT-003**: **Description**: Implement the full browser-based authorization code flow, opening the default browser and spinning up a local HTTP redirect listener.
- **ALT-004**: **Rejection Reason**: The primary consumer (AI agent) has no browser and cannot handle redirect flows. Browser-based auth is explicitly listed as a v1 Non-Goal.

### Environment variable only (no stored credentials)

- **ALT-005**: **Description**: Accept the PAT only via an environment variable (e.g., `CLIQ_TOKEN`) per invocation, with no persistent storage.
- **ALT-006**: **Rejection Reason**: While this covers CI use cases, it does not solve the multi-account developer workflow. Without persistent storage and account naming, switching between `work`, `personal`, and `staging` accounts requires the caller to manage token values externally. The keychain-backed account model is strictly more capable.

## Implementation Notes

- **IMP-001**: `PatAuthProvider` must be registered in DI as `IAuthProvider`. When OAuth2 is added in v2, the registration should use a factory that inspects `AccountEntry.TokenType` to select the appropriate provider per account.
- **IMP-002**: `account re-auth` must be wired in the command tree in v1 but must exit immediately with a structured error: `{ "error": "re-auth is not supported in v1; please use 'account remove' and 're-add' with a new PAT", "code": "NOT_IMPLEMENTED", "exitCode": 1 }`.
- **IMP-003**: Success is measured by: `api call --method GET --path /channels` returns the expected Cliq API response in the JSON success envelope when a valid PAT is stored; the same command exits with `AUTH_FAILURE` (exit 2) when the stored token is invalid.

## References

- **REF-001**: [ADR-0003: OS-Native Keychain with AES-256-GCM Encrypted File Fallback](adr-0003-os-keychain-credential-storage.md)
- **REF-002**: [ADR-0004: JSON-Only Output Contract](adr-0004-json-only-output-contract.md)
- **REF-003**: Zoho OAuth2 documentation — https://www.zoho.com/accounts/protocol/oauth.html
- **REF-004**: [cliq-cli Technical Specification](../../tasks/cliq-cli/tech_spec.md) §6 Authentication, §12 Non-Goals

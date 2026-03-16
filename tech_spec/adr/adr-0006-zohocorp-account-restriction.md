---
title: "ADR-0006: Hard-Block ZohoCorp Accounts via Compile-Time Domain Check"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "security", "privacy", "authentication", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0006: Hard-Block ZohoCorp Accounts via Compile-Time Domain Check

## Status

**Proposed**

## Context

`cliq-cli` is designed to be invoked by AI agents (e.g. GitHub Copilot CLI Skills, Claude Agent Skills). It stores PAT credentials in the OS keychain and can fire arbitrary Cliq REST API requests on behalf of authenticated accounts.

ZohoCorp accounts (`*@zohocorp.com`, `*@zohocorp.eu`, etc.) are internal Zoho employee accounts tied to Zoho's own corporate infrastructure. Permitting an AI-agent-driven CLI tool to:

- Accept and persist credentials for ZohoCorp accounts
- Make API calls on behalf of ZohoCorp accounts

...creates an unacceptable privacy and security risk: an AI agent could exfiltrate internal corporate data, access internal channels, or be used to pivot into Zoho's own infrastructure unintentionally.

The block must be:
- **Unconditional** — not bypassable by end users, flags, or environment variables
- **Early** — enforced before any keychain write or HTTP request is dispatched
- **Comprehensive** — cover all current and future ZohoCorp datacenter TLDs automatically

## Decision

All operations involving a ZohoCorp-domain account are hard-blocked at the earliest possible entry point via a sealed, compile-time-only domain-label check in `CliqCli.Core`.

### Detection Rule

An account is identified as a ZohoCorp account when the first DNS label of its email address host equals `zohocorp` (case-insensitive):

```csharp
email.Split('@')[1].Split('.')[0]
      .Equals("zohocorp", StringComparison.OrdinalIgnoreCase)
```

This single label check covers `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and any future datacenter TLDs automatically — without maintaining an explicit list of known TLDs.

### Blocked Entry Points (in evaluation order)

| Entry point | When checked |
|-------------|-------------|
| `account add` | Email fetched from Zoho user-info API before any account is persisted; rejected if ZohoCorp domain |
| `api call` (active account) | Account's stored email verified before any HTTP request is dispatched |
| `api call --account <name>` | Same check applied to the named account override |
| Any `scope` command | Account resolved first; rejected if ZohoCorp domain before any mutation |

### Implementation Constraints

- The check runs inside both `PatAuthProvider.StoreTokenAsync` and `ApiClient.CallAsync` so it cannot be bypassed by introducing a new auth provider or command in future.
- On `account add`, the email is retrieved from the Zoho user-info endpoint before the account is persisted — the block applies regardless of whether the user passes `--email` explicitly.
- The blocked-label constant (`"zohocorp"`) lives in `CliqCli.Core` as a `private static readonly` or `const` — no runtime override path exists.
- If an existing `accounts.json` already contains a ZohoCorp account (e.g. migrated from an older version), any command that resolves to that account is rejected and a warning is emitted on startup.

### Error Output

```json
{
  "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.",
  "code": "ACCOUNT_DOMAIN_BLOCKED",
  "exitCode": 1
}
```

## Consequences

##### Positive

- **POS-001**: Eliminates the risk of AI agents inadvertently storing or operating on internal ZohoCorp credentials.
- **POS-002**: Single-label check is robust against all current and future datacenter TLDs — no TLD list to maintain.
- **POS-003**: Defence-in-depth: check implemented in both `PatAuthProvider` and `ApiClient`, making it impossible to bypass by future code changes at the command layer.
- **POS-004**: Clear, machine-readable error code (`ACCOUNT_DOMAIN_BLOCKED`) allows agent workflows to handle the rejection gracefully.

##### Negative

- **NEG-001**: Zoho employees who legitimately want to test `cliq-cli` against internal accounts cannot do so — they must use a personal or test account.
- **NEG-002**: Email retrieval from the Zoho user-info API at `account add` adds one extra network round-trip during account registration.
- **NEG-003**: If the user-info API call fails (e.g. invalid PAT), the error message may conflate auth failure with domain-block failure and require careful error path handling.

## Alternatives Considered

##### Runtime Flag / Environment Variable Override

- **ALT-001**: **Description**: Allow `--allow-zohocorp` flag or `CLIQ_CLI_ALLOW_ZOHOCORP=1` env var for operator override.
- **ALT-002**: **Rejection Reason**: Defeats the security purpose entirely — an AI agent could trivially set the env var or pass the flag. The threat model specifically covers unintentional agent misuse, which this does not address.

##### Allowlist of Permitted Domains (Inverse Approach)

- **ALT-003**: **Description**: Maintain a positive allowlist of permitted personal domains instead of a blocklist.
- **ALT-004**: **Rejection Reason**: Impossible to enumerate all valid personal Zoho domains; would require frequent updates and could inadvertently block legitimate external accounts.

##### Post-Hoc API-Level Rejection

- **ALT-005**: **Description**: Rely on Zoho's API server to reject ZohoCorp PATs when called from external IPs (if applicable).
- **ALT-006**: **Rejection Reason**: Cannot be relied upon — depends on server-side policy that is outside `cliq-cli`'s control and may change. The defence must be in the client.

##### No Block (Do Nothing)

- **ALT-007**: **Description**: Accept ZohoCorp accounts like any other account.
- **ALT-008**: **Rejection Reason**: Directly contradicts the privacy and security requirements; unacceptable given the AI-agent consumption model.

## Implementation Notes

- **IMP-001**: Add a `DomainGuard.IsZohoCorpEmail(string email)` static helper in `CliqCli.Core` — both `PatAuthProvider` and `ApiClient` call this helper, keeping the rule in one place.
- **IMP-002**: In `AccountStore.SaveAsync`, add a final guard that verifies no ZohoCorp account exists in the root before writing — belt-and-suspenders against a future code path bypassing `PatAuthProvider`.
- **IMP-003**: On startup, `AccountStore.LoadAsync` should scan for any existing ZohoCorp accounts and emit a `WARN`-level structured log entry for each one found.
- **IMP-004**: Unit tests must cover: (a) known ZohoCorp TLDs (`zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`), (b) a hypothetical future TLD (`zohocorp.xyz`), (c) non-ZohoCorp Zoho emails (`user@zoho.com`) — must pass, (d) case-insensitive match (`USER@ZOHOCORP.COM`).

## References

- **REF-001**: [tech_spec.md — Section 7: ZohoCorp Account Restriction](../tech_spec.md#7-zohocorp-account-restriction)
- **REF-002**: [brainstorming.md — Epic 6: ZohoCorp Account Restriction](../brainstorming.md#epic-6--zohocorp-account-restriction-privacy--security)
- **REF-003**: [ADR-0005: PAT-First Authentication with Pluggable IAuthProvider Interface](adr-0005-pat-first-auth-pluggable-iauthprovider.md)

---
title: "ADR-0007: Compile-Time HTTP Host Allowlist in ApiClient"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "security", "ssrf", "http", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0007: Compile-Time HTTP Host Allowlist in ApiClient

## Status

**Proposed**

## Context

`cliq-cli` is a general-purpose HTTP API invoker. The `api call` command accepts a `--path` argument from the caller (which, in the primary use case, is an AI agent). The path is concatenated with a base URL derived from the active account's `domain` field to form the full outgoing request URL.

This design creates a Server-Side Request Forgery (SSRF)-like risk: a malicious or misconfigured caller could craft a path that resolves to an arbitrary external host (e.g. via `../../` traversal in URLs, absolute URLs being passed as the path, or a compromised `accounts.json` domain field).

Additionally, the tool is the direct successor to `CliqApiMcp`, which already maintained an internal host allowlist — certain Cliq endpoints resolve to `*.zohoapis.com` / `*.zohoapis.in` rather than `*.zoho.com` and must be permitted.

The allowlist must be:
- **Enforced unconditionally** — checked on every outgoing request regardless of how the URL was constructed
- **Sealed at compile time** — not configurable via flags, environment variables, or config files
- **Comprehensive** — cover all Zoho datacenter domains used by Cliq APIs, including the `zohoapis.*` variants

## Decision

`ApiClient` validates the fully resolved URL host against a sealed compile-time allowlist before dispatching every HTTP request. Any request whose host does not end with an allowed suffix is rejected immediately with error code `HOST_NOT_ALLOWED`, exit 1 — no network I/O is performed.

### Allowed Host Suffixes

| Suffix | Covers |
|--------|--------|
| `.zoho.com` | US/AU Cliq REST API + Zoho Accounts endpoints |
| `.zoho.eu` | EU Cliq REST API |
| `.zoho.in` | IN Cliq REST API |
| `.zoho.com.au` | AU Cliq REST API |
| `.zohoapis.com` | US Cliq API domain (some internal endpoints resolve here) |
| `.zohoapis.in` | IN Cliq API domain |

Matching is a case-insensitive **suffix check on the host component** of the resolved URL — e.g. `cliq.zoho.com` passes because it ends with `.zoho.com`.

### Implementation

```csharp
private static readonly IReadOnlyList<string> AllowedHostSuffixes =
[
    ".zoho.com", ".zoho.eu", ".zoho.in", ".zoho.com.au",
    ".zohoapis.com", ".zohoapis.in"
];

private static void ValidateHost(Uri uri)
{
    var host = uri.Host;
    if (!AllowedHostSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        throw new CliqCliException("HOST_NOT_ALLOWED", $"Host '{host}' is not in the allowed list.", exitCode: 1);
}
```

`ValidateHost` is called at the start of `ApiClient.CallAsync`, after URL construction but before `HttpClient.SendAsync`.

### Error Output

```json
{
  "error": "Host '<host>' is not in the allowed list.",
  "code": "HOST_NOT_ALLOWED",
  "exitCode": 1
}
```

## Consequences

##### Positive

- **POS-001**: Eliminates SSRF risk — `cliq-cli` can never be used (inadvertently or intentionally) to probe internal networks or arbitrary external hosts.
- **POS-002**: Protects against a compromised `accounts.json` with a malicious `domain` field routing requests to an attacker-controlled server.
- **POS-003**: Clear, machine-readable error code (`HOST_NOT_ALLOWED`) surfaces misconfiguration immediately rather than silently sending requests to unintended destinations.
- **POS-004**: Consistent with the approach in `CliqApiMcp`'s `APIExecutor`, preserving compatibility with all Cliq API endpoints that use `zohoapis.*` domains.

##### Negative

- **NEG-001**: If Zoho introduces a new datacenter domain not in the list, API calls to that datacenter will fail until the binary is updated and redistributed.
- **NEG-002**: Developers cannot test `cliq-cli` against a local mock server or staging environment without modifying the source and rebuilding.
- **NEG-003**: The list is checked at runtime but defined at compile time — there is no mechanism to add a new suffix without a new binary release.

## Alternatives Considered

##### Runtime-Configurable Allowlist

- **ALT-001**: **Description**: Store the allowed suffixes in a config file or environment variable that operators can extend.
- **ALT-002**: **Rejection Reason**: A configurable allowlist can be modified by any process with access to the config (including a compromised AI agent), nullifying the SSRF protection. The security guarantee requires the list to be immutable at runtime.

##### Domain-Based Blocking (Denylist)

- **ALT-003**: **Description**: Block known dangerous ranges (private IP ranges, `localhost`, etc.) rather than allowlisting Zoho domains.
- **ALT-004**: **Rejection Reason**: Denylists are notoriously incomplete — DNS rebinding, IPv6, and exotic loopback addresses can bypass IP-based denylists. An allowlist is more robust for a tool with a known, narrow set of target hosts.

##### No Host Validation

- **ALT-005**: **Description**: Trust that callers only pass valid Zoho API paths and omit host validation entirely.
- **ALT-006**: **Rejection Reason**: The primary caller is an AI agent. A hallucinated or manipulated `--path` value could route a request anywhere. Given that PAT tokens are injected into every request, the impact of a misdirected call includes credential leakage.

##### TLS Certificate Pinning as the Sole Control

- **ALT-007**: **Description**: Rely on TLS certificate validation to prevent requests to untrusted servers.
- **ALT-008**: **Rejection Reason**: Certificate validation prevents MITM but does not prevent the binary from initiating a connection to an attacker-controlled host that presents a valid certificate (e.g. via Let's Encrypt). Host allowlisting is a complementary, earlier-stage control.

## Implementation Notes

- **IMP-001**: `ValidateHost` must be the first operation inside `ApiClient.CallAsync` after URL construction — before token retrieval (so no keychain access leak occurs on a rejected call).
- **IMP-002**: The `AllowedHostSuffixes` constant should be co-located with `DomainGuard` (from ADR-0006) in a `SecurityConstants` or `HostGuard` static class in `CliqCli.Core` for single-source traceability.
- **IMP-003**: Unit tests must cover: (a) each allowed suffix passes, (b) an arbitrary external host fails, (c) a private IP address as hostname fails, (d) a host that is a substring but not a suffix (e.g. `evil-zoho.com`) fails, (e) case-insensitive match passes.
- **IMP-004**: When a new Zoho datacenter is launched, a patch release updating this list must be issued. Document this in `CONTRIBUTING.md` under "Adding a new datacenter".

## References

- **REF-001**: [tech_spec.md — Section 9: API Client — Host Allowlist](../tech_spec.md#9-api-client)
- **REF-002**: [brainstorming.md — API Base URL and Host Allowlist](../brainstorming.md#api-base-url-and-host-allowlist)
- **REF-003**: [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
- **REF-004**: [ADR-0006: Hard-Block ZohoCorp Accounts via Compile-Time Domain Check](adr-0006-zohocorp-account-restriction.md)

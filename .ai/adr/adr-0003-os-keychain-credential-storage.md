---
title: "ADR-0003: OS-Native Keychain with AES-256-GCM Encrypted File Fallback for Credential Storage"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "security", "credentials", "keychain", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0003: OS-Native Keychain with AES-256-GCM Encrypted File Fallback for Credential Storage

## Status

**Proposed**

## Context

`cliq-cli` stores Personal Access Tokens (PATs) on behalf of users across macOS, Windows, and Linux. The storage mechanism must satisfy the following security and usability requirements:

- Tokens must **never** be written to `accounts.json`, stdout, stderr, or any application log in plain text.
- The solution must work without root/administrator privileges.
- The solution must not require the user to install additional software at setup time.
- On headless CI environments, keychain prompts (password dialogs) must not appear; the tool must either use a non-interactive keychain backend or fall back gracefully.
- The `IKeychainProvider` interface must be injectable so implementations can be swapped in tests without hitting real OS APIs.

Each supported platform exposes a different native credential store:

| Platform | Native store | API |
|----------|-------------|-----|
| macOS | Keychain Services | `SecKeychainAddGenericPassword` / `SecKeychainFindGenericPassword` |
| Windows | Credential Manager | `CredWrite` / `CredRead` / `CredDelete` (advapi32.dll) |
| Linux | Secret Service (libsecret) | D-Bus `org.freedesktop.Secret.Service` via libsecret |

On headless servers or environments where the OS keychain is unavailable, a fallback is required.

## Decision

Implement a layered `IKeychainProvider` abstraction with one concrete class per platform, plus a fallback:

- **`MacOsKeychainProvider`** — calls Security.framework via P/Invoke.
- **`WindowsKeychainProvider`** — calls `advapi32.dll` via P/Invoke.
- **`LinuxKeychainProvider`** — calls `libsecret` via P/Invoke.
- **`EncryptedFileKeychainProvider`** — AES-256-GCM encryption, key derived from machine entropy, stored in `<configDir>/keystore/<accountName>.bin`.

Platform selection uses `RuntimeInformation.IsOSPlatform(...)` at startup. The `EncryptedFileKeychainProvider` is activated automatically when the OS keychain is unavailable (e.g., operation fails with a specific error code, or the service is not running).

All implementations are registered in DI via the `IKeychainProvider` interface; no call site knows which concrete provider is in use.

Key naming convention: `cliq-cli:<accountName>:<tokenType>` (e.g., `cliq-cli:work:pat`).

No external keychain NuGet package is used — all interop is via direct P/Invoke to keep the binary self-contained.

## Consequences

### Positive

- **POS-001**: Tokens stored in the OS keychain are protected by the OS's own credential isolation guarantees (macOS ACL, Windows DPAPI/ACL, Linux D-Bus session isolation) — no `cliq-cli`-specific encryption code is on the hot path for the common case.
- **POS-002**: The `IKeychainProvider` abstraction allows unit tests to inject an in-memory fake provider, making all auth flows fully testable without OS keychain access.
- **POS-003**: The `EncryptedFileKeychainProvider` fallback means the tool works in Docker containers and CI runners that have no desktop session, without any configuration change from the user.
- **POS-004**: No third-party keychain NuGet dependency reduces the attack surface and eliminates a supply-chain risk vector at the credential-storage layer.
- **POS-005**: `accounts.json` never contains a token value; even if the config file is accidentally committed or shared, no credential is exposed.

### Negative

- **NEG-001**: Implementing P/Invoke for three OS keychain APIs requires platform-specific knowledge and testing on each OS; bugs in the interop layer may only surface on one platform.
- **NEG-002**: The `EncryptedFileKeychainProvider` fallback key is derived from machine entropy (e.g., machine GUID, hardware identifiers), which means encrypted keystores are **not portable** between machines; migrating accounts requires re-adding them.
- **NEG-003**: On Linux, `libsecret` availability depends on the desktop environment and session; on minimal server distributions, neither `libsecret` nor a D-Bus session may exist, forcing the fallback unconditionally.
- **NEG-004**: macOS Keychain access on code-signed applications may require provisioning an entitlement (`keychain-access-groups`); developer builds and CI builds not carrying a signing identity may hit access prompts unless the keychain item is created with the correct ACL.

## Alternatives Considered

### External NuGet keychain library (e.g., `CliWrap.Keychain`, `SecureStorage`)

- **ALT-001**: **Description**: Several NuGet packages abstract over OS keystores with a unified API, avoiding the need to write P/Invoke for three platforms.
- **ALT-002**: **Rejection Reason**: Introducing a third-party package at the credential-storage layer creates a supply-chain dependency for the most security-sensitive component of the tool. Vetting, updating, and trusting such a package adds ongoing cost with marginal engineering benefit, given that the P/Invoke surface for all three platforms is well-documented and relatively small.

### Environment variable injection (CI-only)

- **ALT-003**: **Description**: Allow the PAT to be supplied via an environment variable (e.g., `CLIQ_TOKEN`) instead of the keychain, bypassing storage entirely for CI use cases.
- **ALT-004**: **Rejection Reason**: Environment variable injection was deferred as an open question in the PRD (not decided at tech-spec time). It does not replace the need for persistent credential storage for interactive developer use cases. It can be added as an opt-in layer on top of the keychain abstraction in a future iteration without changing this decision.

### Plain-text file storage

- **ALT-005**: **Description**: Write tokens to `accounts.json` alongside other account metadata, protected only by `0600` file permissions.
- **ALT-006**: **Rejection Reason**: Rejected outright. File permissions are a weak protection — root processes, backup utilities, and accidentally elevated processes can all read `0600` files. Storing credentials in a plain config file routinely leads to accidental leakage via version control. The OS keychain exists precisely to avoid this pattern.

## Implementation Notes

- **IMP-001**: The keychain provider is selected once at application startup in `Program.cs` and registered as a singleton in DI; all subsequent calls go through the registered `IKeychainProvider` instance.
- **IMP-002**: When the OS keychain provider throws (i.e., the keychain service is unavailable), the DI configuration should fall through to `EncryptedFileKeychainProvider` automatically — this must be tested in CI on a Linux runner with no desktop session.
- **IMP-003**: Success is measured by: on a stock macOS machine, `account add` stores the token in the system Keychain (verifiable via Keychain Access.app); on a headless Linux runner, the same command stores the token in the encrypted file; in neither case does the token appear in `accounts.json`.

## References

- **REF-001**: [ADR-0005: PAT-First Authentication with Pluggable IAuthProvider](adr-0005-pat-first-auth-pluggable-iauthprovider.md)
- **REF-002**: [ADR-0001: .NET 10 / C# 13 as the Runtime and Language](adr-0001-dotnet-10-runtime-and-language.md)
- **REF-003**: macOS Keychain Services — https://developer.apple.com/documentation/security/keychain_services
- **REF-004**: Windows Credential Manager API — https://learn.microsoft.com/windows/win32/api/wincred/
- **REF-005**: libsecret / Secret Service API — https://gnome.pages.gitlab.gnome.org/libsecret/
- **REF-006**: [cliq-cli Technical Specification](../../tasks/cliq-cli/tech_spec.md) §7 Storage, §6 Authentication

---
title: "ADR-0009: Three-Project Solution Architecture (CliqCli / CliqCli.Core / CliqCli.Keychain)"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "project-structure", "separation-of-concerns", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0009: Three-Project Solution Architecture (CliqCli / CliqCli.Core / CliqCli.Keychain)

## Status

**Proposed**

## Context

`cliq-cli` must be:
- **Testable** — domain logic should be unit-testable without spinning up a real CLI process or OS keychain.
- **Cross-platform** — OS-specific keychain implementations (macOS Security.framework, Windows Credential Manager, Linux libsecret) differ significantly and must be isolated so they do not pollute cross-platform domain logic.
- **Extensible** — future auth providers (OAuth2), future command groups (Pex, WebSocket), and future keychain backends (e.g. a managed NuGet keyring) should be addable without touching unrelated code.
- **Self-contained** — the published binary must not require any runtime framework installation.

A single-project layout would make it difficult to unit-test domain logic (commands tightly coupled to Spectre.Console), and it would require platform `#if` guards scattered throughout the codebase for keychain access.

## Decision

The solution is structured as three projects with a strict one-way dependency graph:

```
CliqCli  →  CliqCli.Core
CliqCli  →  CliqCli.Keychain
CliqCli.Core  →  CliqCli.Keychain
```

### Project Responsibilities

#### `CliqCli` (entry point)
- Owns `Program.cs` and all Spectre.Console command wiring.
- Contains `Commands/` — thin command classes that parse flags, call `CliqCli.Core` services via DI, and write to `IOutputWriter`.
- Has **no business logic** — all validation and state mutations live in `CliqCli.Core`.
- References both `CliqCli.Core` and `CliqCli.Keychain` for DI registration only.

#### `CliqCli.Core` (domain logic)
- Owns all business logic: `AccountStore`, `PatAuthProvider`, `ApiClient`, `ApiRegistry`, `TraceWriter`, `TraceExporter`, `TraceSession`, `PexBuffer`.
- Depends on `CliqCli.Keychain` via the `IKeychainProvider` interface — never on a concrete keychain implementation.
- Has **no Spectre.Console dependency** — fully testable against xUnit without instantiating a CLI context.
- Defines all interfaces (`IAuthProvider`, `IAccountStore`, `IKeychainProvider`) consumed across projects.

#### `CliqCli.Keychain` (OS keychain abstraction)
- Owns `IKeychainProvider` and its four concrete implementations:
  - `MacOsKeychainProvider` — Security.framework via P/Invoke
  - `WindowsKeychainProvider` — `CredWrite` / `CredRead` via P/Invoke
  - `LinuxKeychainProvider` — libsecret Secret Service via P/Invoke
  - `EncryptedFileKeychainProvider` — AES-256-GCM encrypted file fallback
- Runtime platform detection via `RuntimeInformation.IsOSPlatform(...)`.
- Has **no dependencies on `CliqCli.Core` or `CliqCli`** — can be tested and evolved independently.

### Dependency Injection Wiring

DI composition occurs entirely in `CliqCli/Program.cs`:

```csharp
services.AddSingleton<IKeychainProvider>(ResolveKeychain());
services.AddSingleton<IAuthProvider, PatAuthProvider>();
services.AddSingleton<IAccountStore, AccountStore>();
services.AddSingleton<ApiClient>();
// ... etc.
```

No service locator or static singletons are used — all dependencies flow through the DI container.

## Consequences

##### Positive

- **POS-001**: `CliqCli.Core` has zero UI/CLI dependencies; all domain logic is unit-testable with plain xUnit and mock implementations of `IKeychainProvider` and `IAccountStore`.
- **POS-002**: Platform-specific P/Invoke code is fully isolated in `CliqCli.Keychain` — adding or changing a keychain backend has no blast radius on domain logic or command parsing.
- **POS-003**: The dependency graph is acyclic and shallow (max depth 2); it is impossible for `CliqCli.Core` to accidentally take a dependency on Spectre.Console or vice versa.
- **POS-004**: `CliqCli.Keychain` can be replaced entirely (e.g. swapped for a managed NuGet keyring) by changing the DI registration in `Program.cs` without touching any other project.

##### Negative

- **NEG-001**: Three projects require three `.csproj` files and a solution file — more scaffolding overhead than a single-project layout.
- **NEG-002**: Interfaces defined in `CliqCli.Core` (e.g. `IKeychainProvider`) create a compile-time contract between projects; adding a method to an interface requires updating all implementations simultaneously.
- **NEG-003**: DI wiring in `Program.cs` must be kept in sync with all service registrations — a forgotten registration causes a runtime DI resolution failure rather than a compile-time error.

## Alternatives Considered

##### Single-Project Layout

- **ALT-001**: **Description**: Place all code in `CliqCli` with internal namespaces for separation.
- **ALT-002**: **Rejection Reason**: Spectre.Console leaks into domain logic tests; platform `#if` guards for keychain spread throughout the codebase; no clean boundary for mock injection in tests.

##### Two-Project Layout (`CliqCli` + `CliqCli.Core`, Keychain Inlined)

- **ALT-003**: **Description**: Integrate keychain implementations directly into `CliqCli.Core` using `#if` guards.
- **ALT-004**: **Rejection Reason**: Platform-specific P/Invoke code is noisy and brittle; keeping it in `CliqCli.Core` reduces testability and makes cross-platform compilation harder to reason about.

##### Four-Project Layout (Separate `CliqCli.Commands`)

- **ALT-005**: **Description**: Extract Spectre.Console command classes into their own `CliqCli.Commands` project, separate from the entry point.
- **ALT-006**: **Rejection Reason**: Over-engineering for the current size of the codebase. Commands are thin wrappers; the added project boundary provides little benefit and increases solution complexity without a clear payoff until the command set grows significantly.

##### Monorepo with Shared Library NuGet

- **ALT-007**: **Description**: Publish `CliqCli.Core` and `CliqCli.Keychain` as NuGet packages for potential reuse by other tools.
- **ALT-008**: **Rejection Reason**: There are no current consumers beyond `CliqCli`. NuGet packaging adds versioning complexity without benefit. Project references are simpler and sufficient within a single repo.

## Implementation Notes

- **IMP-001**: The solution file `cliq-cli.sln` must reference all three projects and set `CliqCli` as the startup project.
- **IMP-002**: `CliqCli.Core.csproj` and `CliqCli.Keychain.csproj` should set `<IsPublishable>false</IsPublishable>` — only `CliqCli.csproj` is published as the final binary.
- **IMP-003**: All interfaces (`IAuthProvider`, `IAccountStore`, `IKeychainProvider`) must be in `CliqCli.Core` (not split across projects) to avoid circular dependency risks.
- **IMP-004**: `CliqCli.Tests` references `CliqCli.Core` and `CliqCli.Keychain` directly; it does NOT reference `CliqCli` (to avoid testing the CLI parsing layer). Command integration tests use Spectre.Console's `CommandAppTester`.

## References

- **REF-001**: [tech_spec.md — Section 3: Project Structure](../tech_spec.md#3-project-structure)
- **REF-002**: [tech_spec.md — Section 8: Storage — AccountStore Contract](../tech_spec.md#8-storage)
- **REF-003**: [ADR-0005: PAT-First Authentication with Pluggable IAuthProvider Interface](adr-0005-pat-first-auth-pluggable-iauthprovider.md)
- **REF-004**: [ADR-0003: OS Keychain Credential Storage](adr-0003-os-keychain-credential-storage.md)

---
title: "ADR-0002: Spectre.Console.Cli as the CLI Framework for cliq-cli"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "cli", "framework", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0002: Spectre.Console.Cli as the CLI Framework for cliq-cli

## Status

**Proposed**

## Context

`cliq-cli` exposes a three-level command hierarchy: a root with global flags, command groups (`account`, `scope`, `api`), and leaf subcommands within each group (e.g., `account add`, `account list`, `api call`). The framework must:

- Parse and validate typed flags and positional arguments for each leaf command independently.
- Support repeatable flags (e.g., `--header key:value` multiple times in one invocation).
- Integrate cleanly with a DI container (`Microsoft.Extensions.DependencyInjection`) so that services (`IAuthProvider`, `ApiClient`, `IAccountStore`) are injected into command classes without manual wiring.
- Produce clear, discoverable `--help` output for every command and subcommand.
- Be compatible with `--no-input` semantics — commands must not interactively prompt once this flag is set.
- Not introduce an interactive TUI or shell mode; the output contract is pure JSON to stdout/stderr.

## Decision

Adopt **Spectre.Console.Cli** (latest stable) as the CLI parsing and command dispatch framework.

Each command group maps to a Spectre `Branch`; each leaf maps to a `Command<TSettings>` class. Global flags (`--account`, `--json`, `--no-input`) are declared in a shared `GlobalSettings` base class and inherited by all command settings classes. The `CommandApp` is configured with the DI registrar pattern provided by Spectre, bridging `Microsoft.Extensions.DependencyInjection`.

## Consequences

### Positive

- **POS-001**: Spectre.Console.Cli's strongly-typed `CommandSettings` classes bind flags directly to C# properties, eliminating manual string parsing and giving compile-time flag correctness.
- **POS-002**: DI registrar integration allows service lifetimes and dependencies to be configured centrally in `Program.cs`, keeping command classes lean and testable in isolation.
- **POS-003**: Repeatable flags are supported natively via `string[]` or `List<string>` property bindings — required for `--header` and `--query` in `api call`.
- **POS-004**: Spectre generates consistent, styled help text for every subcommand automatically; no manual help-string maintenance is needed.
- **POS-005**: `CommandApp.SetExceptionHandler` provides a single interception point to convert unhandled exceptions into the JSON error envelope on stderr, satisfying the output contract.

### Negative

- **NEG-001**: Spectre.Console.Cli's settings validation hook (`Validate()`) runs before `ExecuteAsync()`, but flag mutual-exclusion logic (e.g., `--body` and `--body-file`) must be manually coded in `Validate()` — it is not declarable as metadata.
- **NEG-002**: The framework ties the command tree to a compile-time type graph; dynamically discovering commands at runtime (e.g., for a future plugin system) is not the intended use case.
- **NEG-003**: Spectre's versioning cadence is independent of .NET SDK releases, introducing a point of dependency drift that must be tracked in the project's dependency update policy.

## Alternatives Considered

### System.CommandLine (Microsoft)

- **ALT-001**: **Description**: Microsoft's official `System.CommandLine` library supports nested commands, typed option binding, and DI integration with a similar design philosophy.
- **ALT-002**: **Rejection Reason**: As of the tech spec date, `System.CommandLine` was still in pre-release / continuous preview with several API-breaking changes between minor versions. Spectre.Console.Cli is stable, widely adopted, and has a more ergonomic API for multi-level subcommand trees.

### Cocona

- **ALT-003**: **Description**: Cocona maps C# methods directly to CLI commands using method parameters as flags, reducing boilerplate.
- **ALT-004**: **Rejection Reason**: Cocona's implicit binding makes the command shape harder to discover without reading the source. Its DI integration is less conventional than the explicit `ITypeRegistrar` pattern Spectre exposes, and it has less community momentum for complex subcommand trees.

### Hand-rolled arg parsing

- **ALT-005**: **Description**: Use `args` array parsing directly with no library dependency.
- **ALT-006**: **Rejection Reason**: Writing a correct, ergonomic arg parser that handles repeatable flags, mutual exclusion, short/long aliases, and `--help` generation from scratch is a significant maintenance surface with no product value. Rejected outright.

## Implementation Notes

- **IMP-001**: The `CommandApp` must be constructed with `CommandApp.Create<RootCommand>(registrar)` where `registrar` wraps the `IServiceCollection`; all services registered before `app.Run()` are available to all commands via constructor injection.
- **IMP-002**: All `CommandSettings` classes that may be called without a terminal (CI, `--no-input`) must check `settings.NoInput` early in `ExecuteAsync()` and throw with code `INVALID_ARGS` for any missing required interaction.
- **IMP-003**: Success is measured by: all subcommands in `account`, `scope`, and `api` groups parse their flags correctly in unit tests that instantiate settings directly, without invoking a real shell or process.

## References

- **REF-001**: [ADR-0001: .NET 10 / C# 13 as the Runtime and Language](adr-0001-dotnet-10-runtime-and-language.md)
- **REF-002**: [ADR-0004: JSON-Only Output Contract](adr-0004-json-only-output-contract.md)
- **REF-003**: Spectre.Console.Cli documentation — https://spectreconsole.net/cli/getting-started
- **REF-004**: [cliq-cli Technical Specification](../../tasks/cliq-cli/tech_spec.md) §4 CLI Structure & Commands, §11 Dependencies

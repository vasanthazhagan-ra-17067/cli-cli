---
title: "ADR-0001: .NET 10 / C# 13 as the Runtime and Language for cliq-cli"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "runtime", "language", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0001: .NET 10 / C# 13 as the Runtime and Language for cliq-cli

## Status

**Proposed**

## Context

`cliq-cli` must ship as a single self-contained binary that runs on macOS (Intel + Apple Silicon), Windows (x64), and Linux (x64) without requiring any runtime pre-installed on the target machine. The tool must:

- Interoperate with OS-native APIs on three platforms (macOS Security.framework, Windows Credential Manager, Linux Secret Service) via P/Invoke.
- Serialize and deserialize structured JSON deterministically.
- Support async I/O throughout, given that every auth and HTTP operation is inherently asynchronous.
- Be maintainable by a team already familiar with the broader Zoho engineering stack.

The chosen runtime and language must support `PublishSingleFile` (or equivalent) without requiring a separate installer, and must have a mature ecosystem of CLI, HTTP, and JSON libraries available as first-party or well-supported NuGet packages.

## Decision

Adopt **.NET 10** as the runtime and **C# 13+** as the implementation language.

The binary will be published using:

```
dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true
```

Supported Runtime Identifiers (RIDs): `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`.

Nullable reference types and implicit usings are both enabled globally in all projects.

## Consequences

### Positive

- **POS-001**: `PublishSingleFile` + `--self-contained` produces a single executable that embeds the runtime, eliminating any "install .NET first" friction for end users and CI runners.
- **POS-002**: P/Invoke support is first-class in .NET; all three target OS keychain APIs (Security.framework, CredWrite/CredRead, libsecret) can be called directly without a third-party interop library, keeping the binary entirely dependency-free at the keychain layer.
- **POS-003**: `System.Text.Json` (stdlib in .NET 10) provides high-performance, allocation-efficient JSON serialization with native `snake_case` naming policy support — no external JSON library needed.
- **POS-004**: C# 13 records with `required` properties and `init`-only setters make the immutable data model (`AccountEntry`, `ApiRequest`, `ApiResponse`) safe and concise.
- **POS-005**: `CancellationToken` propagation is idiomatic in C#; `Ctrl+C` cancellation and graceful shutdown are supported natively via `CancellationTokenSource`.

### Negative

- **NEG-001**: Self-contained binaries are significantly larger than framework-dependent binaries (typically 50–80 MB per RID); this is a build artifact storage and download-size cost.
- **NEG-002**: Native AOT is explicitly out of scope for v1 (see Non-Goals); `PublishSingleFile` still JIT-compiles on first run, which adds a marginal cold-start cost.
- **NEG-003**: The C#/.NET skill set requirement narrows the contributor pool in teams primarily using Swift, Go, or Python.
- **NEG-004**: Cross-compiling self-contained binaries for all four RIDs requires CI agents with access to the .NET 10 SDK on each target OS, or use of Docker-based cross-compilation.

## Alternatives Considered

### Go

- **ALT-001**: **Description**: Go produces small, statically linked binaries natively; cross-compilation is famously simple (`GOOS`/`GOARCH`). Keychain access is available via `go-keyring` or custom `cgo` wrappers.
- **ALT-002**: **Rejection Reason**: The team has no production Go codebase; adopting Go would introduce a new language ecosystem solely for this tool. P/Invoke equivalents in Go (`cgo`) add compilation complexity and can increase binary size. The `cobra` CLI library is capable but less ergonomic for nested subcommand/flag trees than Spectre.Console.Cli.

### Python (PyInstaller / Nuitka)

- **ALT-003**: **Description**: Python is highly productive for scripting; PyInstaller can bundle a self-contained binary. Libraries such as `keyring` and `click`/`typer` are mature.
- **ALT-004**: **Rejection Reason**: PyInstaller bundles are fragile on newer macOS (notarization, Gatekeeper), and startup latency for bundled Python is notably high in CLI contexts. `keyring` abstracts over many backends but adds an external dependency chain that complicates security auditing. Python's dynamic typing reduces confidence in the correctness of the data model layer.

### Rust

- **ALT-005**: **Description**: Rust produces small, fast, safe binaries with no runtime. Crates such as `clap` and `keyring` are mature.
- **ALT-006**: **Rejection Reason**: Rust's learning curve and ownership model would slow initial development significantly. No existing Rust expertise on the team. Build times for incremental development are longer than .NET hot-reload workflows.

## Implementation Notes

- **IMP-001**: All four RID binaries should be built in CI on each push to `main`; artifacts are uploaded to the release as `cliq-cli-<version>-<rid>` (no `.exe` extension; Windows binary carries it implicitly from the RID).
- **IMP-002**: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<Nullable>enable</Nullable>` must be set in all `.csproj` files to enforce correctness at compile time.
- **IMP-003**: Success is measured by: binary runs on a clean macOS/Windows/Linux machine with no .NET SDK installed and produces valid JSON on the first invocation.

## References

- **REF-001**: [ADR-0003: OS Keychain with Encrypted File Fallback](adr-0003-os-keychain-credential-storage.md)
- **REF-002**: [ADR-0002: Spectre.Console.Cli as CLI Framework](adr-0002-spectre-console-cli-framework.md)
- **REF-003**: .NET 10 PublishSingleFile documentation — https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
- **REF-004**: .NET Runtime Identifier catalog — https://learn.microsoft.com/dotnet/core/rid-catalog
- **REF-005**: [cliq-cli Technical Specification](../../tasks/cliq-cli/tech_spec.md) §2 Runtime & Language

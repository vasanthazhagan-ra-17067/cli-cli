---
title: "ADR-0004: JSON-Only Output Contract for cliq-cli"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "output", "contract", "json", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0004: JSON-Only Output Contract for cliq-cli

## Status

**Proposed**

## Context

`cliq-cli` has two primary consumers: AI agents (via GitHub Copilot CLI Skills) and human developers / CI scripts. These consumers have fundamentally different output needs:

- **AI agents** need machine-parseable, structured output every time — they cannot handle free-form text or styled terminal output reliably.
- **CI scripts** pipe the CLI output into tools like `jq`, shell conditionals, and log aggregators; they rely on both output format stability and exit-code semantics.
- **Developers** benefit from structured output too, since it allows them to compose `cliq-cli` with other Unix tools even when working interactively.

The output contract must also handle the security constraint that tokens must never appear in any output under any circumstances. It must distinguish between success output (stdout) and error output (stderr) so that callers can branch independently on the two streams.

Additionally, the tool needs a globally consistent error shape so that AI agents can generically handle failures regardless of which command produced them, without per-command error parsing logic.

## Decision

Adopt a **JSON-only output contract** for all `cliq-cli` output:

**Success (stdout)**:
```json
{ "status": "ok", "data": <command result or raw API response> }
```

**Error (stderr)**:
```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <1|2> }
```

Rules:
- `--json` defaults to `true`; plain-text output is not supported in v1.
- Stdout and stderr are always valid JSON documents, never partial or interleaved.
- All output is routed through a central `IOutputWriter` interface; commands never write directly to `Console`.
- The `code` field in error envelopes uses a fixed vocabulary of symbolic error codes (e.g., `ACCOUNT_NOT_FOUND`, `AUTH_FAILURE`) so callers can branch on codes without parsing the human message.
- Exit codes are limited to three values: `0` (success), `1` (general/recoverable error), `2` (auth failure / needs-reauth).
- Token values are **unconditionally** excluded from all output; `account show` renders the token field as `"***"`.

## Consequences

### Positive

- **POS-001**: AI agents can parse any `cliq-cli` output with a single JSON-parsing step and branch on `status`/`code` without per-command handling logic, making the skill implementation simple and robust.
- **POS-002**: The `IOutputWriter` interface decouples all commands from `Console`; unit tests inject an in-memory implementation and assert on the JSON written, without spawning a real process.
- **POS-003**: The fixed error code vocabulary (`ACCOUNT_NOT_FOUND`, `NEEDS_REAUTH`, etc.) gives callers a stable, versioned contract for error handling; the human-readable `error` message can evolve independently.
- **POS-004**: Unconditional JSON removes any conditional output-format logic from command implementations, reducing cyclomatic complexity and the risk of accidentally emitting plain text in edge cases.
- **POS-005**: stdout/stderr separation means callers can capture success output via `$(cliq-cli ...)` while letting error output flow to the terminal naturally, following Unix conventions.

### Negative

- **NEG-001**: Interactive developer use is less ergonomic — `account list` produces JSON rather than a formatted table, requiring the developer to pipe through `jq` for readable output.
- **NEG-002**: Long API responses are emitted as a single-line JSON blob; large payloads are hard to read in a terminal without post-processing.
- **NEG-003**: No `--pretty` / `--format` flag in v1 means there is no official way to get human-friendly output without a third-party tool; this is a friction point during debugging sessions.

## Alternatives Considered

### Human-readable text by default with `--json` flag

- **ALT-001**: **Description**: Default to styled terminal output (tables, colors) for interactive use, and offer `--json` as an opt-in flag for machine consumers.
- **ALT-002**: **Rejection Reason**: The primary consumer (AI agents) cannot negotiate output format; they will always call the binary with a fixed set of flags. Defaulting to human-readable output and requiring `--json` everywhere would mean the agent skill must always include `--json` and any omission causes a parse failure. JSON-first eliminates this class of error entirely.

### JSON Lines (JSONL / NDJSON) for streaming

- **ALT-003**: **Description**: Each logical output item is one JSON line; streaming responses (e.g., future WebSocket or Pex listening) emit one JSON object per line.
- **ALT-004**: **Rejection Reason**: The v1 command set has no streaming use cases; all responses are single documents. JSONL would complicate the success envelope parsing for non-streaming callers. The design can migrate to JSONL for streaming commands in the `ws` and `pex` groups in a future version without changing v1 behavior.

### Spectre.Console rich terminal output

- **ALT-005**: **Description**: Use Spectre.Console's table, tree, and markup rendering for human-friendly output.
- **ALT-006**: **Rejection Reason**: Rich terminal output contains ANSI escape sequences that break machine parsing. While Spectre can auto-detect when output is redirected and strip escape codes, this adds non-determinism. JSON-only output is fully predictable regardless of terminal state.

## Implementation Notes

- **IMP-001**: `IOutputWriter` must be registered as a singleton in DI with two concrete implementations: `ConsoleOutputWriter` (for production) and `CapturingOutputWriter` (for unit tests). Commands receive it via constructor injection.
- **IMP-002**: The `CommandApp.SetExceptionHandler` hook must serialize any unhandled exception to the JSON error envelope on stderr and exit with code `1` before the process terminates.
- **IMP-003**: Success is measured by: a shell script using `jq '.status'` on every `cliq-cli` command (including error cases captured from stderr) always returns a parseable string; no invocation ever produces non-JSON output on either stream.

## References

- **REF-001**: [ADR-0002: Spectre.Console.Cli as the CLI Framework](adr-0002-spectre-console-cli-framework.md)
- **REF-002**: [ADR-0005: PAT-First Authentication with Pluggable IAuthProvider](adr-0005-pat-first-auth-pluggable-iauthprovider.md)
- **REF-003**: [cliq-cli Technical Specification](../../tasks/cliq-cli/tech_spec.md) §9 Output Contract, §10 Error Handling

---
title: "ADR-0008: Named Session Trace with Append-Only JSONL Storage"
status: "Proposed"
date: "2026-03-16"
authors: "cliq-cli contributors"
tags: ["architecture", "decision", "observability", "tracing", "ai-agents", "cliq-cli"]
supersedes: ""
superseded_by: ""
---

# ADR-0008: Named Session Trace with Append-Only JSONL Storage

## Status

**Proposed**

## Context

`cliq-cli` is the CLI replacement for `CliqApiMcp`, a macOS application that captured every API request and response in an in-memory `APILogEntry` / `APILogEvent` log, displayed in a native macOS window UI. That log was ephemeral — it disappeared when the app was closed.

The primary consumer of `cliq-cli` is an AI agent running an **API analysis session**: the agent fires dozens of API calls against Cliq endpoints, drains Pex real-time events, and at the end of the session exports the complete call record for developer reference and post-mortem debugging.

Key requirements for the trace system:
1. **Passive** — trace recording must be automatic when a session is active; agents cannot be expected to pass a `--trace` flag on every command.
2. **Persistent** — the trace must survive process exits and agent crashes; the agent must be able to export it after the fact.
3. **Named and scoped** — multiple concurrent analysis runs must not pollute each other.
4. **Auditable** — the trace must capture enough information (request, response, timing, account) for a developer to reconstruct exactly what the agent did.
5. **Token-safe** — the `Authorization` header must never appear in the trace file.
6. **Correlated** — Pex/WMS events must be linkable back to the API call that caused them.

The `CliqApiMcp` approach (in-memory, UI-only) fails requirements 2, 3, and 4. A naive approach of writing one log file per process invocation fails requirement 3 (no session grouping) and makes correlation across multiple `api call` invocations impossible.

## Decision

Implement a **named session trace** system backed by append-only JSONL files, with a JSON index file tracking session state.

### Storage Layout

```
<configDir>/cliq-cli/traces/
  sessions.json                 ← index: name, startTime, entryCount, status (active | closed)
  <session-name>/
    trace.jsonl                 ← one JSON object per line (append-only during session)
```

### Session Lifecycle

```
cliq-cli trace session start  --name "<name>"   → creates entry in sessions.json (status: active)
[... api calls and pex drains auto-append entries to trace.jsonl ...]
cliq-cli trace session export --name "<name>"   → reads trace.jsonl, emits JSON array to stdout (non-destructive)
cliq-cli trace session close  --name "<name>"   → updates sessions.json status to "closed"; file preserved
cliq-cli trace session remove --name "<name>"   → deletes sessions.json entry + trace.jsonl
```

### Entry Types

**`"api"` entry** — appended by every `api call` invocation when a session is active:
- `seq`, `type`, `session`, `timestamp`, `durationMs`, `account`, `method`, `url`, `requestHeaders` (Authorization excluded), `requestBody`, `responseStatus`, `responseHeaders`, `responseBody`, `error`

**`"pex"` entry** — appended per event when `pex drain` is called with a session active:
- `seq`, `type`, `session`, `timestamp`, `account`, `relatedApiSeq`, `handlerClass`, `callbackMethod`, `payload`
- `relatedApiSeq` = the `seq` of the most recent `"api"` entry before this drain; `null` if no API call preceded it in this session.

### Auto-Append Behaviour

- `api call` and `pex drain` query `sessions.json` for the single active session and append a trace entry atomically before returning their normal stdout output.
- If no session is active, trace entries are silently dropped — no implicit session is created and no error is emitted.
- The `seq` counter is derived from `entryCount` in `sessions.json`, incremented atomically with each append.

### Export

- `trace session export` reads `trace.jsonl` sequentially and emits a JSON array to stdout.
- Optional `--truncate-body <bytes>` truncates `requestBody` and `responseBody` per entry at export time only — full fidelity is preserved on disk.
- Optional `--type api|pex` filters the output to entries of one type.
- Export is **non-destructive** — the `.jsonl` file is not modified.

### Token Safety

`ApiClient` explicitly excludes `Authorization` from `requestHeaders` before constructing the trace entry. This is enforced in `TraceWriter`, not at the command layer, so it cannot be accidentally omitted by future command implementations.

## Consequences

##### Positive

- **POS-001**: Passive recording eliminates the risk of an agent "forgetting" to pass a trace flag — all calls in an active session are captured automatically.
- **POS-002**: Append-only JSONL survives process crashes mid-session; partial traces are always recoverable.
- **POS-003**: `relatedApiSeq` correlation enables developers to reconstruct the causal link between an API call and subsequent Pex events without manual timestamp matching.
- **POS-004**: Non-destructive export means the agent can export the trace multiple times (e.g. for different consumers) without re-running the session.
- **POS-005**: Body truncation at export time preserves full fidelity on disk while keeping exported files manageable for large API responses.

##### Negative

- **NEG-001**: Sessions must be explicitly started and closed by the agent — there is no automatic session-per-invocation mode, which means a session that is never started produces no trace.
- **NEG-002**: The append-only design means `trace.jsonl` grows unboundedly within a session; very long sessions (hundreds of calls with large response bodies) can produce large files.
- **NEG-003**: The `seq` counter in `sessions.json` is updated on each append; concurrent invocations of `api call` (e.g. from a parallel agent) could race on the counter — mitigated by file locking but adds complexity.
- **NEG-004**: The `Authorization` exclusion is enforced in `TraceWriter` — a future refactor that bypasses `TraceWriter` and writes entries directly could inadvertently include tokens.

## Alternatives Considered

##### In-Memory Log (CliqApiMcp Approach)

- **ALT-001**: **Description**: Keep an in-process in-memory list of trace entries; write to file only on `trace export`.
- **ALT-002**: **Rejection Reason**: Does not survive process exits between individual `cliq-cli` invocations — each CLI invocation is a separate process. The agent runs `api call` in a subprocess per call; there is no shared long-lived process to hold state.

##### Single Rotating Log File (No Named Sessions)

- **ALT-003**: **Description**: Write all trace entries to a single `trace.jsonl` and let the agent filter by timestamp.
- **ALT-004**: **Rejection Reason**: Pollutes entries across unrelated sessions; makes it impossible to export "just the Messages session" without complex timestamp filtering. Named sessions provide clean isolation at zero extra cost.

##### SQLite Database

- **ALT-005**: **Description**: Store trace entries in a local SQLite database for rich querying.
- **ALT-006**: **Rejection Reason**: Adds a significant dependency and complexity. JSONL is sufficient for the agent's read pattern (sequential export), simpler to implement, and human-readable without tooling.

##### Structured Log via `Microsoft.Extensions.Logging`

- **ALT-007**: **Description**: Reuse the existing logging infrastructure to write trace entries as structured log events.
- **ALT-008**: **Rejection Reason**: The logging pipeline is intended for diagnostic output, not data export. The trace is a first-class data product (exported as JSON to the agent); conflating it with diagnostic logs would make filtering and export significantly harder.

## Implementation Notes

- **IMP-001**: `TraceWriter.AppendAsync` must use file locking (`FileShare.None` or advisory lock) to handle the unlikely but possible case of concurrent CLI invocations in the same session.
- **IMP-002**: `sessions.json` entry `entryCount` is the authoritative source for the next `seq` value — `TraceWriter` must increment it atomically (read-modify-write with the file lock held).
- **IMP-003**: `ApiClient.CallAsync` delegates entry construction to a `TraceEntryFactory` that explicitly builds `requestHeaders` without the `Authorization` key — unit tests must assert `Authorization` is absent from the constructed entry.
- **IMP-004**: The agent workflow documentation (and `HttpApiAnalysisAgent` instructions) must include the three lifecycle steps: `trace session start` at Phase 2 entry, `trace session export` + `trace session close` at Phase 2 exit.
- **IMP-005**: Default body truncation behaviour: write full body to `.jsonl` (no truncation on write); apply truncation only at export time via `--truncate-body`. This preserves full fidelity in storage and gives the agent control at export.

## References

- **REF-001**: [tech_spec.md — Section 10: Trace Sessions](../tech_spec.md#10-trace-sessions)
- **REF-002**: [brainstorming.md — Epic 9: Trace Sessions](../brainstorming.md#epic-9--trace-sessions)
- **REF-003**: [ADR-0004: JSON-Only Output Contract](adr-0004-json-only-output-contract.md)
- **REF-004**: CliqApiMcp — `APILogEntry.swift`, `APILogEvent.swift` (origin of the trace concept)

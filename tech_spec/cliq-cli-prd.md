# PRD: cliq-cli — Cross-Platform Zoho Cliq API CLI

## 1. Product overview

### 1.1 Document title and version

- PRD: cliq-cli — Cross-Platform Zoho Cliq API CLI
- Version: 1.0
- Date: 2026-03-16
- Authors: cliq-cli contributors
- Status: Draft

### 1.2 Product summary

`cliq-cli` is a standalone, self-contained command-line binary that provides a deterministic, scriptable interface to Zoho Cliq REST APIs. It manages multiple Zoho accounts, handles authentication via a pluggable interface (PAT in v1, OAuth2 in v2), and exposes a general-purpose HTTP API invoker. The binary runs natively on macOS (Intel + Apple Silicon), Windows (x64), and Linux (x64) without requiring any runtime pre-installed on the target machine.

The primary consumer is AI agents — specifically GitHub Copilot CLI Skills and Claude Agent Skills — that need to fire structured API calls against Cliq endpoints and consume the results programmatically. All output is valid JSON on stdout (success) and stderr (error), making the tool composable with `jq`, shell pipelines, and agent toolchains without additional parsing logic.

`cliq-cli` is the direct replacement for `CliqApiMcp`, a macOS-only application that exposed Cliq API access as an MCP server over stdio. `CliqApiMcp` could not run headlessly, required a running UI session for authentication, and was tied to internal Zoho CocoaPod dependencies (`ZMacAuth`, `PEXLibrary`). `cliq-cli` eliminates all of these constraints by shipping as a cross-platform .NET 10 / C# 13 binary with no external runtime dependencies.

## 2. Goals

### 2.1 Business goals

- Replace `CliqApiMcp` with a cross-platform, headless-capable alternative that can be distributed via GitHub Releases and standard package managers.
- Enable AI agent workflows (Copilot, Claude) to drive Zoho Cliq API operations without human interaction, expanding the automation surface for internal Zoho engineering teams.
- Establish a versioned, stable output contract (JSON envelopes + symbolic error codes) that agent skill manifests can depend on across releases.
- Provide a single tool that consolidates Cliq account management, API invocation, real-time event buffering, and session tracing — reducing the number of bespoke scripts each team maintains.

### 2.2 User goals

- **Developers:** Add, manage, and switch between multiple Zoho accounts from the terminal with a single command; invoke any Cliq REST endpoint without writing boilerplate HTTP code.
- **AI agents:** Receive machine-parseable JSON responses from every command invocation; handle errors generically using symbolic error codes without per-command parsing logic.
- **Zoho engineering teams:** Record, export, and post-mortem complete API call traces across multi-step analysis sessions — replacing the ephemeral in-memory log of `CliqApiMcp`.

### 2.3 Non-goals

- Native AOT compilation (deferred to post-v1; `PublishSingleFile` with JIT is sufficient).
- Interactive TUI / shell mode — the output contract is pure JSON; no styled terminal output.
- Support for ZohoCorp employee accounts (`*@zohocorp.com`) — hard-blocked unconditionally for security reasons (see ADR-0006).
- OAuth2 authorization code (browser-redirect) flow — v2 OAuth will use an API-based terminal flow (client credentials or device-authorization grant) calling the Zoho OAuth2 endpoints directly; no browser window, redirect URL, or local HTTP listener is required at any point.
- A plugin or extensibility system for third-party command groups — the command tree is fixed at compile time in v1.
- Automatic token rotation or expiry handling in v1 (PATs are long-lived; no refresh logic until OAuth2).
- CI/CD pipeline automation — automated build pipelines, GitHub Actions release workflows, multi-RID artifact publishing, code-signing automation, and Homebrew tap publishing are deferred to a dedicated DevOps phase after v1 GA.

## 3. User personas

### 3.1 Key user types

- **Developer / power user** — configures accounts, invokes the CLI interactively, pipes output through `jq`.
- **AI agent** — calls `cliq-cli` as a subprocess within a Copilot CLI Skill or Claude Agent Skill; reads stdout JSON and branches on `status`/`code`.

### 3.2 Basic persona details

- **Vasanth / Internal Zoho Developer**: A senior Zoho engineer exploring Cliq APIs for a new integration feature. Uses `cliq-cli` interactively to prototype API calls, register useful endpoints in the registry, and later package those calls into an agent skill. Prefers terse CLI commands and expects JSON output that pipes cleanly into shell tools.
- **Copilot Agent (AI)**: An instance of GitHub Copilot Agent running a CLI Skill that calls `cliq-cli` as a subprocess for each API interaction. Issues commands like `cliq-cli api call --method GET --path "/api/v2/channels"`, parses `{"status":"ok","data":{...}}` from stdout, and branches on the `code` field in stderr on failure.

### 3.3 Role-based access

- **Account owner (human)**: Can add, remove, and manage all configured accounts on the local machine. No server-side RBAC — access is governed by the PAT/OAuth token's own permissions against the Zoho Cliq API.
- **Agent (automated)**: Inherits account permissions at the token level. Cannot manage accounts directly — account configuration must be completed by a human before the agent skill is invoked.
- **ZohoCorp domain accounts**: Hard-blocked at both `account add` and `api call` regardless of who or what invokes the command. Error code: `ACCOUNT_DOMAIN_BLOCKED`.

## 4. Functional requirements

- **Account management** (Priority: High — v1)

  - Add a named account with a PAT and an optional datacenter domain (`zoho.com`, `zoho.eu`, `zoho.in`, `zoho.com.au`). Email is fetched from the Zoho user-info API at `account add` to validate the PAT and perform the ZohoCorp domain check before any account is persisted.
  - List all configured accounts with token values masked.
  - Remove an account, clearing both the metadata in `accounts.json` and the keychain secret.
  - Show a single account with the token rendered as `"***"`.
  - Set a default/active account that all subsequent commands use when `--account` is not specified.
  - `re-auth` stub present in v1 (returns a clear "not yet supported" error); fully implemented with OAuth2 in v2.

- **PAT authentication** (Priority: High — v1)

  - Store PAT secrets in the OS-native keychain (macOS Keychain Services, Windows Credential Manager, Linux Secret Service) using direct P/Invoke — no third-party keychain NuGet dependency.
  - Fall back to an AES-256-GCM encrypted file store (`<configDir>/cliq-cli/keystore/<accountName>.bin`) on headless or CI environments where the OS keychain is unavailable.
  - Inject `Authorization: Zoho-oauthtoken <token>` into every outgoing HTTP request transparently via `PatAuthProvider`.
  - Token values must never appear in any output, log, or file in plain text.

- **API invocation** (Priority: High — v1)

  - Call any Cliq REST endpoint with configurable method, path, headers, query parameters, and body (inline string or file).
  - Automatically prepend `/api/v2` if the `--path` argument does not begin with `/api/`.
  - Validate the fully-resolved request URL host against a compile-time allowlist of Zoho datacenter suffixes (`.zoho.com`, `.zoho.eu`, `.zoho.in`, `.zoho.com.au`, `.zohoapis.com`, `.zohoapis.in`) before dispatching any network I/O.
  - Return the raw API response as `{"status":"ok","data":<response>}` on stdout.

- **Scope management** (Priority: Medium — v1, scaffolded for v2)

  - Add, remove, and list OAuth scope strings per account.
  - Setting `needs_reauth = true` on the account when scopes change, so subsequent `api call` commands surface a `NEEDS_REAUTH` error and guide the user to run `account re-auth`.
  - In v1, scope metadata is recorded but re-auth is a no-op with PATs.

- **Trace sessions** (Priority: High — v1)

  - Start a named trace session that passively captures all subsequent `api call` (and future `pex drain`) invocations as append-only JSONL entries.
  - Export the full session trace as a JSON array to stdout (non-destructive); support optional body truncation and type filtering (`--type api|pex`).
  - Close a session (marks it inactive, file preserved) or remove it (deletes all trace files).
  - `Authorization` headers are unconditionally excluded from trace entries by `TraceWriter`.

- **ZohoCorp account block** (Priority: High — v1, security)

  - Hard-block any account whose email domain's first DNS label equals `zohocorp` (case-insensitive). Covers all current and future datacenter TLDs automatically.
  - Block applied at both `PatAuthProvider.StoreTokenAsync` and `ApiClient.CallAsync` — cannot be bypassed at the command layer.
  - Error code: `ACCOUNT_DOMAIN_BLOCKED`, exit 1.

- **JSON output contract** (Priority: High — v1)

  - All commands write only valid JSON to stdout (success envelope) and stderr (error envelope). No plain text or styled terminal output in any code path.
  - Fixed symbolic error code vocabulary (`ACCOUNT_NOT_FOUND`, `AUTH_FAILURE`, `NEEDS_REAUTH`, `HOST_NOT_ALLOWED`, `ACCOUNT_DOMAIN_BLOCKED`, `INVALID_ARGS`, etc.).
  - Three exit codes: `0` success, `1` general error, `2` auth failure / needs-reauth.

- **Pex / WMS real-time events** (Priority: High — v2)

  - Open a named Pex/WMS WebSocket connection per account and buffer incoming callback events to a per-account JSONL file.
  - `pex drain` atomically reads and truncates the buffer, returning events as a JSON array and appending Pex trace entries to any active trace session with `relatedApiSeq` correlation.
  - `pex listen` streams events to stdout as newline-delimited JSON (blocking).

- **API registry** (Priority: Medium — v2)

  - Persist a local `registry.json` catalogue of named Cliq API endpoints with method, URL template, and purpose.
  - Supports `add`, `list`, `show`, and `remove` operations; compatible with the existing `CliqApiMcp` registry format to ease migration.

- **OAuth2 authentication** (Priority: Medium — v2)

  - Implement `OAuthProvider` as a drop-in `IAuthProvider` replacement; no changes required in `ApiClient` or any command class.
  - API-based terminal flow only — `account add --client-id <id> --client-secret <secret>` calls the Zoho OAuth2 token endpoint directly from the CLI process. No browser, no redirect listener, no callback URL.
  - `account re-auth` fully activates to exchange a new token via the Zoho OAuth API and clear `needs_reauth`.

- **WebSocket support** (Priority: Low — v2/v3)

  - Named WebSocket connections with `connect`, `send`, `listen`, and `close` subcommands.
  - Streaming JSON output (JSONL) to stdout for `listen`.

- **Utility commands** (Priority: Low — v1)

  - `util time-ms` — current UTC time as Unix millisecond timestamp.
  - `util uuid` — generate a random UUID v4.

## 5. User experience

### 5.1 Entry points & first-time user flow

- User downloads the platform binary from GitHub Releases (e.g. `cliq-cli-1.0.0-osx-arm64`) and places it on their `$PATH`.
- First invocation: `cliq-cli --help` shows the command tree.
- Account setup: `cliq-cli account add --name "work" --token "<PAT>"` — the CLI contacts the Zoho user-info API, validates the PAT, performs the ZohoCorp check, and stores the token in the OS keychain. Success output: `{"status":"ok","data":{"name":"work","domain":"zoho.com",...}}`.
- Immediate API call: `cliq-cli api call --method GET --path "/api/v2/channels"` — no further setup required.

### 5.2 Core experience

- **Account switching**: `--account <name>` on any command overrides the active account for that invocation only; no global state is mutated.

  - Developers working across multiple Zoho datacenters (EU, US, IN) set a default per-session or override per-call without re-configuring.

- **AI agent invocation**: The agent calls `cliq-cli api call --method POST --path "/api/v2/channels/{id}/messages" --body '{"text":"hello"}'` and receives `{"status":"ok","data":{...}}`. On failure it reads stderr for the `code` field and retries or escalates.

  - The fixed error code vocabulary means the agent's error-handling logic is written once and works for all commands.

- **Trace sessions for analysis runs**: The developer or agent starts `cliq-cli trace session start --name "channel-audit"`, fires a series of `api call` commands, then exports: `cliq-cli trace session export --name "channel-audit"`. The full call log — request, response, timing, account — is available as a JSON array for review or archival.

  - No `--trace` flag is needed on each call; passive recording eliminates omission risk.

### 5.3 Advanced features & edge cases

- `--body` and `--body-file` are mutually exclusive; violation produces `INVALID_ARGS` on stderr before any network I/O.
- Paths without the `/api/v2` prefix are silently corrected; callers passing absolute paths bypass this normalization.
- Body truncation at export time (`--truncate-body <bytes>`) keeps full fidelity in the `.jsonl` file while producing concise exported output for large responses.
- Concurrent `api call` invocations with the same active trace session use file locking to serialize seq counter increments.
- If `accounts.json` contains a ZohoCorp account from a pre-check version, a startup warning is emitted on stderr; the account is rejected on first use.

### 5.4 UI/UX highlights

- Every command's `--help` is auto-generated by Spectre.Console.Cli — no manual help-string maintenance.
- Token masking is enforced unconditionally: `account show` renders `"token": "***"` regardless of flags.
- Error messages are developer-friendly English strings; the `code` field provides the machine-parseable signal for automated callers.
- `--no-input` mode converts any missing required interaction (e.g., a prompt that would normally appear) into an immediate `INVALID_ARGS` error — safe for headless and non-interactive contexts.

## 6. Narrative

A Zoho engineer needs to build an automated integration that monitors Cliq channels and posts digest messages to a team channel every morning. Instead of embedding raw HTTP calls, OAuth token management, and error-handling boilerplate into a Python script, they install `cliq-cli`, register their PAT in 30 seconds, and compose the entire workflow using shell commands and `jq`. When they want to hand this off to the team's GitHub Copilot CI agent, they package the same `cliq-cli` commands into a Copilot CLI Skill — the agent fires the exact same commands, reads the exact same JSON, and handles errors using the same symbolic codes. The trace session feature lets them audit exactly what the agent did during a debugging session, exporting the full API call log with request/response bodies for post-mortem review — something `CliqApiMcp`'s ephemeral in-memory log could never provide.

## 7. Success metrics

### 7.1 User-centric metrics

- Time-to-first-API-call from binary download ≤ 2 minutes (account add + api call).
- Zero "install .NET first" support tickets — self-contained binary runs on a clean OS with no SDK present.
- AI agent error rate for JSON parse failures on stdout/stderr = 0% — every output path emits valid JSON.
- Trace export completeness: 100% of `api call` invocations within an active session appear in the exported JSONL.

### 7.2 Business metrics

- Full feature parity with `CliqApiMcp` MCP tool surface (11 tools mapped to CLI equivalents) achieved in v1 + v2.
- `CliqApiMcp` deprecated and removed from active use within 1 release cycle of `cliq-cli` v2 GA.
- Adoption: `cliq-cli` used as the primary Cliq API interface in ≥ 3 internal Zoho agent skill packages within 6 months of v1 GA.

### 7.3 Technical metrics

- Binary size ≤ 80 MB per RID (self-contained .NET 10 baseline).
- Unit test coverage ≥ 80% on `CliqCli.Core` (domain logic, no OS keychain or real HTTP required).
- Cold-start latency ≤ 500 ms for `cliq-cli account list` on a clean developer machine.
- Zero compiler warnings (`TreatWarningsAsErrors=true`) and zero nullable reference violations at build time.

## 8. Technical considerations

### 8.1 Integration points

- **Zoho Cliq REST API** — base URL derived from account `domain` field; all calls subject to compile-time host allowlist (ADR-0007).
- **Zoho user-info API** — called at `account add` to validate the PAT and retrieve the email for the ZohoCorp domain check (ADR-0006).
- **OS keychain APIs** — macOS Security.framework, Windows Credential Manager (`advapi32.dll`), Linux Secret Service (`libsecret`) via P/Invoke without third-party NuGet packages (ADR-0003).
- **GitHub Copilot CLI Skills** — `SKILL.md` in `.github/skills/cliq-cli/` describes available commands; Copilot invokes `cliq-cli` subprocesses and reads stdout JSON.
- **Claude Agent Skills** — analogous skill manifest; same JSON output contract consumed.

### 8.2 Data storage & privacy

- `accounts.json` stored at the platform config directory (`~/Library/Application Support/cliq-cli/` on macOS, `%LOCALAPPDATA%\cliq-cli\` on Windows, `~/.config/cliq-cli/` on Linux) with `0600` permissions on Unix.
- PAT and OAuth tokens stored exclusively in the OS keychain or the AES-256-GCM encrypted file fallback — never in `accounts.json`, stdout, stderr, or any log file.
- Trace files at `<configDir>/cliq-cli/traces/<session-name>/trace.jsonl` — `Authorization` headers unconditionally excluded by `TraceWriter` (ADR-0008).
- ZohoCorp accounts hard-blocked at both write (keychain) and read (API dispatch) layers — no internal credential can be stored or used even if `accounts.json` is manually edited.

### 8.3 Scalability & performance

- Self-contained binary with JIT on first run; no warm-up required — each invocation is a fresh, independent process.
- Trace JSONL files grow unboundedly within a session — teams running very long analysis sessions (hundreds of calls with large bodies) should use `--truncate-body` at export time.
- Concurrent `api call` invocations in the same trace session rely on file locking for seq counter integrity; high-concurrency parallel agents should use separate named sessions.
- No in-process state is shared across CLI invocations; horizontal scaling is trivial.

### 8.4 Potential challenges

- **P/Invoke correctness across three OS keychain APIs**: Bugs in the interop layer may surface only on one platform; each platform requires dedicated integration testing on macOS, Windows, and Linux.
- **Self-contained binary size**: 50–80 MB per RID is large for a CLI tool; Native AOT is the long-term mitigation (post-v1).
- **New Zoho datacenter TLDs**: If Zoho introduces a datacenter domain not in the compile-time host allowlist (ADR-0007), API calls will fail until a new binary is released. The ZohoCorp domain check (ADR-0006) is TLD-agnostic by design but the allowlist is not.
- **OAuth2 scope enforcement**: In v1, scope metadata is advisory only — a PAT with broad permissions can call any endpoint regardless of recorded scopes. Full enforcement requires OAuth2 (v2).
- **macOS code-signing**: Developer builds without an Apple signing identity may hit Gatekeeper prompts; distribution via a notarized binary or Homebrew tap is required for smooth installation.

## 9. Milestones & sequencing

### 9.1 Project estimate

- v1 (core CLI): Medium — 6–8 weeks
- v2 (OAuth2 + Pex + Registry): Medium — 4–6 weeks after v1 GA

### 9.2 Team size & composition

- Small team: 1–2 backend engineers (.NET / C#)

### 9.3 Suggested phases

- **Phase 1 — Foundation** (weeks 1–2): Three-project solution scaffold (`CliqCli`, `CliqCli.Core`, `CliqCli.Keychain`), `IKeychainProvider` with all platform implementations and encrypted-file fallback, `IAuthProvider` / `PatAuthProvider`, `IOutputWriter`, JSON output contract, global flags.

  - Key deliverables: `cliq-cli --version` and `cliq-cli --help` functional; keychain round-trip tests passing on macOS, Windows, and Linux; local `dotnet publish` producing working binaries for all four RIDs.

- **Phase 2 — Account & API** (weeks 3–4): Account management command group (`add`, `list`, `remove`, `show`, `set-default`, `re-auth` stub), `ApiClient` with host allowlist and ZohoCorp block, `api call` command, scope scaffolding.

  - Key deliverables: `cliq-cli account add` + `cliq-cli api call` end-to-end functional; ZohoCorp block and host allowlist verified by unit tests; `NEEDS_REAUTH` error path live.

- **Phase 3 — Trace & Utility** (weeks 5–6): `TraceWriter`, `TraceSession`, `TraceExporter`, `trace session` command group, `util` commands, full unit test coverage for `CliqCli.Core` (≥ 80%), README and SKILL.md.

  - Key deliverables: `trace session start/export/close/remove` functional; passive trace recording in `api call` verified; binary passes end-to-end smoke tests on all four RIDs.

- **Phase 4 — v1 GA** (week 7–8): Migration guide from `CliqApiMcp`, SKILL.md for Copilot and Claude agent skill directories, manual binary distribution via GitHub Releases. CI/CD pipeline automation, code-signing, and Homebrew tap are deferred to a post-GA DevOps phase.

  - Key deliverables: `CliqApiMcp` migration guide published; SKILL.md registered in skill directory; v1.0.0 binaries published to GitHub Releases (manual).

- **Phase 5 — v2: OAuth2 + Pex + Registry** (weeks 9–14): `OAuthProvider` implementation, `account re-auth` activation, `pex` command group with WMS WebSocket and buffer, `api registry` commands, `pex drain` trace correlation.

  - Key deliverables: Full feature parity with `CliqApiMcp`; `CliqApiMcp` deprecated; OAuth2 account flow documented.

## 10. User stories

### 10.1. Account management

- **ID**: CLIQ-001
- **Description**: As a Zoho developer, I want to add, list, view, and remove Zoho account credentials from the CLI so that I can manage access to multiple Zoho datacenters from a single tool without manually handling tokens.
- **Acceptance criteria**:
  - `account add --name <n> --token <pat> [--domain <d>]` stores the PAT in the OS keychain and writes metadata (without the token) to `accounts.json`.
  - `account list` returns all accounts with token fields masked; output is valid JSON.
  - `account show --name <n>` returns full metadata with `"token": "***"`.
  - `account remove --name <n>` deletes the keychain secret and the `accounts.json` entry atomically.
  - `account set-default --name <n>` updates `is_default` in `accounts.json`; subsequent commands resolve to this account when `--account` is omitted.
  - Adding a `*@zohocorp.*` account returns `ACCOUNT_DOMAIN_BLOCKED`, exit 1, and no data is persisted.
  - All outputs are valid JSON envelopes on stdout (success) or stderr (error).

### 10.2. PAT-based authentication and secure credential storage

- **ID**: CLIQ-002
- **Description**: As a Zoho developer, I want PAT credentials stored securely in the OS-native keychain (or an encrypted file fallback on headless systems) so that tokens are never exposed in plain text in any file, log, or output.
- **Acceptance criteria**:
  - On macOS, Windows, and Linux, `IKeychainProvider` uses the platform-native store (Security.framework, CredWrite/CredRead, libsecret) without any third-party NuGet keychain package.
  - On headless environments (no OS keychain available), `EncryptedFileKeychainProvider` activates automatically using AES-256-GCM with a machine-derived key.
  - Token values do not appear in stdout, stderr, `accounts.json`, or any trace file under any circumstances.
  - Unit tests for `PatAuthProvider` and `ApiClient` use an in-memory `IKeychainProvider` mock — no real OS keychain interaction required.
  - `IAuthProvider.GetTokenAsync` injects `Authorization: Zoho-oauthtoken <token>` into every outgoing request.

### 10.3. REST API invocation

- **ID**: CLIQ-003
- **Description**: As a Zoho developer and as a Copilot AI agent, I want to call any Zoho Cliq REST endpoint with full control over method, path, headers, query parameters, and body, so that I can prototype integrations and automate API interactions without writing HTTP client code.
- **Acceptance criteria**:
  - `api call --method <M> --path <P> [--body <json>|--body-file <file>] [--header k:v]... [--query k=v]...` dispatches the request and returns the response as `{"status":"ok","data":<response>}` on stdout.
  - `--body` and `--body-file` are mutually exclusive; using both produces `INVALID_ARGS` on stderr before any network I/O.
  - Paths not starting with `/api/` are automatically prefixed with `/api/v2`.
  - The fully resolved request host is validated against the compile-time allowlist before `HttpClient.SendAsync`; hosts not on the list produce `HOST_NOT_ALLOWED`, exit 1.
  - AI agents receive parseable JSON on both stdout and stderr for every invocation.

### 10.4. Scope management

- **ID**: CLIQ-004
- **Description**: As a Zoho developer, I want to record OAuth scope metadata per account so that when OAuth2 is activated in v2, `cliq-cli` can enforce re-authentication after scope changes without a breaking schema migration.
- **Acceptance criteria**:
  - `scope add --scope <s> [--account <n>]` appends the scope string to the account's `scopes[]` array and sets `needs_reauth = true`.
  - `scope remove --scope <s> [--account <n>]` removes the scope and sets `needs_reauth = true`.
  - `scope list [--account <n>]` returns the scope array as a JSON array in the success envelope.
  - An `api call` on an account with `needs_reauth = true` returns `NEEDS_REAUTH`, exit 2, with a clear message directing the user to `account re-auth`.
  - In v1, `account re-auth` returns a clear "not yet supported" error; no partial OAuth flow is attempted.

### 10.5. Session trace recording and export

- **ID**: CLIQ-005
- **Description**: As a Zoho developer and as a Copilot AI agent running an analysis session, I want API calls to be automatically recorded to a named persistent trace so that I can export and review the complete call history — including request/response bodies — after the session ends.
- **Acceptance criteria**:
  - `trace session start --name <n>` creates a new active session; `sessions.json` entry added with `status: active`.
  - All `api call` invocations while a session is active append a trace entry to `<session-name>/trace.jsonl` without any extra flag on `api call`.
  - `Authorization` headers are absent from all trace entries.
  - `trace session export --name <n>` emits a JSON array of all entries to stdout without modifying the `.jsonl` file.
  - `--truncate-body <bytes>` limits body field lengths in the export without modifying the on-disk file.
  - `trace session close` marks the session inactive; `trace session remove` deletes session entry and all files.
  - If no session is active, `api call` proceeds normally and silently drops the trace entry — no error emitted.

### 10.6. Security controls — ZohoCorp block and HTTP host allowlist

- **ID**: CLIQ-006
- **Description**: As the cliq-cli security owner, I want all ZohoCorp-domain accounts unconditionally blocked and all outgoing HTTP requests restricted to known Zoho datacenter hosts, so that the tool cannot be misused by AI agents to access Zoho internal infrastructure or arbitrary external hosts.
- **Acceptance criteria**:
  - Any email whose host's first DNS label equals `zohocorp` (case-insensitive) is rejected at `account add` with `ACCOUNT_DOMAIN_BLOCKED`, exit 1 — covers all datacenter TLDs without a TLD list.
  - The block is also enforced inside `PatAuthProvider.StoreTokenAsync` and `ApiClient.CallAsync` — cannot be bypassed by future command additions.
  - Any outgoing request URL whose host does not match a suffix in the compile-time allowlist (`.zoho.com`, `.zoho.eu`, `.zoho.in`, `.zoho.com.au`, `.zohoapis.com`, `.zohoapis.in`) is rejected with `HOST_NOT_ALLOWED`, exit 1 — no network I/O is performed.
  - No flag, environment variable, or config file can override either restriction.
  - Unit tests verify both blocks using in-memory fakes — no real network or keychain access.

### 10.7. Cross-platform self-contained binary distribution

- **ID**: CLIQ-007
- **Description**: As a Zoho developer, I want to download a single self-contained binary for my platform and use `cliq-cli` immediately without installing .NET or any other runtime, so that the tool is zero-friction to adopt on developer machines and headless environments.
- **Acceptance criteria**:
  - `dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true` produces a working binary for `win-x64`, `osx-x64`, `osx-arm64`, and `linux-x64`.
  - The binary runs on a clean OS (no .NET SDK installed) and produces valid JSON on the first invocation.
  - Binary size per RID ≤ 80 MB.
  - `TreatWarningsAsErrors=true` and `<Nullable>enable</Nullable>` enforced in all `.csproj` files; build fails on any warning or nullable violation.

### 10.8. Pex / WMS real-time event buffering (v2)

- **ID**: CLIQ-008
- **Description**: As a Copilot AI agent running a Cliq API analysis session, I want to connect to Cliq's Pex/WMS real-time event stream, buffer events locally, and drain them as a JSON array, so that I can correlate real-time Cliq events with the API calls that triggered them.
- **Acceptance criteria**:
  - `pex connect [--account <n>]` opens a Pex/WMS WebSocket and begins buffering incoming callback events to `<configDir>/cliq-cli/pex-buffer/<account>.jsonl`.
  - `pex drain [--account <n>]` atomically reads and truncates the buffer; returns all events as a JSON array and appends `pex` trace entries to any active session with `relatedApiSeq` pointing to the most recent preceding `api` entry.
  - `pex clear` discards buffered events without returning them.
  - `pex listen` streams events to stdout as newline-delimited JSON (blocking; Ctrl+C to stop).
  - `pex close` closes the WebSocket.
  - Token values never appear in Pex buffer files.

### 10.9. OAuth2 authentication (v2)

- **ID**: CLIQ-009
- **Description**: As a Zoho developer, I want to authenticate accounts using OAuth2 (machine-to-machine, no browser) with fine-grained scope enforcement, so that token access is automatically restricted to the declared scopes and tokens can be refreshed without manually re-adding the account.
- **Acceptance criteria**:
  - `OAuthProvider` implements `IAuthProvider`; `ApiClient` and all command classes require no changes.
  - `account add` supports `--client-id` and `--client-secret` flags when `OAuthProvider` is active.
  - `account re-auth --name <n>` exchanges a new token via the Zoho OAuth API and updates the OS keychain; sets `needs_reauth = false`.
  - OAuth access and refresh tokens are stored in the OS keychain under the same naming convention as PATs (`cliq-cli:<accountName>:oauth`).
  - `scope add/remove` triggers `needs_reauth = true`; subsequent `api call` returns `NEEDS_REAUTH` until `re-auth` is run.
  - No browser window, redirect URL, or local HTTP server is required at any point in the flow.

### 10.10. API registry (v2)

- **ID**: CLIQ-010
- **Description**: As a Zoho developer, I want to maintain a local catalogue of named Cliq API endpoints with methods, URL templates, and purpose descriptions, so that I can quickly recall and share standardised endpoint definitions without memorising URL patterns.
- **Acceptance criteria**:
  - `api registry add --id <id> --method <M> --url-template <t> --purpose <p>` upserts an entry into `<configDir>/cliq-cli/registry.json`.
  - `api registry list` returns all entries as a JSON array.
  - `api registry show --id <id>` returns a single entry by ID.
  - `api registry remove --id <id>` deletes the entry.
  - `registry.json` format is compatible with the existing `CliqApiMcp` registry format for seamless migration.
  - All operations return valid JSON envelopes; `REGISTRY_ENTRY_NOT_FOUND` returned when an ID does not exist.

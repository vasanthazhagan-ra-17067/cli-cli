# PRD: cliq-cli — Zoho Cliq Command-Line Interface

**Version:** 1.0  
**Date:** 2026-03-16  
**Author:** TBD  

---

## 1. Overview

`cliq-cli` is a standalone, cross-platform command-line tool that lets users and AI agents interact with Zoho Cliq programmatically — without a browser or the desktop app. It manages multiple Zoho accounts, handles authentication securely, and exposes a general-purpose way to call Cliq REST APIs from the terminal. The primary consumer in v1 is AI agents running inside GitHub Copilot CLI Skills; the secondary consumer is any developer or automation script that needs to drive Cliq from a shell.

---

## 2. Problem Statement

Today, interacting with Zoho Cliq APIs from a script or an AI agent requires the caller to manage authentication tokens manually, construct raw HTTP requests, and handle errors with no shared convention. There is no official CLI, so every team or automation reinvents the same plumbing. This friction is especially painful for AI agents that need to read or write to Cliq channels as part of a larger workflow — they either cannot do it at all, or they use brittle ad-hoc scripts that break when tokens expire or domains differ.

---

## 3. Goals & Success Criteria

**Goals**
- Give AI agents a reliable, documented way to invoke Cliq APIs through a CLI skill.
- Let developers manage multiple Zoho accounts (work, personal, staging) from a single tool.
- Store credentials securely using OS-native keychains so tokens never appear in config files, logs, or shell history.
- Produce machine-readable (JSON) output on every invocation so callers can parse results without screen-scraping.

**Success Criteria**
- An AI agent can add an account, authenticate, and call a Cliq REST endpoint in a single session without human intervention.
- A developer can switch between multiple Zoho accounts and domains without editing config files manually.
- No credential value ever appears in any file on disk, in stdout, or in stderr.
- All commands exit with a predictable exit code so scripts can branch on success or failure.

**Out of Scope (v1)**
- OAuth2 / browser-based authentication flows.
- Real-time WebSocket or Pex connections.
- An interactive TUI or shell mode.
- Simultaneous multi-account API calls in a single invocation.
- Caching or offline response storage.

---

## 4. Target Users & Personas

| Persona | Description | Primary need |
|---------|-------------|--------------|
| AI Agent (Copilot Skill) | An automated agent running inside a GitHub Copilot CLI skill that needs to read channels, post messages, or query Cliq resources as part of a development workflow | A stable, scriptable interface with JSON output and clear error codes |
| Developer / Power User | A developer building integrations or debugging Cliq APIs who wants a quick way to fire requests without writing code | Ergonomic command syntax, multi-account support, and safe credential handling |
| CI / Automation Script | An unattended script in a CI pipeline that posts notifications or reads Cliq data | Predictable exit codes, `--no-input` mode, and machine-readable output |

---

## 5. User Stories

- As an **AI agent**, I want to call any Cliq REST endpoint with a single command so that I can query or update Cliq resources without additional scripting.
- As an **AI agent**, I want the tool to fail with a clear, structured error when authentication is broken so that I can surface the problem to the user rather than silently producing wrong results.
- As a **developer**, I want to add and switch between multiple Zoho accounts so that I can work with my work account, personal account, and sandbox in the same environment.
- As a **developer**, I want my API tokens stored in the OS keychain, not in a plain-text file, so that I do not have to worry about accidentally committing credentials.
- As a **developer**, I want to see a masked representation of my account (not the raw token) when I list or inspect accounts so that I can verify my setup without exposing secrets.
- As a **CI script**, I want to run the tool with `--no-input` so that it never hangs waiting for keyboard input in an automated environment.
- As a **developer**, I want to pass custom HTTP headers and query parameters when calling an API so that I can test edge cases and advanced API features.
- As a **developer**, I want a consistent JSON envelope on both success and failure so that I can pipe `cliq-cli` output directly into `jq` or other tools without special-casing.

---

## 6. Feature Description

### 6.1 Account Management (Core Flow)

1. A user runs `cliq-cli account add`, supplying a friendly name, a Personal Access Token, and optionally a Zoho domain.
2. The tool validates that the account name is not already in use.
3. The token is stored securely in the OS keychain; the config file records the account name, domain, and metadata — never the token itself.
4. If this is the first account added, it becomes the default account automatically.
5. The user can list all accounts; token values are masked in the output.
6. The user can switch which account is default, or remove an account entirely (which also deletes the stored token).

### 6.2 API Invocation (Core Flow)

1. A user runs `cliq-cli api call --method GET --path /channels`.
2. The tool resolves which account to use (from `--account` flag or the default account).
3. The token for that account is retrieved from the OS keychain.
4. The HTTP request is sent to the Cliq REST API, adding the correct auth header automatically.
5. The full API response is printed to stdout wrapped in a `{"status": "ok", "data": ...}` envelope.
6. If the server returns an error, the error detail is printed to stderr as `{"error": "...", "code": "...", "exitCode": 1}` and the process exits with a non-zero code.

### 6.3 Scope Management Flow

1. A user runs `cliq-cli scope add --scope <scope-name>` to register that an account now requires a new permission scope.
2. The tool records the new scope against the account and flags that re-authentication is needed.
3. Subsequent API calls on that account fail with a clear "needs re-auth" error until the user re-authenticates (v2 feature).
4. The user can list or remove scopes at any time.

### 6.4 Alternative Flows

- **Account override per call:** Any command can specify `--account <name>` to bypass the default account for that invocation only.
- **Inline vs. file body:** When calling an API, the request body can be provided inline (`--body '{"key":"val"}'`) or as a path to a JSON file (`--body-file ./payload.json`). Using both simultaneously is rejected with an error.
- **Path shorthand:** If the supplied path does not start with `/api/`, the tool prepends `/api/v2` automatically so users can type `/channels` instead of `/api/v2/channels`.

### 6.5 Edge Cases & Error States

| Scenario | Expected behaviour |
|----------|--------------------|
| Account name already exists on `account add` | Error printed to stderr; process exits with code 1; no changes saved |
| Named account not found on any command | Error with code `ACCOUNT_NOT_FOUND`; exit 1 |
| No default account set and `--account` not supplied | Error with code `NO_DEFAULT_ACCOUNT`; exit 1 |
| OS keychain unavailable | Tool falls back to an encrypted local file; user is not asked for a passphrase interactively |
| Token rejected by the Cliq API (HTTP 401/403) | Error with code `AUTH_FAILURE`; exit 2 |
| Account has pending scope changes | Any API call on that account fails with `NEEDS_REAUTH`; exit 2 |
| `--body` and `--body-file` both supplied | Error with code `INVALID_ARGS`; exit 1; no request sent |
| `Ctrl+C` / process interrupted | Graceful cancellation; exit 1 |
| JSON file at `--body-file` is missing or unreadable | Error with code `IO_ERROR`; exit 1 |

---

## 7. UX & Design Considerations

- **JSON everywhere:** Every response — success and failure alike — is JSON. This makes the tool trivially composable with `jq`, shell scripts, and AI agents without any output parsing. Human-readable text is never the primary output format.
- **Token safety:** The word "token" should never appear next to an actual token value in any output. Masked display (`***`) must be applied consistently across `account show` and `account list`.
- **Discoverability:** `--help` on every command and subcommand should produce concise, accurate usage text. New users should be able to get started with account add and api call without reading documentation.
- **No surprises in `--no-input` mode:** Any command that would normally prompt (e.g., for a missing required flag) must instead fail immediately with a clear error. CI environments must never hang.
- **Global `--account` override:** Users working across multiple accounts daily should never need to edit config files; the flag-level override covers all ad-hoc account switching.
- **Minimal install footprint:** The tool ships as a single self-contained binary. No runtime, virtual environment, or external keychain library installation should be required.

---

## 8. Dependencies & Impacted Areas

| Dependency / Area | Relationship |
|-------------------|--------------|
| Zoho Cliq REST API | The tool is a thin client over the existing Cliq API surface; it does not introduce new server-side changes |
| GitHub Copilot CLI Skills (`cliq-cli` skill) | The primary integration point; the skill definition will call this tool's commands |
| OS Keychain (macOS Keychain, Windows Credential Manager, Linux Secret Service) | Required for secure token storage; the tool must degrade gracefully where the keychain is unavailable |
| CI/CD environments (GitHub Actions, etc.) | Scripts and workflows consuming the tool must be able to rely on `--no-input` and stable exit codes |

---

## 9. Open Questions

| # | Question | Owner | Status |
|---|----------|-------|--------|
| 1 | What is the rollout plan — will this be distributed via a package manager (Homebrew, winget, apt) or only as a direct binary download? | Product / Engineering | Open |
| 2 | Should `account re-auth` for PAT accounts allow the user to supply a new token in v1, or is it strictly deferred to the OAuth v2 milestone? | Product | Open |
| 3 | Are there any Cliq API rate limits that the tool should surface to the caller, or is transparent pass-through sufficient? | Engineering | Open |
| 4 | Does the CI/CD persona need a way to supply the token via an environment variable (e.g., `CLIQ_TOKEN`) instead of the keychain, to avoid keychain setup in ephemeral runners? | Product / DevOps | Open |
| 5 | What is the versioning and update strategy? Should users be notified of new versions, or is silent usage of a pinned binary preferred? | Product | Open |

---

## 10. Assumptions

- Users already have a valid Zoho Cliq Personal Access Token; the tool does not guide token creation.
- AI agents consuming this tool are capable of parsing structured JSON output and acting on error codes without additional human guidance.
- A single PAT covers all the API scopes the user needs; scope management is a preparatory feature for future OAuth flows, not a blocker for v1 API calls.
- The `cliq-cli` Copilot skill and this binary will be versioned and released together so that the skill's command syntax always matches the installed binary.
- Developers running the tool locally have access to a functional OS keychain; the encrypted-file fallback is intended only for edge cases such as headless servers.

---

## 11. Constraints

- Token values must never be persisted in plain text on disk, emitted to stdout or stderr, or captured in any log.
- The tool must work without root or administrator privileges on all supported platforms.
- The binary must be self-contained (no external runtime installation required by the end user).
- `--no-input` mode must be respected unconditionally; no interactive prompt may appear in any code path once this flag is set.
- Initial release covers PAT authentication only; the design must not preclude adding OAuth2 in a future version.

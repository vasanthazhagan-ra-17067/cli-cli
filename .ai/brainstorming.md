# cliq-cli — Brainstorming

> **Status:** Brainstorming complete — ready for technical spec phase.
> **Task directory:** `tasks/cliq-cli/`
> **Date:** 2026-03-12

---

## Project Vision

**cliq-cli** is a standalone, C# multi-platform CLI tool for interacting with Zoho Cliq APIs. It manages multiple Zoho accounts, handles PAT/OAuth authentication via a pluggable interface, and exposes a general-purpose HTTP API invoker.

**Primary consumer:** AI Agents, invoked as GitHub Copilot **CLI Skills**.

```sh
cliq-cli account add --name "work" --token "myPAT"
cliq-cli account set-default --name "work"
cliq-cli api call --method GET --path "/api/v2/channels"
cliq-cli api call --method POST --path "/api/v2/channels/{id}/messages" --body '{"text":"hello"}'
```

**Why this exists:**
- AI agents need a deterministic, scriptable interface to Cliq APIs without needing a browser or GUI.
- No existing tool combines Cliq auth management + general API invocation in a single binary.
- Designed to be packaged as a self-contained GitHub Copilot Agent CLI Skill **and** Claude Agent Skill.

---

## Replacing CliqApiMcp

`cliq-cli` is the direct replacement for the **CliqApiMcp** macOS app (currently at `~/Downloads/CliqApiMcp`), which exposes Cliq API access as an MCP server over stdio.

### Why CliqApiMcp is being replaced

| Limitation | Details |
|---|---|
| **macOS-only** | Requires `NSApplication` run loop; cannot run headlessly on Linux CI or Windows dev machines |
| **GUI-dependent auth** | Uses `ZMacAuth` / ZohoSSO login views (`NSView`) — cannot authenticate without a running UI session |
| **Internal SDK dependency** | Tied to `ZMacAuth` and `PEXLibrary` CocoaPods — unavailable outside Zoho internal network |
| **MCP protocol coupling** | Requires VS Code to spawn a specific binary via `mcp.json`; hard to use from non-VS-Code agents (Claude Desktop, CI pipelines, shell scripts) |
| **No cross-platform binary** | Not distribuable as a portable tool; cannot be published to package managers or a GitHub Release |

### Feature mapping: MCP tools → CLI commands

| MCP Tool | CLI Equivalent | Epic |
|---|---|---|
| `list_accounts` | `cliq-cli account list` | Epic 1 |
| `execute_request` | `cliq-cli api call` | Epic 3 |
| `get_pex_messages` | `cliq-cli pex drain` | Epic 5 |
| `clear_pex_messages` | `cliq-cli pex clear` | Epic 5 |
| `connect_pex` | `cliq-cli pex connect` | Epic 5 |
| `disconnect_pex` | `cliq-cli pex close` | Epic 5 |
| `get_current_time_in_milliseconds` | `cliq-cli util time-ms` | Epic 8 |
| `list_apis` | `cliq-cli api registry list` | Epic 7 |
| `register_api` | `cliq-cli api registry add` | Epic 7 |
| `get_api` | `cliq-cli api registry show` | Epic 7 |
| `delete_api` | `cliq-cli api registry remove` | Epic 7 |

### How agents consume the CLI instead of MCP

Rather than registering a running app binary in `mcp.json`, agents invoke the CLI directly as a skill:

- **GitHub Copilot CLI Skill:** the `SKILL.md` describes available commands — Copilot calls `cliq-cli <group> <subcommand>` subprocesses and reads stdout JSON.
- **Claude Agent Skill:** analogous — the skill manifest lists available tools, each mapping to a `cliq-cli` invocation.
- The binary is self-contained, cross-platform, and requires no background process or network daemon.

### Migration path

1. Install `cliq-cli` on the target machine (`dotnet publish` artifact or package-manager package).
2. Run `cliq-cli account add --name "work" --token "<PAT>"` to register accounts (replaces ZMacAuth login).
3. Remove the `CliqApiMcp` entry from `.vscode/mcp.json`.
4. Add the `cliq-cli` skill manifest to the agent's skill directory.
5. Pex/WMS connectivity: use `cliq-cli pex connect` after account setup instead of automatic connection at app launch.

---

## Decisions & Data

### Platform

- **Targets:** Windows, macOS, Linux
- **Runtime:** .NET 10, C# 13+
- **Packaging:** Single self-contained binary (`dotnet publish -r <rid> /p:PublishSingleFile=true`)
- **Infrastructure:** Independent codebase — no shared library dependencies by default.

---

### Authentication

| Phase | Mechanism | Status |
|-------|-----------|--------|
| v1 | PAT (Personal Access Token) | Implement |
| v2 | OAuth2 via Zoho API (no browser) | Deferred |

**Design principle:** Interface-first. `IAuthProvider` abstraction — concrete implementations can be swapped without touching any command-layer code.

#### Auth Interface Contract

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName);
    Task StoreTokenAsync(string accountName, string token);
    Task ClearTokenAsync(string accountName);
}

// v1
public class PatAuthProvider : IAuthProvider { ... }

// Future
public class OAuthProvider : IAuthProvider { ... }
```

#### PAT Flow (v1)

```
cliq-cli account add --name "work" --token "xxx"
  → writes account metadata to accounts.json
  → stores PAT secret in OS keychain under key: cliq-cli:work:pat

cliq-cli api call --method GET --path "/api/v2/channels"
  → resolves active account
  → fetches PAT from OS keychain via PatAuthProvider
  → injects as Authorization: Zoho-oauthtoken <token>
```

#### OAuth Flow (future)

```
cliq-cli account add --name "work" --client-id "..." --client-secret "..." --scope "ZohoCliq.Channels.READ"
  → calls Zoho OAuth API entirely in terminal (no browser)
  → stores access + refresh tokens in OS keychain
  → stores scope list in accounts.json

cliq-cli scope add --account "work" --scope "ZohoCliq.Messages.READ"
  → updates scopes[] for that account in accounts.json
  → sets needs_reauth = true on the account

cliq-cli account re-auth --name "work"
  → calls Zoho OAuth token refresh/exchange API
  → stores new tokens
  → clears needs_reauth flag
```

---

### Accounts

- **Multi-account:** Yes — multiple Zoho accounts can be configured simultaneously.
- **Active account:** Set via `cliq-cli account set-default --name "work"` — persisted in `accounts.json`.
- **Per-command override:** `--account "personal"` flag available on any command.

#### Account Metadata Fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | User-given alias |
| `domain` | string | Datacenter domain (`zoho.com`, `zoho.eu`, `zoho.in`, `zoho.com.au`) |
| `scopes` | string[] | OAuth permission strings |
| `token_type` | `pat` \| `oauth` | Auth mechanism in use |
| `is_default` | bool | Whether this is the active account |
| `needs_reauth` | bool | Set to `true` when scopes have changed and a new token is required |

#### `accounts.json` Example

```json
{
  "accounts": [
    {
      "name": "work",
      "domain": "zoho.com",
      "scopes": ["ZohoCliq.Channels.READ", "ZohoCliq.Messages.WRITE"],
      "token_type": "pat",
      "is_default": true,
      "needs_reauth": false
    },
    {
      "name": "personal",
      "domain": "zoho.eu",
      "scopes": [],
      "token_type": "pat",
      "is_default": false,
      "needs_reauth": false
    }
  ]
}
```

---

### Scopes

- Scopes are **per-account** — each account maintains its own independent scope list.
- Scopes are OAuth permission strings (e.g., `ZohoCliq.Channels.READ`).
- Stored per-account in `accounts.json` alongside other metadata (secrets are never stored in JSON).
- All scope commands operate on a specific account (via `--account` flag or the active/default account).
- Adding or removing a scope sets `needs_reauth = true` on **that account**.
- **PAT phase:** scope metadata is recorded but no token re-generation is triggered — re-auth is a no-op with PAT. Fully activated in the OAuth phase.
- On the next `api call` with `needs_reauth = true`: the CLI outputs an error guiding the user to run `account re-auth`.

#### Re-auth Trigger Flow

```
cliq-cli scope add --account "work" --scope "ZohoCliq.Messages.READ"
  └─ update scopes[] for account "work" in accounts.json
  └─ set needs_reauth = true on account "work"

next api call using account "work"
  └─ error to stderr:
     { "error": "Account 'work' has scope changes pending. Run: cliq-cli account re-auth --name work",
       "code": "NEEDS_REAUTH", "exitCode": 2 }

cliq-cli account re-auth --name "work"
  └─ (OAuth) exchange new token via Zoho OAuth API
  └─ update OS keychain for account "work"
  └─ set needs_reauth = false on account "work"
```

---

### Storage

| What | Location | Format |
|------|----------|--------|
| Account metadata | `<configDir>/cliq-cli/accounts.json` | Plain JSON |
| Secrets (PAT / tokens) | OS Keychain (platform-native) | OS-managed, not on disk |
| Keychain key format | `cliq-cli:<accountName>:<tokenType>` | — |
| Keychain fallback | `<configDir>/cliq-cli/keystore/` (encrypted file) | Encrypted JSON |

**Platform config directory (`<configDir>`):**

| Platform | Path |
|----------|------|
| macOS | `~/Library/Application Support/cliq-cli/` |
| Windows | `%LOCALAPPDATA%\cliq-cli\` |
| Linux | `~/.config/cliq-cli/` |

**Security rules:**
- `accounts.json` written with restrictive permissions (`0600` on Unix, ACL-restricted on Windows).
- Tokens are **never** written to `stdout` or `accounts.json`.
- Errors referencing auth failures use generic messages — no token values in stderr.

---

### API Base URL and Host Allowlist

The base URL for Cliq REST APIs is derived from the active account's `domain` field — never hardcoded:

| Datacenter | `domain` field | Cliq Base URL |
|------------|----------------|---------------|
| US | `zoho.com` | `https://cliq.zoho.com/api/v2` |
| EU | `zoho.eu` | `https://cliq.zoho.eu/api/v2` |
| IN | `zoho.in` | `https://cliq.zoho.in/api/v2` |
| AU | `zoho.com.au` | `https://cliq.zoho.com.au/api/v2` |

Resolution: `https://cliq.{domain}/api/v2` + `--path` argument.

**Host allowlist (enforced in `ApiClient` before every request):**

The outgoing URL host must end with one of the following suffixes. Any call to an unlisted host is rejected with `HOST_NOT_ALLOWED`, exit 1.

| Suffix | Covers |
|---|---|
| `zoho.com` | US/AU Cliq REST + Accounts |
| `zoho.eu` | EU Cliq REST |
| `zoho.in` | IN Cliq REST |
| `zoho.com.au` | AU Cliq REST |
| `zohoapis.com` | US Cliq API domain (used by some internal endpoints) |
| `zohoapis.in` | IN Cliq API domain |

> **Why `zohoapis.*`?** The MCP's `APIExecutor` included these as well — some Cliq API endpoints resolve to `*.zohoapis.com` / `*.zohoapis.in` rather than `*.zoho.com`. The allowlist must include them or those calls will be silently blocked.

---

### Output Contract

- **stdout:** JSON always (structured result or success envelope).
- **stderr:** JSON error envelope on all failures.
- **Exit codes:** `0` = success, `1` = general error, `2` = auth failure / needs-reauth.

#### Success Example

```json
{
  "status": "ok",
  "data": { ... }
}
```

#### Error Envelope (stderr)

```json
{ "error": "account 'nonexistent' not found", "code": "ACCOUNT_NOT_FOUND", "exitCode": 1 }
{ "error": "authentication failed", "code": "AUTH_FAILURE", "exitCode": 2 }
{ "error": "API returned 403 Forbidden", "code": "API_ERROR", "detail": "...", "exitCode": 1 }
{ "error": "Account 'work' has scope changes pending. Run: cliq-cli account re-auth --name work", "code": "NEEDS_REAUTH", "exitCode": 2 }
{ "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.", "code": "ACCOUNT_DOMAIN_BLOCKED", "exitCode": 1 }
```

---

## Use Cases (Stories)

### Epic 1 — Account Management

| ID | Command | Description |
|----|---------|-------------|
| UC-1 | `cliq-cli account add --name "work" --token "xxx"` | Add a PAT-backed account |
| UC-2 | `cliq-cli account list` | List all configured accounts |
| UC-3 | `cliq-cli account remove --name "work"` | Remove account + clear keychain secret |
| UC-4 | `cliq-cli account show --name "work"` | Show account details (token masked as `***`) |
| UC-5 | `cliq-cli account set-default --name "work"` | Set the active/default account |
| UC-6 | `cliq-cli account re-auth --name "work"` | Re-authenticate after scope change (v2 OAuth) |

### Epic 2 — Scope Management

All scope commands operate on a specific account. `--account` defaults to the active/default account if omitted.

| ID | Command | Description |
|----|---------|-------------|
| UC-7 | `cliq-cli scope add --scope "ZohoCliq.Channels.READ" [--account "work"]` | Add scope to an account |
| UC-8 | `cliq-cli scope remove --scope "ZohoCliq.Channels.READ" [--account "work"]` | Remove a scope from an account |
| UC-9 | `cliq-cli scope list [--account "work"]` | List scopes for an account |

### Epic 3 — General-Purpose API Invocation

| ID | Command | Description |
|----|---------|-------------|
| UC-10 | `cliq-cli api call --method GET --path "/api/v2/channels"` | GET request |
| UC-11 | `cliq-cli api call --method POST --path "..." --body '{...}'` | POST with inline JSON body |
| UC-12 | `cliq-cli api call --method POST --path "..." --body-file ./req.json` | POST with body from file |
| UC-13 | `cliq-cli api call --method PUT --path "..." --header "X-Custom: val"` | Custom request headers |
| UC-14 | `cliq-cli api call ... --account "personal"` | Override account for a single call |
| UC-15 | `cliq-cli api call ... --query "key=value"` | Append query parameters |

### Epic 4 — WebSocket (Future)

| ID | Command | Description |
|----|---------|-------------|
| UC-16 | `cliq-cli ws connect --url "wss://..." --name "ws-1"` | Open a named WebSocket connection |
| UC-17 | `cliq-cli ws send --name "ws-1" --message '{"type":"ping"}'` | Send a message via the connection |
| UC-18 | `cliq-cli ws listen --name "ws-1"` | Stream incoming messages to stdout as JSON lines |
| UC-19 | `cliq-cli ws close --name "ws-1"` | Close the connection |

### Epic 6 — ZohoCorp Account Restriction (Privacy & Security)

> **Rationale:** ZohoCorp accounts (`*@zohocorp.com`) are internal employee accounts tied to Zoho's own corporate infrastructure. Allowing a CLI tool — particularly one used by AI agents — to authenticate with, store credentials for, or make API calls on behalf of these accounts creates an unacceptable privacy and security risk. All operations involving a ZohoCorp-domain account must be hard-blocked at the earliest possible entry point.

**Blocked entry points (in order of evaluation):**

| Entry point | Check performed |
|-------------|----------------|
| `account add` | Token's associated email domain resolved at add time; reject if `zohocorp.com` |
| `api call` (active account) | Resolved account's stored email checked before any HTTP request is dispatched |
| `api call --account <name>` | Same check applied to the named account override |
| Any `scope` command | Account resolved first; reject if ZohoCorp domain |

**Detection:** An account is identified as a ZohoCorp account when its email address matches the pattern `@zohocorp.<tld>` — i.e., the organisational part of the domain is exactly `zohocorp`, regardless of datacenter TLD suffix. Known ZohoCorp datacenter domains include `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and any future regional variants. The match is performed as a case-insensitive suffix check against the set of blocked org-domain patterns (`*.zohocorp.*` normalised to: domain host ends with `.zohocorp.<tld>` or equals `zohocorp.<tld>`). The email is captured during `account add` (retrieved from the Zoho accounts info API or provided explicitly) and stored in `accounts.json` as the `email` field.

| ID | Scenario | Expected behaviour |
|----|----------|--------------------|
| UC-24 | `account add` with a `@zohocorp.com` PAT | Rejected immediately; keychain write never happens; exit 1 with `ACCOUNT_DOMAIN_BLOCKED` |
| UC-25 | `api call` resolves to a ZohoCorp account (default or `--account`) | Rejected before any HTTP request; exit 1 with `ACCOUNT_DOMAIN_BLOCKED` |
| UC-26 | `scope add/remove/list` targets a ZohoCorp account | Rejected; no mutation to `accounts.json`; exit 1 with `ACCOUNT_DOMAIN_BLOCKED` |
| UC-27 | Existing accounts.json already contains a ZohoCorp account (e.g., migrated from an older version) | Any command that resolves to that account is rejected; tooling should also emit a warning on startup |

**Error output (stderr):**

```json
{ "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.", "code": "ACCOUNT_DOMAIN_BLOCKED", "exitCode": 1 }
```

**Design notes:**
- The blocked-domain list must be a constant in `CliqCli.Core` — not a runtime config that can be overridden via flags or environment variables. The check is implemented as: `email.Split('@')[1].Split('.')[0].Equals("zohocorp", StringComparison.OrdinalIgnoreCase)` — i.e., block any email whose domain's first label is `zohocorp`, covering `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and all future datacenter TLDs automatically.
- The check must run in `PatAuthProvider.StoreTokenAsync` and at the start of `ApiClient.CallAsync`, so that no future auth provider or command can bypass it inadvertently.
- On `account add`, the email must be fetched from the Zoho user-info endpoint (or equivalent) before the account is persisted, so the block applies even if the user does not explicitly supply their email.

---

### Epic 5 — Pex / Real-time Chat (Future)

> Pex is Zoho Cliq's proprietary real-time protocol — ping-pong + chat message delivery.
> **CliqApiMcp equivalent:** `connect_pex`, `disconnect_pex`, `get_pex_messages`, `clear_pex_messages`.
> The MCP captures all WMS callbacks into a per-account in-memory cache; agents drain the cache on demand.
> The CLI models the same pattern: `pex connect` opens the socket, callbacks are buffered in a local log, `pex drain` retrieves and clears them, `pex clear` discards without returning.

| ID | Command | Description |
|----|---------|-------------|
| UC-20 | `cliq-cli pex connect --account "work"` | Open a Pex/WMS WebSocket for the named account |
| UC-21 | `cliq-cli pex send --account "work" --message '{"type":"ping"}'` | Send a raw message over the Pex socket |
| UC-22 | `cliq-cli pex drain --account "work"` | Return all buffered Pex callback events since last drain, then clear the buffer (JSON array to stdout) |
| UC-23 | `cliq-cli pex clear --account "work"` | Discard all buffered Pex events without returning them |
| UC-28 | `cliq-cli pex listen --account "work"` | Stream incoming Pex events to stdout as newline-delimited JSON (blocking, Ctrl+C to stop) |
| UC-29 | `cliq-cli pex close --account "work"` | Close the Pex WebSocket for the named account |

**Buffer model:** Pex events are appended to a local buffer file at `<configDir>/cliq-cli/pex-buffer/<account>.jsonl`. `drain` atomically reads and truncates this file. `listen` tails it in real time.

---

### Epic 7 — API Registry (Future)

> The MCP had a persistent `registry.json` of known Cliq endpoints (`id`, `method`, `urlTemplate`, `purpose`). Agents use it to discover prerequisite endpoints and avoid re-documenting the same URL twice.
> The CLI carries this forward as an `api registry` subgroup backed by the same JSON format.

**Storage:** `<configDir>/cliq-cli/registry.json` — same location and schema as `CliqApiMcp`.

```json
{
  "apis": [
    { "id": "list-channels", "method": "GET", "urlTemplate": "/api/v2/channels", "purpose": "Returns all channels the authenticated user can access" }
  ]
}
```

| ID | Command | Description |
|----|---------|-------------|
| UC-30 | `cliq-cli api registry list` | List all registered API entries |
| UC-31 | `cliq-cli api registry add --id "list-channels" --method GET --url-template "/api/v2/channels" --purpose "..."` | Upsert an entry (add or overwrite by id) |
| UC-32 | `cliq-cli api registry show --id "list-channels"` | Show a single entry by id |
| UC-33 | `cliq-cli api registry remove --id "list-channels"` | Delete an entry by id |

**Agent workflow:** at the end of an analysis session the agent calls `api registry add` once per new endpoint documented — building up a session-persistent lookup index for dependency resolution (e.g., know that `GET /channels` must be called before `POST /channels/{channelId}/messages`).

---

### Epic 8 — Utility Commands

> Thin utility commands that agents need in lieu of invoking system tools. The MCP provided `get_current_time_in_milliseconds`; the CLI exposes these as a `util` command group.

| ID | Command | Description |
|----|---------|-------------|
| UC-34 | `cliq-cli util time-ms` | Current UTC time as a Unix millisecond timestamp (stdout JSON: `{"ts": 1710000000000}`) |
| UC-35 | `cliq-cli util uuid` | Generate a random UUID v4 (useful for idempotency keys in API bodies) |

---

```
cliq-cli [--account <name>] [--json] [--no-input] [--help] [--version]
         <group> <subcommand> [flags]

Groups:
  account   Manage Zoho accounts
  scope     Manage OAuth scopes per account (--account to target a specific account)
  api       Invoke Cliq REST API endpoints; manage local API registry
  pex       (future) Pex/WMS real-time WebSocket sessions
  util      Utility helpers for agents (timestamps, UUIDs, etc.)
  ws        (future) Generic WebSocket connections

Global Flags:
  --account   string   Override active account for this invocation
  --json               Force JSON output to stdout (default: true)
  --no-input           Never prompt interactively; fail instead
  --help               Show help for current command / group
  --version            Print version and exit

api subcommands:
  api call             Fire an HTTP request against a Cliq API endpoint
  api registry list    List all entries in the local API registry
  api registry add     Upsert an endpoint into the registry
  api registry show    Show a single registry entry by id
  api registry remove  Delete an entry from the registry

pex subcommands:
  pex connect          Open a Pex/WMS WebSocket for an account
  pex send             Send a raw message over the socket
  pex drain            Return + clear the buffered event log
  pex clear            Discard buffered events without returning them
  pex listen           Stream events to stdout as newline-delimited JSON (blocking)
  pex close            Close the socket

util subcommands:
  util time-ms         Current UTC time as a millisecond epoch timestamp
  util uuid            Generate a random UUID v4
```

---

## Tech Choices

| Concern | Choice | Reasoning |
|---------|--------|-----------|
| Runtime | .NET 10 | Latest stable, cross-platform, single-binary publish |
| Language | C# 13+ | Records, file-scoped namespaces, pattern matching, nullable types |
| CLI Framework | `Spectre.Console.Cli` | Mature, convention-based, handles commands/subcommands/flags cleanly |
| JSON | `System.Text.Json` | Built-in stdlib, no extra dependency, good performance |
| HTTP | `HttpClient` (stdlib) | Sufficient for a general API invoker |
| DI | `Microsoft.Extensions.DependencyInjection` | Standard .NET DI container |
| OS Keychain | TBD: `Microsoft.Windows.Security.Credentials` / Security.framework / `libsecret` via P/Invoke, or cross-platform NuGet (evaluate `git-credential-manager` keyring) | Platform-native secret storage |
| Config I/O | Custom `AccountStore` (lightweight JSON read/write) | Simple flat store; no config framework needed |
| Logging | `Microsoft.Extensions.Logging` | Structured, standard |
| Publish | `dotnet publish -r <rid> /p:PublishSingleFile=true` | Self-contained single binary |
| Test | xUnit | Convention-aligned, cross-platform |

### Suggested Project Structure

```
cliq-cli/
├── src/
│   ├── CliqCli/                         ← Entry point + Spectre command wiring
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── AccountCommands.cs       ← add, list, remove, show, set-default, re-auth
│   │       ├── ScopeCommands.cs         ← add, remove, list
│   │       ├── ApiCommands.cs           ← api call
│   │       ├── ApiRegistryCommands.cs   ← api registry list/add/show/remove
│   │       ├── PexCommands.cs           ← pex connect/send/drain/clear/listen/close
│   │       └── UtilCommands.cs          ← util time-ms, util uuid
│   │
│   ├── CliqCli.Core/                    ← Domain logic (no CLI concerns)
│   │   ├── Auth/
│   │   │   ├── IAuthProvider.cs
│   │   │   └── PatAuthProvider.cs
│   │   ├── Accounts/
│   │   │   ├── AccountStore.cs          ← JSON config read/write
│   │   │   └── AccountConfig.cs         ← Model: account metadata + DTO
│   │   ├── Api/
│   │   │   ├── ApiClient.cs             ← HttpClient wrapper; injects auth header; host allowlist
│   │   │   └── ApiRegistry.cs           ← registry.json read/write
│   │   └── Pex/
│   │       └── PexBuffer.cs             ← per-account JSONL event buffer (future)
│   │
│   └── CliqCli.Keychain/                ← OS keychain abstraction
│       ├── IKeychainProvider.cs
│       ├── MacOsKeychainProvider.cs
│       ├── WindowsKeychainProvider.cs
│       ├── LinuxKeychainProvider.cs
│       └── EncryptedFileKeychainProvider.cs  ← fallback when no OS keychain
│
└── tests/
    └── CliqCli.Tests/
```

---

## Open Questions / Further Considerations

1. **Keychain library choice** — Evaluate whether P/Invoke per-platform is worth it vs adopting `git-credential-manager`'s keyring abstractions as a NuGet. The latter is battle-tested but adds a transitive dependency.
2. **Pex protocol details** — Before the Pex epic, capture the exact message format, connection lifecycle, and server handshake from Cliq internal specs. This is a non-standard protocol — no design work until fully documented.
3. **WebSocket session state** — Named WS sessions (`--name`) imply a running daemon or persisted connection ID. Decide upfront: long-lived background process vs ephemeral connect-send-close per invocation.
4. **AI Skill packaging** — The binary is the delivery unit for both GitHub Copilot CLI Skills and Claude Agent Skills:

   **GitHub Copilot CLI Skill (`awesome-copilot/skills/cliq-cli/SKILL.md`)**
   - Follow the [Agent Skills specification](https://agentskills.io/specification).
   - `name`: `cliq-cli`; `description`: wraps the purpose and all command groups.
   - List all command groups and their flags in the instructions body so Copilot knows what to invoke.
   - Bundle the pre-built binary (or a shell wrapper that locates it on `PATH`) as a skill asset.
   - The skill must NOT expose token values — all auth is handled by the binary itself.
   - Expected usage pattern: Copilot calls `cliq-cli <group> <subcommand> [flags]` as a subprocess and parses the stdout JSON.

   **Claude Agent Skill**
   - Analogous manifest structure; maps each command group to a tool definition with the binary invocation as the tool action.
   - Auth setup is a one-time prerequisite: agent must call `cliq-cli account add` before any API commands.

   **Installation prerequisite note (in skill instructions):**
   > Before using this skill, install `cliq-cli`:
   > - macOS/Linux: `dotnet tool install -g cliqcli` (or download the release binary and add to PATH)
   > - Windows: same via `winget` or direct binary download
5. **datacenter auto-detection** — When adding an account, consider auto-detecting the datacenter from the token/user info API response instead of requiring manual `--domain` input.
6. **blocked-domain list extensibility** — The current design blocks all accounts whose email domain's first label is `zohocorp` (covering `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, etc.). Decide whether the list of blocked org-domain labels should remain a single sealed constant (`["zohocorp"]`) or be extended to cover other internal org domains in future, via a compile-time list that is still not overridable at runtime by end users.

# 4. CLI Structure & Commands

## Root

```
cliq-cli [global-flags] <group> <subcommand> [flags]
```

## Global Flags

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--account` | string | active account | Override account for this invocation |
| `--json` | bool | `true` | Force JSON output to stdout |
| `--no-input` | bool | `false` | Never prompt; fail instead |
| `--help` | — | — | Show help for current command/group |
| `--version` | — | — | Print version and exit |

---

## Group: `account`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--name` (req), `--token` (req), `--domain` (default: `zoho.com`) | Add a PAT-backed account |
| `list` | — | List all configured accounts (token masked) |
| `remove` | `--name` (req) | Remove account + clear keychain secret |
| `show` | `--name` (req) | Show account details (token masked as `***`) |
| `set-default` | `--name` (req) | Set active/default account |
| `re-auth` | `--name` (req) | Re-authenticate after scope change *(OAuth — v2)* |

---

## Group: `scope`

All scope subcommands target an account. `--account` defaults to the active account if omitted.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--scope` (req), `[--account]` | Add an OAuth scope to an account |
| `remove` | `--scope` (req), `[--account]` | Remove an OAuth scope from an account |
| `list` | `[--account]` | List all scopes for an account |

Adding or removing a scope sets `needs_reauth = true` on that account.

---

## Group: `api`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `call` | `--method` (req), `--path` (req), `[--body]`, `[--body-file]`, `[--header]` (repeatable), `[--query]` (repeatable), `[--account]` | Invoke a Cliq REST API endpoint |

### `api call` Flag Details

| Flag | Type | Description |
|------|------|-------------|
| `--method` | `GET\|POST\|PUT\|PATCH\|DELETE` | HTTP method |
| `--path` | string | URL path, e.g. `/api/v2/channels` |
| `--body` | string | Inline JSON request body |
| `--body-file` | string | Path to a JSON file to use as request body |
| `--header` | `key:value` | Additional request header (repeatable) |
| `--query` | `key=value` | Query string parameter (repeatable) |
| `--account` | string | Account override for this call |

`--body` and `--body-file` are mutually exclusive. `/api/v2` prefix is inserted automatically if `--path` does not start with `/api/`.

---

## Group: `ws` *(Future)*

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `connect` | `--url` (req), `--name` (req), `[--account]` | Open a named WebSocket connection |
| `send` | `--name` (req), `--message` (req) | Send a message |
| `listen` | `--name` (req) | Stream incoming messages to stdout as JSON lines |
| `close` | `--name` (req) | Close the connection |

---

## Group: `pex` *(Future)*

> Pex is Zoho Cliq's proprietary real-time protocol (ping-pong + chat message delivery).

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `connect` | `--channel` (req), `[--account]` | Open a Pex session |
| `send` | `--session` (req), `--message` (req) | Send a message over Pex |
| `listen` | `--session` (req) | Stream incoming Pex messages to stdout as JSON lines |
| `close` | `--session` (req) | Close the Pex session |

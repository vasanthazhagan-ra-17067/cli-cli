# 9. Output Contract

All output goes through a central `IOutputWriter` interface so tests can capture it without console side effects.

## stdout — Success

```json
{ "status": "ok", "data": <raw API response or command result> }
```

### `account list` Example

```json
{
  "status": "ok",
  "data": [
    { "name": "work", "domain": "zoho.com", "token_type": "pat", "is_default": true, "needs_reauth": false, "scope_count": 2 },
    { "name": "personal", "domain": "zoho.eu", "token_type": "pat", "is_default": false, "needs_reauth": false, "scope_count": 0 }
  ]
}
```

Token value is **never** included in any output. `account show` displays `"token": "***"`.

---

## stderr — Error Envelope

```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <0|1|2> }
```

---

## Error Codes

| Code | Exit | Meaning |
|------|------|---------|
| `ACCOUNT_NOT_FOUND` | 1 | Named account does not exist |
| `ACCOUNT_ALREADY_EXISTS` | 1 | `account add` with duplicate name |
| `NO_DEFAULT_ACCOUNT` | 1 | No active account set and `--account` not provided |
| `AUTH_FAILURE` | 2 | Keychain read failed or token rejected by API |
| `NEEDS_REAUTH` | 2 | Account has pending scope changes |
| `API_ERROR` | 1 | Non-2xx response from Cliq API |
| `INVALID_ARGS` | 1 | Missing or conflicting flags |
| `IO_ERROR` | 1 | File system failure (accounts.json read/write) |
| `KEYCHAIN_ERROR` | 2 | OS keychain operation failed |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | General / recoverable error |
| `2` | Auth failure / needs-reauth |

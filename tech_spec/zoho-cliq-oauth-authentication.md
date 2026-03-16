# Zoho Cliq API v2 — OAuth 2.0 Authentication Reference

> **Status:** Reference Document  
> **Applies to:** `cliq-cli` v2+ OAuth implementation  
> **Source:** [Zoho Cliq REST API v2 — Authentication](https://www.zoho.com/cliq/help/restapi/v2/#authentication)  
> **Date:** 2026-03-16

---

## Table of Contents

1. [Overview](#1-overview)
2. [Prerequisites — Registering Your Application](#2-prerequisites--registering-your-application)
3. [OAuth 2.0 Grant Types](#3-oauth-20-grant-types)
4. [Authorization Code Grant (Web Server Flow)](#4-authorization-code-grant-web-server-flow)
5. [Self Client Grant (Terminal / Headless Flow)](#5-self-client-grant-terminal--headless-flow)
6. [Client Credentials Grant (Server-Based / Service Account Flow)](#6-client-credentials-grant-server-based--service-account-flow)
7. [Device Authorization Grant (CLI / Headless Flow)](#7-device-authorization-grant-cli--headless-flow)
8. [Token Endpoints Reference](#8-token-endpoints-reference)
9. [Refreshing an Access Token](#9-refreshing-an-access-token)
10. [Revoking a Token](#10-revoking-a-token)
11. [Using the Access Token in API Calls](#11-using-the-access-token-in-api-calls)
12. [Zoho Cliq API Scopes](#12-zoho-cliq-api-scopes)
13. [Multi-Datacenter Support](#13-multi-datacenter-support)
14. [Token Lifecycle Summary](#14-token-lifecycle-summary)
15. [Error Responses](#15-error-responses)
16. [cliq-cli Integration Notes (v2)](#16-cliq-cli-integration-notes-v2)

---

## 1. Overview

The Zoho Cliq REST API v2 uses **OAuth 2.0** as its authentication and authorisation framework. Every API request must carry a valid OAuth access token. The token is passed as a custom `Authorization` header:

```
Authorization: Zoho-oauthtoken <access_token>
```

### Key characteristics

| Property | Value |
|----------|-------|
| Protocol | OAuth 2.0 (RFC 6749) |
| Header format | `Authorization: Zoho-oauthtoken <access_token>` |
| Access token TTL | 3600 seconds (1 hour) by default |
| Refresh token TTL | Long-lived (until revoked) |
| Token authority | `https://accounts.zoho.<tld>/oauth/v2/` |
| API base URL | `https://cliq.zoho.<tld>/api/v2/` |

> **Note for cliq-cli v1:** The v1 implementation uses a **Personal Access Token (PAT)** which uses the same `Authorization: Zoho-oauthtoken <token>` header format. The PAT does not expire and does not require a token-refresh mechanism. The v2 OAuth implementation described in this document extends v1 by adding token exchange, refresh, and scope enforcement.

---

## 2. Prerequisites — Registering Your Application

Before implementing OAuth authentication you must register an application in the **Zoho API Console** to obtain a `client_id` and `client_secret`.

### Steps

1. Navigate to [https://api-console.zoho.com](https://api-console.zoho.com) (use the datacenter-appropriate URL for your region — see [§12 Multi-Datacenter Support](#12-multi-datacenter-support)).
2. Click **Add Client**.
3. Choose the client type appropriate for your use-case (see [§3 OAuth 2.0 Grant Types](#3-oauth-20-grant-types) for guidance).
4. Fill in application details:
   - **Client Name** — a human-readable name (e.g. `cliq-cli`)
   - **Homepage URL** — your application URL (can be any valid URL for non-web clients)
   - **Authorized Redirect URIs** — required for Authorization Code Grant; use `http://localhost` or `urn:ietf:wg:oauth:2.0:oob` for native/CLI clients
5. Click **Create**.
6. Copy the **Client ID** and **Client Secret** — these are the credentials used by `cliq-cli account add --client-id <id> --client-secret <secret>` in v2.

### Client types in the Zoho API Console

| Client Type | OAuth Flow | CLI/Headless Suitable |
|-------------|-----------|----------------------|
| Web Based | Authorization Code Grant | ❌ Requires browser redirect |
| Self Client | Authorization Code via terminal | ✅ No browser/redirect listener needed |
| Server-Based | Client Credentials | ✅ Machine-to-machine |
| Mobile / Desktop | Authorization Code + PKCE | ⚠️ Requires local redirect listener |

> **Recommendation for `cliq-cli`:** Use the **Self Client** type for developer workflows (produces a code that can be entered in the terminal) or the **Server-Based** type when a service account with pre-approved scopes is available.

---

## 3. OAuth 2.0 Grant Types

Zoho supports several OAuth 2.0 grant types. The table below summarises their suitability for `cliq-cli`:

| Grant Type | RFC | Browser Required | Local HTTP Listener | Recommended for cliq-cli |
|------------|-----|-----------------|---------------------|--------------------------|
| Authorization Code | RFC 6749 §4.1 | ✅ Yes | ✅ Yes | ❌ Not suitable — v1 non-goal |
| Authorization Code + PKCE | RFC 7636 | ✅ Yes | ✅ Yes | ❌ Not suitable |
| Self Client (Zoho-specific) | — | ❌ No | ❌ No | ✅ **Recommended for v2** |
| Client Credentials | RFC 6749 §4.4 | ❌ No | ❌ No | ✅ Suitable for service accounts |
| Device Authorization | RFC 8628 | ✅ User visits URL on any device | ❌ No | ✅ Suitable for headless AI agents |
| Refresh Token | RFC 6749 §6 | ❌ No | ❌ No | ✅ Required for token renewal |

> **`cliq-cli` v2 plans to implement:** Self Client grant and Client Credentials, calling Zoho OAuth endpoints directly from the terminal process. No browser window, redirect URL, or local HTTP listener is required.

---

## 4. Authorization Code Grant (Web Server Flow)

> **Applicability for cliq-cli:** ❌ Not suitable. Documented here for completeness. cliq-cli explicitly does **not** support browser-redirect flows (see PRD §2.3 Non-Goals).

### Step 1 — Build the Authorization URL

Direct the user's browser to:

```
https://accounts.zoho.com/oauth/v2/auth
  ?response_type=code
  &client_id=<CLIENT_ID>
  &scope=<SCOPES>
  &redirect_uri=<REDIRECT_URI>
  &access_type=offline
  &state=<RANDOM_STATE>
  &prompt=consent
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `response_type` | ✅ | Always `code` |
| `client_id` | ✅ | Your application's Client ID |
| `scope` | ✅ | Space-separated list of OAuth scopes (see [§11](#11-zoho-cliq-api-scopes)) |
| `redirect_uri` | ✅ | Must match the URI registered in the API Console |
| `access_type` | ✅ | `offline` to receive a refresh token; `online` for access token only |
| `state` | Recommended | Random value to prevent CSRF; verified in the callback |
| `prompt` | Optional | `consent` forces the consent screen even if already approved |

### Step 2 — Exchange the Authorization Code for Tokens

After the user approves, Zoho redirects to `<redirect_uri>?code=<AUTH_CODE>&state=<STATE>`.

Make a `POST` request to the token endpoint:

```
POST https://accounts.zoho.com/oauth/v2/token
Content-Type: application/x-www-form-urlencoded

code=<AUTH_CODE>
&client_id=<CLIENT_ID>
&client_secret=<CLIENT_SECRET>
&redirect_uri=<REDIRECT_URI>
&grant_type=authorization_code
```

**Success response (HTTP 200):**

```json
{
  "access_token": "1000.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "refresh_token": "1000.yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "ZohoCliq.Channels.READ ZohoCliq.Messages.WRITE"
}
```

---

## 5. Self Client Grant (Terminal / Headless Flow)

> **Applicability for cliq-cli:** ✅ **Recommended for v2 developer workflows.** No browser automation or local redirect listener required.

The Self Client type is a Zoho-specific extension of the Authorization Code flow that allows a developer to generate a short-lived authorization code directly in the Zoho API Console and paste it into their application. This is the most practical flow for CLI tools used by developers.

### Step 1 — Generate a Code in the API Console

1. Go to [https://api-console.zoho.com](https://api-console.zoho.com).
2. Select your **Self Client** application.
3. Click **Generate Code**.
4. Enter the required **Scopes** and an optional **Time Duration** (default 3 minutes).
5. Copy the generated **Authorization Code**.

### Step 2 — Exchange the Code for Tokens

```
POST https://accounts.zoho.com/oauth/v2/token
Content-Type: application/x-www-form-urlencoded

code=<AUTH_CODE>
&client_id=<CLIENT_ID>
&client_secret=<CLIENT_SECRET>
&redirect_uri=<REDIRECT_URI>
&grant_type=authorization_code
```

> **Note:** The `redirect_uri` must match the value registered in the console. For Self Client, use the same redirect URI configured (typically `http://localhost` or `urn:ietf:wg:oauth:2.0:oob`).

**Success response (HTTP 200):**

```json
{
  "access_token": "1000.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "refresh_token": "1000.yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy",
  "token_type": "Bearer",
  "expires_in": 3600,
  "api_domain": "https://www.zohoapis.com"
}
```

### cliq-cli v2 command mapping

```sh
# Developer generates a code in the API Console and provides it here:
cliq-cli account add \
  --name "work" \
  --client-id "1000.AAABBBCCC" \
  --client-secret "xxxxxxxxxxxxxxxx" \
  --code "<auth_code_from_console>"
```

The CLI performs the token exchange in-process (POST to the token endpoint) and stores:
- `access_token` → OS keychain under `cliq-cli:<accountName>:access_token`
- `refresh_token` → OS keychain under `cliq-cli:<accountName>:refresh_token`
- `expires_at` → computed from `expires_in`, stored in `accounts.json`

---

## 6. Client Credentials Grant (Server-Based / Service Account Flow)

> **Applicability for cliq-cli:** ✅ Suitable for service accounts and machine-to-machine integrations where no user interaction is possible.

The Client Credentials grant (RFC 6749 §4.4) allows a server-side application to authenticate directly using its own `client_id` and `client_secret`, without any user authorisation step. This flow is appropriate when the `cliq-cli` instance acts as an automated service account (e.g. a bot or CI pipeline identity) rather than on behalf of an individual user.

> **Prerequisite:** Create a **Server-Based** client in the Zoho API Console (see [§2](#2-prerequisites--registering-your-application)). The scopes must be pre-approved by a Zoho organisation administrator before the client can use this grant.

### Request

```
POST https://accounts.zoho.com/oauth/v2/token
Content-Type: application/x-www-form-urlencoded

client_id=<CLIENT_ID>
&client_secret=<CLIENT_SECRET>
&scope=<SCOPES>
&grant_type=client_credentials
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `client_id` | ✅ | Your application's Client ID |
| `client_secret` | ✅ | Your application's Client Secret |
| `scope` | ✅ | Space-separated list of OAuth scopes |
| `grant_type` | ✅ | Always `client_credentials` |

### Success Response (HTTP 200)

```json
{
  "access_token": "1000.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "ZohoCliq.Channels.READ ZohoCliq.Messages.WRITE"
}
```

> **Important:** The Client Credentials grant does **not** return a `refresh_token`. A new access token must be requested using another `client_credentials` grant when the current one expires. `OAuthProvider` handles this automatically in v2 by re-issuing the grant when `expires_at - now < 60s`.

### cliq-cli v2 command mapping

```sh
# Service account — no user code or browser required:
cliq-cli account add \
  --name "service-bot" \
  --client-id "1000.AAABBBCCC" \
  --client-secret "xxxxxxxxxxxxxxxx" \
  --grant-type client_credentials \
  --scope "ZohoCliq.Channels.READ ZohoCliq.Messages.WRITE"
```

---

## 7. Device Authorization Grant (CLI / Headless Flow)

> **Applicability for cliq-cli:** ✅ Suitable for AI agent / headless environments where the user can visit a URL on a separate device.

The Device Authorization Grant (RFC 8628) is designed for input-constrained devices and headless environments. The device displays a URL and a user code; the user visits the URL on any device (phone, laptop browser) and approves the request.

### Step 1 — Request a Device Code

```
POST https://accounts.zoho.com/oauth/v2/device/code
Content-Type: application/x-www-form-urlencoded

client_id=<CLIENT_ID>
&scope=<SCOPES>
&response_type=device_code
```

**Success response (HTTP 200):**

```json
{
  "device_code": "1000.ddddddddddddddddd",
  "user_code": "ABCD-1234",
  "verification_url": "https://accounts.zoho.com/device",
  "expires_in": 900,
  "interval": 5
}
```

| Field | Description |
|-------|-------------|
| `device_code` | Opaque code used to poll for the token |
| `user_code` | Human-readable code displayed to the user |
| `verification_url` | URL the user visits to approve the request |
| `expires_in` | Seconds until the device code expires (typically 900s / 15 min) |
| `interval` | Minimum polling interval in seconds (typically 5s) |

### Step 2 — Display Instructions to the User

The CLI should display:

```
Please visit https://accounts.zoho.com/device and enter the code: ABCD-1234
Waiting for authorisation...
```

### Step 3 — Poll for the Access Token

While the user completes approval, poll the token endpoint at the specified `interval`:

```
POST https://accounts.zoho.com/oauth/v2/token
Content-Type: application/x-www-form-urlencoded

client_id=<CLIENT_ID>
&device_code=<DEVICE_CODE>
&grant_type=urn:ietf:params:oauth:grant-type:device_code
```

| Poll Response | Status | Action |
|---------------|--------|--------|
| `authorization_pending` | 400 | Continue polling |
| `slow_down` | 400 | Increase polling interval by 5s |
| `expired_token` | 400 | Display error; request new device code |
| `access_denied` | 400 | User denied — display error |
| `{ "access_token": … }` | 200 | Success — store tokens |

---

## 8. Token Endpoints Reference

All token operations use `Content-Type: application/x-www-form-urlencoded`.

### Authorization Endpoint (GET)

```
https://accounts.zoho.<tld>/oauth/v2/auth
```

### Token Endpoint (POST)

```
https://accounts.zoho.<tld>/oauth/v2/token
```

### Token Revocation Endpoint (POST)

```
https://accounts.zoho.<tld>/oauth/v2/token/revoke
```

### Device Code Endpoint (POST)

```
https://accounts.zoho.<tld>/oauth/v2/device/code
```

Replace `<tld>` with the datacenter-appropriate value (see [§12](#12-multi-datacenter-support)).

---

## 9. Refreshing an Access Token

Access tokens expire after **3600 seconds** (1 hour). Use the refresh token to obtain a new access token without requiring user interaction.

### Request

```
POST https://accounts.zoho.com/oauth/v2/token
Content-Type: application/x-www-form-urlencoded

refresh_token=<REFRESH_TOKEN>
&client_id=<CLIENT_ID>
&client_secret=<CLIENT_SECRET>
&grant_type=refresh_token
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `refresh_token` | ✅ | The refresh token from the original token exchange |
| `client_id` | ✅ | Your application's Client ID |
| `client_secret` | ✅ | Your application's Client Secret |
| `grant_type` | ✅ | Always `refresh_token` |

### Success Response (HTTP 200)

```json
{
  "access_token": "1000.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

> **Important:** A refresh response does **not** include a new `refresh_token`. The original refresh token remains valid until it is revoked.

### Automatic Refresh in cliq-cli

When OAuth 2.0 is implemented in v2, `OAuthProvider.GetTokenAsync` will:

1. Read the stored `access_token` and `expires_at` from the keychain.
2. If `expires_at - now < 60s`, proactively refresh before returning.
3. On an `HTTP 401` response from the Cliq API, attempt one refresh and retry.
4. If refresh also fails, set `AccountEntry.NeedsReauth = true` and return an `AUTH_FAILURE` error.

---

## 10. Revoking a Token

To invalidate an access or refresh token (e.g. on `account remove`):

```
POST https://accounts.zoho.com/oauth/v2/token/revoke
Content-Type: application/x-www-form-urlencoded

token=<ACCESS_TOKEN_OR_REFRESH_TOKEN>
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `token` | ✅ | The access token or refresh token to revoke |

**Success response:** HTTP 200 with an empty body or `{}`.

> **cliq-cli integration:** `account remove --name <name>` should revoke the stored `refresh_token` (preferred) before deleting keychain entries. If revocation fails (e.g. network error), the tokens are still removed from the local keychain and the account is deleted — revocation failure is non-fatal.

---

## 11. Using the Access Token in API Calls

Every request to the Zoho Cliq REST API v2 must include an `Authorization` header with the OAuth access token (or PAT):

```
Authorization: Zoho-oauthtoken <access_token>
```

### Example API Call

```
GET https://cliq.zoho.com/api/v2/channels
Authorization: Zoho-oauthtoken 1000.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Accept: application/json
```

**Success response:**

```json
{
  "channels": [
    { "id": "channel123", "name": "General", "type": "open" }
  ]
}
```

### Error on Invalid or Expired Token

```
HTTP/1.1 401 Unauthorized
Content-Type: application/json

{
  "error": "OAUTH_SCOPE_MISMATCH",
  "message": "The access token provided does not match the required scope"
}
```

Common 401 error codes:

| Error Code | Cause |
|------------|-------|
| `INVALID_OAUTHTOKEN` | Token is malformed or does not exist |
| `OAUTH_TOKEN_EXPIRED` | Access token has expired; refresh and retry |
| `OAUTH_SCOPE_MISMATCH` | Token lacks the scope required for this endpoint |
| `INVALID_CLIENT` | Client ID or secret is incorrect |

---

## 12. Zoho Cliq API Scopes

Scopes are space-separated strings provided when requesting authorisation. Request only the scopes your application needs (principle of least privilege).

### Channel Scopes

| Scope | Permission |
|-------|-----------|
| `ZohoCliq.Channels.READ` | Read channel metadata and messages |
| `ZohoCliq.Channels.WRITE` | Create channels, update channel settings |
| `ZohoCliq.Channels.ALL` | Full access to channels (read + write) |

### Message Scopes

| Scope | Permission |
|-------|-----------|
| `ZohoCliq.Messages.READ` | Read messages in channels and direct chats |
| `ZohoCliq.Messages.WRITE` | Post, edit, and delete messages |
| `ZohoCliq.Messages.ALL` | Full access to messages (read + write) |

### Conversation Scopes

| Scope | Permission |
|-------|-----------|
| `ZohoCliq.Conversations.READ` | Read direct messages and group conversations |
| `ZohoCliq.Conversations.WRITE` | Send direct messages |
| `ZohoCliq.Conversations.ALL` | Full access to conversations |

### Bot and Webhook Scopes

| Scope | Permission |
|-------|-----------|
| `ZohoCliq.Bots.READ` | Read bot configurations |
| `ZohoCliq.Bots.WRITE` | Create and manage bots |
| `ZohoCliq.Webhooks.READ` | Read incoming webhook configurations |
| `ZohoCliq.Webhooks.WRITE` | Create and manage webhooks |

### User and Team Scopes

| Scope | Permission |
|-------|-----------|
| `ZohoCliq.Teams.READ` | Read team/organisation information |
| `ZohoCliq.UserProfile.READ` | Read the authenticated user's profile (used at `account add` to fetch email) |

### Wildcard Scope

| Scope | Permission |
|-------|-----------|
| `ZohoCliq.FullAccess.all` | Full access to all Cliq resources (equivalent to all scopes above) |

> **Security note for cliq-cli:** The `scope add / scope remove` commands record scope metadata per account in `accounts.json`. When OAuth is implemented in v2, `account re-auth` will use the stored scope list to request a new token with the updated scopes. Changing scopes sets `needs_reauth = true` on the account.

---

## 13. Multi-Datacenter Support

Zoho operates separate OAuth servers and API endpoints for each datacenter region. The domain used must match the account's datacenter.

| Region | Account Domain | OAuth Base URL | Cliq API Base URL |
|--------|---------------|----------------|-------------------|
| United States | `zoho.com` | `https://accounts.zoho.com/oauth/v2/` | `https://cliq.zoho.com/api/v2/` |
| Europe | `zoho.eu` | `https://accounts.zoho.eu/oauth/v2/` | `https://cliq.zoho.eu/api/v2/` |
| India | `zoho.in` | `https://accounts.zoho.in/oauth/v2/` | `https://cliq.zoho.in/api/v2/` |
| Australia | `zoho.com.au` | `https://accounts.zoho.com.au/oauth/v2/` | `https://cliq.zoho.com.au/api/v2/` |
| Japan | `zoho.jp` | `https://accounts.zoho.jp/oauth/v2/` | `https://cliq.zoho.jp/api/v2/` |
| Canada | `zohocloud.ca` | `https://accounts.zohocloud.ca/oauth/v2/` | `https://cliq.zohocloud.ca/api/v2/` |

### URL Construction in cliq-cli

The `AccountEntry.Domain` field (e.g. `"zoho.com"`) is used to construct the OAuth and API base URLs:

```csharp
// OAuth endpoint
var tokenEndpoint = $"https://accounts.{account.Domain}/oauth/v2/token";

// Cliq API base
var apiBase = $"https://cliq.{account.Domain}/api/v2";
```

> **API Console URL:** Also datacenter-specific — use `https://api-console.zoho.<tld>` matching the account's region.

---

## 14. Token Lifecycle Summary

```
[Developer] → Registers app in API Console
                       ↓
          Obtains client_id + client_secret
                       ↓
       cliq-cli account add --client-id ... --client-secret ... --code ...
                       ↓
      POST /oauth/v2/token  (authorization_code grant)
                       ↓
      ┌─────────────────────────────────────────────┐
      │  access_token  (TTL: 3600s)                 │
      │  refresh_token (TTL: long-lived)             │
      │  scope         (space-separated)             │
      └─────────────────────────────────────────────┘
                       ↓
        Stored in OS keychain by OAuthProvider
                       ↓
       [API call] → ApiClient reads access_token
                       ↓
             Authorization: Zoho-oauthtoken <token>
                       ↓
          ┌──────────────────────────────┐
          │  Token valid → 200 OK        │
          │  Token expired → 401         │
          │     → refresh_token exchange │
          │     → new access_token       │
          │     → retry request          │
          └──────────────────────────────┘
                       ↓
       [account remove] → revoke refresh_token
                        → delete keychain entries
                        → remove from accounts.json
```

---

## 15. Error Responses

### Token Endpoint Errors (HTTP 400)

```json
{
  "error": "<error_code>",
  "error_description": "Human-readable error description"
}
```

| `error` | Cause |
|---------|-------|
| `invalid_code` | The authorization code is invalid, expired, or already used |
| `invalid_client` | Client ID or client secret is incorrect |
| `invalid_grant` | Refresh token is invalid or revoked |
| `invalid_scope` | One or more requested scopes are invalid or not permitted |
| `authorization_pending` | (Device grant) User has not yet approved; continue polling |
| `slow_down` | (Device grant) Polling too fast; increase interval |
| `expired_token` | (Device grant) Device code has expired |
| `access_denied` | (Device grant) User denied the request |

### cliq-cli Error Code Mapping

| OAuth Error | cliq-cli Exit Code | Structured Error Code |
|-------------|-------------------|-----------------------|
| `invalid_grant` | 2 | `AUTH_FAILURE` |
| `invalid_client` | 2 | `AUTH_FAILURE` |
| `OAUTH_TOKEN_EXPIRED` (API 401) | 2 | `AUTH_FAILURE` |
| `OAUTH_SCOPE_MISMATCH` (API 401) | 2 | `AUTH_SCOPE_MISMATCH` |
| `needs_reauth == true` | 2 | `NEEDS_REAUTH` |
| Revocation failure (non-fatal) | — | warning in stderr only |

---

## 16. cliq-cli Integration Notes (v2)

This section maps the OAuth flows above to concrete `cliq-cli` v2 implementation requirements.

### Interface

```csharp
// OAuthProvider — v2 implementation of IAuthProvider
public sealed class OAuthProvider : IAuthProvider
{
    // GetTokenAsync: reads access_token from keychain;
    //   if expires_at - now < 60s → calls the private RefreshTokenAsync helper first
    Task<string> GetTokenAsync(string accountName, CancellationToken ct);

    // StoreTokenAsync: stores access_token + refresh_token + expires_at
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct);

    // ClearTokenAsync: revokes refresh_token then deletes keychain entries
    Task ClearTokenAsync(string accountName, CancellationToken ct);

    // RefreshTokenAsync: private helper — POSTs to /oauth/v2/token with grant_type=refresh_token;
    //   on success updates the stored access_token + expires_at in the keychain.
    // private Task<string> RefreshTokenAsync(string accountName, CancellationToken ct);
}
```

### New `account add` flags (v2)

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `--client-id` | string | ✅ (OAuth) | OAuth application Client ID |
| `--client-secret` | string | ✅ (OAuth) | OAuth application Client Secret |
| `--code` | string | ✅ (Self Client) | Authorization code from the API Console |
| `--token` | string | ✅ (PAT) | Personal Access Token (v1 — kept for backwards compat) |
| `--domain` | string | optional | Datacenter domain (default: `zoho.com`) |

### New `account re-auth` behaviour (v2)

```
cliq-cli account re-auth --name "work"
  1. Read AccountEntry for "work" — verify TokenType == "oauth"
  2. Prompt user for a new authorization code (Self Client flow)
     or re-run the device flow if TokenType == "device"
  3. Exchange code for new access_token + refresh_token
  4. Call OAuthProvider.StoreTokenAsync("work", access_token, ...)
  5. Also store new refresh_token under "cliq-cli:work:refresh_token"
  6. Set AccountEntry.NeedsReauth = false
  7. Write accounts.json
  8. Output: { "status": "ok", "data": { "name": "work", "reauthed": true } }
```

### Keychain Key Scheme (v2)

| Key | Value stored |
|-----|-------------|
| `cliq-cli:<accountName>:pat` | PAT value (v1) |
| `cliq-cli:<accountName>:access_token` | OAuth access token (v2) |
| `cliq-cli:<accountName>:refresh_token` | OAuth refresh token (v2) |
| `cliq-cli:<accountName>:client_secret` | OAuth client secret (v2) |

### `accounts.json` schema additions (v2)

```json
{
  "accounts": [
    {
      "name": "work",
      "domain": "zoho.com",
      "email": "user@example.com",
      "scopes": ["ZohoCliq.Channels.READ", "ZohoCliq.Messages.WRITE"],
      "token_type": "oauth",
      "client_id": "1000.AAABBBCCC",
      "expires_at": "2026-03-16T10:18:57Z",
      "is_default": true,
      "needs_reauth": false
    }
  ]
}
```

> **Security note:** `client_secret`, `access_token`, and `refresh_token` are **never** written to `accounts.json`. They are stored exclusively in the OS keychain. `client_id` and `expires_at` are non-sensitive and stored in `accounts.json` for operational purposes.

### Related Documents

- [ADR-0005: PAT-First Authentication with Pluggable IAuthProvider Interface](adr/adr-0005-pat-first-auth-pluggable-iauthprovider.md)
- [ADR-0003: OS-Native Keychain with AES-256-GCM Encrypted File Fallback](adr/adr-0003-os-keychain-credential-storage.md)
- [tech_spec.md — §6 Authentication](tech_spec.md#6-authentication)
- [Zoho Accounts OAuth2 Protocol Reference](https://www.zoho.com/accounts/protocol/oauth.html)
- [Zoho Cliq REST API v2 — Authentication](https://www.zoho.com/cliq/help/restapi/v2/#authentication)
- [Zoho API Console](https://api-console.zoho.com)

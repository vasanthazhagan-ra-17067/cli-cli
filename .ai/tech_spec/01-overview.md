# 1. Project Overview

**cliq-cli** is a standalone, multi-platform CLI binary for interacting with Zoho Cliq APIs. It manages multiple Zoho accounts, handles authentication via a pluggable `IAuthProvider` interface, and exposes a general-purpose HTTP API invoker.

**Primary consumer:** AI Agents via GitHub Copilot CLI Skills (`.github/skills/cliq-cli/SKILL.md`).

**Scope:**
- v1: Account management, scope management, REST API invocation (PAT auth)
- Future: OAuth2 re-auth, WebSocket connections, Pex real-time chat

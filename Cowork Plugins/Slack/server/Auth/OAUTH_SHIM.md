# OAuth Shim — Setup

The shim translates between Cowork Plugin Vault's standard OAuth 2.0 flow and
Slack v2's bot/user scope split, so the MCP receives an `xoxp-*` user token
instead of the `xoxb-*` bot token Slack defaults to.

## Architecture

```
Cowork ──/oauth/authorize──▶ SHIM ──/oauth/v2/authorize?scope=&user_scope=──▶ Slack
                                                                                 │
                                                                                 ▼
Cowork ◀─redirect─────────── SHIM ◀─/oauth/callback?code───────────────────── Slack
                                │
                                ├─ POST /api/oauth.v2.access ─▶ Slack (server-to-server)
                                └─ stores authed_user.access_token (xoxp-*) under a shim code

Cowork ──POST /oauth/token──▶ SHIM ──returns { access_token: "xoxp-*" }
```

Endpoints (all on the Container App's public FQDN):

| Path | Verb | Caller | Purpose |
| --- | --- | --- | --- |
| `/oauth/authorize` | GET | User browser (from Cowork) | Redirects to Slack with `scope` + `user_scope` split correctly |
| `/oauth/callback` | GET | User browser (from Slack) | Server-side code exchange with Slack; mints shim code; redirects to Cowork |
| `/oauth/token` | POST | Cowork backend | Returns the `xoxp-*` user token as a standard OAuth bearer |

## Configuration

Set these `azd` env vars before `azd up` / `azd deploy`:

```pwsh
# Slack app
azd env set SLACK_CLIENT_ID "1094...11159..."
azd env set SLACK_CLIENT_SECRET "<rotate-in-keyvault-after-setup>"

# Cowork-facing client (any value you choose; paste the same pair into Teams Dev Portal)
azd env set COWORK_CLIENT_ID "slack-cowork-shim"
azd env set COWORK_CLIENT_SECRET "<random-strong-secret>"

# Optional overrides
# azd env set SLACK_USER_SCOPES "chat:write,channels:read,..."
# azd env set SLACK_BOT_SCOPES  ""
```

The scope lists default to the 26 user scopes the MCP relies on; override only
if you need to add/remove. Bot scopes default to empty (user-token-only).

## Slack app — Redirect URL

In Slack app config → **OAuth & Permissions** → **Redirect URLs**, add:

```
https://<container-app-fqdn>/oauth/callback
```

The FQDN is the `AZURE_CONTAINER_APP_FQDN` azd output.

## Teams Developer Portal — OAuth client registration

In dev.teams.microsoft.com → **Tools** → **OAuth client registration**:

| Field | Value |
| --- | --- |
| Registration name | `Slack Cowork Shim` (any label) |
| Base URL | `https://<container-app-fqdn>` |
| Client ID | `slack-cowork-shim` (must match `COWORK_CLIENT_ID`) |
| Client Secret | matches `COWORK_CLIENT_SECRET` |
| Authorization endpoint | `https://<container-app-fqdn>/oauth/authorize` |
| Token endpoint | `https://<container-app-fqdn>/oauth/token` |
| Refresh endpoint | (leave blank — user tokens are long-lived) |
| Scope | leave blank or `placeholder` (the shim ignores it; real scopes come from env vars) |
| PKCE | On |

Use the `referenceId` Dev Portal produces in the manifest's
`authorization.oAuthPluginVault.referenceId` (base64 of
`{tenantId}##{vaultEntryId}`).

## Flow verification

```pwsh
# 1. Open the authorize endpoint in a browser; should bounce to Slack
Start-Process "https://<fqdn>/oauth/authorize?response_type=code&client_id=slack-cowork-shim&redirect_uri=https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect&state=test&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"

# 2. After Slack consent, the browser returns to teams.microsoft.com with ?code=...&state=test

# 3. Inspect a /oauth/token call (replace CODE and VERIFIER):
$body = @{
  grant_type    = "authorization_code"
  code          = "CODE"
  redirect_uri  = "https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect"
  client_id     = "slack-cowork-shim"
  client_secret = "<secret>"
  code_verifier = "VERIFIER"
}
Invoke-RestMethod -Method Post -Uri "https://<fqdn>/oauth/token" -Body $body
# Expect: { access_token = "xoxp-...", token_type = "Bearer", scope = "..." }
```

## Limitations

- **In-memory state store**: codes and pending auth state live in process
  memory. Single replica or sticky routing recommended; restart clears
  in-flight authorizations. Acceptable because OAuth codes are consumed
  within seconds.
- **No refresh-token support**: Slack user tokens are long-lived by default.
  If your Slack app has token rotation enabled, add `grant_type=refresh_token`
  to `/oauth/token` and call `oauth.v2.access` with the refresh token.
- **PKCE optional, enforced when sent**: if Cowork sends `code_challenge`,
  the shim validates `code_verifier` at `/oauth/token`. If Cowork doesn't,
  client secret check applies.

# Salesforce Data 360

Cowork / Copilot plugin for the Salesforce hosted `data360` MCP server (`https://api.salesforce.com/platform/mcp/v1/data/data360`). Three meta-tools (`search`, `payload_examples`, `execute`) discover and run Data 360 Connect API tools at runtime.

| Component | Path |
| --- | --- |
| Connector `salesforce-data-360` (remote MCP, OAuth) | `appPackage/manifest.json` |
| Skills: Explore Unified Profile, Build Segment, Inventory Data Model, Manage Calculated Insights, Map Data To Model, Configure Activation | `appPackage/skills/*/SKILL.md` |

Needs a Data 360 license, API v66.0+, and `View Data 360` (reads) or `Manage Data 360` (changes). The `execute` tool can create, update and delete, so it is annotated as destructive and the skills confirm before changing anything. Calls count toward Connect API limits and Flex Credit usage.

## Setup

1. **Salesforce org**: an admin activates the `data360` server in Setup > MCP Servers. Salesforce hosted MCP is intended for customers with Flex Credits and may be billed.
2. **External Client App** in Salesforce (Connected Apps are not supported):
   - Enable OAuth. Callback URL `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`.
   - OAuth scopes: Access MCP servers (`mcp_api`) and Perform requests at any time (`refresh_token`). Add nothing else.
   - Security: select **Issue JSON Web Token (JWT)-based access tokens for named users**; deselect the other options you can.
   - Require a client secret for the web server flow (Teams Developer Portal needs one). Keep PKCE enabled.
   - Allow up to 30 minutes after creating or changing the app before testing.
3. **Teams Developer Portal** ([dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools)) > Tools > OAuth client registration > Register client:
   - **Registration name**: for example `Salesforce hosted MCP`.
   - **Base URL**: `https://api.salesforce.com/platform/mcp/v1` (sandbox or scratch orgs: `https://api.salesforce.com/platform/mcp/v1/sandbox`). A prefix base URL has worked for the SObject plugins; if sign-in or tool calls fail, register the server's full URL instead.
   - **Restrict usage by org**: **My organization only** for one tenant; **Any Microsoft 365 organization** for cross-tenant use.
   - **Restrict usage by app**: **Any Teams app**. Do not bind it to a Teams app ID, or provisioning succeeds and every tool call returns 404.
   - **Client ID** and **Client secret**: the External Client App's consumer key and secret.
   - **Authorization endpoint**: `https://login.salesforce.com/services/oauth2/authorize` (`https://test.salesforce.com/...` for a sandbox).
   - **Token endpoint** and **Refresh endpoint**: `https://login.salesforce.com/services/oauth2/token` (`https://test.salesforce.com/...` for a sandbox).
   - **Scope**: `mcp_api refresh_token`.
   - **Enable Proof Key for Code Exchange (PKCE)**: leave enabled.
   - After saving, the portal shows the **OAuth client registration ID**.
4. Copy the registration ID into `env/.env.dev.user` (copy `env/.env.dev.user.sample`) as `SALESFORCE_OAUTH_REFERENCE_ID=<id>`. One registration can be reused by every Salesforce plugin. Nothing secret is committed; the manifest only references `${{SALESFORCE_OAUTH_REFERENCE_ID}}`.

## Build and test

```powershell
wiqd auth login --interactive
wiqd plugin validate
wiqd plugin provision --env dev
wiqd plugin package --env dev          # only after provision
wiqd plugin validate --mode deep --env dev
wiqd plugin share --scope users --email <address>
```

If `package` or deep validation reports `SALESFORCE_OAUTH_REFERENCE_ID` missing, set it in the shell: `$env:SALESFORCE_OAUTH_REFERENCE_ID = "<id>"`. Bump `version` in `appPackage/manifest.json` before uploading a rebuilt package.

## Known issues

- **Verification:** provisioned to a dev tenant and deep-validated, but not exercised against a live org. Verify the connection before sharing.
- **Tokens are not refreshed.** The Salesforce side is fine (a direct `refresh_token` grant succeeds), but the Microsoft OAuth token store does not refresh these tokens: an expired token produces a sign-in prompt instead of a refresh. Expect to sign in again once the access token expires (JWT lifetime equals the Salesforce session timeout, 12 hours at most). Salesforce's hosted MCP 401 responses also omit the `WWW-Authenticate` header.
- **No dynamic client registration.** Salesforce does not publish a registration endpoint, so the manual OAuth registration above is required.
- **Tool definitions come from the live server.** The manifest has no `mcpToolDescription`; a hand-authored copy once caused an SObject `soqlQuery` call to be sent with an empty `query`. The `appPackage/tools/*.json` files are unused leftovers.
- Every call runs as the signed-in user, so Salesforce object permissions, field-level security and sharing rules apply.
- Default icons (`appPackage/color.png` 192x192, `outline.png` 32x32), developer name and URLs in the manifest are placeholders to replace before publishing.

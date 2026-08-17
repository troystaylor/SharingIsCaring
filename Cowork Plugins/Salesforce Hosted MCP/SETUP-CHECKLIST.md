# Setup Checklist — Salesforce (Hosted MCP) Cowork Plugin

Work top to bottom. Nothing here provisions infrastructure — the MCP server is
operated by Salesforce.

## Part 1 — Salesforce org

### 1.1 Create the External Client App

Connected Apps are **not** supported for MCP authentication. You must use an
External Client App (ECA).

- [ ] Setup → Quick Find → **External Client App Manager** → **New External Client App**
- [ ] Fill in Basic Information (name, contact email)
- [ ] Expand **API (Enable OAuth Settings)** → check **Enable OAuth**
- [ ] Set **Callback URL** to the fixed Microsoft 365 redirect URI:
      `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`

      This value is identical for every Copilot plugin and every OAuth
      provider — it is not generated per app, so you can enter it now. Teams
      receives the authorization response here and exchanges the code for
      tokens. If it is missing from the ECA, sign-in fails.
- [ ] Add OAuth scopes:
  - [ ] `mcp_api` — *Access MCP servers*
  - [ ] `refresh_token` — *Perform requests at any time*
- [ ] Under **Security**, select **Issue JSON Web Token (JWT)-based access
      tokens for named users**
- [ ] Deselect every other security option that does not require Salesforce
      support to change
- [ ] Click **Create**

> The ECA can take **up to 30 minutes** to become operational. Expect
> authentication failures before then.

### 1.2 Decide on a client secret

Microsoft 365 Copilot is a web-based client — Teams performs the code-for-token
exchange server side, so the secret is never exposed to a desktop binary. The
Teams developer portal registration form has a **Client secret** field.

- [ ] Enable **Require Secret for Web Server Flow** under **Security** in the ECA
- [ ] Generate the secret and store it — you need it in Part 2
- [ ] If you deliberately skip the secret, PKCE alone protects the flow; leave
      the portal's Client secret field empty and keep PKCE enabled

### 1.3 Capture the consumer key

- [ ] ECA → **Settings** → **OAuth Settings** → **Consumer Key and Secret**
- [ ] Record the **Consumer Key** — this is the OAuth client ID
- [ ] Record the **Consumer Secret** if you enabled it in 1.2

### 1.4 Enable the MCP server

MCP servers are disabled by default and need explicit admin action.

- [ ] Setup → **Integration** → **Salesforce MCP Servers**
- [ ] Enable the server you plan to target (`platform/sobject-all` by default —
      see Part 3 to choose a different one)
- [ ] Or build a **custom server** here to combine sObject tools with Flows,
      Apex invocable actions, `@AuraEnabled` methods, Apex REST, or API Catalog
      endpoints — this is how to expose operations the standard servers lack,
      such as lead conversion
- [ ] Confirm which users are authorized for it

### 1.5 Optional — restrict who can connect

- [ ] Create a permission set for MCP access
- [ ] ECA → **OAuth Policies** → require that permission set for
      pre-authorization
- [ ] Assign it only to the users who should reach Salesforce from Cowork

### 1.6 Verify with Postman before touching Cowork

Salesforce recommends this, and it isolates Salesforce-side problems from
Cowork-side ones.

- [ ] Add the Postman callback URL to the ECA callback list alongside the Teams
      redirect URI from 1.1 — an ECA accepts multiple callback URLs
- [ ] New Postman request → type **MCP** → transport **HTTP**
- [ ] URL: `https://api.salesforce.com/platform/mcp/v1/platform/sobject-all`
      (add `sandbox/` after `v1/` for a sandbox org)
- [ ] Authorization → OAuth 2.0 → Grant Type **Authorization Code (With PKCE)**
  - [ ] Auth URL: `https://login.salesforce.com/services/oauth2/authorize`
        (sandbox: `https://test.salesforce.com/...`)
  - [ ] Access Token URL: `https://login.salesforce.com/services/oauth2/token`
        (sandbox: `https://test.salesforce.com/...`)
  - [ ] Client ID: the consumer key from 1.3
  - [ ] Scope: `mcp_api refresh_token`
  - [ ] Code Challenge Method: **SHA-256**
  - [ ] Client Authentication: **Send client credentials in body**
  - [ ] Callback URL must match one registered on the ECA
        (`https://oauth.pstmn.io/v1/callback` for Postman desktop)
- [ ] Get a token, then confirm `tools/list` returns the eleven sObject tools
- [ ] Call `getUserInfo` and confirm it returns the expected Salesforce user

If this fails, stop. Nothing downstream will work.

## Part 2 — Microsoft 365 OAuth registration

Salesforce does **not** support dynamic client registration, so Cowork cannot
self-register a client. You must create an **auth config** in the Microsoft
Enterprise token store and reference it from the manifest.

There are three ways to create it. Pick one:

| Path | Use when |
|---|---|
| **Teams developer portal** | You have no agent project to scaffold — **the fit for this plugin** |
| Microsoft 365 Agents Toolkit | You are building the agent in the toolkit; it writes the manifest for you |
| Declarative agent developer skill | You want the config created from natural-language instructions |

Agents Toolkit fetches the authorization and token endpoints from the MCP
server's well-known endpoint. This plugin's manifest is hand-authored and the
Salesforce endpoints are fixed and documented, so the portal path is more
direct. Whichever you choose, the result is the same: an **auth config ID**.

### 2.1 Register in the Teams developer portal

- [ ] Open [Teams developer portal](https://dev.teams.microsoft.com/tools) →
      **Tools** → **OAuth client registration**
- [ ] Select **Register client** (or **New OAuth client registration**)
- [ ] Fill in the fields:

| Field | Value |
|---|---|
| **Registration name** | e.g. `Salesforce Hosted MCP` |
| **Base URL** | `https://api.salesforce.com/platform/mcp/v1/platform/sobject-all` |
| **Restrict usage by org** | *My organization only* for testing; *Any Microsoft 365 organization* to distribute |
| **Restrict usage by app** | *Any Teams app* during development; bind to the app ID once stable |
| **Client ID** | The ECA **consumer key** from 1.3 |
| **Client secret** | The ECA **consumer secret** from 1.2, if you enabled one |
| **Authorization endpoint** | `https://login.salesforce.com/services/oauth2/authorize` |
| **Token endpoint** | `https://login.salesforce.com/services/oauth2/token` |
| **Refresh endpoint** | `https://login.salesforce.com/services/oauth2/token` |
| **Scope** | `mcp_api refresh_token` |
| **Enable PKCE** | **On** — Salesforce requires PKCE |

- [ ] For a sandbox org, substitute `https://test.salesforce.com/...` in the
      authorization, token, and refresh endpoints, and add `sandbox/` to the
      Base URL after `v1/`
- [ ] **Save**
- [ ] Record the generated **OAuth client registration ID** — this is the auth
      config ID the manifest references

> **Base URL must match the manifest.** It has to correspond to the
> `mcpServerUrl` in [manifest.json](manifest.json). A mismatch does not produce
> a clear error — it blocks token exchange.

> `offline_access` is a Microsoft identity platform scope. Salesforce uses
> `refresh_token` instead, which is already in the scope list above. Do not add
> `offline_access` here.

## Part 3 — Configure the plugin

- [ ] Pick the server and apply it, supplying the auth config ID from Part 2:

      ```powershell
      cd "Cowork Plugins/Salesforce Hosted MCP"
      ./configure.ps1 -Server sobject-all -ReferenceId "<auth-config-id>"
      ```

- [ ] For a sandbox org, add `-Sandbox` (this must match the Base URL you
      registered in 2.1)
- [ ] For a read-only rollout, use `-Server sobject-reads` — the script drops
      the eight write skills automatically
- [ ] Run `./configure.ps1 -ListServers` to compare server options first
- [ ] For a custom server built in Setup, use
      `./configure.ps1 -CustomServer "myorg/crm-plus" [-BaseOn sobject-reads]`,
      then verify its `tools/list` matches the assumed tool set
- [ ] Leave `id` in [manifest.json](manifest.json) as the existing GUID unless
      you are forking the plugin — changing it creates a separate app identity

> `configure.ps1` owns `mcpServerUrl`, `agentSkills`, the connector
> description, and `referenceId`. Hand-editing those is fine, but re-running the
> script overwrites them, and `preflight.ps1` will fail the build if the skill
> set no longer matches the server.

## Part 4 — Validate and package

```powershell
./preflight.ps1
./package.ps1
```

- [ ] `preflight.ps1` passes with no errors
- [ ] `package.ps1` produces `Salesforce Hosted MCP.zip`

## Part 5 — Publish and connect

- [ ] Upload the zip in the Microsoft 365 Admin Center
- [ ] Publish to a test user group first
- [ ] Open a **fresh** Cowork session — an existing session will not pick up a
      newly published connector
- [ ] Connect the Salesforce connector and complete the sign-in
- [ ] Smoke test in this order:
  - [ ] "Who am I in Salesforce?" — exercises `getUserInfo`
  - [ ] "What's my Salesforce pipeline this quarter?" — exercises `soqlQuery`
        aggregates
  - [ ] "Brief me on [a real account]" — exercises `find` plus multi-query
        synthesis
  - [ ] "What fields are on the Opportunity object?" — exercises
        `getObjectSchema`
  - [ ] A small write on a test record — confirms the confirmation gate fires
        before anything is saved

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Agent never calls any tool | Manifest is not `devPreview`, or the connector was dropped — check `manifestVersion` |
| Sign-in loop or redirect mismatch | `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` is missing from the ECA callback URLs (1.1) |
| Sign-in succeeds but the connector stays disconnected | Teams developer portal **Base URL** does not match `mcpServerUrl` in the manifest, or the registration is restricted to a different org or app ID |
| `invalid_client_id` | ECA is still propagating — wait up to 30 minutes after creation |
| `invalid_grant` on token exchange | Client secret mismatch — the ECA has *Require Secret for Web Server Flow* on but the portal registration has no secret, or vice versa |
| Token expires and never refreshes | `refresh_token` missing from the scope list, or the **Refresh endpoint** was left blank in the portal |
| Authenticates but `tools/list` is empty | The MCP server is not enabled in Setup → Integration → Salesforce MCP Servers |
| `INVALID_TYPE` / `INVALID_FIELD` on every query | Wrong org type in the URL (production vs sandbox), or the user lacks object access |
| Queries return far less than expected | Sharing rules and field-level security — the connector always runs as the signed-in user |
| Writes rejected with a validation message | An org validation rule; the message is surfaced verbatim by the skills |
| Works in Postman, fails in Cowork | The auth config, not Salesforce — recheck Base URL, scopes, PKCE, secret, and org/app restrictions in the Teams developer portal |
| Preflight fails with "cannot run it" | The manifest declares skills the target server lacks — run `./configure.ps1 -Server <server>` |
| Agent cannot scope "my deals" or "my tasks" | The target server does not expose `getUserInfo` — only `sobject-reads` and `sobject-all` do |

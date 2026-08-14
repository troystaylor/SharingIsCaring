# GitHub for Copilot Cowork

A [Microsoft 365 Copilot Cowork](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/)
plugin that connects Cowork to [GitHub's official remote MCP server](https://github.com/github/github-mcp-server).

Six skills cover the GitHub work that people actually delegate: finding code,
triaging a backlog, reviewing pull requests, drafting release notes, writing
engineering status updates, and handing implementation tasks to the Copilot
coding agent.

No server to host. The connector points directly at GitHub's hosted MCP
endpoint, and each user authenticates as themselves — Cowork only ever sees
repositories that user already has access to. You do need to register a GitHub
App for the OAuth flow; see [Authentication](#authentication).

## Contents

```
GitHub Cowork/
├── manifest.json                 M365 unified app manifest (v1.30)
├── color.png                     192×192 app icon
├── outline.png                   32×32 outline icon
├── package.ps1                   Validates and zips the plugin
├── tools/
│   └── github-tools.json         47 tools with full input schemas
└── skills/
    ├── explore-repositories/     Search repos and code, read files, history
    ├── triage-issues/            Find, file, label, assign, close issues
    ├── review-pull-requests/     Review, comment, approve, merge PRs
    ├── release-notes/            Changelogs and "what shipped" summaries
    ├── engineering-report/       Status rollups across repos and teams
    └── delegate-to-copilot/      Hand work to the Copilot coding agent
```

## Skills

| Skill | Pattern | Triggers on |
|---|---|---|
| [explore-repositories](skills/explore-repositories/SKILL.md) | Discovery | "find the repo for", "search GitHub for", "show me the code that", "what changed recently in" |
| [triage-issues](skills/triage-issues/SKILL.md) | Mutation | "triage the backlog", "file an issue", "assign this to", "what's untriaged" |
| [review-pull-requests](skills/review-pull-requests/SKILL.md) | Mutation | "what PRs are waiting on me", "review this PR", "is this ready to merge" |
| [release-notes](skills/release-notes/SKILL.md) | Aggregation | "write release notes", "what shipped in v2.1", "draft a changelog" |
| [engineering-report](skills/engineering-report/SKILL.md) | Aggregation | "sprint summary", "how's the team doing", "what's blocked" |
| [delegate-to-copilot](skills/delegate-to-copilot/SKILL.md) | Mutation | "have Copilot fix this", "assign it to Copilot", "get Copilot to review this PR" |

Two skills ship reference files that stay out of the main context until needed:

- [github-search-syntax.md](skills/explore-repositories/references/github-search-syntax.md) — search qualifiers and practical query recipes
- [issue-tool-reference.md](skills/triage-issues/references/issue-tool-reference.md) — `issue_write` methods, state reasons, custom fields

## Architecture

```
Copilot Cowork
   |  Agent Skills (SKILL.md)  → workflow, output shape, guardrails
   |  agentConnectors[]        → JSON-RPC 2.0 over HTTPS (streamable)
   v
https://api.githubcopilot.com/mcp/   (GitHub-hosted, OAuth per user)
   |
   v
GitHub REST + GraphQL API
```

This differs from the sibling plugins in [Cowork Plugins/](../readme.md), which
deliberately keep the vendor's hosted MCP out of the data plane and run an
in-tenant MCP server instead. Here the endpoint is GitHub's own first-party
service, so there is no third-party intermediary to remove. If your
organization requires all GitHub traffic to egress from your own Azure
subscription, self-host the
[github-mcp-server](https://github.com/github/github-mcp-server) container and
point `mcpServerUrl` at it instead.

## Authentication

**GitHub's remote MCP server does not support Dynamic Client Registration**
([confirmed in GitHub's host integration docs](https://github.com/github/github-mcp-server/blob/main/docs/host-integration.md#oauth-support-on-github):
"Dynamic Client Registration is NOT supported by Remote GitHub MCP Server at
this time"). You cannot omit the `authorization` block and let Cowork register
a client at runtime — you must bring your own GitHub App or OAuth App.

The connector therefore uses `OAuthPluginVault`:

```json
"toolSource": {
    "remoteMcpServer": {
        "mcpServerUrl": "https://api.githubcopilot.com/mcp/",
        "mcpToolDescription": {
            "file": "tools/github-tools.json"
        },
        "authorization": {
            "type": "OAuthPluginVault",
            "referenceId": "{{OAUTH_REFERENCE_ID}}"
        }
    }
}
```

### Setup

**Step 1 — Register an OAuth App on GitHub.**
[github.com/settings/developers](https://github.com/settings/developers) →
**OAuth Apps** → **New OAuth App**.

| Field | Value |
|---|---|
| Application name | `GitHub for Copilot Cowork` |
| Homepage URL | anything valid, e.g. your repo URL |
| Authorization callback URL | `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` |

That callback URL is fixed — it is the same for every Cowork plugin and every
provider. Sign-in fails if it is not registered exactly. Generate a client
secret and copy it immediately; GitHub shows it once.

A [GitHub App](https://docs.github.com/en/apps/creating-github-apps) is the
more secure option for production — its user tokens expire and refresh, and
repository access is selectable. It is more setup, so the walkthrough above
uses an OAuth App for a first test deployment.

**Step 2 — Create the auth config.** [Teams developer portal](https://dev.teams.microsoft.com/tools)
→ **Tools** → **OAuth client registration** → **New OAuth client registration**.

| Field | Value |
|---|---|
| Registration name | `GitHub MCP` |
| Base URL | `https://api.githubcopilot.com/mcp` — **no trailing slash** |
| Restrict usage by org | **My organization only** for testing; **Any Microsoft 365 organization** to ship |
| Restrict usage by app | **Any Teams app** during development |
| Client ID | from Step 1 |
| Client secret | from Step 1 |
| Authorization endpoint | `https://github.com/login/oauth/authorize` |
| Token endpoint | `https://github.com/login/oauth/access_token` |
| Refresh endpoint | `https://github.com/login/oauth/access_token` |
| Scope | `repo read:org read:user offline_access` |
| Enable PKCE | On |

The endpoints are GitHub's published values from
`https://github.com/.well-known/oauth-authorization-server/login/oauth`.

**The Base URL must not have a trailing slash**, even though the connector's
`mcpServerUrl` does. See [Base URL must not have a trailing slash](#base-url-must-not-have-a-trailing-slash)
— this is the single most likely cause of a connector that fails after a
successful sign-in.

`offline_access` opts the sign-in into an expiring access token plus a refresh
token. It is not a normal scope, does not add a consent prompt, and is
advertised under `scopes_supported` in GitHub's metadata. It is recommended but
not required — the connector also works with non-expiring tokens, which are the
weaker posture.

PKCE is safe to enable — GitHub documents `code_challenge` on the authorize
endpoint as strongly recommended for OAuth Apps.

**Step 3 — Wire up the manifest.** Saving the registration returns an **auth
config ID** (labeled *OAuth client registration ID* in the portal). Replace the
`{{OAUTH_REFERENCE_ID}}` placeholder in
[manifest.json](manifest.json) with it, then rebuild the package —
`package.ps1` warns while the placeholder is still in place.

The value encodes as `base64("{tenantId}##{authConfigId}")`. With *My
organization only*, it works **only** in the tenant that created it — create it
while signed into the same tenant you sideload from, or the plugin installs
cleanly and then fails at sign-in with what looks like a GitHub error.

The committed `GitHub Cowork.zip` ships with the placeholder, so it will not
authenticate until you set your own value and re-run `package.ps1`.

### Scopes

GitHub's protected-resource metadata at
`https://api.githubcopilot.com/.well-known/oauth-protected-resource/mcp`
advertises: `repo`, `read:org`, `read:user`, `user:email`, `read:packages`,
`write:packages`, `read:project`, `project`, `gist`, `notifications`,
`workflow`, `codespace`.

`repo read:org read:user offline_access` covers all six skills. `repo` is broad —
it grants full control of private repositories, which is what the issue, pull
request, and merge tools need. There is no narrower scope that still permits
writes. `offline_access` is not a permission; it opts the sign-in into an
expiring access token plus a refresh token, which is the stronger posture.

### Verified working configuration

Confirmed end to end on 2026-08-14:

| Setting | Value |
|---|---|
| `manifestVersion` | `1.30` |
| `mcpServerUrl` (manifest) | `https://api.githubcopilot.com/mcp/` — **with** slash |
| Base URL (registration) | `https://api.githubcopilot.com/mcp` — **no** slash |
| Scope | `repo read:org read:user offline_access` |
| Refresh endpoint | `https://github.com/login/oauth/access_token` |
| PKCE | Enabled |
| Expiring user tokens (OAuth App) | Opted in — 8h token with refresh |
| Restrict usage by app | Any Teams app |
| Restrict usage by org | Any Microsoft 365 organization |

The connector also works with non-expiring tokens (opt out of expiring user
tokens and drop `offline_access`), but that is the weaker posture — the token
never expires and cannot be rotated.

Each user completes a one-time GitHub sign-in the first time a skill calls a
tool. Admins cannot complete it on a user's behalf.

### Troubleshooting the connection

| Symptom | Likely cause |
|---|---|
| `Connector error: GitHub couldn't complete the request` on enable | `offline_access` missing from the registration's Scope, so no refresh token is issued |
| OAuth popup closes instantly without showing a consent screen | Expected. GitHub auto-completes when a prior authorization exists, and `offline_access` never prompts. Only a problem if the connection still fails. |
| Still failing after adding `offline_access` | The previous grant is being replayed. Revoke the app at [github.com/settings/applications](https://github.com/settings/applications) → Authorized OAuth Apps, then reconnect. |
| Sign-in page shows a redirect URI mismatch | The OAuth App's callback URL is not exactly `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` |
| Sign-in succeeds, then tools fail with 401/403 | The `referenceId` tenant does not match the tenant you installed into |
| Personal repos work, organization repos 404 | The GitHub org has not approved the OAuth App |

After changing the registration, disconnect and re-enable the connector in
Cowork so it runs a fresh sign-in — the old authorization is cached. Changing
scopes does **not** require reinstalling or repackaging the plugin; the grant
lives on GitHub and in the M365 token store, not in the app package.

### Bisecting the OAuth flow with the redirect URL

The authorization code flow has two legs, and they fail for different reasons.
Capture the popup's redirect to
`teams.microsoft.com/api/platform/v1.0/oAuthRedirect` (browser devtools,
**Network** tab, **Preserve log** ticked) and read its query string:

| Redirect contains | Meaning |
|---|---|
| `error=...&error_description=...` | **Leg 1 failed.** Client ID, redirect URI registration, org policy, or consent. The description names the cause. |
| `code=...&state=...` with no `error` | **Leg 1 succeeded.** Client ID and redirect URI are provably correct and consent was granted. The failure is in leg 2, the token exchange. |

When leg 1 succeeds, the client ID in the registration is confirmed good — it is
the value that produced the authorize request. The remaining candidates are the
**client secret** (the only value nothing validates until token exchange) and
**PKCE** handling.

**GitHub returns HTTP 200 even on token-exchange errors**, and by default
responds in `application/x-www-form-urlencoded` rather than JSON:

```
error=incorrect_client_credentials&error_description=The+client_id+and%2For+client_secret+passed+are+incorrect.
```

Only with `Accept: application/json` does it return a JSON error object. A
client that checks status codes alone sees a 200 with no token, which is why
this surfaces as a generic connector error rather than an auth error. Verify
with:

```powershell
$body = "client_id=YOUR_ID&client_secret=YOUR_SECRET&code=bogus&grant_type=authorization_code&redirect_uri=https%3A%2F%2Fteams.microsoft.com%2Fapi%2Fplatform%2Fv1.0%2FoAuthRedirect"
$r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "https://github.com/login/oauth/access_token" `
     -Body $body -ContentType "application/x-www-form-urlencoded" -Headers @{Accept="application/json"} -SkipHttpErrorCheck
$r.Content
```

`incorrect_client_credentials` with a known-good code means the secret is wrong.

### Read the real error with developer mode

> Developer mode is a **Microsoft 365 Copilot Chat** feature for declarative
> agents. A Cowork plugin is not an agent, so `-developer on` and
> **Chat settings → Agents** may not be available for this package. The error
> table below still maps Enterprise Token Store failures to fixes, because
> Cowork uses the same auth config, but read it as a reference rather than a
> Cowork procedure.

In Copilot Chat, `-developer on` enables
[developer mode](https://learn.microsoft.com/microsoft-365/copilot/extensibility/prerequisites#enabling-developer-mode)
and surfaces the underlying OAuth error in a debug information card.

Map the message to the fix
([full list](https://learn.microsoft.com/microsoft-365/copilot/extensibility/plugin-authentication-troubleshooting)):

| Debug card error | Fix |
|---|---|
| `The base URL in your authentication configuration does not match the server URL. (HTTP 401)` | The registration's **Base URL** must be `https://api.githubcopilot.com/mcp` with **no** trailing slash — it does not match `mcpServerUrl` |
| `No matching configuration found for referenceID in 'runtime.auth'` | `referenceId` in the manifest ≠ the auth config ID in the portal |
| `The App ID used in the request does not match the App ID in the authentication configuration. (HTTP 404)` | Set **Restrict usage by app** to *Any Teams app*, or bind it to this app's ID |
| `Access is restricted by your organization's policy. (HTTP 404)` | Tenant policy blocks the app — needs an admin |
| `Incorrect credentials` | Client ID or secret in the registration is wrong |

Other documented cases worth knowing:

- **Popup opens but never closes** — a page in the OAuth redirect chain
  destroyed `window.opener`, usually via `Cross-Origin-Opener-Policy: same-origin`
  or `window.opener = null`. Classic tell: fails the first time, works on retry.
- **`307 Temporary Redirect` from the token endpoint** — unsupported. The token
  endpoint must be direct.
- **Sign-in URL not found** — uninstall and reinstall the app.

### Clearing a cached token between attempts

Disconnecting in **Sources & Skills** does not reliably clear a stored token.
Sign out from **Chat settings → Agents** in Microsoft 365 Copilot, which
explicitly clears the stored OAuth token, then reconnect. Otherwise a failed
authorization can be replayed and mask a fix that already worked.

### Isolating a failed connection

Work through these in order. Each one eliminates a whole layer, and together
they narrow a generic connector error to a single binding.

| # | Test | Proves |
|---|---|---|
| 1 | Install a no-auth connector plugin (e.g. the public Microsoft Learn MCP server, `authorization: None`) and call a tool | Whether Cowork remote MCP connectors work at all in this tenant, independent of OAuth |
| 2 | Device-flow login with your own client ID, then call `tools/list` | Whether the OAuth App, its scopes, and GitHub org/enterprise policy allow access |
| 3 | `Test-OAuthExchange.ps1` with the real client ID and secret | Whether those exact credentials complete authorize **and** token exchange |
| 4 | Compare the portal's auth config ID against the manifest `referenceId` | Whether the plugin points at the live registration |
| 5 | Check the portal's Base URL is `https://api.githubcopilot.com/mcp` with **no** trailing slash | Whether the connector resolves to that registration. Do **not** match it to `mcpServerUrl` — the manifest keeps the trailing slash and the Base URL does not |

If 1–4 pass and Cowork still fails, the fault is in the Teams auth config
binding or the Enterprise Token Store, not in GitHub, the credentials, or the
package. At that point stop changing settings and report it — passing results
from 1, 2, and 3 alongside a failing connector is strong evidence.

**`Test-OAuthExchange.ps1`** (in this folder) automates test 3. It reads the
secret as a `SecureString`, never echoes or stores it, splits the flow into
leg 1 and leg 2, prints GitHub's real error text, and on success calls the MCP
server with the resulting token.

```powershell
.\Test-OAuthExchange.ps1              # opens the browser for you
.\Test-OAuthExchange.ps1 -NoBrowser   # prints the URL instead, for a specific browser profile
```

The `teams.microsoft.com` error page you land on during that test
("...ensure the app is registered with the OAuth provider and this client id is
registered in the OAuth configuration") is **expected and not a finding**.
Teams cannot resolve a callback whose `state` it did not generate. The test
only needs the `code` value from that URL.

Cowork's connector error is generic, so bisect it instead of guessing.

**Validate the Client ID** without needing a secret or a browser session — the
device-code endpoint distinguishes a real app from a bad ID:

```powershell
Invoke-WebRequest -UseBasicParsing -Method Post -Uri "https://github.com/login/device/code" `
  -Body "client_id=YOUR_CLIENT_ID&scope=repo" -ContentType "application/x-www-form-urlencoded" `
  -Headers @{Accept="application/json"} -SkipHttpErrorCheck | Select-Object -Expand Content
```

`device_flow_disabled` means the app exists and the ID is valid. `Not Found`
means the ID is wrong. (Calling `/login/oauth/authorize` proves nothing —
GitHub defers client and redirect validation until after sign-in, so a bad ID
and a good one both redirect to the login page.)

**Then split callback URL from client secret.** Attempt the connection, then
check [github.com/settings/applications](https://github.com/settings/applications)
→ Authorized OAuth Apps:

| Observation | Meaning |
|---|---|
| App not listed | Consent never completed — the callback URL on the OAuth App is wrong |
| App listed | Consent succeeded, token exchange failed — the client secret in the Teams registration is stale or mistyped |

**Prove the GitHub side independently.** If the connector still fails, take
Cowork out of the picture entirely: temporarily tick **Enable Device Flow** on
the OAuth App and authenticate as your own app, with no client secret and no
browser redirect.

```powershell
$cid = "YOUR_CLIENT_ID"
$r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "https://github.com/login/device/code" `
     -Body "client_id=$cid&scope=repo%20read:org%20read:user" -ContentType "application/x-www-form-urlencoded" `
     -Headers @{Accept="application/json"}
$j = $r.Content | ConvertFrom-Json
"Enter $($j.user_code) at $($j.verification_uri)"

# after approving in the browser
$t = (Invoke-WebRequest -UseBasicParsing -Method Post -Uri "https://github.com/login/oauth/access_token" `
      -Body "client_id=$cid&device_code=$($j.device_code)&grant_type=urn:ietf:params:oauth:grant-type:device_code" `
      -ContentType "application/x-www-form-urlencoded" -Headers @{Accept="application/json"}).Content | ConvertFrom-Json

Invoke-WebRequest -UseBasicParsing -Method Post -Uri "https://api.githubcopilot.com/mcp/" `
  -Body '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' -ContentType "application/json" `
  -Headers @{Accept="application/json, text/event-stream"; Authorization="Bearer $($t.access_token)"} |
  Select-Object -Expand StatusCode
```

A `200` proves the app, its scopes, and every GitHub-side policy are fine, which
narrows the fault to the Teams registration — in practice the **client secret**,
since it is the one value nothing else in the chain validates. Untick Device
Flow afterward.

### Enterprise policies that can block the connector

These are outside the plugin and cannot be fixed by editing the manifest. See
[GitHub's policies and governance doc](https://github.com/github/github-mcp-server/blob/main/docs/policies-and-governance.md).

| Policy | Location | Effect when disabled |
|---|---|---|
| MCP servers in Copilot | Enterprise/Org → Policies → Copilot | Blocks all GitHub MCP access, every auth method |
| Copilot Editor Preview Features | Enterprise/Org → Policies → Copilot | Blocks the **remote** server over OAuth; PAT auth unaffected |
| OAuth App access policy | Org → Settings → Third-party Access | Org admin must approve the OAuth App before it sees org data |
| SSO enforcement | Enterprise/Org → SSO | OAuth tokens must map to a recent SSO login for protected orgs |

A token from a long-established app such as the GitHub CLI can succeed while a
newly registered third-party OAuth App is blocked, so test with **your own**
client ID rather than inferring from `gh auth token`.

### Organization approval

GitHub organizations can block third-party Apps until an admin approves them.
If a user signs in successfully but organization repositories return 404, the
App needs approval at
`https://github.com/organizations/{ORG}/settings/oauth_application_policy`.
The skills detect this pattern and tell the user to contact their GitHub org
admin rather than looping on re-authentication.

### Personal access tokens

The remote server also accepts any valid GitHub token in the `Authorization`
header, including a PAT. Cowork's connector model has no per-user secret input,
so this is not a viable path for a distributed plugin — it is mentioned only
because GitHub's docs describe it as an option for other MCP hosts.

## Toolset scope

One connector at `https://api.githubcopilot.com/mcp/`, the **default** endpoint,
which serves **47 tools**.

GitHub's README documents the default toolset as `context, repos, issues,
pull_requests, users` (42 tools). That describes the **local** server. The
**remote** default is broader — it also serves the Copilot coding agent tools
(`assign_copilot_to_issue`, `create_pull_request_with_copilot`,
`get_copilot_job_status`, `request_copilot_review`) plus `run_secret_scanning`.
Confirmed by calling `tools/list` against the live endpoint; a second connector
on `/x/copilot` is redundant because all five of its tools are already in the
default.

**Verify against the live server rather than the docs** when changing scope:

```powershell
$tok = (gh auth token).Trim()
$body = '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
$r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "https://api.githubcopilot.com/mcp/" `
     -Body $body -ContentType "application/json" `
     -Headers @{ Accept="application/json, text/event-stream"; Authorization="Bearer $tok" }
(($r.Content -replace '(?m)^data:\s*','') | ConvertFrom-Json).result.tools.name
```

### `mcpToolDescription` is mandatory

Every `remoteMcpServer` connector must point at a tool-description file present
in the ZIP. Three rules the package service enforces, each learned from a real
HTTP 400:

| Rule | Failure if broken |
|---|---|
| The `file` path must exist in the ZIP | `declared MCP tool description file ... not found in the app package` |
| The path must **not** start with `./` | Same "not found" error — the path is resolved literally |
| Every tool needs an `inputSchema` JSON Schema object | `MCP tool 'x' is missing a valid 'inputSchema' object` |

`package.ps1` checks all three locally so you find out in a second rather than
on upload.

[tools/github-tools.json](tools/github-tools.json) is generated from the live
server's `tools/list` (names, descriptions, annotations, and full input
schemas, with icon blobs stripped). Regenerate it whenever you change
`mcpServerUrl` — nothing detects drift between the file and the live server.

### Adding toolsets

Adding a toolset does **not** make the skills use it — skills name specific
tools. To use GitHub Actions or Projects, point a connector at that toolset,
regenerate its tool-description file, and add or extend a skill that references
those tools. The full list is in
[GitHub's remote server docs](https://github.com/github/github-mcp-server/blob/main/docs/remote-server.md).

Gaps the skills call out explicitly rather than silently failing on:

| Want | Needs toolset |
|---|---|
| Create or edit label definitions | `labels` |
| Read or update project boards | `projects` |
| Workflow runs, job logs, re-runs | `actions` |
| Code scanning / Dependabot alerts | `code_security`, `dependabot` |
| Notification inbox | `notifications` |
| Discussions | `discussions` |

### Repository write tools

The default toolset includes `create_or_update_file`, `push_files`,
`delete_file`, `create_repository`, `create_branch`, and `fork_repository`. No
skill in this plugin uses them, but they remain available to the agent, and the
tool-description file lists them because it should describe what the server
actually exposes.

If unattended file writes are a concern, the options are to point the connector
at `/readonly` (which also disables issue writes, reviews, and merges —
breaking three skills) or to self-host the server with `X-MCP-Tools` set to an
explicit allowlist.

## Base URL must not have a trailing slash

The **Base URL** in the Teams developer portal OAuth client registration must be:

```
https://api.githubcopilot.com/mcp
```

**Without** a trailing slash — even though the connector's `mcpServerUrl` in
[manifest.json](manifest.json) is `https://api.githubcopilot.com/mcp/` **with**
one. The two do not have to match each other, and matching them breaks the
connection.

The Base URL is matched against the resource identifier GitHub publishes at
`https://api.githubcopilot.com/.well-known/oauth-protected-resource/mcp`, which
declares:

```json
"resource": "https://api.githubcopilot.com/mcp"
```

The working configuration is therefore asymmetric:

| Setting | Value |
|---|---|
| `mcpServerUrl` in `manifest.json` | `https://api.githubcopilot.com/mcp/` (**with** slash) |
| Base URL in the OAuth client registration | `https://api.githubcopilot.com/mcp` (**no** slash) |

Getting this wrong produces `Connector error: GitHub couldn't complete the
request` with no indication that a URL is at fault. The authorization leg still
succeeds — GitHub returns a valid `code` — so every check on the GitHub side
looks healthy while the token exchange fails.

Microsoft's guidance that the Base URL "should correspond to the URL in the
`url` property" of the MCP server spec reads as an instruction to match the
manifest exactly. For this server, it isn't.

## Read-only deployment

For a lower-risk rollout, publish a read-only variant:

1. Set `mcpServerUrl` to `https://api.githubcopilot.com/mcp/readonly`
2. Regenerate `tools/github-tools.json` from that endpoint
3. Remove `triage-issues`, `review-pull-requests`, and `delegate-to-copilot`
   from `agentSkills` — write calls would fail, and skills that promise actions
   they cannot perform are worse than absent ones
4. Bump `version` and change `id` to a new GUID so both variants can coexist

## Package and deploy

```powershell
cd "GitHub Cowork"
.\package.ps1 -SkipIcons     # during development
.\package.ps1                # full validation, produces GitHub Cowork.zip
```

Sideload for testing with the Microsoft 365 Agents Toolkit CLI:

```powershell
npm install -g @microsoft/m365agentstoolkit-cli
atk auth login m365
atk install --file-path ".\GitHub Cowork.zip" --scope Personal
```

### Installing is not the same as surfacing it in Cowork

`atk install` installs the app for the signed-in account. There are three
distinct ways to get the plugin in front of Cowork, and they are not
equivalent.

**1. Author testing — fastest loop, use this first.** Install your own package
directly in Cowork, bypassing both `atk` and the admin center:

1. In Cowork, open **Customize** → **Plugins** tab
2. Select **Add plugin** and choose the `.zip`
3. In the **Share** dialog, choose **Only you**

Microsoft describes this as the fastest way to confirm that skills, connectors,
and tool calls work end to end. If a connector misbehaves after an `atk install`,
try this path before debugging the connector itself — remove the other copy
first so two installs of the same manifest `id` don't collide.

**2. Tenant distribution.** **M365 admin center** → **Manage apps** →
**Upload custom app** → ellipsis (**…**) → **Add agent**, then assign to
specific users, groups, or the whole org. The **… → Add agent** step is easy to
miss; a plain custom app upload does not register the agent surface. Allow a
few minutes for the catalog to propagate.

**3. `atk install --scope Personal`.** Installs for the signed-in account and
returns a `TitleId` and `AppId`. Useful for scripted or repeatable installs.
(`atk launchinfo` returns 404 for this package; expected, since a
skills-and-connector plugin has no launchable tab or bot surface.)

**The `referenceId` is tenant-scoped.** It encodes as
`base64("{tenantId}##{authConfigId}")`, and a registration created with
*My organization only* works solely in the tenant that created it. Verify the
account you sideload with matches before uploading:

```powershell
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($referenceId))
```

Re-uploading with the same `id` and a higher `version` replaces the existing
install. Active conversations keep the old version; new ones pick up the update.

## Guardrails built into the skills

Every skill that writes follows the same rules, because Cowork acts
autonomously and GitHub writes are visible to other people:

- **Confirm before every write.** Triage plans are presented as a table and
  applied only after an explicit yes.
- **Never approve or merge unprompted.** Summarizing a PR is safe; putting the
  user's name on an approval is not.
- **Never dispatch the coding agent speculatively.** Each dispatch costs a
  Copilot premium request and creates real repository activity.
- **Distinguish 401 from 403.** A sign-in prompt does not fix a permissions
  problem, and telling the user to re-authenticate when they lack write access
  just wastes their time.
- **Report partial results as partial.** If some repositories in an org are
  invisible to the user's token, the report says so rather than reading as
  complete.
- **Activity is not performance.** The reporting skill will produce per-person
  counts if asked, but states plainly that PR and commit counts do not measure
  individual productivity.

## Cross-platform reuse

The `skills/` folder follows the [Agent Skills](https://code.visualstudio.com/docs/copilot/customization/agent-skills)
open standard. The same `SKILL.md` files work unchanged in VS Code Copilot,
Claude Code, Gemini CLI, and Cursor when paired with the GitHub MCP server
configured in that host.

## Related

- [github/github-mcp-server](https://github.com/github/github-mcp-server) — the MCP server this plugin consumes
- [Remote server docs](https://github.com/github/github-mcp-server/blob/main/docs/remote-server.md) — toolset URLs, headers, insiders mode
- [Cowork Plugin Template/](../../Cowork%20Plugin%20Template/readme.md) — the scaffold this plugin was built from
- [Cowork plugin development](https://learn.microsoft.com/en-us/microsoft-365/copilot/cowork/cowork-plugin-development)

## Icons

`color.png` and `outline.png` are a neutral branch mark, not GitHub's logo or
Octocat — those are GitHub trademarks and should not be shipped in a
third-party package without permission. Replace them with your own artwork
before publishing (192×192 and 32×32 PNG respectively).

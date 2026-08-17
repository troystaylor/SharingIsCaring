# Salesforce for Copilot Cowork — Hosted MCP

Cowork plugin that brings Salesforce CRM workflows into Copilot Cowork using
**Salesforce's own hosted MCP servers**. There is no MCP server to build, no
container to deploy, and no Azure infrastructure to run.

This is the counterpart to [`Cowork Plugins/Salesforce`](../Salesforce/readme.md),
which ships a self-hosted MCP server on Azure Container Apps. Same domain,
opposite architecture — see [Which plugin should I use](#which-plugin-should-i-use).

- Setup checklist: [SETUP-CHECKLIST.md](SETUP-CHECKLIST.md)

## How it works

```
Copilot Cowork
   │  MCP (streamable HTTP) + OAuth 2.0 / PKCE
   ▼
https://api.salesforce.com/platform/mcp/v1/platform/sobject-all
   │  runs as the signed-in Salesforce user
   ▼
Salesforce org — profile, sharing rules, and field-level security enforced
```

Salesforce operates the MCP server. The plugin contributes two things:

1. **A connector definition** pointing at the Salesforce-hosted endpoint.
2. **Nineteen skills** that carry the CRM domain knowledge — which objects and
   fields to touch, the exact SOQL to run, how to interpret the results, and
   when to require confirmation before writing.

That second part is the whole value. The hosted server exposes *generic* sObject
tools, not `search_accounts`-style domain tools. Without skills, the agent has to
guess field API names and SOQL syntax on every turn.

## Tools exposed by the hosted server

By default the plugin connects to `platform/sobject-all`, which provides eleven
tools:

| Tool | Purpose |
|---|---|
| `getObjectSchema` | Object index, or full field schema for one object |
| `soqlQuery` | Run a SOQL query |
| `find` | Run a SOSL text search |
| `getUserInfo` | Identity of the signed-in Salesforce user |
| `listRecentSobjectRecords` | Recently viewed or modified records |
| `getRelatedRecords` | Child records via a relationship path |
| `createSobjectRecord` | Create a record |
| `updateSobjectRecord` | Update a record |
| `updateRelatedSobjectRecord` | Update a child record via relationship |
| `deleteSobjectRecord` | Delete a record |
| `deleteRelatedSobjectRecord` | Delete a child record via relationship |

The full set of Salesforce standard servers:

| Server name | Scope |
|---|---|
| `platform/sobject-all` | Full CRUD (**this plugin's default**) |
| `platform/sobject-reads` | Read only — schema, SOQL, SOSL, relationships |
| `platform/sobject-mutations` | Create and update, no delete |
| `platform/sobject-deletes` | Delete only |
| `data/data-cloud-queries` | Data 360 SQL queries |
| `data/data360` | Data 360 Connect APIs |
| `analytics/tableau-next` | Tableau Next semantic models, dashboards, metrics |

Switch between them with `configure.ps1` rather than editing `mcpServerUrl` by
hand — see below.

## Choosing a server

Run `configure.ps1` to target a server — it rewrites `mcpServerUrl` and declares
only the skills that server's tools can actually support:

```powershell
./configure.ps1 -ListServers                    # see the options
./configure.ps1 -Server sobject-all             # full CRUD (default)
./configure.ps1 -Server sobject-reads           # read-only deployment
./configure.ps1 -Server sobject-all -Sandbox    # sandbox or scratch org
./configure.ps1 -Server sobject-all -ReferenceId "<auth-config-id>"
```

| Server | Tools | Skills | Capability |
|---|---|---|---|
| `platform/sobject-all` | 11 | 19 of 19 | Full CRUD — **default** |
| `platform/sobject-reads` | 6 | 11 of 19 | Read-only |
| `platform/sobject-mutations` | 6 | 12 of 19 | Create and update, no delete |
| `platform/sobject-deletes` | 5 | 6 of 19 | Delete only |

Skill folders are never deleted, so re-running with a broader server restores
the fuller set. `preflight.ps1` fails the build if a hand-edited manifest
declares a skill the target server cannot run.

**Why the counts differ so much.** Salesforce exposes `getUserInfo` only on
`sobject-reads` and `sobject-all`. Six skills — pipeline review, deal health,
risk, tasks, leads, cases — resolve "my deals" and "my tasks" through it, so
they cannot be scoped on `sobject-mutations` or `sobject-deletes`. Those two
servers are best treated as narrow, purpose-built deployments rather than
general CRM assistants.

The Data 360 and Tableau Next servers expose entirely unrelated tools; these
skills do not apply to them. See [Why this plugin ships one connector](#why-this-plugin-ships-one-connector).

### Custom servers

Salesforce Setup can build a **custom** MCP server that composes sObject tools
with org-specific capabilities:

| Tool source | What it exposes |
|---|---|
| Apex `@InvocableMethod` | Business logic and multi-step operations |
| Apex `@AuraEnabled` | Existing Lightning controller methods, no new code |
| Apex REST (`@RestResource`) | Existing custom REST endpoints |
| API Catalog / Connect APIs | Salesforce-authored product APIs |
| Flows | Declarative automation from Flow Builder |
| Prompt Builder templates | MCP prompts — client support varies |

Target one with:

```powershell
./configure.ps1 -CustomServer "myorg/crm-plus"
./configure.ps1 -CustomServer "myorg/crm-readonly" -BaseOn sobject-reads
```

`-BaseOn` tells the script which standard tool set the custom server is assumed
to include, so it can pick the skill set. Verify that assumption against the
server's real `tools/list` output — `preflight.ps1` cannot validate a custom
server's tools, and will only note that the name is not a documented standard
server.

This is also the route around the constraints below: an admin can expose lead
conversion or record merge as a Flow or invocable action, then add a skill that
names that tool.

### Why this plugin ships one connector

The manifest schema allows up to 10 connectors, and all Salesforce hosted
servers share one External Client App and one scope pair — so adding
`analytics/tableau-next` or `data/data360` alongside the sObject server is
technically possible. This plugin deliberately does not, for four reasons:

- **Skill budget.** A plugin may declare at most 20 skills and this one uses 19.
  Data 360 and Tableau Next need roughly six to eight skills between them to be
  worth having.
- **Separate licensing.** Data 360 servers require a Data 360 license; Tableau
  Next requires Concierge enabled in the org. Bundling them means most users
  install skills that can never run.
- **Separate sign-in.** Each connector is its own user consent, and each needs
  its own Teams developer portal registration because **Base URL** must
  correspond to that connector's `mcpServerUrl`.
- **Different job.** CRM record work and analytics exploration have different
  audiences and different output expectations.

To add one anyway, append a second entry to `agentConnectors` with its own `id`,
its own `mcpServerUrl`, and its own `referenceId` from a second portal
registration, then add skills that name that server's tools. Note that
`data/data360` is a **meta-tool** server — `search`, then `payload_examples`,
then `execute` — so its skills look nothing like the SOQL-based ones here.

> **If you do add a second connector, stop using `configure.ps1`.** It rebuilds
> `agentSkills` from its own catalog and only manages `agentConnectors[0]`, so
> re-running it silently drops any skill added for the second connector. Manage
> the manifest by hand from that point on.

If you later want Data 360 or Tableau Next coverage, a separate plugin is the
better shape: it gets its own 20-skill budget, and users install it only if
their org is licensed for it.

## Skills

The repo carries all 19 skills; `configure.ps1` decides which ship. **Needs**
lists the tools a skill cannot work without — that is what determines whether it
survives on a reduced server.

| Skill | Intent | Mode | Needs |
|---|---|---|---|
| [account-briefing](skills/account-briefing/SKILL.md) | Account 360 snapshot before a customer meeting | Read | — |
| [opportunity-health-summary](skills/opportunity-health-summary/SKILL.md) | Deal hygiene inspection and risk scoring | Read | `getUserInfo` |
| [pipeline-review](skills/pipeline-review/SKILL.md) | Pipeline and forecast roll-ups by stage, owner, or period | Read | `getUserInfo` |
| [open-risks-and-blockers](skills/open-risks-and-blockers/SKILL.md) | Stalled deals, past-due dates, overdue tasks, escalations | Read | `getUserInfo` |
| [next-best-action](skills/next-best-action/SKILL.md) | Evidence-based next move on a deal | Read | — |
| [find-contacts](skills/find-contacts/SKILL.md) | Locate contacts and leads, with deal context | Read | — |
| [review-tasks](skills/review-tasks/SKILL.md) | Triage open activities and upcoming meetings | Read | `getUserInfo` |
| [lead-followup](skills/lead-followup/SKILL.md) | Untouched and aging lead triage | Read | `getUserInfo` |
| [case-triage](skills/case-triage/SKILL.md) | Open and escalated cases, correlated with revenue | Read | `getUserInfo` |
| [explore-salesforce-data](skills/explore-salesforce-data/SKILL.md) | Schema discovery and ad-hoc SOQL on any object | Read | — |
| [create-account](skills/create-account/SKILL.md) | Create an account, duplicate-checked | Write | `createSobjectRecord` |
| [update-account](skills/update-account/SKILL.md) | Edit account fields | Write | `updateSobjectRecord` |
| [create-opportunity](skills/create-opportunity/SKILL.md) | Create a deal tied to an account | Write | `createSobjectRecord` |
| [update-opportunity](skills/update-opportunity/SKILL.md) | Stage, amount, close date, next step, owner | Write | `updateSobjectRecord` |
| [add-contact](skills/add-contact/SKILL.md) | Create a contact and link it to a deal | Write | `createSobjectRecord` |
| [update-contact](skills/update-contact/SKILL.md) | Edit contact fields and deal roles | Write | `updateSobjectRecord` |
| [log-call-notes](skills/log-call-notes/SKILL.md) | Log calls and meetings, create follow-ups | Write | `createSobjectRecord` |
| [delete-salesforce-record](skills/delete-salesforce-record/SKILL.md) | Guarded deletion with cascade disclosure | Write | `deleteSobjectRecord` |
| [improve-skills](skills/improve-skills/SKILL.md) | Capture skill misfires and report improvement insights | Meta | — |

Skills marked "—" need only `soqlQuery`, `find`, and `getObjectSchema`, which
every sObject server provides. `improve-skills` needs no connector tools at all,
so it ships on every server.

Every write skill shows a before/after diff and requires explicit confirmation
before calling a mutation tool. `delete-salesforce-record` additionally requires
confirmation by record name and Id, and discloses what the cascade will remove.

Two reference documents carry the detail that would otherwise bloat every skill:

- [`skills/account-briefing/references/crm-object-reference.md`](skills/account-briefing/references/crm-object-reference.md) — field API names, picklist values, Id prefixes, required fields
- [`skills/explore-salesforce-data/references/soql-cookbook.md`](skills/explore-salesforce-data/references/soql-cookbook.md) — SOQL/SOSL syntax, aggregates, relationships, escaping, error codes, limits

## Authentication

Salesforce Hosted MCP uses OAuth 2.0 authorization code with PKCE, per user.
Three facts drive the plugin's configuration:

- **Dynamic client registration is not supported.** Salesforce states this
  explicitly, so the connector cannot omit `authorization` and let Cowork
  self-register — even though Copilot itself supports DCR for MCP plugins. An
  auth config must be created up front in the Microsoft Enterprise token store,
  which is why `authorization.type` is `OAuthPluginVault` with a `referenceId`.
- **Connected Apps are not supported.** Authentication requires an
  **External Client App** (ECA) in the Salesforce org, with the `mcp_api` and
  `refresh_token` scopes and JWT-based access tokens for named users.
- **The redirect URI is fixed.** Register
  `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` as the ECA
  callback URL. It is identical for every Copilot plugin and provider — Teams
  receives the authorization response there and exchanges the code for tokens.

| Setting | Production | Sandbox / scratch |
|---|---|---|
| Authorization URL | `https://login.salesforce.com/services/oauth2/authorize` | `https://test.salesforce.com/services/oauth2/authorize` |
| Token URL | `https://login.salesforce.com/services/oauth2/token` | `https://test.salesforce.com/services/oauth2/token` |
| Refresh URL | `https://login.salesforce.com/services/oauth2/token` | `https://test.salesforce.com/services/oauth2/token` |
| Scopes | `mcp_api refresh_token` | same |
| PKCE | Required (S256) | same |
| MCP URL | `https://api.salesforce.com/platform/mcp/v1/<SERVER-NAME>` | `https://api.salesforce.com/platform/mcp/v1/sandbox/<SERVER-NAME>` |

Every action runs as the signed-in Salesforce user. The plugin has no service
account and cannot exceed that user's profile, sharing rules, or field-level
security.

## Deploy

1. **In Salesforce:** create an External Client App with the callback URL
   `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`, and enable the
   MCP server. Full steps in [SETUP-CHECKLIST.md](SETUP-CHECKLIST.md).
2. **In Microsoft 365:** create the OAuth auth config. Use the
   [Teams developer portal](https://dev.teams.microsoft.com/tools) →
   **Tools** → **OAuth client registration** (Microsoft 365 Agents Toolkit and
   the declarative agent developer skill also work, and write the manifest for
   you). Supply the ECA consumer key and secret, the Salesforce authorization,
   token, and refresh endpoints, scopes `mcp_api refresh_token`, and enable
   PKCE. Set **Base URL** to the same value as `mcpServerUrl`. Capture the
   generated **auth config ID**.
3. **Configure the plugin** for your chosen server and auth config:

   ```powershell
   cd "Cowork Plugins/Salesforce Hosted MCP"
   ./configure.ps1 -Server sobject-all -ReferenceId "<auth-config-id>"
   ```

4. **Validate and package:**

   ```powershell
   ./preflight.ps1
   ./package.ps1
   ```

5. Upload `Salesforce Hosted MCP.zip` in the Microsoft 365 Admin Center,
   publish to test users, then connect in a fresh Cowork session.

`preflight.ps1 -AllowPlaceholders` downgrades unresolved placeholders to
warnings while you are still developing.

## Which plugin should I use

| | Hosted MCP (this plugin) | [Self-hosted](../Salesforce/readme.md) |
|---|---|---|
| Infrastructure | None | Azure Container App, App Insights, Bicep |
| Time to first call | Salesforce Setup only | `azd up` plus Salesforce Setup |
| Tool surface | Generic sObject tools, all objects | 16 purpose-built CRM tools |
| Custom logic and shaping | In skills only | In C# server code |
| Telemetry | Salesforce-side | Application Insights, fully controlled |
| Custom objects | Work immediately | Require server changes |
| Read-only variant | `configure.ps1 -Server sobject-reads` | Second `/mcp/federated` endpoint |
| Ongoing maintenance | Salesforce's | Yours |

Choose hosted MCP when you want breadth and zero infrastructure. Choose
self-hosted when you need response shaping, custom telemetry, request-level
policy, or tool definitions that hide Salesforce's data model from the agent.

## Manifest schema

This plugin uses the `vDevPreview` Teams manifest schema
(`manifestVersion: "devPreview"`), not `v1.28`. In Cowork's current runtime,
only the devPreview path binds the MCP connector — a `v1.28` manifest loads the
skills but silently drops the connector, so the agent never invokes any tools.
devPreview also requires `packageName` in reverse-DNS form and omits
`mcpToolDescription`; Cowork discovers tools dynamically via MCP `tools/list`.

## Known constraints

- **Lead conversion is unavailable** on the standard sObject servers — they
  expose no lead-convert operation. An admin can expose it as a Flow or
  `@InvocableMethod` on a [custom server](#custom-servers); otherwise handle
  conversion in Salesforce.
- **Record merge is unavailable** for the same reason, with the same workaround.
- **Products and price books** need a `Pricebook2Id` on the opportunity before
  `OpportunityLineItem` records can be created.
- **SOQL returns at most 50,000 records** per transaction; SOSL `find` returns
  at most 2,000.
- **Deletions are recoverable for 15 days** from the Salesforce Recycle Bin.
  There is no undelete tool on the connector.
- **The External Client App can take up to 30 minutes** to become operational
  after creation.
- **Custom picklist values are the norm.** Skills call `getObjectSchema` before
  writing picklist fields rather than assuming Salesforce defaults.

# Graph Mission Control

A custom MCP connector that exposes **all of Microsoft Graph through three operations** instead of hundreds of typed actions.

Built on the [Power Mission Control Template](https://github.com/troystaylor/SharingIsCaring/blob/main/Connector-Code/Power%20Mission%20Control%20Template/readme.md). Read that first for the protocol and framework reference — this document covers what is specific to Graph.

## Why three operations

Registering Graph as typed actions means hundreds of tools, and a planner pays for every one of them in its context window before it does any work.

| Approach | tools/list cost |
|---|---|
| One typed tool per Graph operation | ~500 tokens × N |
| **Graph Mission Control** | **~1,500 tokens, fixed** |

The planner calls `scan_graph` to find what it needs, then `launch_graph` to run it. Operation schemas are fetched on demand, never injected up front.

## The three tools

| Tool | Purpose |
|---|---|
| `scan_graph` | Find Graph operations matching an intent. Returns endpoint, method, and parameters. |
| `launch_graph` | Execute one Graph v1.0 call. |
| `sequence_graph` | Execute up to 20 calls in a single round trip via Graph `$batch`. |

## Resources and prompts

The connector head also serves two MCP resources and two prompts. Neither is required to use it, and **no Microsoft surface reads them today** — Copilot Studio exposes resources only as the output of a tool, and federated connectors are tools-only. They exist for spec-compliant clients, and because they cost nothing when unused.

| Resource | Contents |
|---|---|
| `graph://capabilities` | The whole index — every operation with endpoint, method, domain, and parameters |
| `graph://capabilities/{domain}` | One area: `mail`, `calendar`, `files`, `people`, `insights`, `teams`, `sites`, `groups`, `tasks`, `search` |

This is the one thing `scan_graph` cannot do. Scan returns the five best matches for an intent; a client that supports resources can read the entire surface and decide for itself.

| Prompt | Argument | Produces |
|---|---|---|
| `find_and_read` | `intent` | scan, then launch, with `$select`/`$filter` guidance |
| `batch_related_reads` | `intent` | scan, then one `sequence_graph` instead of N launches |

Both restate what the `initialize` instructions already tell the model, as something a person can pick from a menu instead.

The domain resource filters the raw index rather than the parsed `CapabilityEntry`, so entries keep their original shape including `schemaJson`.

## Dual-mode

One connector, two consumers:

- **Copilot Studio** — the MCP endpoint at `/mcp`. Requires [generative orchestration](https://learn.microsoft.com/microsoft-copilot-studio/advanced-generative-actions).
- **Power Automate** — the typed `ScanGraph`, `LaunchGraph`, and `SequenceGraph` actions, with full schemas and dynamic content.

Both route through the same capability index and the same proxy, so behavior cannot drift between them.

## Prerequisites

The two heads are independent. Deploy either without the other.

| | Connector head | Federated head |
|---|---|---|
| Tooling | `pac` CLI | `azd`, Azure CLI, .NET 10 SDK |
| Hosting | None — runs inside Power Platform | An Azure subscription |
| Entra rights | Create an app registration, grant admin consent | Same, plus a federated credential |
| Admin role | Power Platform environment maker | Global Administrator or AI Administrator, to register the connector |
| Per-user licence | Power Automate or Copilot Studio — custom connectors are a premium capability | **Microsoft 365 Copilot**, for every user who queries it |

The M365 Copilot requirement is the one worth checking first. It is a flat per-user add-on rather than a per-call meter, so query volume is free, but a tenant without it cannot use the federated head at all.

### What the federated head costs to run

The connector head has no infrastructure. The federated head provisions five billable resources, and two of them bill whether or not anyone uses it:

| Resource | Notes |
|---|---|
| Container App | 0.5 vCPU / 1 GiB, `minReplicas: 1` — **never scales to zero** |
| Container Registry | Basic tier, flat monthly |
| Log Analytics | Pay-per-GB ingested, 30-day retention |
| Application Insights | Workspace-based, bills through the workspace above |
| Managed identity | Free |

`minReplicas: 1` is deliberate — a cold start would land inside Copilot's request timeout. Set it to `0` in `infra/resources.bicep` if you would rather pay less than answer promptly. Check current rates in the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) rather than trusting a figure written here; run `azd down` when you are finished evaluating.

## Capability index

`graph-capability-index.json` is the source of truth: **82 Graph v1.0 operations**, 50 read and 32 write.

| Domain | Operations |
|---|---|
| mail | 14 |
| files | 13 |
| calendar | 12 |
| teams | 12 |
| people | 8 |
| sites | 8 |
| tasks | 8 |
| groups | 3 |
| insights | 3 |
| search | 1 |

Each entry carries a `readOnly` flag. This connector ignores it — Copilot Studio and Power Automate get the full surface. It exists for consumers that must expose reads only.

**`readOnly` is not derivable from the HTTP verb.** Three operations are `POST` but semantically reads:

```
POST /me/findMeetingTimes
POST /me/calendar/getSchedule
POST /search/query
```

Any consumer filtering for reads must read the flag, never infer from the method. `/search/query` in particular is the highest-value read in the whole index — a verb-based filter would drop it.

### Editing the index

Edit `graph-capability-index.json`, then re-embed it into `script.csx`. The `CAPABILITY_INDEX` constant is a C# verbatim string, so every `"` becomes `""`. Do that mechanically rather than by hand:

```powershell
$json = Get-Content .\graph-capability-index.json -Raw
$escaped = $json.TrimEnd() -replace '"', '""'
```

Then replace the body of `CAPABILITY_INDEX` in `script.csx` with `$escaped`.

To add operations, use the template's `generate-capability-index.prompt.md` and add the `readOnly` field to each generated entry.

## Batching

`sequence_graph` maps onto the native Graph `$batch` endpoint rather than issuing calls one at a time. Twenty operations cost one round trip.

Two constraints follow from Graph's implementation:

- **20 requests maximum.** `MaxBatchSize` matches it.
- **Sub-request URLs must be version-relative** — `/me/messages`, not `/v1.0/me/messages`. This is why `DefaultApiVersion` is left unset; the version lives in `BaseApiUrl` only. Setting both would prefix every sub-request URL and Graph would reject the batch.

A partial failure is not a failure: individual requests can fail while the sequence returns 200, with per-request `status` and `success`.

## Discovery mode

`DiscoveryMode.Static` — the embedded index, no external calls.

Graph has no per-resource describe endpoint (`$metadata` is one monolithic CSDL document), so `Hybrid` has nothing to call and is not usable here.

To reach past the 82 embedded operations, switch to `McpChain` against MS Learn, which covers all of Graph at the cost of an external call on every scan:

```csharp
DiscoveryMode = DiscoveryMode.McpChain,
McpChainEndpoint = "https://learn.microsoft.com/api/mcp",
McpChainToolName = "microsoft_docs_search",
McpChainQueryPrefix = "Microsoft Graph",
```

The modes are exclusive, not additive — `McpChain` replaces index search rather than supplementing it.

## Authentication

Entra ID with on-behalf-of, so calls run as the signed-in user and Graph enforces that user's own permissions.

Graph already publishes its own app registration and scopes, so you only need **one** app registration — the connector app.

### 1. Create the connector app registration

In the [Microsoft Entra admin center](https://entra.microsoft.com/), create an app registration and add the delegated Microsoft Graph permissions your scenarios need. Grant admin consent.

Effective permissions come from what is consented on this app. The `aad` identity provider hardcodes `scope=openid` in the on-behalf-of exchange and has no scopes parameter, so pre-consented delegated permissions are what actually govern access.

Start narrow. `User.Read`, `Mail.Read`, and `Calendars.Read` cover most read scenarios; add write scopes only for the operations you intend to use.

### 2. Avoid the per-call sign-in prompt

On the same app registration, go to **Expose an API**, add a scope named `access_as_user`, then under **Authorized client applications** add:

```
fe053c5f-3692-4f14-aef2-ee34fc081cae
```

That is the Azure API Connections service principal. Without it, users are prompted to sign in on **every** call. A tenant admin may need to provision that service principal first.

### 3. Set the client secret and redirect URI

Add a client secret.

The redirect URI is necessarily a **second pass**, because `apiProperties.json` sets `redirectMode: GlobalPerConnector`. That mints a URI carrying the connector's own name and a hash of its id:

```
https://global.consent.azure-apim.net/redirect/new-5fgraph-20mission-20control-5f<hash>
```

It is unknowable until the connector exists — note the URL-encoded name, where `-5f` is `_` and `-20` is a space. So: deploy first, read the exact value off the connector's **Security** tab, then add it to the app registration as a **Web** platform redirect URI.

Until you do, every connection attempt fails with `AADSTS50011: The redirect URI ... does not match the redirect URIs configured for the application`. Adding the generic `https://global.consent.azure-apim.net/redirect` does **not** satisfy it — keep both, since the generic one covers other connectors sharing this app registration.

### Diagnosing permission failures

The two auth failures mean different things and are reported separately rather than as a generic "access denied":

| Status | Meaning | Fix |
|---|---|---|
| **401** | Graph rejected the token itself — expired, or wrong audience | Reconnect. If it persists, check `resourceUri` is `https://graph.microsoft.com`. |
| **403** | Token is valid, a delegated permission is missing | Graph names the permission in the error. Add it to the connector app registration and grant admin consent. |

Graph's own error code and message are passed through, so a 403 tells you *which* permission to add. Under on-behalf-of this is the most common failure, because effective permissions come from what is consented on the connector app rather than from a scopes parameter.

### Known limitation

On-behalf-of is designed for conversational consent. A Power Automate connection is established once by its owner at design time, which is a different model. Test your flows before relying on OBO there.

## Deploy

```powershell
pac auth create --environment <ENVIRONMENT_ID>

pac connector create --environment <ENVIRONMENT_ID> `
  --api-definition-file "apiDefinition.swagger.json" `
  --api-properties-file "apiProperties.json" `
  --script-file "script.csx"
```

Pass `--environment` explicitly even though `pac auth create` already named one. Relying on the auth profile alone fails with the same opaque `An unexpected error occurred` that the OAuth bug below produced, which sends you chasing the wrong problem.

Then configure OAuth on the connector's **Security** tab in the portal: identity provider Azure Active Directory, plus the client ID and secret from your app registration.

Earlier guidance here said to omit `apiProperties.json`, because PAC CLI 2.8.1 failed with "An unexpected error occurred" whenever OAuth `connectionParameters` were present. **That is fixed as of 2.9.3** — pushing all three files in one command works, and the two-step workaround is no longer needed.

`script.csx` is compiled by the platform at deploy time, not locally. Neither `ppcv` nor a local build will catch an error in it, so a real `pac connector create` is the only validation.

`ppcv` also reports a failure that isn't one: `Unbalanced braces (depth: 3)`. Its brace counter is naive, and the framework contains three lone `"{"` string literals. The count will never balance and the script compiles fine — a successful deploy is the authority, not `ppcv`.

One trap inherited from the template: its `GetConnectionParameter` helper **does not compile**. `IScriptContext` exposes only `CorrelationId`, `OperationId`, `Request`, `Logger`, and `SendAsync` — there is no `ConnectionParameters` member, so deploying fails with `CS1061`. The `try/catch` around it is no help, because the failure is at compile time rather than runtime. It is dead code here and has been removed. To read a connection value inside `script.csx`, inject it as a request header with a `setheader` policy and strip the header before forwarding.

To update an existing connector:

```powershell
pac connector update --environment <ENVIRONMENT_ID> --connector-id <GUID> `
  --api-definition-file "apiDefinition.swagger.json" `
  --script-file "script.csx"
```

If `create` fails with a duplicate key error on `ndx_connector_name`, the connector already exists — use `update` with its ID.

## The federated head

`Graph Mission Control MCP/` is a second, separate deployment: a public HTTPS MCP server that surfaces the same Graph operations inside **Microsoft 365 Copilot** as a federated connector.

It exists because federated connectors require a public MCP endpoint. A Power Platform custom connector is not one, so this cannot be served from the connector above.

### What it adds over Copilot on its own

These are not competitors on the same axis. Microsoft 365 Copilot reasons over a **semantic index** of your content; this server makes **live, deterministic Graph reads**. Where they overlap, Copilot on its own is usually better — an index plus a model beats fifty REST endpoints at summarising, fuzzy intent, and cross-document reasoning. This server returns JSON; it does not reason.

The value is in the parts that do not overlap. Roughly 30 of the 50 read-only capabilities have no native equivalent, because they are configuration, structure, and computation rather than content:

| Ask | Capability | Why the index cannot answer it |
|---|---|---|
| Working hours, time zone, auto-reply | `get_mailbox_settings` | Mailbox configuration, not content |
| When are we both free? | `get_free_busy_schedule` | Live availability computation |
| Find a slot for these five people | `find_meeting_times` | Graph-side scheduling algorithm |
| Who reports to whom | `get_manager`, `list_direct_reports`, `list_user_direct_reports` | Directory graph traversal |
| My Planner and To Do tasks | `list_my_planner_tasks`, `list_todo_tasks`, `list_plan_tasks` | Separate task stores |
| Rows in a SharePoint list | `list_site_lists`, `list_list_items` | Structured list items, not documents |
| Meeting transcript text | `list_online_meeting_transcripts`, `get_transcript_content` | Explicit transcript fetch |
| What is trending around me | `list_trending_documents`, `list_used_documents` | Graph insights signals |
| Group membership | `list_group_members`, `get_group` | Directory objects |

Three structural differences beyond the capability list:

- **Freshness.** Reads hit Graph at request time. The semantic index has ingestion lag, so "did the contract arrive yet" is answered correctly here and possibly not there.
- **Determinism.** `$filter`, `$select`, `$orderby`, `$top` and `@odata.nextLink` paging return complete, exactly-specified result sets. The index returns relevance-ranked top-N. "All 47 unread messages from finance, oldest first" is a query, not a search.
- **Auditability.** Every call lands in Application Insights with its exact Graph path and result code, so "did it actually run" is answerable. With native grounding it is not — which is why a connector that is merely *not selected* is indistinguishable from one that is broken.

The honest counterweight: Copilot on its own needs no Entra app, no on-behalf-of chain, no container, and no per-user connection step. For anything inside the overlap this is more moving parts for a worse answer. The case rests on the non-overlapping capabilities, the freshness and determinism properties, and the fact that the same capability index also drives the Power Automate connector, where none of Copilot's grounding exists at all.

### Read-only, by construction

Federated connectors are read-only by contract — Microsoft enables only search and fetch operations, and each tool must carry the `readOnlyHint` annotation.

That annotation is checked at registration time and never enforced at runtime, so the server enforces it itself, in two independent layers:

| Layer | Guarantee |
|---|---|
| `fetch_work` only ever issues `GET` | No request can mutate anything |
| Path must match a `readOnly: true` index entry | Reads stay inside the approved surface |

Both are necessary. `/me/messages` and `/chats/{chatId}/messages` each have a read **and** a write at the same path, distinguished only by method — the path guard allows them and the GET-only rule blocks the write. Conversely, GET alone would happily read `/servicePrincipals` and `/auditLogs/signIns`; the path guard rejects those.

`ToolRegistry.Add` throws if a tool declares `readOnlyHint: false`, so a violation fails startup rather than a request.

### Tools

Two, not three. There is no `launch` equivalent, because launch can write.

| Tool | Purpose |
|---|---|
| `search_work` | Search across mail, files, events, Teams messages, people, and sites |
| `fetch_work` | Read one Graph resource by path |

### Search is split across Graph's entity-type groups

Graph rejects most cross-type searches outright with `Invalid entity type combination`. Only certain types may share a single `/search/query` request:

| Group | Types |
|---|---|
| Files | `drive`, `driveItem`, `list`, `listItem`, `site`, `externalItem` |
| Mail | `message` |
| Calendar | `event` |
| Teams | `chatMessage` |
| People | `person` |

`search_work` buckets the requested sources by group, issues one search per group, and merges the hits. A failure in one bucket is reported alongside the results that succeeded rather than discarding them.

This is not a theoretical constraint. The tool's own defaults are mail + files + events, which is an illegal combination, so *every* default call failed until the bucketing was added.

### Paging and throttling

`fetch_work` accepts an `@odata.nextLink` in `path` as well as a relative path, so collections can be read past the first page. A continuation is passed through untouched — re-applying `$top` or `$select` would corrupt it. `search_work` takes a `from` offset for the same purpose.

Only `https://graph.microsoft.com/v1.0/...` is accepted as an absolute URL. Anything else is refused: following an arbitrary absolute URL would turn the tool into an open proxy for whatever host the caller names.

Graph throttles per user and per app. Transient responses (429, 503, 504) are retried up to three times, honoring `Retry-After` when it is short enough to be worth waiting for. A longer `Retry-After` is surfaced as an error instead — sleeping past the caller's own budget would hold the request open only to time out anyway.

### Discovery goes through the tools

Federated connectors expose **tools only**. Microsoft's documentation describes tool availability exclusively, and MCP resources and prompts are never mentioned for this surface. Copilot Studio does support resources, but even there the server owner "needs to configure the resource as an output of one of the MCP tools" — so resources are never independently browsable on any Microsoft surface.

That leaves the tools as the only discovery channel. Fifty readable operations would otherwise be reachable but invisible, so:

- `fetch_work` returns the full catalog of readable paths, grouped by domain, whenever it is given a path it doesn't recognise. A dead end becomes self-correction on the first miss.
- The tool description and the `initialize` instructions name the domains, both derived from the index rather than hardcoded, so they can't drift from it.

The catalog is roughly 1.3 KB for 50 operations and is produced only on a miss, so it costs nothing in the `tools/list` payload a planner loads every session.

## Deploying the federated head

`azd up` provisions the Container App, registry, log workspace, Application Insights, and user-assigned identity, then builds and pushes the image.

```powershell
azd env new <ENV_NAME> --location <REGION> --subscription <SUBSCRIPTION_ID>
azd env set ENTRA_TENANT_ID <TENANT_ID>
azd env set ENTRA_CLIENT_ID <SERVER_APP_CLIENT_ID>
azd up
```

Re-provisioning is non-destructive: `infra/fetch-container-image.bicep` reads the running image back so a bare `azd provision` cannot revert the app to the placeholder. That read has to stay in its own module — done inline it makes the app depend on itself and ARM rejects the template with a circular dependency.

Do not pipe `azd up` through `Select-Object` or anything else that buffers. It kills the process mid-build, which looks like an early return but provisions nothing. Redirect to a file instead.

### No client secret

The server authenticates to Entra with **workload identity federation**, using the same user-assigned identity that pulls the image:

```
AzureAd__ClientCredentials__0__SourceType              = SignedAssertionFromManagedIdentity
AzureAd__ClientCredentials__0__ManagedIdentityClientId = <identity client ID>
```

That requires a federated credential on the server app registration:

| Field | Value |
|---|---|
| Issuer | `https://login.microsoftonline.com/<TENANT_ID>/v2.0` |
| Subject | The identity's **principal** ID — not its client ID |
| Audience | `api://AzureADTokenExchange` |

Entra accepts a federated credential with a wrong issuer, subject, or audience **without any error**. It fails later, at token exchange. Verify it by making a real call, never by the credential saving successfully.

### Registering as a federated connector

Each querying user needs a Microsoft 365 Copilot licence — a flat per-user licence, not a per-call meter.

1. **Teams Developer Portal** → **Tools** → **Microsoft Entra SSO client ID registration**. Supply the server app's client ID, the base URL, and the scope. It returns an **SSO registration ID** and an **Application ID URI**. There is no API for this step.

   The scope must be **fully qualified** — `api://<Application ID URI>/mcp.access`, not the bare `mcp.access`. A bare scope name has no resource attached, so Entra resolves it against its default resource, Microsoft Graph, and consent fails with `AADSTS650053: ... scope 'mcp.access' that doesn't exist on the resource '00000003-0000-0000-c000-000000000000'`. Graph is correct; it has no such scope. The error surfaces as `error=invalid_client` on the Teams consent redirect.
2. On the server app registration: add that Application ID URI to `identifierUris`, add `https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect` as a **Web** redirect URI, and pre-authorize `ab3be6b7-f5df-413d-ac2d-abf1e3fd9c0b` (the Microsoft Enterprise token store).
3. `azd env set ENTRA_EXTRA_AUDIENCES "<Application ID URI>"` and redeploy. Copilot's token carries *that* audience rather than the app's own `api://` URI, so without this every Copilot call is rejected while direct clients keep working.
4. **M365 admin center** → **Copilot** → **Connectors** → **Gallery** → **Created by your org** → **Connect to MCP server**. Supply the display name, base URL, and SSO registration ID. **Display name is capped at 30 characters** — undocumented, and the only field on the form short enough to hit it.
5. Each user then connects it themselves in **Copilot Chat** → **Settings** → **Sources**. Admin rollout only makes the connector available; until a user creates the connection, prompts are answered from Copilot's native M365 data and the connector is never consulted — which looks like a broken connector but is just an unconnected one.

All four Entra-side prerequisites are readable in one call, which is the fastest way to tell whether a consent failure is Entra's fault or the portal registration's:

```powershell
az ad app show --id <serverAppId> --query "{uris:identifierUris,scopes:api.oauth2PermissionScopes[].value,redirects:web.redirectUris,preAuth:api.preAuthorizedApplications[].appId}"
```

Changes take up to 15 minutes to take effect, so an immediate failure is not meaningful. Use staged rollout to pilot before deploying to everyone.

### Checking the audience without signing in

A wrong audience fails only on the first real Copilot call, which reads as a Copilot fault rather than a misconfiguration here. The server logs the audiences it actually validates against at startup:

```
accepted token audiences: api://<clientId> | api://auth-<registrationGuid>/<clientId>
```

That value is read back from the token validation parameters *after* configuration runs, so it confirms the environment variable was applied rather than merely set.

Don't try to infer this by sending a token with the wrong audience — ASP.NET Core returns only `The audience '(null)' is invalid` and never lists the accepted ones.

Two things that look broken but are not:

- The Entra portal displays only the *first* entry in `identifierUris`. Both are stored — set them with `az ad app update --identifier-uris "<uri1>" "<uri2>"` and confirm with `az ad app show`.
- `PATCH`ing the `api` property replaces it wholesale, so existing `oauth2PermissionScopes` must be resent in the same request or they are silently dropped.

### Why the advertised resource comes from configuration

The Container Apps ingress terminates TLS and forwards plain HTTP, so `Request.Scheme` is `http` inside the container and `/.well-known/oauth-protected-resource` advertised an `http://` resource identifier that no client could match.

`UseForwardedHeaders` does **not** fix this here — it was tried, correctly ordered, against a revision serving all traffic, and had no effect. The deployed URL is supplied explicitly instead, as `Mcp__PublicUrl`, built in Bicep from the environment's domain rather than the app's own ingress FQDN, which would be a self-reference.

That is also the safer design: deriving the identifier from the request would let a caller change it through the `Host` header.

### Authentication

Different from the connector. The inbound token is issued for *this server*, so it cannot be forwarded to Graph — the server performs the on-behalf-of exchange itself. Every call stays bounded by the calling user's own permissions.

Entra SSO is what the registration steps above use, and it works **within a single tenant**. It fails cross-tenant with `AADSTS90009` — the app ends up requesting a token for itself, which needs a GUID-based resource. To publish this connector to other tenants, register it with OAuth 2.0 and a dedicated client app instead.

`/.well-known/oauth-protected-resource` is served anonymously so MCP clients can discover the auth configuration instead of being configured by hand.

### Deploy

```powershell
azd auth login
azd env new graph-mission-control
azd env set ENTRA_TENANT_ID  <tenant-guid>
azd env set ENTRA_CLIENT_ID  <server-app-client-id>
azd up
```

Three things in the infrastructure are deliberate and easy to break:

- **User-assigned managed identity for AcrPull.** A system-assigned identity does not exist until the app does, so it cannot be granted pull rights beforehand — which deadlocks the first provision.
- **Probes follow the image, not a flag.** Until a real image is present the app gets a TCP readiness probe on 8080, because the placeholder serves nothing on `/health` and an HTTP probe would stop the first revision reaching Ready. Once the deployed image is read back, readiness and liveness both move to `/health`.
- **`environmentName` must stay stable.** The Container Apps environment derives the public FQDN suffix from it, so changing it changes the MCP endpoint URL and breaks a registered connector.

After `azd deploy` reports success the old revision can still serve traffic for a few seconds. Re-probe before concluding the deploy failed.

### Telemetry

Application Insights is workspace-based with `DisableLocalAuth`, so the ingestion key in the connection string is inert and the server publishes with the same managed identity it uses for everything else (**Monitoring Metrics Publisher** on the component). `/health` is excluded from request tracing — the two probes generate roughly 120 hits every 20 minutes and would otherwise bury real traffic.

`az monitor app-insights component show` misreports this component as unconfigured (`disableLocalAuth: null`, no workspace). Read the raw properties instead, which are PascalCase:

```powershell
az resource show -g <rg> -n appi-<token> --resource-type "Microsoft.Insights/components" `
  --query "properties.{DisableLocalAuth:DisableLocalAuth,Workspace:WorkspaceResourceId}"
```

### Register the connector

Microsoft 365 admin center → **Copilot** → **Connectors** → **Gallery** → *Created by your org* → **Create a new connector** → **Connect to MCP server**.

Supply the display name (30 characters maximum), the base URL from the `MCP_ENDPOINT` output, and the OAuth registration ID from the Teams Developer Portal. Requires Global Administrator or AI Administrator. Users then connect it in Copilot Chat under **Settings** → **Sources**.

Every user who queries the connector needs a Microsoft 365 Copilot add-on license. That is a flat license rather than a per-call meter, so query volume costs nothing extra.

## Files

Grouped by which head owns them. The layout is deliberate: the index sits at the root because both heads compile against it, and the .NET project reaches it with a `..\` link that the Docker build context (`context: ../`) is widened to include.

**Shared**

| File | Purpose |
|---|---|
| `graph-capability-index.json` | The 82 operations. Source of truth for both heads. |
| `readme.md` | This document, covering both |

**Connector head** — Copilot Studio and Power Automate

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | MCP endpoint at `/mcp` plus three typed operations |
| `apiProperties.json` | Entra ID OAuth with on-behalf-of |
| `script.csx` | Section 1 is Graph configuration and the embedded index; Section 2 is the framework. |

**Federated head** — Microsoft 365 Copilot

| File | Purpose |
|---|---|
| `Graph Mission Control MCP/` | The .NET 10 MCP server |
| `Graph Mission Control MCP Tests/` | Read-only guard, path normalization, readable surface, search grouping |
| `azure.yaml`, `infra/` | azd and Bicep |
| `Graph Mission Control MCP/Dockerfile` | Container build |
| `.dockerignore` | Sits at the root, not beside the Dockerfile, because the build context is the root |
| `Directory.Build.props` | Redirects build output outside OneDrive |

## Tests

For maintainers, not consumers — deploying the connector or the server does not require running these.

```powershell
dotnet test "Graph Mission Control MCP Tests"
```

Four suites, each covering a place where a small edit changes behavior silently rather than loudly:

| Suite | What it protects |
|---|---|
| `ReadOnlyPathGuardTests` | The read-only guarantee — the federated head's only real safety property, and M365 never enforces it at runtime |
| `GraphPathNormalizationTests` | Relative paths, `@odata.nextLink` continuations, and the refusal of absolute URLs outside Graph v1.0 |
| `ReadableSurfaceTests` | The catalogue returned on an unrecognised path stays in sync with the index and advertises nothing unreadable |
| `SearchGroupingTests` | Entity-type bucketing, without which every default search fails |

One case exists to stop a plausible "fix": `/me/messages` and `/chats/{chatId}/messages` each have a read **and** a write at the same path. Tightening the path guard to reject them would break reading mail without making anything safer — the GET-only rule is what blocks the write.

Another exists to stop a plausible simplification: `LeadingSlashIsNeverTreatedAsAnAbsoluteUrl` and `QueryValueContainingASchemeIsNotTreatedAsAbsolute` both guard against detecting absolute URLs with a naive check. A path beginning `//` or a `$filter` value containing `https:` would each slip through one.

Build output is redirected to `%LOCALAPPDATA%\GraphMissionControl\build` because this repo is OneDrive-synced and OneDrive ignores `.gitignore`, thrashing continuously on `bin/` and `obj/`.

## Behavior notes

- **Pagination.** `launch_graph` surfaces `@odata.nextLink` as `nextLink`. Pass it back as the endpoint to page forward.
- **Throttling.** Graph 429s are retried up to three times, honoring `Retry-After`.
- **Directory search.** Graph rejects `$search` and `$count` on `/users` and `/groups` unless the request carries `ConsistencyLevel: eventual`, and `$search` additionally requires `$count=true`. Both are added automatically. Mail and file `$search` use different syntax and need neither.
- **Response size.** Mail bodies and Teams messages are HTML and can be very large. Summarization strips markup and truncates at 500 characters for bodies, 1000 for text. Set `SummarizeResponses = false` to disable.
- **Default page size.** Graph defaults to 10 and allows up to 999. This connector injects `$top=25` on collection reads when no page size is given.

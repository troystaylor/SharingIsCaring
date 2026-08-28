# MongoDB → Microsoft 365 Copilot: Design

**Status:** Implemented — logic verified by tests; deployment steps need a customer tenant
**Author:** Troy Taylor
**Last updated:** 2026-08-28

Design for surfacing MongoDB data in Microsoft 365 Copilot through two complementary
heads — a **federated Copilot connector** (real-time MCP) and a **Microsoft 365 Copilot
connector** (Graph indexing) — sharing a single data-access and mapping core.

---

## 1. Problem

MongoDB holds a mix of content-like records (articles, knowledge base, support tickets,
product catalog) and operational records (orders, events, telemetry). Both need to reach
Copilot, but they have opposite access profiles:

- Content-like data is relatively stable and needs **discovery and citation** — users ask
  open questions and expect Copilot to find and link the source.
- Operational data is volatile and high-cardinality, and is queried **by key** — "what's
  the status of order 4417" — where an index would be stale the moment it was written.

No single Copilot extensibility surface serves both well.

## 2. Why not a Power Platform custom connector

The obvious first instinct — a `script.csx` custom connector, like most connectors in this
repo — does not work for MongoDB. Recorded here so it isn't re-litigated later.

**MongoDB has no REST API for your data.** The Atlas Data API and Custom HTTPS Endpoints
reached end-of-life on **September 30, 2025**, and MongoDB shipped no hosted replacement.
The wire protocol is TCP/BSON, and Power Platform connectors only speak HTTPS, so a
connector script cannot reach a cluster directly.

The remaining option would be a passthrough to MongoDB's hosted MCP server. Verified
against live endpoints and the `mongodb-js/mongodb-mcp-server` source:

| Concern | Value |
|---|---|
| Endpoint | `https://mcp.mongodb.com` (root path, no `/mcp`) |
| Transport | Streamable HTTP (SSE or JSON response body) |
| Auth | OAuth 2.1 — `authorization_code` + PKCE, or `client_credentials` |
| Authorization endpoint | `https://cloud.mongodb.com/oauth/authorize` |
| Token endpoint | `https://cloud.mongodb.com/api/oauth/token` |
| AS issuer | `https://authorize.mongodb.com` |
| Credentials | Atlas portal → "MCP configuration" object (Org/Project Owner) |
| Tools exposed | ~54 (26 database, 22 Atlas admin, 4 Atlas Local, 2 Assistant) |

Three problems make it a poor fit:

1. **No dynamic client registration.** `POST /register` returns 404. Atlas MCP credentials
   are minted in the portal, and `https://global.consent.azure-apim.net/redirect/...`
   cannot be registered as a redirect URI — so the `oauth2/accessCode` flow a custom
   connector needs is unavailable. Static API keys over HTTP are explicitly unsupported.
2. **Session lifecycle.** The managed server destroys idle sessions and returns 404 on the
   next POST. MongoDB's own client ships a session-recovery wrapper that re-sends
   `initialize` and replays `notifications/initialized`. A passthrough connector has
   nowhere to put that logic.
3. **Ungoverned tool surface.** `drop-database`, `drop-collection`, `delete-many`, and
   `atlas-create-cluster` are all exposed. `--readOnly` and `MDB_MCP_DISABLED_TOOLS` are
   local-server options; whether the managed server supports tool filtering is
   undocumented.

It is also **Atlas-only**, which rules out self-managed deployments.

**The unlock:** both M365 paths run our own .NET process, so we use the official
`MongoDB.Driver` package over the native wire protocol. The Data API's death, the Atlas
OAuth constraints, and session recovery all become non-issues. Self-managed MongoDB works
too, and Atlas can be reached over a private endpoint from a VNet-integrated Container App.

## 3. Decision

Build **one core with two heads**, shipped as a **reusable template**: installers point it
at their own MongoDB and describe their collections in JSON, without forking or
recompiling.

The failure mode of a hybrid is two divergent field mappings — a Graph schema in the
ingestion job and a separate projection in the MCP tools. They drift, and Copilot ends up
citing an indexed field the live tool doesn't return. A shared core makes that
structurally impossible.

```
MongoDB/
├── config/                        # collection profiles, edited per installation
├── src/Mongo.Copilot.Core/        # MongoClient, collection profiles, projection, ACL mapping
├── src/Mongo.Copilot.Federated/   # Azure Container Apps: MCP server (read-only tools)
├── src/Mongo.Copilot.Indexer/     # Always-on Container App: crawl + change stream tail
├── tests/                         # 74 tests over the safety-critical logic
└── infra/                         # Bicep + deploy script
```

### Resolved decisions

| Decision | Choice | Rationale |
|---|---|---|
| Distribution | Reusable template | Installers supply their own cluster and profiles |
| Profile definition | JSON config | No recompile; edit config and redeploy |
| ACL posture | Fail closed | Refuse to start if `Access` unset; skip unresolvable documents |
| Indexer state | Reserved `externalItem`s, deny-all ACL | No extra infrastructure (§6.1) |
| Connection topology | Configurable grouping, default one per collection | Clean per-collection schemas (§6.2) |
| Indexer hosting | Single always-on Container App | Change-stream tail needs a persistent cursor |
| Citation URLs | Install-time config | Location is a property of the environment (§4.2) |

## 4. The collection profile

The central abstraction. One profile per collection, owned by `Core`, consumed by both
heads. Defined in JSON so installers never touch C#; shown here as its C# shape for
clarity.

```csharp
new CollectionProfile("support_tickets")
{
    Mode         = ProfileMode.Indexed,     // Indexed | Live | Both
    IdField      = "_id",
    TitleField   = "subject",
    ContentField = "body",
    UrlPath      = "/ticket/{_id}",         // resolved against installer-configured base
    Properties   =
    [
        ("status",     PropType.String),
        ("priority",   PropType.String),
        ("assignedTo", PropType.String),
        ("updatedAt",  PropType.DateTime)
    ],
    Access       = AccessMapping.EntraGroupField("assignedTeamGroupId")
}
```

A single profile emits, from one definition:

- the Graph external-item **schema** and per-item **ACL** (indexer head)
- the MCP tool **projection** and mandatory **query filter** (federated head)

`Mode` routes each collection: content-like → `Indexed`, operational → `Live`, and `Both`
for anything that should be discoverable *and* freshly readable.

Note that `UrlPath` is **relative**. The profile declares the shape of a link to a
document; the installing developer supplies where that link points. See §4.2.

The installer writes this as JSON, never C#:

```json
{
  "Collections": {
    "support_tickets": {
      "Mode": "Indexed",
      "Database": "helpdesk",
      "IdField": "_id",
      "TitleField": "subject",
      "ContentField": "body",
      "UrlPath": "/ticket/{_id}",
      "Description": "Customer support tickets, including status and assignee.",
      "Properties": {
        "status":     { "Type": "String",   "IsSearchable": true, "IsQueryable": true },
        "priority":   { "Type": "String",   "IsQueryable": true },
        "assignedTo": { "Type": "String",   "Labels": [ "createdBy" ] },
        "updatedAt":  { "Type": "DateTime", "Labels": [ "lastModifiedDateTime" ] }
      },
      "Access": { "Kind": "EntraGroupField", "Field": "assignedTeamGroupId" }
    }
  }
}
```

`Description` is not decoration — it becomes the MCP tool description Copilot reads when
choosing whether to query this collection, so it should describe the data in business
terms.

### 4.1 Why `Access` is the important field

MongoDB has no concept of an Entra identity, so authorization is the hard part on both
heads — and it is the same rule enforced at two different times:

| Head | When enforced | How |
|---|---|---|
| Indexer | Index time | `externalItem.acl[]` — grant to Entra user/group IDs |
| Federated | Query time | Mandatory `$match` filter injected before every query |

Defining it once and projecting it twice is the main reason the shared core exists.
Supported mappings:

- `Everyone` — grant to all. **Governance decision, not a default.**
- `EntraGroupField` — document field holds a group identifier.
- `UpnField` — document field holds a user identifier.
- `TenantField` — document field matched against a claim on the caller's token.

**Values need resolving.** Documents hold what the source system happened to store — an
email, a mail nickname, a team display name — not Entra object IDs. `PrincipalResolver` in
the indexer resolves those through Microsoft Graph, caching hits and misses for the life of
a crawl, and passes through values that already parse as a GUID. A name matching more than
one directory object is refused rather than guessed, because an arbitrary ACL is a wrong ACL.
This is why the indexer needs `User.Read.All` and `Group.Read.All` beyond the external-item
permissions.

**Fail closed.** `Access` is required; a profile that omits it is a startup error naming
the offending collection, not a silent grant. For `Live` collections the filter derives
from claims on the caller's validated JWT, and a caller whose token lacks the claim gets
an empty result rather than an unfiltered one. For `Indexed` collections the value must
resolve to an Entra object ID at crawl time; documents whose ACL cannot be resolved are
**skipped, and any copy published by an earlier crawl is withdrawn** — leaving it would keep
the document visible under its previous audience. A wrong grant in the search index is
visible tenant-wide until corrected, which is why this side errs toward invisible.

### 4.2 Citation URLs are an install-time input

Neither head can know where a MongoDB document is viewable — that is a property of the
environment the connector is installed into, not of the data. The same `support_tickets`
collection might be fronted by a helpdesk app at one customer, a Power App at another, and
nothing at all at a third.

So the profile owns the **shape** of the link and the installer owns the **location**.
`Core` exposes a `CitationUrlResolver` that both heads call; nothing else constructs URLs.

```json
{
  "Ui": {
    "BaseUrl": "https://helpdesk.contoso.com",
    "RequireUrl": true,
    "Collections": {
      "orders":   { "BaseUrl": "https://erp.contoso.com" },
      "invoices": { "Url": "https://finance.contoso.com/view?doc={invoiceNumber}" }
    }
  }
}
```

Resolution order, first match wins:

1. `Ui:Collections:{name}:Url` — absolute template, full override for collections that
   don't fit the common base.
2. `Ui:Collections:{name}:BaseUrl` + profile `UrlPath` — per-collection host, shared path
   shape. Covers the usual case of one collection living in a different system.
3. `Ui:BaseUrl` + profile `UrlPath` — the default.
4. Unset → the collection is **uncited**.

Substitution rules:

- `{field}` interpolates from the document; dotted paths (`{customer.accountId}`) reach
  into subdocuments.
- Values are URL-encoded on substitution. `_id` renders as its 24-character ObjectId hex.
- A token that resolves to null or missing does **not** emit a broken link — that
  individual item is treated as uncited.

**Startup validation.** `Ui:RequireUrl` defaults to `true`, and the indexer refuses to
start if any `Indexed` or `Both` collection resolves to no URL. Publishing a hundred
thousand uncited items and discovering it in Copilot afterwards is the expensive failure;
failing at boot with the offending collection named is the cheap one. Setting it to `false`
is the explicit opt-in to uncited grounding.

**Config changes force a re-crawl.** Item URLs are written into the index at crawl time, so
editing `Ui:BaseUrl` later does not retroactively fix published items. The indexer stores a
hash of the resolved URL configuration in its connection state; on mismatch it schedules a
full re-crawl rather than silently serving stale links. This is the one place where an
install-time input has ongoing operational cost, and it is why the value is worth getting
right before the first crawl.

The federated head uses the same resolver to stamp a `url` into every tool response, so
live answers cite as consistently as indexed ones — and a URL config change there takes
effect immediately, with no re-crawl.

## 5. Federated head — real-time MCP

Built from the repository's federated Copilot connector template (an ASP.NET Core host using
the official MCP C# SDK), replacing that template's `IHttpClientFactory` / `Upstream.BaseUrl`
wiring with a singleton
`IMongoClient` from `Core`.

**Tools** (all read-only, all derived from `Live`/`Both` profiles):

| Tool | Purpose |
|---|---|
| `list_collections` | Collections exposed to the caller, with descriptions |
| `collection_schema` | Field names and types for a collection, to ground query construction |
| `find` | Filtered document lookup, projected per profile |
| `aggregate` | Constrained pipeline — `$match`/`$group`/`$sort`/`$limit` and similar read stages |
| `count` | Cardinality for a filter |

Every tool carries `ReadOnly = true`, which surfaces as `readOnlyHint` in `tools/list`.
Federated connector registration drops tools without it, so this is required rather than
advisory.

Tool descriptions matter more than usual: Copilot selects tools from `[Description]`
attributes, so each needs to state what the collection contains in business terms, not
schema terms.

**Auth.** Entra SSO authenticates the *user* to the MCP server via JWT bearer (already
wired in the template's `Program.cs`). The server holds the MongoDB connection string in
Key Vault, read via managed identity. This is what sidesteps the Atlas OAuth problem
entirely — Copilot never authenticates to MongoDB.

**Guardrails.** Federated read-only is **declared, not enforced** — the platform trusts
`readOnlyHint` at registration and does not block writes at runtime, as demonstrated in
[Federated Copilot connectors aren't actually read-only](https://troystaylor.com/power%20platform/mcp/2026-06-19-federated-connectors-crud-experiment.html).
The guardrail is therefore structural, not declarative:

- No mutating tool is ever written. `Core` exposes no write surface to this project.
- The `aggregate` tool allow-lists pipeline stages; `$out`, `$merge`, and `$function` are
  rejected before the pipeline reaches the driver.
- The connection string uses a MongoDB user with a **read-only role** on exactly the
  databases in scope. Defence in depth for the case where the first two fail.

**Surfaces:** Copilot Chat, Researcher, Excel agent mode. Not Copilot Studio, not M365
Search.

**Deployment:** Azure Container Apps via the template's `infra/main.bicep` and
`deploy.ps1`, then Teams Developer Portal (SSO registration) → M365 admin center
(**Copilot → Connectors → Gallery → Connect to MCP server**) → staged rollout.

## 6. Indexer head — Graph ingestion

No official or third-party MongoDB Graph connector exists; this is a full custom build.
App-only auth with `ExternalConnection.ReadWrite.OwnedBy` and
`ExternalItem.ReadWrite.OwnedBy`.

**Schema flattening is the main cost.** Graph external-item properties are flat and typed
(`String`, `Int64`, `Double`, `DateTime`, `Boolean` and their collection variants), and the
schema is registered up front and is expensive to change. MongoDB's nested subdocuments and
arrays must be projected down to that fixed shape — which fights the schemaless design and
is precisely why `Properties` is explicit in the profile rather than inferred. Semantic
labels (`title`, `url`, `lastModifiedDateTime`, `createdBy`) are populated from the profile
and materially affect grounding quality — `url` comes from the resolver in §4.2, not from
the profile directly.

**Change streams are the differentiator.** MongoDB's delta story is cleaner than most
sources get: a change stream emits insert/update/delete events that map almost one-to-one
onto `PUT /items/{id}` and `DELETE /items/{id}`. Three details to get right:

- **Resume tokens must be persisted** after each processed batch, so a restart resumes
  rather than re-crawls. Persisted to the Graph state item described in §6.1.
- **Deletes carry only `documentKey`** unless pre-images are enabled on the collection.
  That is sufficient here — `_id` is all a Graph delete needs — so pre-images are *not*
  required.
- **Resume tokens expire** once the oplog rolls over past them. On an invalid-token error,
  fall back to a full crawl. This is one of two unavoidable re-crawl paths; the other is a
  citation-URL config change (§4.2).

**Crawl model:** initial full crawl over each `Indexed`/`Both` collection, then a
long-running change stream tail. `_id` (ObjectId hex, 24 chars) maps directly to the
external item ID, which is well inside the 128-character limit.

**Limits to respect:** 4 MB per external item — oversized `ContentField` values are
truncated with a note, not dropped; Graph write throttling requires batching with retry on
429; schema registration is asynchronous and must be polled to completion before the first
item is published.

**Surfaces:** Copilot Chat grounding with citations, M365 Search, and Copilot Studio
knowledge — a materially wider reach than the federated head.

**Hosting:** a single always-on Container App (`minReplicas: 1`). The change-stream tail
holds an open cursor and reacts to events as they arrive, so it is a long-running process
rather than a scheduled batch. One hosted service performs the full crawl on first run for
a collection, then transitions to the tail.

### 6.1 State lives in Graph, not in MongoDB

The indexer must persist a resume token per collection plus a hash of the resolved URL
configuration. The obvious home — a `_copilot_state` collection in MongoDB — is exactly
what the read-only role in §5 forbids, and adding a second read-write user would undercut
the guardrail.

`externalConnection` cannot hold it either: its writable properties are `id`, `name`,
`description`, `configuration` (only `authorizedAppIds`), `state` (an enum of
`draft`/`ready`/`obsolete`/`limitExceeded`), `activitySettings`, `searchSettings`, and
`contentCategory`. There is no free-form metadata bag, and `description` is displayed in
the M365 admin center.

State is therefore written as a **reserved `externalItem`** inside the connection it
belongs to, at a well-known ID (`_mongo_copilot_state`):

- The item carries a single `stateJson` string property, added to every registered schema.
- Its ACL is a single `everyone` / **`deny`** entry, so it can never surface in search
  results for any user.
- It is fetched on startup with `GET /items/_mongo_copilot_state` and rewritten after each
  processed batch.

This keeps the deployment to two Container Apps and no storage account. The cost is one
schema property and one item against connection quota. A missing state item is simply a
first run.

### 6.2 Connection topology

A Graph connection has one flat schema shared by all its items, so unrelated collections in
one connection produce a union schema where every item leaves half its properties empty —
which degrades ranking and complicates result layouts.

Default is therefore **one connection per collection**, each with its own schema, its own
state item, and independent re-crawl. Profiles may opt into sharing by declaring a
`ConnectionGroup`; profiles in the same group are merged into one connection with a union
schema and a single state item keyed per collection. Grouping is worth it when collections
are genuinely alike and an admin would rather enable one connection than six.

Connection IDs are derived from the group or collection name, sanitized to the Graph
constraint of 3–32 alphanumeric characters.

## 7. Cross-cutting

**Citations.** Graph connectors require a `url` per item so Copilot can cite it, and MCP
tool responses carry the same link. Both come from the single resolver described in §4.2 —
profiles declare the link shape, the installer configures the location. No other code
constructs URLs.

**Secrets.** With `AuthMode: ManagedIdentity` there is no database credential anywhere in
the deployment. Otherwise the connection string is a Container App secret sourced from Key
Vault. No credentials in configuration files or images.

**Telemetry.** Application Insights on both heads per repo convention — tool invocations
and query shapes on the federated head, crawl progress, resume-token position, and item
failure counts on the indexer.

**Networking.** Both heads reach the cluster over a private endpoint where it is not
publicly exposed; the Container App is VNet-integrated and the private DNS zone is linked to
that VNet, which `mongodb+srv` requires in order to resolve to private IPs.

## 7.1 Azure-hosted MongoDB

"Hosted in Azure" resolves to three products with materially different capabilities.

| Target | Provider enum | Indexed | Live |
|---|---|---|---|
| MongoDB Atlas on Azure | `Atlas` | ✅ | ✅ |
| Azure DocumentDB (Cosmos DB for MongoDB **vCore**) | `AzureDocumentDb` | ✅ | ✅ |
| Azure Cosmos DB for MongoDB (**RU**) | `AzureCosmosRu` | ❌ | ✅ |
| Self-managed on VM or AKS | `SelfManaged` | ✅ | ✅ |

The service is detected from the connection string host at startup — no network call — in
`MongoProviderDetector`. The two Azure offerings differ only by hostname segment
(`*.mongocluster.cosmos.azure.com` versus `*.mongo.cosmos.azure.com`), and that distinction
is load-bearing.

### Why the request-unit offering cannot be indexed

Microsoft documents that its change streams support `insert`, `update`, and `replace` but
**not delete**, and do not return `operationType` or `updateDescription`. A document deleted
upstream would therefore remain in the Microsoft 365 index indefinitely, with Copilot
continuing to cite a record that no longer exists.

This is a correctness failure that degrades silently, so it is treated the same way as an
unresolvable ACL: **refuse to start** rather than build an index that rots. `ProfileStore`
raises a validation error when the host is RU-based and any collection is `Indexed` or
`Both`; `deploy.ps1` checks the same condition before building images. `Live`-only
configurations are permitted, because live queries never touch change streams.

### Passwordless authentication

Both viable targets accept an Entra token as a database credential through `MONGODB-OIDC`,
which removes the connection-string password from the deployment entirely. `Core` supplies
tokens through `AzureIdentityOidcCallback`, backed by `DefaultAzureCredential`.

| Target | Token resource |
|---|---|
| Azure DocumentDB | `https://ossrdbms-aad.database.windows.net/.default` (defaulted) |
| Atlas | Application ID URI of the app registration backing workload identity federation (**required**; no sensible default) |

Atlas additionally requires a dedicated M10+ cluster on MongoDB 7.0.11+. Validation rejects
a connection string that still contains credentials while `AuthMode` is `ManagedIdentity`,
since that combination usually means a half-finished migration.

The **Azure Native ISV integration for Atlas** is billing and control-plane only — unified
Azure invoicing and Entra SSO for the Atlas portal. It does not configure data-plane
authentication or private networking.

### DocumentDB change-stream differences

Change streams reached GA in May 2025 and emit deletes, support `fullDocument: updateLookup`,
and honour `resumeAfter`, so the indexer works unmodified in the common case. Four
divergences are handled explicitly in `TailAsync`:

1. **A 400 MB active change log** replaces the oplog, so a resume token can age out under
   sustained write load. Detection falls back to message inspection because DocumentDB does
   not document error codes for this.
2. **Failover invalidates the cursor** while leaving the token valid — reopened as a routine
   reconnect, logged at information level rather than as an error.
3. **A dropped collection surfaces as `NamespaceNotFound`** rather than a `drop` event.
4. **Multi-shard change streams are preview**, require a support request, and lose
   resumability across a topology change. Surfaced as a startup log line.

## 8. Install-time inputs and remaining unknowns

Because this ships as a template, the questions that would otherwise block design are
deferred to the installer as configuration. Collection inventory, ACL source fields,
Atlas vs self-managed, and tenancy are all expressed in `profiles.json` and `appsettings`
rather than decided here.

Inputs supplied per installation:

| Input | Where | Notes |
|---|---|---|
| `Collections` profiles | `config/profiles.json` | Mode, fields, properties, `Access`, `UrlPath`, optional `ConnectionGroup`. |
| `Ui:BaseUrl` (+ per-collection overrides) | `appsettings` / env | §4.2. Changing it after first crawl forces a re-crawl. |
| `Ui:RequireUrl` | `appsettings` / env | Defaults `true`; `false` opts into uncited grounding. |
| MongoDB connection string | Key Vault | Read-only role, scoped to in-scope databases. Atlas or self-managed. |
| Entra tenant, app client ID, audiences | `appsettings` / env | Federated head JWT validation. |
| Graph app registration + admin consent | Entra | Indexer head, app-only permissions. |
| VNet integration | Bicep parameter | Required only for Atlas private endpoints. |

Genuinely open, to revisit after first deployment:

1. **Tenant-claim naming** — `TenantField` matches a document field against a token claim,
   but the claim name is installer-specified and there is no convention worth hardcoding yet.
2. **Result layouts** — Graph supports adaptive-card display templates per connection;
   the template ships none, so results use the default layout.
3. **Manifest ceiling** — orphan reconciliation is bounded by what the 4 MB state item can
   hold (`Indexer:MaxManifestItems`, default 20,000). Larger collections need either an
   external store or a different reconciliation strategy.
4. **Full-text relevance in live tools** — `find` matches exactly; ranking would need
   `$search` against an Atlas Search index.

## 9. Build status

The template was built federated-first, because that loop is faster — it runs locally with
auth skipped in Development, so tools can be driven from MCP Inspector — and it forced `Core`
and the collection profiles into existence before the Graph schema depended on them.

Everything below is implemented. The distinction that matters for a customer is what has been
*verified* versus what needs their tenant and cluster to confirm.

| Area | State | Verified how |
|---|---|---|
| `Core`: profiles, projection, ACL mapping, `CitationUrlResolver` | Implemented | 57 unit tests |
| `Federated`: five read-only tools | Implemented | Live MCP session: `initialize` → `tools/list` → `tools/call`, `readOnlyHint` confirmed on all five |
| `PipelineGuard` | Implemented | Rejected `$out` and a nested `$function` against a running server |
| RU-based Cosmos guard | Implemented | Indexer refuses to start and exits non-zero |
| Startup validation (profiles, URLs, Graph settings) | Implemented | Confirmed by running each failure case |
| `Indexer`: connection + schema registration | Implemented | Not yet run against a tenant |
| `Indexer`: full crawl, ACLs, orphan reconciliation | Implemented | Threshold logic unit-tested; crawl not yet run against real data |
| `Indexer`: change stream tail, resume tokens | Implemented | Not yet run against a live cluster |
| Liveness probe | Implemented | `/health` returned `200 Healthy` from a running indexer |
| Bicep + deploy script | Implemented | Compiles with zero diagnostics; not yet deployed |

### Remaining validation

Three things need a real environment and should be treated as the first deployment's exit
criteria:

1. **Entra SSO end to end** — answers appearing in Copilot Chat under staged rollout.
2. **A full crawl against real data** — ACL trimming correct, citations resolving, and the
   flat schema actually fitting the documents.
3. **Change-stream behaviour on the target deployment** — an edit in MongoDB reaching Copilot,
   and a restart resuming rather than re-crawling.

## 10. References

- [Federated connectors overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/federated-connectors-overview)
- [Set up custom federated connectors](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/set-up-custom-federated-connectors)
- [Microsoft 365 Copilot connectors overview](https://learn.microsoft.com/en-us/microsoftsearch/connectors-overview)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MongoDB MCP Server docs](https://www.mongodb.com/docs/mcp-server/overview/)
- [MongoDB change streams](https://www.mongodb.com/docs/manual/changeStreams/)
- [Federated MCP template](https://troystaylor.com/power%20platform/mcp/2026-05-05-federated-mcp-template-copilot-connector.html)
- [Federated Copilot connectors aren't actually read-only](https://troystaylor.com/power%20platform/mcp/2026-06-19-federated-connectors-crud-experiment.html)

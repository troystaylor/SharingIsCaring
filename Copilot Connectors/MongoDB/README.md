# MongoDB Copilot Connectors

Bring MongoDB data into Microsoft 365 Copilot two ways from one codebase:

- a **federated Copilot connector** — an MCP server Copilot queries in real time
- a **Microsoft 365 Copilot connector** — a Graph indexer that crawls content into the
  semantic index for grounded, cited answers

Both read the same collection profiles, so an indexed field and a live field cannot drift
apart. See [design.md](./design.md) for the reasoning behind the architecture.

## Layout

```
MongoDB/
├── config/profiles.example.json          # copy to profiles.json and edit
├── src/Mongo.Copilot.Core/               # profiles, projection, citations, access rules
├── src/Mongo.Copilot.Federated/          # MCP server (Container App, scales to zero)
├── src/Mongo.Copilot.Indexer/            # crawl + change stream tail (always-on Container App)
├── tests/Mongo.Copilot.Core.Tests/       # 57 tests: access, citations, profiles, pipeline guard
├── tests/Mongo.Copilot.Indexer.Tests/    # 17 tests: heartbeat, orphan reconciliation, item IDs
└── infra/                                # Bicep + deploy script
```

## Getting started

### 1. Describe your collections

```bash
cp config/profiles.example.json config/profiles.json
```

Each collection needs a `Mode`, a `Description`, and an `Access` rule:

```json
"support_tickets": {
  "Mode": "Both",
  "Database": "helpdesk",
  "Description": "Customer support tickets, including status and assignee.",
  "TitleField": "subject",
  "ContentField": "body",
  "UrlPath": "/ticket/{_id}",
  "Properties": {
    "status":    { "Type": "String",   "IsQueryable": true, "IsRefinable": true },
    "updatedAt": { "Type": "DateTime", "Labels": [ "lastModifiedDateTime" ] }
  },
  "Access": { "Kind": "EntraGroupField", "Field": "assignedTeamGroupId" }
}
```

| Field | Notes |
|---|---|
| `Mode` | `Indexed` (crawled), `Live` (queried in real time), or `Both`. |
| `Description` | Copilot reads this to choose the tool. Describe the data in business terms. |
| `UrlPath` | Relative. Combined with `Ui:BaseUrl` at deploy time — see below. |
| `Properties` | Flat and typed; MongoDB subdocuments are reached with `"Path": "customer.email"`. |
| `Access` | **Required.** `Everyone`, `EntraGroupField`, `UpnField`, or `TenantField`. |
| `ConnectionGroup` | Optional. Collections sharing a group share one Graph connection. |

### 2. Decide where documents are viewable

Citations need a URL, and only you know where your documents live. `UrlPath` in the profile
is the link *shape*; `Ui:BaseUrl` at deploy time is the *location*. Resolution order:

1. `Ui:Collections:{name}:Url` — absolute override
2. `Ui:Collections:{name}:BaseUrl` + `UrlPath`
3. `Ui:BaseUrl` + `UrlPath`
4. nothing → the collection is uncited

If a collection has no front end, either point at a surrogate (an admin tool, a Power App)
or deploy with `-AllowUncitedItems`. **Changing the base URL after the first crawl forces a
full re-crawl**, because item URLs are written into the index at crawl time — the indexer
detects this and re-crawls automatically.

### 3. Deploy

```powershell
cd infra
./deploy.ps1 `
    -ResourceGroup rg-mongo-copilot `
    -RegistryName mongocopilotacr `
    -TenantId <tenant-id> `
    -AppClientId <app-registration-client-id> `
    -MongoConnectionString 'mongodb+srv://reader:...@cluster.mongodb.net' `
    -MongoDatabase helpdesk `
    -UiBaseUrl https://helpdesk.contoso.com
```

Then complete the manual steps the script prints, which it tailors to the auth mode you chose:

1. Grant the indexer's managed identity the Microsoft Graph application permissions
   `ExternalConnection.ReadWrite.OwnedBy`, `ExternalItem.ReadWrite.OwnedBy`, `User.Read.All`,
   and `Group.Read.All`, then grant admin consent.
2. With `-MongoAuthMode ManagedIdentity`, grant both managed identities a read-only database
   role in Atlas or Azure DocumentDB.
3. Register Entra SSO in the Teams Developer Portal and note the registration ID.
4. Register the connector in the M365 admin center under
   **Copilot → Connectors → Gallery → Create a new connector → Connect to MCP server**.
5. Roll out to a test group before enabling for all users.

### 4. Run locally

```bash
dotnet run --project src/Mongo.Copilot.Federated
```

In Development, auth is skipped so MCP Inspector can connect without a token. Access filters
then see no claims and — being fail-closed — return nothing for any collection not mapped to
`Everyone`. That is deliberate: a local run should not see more than a real caller would.

## Tools exposed to Copilot

| Tool | Purpose |
|---|---|
| `list_collections` | Discover what data is available |
| `collection_schema` | Field names and types, to ground query construction |
| `find` | Filtered document lookup |
| `count` | Cardinality for a filter |
| `aggregate` | Grouping and summarizing, read stages only |

All five are annotated `readOnlyHint: true`, which federated connector registration requires —
tools without it are dropped when the connector is registered.

## Azure-hosted MongoDB

"MongoDB hosted in Azure" is three different products, and they are not equivalent.

| Target | Indexer | Live tools | Notes |
|---|---|---|---|
| **MongoDB Atlas on Azure** | ✅ | ✅ | M10+ for private endpoints and workload identity federation |
| **Azure DocumentDB** (Cosmos DB for MongoDB **vCore**) | ✅ | ✅ | Change streams GA May 2025, deletes included |
| **Azure Cosmos DB for MongoDB (RU)** | ❌ | ✅ | **No change-stream delete events** |
| Self-managed on VM/AKS | ✅ | ✅ | Ordinary MongoDB |

### Why RU-based Cosmos DB cannot be indexed

Microsoft's documentation is explicit: *"The `insert`, `update`, and `replace` operations
types are currently supported. However, the delete operation or other events are not yet
supported."*

A document deleted in MongoDB would therefore stay in the Microsoft 365 search index
indefinitely, and Copilot would keep citing records that no longer exist. Both apps
**refuse to start** if the connection string points at `*.mongo.cosmos.azure.com` while any
collection is `Indexed` or `Both`; `deploy.ps1` catches it earlier still. Setting every
collection to `Live` is supported, since live queries never use change streams.

If you must index RU-based data, the documented workaround is a soft-delete field plus TTL
— but that is a change to your data model, not to this connector.

### Passwordless auth with managed identity

Both viable targets accept an Entra token as a database credential, which removes the
connection-string password from the deployment entirely.

```powershell
./deploy.ps1 ... -MongoAuthMode ManagedIdentity `
    -MongoConnectionString 'mongodb+srv://mycluster.global.mongocluster.cosmos.azure.com/'
```

Note the connection string now carries **no credentials** — startup validation rejects it if
it still contains an `@`.

| Target | `Mongo:TokenResource` | Database access |
|---|---|---|
| Azure DocumentDB | defaulted to `https://ossrdbms-aad.database.windows.net/.default` | `az documentdb mongocluster microsoft-entra-user assign --type ManagedIdentity` |
| Atlas | **required** — the Application ID URI of the app registration backing workload identity federation | Add a database user of type Workload Identity Federation, mapped to the principal's object ID |

Atlas requires a dedicated M10+ cluster on MongoDB 7.0.11+ for this. Grant both container
apps' managed identities a **read-only** database role.

### Private networking

Pass the Container Apps subnet and the private DNS zones to link:

```powershell
./deploy.ps1 ... `
    -InfrastructureSubnetId '/subscriptions/.../subnets/aca' `
    -PrivateDnsZoneIds '/subscriptions/.../privateDnsZones/privatelink.mongocluster.cosmos.azure.com'
```

Both targets use `mongodb+srv`, which performs an SRV lookup, so the private DNS zone
**must** be linked to the VNet or the hostname will resolve to the public endpoint.

### DocumentDB operational differences

- Change events live in a **400 MB active log**, not an oplog. Under sustained write load a
  resume token can age out and force a re-crawl; the indexer detects this and re-crawls.
- **Failover invalidates the cursor** but not the token. This is handled as a routine
  reconnect, logged at information level rather than as an error.
- A dropped collection surfaces as `NamespaceNotFound` rather than a `drop` event.
- **Multi-shard change streams are preview** and require a support request; changing cluster
  topology invalidates resumability.

One clarification worth noting: the **Azure Native ISV integration for Atlas** provides
unified Azure billing and Entra SSO for the Atlas *portal*. It does not configure data-plane
authentication or private networking — you still set those up in Atlas project settings.

## Security model

**Authorization is defined once and enforced twice.** The `Access` rule on each profile
becomes an index-time ACL for the indexer and a query-time filter for the MCP server.
MongoDB has no notion of Entra identity, so this mapping is the bridge — and it is
deliberately fail-closed at every step:

- A profile with no `Access` rule is a **startup error**, not an implicit grant.
- A caller whose token lacks the required claim gets an **empty result**, not an unfiltered one.
- A document whose ACL cannot be resolved is **skipped, and any previously published copy is
  withdrawn**. A wrong grant in the search index is visible tenant-wide until corrected.
- A name matching **more than one** directory object is refused rather than guessed.

**Documents rarely hold object IDs.** `PrincipalResolver` accepts what real data contains —
an email, a UPN, a mail nickname, or a team display name — and resolves it through Microsoft
Graph, caching both hits and misses for the life of a crawl. Values that already parse as a
GUID skip the lookup. This needs `User.Read.All` and `Group.Read.All` in addition to the
external-item permissions.

**Read-only is structural, not declarative.** Microsoft 365 trusts the `readOnlyHint`
annotation at registration and does not block writes at runtime
([demonstrated here](https://troystaylor.com/power%20platform/mcp/2026-06-19-federated-connectors-crud-experiment.html)),
so the guarantee is enforced three ways instead:

1. No mutating tool exists — `Core` exposes no write surface to the MCP server.
2. `PipelineGuard` rejects `$out`, `$merge`, `$function`, `$where`, and cross-collection
   stages before a pipeline reaches the driver, including when nested inside an allowed stage.
3. The database credential should be a **read-only** MongoDB user or role, so a defect in the
   first two still cannot write.

Store the connection string in Key Vault; the Bicep template passes it as a Container App
secret. With `-MongoAuthMode ManagedIdentity` there is no password at all — see
[Azure-hosted MongoDB](#azure-hosted-mongodb).

## Operational notes

- **Indexer state** lives as a reserved `externalItem` (`mongocopilotstate`) inside each
  connection, ACL'd deny-everyone so it never surfaces in search. `externalConnection` has
  no free-form metadata bag, and MongoDB is read-only, so this avoids a storage account.
- **Orphan reconciliation** diffs each crawl against the manifest of previously published
  items and deletes what has disappeared — but refuses if more than
  `Indexer:MaxOrphanDeleteRatio` (default 25%) would go, because a crawl cut short by a
  connectivity failure looks exactly like a mass deletion.
- **Liveness**: the tail beats on every change-stream batch, including empty ones. `/health`
  fails after 10 minutes of silence and Container Apps restarts the container.
- **Resume tokens** are persisted per collection and saved at most every 30 seconds while
  tailing, bounding replay after a restart.
- **Oplog rollover** invalidates a resume token; the indexer detects this and re-crawls.
- **Deletes** carry only `documentKey`, which is all a Graph delete needs — pre-images are
  not required.
- **Items over 4 MB** are truncated rather than dropped.
- **Telemetry**: both apps export to Application Insights through OpenTelemetry when
  `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, which the Bicep provides.

## Known limitations

**Very large collections forgo orphan reconciliation.** The manifest of published item IDs
lives in the reserved state item, which Graph caps at 4 MB. Collections past
`Indexer:MaxManifestItems` (default 20,000) skip the diff and log a warning; deletes seen by
the change stream still apply. Raise the limit if your state item can carry it, or split the
collection across connections.

**No full-text relevance in live tools.** `find` takes MongoDB filters, so Copilot can match
exactly but cannot rank by relevance. Collections that need fuzzy search should be `Indexed`,
where the Microsoft 365 semantic index provides it, or the tool set extended with `$search`
against an Atlas Search index.

**Ambiguous directory names are not guessed.** If a document holds a team name that matches
two Entra groups, the resolver refuses rather than picking one, and the document is not
published. Store the object ID in the document to disambiguate.

## Tests

```bash
dotnet test
```

74 tests, no external dependencies — they run on a clean clone without a MongoDB instance or
an Azure subscription.

| Suite | Covers |
|---|---|
| `Core.Tests` (57) | URL resolution order and encoding, the fingerprint that triggers re-crawl, every fail-closed access path, provider detection and the RU guard, and the pipeline guard including nested `$function` injection |
| `Indexer.Tests` (17) | Heartbeat staleness and health reporting, the orphan-deletion safety threshold, and external item ID generation |

### What still needs your environment

The suites cover logic, not integration. These need a real tenant and cluster to confirm:

- Entra SSO end to end, and answers appearing in Copilot Chat under staged rollout
- A full crawl against your data: ACL trimming, citation resolution, schema fit
- Change-stream behaviour on your specific deployment, including resume after restart

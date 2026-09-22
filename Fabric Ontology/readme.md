# Fabric Ontology

A dual-purpose Power Platform custom connector for Microsoft Fabric **ontology (preview)**. One connector does two jobs:

| Surface | What it does | Who uses it |
|---|---|---|
| **REST API** | Create, list, read, update, and delete ontology items and their definitions | Power Automate flows, makers, ALM pipelines |
| **MCP server** | Exposes one ontology item to a Copilot Studio agent over Model Context Protocol | Copilot Studio generative orchestration |

Both surfaces share one host (`api.fabric.microsoft.com`), one app registration, and one connection.

## Why both in one connector

The MCP endpoint is addressed per ontology item:

```
https://api.fabric.microsoft.com/v1/mcp/dataPlane/workspaces/{workspaceId}/items/{ontologyItemId}/ontologyEndpoint
```

Those GUIDs have to be literals in the swagger path — an operation carrying `x-ms-agentic-protocol` **must not declare a `parameters` key**, and Copilot Studio rejects the operation if one is present. So the MCP side is pinned to a single ontology.

The REST side is what makes that workable. `List Workspaces` and `List Ontologies` hand you the two GUIDs you need to fill in, with dropdowns, instead of hunting through portal URLs. After that the REST operations keep earning their place: definition round-trips, ALM, and provisioning.

## Operations

### Ontology items

| Operation | Method | Path |
|---|---|---|
| List ontologies | GET | `/v1/workspaces/{workspaceId}/ontologies` |
| Create ontology | POST | `/v1/workspaces/{workspaceId}/ontologies` |
| Get ontology | GET | `/v1/workspaces/{workspaceId}/ontologies/{ontologyId}` |
| Update ontology | PATCH | `/v1/workspaces/{workspaceId}/ontologies/{ontologyId}` |
| Delete ontology | DELETE | `/v1/workspaces/{workspaceId}/ontologies/{ontologyId}` |
| Get ontology definition | POST | `/v1/workspaces/{workspaceId}/ontologies/{ontologyId}/getDefinition` |
| Update ontology definition | POST | `/v1/workspaces/{workspaceId}/ontologies/{ontologyId}/updateDefinition` |

### Supporting operations

| Operation | Method | Path | Why it's here |
|---|---|---|---|
| List workspaces | GET | `/v1/workspaces` | Dropdown source for every `workspaceId` |
| List folders | GET | `/v1/workspaces/{workspaceId}/folders` | Dropdown source for `rootFolderId` |
| List lakehouses | GET | `/v1/workspaces/{workspaceId}/lakehouses` | Data binding source discovery |
| List lakehouse tables | GET | `/v1/workspaces/{workspaceId}/lakehouses/{lakehouseId}/tables` | Supplies `sourceTableName` for data bindings |
| Get operation state | GET | `/v1/operations/{operationId}` | Polls long running operations |
| Get operation result | GET | `/v1/operations/{operationId}/result` | Retrieves the payload once state is `Succeeded` |

### MCP

| Operation | Method | Path |
|---|---|---|
| Invoke Fabric Ontology MCP | POST | `/v1/mcp/dataPlane/workspaces/WORKSPACE_ID/items/ONTOLOGY_ITEM_ID/ontologyEndpoint` |

## Dynamic dropdowns

The picker chain removes GUID copy-paste from the whole connector:

```
List Workspaces ──▶ workspaceId
                      ├──▶ List Ontologies  ──▶ ontologyId
                      ├──▶ List Folders     ──▶ rootFolderId
                      └──▶ List Lakehouses  ──▶ lakehouseId ──▶ List Lakehouse Tables
```

Every dependent dropdown passes its parent through `x-ms-dynamic-values.parameters`, so choosing a workspace refilters the ontology, folder, and lakehouse lists automatically.

`List Lakehouse Tables` is the one dropdown that isn't wired to a parameter. Table names belong in the **body** of an entity type data binding (`sourceTableProperties.sourceTableName`), and Swagger 2.0 dynamic values can't populate a nested body property inside an array. Run it as its own action and read the names off the result.

## Definition payloads are handled for you

Fabric returns and accepts ontology definitions as base64 parts:

```json
{ "path": "definition.json", "payload": "e30=", "payloadType": "InlineBase64" }
```

[script.csx](./script.csx) removes the encode/decode step:

- **On read** (`Get ontology definition`, `Get operation result`) each part gains a `payloadText` field holding the decoded JSON. The original `payload` is preserved. A part whose payload isn't valid base64 is left alone rather than failing the call.
- **On write** (`Create ontology`, `Update ontology definition`) supply readable JSON in `payloadText` and the script base64-encodes it into `payload` and sets `payloadType`. Supplying `payload` directly still works.

So a definition round-trip is: Get definition → edit `payloadText` → Update definition. No base64 actions in the flow.

## Long running operations

`Create ontology`, `Get ontology definition`, and `Update ontology definition` can return **202** with an empty body and the real information in headers. Power Automate can't read those headers off a connector response, so the script synthesizes a body:

```json
{
  "status": "Accepted",
  "operationId": "0acd697c-1550-43cd-b998-91bfbfbd47c6",
  "location": "https://api.fabric.microsoft.com/v1/operations/0acd697c-1550-43cd-b998-91bfbfbd47c6",
  "retryAfter": 30
}
```

Poll `Get operation state` with that `operationId` until `status` is `Succeeded`, then call `Get operation result`.

## Region-safe operation routing

In some regions Power Platform delivers `Context.OperationId` base64 encoded. The script handles both forms, so the definition and long-running-operation transformations behave consistently wherever the connector is deployed. This matters if you fork `script.csx` — drop the decoding and the transformations stop running in affected regions while calls still return 200.

## Prerequisites

- A paid **F2+ Fabric capacity**, or P1+ Power BI Premium with Fabric enabled.
- **Ontology item (preview)** enabled in Fabric tenant settings.
- Permission to create an Entra app registration, and a Global Administrator to grant admin consent.
- Users need the relevant workspace role: *viewer* to list, *contributor* to create, read/write on the item to change a definition.

## Setup

### 1. Register an Entra application

Supported account types: *Accounts in this organizational directory only*. Copy the **Application (client) ID** and **Directory (tenant) ID**.

Add these **delegated** permissions, then **Grant admin consent**:

| Scope | Needed for |
|---|---|
| `Item.ReadWrite.All` | Create, update, and both definition operations |
| `Item.Read.All` | Get ontology, and item metadata reads during MCP `tools/list` |
| `Item.Execute.All` | The ontology MCP data-plane endpoint |
| `Workspace.Read.All` | List workspaces, list ontologies, list lakehouses, list folders |
| `Lakehouse.Read.All` | List lakehouse tables |

> `Item.Execute.All` is the scope that separates ontology from data agents. A data agent uses `DataAgent.Execute.All`; ontology and Power BI semantic model endpoints use `Item.Execute.All`. Missing it produces a 403 on the MCP endpoint while every REST operation keeps working — a confusing failure if you don't expect it.

The connector requests `https://api.fabric.microsoft.com/.default`, so it inherits whatever is consented on the app registration. Adding a scope later means re-consenting **and** recreating connections.

### 2. Configure OAuth

Open [apiProperties.json](./apiProperties.json) and replace `[YOUR_CLIENT_ID]` with your Application (client) ID and `[YOUR_TENANT_ID]` with your Directory (tenant) ID. Leave `[YOUR_REDIRECT_URL]` as-is — Power Platform generates the real value and overwrites it during deployment.

| Setting | Value |
|---|---|
| Identity provider | Azure Active Directory |
| Resource URL | `https://api.fabric.microsoft.com` |
| Scope | `https://api.fabric.microsoft.com/.default` |
| Enable on-behalf-of login | `true` |
| Tenant ID | Your tenant GUID |

**Use your tenant GUID, not `common`.** `common` causes intermittent per-user authentication failures against the Fabric data-plane endpoints.

If you prefer not to put the client ID in the file, deploy first and set it on the connector's **Security** tab in the portal instead. The connector deploys either way, but no one can create a connection until a real client ID is in place.

For on-behalf-of single sign-on, also preauthorize **Azure API Connections** (`fe053c5f-3692-4f14-aef2-ee34fc081cae`) under **Expose an API** → **Authorized client applications** on an `access_as_user` scope. Without OBO, each user creates their own connection and re-authenticates when tokens lapse.

### 3. Deploy

```powershell
cd "Fabric Ontology"

pac connector create `
  --environment <YOUR_ENVIRONMENT_ID> `
  --api-definition-file apiDefinition.swagger.json `
  --api-properties-file apiProperties.json `
  --script-file script.csx
```

**`--script-file` is required.** This connector declares `scriptOperations`, and omitting the script fails the deployment with `InvalidScriptDefinitionUrlWithNonNullOperations`. The two always travel together.

To update an existing deployment, swap `create` for `update --connector-id <id>` and keep the same file arguments.

### 4. Confirm the deployment

A returned connector ID means the record was written, not that the definition arrived intact. Download it back and check it:

```powershell
pac connector download --connector-id <id> --outputDirectory ./verify
```

The definition should contain **14 operations**, the dynamic dropdowns should still be present, and `script.csx` should match the file you uploaded.

### 5. Register the redirect URL

Power Platform generates a redirect URL unique to this connector and environment. Read it from the downloaded properties file at `properties.connectionParameters.token.oAuthSettings.redirectUrl`, then add it to your app registration:

```powershell
az ad app update --id <YOUR_APP_ID> --web-redirect-uris `
    "https://global.consent.azure-apim.net/redirect" "<PER_CONNECTOR_URL>"
```

Or in the portal: **App registration** → **Authentication** → **Add a platform** → **Web**.

Because the URL encodes both the connector name and the environment, deploying to dev and prod produces two different values and **both must be registered**.

### 6. Point the MCP operation at your ontology

Do this last, because it needs a working connection. Create a connection, then run **List Workspaces** followed by **List Ontologies** in the Test tab. Take the `workspaceId` and the ontology `id`, and replace `WORKSPACE_ID` and `ONTOLOGY_ITEM_ID` in the MCP path in [apiDefinition.swagger.json](./apiDefinition.swagger.json). Redeploy with `pac connector update`.

Until you do, the REST operations work but `InvokeMCP` points at a literal `WORKSPACE_ID` path and will fail.

Both GUIDs are also visible in the Fabric portal URL when the ontology is open:

```
https://app.fabric.microsoft.com/groups/<workspace-ID>/ontologies/<ontology-item-ID>
```

## Test the deployed connector

In the connector **Test** tab, confirm the REST side first:

1. **List Workspaces** — returns your workspaces
2. **List Ontologies** — pick a workspace from the dropdown
3. **Get Ontology Definition** — confirm `payloadText` appears next to each `payload`

Then the MCP side, with **Raw Body** on:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": {},
    "clientInfo": { "name": "power-platform-test", "version": "1.0.0" }
  }
}
```

Expect HTTP 200 with `result.protocolVersion` and `result.serverInfo`. Then:

```json
{ "jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {} }
```

**The operation ID is not an MCP method.** `InvokeMCP` is the Power Platform operation. The `method` in the JSON-RPC body must be `initialize`, `tools/list`, or `tools/call`.

## Add to Copilot Studio

**Tools** → **Add a tool** → select the connector → choose **Invoke Fabric Ontology MCP** → **Add to agent**. Enable generative orchestration and publish.

Keep the tool on **user authentication**. Ontology honors Fabric RBAC, and a shared maker connection would show every user the maker's view of the data.

## Constraints

### One connector, one ontology

The MCP path is literal. Ten ontologies means ten connectors, or ten copies of the MCP operation with different paths in one connector. The REST operations are fully parameterized and cover every ontology in the tenant from a single connection — only the MCP surface is pinned.

### Preview

Ontology items, the ontology MCP server, and the lakehouse tables API are all in preview. No SLA, and endpoint shapes can change.

### No unattended MCP scenarios

Delegated auth only. An autonomous Copilot Studio agent fired by a schedule has no signed-in user and no valid identity for the data-plane call. The REST operations do support service principals and managed identities, so ALM automation is fine — it's the MCP tool that needs a real user.

## Telemetry

[script.csx](./script.csx) includes Application Insights logging, disabled by default. Set `APP_INSIGHTS_ENABLED` to `true` and replace `APP_INSIGHTS_KEY` to record operation outcomes and exceptions. Telemetry failures are swallowed and never affect the response.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Deployment fails with `InvalidScriptDefinitionUrlWithNonNullOperations` | `scriptOperations` declared but no script uploaded | Add `--script-file script.csx` |
| Deployment fails with `CustomScriptProvisioningFailed` | No unassigned function app in the region | Deploy without the script, then attach it with `pac connector update`, or try another region |
| Connector deploys but no connection can be created | No real client ID set | Set your Application (client) ID in `apiProperties.json` or on the **Security** tab |
| REST operations work, MCP returns 403 | `Item.Execute.All` missing or not consented | Add the delegated scope, grant admin consent, recreate the connection |
| `InvokeMCP` fails with a path error | `WORKSPACE_ID` / `ONTOLOGY_ITEM_ID` never replaced | Complete step 6 and redeploy |
| Consent succeeds but calls return 401/403 | Scope added after the connection was made | Connections cache the token audience — delete and recreate the connection |
| Auth fails intermittently across users | Tenant ID set to `common` | Use the concrete tenant GUID |
| Sign-in redirect fails | Callback URL not registered | Add the generated redirect URL to the app's **Web** redirect URIs |
| Definition parts come back unreadable | Looking at `payload` instead of `payloadText` | The decoded JSON is in `payloadText` |
| A 202 response looks empty | Fabric returns LRO data in headers | The script synthesizes a body — read `operationId` from it |
| Test tab returns a protocol error | Operation ID sent as the MCP `method` | Use `initialize` / `tools/list` / `tools/call`; enable **Raw Body** |
| Dropdowns are empty | `Workspace.Read.All` missing, or no connection yet | Grant the scope and confirm the connection works |

## References

- [Ontology items REST API](https://learn.microsoft.com/rest/api/fabric/ontology/items)
- [Ontology definition structure](https://learn.microsoft.com/rest/api/fabric/articles/item-management/definitions/ontology-definition)
- [Consume ontology as an MCP server](https://learn.microsoft.com/fabric/iq/ontology/how-to-use-ontology-mcp-server)
- [Fabric REST API scopes](https://learn.microsoft.com/rest/api/fabric/articles/scopes)
- [Long running operations](https://learn.microsoft.com/rest/api/fabric/articles/long-running-operation)
- [Configure OBO authentication for custom connectors](https://learn.microsoft.com/microsoft-copilot-studio/advanced-custom-connector-on-behalf-of)
- [Fabric Data Agent MCP](../Fabric%20Data%20Agent%20MCP/readme.md) — the sibling pattern for per-item MCP endpoints
- [Fabric MCP Custom](../Fabric%20MCP%20Custom/readme.md) — the tenant-wide Fabric core MCP connector

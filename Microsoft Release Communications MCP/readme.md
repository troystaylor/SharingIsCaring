# Microsoft Release Communications MCP

Power Platform custom connector for **Microsoft Release Communications (MRC)**, bringing Microsoft 365 Roadmap and Azure Updates release information into Copilot Studio agents, Power Automate, and Power Apps.

The MRC MCP Server is a public, unauthenticated remote MCP server hosted by Microsoft at `https://www.microsoft.com/releasecommunications/mcp`. This connector wraps it so it can be consumed as a Power Platform connection instead of only from IDE-based MCP clients.

It is a **hybrid connector**: the MCP endpoint serves Copilot Studio agents, and OData plus RSS operations serve Power Automate and Power Apps, which cannot call MCP.

## Operations

| Operation | Type | Purpose |
| --- | --- | --- |
| `InvokeMCP` | MCP | Streamable HTTP MCP endpoint for Copilot Studio agents |
| `ListM365RoadmapItems` | REST | OData query over Microsoft 365 Roadmap posts |
| `ListAzureUpdates` | REST | OData query over Azure Updates posts |
| `GetM365RoadmapFeed` | REST | Microsoft 365 Roadmap RSS 2.0 feed |
| `GetAzureUpdatesFeed` | REST | Azure Updates RSS 2.0 feed |

## Why a proxy script is needed

The script applies to `InvokeMCP` only. The remote MCP server is spec-compliant, but three behaviors break a naive passthrough in Power Platform. [script.csx](script.csx) resolves each one:

| Upstream behavior | Impact | Handling in script |
| --- | --- | --- |
| Returns **406 Not Acceptable** when `Accept` is only `application/json` | Every call fails | Forces `Accept: application/json, text/event-stream` on the forwarded request |
| Replies over **SSE** (`text/event-stream`) with `event: message` / `data: {...}` framing | Response body isn't parseable JSON-RPC | Unwraps the SSE frames and returns the JSON-RPC message as `application/json` |
| Answers notifications with **202 Accepted** and an empty body | Copilot Studio reports an invalid JSON-RPC response | Returns `{"jsonrpc":"2.0","result":{},"id":null}` for empty successful responses |

The script also normalizes `tools/list` schemas into the JSON Schema subset Power Platform accepts:

- Collapses nullable union types (`"type": ["integer","null"]` on `skip`) to a single scalar type
- Removes `"default": null` entries
- Removes the non-standard `execution` vendor key from tool objects
- Coerces non-boolean `exclusiveMinimum` / `exclusiveMaximum` values

Tool descriptions — including the detailed OData filter guidance — are passed through untouched, since agents rely on them to build correct filters.

The connector is anonymous by design. The caller's `Authorization` header is deliberately **not** forwarded to the public endpoint.

## Available tools

| Tool | Description |
| --- | --- |
| `get_recent_m365_roadmaps` | Microsoft 365 Roadmap posts with OData filtering, title search, pagination, and optional facets. Returns up to 50 items with truncated descriptions. |
| `get_m365_roadmap_by_id` | Full details for a single Microsoft 365 Roadmap post, including the untruncated description. |
| `get_recent_azure_updates` | Azure Updates posts with OData filtering, title search, pagination, and optional facets. Returns up to 50 items with truncated descriptions. |
| `get_azure_update_by_id` | Full details for a single Azure update post, including the untruncated description. |

> The published documentation lists the roadmap list tool as `get_recent_roadmaps`. The live server exposes it as **`get_recent_m365_roadmaps`** — the names above reflect the live `tools/list` response.

### Filtering notes

Both list tools accept an OData `filter` expression and a `search` string (title text only):

- Feature timing: `generalAvailabilityDate eq '2026-02'`, `previewAvailabilityDate ge '2026-01'`
- Publication timing: `created ge 2026-02-01T00:00:00Z and created le 2026-02-28T23:59:59Z`
- Collections: `products/any(p: p eq 'Microsoft Teams')`, `tags/any(t: t eq 'Retirements')`
- Nested availabilities: `availabilities/any(a: a/ring eq 'Retirement' and a/year eq 2026)`
- Pagination: `skip=0`, `skip=50`, `skip=100`; responses include `Offset`, `Limit`, `TotalCount`, and `HasMore`
- Set `include_facets` to `true` to discover valid filter values

## REST API endpoints

Microsoft Learn documents only the MCP server, but the MCP tools are a wrapper over a public **OData v4 API** on the same host. The OData service container is named `ReleaseCommunicationsApi`, matching the `serverInfo.name` the MCP server reports. The connector exposes these directly so flows and apps can use them.

| Endpoint | Notes |
| --- | --- |
| `GET /releasecommunications/api/v2/M365` | OData entity set for Microsoft 365 Roadmap |
| `GET /releasecommunications/api/v2/Azure` | OData entity set for Azure Updates |
| `GET /releasecommunications/api/v2/$metadata` | EDMX schema for both entity sets |
| `GET /releasecommunications/api/v2/M365/rss` | Roadmap RSS 2.0 feed (`application/rss+xml`) |
| `GET /releasecommunications/api/v2/Azure/rss` | Azure Updates RSS 2.0 feed (`application/rss+xml`) |
| `GET /releasecommunications/api/v1/m365/` | Legacy JSON array; different shape (`tagsContainer`, `publicDisclosureAvailabilityDate`). `/api/v1/m365/{id}` returns a single post. No v1 equivalent exists for Azure. |

Supported query options: `$filter` (including `any()` lambdas over collections and the nested `availabilities` complex type), `$search`, `$orderby`, `$select`, `$top`, `$skip`, and `$count`.

### Why use REST instead of MCP

| | MCP tools | OData API |
| --- | --- | --- |
| Page size | Capped at 50 items | 1,000+ items per request |
| Descriptions | Truncated to fit context windows | Full HTML descriptions |
| Total count | Not exposed | `$count=true` returns `@odata.count` |
| Consumers | Copilot Studio agents | Power Automate, Power Apps, any HTTP client |

### Examples

```http
GET /releasecommunications/api/v2/M365?$filter=products/any(p: p eq 'Microsoft Teams')&$top=50
GET /releasecommunications/api/v2/M365?$filter=generalAvailabilityDate eq '2026-02'&$orderby=created desc
GET /releasecommunications/api/v2/Azure?$filter=tags/any(t: t eq 'Retirements')&$count=true
GET /releasecommunications/api/v2/Azure?$filter=availabilities/any(a: a/ring eq 'Retirement' and a/year eq 2026)
GET /releasecommunications/api/v2/M365?$search=Copilot&$select=id,title,status
```

Entity key access such as `M365(569217)` returns `404`. Retrieve a single post with `$filter=id eq 569217`, or use the v1 path `/api/v1/m365/569217`.

> The OData endpoints are **not documented on Microsoft Learn**. They are public and anonymous, and they back the published roadmap and updates sites, but they carry no compatibility guarantee. The MCP endpoint is the documented, supported surface — prefer it for agent scenarios and treat the REST operations as convenience for flows.

## Files

- [apiDefinition.swagger.json](apiDefinition.swagger.json)
- [apiProperties.json](apiProperties.json)
- [script.csx](script.csx)
- [readme.md](readme.md)

## Prerequisites

- A Power Platform environment where you can create custom connectors
- [Power Platform Connectors CLI](https://learn.microsoft.com/connectors/custom-connectors/paconn-cli) (`paconn`) for command line deployment
- No API key, license, or tenant configuration — the upstream server is anonymous

Use of the upstream server is subject to the [Microsoft API Terms of Use](https://learn.microsoft.com/legal/microsoft-apis/terms-of-use).

## Setup

1. Sign in with the connector CLI:

   ```powershell
   paconn login
   ```

2. Create the connector:

   ```powershell
   paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx
   ```

3. Update it later after changing any file:

   ```powershell
   paconn update --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx --cid <CONNECTOR_ID>
   ```

4. Create a connection. There are no connection parameters to fill in.

5. Test `InvokeMCP` with an initialize payload:

   ```json
   {
     "jsonrpc": "2.0",
     "id": "1",
     "method": "initialize",
     "params": {
       "protocolVersion": "2024-11-05",
       "capabilities": {},
       "clientInfo": {
         "name": "Power Platform Test",
         "version": "1.0.0"
       }
     }
   }
   ```

   A successful response reports `serverInfo.name` as `ReleaseCommunicationsApi`.

## Use in Power Automate and Power Apps

Copilot Studio consumes the MCP operation; flows and apps use the REST operations, since neither can call MCP.

Use **List Microsoft 365 roadmap items** or **List Azure updates** and supply OData values directly in the parameter fields. The connector handles URL encoding, so enter expressions unescaped:

| Goal | Filter |
| --- | --- |
| Teams features still in development | `products/any(p: p eq 'Microsoft Teams') and status eq 'In development'` |
| Features reaching GA in a given month | `generalAvailabilityDate eq '2026-02'` |
| Roadmap posts published on or after a date | `created ge 2026-02-01T00:00:00Z` |
| Azure retirements in a given year | `tags/any(t: t eq 'Retirements') and availabilities/any(a: a/ring eq 'Retirement' and a/year eq 2026)` |
| GCC High launched features | `cloudInstances/any(ci: ci eq 'GCC High') and status eq 'Launched'` |

Practical tips:

- Set **Order By** to `created desc` to process newest posts first.
- Set **Include Count** to `true` and read `@odata.count` to size a paging loop, then step **Skip** by your **Top** value.
- Use **Select** (for example `id,title,status`) to keep flow payloads small; descriptions are full HTML and can be long.
- Iterate the `value` array in an **Apply to each** action.

For change detection, run a **Recurrence** trigger against either RSS feed operation, or query with a `created ge` filter using the timestamp of the previous run.

## Use in Copilot Studio

1. Open your agent and select **Tools** > **Add a tool** > **Model Context Protocol**.
2. Choose **Microsoft Release Communications MCP** and select the connection.
3. Add the tools you want the agent to use.

Because the server covers two distinct catalogs, keep prompts scoped to one of them at a time and name the source explicitly. Sample prompts:

- Which Microsoft Teams features on the Microsoft 365 Roadmap are releasing in June?
- What is the status of Feature ID 526798 on the Microsoft 365 Roadmap?
- Show all Azure retirements scheduled for this year.
- Which Azure Databricks features reached general availability in February?

If the agent answers from model knowledge instead of calling a tool, add an instruction that names the tools directly:

```markdown
When the user asks about Microsoft 365 Roadmap features, Azure service updates, release
timing, or retirements, call get_recent_m365_roadmaps, get_m365_roadmap_by_id,
get_recent_azure_updates, or get_azure_update_by_id before answering.
```

## Use the server directly from an MCP client

The connector is only needed for Power Platform. Any MCP client can reach the server directly:

```json
{
  "servers": {
    "MRC-MCP-Server": {
      "type": "http",
      "url": "https://www.microsoft.com/releasecommunications/mcp"
    }
  }
}
```

## Application Insights logging

[script.csx](script.csx) emits `McpRequestCompleted` and `RequestError` events with the MCP method, tool name, correlation ID, status code, and duration. Telemetry is disabled until you replace the placeholder instrumentation key:

```csharp
private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
```

Telemetry failures are swallowed so they never affect a connector call.

## Notes

- The MCP operation proxies the remote server; it does not implement tools locally.
- The REST operations are passthrough. They are not listed in `scriptOperations`, so the script does not run for them. If you do add them, the script forwards any non-`InvokeMCP` operation unchanged, so behavior stays the same.
- Underlying roadmap and update data refreshes daily and contains only publicly available information.
- The upstream MCP endpoint rejects browser `GET` requests with `405 Method Not Allowed`; it is `POST`-only for MCP clients.
- No secrets are stored in this connector.

## Reference

- [Get started with the Microsoft Release Communications MCP Server](https://learn.microsoft.com/microsoft-365/admin/manage/mrc-mcp)
- [Microsoft 365 Roadmap](https://www.microsoft.com/microsoft-365/roadmap)
- [Azure Updates](https://azure.microsoft.com/updates)

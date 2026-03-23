# Power Mission Control — Power MCP Template v3

Progressive API discovery for Copilot Studio agents. Instead of registering dozens of typed tools (which consume context window tokens), this template exposes **3 mission control tools** that cover any API surface:

- **`scan_{service}`** — Scan for available operations by intent
- **`launch_{service}`** — Launch any API endpoint with auth forwarding
- **`launch_batch_{service}`** — Launch multiple operations in one call

## Token Impact

| Approach | 30-operation API | tools/list tokens |
|---|---|---|
| Typed tools (v2) | 30 tools × ~500 tokens | ~15,000 |
| **Mission Control (v3)** | **3 tools** | **~1,500** |

The planner pulls operation details on demand via `scan`, so full schemas are never injected upfront.

## Discovery Modes

### Static (default)
Embedded capability index in `script.csx`. The author curates a JSON array of operations at build time. No external calls needed.

**Best for:** APIs with no describe endpoints and limited documentation. Any API works.

### Hybrid
Embedded index for operation discovery + live API describe/metadata calls for field schemas. Describe results are cached (default 30 min).

**Best for:** APIs with runtime metadata endpoints (Salesforce `/describe`, Shopify introspection).

### McpChain
External MCP server for documentation search (e.g., MS Learn MCP for Microsoft Graph). Results are cached (default 10 min).

**Best for:** APIs backed by searchable documentation services.

## Quick Start

### 1. Configure `MissionControlOptions`

In Section 1 of `script.csx`, set your service name and base URL:

```csharp
private static readonly MissionControlOptions McOptions = new MissionControlOptions
{
    ServiceName = "salesforce",
    BaseApiUrl = "https://your-instance.salesforce.com/services/data/v66.0",
    DiscoveryMode = DiscoveryMode.Static,
    MaxDiscoverResults = 3,
};
```

### 2. Build Your Capability Index

Use the companion `generate-capability-index.prompt.md` to create the index from your API docs:

1. Open `generate-capability-index.prompt.md` in VS Code
2. Paste your API documentation (Swagger, Postman, or text)
3. Copilot generates the JSON array
4. Review and paste into `CAPABILITY_INDEX` in `script.csx`

### 3. Configure Auth

Update `apiDefinition.swagger.json` with your API host and auth:

```json
{
    "host": "your-api-host.com",
    "basePath": "/mcp",
    "securityDefinitions": {
        "oauth2_auth": { ... }
    }
}
```

Update `apiProperties.json` with connection parameters matching your auth.

### 4. Deploy

Deploy as a custom connector in Power Platform. Add to your Copilot Studio agent.

## Files

| File | Purpose |
|---|---|
| `script.csx` | Connector logic — Section 1 (your config) + Section 2 (framework) |
| `apiDefinition.swagger.json` | OpenAPI definition — single POST at `/mcp/` |
| `apiProperties.json` | Connector metadata and auth config |
| `../generate-capability-index.prompt.md` | Copilot prompt for generating capability indexes |

## Architecture

```
Copilot Studio Planner
    │
    ├─ tools/list → [scan_myservice, launch_myservice, launch_batch_myservice]
    │                (~1,500 tokens)
    │
    ├─ tools/call: scan_myservice({query: "create customer"})
    │   ├─ Static:   search embedded CapabilityIndex → return matches
    │   ├─ Hybrid:   search index + call API /describe → return matches + live schema
    │   └─ McpChain: call external MCP server → parse docs → return operations
    │
    ├─ tools/call: launch_myservice({endpoint: "/customers", method: "POST", body: {...}})
    │   ├─ Build URL from BaseApiUrl + endpoint
    │   ├─ Forward Authorization header (OBO token)
    │   ├─ Apply smart defaults ($top, Content-Type, Accept)
    │   ├─ Handle 429 retry (up to 3 with Retry-After)
    │   ├─ Translate 401/403/404 to friendly errors
    │   └─ Summarize response (strip HTML, truncate)
    │
    └─ tools/call: launch_batch_myservice({requests: [...]})
        ├─ Sequential: execute one at a time, in order
        └─ BatchEndpoint: single POST to $batch path
```

## Configuration Reference

### MissionControlOptions

| Property | Default | Description |
|---|---|---|
| `ServiceName` | `"api"` | Used in tool names: `scan_{ServiceName}` |
| `DiscoveryMode` | `Static` | `Static`, `Hybrid`, or `McpChain` |
| `BaseApiUrl` | — | Base URL for all API calls |
| `DefaultApiVersion` | — | API version appended to URL |
| `BatchMode` | `Sequential` | `Sequential` or `BatchEndpoint` |
| `BatchEndpointPath` | `"/$batch"` | Path for native batch endpoint |
| `MaxBatchSize` | `20` | Max requests per batch |
| `DefaultPageSize` | `25` | Auto-injected `$top` for GET collections |
| `CacheExpiryMinutes` | `10` | Discovery cache TTL |
| `DescribeCacheTTL` | `30` | Describe/metadata cache TTL (Hybrid) |
| `MaxDiscoverResults` | `3` | Max operations returned by discover |
| `SummarizeResponses` | `true` | Enable HTML stripping and truncation |
| `MaxBodyLength` | `500` | Max chars for body fields |
| `MaxTextLength` | `1000` | Max chars for text fields |
| `DescribeEndpointPattern` | — | Hybrid: describe path with `{resource}` |
| `McpChainEndpoint` | — | McpChain: external MCP server URL |
| `McpChainToolName` | — | McpChain: tool to call on external server |
| `McpChainQueryPrefix` | — | McpChain: prefix for search queries |
| `SmartDefaults` | — | Author-defined per-endpoint defaults |

### Capability Entry Fields

| Field | Required | Description |
|---|---|---|
| `cid` | Yes | Unique operation identifier (snake_case) |
| `endpoint` | Yes | API path with `{param}` placeholders |
| `method` | Yes | HTTP method (GET/POST/PATCH/PUT/DELETE) |
| `outcome` | Yes | AI-readable description (~1 sentence) |
| `domain` | Yes | Category tag (e.g., "crm", "billing") |
| `requiredParams` | No | Required parameter names |
| `optionalParams` | No | Optional parameter names |
| `schemaJson` | No | Full JSON Schema for input parameters |

## Custom Tools

You can add typed tools alongside mission control tools:

```csharp
private void RegisterCustomTools(McpRequestHandler handler)
{
    handler.AddTool("get_limits", "Get current API usage limits.",
        schema: s => { },
        handler: async (args, ct) =>
        {
            return await SendExternalRequestAsync(HttpMethod.Get, $"{McOptions.BaseApiUrl}/limits");
        });
}
```

These appear in `tools/list` alongside the 3 mission control tools.

## Smart Defaults

Add domain-specific parameter injection:

```csharp
McOptions.SmartDefaults = new Dictionary<string, Action<string, JObject>>
{
    ["/calendar"] = (endpoint, queryParams) =>
    {
        if (queryParams["startDate"] == null)
            queryParams["startDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
    },
    ["/events"] = (endpoint, queryParams) =>
    {
        if (queryParams["$orderby"] == null)
            queryParams["$orderby"] = "start/dateTime";
    }
};
```

## Backward Compatibility

All v2 constructs work unchanged. You can use `handler.AddTool()`, `handler.AddResource()`, and `handler.AddPrompt()` directly — either instead of or alongside mission control mode.

## Template Comparison: v2 vs v3

### Architecture

| | v2 (Typed Tools) | v3 (Mission Control) |
|---|---|---|
| **Pattern** | One `AddTool()` per API operation | 3 meta-tools: discover, invoke, batch |
| **Tool count** | Grows with API surface (10–50+) | Fixed at 3 (+ optional custom tools) |
| **Discovery** | `tools/list` dumps all schemas upfront | Progressive: planner asks `discover` first |
| **Schema delivery** | Full JSON Schema per tool, always loaded | On-demand via `include_schema=true` |
| **API coverage** | Only operations you explicitly register | Any endpoint via generic `invoke` |

### Token Budget (30-operation API)

| | v2 | v3 |
|---|---|---|
| `tools/list` payload | ~15,000 tokens | ~1,500 tokens |
| Per-interaction cost | 0 (schemas pre-loaded) | ~200–400 (discover call) |
| Net savings | — | **~90% on initial load** |

### Developer Experience

| | v2 | v3 |
|---|---|---|
| **User code** | ~10–20 lines per tool × N tools | ~60–80 lines total (configure + index) |
| **Adding operations** | Write new `AddTool()` with schema | Add entry to `CAPABILITY_INDEX` JSON |
| **Auth handling** | Manual per-tool `SendExternalRequestAsync` | Automatic via `ApiProxy` auth forwarding |
| **Error handling** | Manual per-tool try/catch | Built-in hybrid errors (`friendlyMessage` + `suggestion`) |
| **Response processing** | Manual per-tool | Built-in summarization (HTML strip, truncate) |
| **Retry logic** | Manual per-tool | Built-in 429 retry with Retry-After |
| **Pagination** | Manual per-tool | Built-in `$top` injection + `nextLink` detection |
| **Batch operations** | Not supported | Built-in sequential or `$batch` endpoint |

### Capabilities

| Feature | v2 | v3 |
|---|---|---|
| MCP tools | Yes | Yes |
| MCP resources | Yes | Yes |
| MCP prompts | Yes | Yes |
| Custom tools alongside | N/A (all custom) | Yes (`RegisterCustomTools`) |
| Static discovery | N/A | Yes (embedded index) |
| Hybrid discovery | N/A | Yes (index + live describe) |
| MCP chain discovery | N/A | Yes (external MCP server) |
| Smart defaults | N/A | Yes (built-in + author-defined) |
| Response summarization | N/A | Yes (opt-out, configurable limits) |
| App Insights logging | Yes | Yes |
| Capability index authoring | N/A | Yes (`.prompt.md` companion) |

### When to Use Each

**Use v2** when:
- You have a small, fixed set of operations (≤5 tools)
- Each tool has complex, unique logic that doesn't fit a generic proxy pattern
- You need fine-grained control over each tool's schema and behavior
- The API requires different auth or processing per operation

**Use v3** when:
- The API has 10+ operations (token savings become significant)
- Operations follow standard REST patterns (CRUD on resources)
- You want to cover the entire API surface without registering every endpoint
- You want built-in retry, pagination, error handling, and response summarization
- The API may evolve — adding operations means updating the index, not writing code

**Mix both** when:
- Most operations are standard REST (use mission control) but a few need custom logic (use `AddTool`)
- You want mission control for discovery + launch but also expose specific high-value tools directly

## Version History

- **v3.0.0** (2026-03-23) — Mission Control mode with progressive discovery, three discovery modes, embedded capability index, response summarization, smart defaults, hybrid error format
- **v2.1.0** — Fluent registration API, MCP 2025-11-25 support

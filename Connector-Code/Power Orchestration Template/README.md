# Power Orchestration Template

Orchestration-first template (uses MCP as transport) for Power Platform custom connectors. Prefer this when you need to **search** for an operation (Graph or other APIs) before **invoking** it.

## Features
- **MCP compliant** (2025-11-25) and **Copilot Studio** compatible
- Tools: **discover**, **invoke**, **batch_invoke**
- Pluggable discovery providers:
  - `mslearn-graph` (default) — chains to MS Learn MCP (`microsoft_docs_search`)
  - `http-json` — call your own HTTP search endpoint
  - `custom-mcp` — call your own MCP tool
- Generic HTTP invoker with connector auth pass-through
- Optional Application Insights telemetry
- Stateless by default (optional cache toggle)

## Tools
- **discover**
  - `query` *(string, required)*: natural language description
  - `category` *(string, optional)*: category filter (e.g., `mail`, `calendar`)
  - `api` *(string, optional)*: e.g., `graph`, `custom`
- **invoke**
  - `method` *(GET|POST|PATCH|PUT|DELETE, required)*
  - `endpoint` *(string, required)*: relative path or full URL
  - `baseUrl` *(string, optional)*: prepended when `endpoint` is relative
  - `queryParams` *(object, optional)*
  - `headers` *(object, optional)*
  - `body` *(object, optional)*
  - `useConnectorAuth` *(bool, default true)*
- **batch_invoke**
  - `requests` *(array, required; max 20)*: `{ id, method, endpoint, headers?, body? }`
  - `baseUrl` *(string, optional)*

## Connection Parameters (optional)
- `discoveryMode`: `mslearn-graph` (default) | `http-json` | `custom-mcp`
- `discoveryEndpoint`: for `http-json` or `custom-mcp`
- `discoveryHttpMethod`: `GET` (default) | `POST`
- `discoveryToolName`: MCP tool name for `custom-mcp` (default `microsoft_docs_search`)

## Swagger (Streamable HTTP)
```json
{
  "swagger": "2.0",
  "info": {
    "title": "Power Orchestration Template",
    "version": "1.0.0"
  },
  "host": "your-api-host.com",
  "basePath": "/mcp",
  "schemes": ["https"],
  "consumes": ["application/json"],
  "produces": ["application/json"],
  "paths": {
    "/": {
      "post": {
        "summary": "MCP Server Streamable HTTP",
        "x-ms-agentic-protocol": "mcp-streamable-1.0",
        "operationId": "InvokeMCP",
        "responses": { "200": { "description": "Success" } }
      }
    }
  },
  "securityDefinitions": {}
}
```

### apiProperties.json
```json
{
  "properties": {
    "connectionParameters": {
      "discoveryMode": { "type": "string", "uiDefinition": { "displayName": "Discovery Mode", "description": "mslearn-graph | http-json | custom-mcp", "tooltip": "Discovery Mode", "constraints": { "allowedValues": ["mslearn-graph", "http-json", "custom-mcp"], "required": false } } },
      "discoveryEndpoint": { "type": "string", "uiDefinition": { "displayName": "Discovery Endpoint", "description": "Required for http-json or custom-mcp", "tooltip": "Discovery Endpoint", "constraints": { "required": false } } },
      "discoveryHttpMethod": { "type": "string", "uiDefinition": { "displayName": "Discovery HTTP Method", "description": "GET or POST", "tooltip": "Discovery HTTP Method", "constraints": { "allowedValues": ["GET", "POST"], "required": false } } },
      "discoveryToolName": { "type": "string", "uiDefinition": { "displayName": "Discovery Tool Name", "description": "MCP tool name for custom-mcp", "tooltip": "Discovery Tool Name", "constraints": { "required": false } } }
    },
    "policyTemplateInstances": [
      {
        "templateId": "routeRequestToCode",
        "title": "MCP Handler",
        "parameters": {
          "x-ms-apimTemplate-operationName": ["InvokeMCP"]
        }
      }
    ]
  }
}
```

## Telemetry Events
- `McpRequestReceived`, `McpInitialized`, `McpToolsListed`, `McpToolCallStarted`, `McpToolCallCompleted`, `McpToolCallError`, `McpRequestCompleted`
- `DiscoveryCall`, `InvokeCall`, `BatchInvoke`, `DiscoveryError`, `InvokeError`, `BatchInvokeError`

## Stateless Behavior
- `ENABLE_CACHE = false` by default; set to `true` to enable in-memory cache with TTL
- No notifications or listChanged events

## Tips
- For Graph APIs, `discover` uses MS Learn MCP and adds permission hints
- For other APIs, use `http-json` discovery returning `operations[]` with `endpoint`, `method`, `description`, etc.
- `invoke` auto-forwards connector auth if present

## License
MIT

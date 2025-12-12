# Agent 365 MCP Server

Power Platform custom connector template that proxies MCP JSON-RPC requests to the Agent 365 server. This enables Copilot Studio to invoke MCP tools exposed by the server via the `/mcp` endpoint.

## What it does
- MCP protocol endpoint (`/mcp`) with `x-ms-agentic-protocol: mcp-streamable-1.0`
- Forwards MCP requests to the Agent 365 server and returns responses unchanged
- Lets Copilot Studio agents use tools through a Power Platform connector

## Files
- `apiDefinition.swagger.json` — OpenAPI 2.0 with `/mcp` operation and OAuth2 security
- `apiProperties.json` — Connection parameters (`envId`), OAuth2 settings, RouteRequestToEndpoint policy
- `script.csx` — No-op placeholder (routing handled by policy)

## Environment-specific testing
Environment routing via **envId**:
```
https://agent365.svc.cloud.microsoft/mcp/environments/{envId}/servers/MCPManagement
```

## Make it generic before publishing
- Prefer **envId** parameter and build the server URL in script or policy
- Keep request/response for `/mcp` generic to preserve Copilot Studio compatibility
- Remove any tenant-specific metadata in `x-ms-connector-metadata`

## Import & Validate
1. Import via Maker portal → Custom connectors → Import OpenAPI (apiDefinition.swagger.json)
2. Add policy: Route request to endpoint
   - Scheme: `https`
   - Host: `agent365.svc.cloud.microsoft`
   - Base URL: `/mcp/environments/@connectionParameters('envId')/servers/MCPManagement`
3. Security: Configure OAuth2 (AAD) with your app registration `clientId` and scope `https://agent365.svc.cloud.microsoft/.default`
4. Create a connection and set **envId** (your Dataverse environment GUID)

## Copilot Studio usage
- Add this connector to your agent; Copilot Studio detects the MCP endpoint
- Agent can call tools (e.g., GetMCPServers, CreateToolWithConnector) via natural language

## Notes
- Security: OAuth2 (Azure AD) using `https://agent365.svc.cloud.microsoft/.default` scope; headers are handled by the platform
- Protocol: JSON-RPC 2.0; methods like `initialize`, `tools/list`, `tools/call` are forwarded
- Rate limits & permissions: Enforced by server

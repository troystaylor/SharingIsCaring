# Agent 365 MCP Server

Power Platform custom connector template that proxies MCP JSON-RPC requests to the Agent 365 server. This enables Copilot Studio to invoke MCP tools exposed by the server via the `/mcp` endpoint.

## What it does
- MCP protocol endpoint (`/mcp`) with `x-ms-agentic-protocol: mcp-streamable-1.0`
- Forwards MCP requests to the Agent 365 server and returns responses unchanged
- Lets Copilot Studio agents use tools through a Power Platform connector

## Files
- `apiDefinition.swagger.json` — OpenAPI 2.0 with `/mcp` operation and OAuth2 security
- `apiProperties.json` — Connection parameters (`envId`), OAuth2 settings, RouteRequestToEndpoint policy
- `script.csx` — MCP JSON-RPC proxy forwarding to Agent 365 MCPManagement server using `envId`

## Prerequisites
1. **Azure AD App Registration** for OAuth2:
   - Create a new app registration in Azure portal
   - Add redirect URI: `https://global.consent.azure-apim.net/redirect`
   - API permissions: Add `https://agent365.svc.cloud.microsoft` as a custom API with `.default` scope
   - Copy the Application (client) ID for use in step 3 below
2. **Dataverse Environment ID**: Your Power Platform environment GUID

## Import & Deploy
1. Import via Maker portal → Custom connectors → Import OpenAPI (apiDefinition.swagger.json)
2. Security: Configure OAuth2 (AAD) with your app registration `clientId` and scope `https://agent365.svc.cloud.microsoft/.default`
3. Add policy: Route request to endpoint
   - Scheme: `https`
   - Host: `agent365.svc.cloud.microsoft`
   - Base URL: `/mcp/environments/@connectionParameters('envId')/servers/MCPManagement`
4. Create a connection and set **envId** (your Dataverse environment GUID)

## Copilot Studio usage
- Add this connector to your agent; Copilot Studio detects the MCP endpoint
- Agent can call tools (e.g., GetMCPServers, CreateToolWithConnector) via natural language

## Notes
- Security: OAuth2 (Azure AD) using `https://agent365.svc.cloud.microsoft/.default` scope; headers are handled by the platform
- Protocol: JSON-RPC 2.0; methods like `initialize`, `tools/list`, `tools/call` are forwarded
- Rate limits & permissions: Enforced by server

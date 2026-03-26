# Tableau Remote MCP

Power Platform custom connector that proxies JSON-RPC requests directly to a remote Tableau MCP server.

This connector does not implement MCP tools in script. It forwards MCP traffic to your remote server and includes a response normalization workaround for `tools/list` schemas where `exclusiveMinimum` or `exclusiveMaximum` are sent as non-boolean values.

## Authentication Modes

The connector supports two auth modes at runtime:

- OAuth mode
  - Uses incoming `Authorization: Bearer` header if present.
  - Otherwise uses the configured `OAuth Bearer Token` connection parameter.

- PAT mode
  - Uses `PAT Name` and `PAT Secret` connection parameters.
  - Signs in to Tableau REST API (`/api/3.25/auth/signin`) using `Tableau Server URL` and `Site Content URL`.
  - Forwards the resulting token as `X-Tableau-Auth` to the remote MCP endpoint.

Auth selection order in script:

1. Existing Authorization header
2. OAuth Bearer Token connection parameter
3. PAT sign-in and `X-Tableau-Auth`
4. Unauthenticated passthrough

## Files

- [apiDefinition.swagger.json](apiDefinition.swagger.json)
- [apiProperties.json](apiProperties.json)
- [script.csx](script.csx)
- [readme.md](readme.md)

## Connection Parameters

- Remote MCP Host (required)
  - Example: `tableau-mcp.contoso.com`

- Tableau Server URL (optional, PAT mode)
  - Example: `https://prod-useast-c.online.tableau.com`

- Site Content URL (optional, PAT mode)
  - Example: `marketing`

- PAT Name (optional, PAT mode)
- PAT Secret (optional, PAT mode)
- OAuth Bearer Token (optional, OAuth mode)

## Setup

1. Sign in with PAC CLI:

```powershell
paconn login
```

2. Create the connector from Swagger + properties:

```powershell
paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json
```

3. Update the connector later (for changes to Swagger or properties):

```powershell
paconn update --api-def apiDefinition.swagger.json --api-prop apiProperties.json --cid <CONNECTOR_ID>
```

4. Upload [script.csx](script.csx) manually to the Maker portal Code tab.

5. Create a connection and populate the required parameters.

6. Test `InvokeMCP` with an initialize payload:

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-11-25",
    "capabilities": {},
    "clientInfo": {
      "name": "Power Platform Test",
      "version": "1.0.0"
    }
  }
}
```

## Notes

- The connector uses direct remote proxy behavior. It does not register local MCP tools.
- The schema workaround runs only for `tools/list` and only for JSON responses.
- No hardcoded secrets are included.

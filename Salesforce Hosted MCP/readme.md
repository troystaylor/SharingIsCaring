# Salesforce Hosted MCP

Passthrough connector to Salesforce's hosted MCP servers for Copilot Studio. This connector proxies MCP traffic directly to `api.salesforce.com` — Salesforce handles all tool definitions and execution. No custom code runs in the connector.

Use this connector to target any standard or custom Salesforce Hosted MCP Server by changing the path in `apiDefinition.swagger.json`.

## Standard servers

| Server | Path segment | What it does |
|--------|-------------|--------------|
| SObject All | `sobject-all` | Full CRUD, SOQL, SOSL, schema discovery, relationship traversal |
| SObject Reads | `sobject-reads` | Read-only queries and search |
| SObject Mutations | `sobject-mutations` | Create and update only, no delete |
| SObject Deletes | `sobject-deletes` | Delete operations only |
| Data 360 | `data-cloud-queries` | Data Cloud SQL queries against unified customer profiles |
| Tableau Next | `tableau-next` | Semantic models, KPIs, and analytics queries |

Custom servers use the same pattern — replace the path segment with your custom server's API name.

## Prerequisites

- A Salesforce org (Production or Sandbox)
- Admin access to create an External Client App in Salesforce Setup
- Power Platform environment with Copilot Studio

## Salesforce setup

### Create an External Client App

1. In Salesforce Setup, search for **External Client App Manager** and click it.
2. Click **New External Client App**.
3. Fill out the Basic Information section.
4. Expand **API (Enable OAuth Settings)** and check **Enable OAuth**.
5. In **Callback URL**, enter: `https://global.consent.azure-apim.net/redirect/ENVIRONMENT_CONNECTOR_ID`
6. In **OAuth Scopes**, add:
   - **Access Salesforce hosted MCP servers** (`mcp_api`)
   - **Perform requests at any time** (`refresh_token`)
7. Under **Security**:
   - Uncheck **Require secret for Web Server Flow** and **Require secret for Refresh Token Flow**
8. Click **Create**.
9. Click **Settings**, then **Consumer Key and Secret** under OAuth Settings. Copy both values.

> **Note:** The External Client App may take up to 30 minutes to become available after creation.

### Activate MCP servers

1. In Salesforce Setup, search for **MCP Servers**.
2. Click the **Salesforce Servers** tab.
3. Click on each server you want to expose, then click **Activate**.

MCP servers are disabled by default — they must be explicitly activated before any client can connect.

## Connector setup

1. Replace `YOUR_CLIENT_ID` and `YOUR_CLIENT_SECRET` in `apiProperties.json` with the values from your External Client App.
2. Deploy the connector:

```powershell
pac connector create `
  --settings-file apiProperties.json `
  --api-definition apiDefinition.swagger.json
```

3. Create a new connection and sign in with your Salesforce credentials.
4. Add the connector to a Copilot Studio agent — the MCP tools are discovered automatically.

### Targeting a different server

The Swagger file ships with `sobject-all` as the default path. To target a different server, update the path in `apiDefinition.swagger.json`:

```json
"paths": {
    "/platform/mcp/v1/platform/sobject-reads": {
```

For sandbox or scratch orgs, replace `platform` in the path segment after `v1/` with `sandbox`:

```json
"paths": {
    "/platform/mcp/v1/sandbox/platform/sobject-all": {
```

Also update the `authorizationUrl` and `tokenUrl` in `securityDefinitions` from `login.salesforce.com` to `test.salesforce.com` for sandbox orgs.

## Architecture

This connector is a pure passthrough:

- **Host:** `api.salesforce.com`
- **Protocol:** MCP Streamable HTTP 1.0 (`x-ms-agentic-protocol: "mcp-streamable-1.0"`)
- **Auth:** OAuth 2.0 Authorization Code via Salesforce External Client App with `SalesforceV2` identity provider
- **Scope:** `mcp_api`, `refresh_token`

No `script.csx` is used. Salesforce's hosted MCP server handles all tool definitions, request processing, and response formatting. The `produces` field includes `text/event-stream` alongside `application/json` for MCP Streamable HTTP.

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | Swagger with MCP passthrough operation and OAuth security definitions |
| `apiProperties.json` | OAuth configuration with `SalesforceV2` identity provider |
| `readme.md` | This file |

## Reference

- [Salesforce Hosted MCP Servers overview](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/overview)
- [Standard MCP Servers reference](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/references/reference/servers-reference.html)
- [Create an External Client App](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html)
- [Connect Claude with Salesforce Hosted MCP Servers](https://developer.salesforce.com/blogs/2026/05/connect-claude-with-salesforce-hosted-mcp-servers)
- [Copilot Studio documentation](https://learn.microsoft.com/microsoft-copilot-studio/)

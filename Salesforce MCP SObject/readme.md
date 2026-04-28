# Salesforce MCP SObject

Passthrough connector to Salesforce's official hosted MCP server for full SObject CRUD operations. This connector proxies MCP traffic directly to `api.salesforce.com` — Salesforce handles all MCP protocol, tool definitions, and execution. No custom code runs in the connector.

## Tools Provided by Salesforce

The hosted MCP server exposes these tools automatically:

| Tool | Description |
|------|-------------|
| `getObjectSchema` | Returns schema info optimized for LLM consumption. Call with no parameters for an index of all queryable objects; call with object name(s) for field-level details |
| `soqlQuery` | Executes a SOQL query to retrieve records. Supports relationship queries, subqueries, filtering, sorting, and aggregation |
| `find` | Text search (SOSL) across multiple objects simultaneously with relevance-ranked results. Max 2,000 records |
| `getUserInfo` | Returns current user's identity, role, profile, manager, local time, timezone, and admin-configured preferences |
| `listRecentSobjectRecords` | Returns records of a specific type the user recently viewed or modified. Up to 2,000 records |
| `getRelatedRecords` | Retrieves child records via relationship traversal. Supports multi-level traversal (e.g., Account → Contacts → Cases) |
| `createSobjectRecord` | Creates a new record. Returns the new record's ID on success |
| `updateSobjectRecord` | Updates an existing record by ID. Only include fields you want to change |
| `updateRelatedRecord` | Updates a child record by navigating from a parent record through a relationship |

All operations respect the authenticated user's field-level security, object permissions, and sharing rules.

## Prerequisites

- A Salesforce org (Production or Sandbox)
- Admin access to create an External Client App in Salesforce Setup
- Power Platform environment with Copilot Studio

## Salesforce Setup

### Create an External Client App

1. In Salesforce Setup, search for **External Client App Manager** and click it.
2. Click **New External Client App**.
3. Fill out the Basic Information section.
4. Expand **API (Enable OAuth Settings)** and check **Enable OAuth**.
5. In **Callback URL**, enter: `https://global.consent.azure-apim.net/redirect`
6. In **OAuth Scopes**, add:
   - **Access Salesforce hosted MCP servers** (`mcp_api`)
   - **Access the identity URL service** (`id`)
   - **Access unique user identities** (`openid`)
   - **Perform requests at any time** (`refresh_token`)
7. Under **Security**:
   - Enable **Issue JSON Web Token (JWT)-based access tokens for named users**
8. Click **Create**.
9. Click **Settings**, then **Consumer Key and Secret** under OAuth Settings. Copy the Consumer Key and Consumer Secret for use in the connector configuration.

> **Note:** The External Client App may take up to 30 minutes to become available worldwide after creation.

### Activate MCP Servers

1. In Salesforce Setup, search for **MCP** or **Salesforce MCP Servers**.
2. Find the **sobject-all** server and activate it.
3. MCP servers are disabled by default — they must be explicitly activated before any client can connect.

### For Sandbox/Scratch Orgs

The connector defaults to `https://login.salesforce.com`. For sandbox orgs, you may need to update the Login URI to `https://test.salesforce.com` when creating the connection.

The hosted MCP server URL for sandbox/scratch orgs uses a different path: `/platform/mcp/v1/sandbox/platform/sobject-all`. If targeting a sandbox, the `basePath` in `apiDefinition.swagger.json` must be updated accordingly.

## Connector Setup

1. Deploy the connector to your Power Platform environment using `pac connector create`.
2. Replace `YOUR_CLIENT_ID` and `YOUR_CLIENT_SECRET` in `apiProperties.json` with the values from your External Client App before deployment.
3. Create a new connection and sign in with your Salesforce credentials.
4. Add the connector to a Copilot Studio agent — the MCP tools will be discovered automatically.

## Architecture

This connector is a pure passthrough:

- **Host:** `api.salesforce.com`
- **MCP Server:** `/platform/mcp/v1/platform/sobject-all`
- **Protocol:** MCP Streamable HTTP 1.0 (`x-ms-agentic-protocol: "mcp-streamable-1.0"`)
- **Auth:** OAuth 2.0 Authorization Code with PKCE via Salesforce External Client App
- **Scope:** `mcp_api`, `refresh_token`

No `script.csx` is used. Salesforce's hosted MCP server handles all tool definitions, request processing, and response formatting.

## Related Connectors

| Connector | Server | Use Case |
|-----------|--------|----------|
| **Salesforce SObject Reads** | `sobject-reads` | Read-only access (governance-safe) |
| **Salesforce SObject Mutations** | `sobject-mutations` | Create/update only (no delete) |
| **Salesforce Data 360** | `data-cloud-queries` | Data Cloud SQL queries |
| **Salesforce Tableau Next** | `tableau-next` | Analytics, dashboards, semantic models |
| **Salesforce Custom MCP** | Admin-configured | Flows, Apex Actions, API Catalog |

## Reference

- [Salesforce Hosted MCP Servers Overview](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/overview)
- [SObject All Server Reference](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/references/reference/sobject-all.html)
- [Create an External Client App](https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/create-external-client-app.html)

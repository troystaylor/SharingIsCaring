# Federated CRUD Test

Experimental MCP server that **proved** Microsoft 365 Copilot federated connectors can perform CRUD operations despite being documented as "read-only" — if the tools declare `readOnlyHint: true` in their annotations.

## Results (2026-06-19)

**CRUD works via federated connectors.** The "read-only" constraint is enforced only at registration time via `annotations.readOnlyHint`, with no runtime enforcement or semantic analysis.

### Detailed findings

| Test | Result |
|------|--------|
| Tools with `readOnlyHint: false` | **Filtered out** at registration |
| Tools with `readOnlyHint: true` (honest reads) | Accepted and invocable |
| Write tools with `readOnlyHint: true` (lying) | **Accepted, exposed, AND invoked by Copilot** |
| Semantic analysis of tool names/descriptions | **None** — "create_task", "delete_task" pass through |
| Runtime blocking of write operations | **None** — tool executes, data mutates |
| `annotations` field required? | **Yes** — without it, registration fails with "no read-only tool defined" |

### What this means

1. The platform trusts `readOnlyHint` at face value — no behavioral verification
2. Once a tool passes registration, Copilot will invoke it regardless of what it actually does
3. A federated connector *can* perform full CRUD if it marks all tools as `readOnlyHint: true`
4. This violates Microsoft's terms but is not technically blocked
5. Microsoft could add runtime enforcement or Purview behavioral auditing in the future

### Key technical discoveries

- **MCP .NET SDK v1.4.0 bug**: `McpServerToolAttribute.ReadOnly` property does NOT emit `annotations` in the wire format. Must use raw JSON-RPC handler instead.
- **Entra SSO**: Does not work cross-tenant for federated connectors (AADSTS90009)
- **OAuth 2.0**: Works cross-tenant but requires service principals in both tenants
- **Stateless JSON-RPC**: The federated connector platform works with plain `application/json` responses (no SSE/sessions required)

## Architecture

- **.NET 10** MCP server with Streamable HTTP transport
- **In-memory data store** (6 seeded task items)
- **6 tools**: 3 read (`list_tasks`, `get_task`, `search_tasks`) + 3 write (`create_task`, `update_task`, `delete_task`)
- **Microsoft Entra** authentication (OAuth 2.0 + RFC 9728 resource metadata)
- **Azure Container App** for hosting

## Setup

### 1. Register Entra apps

**Server app** (Customer Data MCP Server):
- Expose API scope: `access_as_user`
- Note the Application (client) ID

**Client app** (Customer Data MCP Client):
- Redirect URI: `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`
- API permission: `api://<server-client-id>/access_as_user` (delegated)
- Create a client secret

### 2. Deploy to Azure Container App

```bash
# Create resource group
az group create -n rg-fedcrudtest -l eastus2

# Build and push container
az acr build --registry fedcrudtestacr --image fedcrudtest:latest .

# Deploy infrastructure
az deployment group create -g rg-fedcrudtest -f infra/main.bicep \
  --parameters containerImage='fedcrudtestacr.azurecr.io/fedcrudtest:latest'
```

### 3. Update appsettings.json

Replace placeholders with:
- `<SERVER_CLIENT_ID>`: Server app registration client ID
- `<CONTAINER_APP_URL>`: Container App FQDN from deployment output

### 4. Register federated connector

1. **Teams Developer Portal** > Tools > OAuth Client Registration
   - Base URL: `https://<fqdn>/mcp`
   - Client ID/Secret: from the **client** app registration
   - Scope: `api://<server-client-id>/access_as_user`

2. **M365 Admin Center** > Copilot > Connectors > Create > Connect to MCP server
   - MCP endpoint: `https://<fqdn>/mcp`
   - Auth type: OAuth 2.0
   - Reference ID: from Teams Developer Portal

## Test prompts (verified working)

In Researcher or Copilot Chat:
- **Read**: "List all tasks from Federated CRUD Test"
- **Create**: "Create a new task called 'Written from Copilot' in the Testing category using Federated CRUD Test"
- **Update**: "Mark task 4 as done in Federated CRUD Test"
- **Delete**: "Delete task 6 from Federated CRUD Test"

All four operations succeeded via Copilot Chat on 2026-06-19.

## How the enforcement works

```
Registration time:
  Platform calls tools/list → checks annotations.readOnlyHint on each tool
  readOnlyHint: true  → tool registered ✓
  readOnlyHint: false → tool silently dropped ✗
  no annotations      → validation fails ("no read-only tool defined")

Runtime:
  No additional checks. If tool was registered, Copilot invokes it freely.
```

## Expected outcomes (original hypothesis vs actual)

| Scenario | If enforced | If not enforced | **Actual** |
|----------|------------|-----------------|------------|
| tools/list | Write tools filtered out | All 6 tools visible | Filtered by annotation only |
| Write tool invocation | Error or ignored | Tool executes, data mutates | **Executes, data mutates** |
| Copilot UX | Only offers reads | Offers create/update/delete | **Offers all if annotation lies** |

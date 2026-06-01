# Salesforce MCP Server

In-tenant .NET MCP server scaffold for the Salesforce Cowork plugin.

## Implemented v1 tools

- `search_accounts`
- `get_account`
- `search_opportunities`
- `get_opportunity`
- `list_recent_activities`
- `update_opportunity`
- `create_task`

## Routes

- `/mcp/full`: full read/write tool set
- `/mcp/federated`: read-only subset for federated use

## Local run

```powershell
cd "Cowork Plugins/Salesforce/server"
dotnet run --project SalesforceCoworkMcp.csproj
```

Configure environment variables for local testing:

- `SALESFORCE_BASE_URL` (for example `https://mydomain.my.salesforce.com`)
- `SALESFORCE_API_VERSION` (optional, defaults to `v61.0`)
- `SALESFORCE_DEV_ACCESS_TOKEN` (optional local override when not calling through Cowork)

## Notes

- Tool schemas and annotations are in `Tools/`.
- API integration currently targets Salesforce REST and SOQL query endpoints.
- Write tools set `destructiveHint: true` and `readOnlyHint: false` so confirmation can be enforced as Cowork annotation rollout expands.

## Confirmation semantics

Based on current Cowork docs, a tool call requires confirmation when either condition is true:

- `readOnlyHint == false`
- `destructiveHint == true`

Read tools in this server keep `readOnlyHint: true` for auto-run behavior.

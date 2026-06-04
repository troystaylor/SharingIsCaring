# Power Platform Inventory

Dual-mode Power Platform custom MCP connector that exposes the Power Platform **inventory API** (`PowerPlatformResources` in Azure Resource Graph) as both:

- an **MCP server** for Copilot Studio (`POST /mcp` with `mcp-streamable-1.0`)
- typed **Power Automate** operations with full schemas and dynamic dropdowns

Every sample query from the Microsoft Learn documentation has a dedicated operation, so end users can call them without writing KQL.

## What it does

Operations map 1:1 to the documented [sample queries](https://learn.microsoft.com/power-platform/admin/inventory-sample-queries):

### Counts and distribution

| Operation | MCP tool | KQL equivalent |
|---|---|---|
| `CountAllResources` | `inventory_count_all` | `PowerPlatformResources \| count` |
| `CountByType` | `inventory_count_by_type` | `summarize count() by type` |
| `CountByEnvironment` | `inventory_count_by_environment` | `summarize count() by environmentId` |
| `CountByRegion` | `inventory_count_by_region` | `summarize count() by location` |
| `TopOwners` | `inventory_top_owners` | `summarize count() by ownerId \| order by desc` |

### Resource lookups

| Operation | MCP tool | Description |
|---|---|---|
| `FindResource` | `inventory_find_resource` | Find a single resource by ID (optionally scoped by type) |
| `RecentResources` | `inventory_recent_resources` | Items created in the past N hours (default 24) |
| `ListResourcesByType` | `inventory_list_resources_by_type` | List resources of a given type with paging |
| `ListEnvironments` | `inventory_list_environments` | List every environment |
| `ListEnvironmentGroups` | `inventory_list_environment_groups` | List every environment group |

### Connector queries (preview)

| Operation | MCP tool | Description |
|---|---|---|
| `TopConnectors` | `inventory_top_connectors` | Top connectors by distinct resources |
| `ConnectorCountDistribution` | `inventory_connector_count_distribution` | Resources grouped by number of connectors used |
| `ResourcesUsingConnector` | `inventory_resources_using_connector` | Impact analysis — every resource using a given connector |
| `ConnectorUsageByEnvironment` | `inventory_connector_usage_by_environment` | Connector adoption per environment |

### Advanced

| Operation | MCP tool | Description |
|---|---|---|
| `RunQuery` | `inventory_run_query` | Submit a raw `TableName + Clauses + Options` payload directly to the inventory API |

## Setup

1. Create a Microsoft Entra ID application registration in your tenant.
2. Add `https://api.powerplatform.com/.default` (delegated) as an API permission and grant admin consent.
3. Replace `[INSERT_YOUR_CLIENT_ID]` in `apiProperties.json` with the application (client) ID.
4. (Optional) Replace `[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]` in `script.csx` to enable telemetry.
5. Deploy to your target environment with the Power Platform CLI:

   ```powershell
   pac connector create `
     --environment <ENVIRONMENT_ID> `
     --api-definition-file .\apiDefinition.swagger.json `
     --api-properties-file .\apiProperties.json `
     --script-file .\script.csx
   ```

   `pac connector create` prints the new connector ID — save it for redeploys.

6. **After the connector exists in the environment**, open it in the maker portal → **Security** tab → enter the client secret (the client ID is already baked in from step 3) → **Update connector**. Copy the per-connector **Redirect URL** shown on the Security tab (it looks like `https://global.consent.azure-apim.net/redirect/<connector-internal-name>`).
7. Back in the Entra app registration → **Authentication** → **Web** platform, paste the per-connector redirect URL from step 6 as a redirect URI and save.
8. Create a connection — sign in as a **Power Platform administrator** or **Dynamics 365 administrator** (required per inventory access rules).

## Redeploy

```powershell
pac connector update `
  --connector-id <CONNECTOR_ID> `
  --environment <ENVIRONMENT_ID> `
  --api-definition-file .\apiDefinition.swagger.json `
  --api-properties-file .\apiProperties.json `
  --script-file .\script.csx
```

## Endpoint

All operations call:

```
POST https://api.powerplatform.com/resourcequery/resources/query?api-version=2024-10-01
```

## Notes

- **MFA / conditional access**: If your tenant requires MFA for Azure Resource Manager, include client ID `00b46ad5-e4ae-43ac-a878-281fc03d0839` and the Microsoft Azure Management resource in the policy, otherwise inventory queries may fail.
- **Connector inventory** queries are preview and only cover canvas apps, model-driven apps, cloud flows, agent flows, workflow agent flows, and Copilot Studio agents.
- Tabular data sources (SharePoint, Dataverse, SQL, Excel Online) report empty `operations` arrays.
- For unsupported scenarios use `RunQuery` / `inventory_run_query` with the documented clause syntax (`$type: where | project | take | orderby | distinct | count | summarize | extend | join`).

## References

- [Power Platform inventory overview](https://learn.microsoft.com/power-platform/admin/power-platform-inventory)
- [Power Platform inventory API](https://learn.microsoft.com/power-platform/admin/inventory-api)
- [Power Platform inventory schema reference](https://learn.microsoft.com/power-platform/admin/inventory-schema)
- [Power Platform inventory sample queries](https://learn.microsoft.com/power-platform/admin/inventory-sample-queries)

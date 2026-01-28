# Azure Cost Management Connector

A comprehensive Power Platform custom connector for the Azure Cost Management API (version 2025-03-01), providing complete access to cost analysis, forecasting, budget management, exports, alerts, scheduled actions, and benefit utilization across all Azure scopes.

## Overview

This connector provides **58 operations** covering the full Azure Cost Management REST API:

- **Query Costs**: Analyze cost data with customizable filters, groupings, and aggregations
- **Forecast Costs**: Get cost predictions based on historical usage patterns
- **Manage Budgets**: Create, update, delete, and monitor budget alerts
- **Configure Alerts**: List, get, and dismiss cost alerts
- **Schedule Exports**: Create automated cost export schedules to storage
- **Create Views**: Save and manage custom cost analysis views
- **Scheduled Actions**: Automate cost-related actions and notifications
- **Cost Allocation Rules**: Define rules for allocating shared costs
- **Generate Reports**: Trigger detailed cost reports and reservation details
- **Benefit Recommendations**: Get savings recommendations for reservations and savings plans
- **Benefit Utilization**: Track reservation and savings plan utilization
- **Settings**: Manage Cost Management settings
- **Price Sheets**: Download pricing information

## Prerequisites

1. **Azure Subscription**: An active Azure subscription with Cost Management access
2. **Azure AD App Registration**: Pre-configured (see Configuration section)
3. **RBAC Permissions**: Appropriate roles assigned:
   - Cost Management Reader (for read operations)
   - Cost Management Contributor (for write operations)
   - Billing Reader (for billing account operations)

## Supported Scopes

| Scope | Description |
|-------|-------------|
| Subscription | Individual Azure subscription |
| Resource Group | Specific resource group within a subscription |
| Management Group | Enterprise-level management group |
| Billing Account | Enterprise Agreement or Microsoft Customer Agreement |
| Billing Profile | MCA billing profile |
| Reservation Order | Reserved instance order |
| Savings Plan Order | Savings plan order |

## Operations

### Helper Operations
| Operation | Description |
|-----------|-------------|
| List Subscriptions | Get all accessible subscriptions (populates dropdowns) |
| List Operations | List available API operations |

### Cost Query Operations (4)
| Operation | Description |
|-----------|-------------|
| Query Subscription Costs | Query cost data for a subscription |
| Query Resource Group Costs | Query cost data for a resource group |
| Query Management Group Costs | Query cost data for a management group |
| Query Billing Account Costs | Query cost data for a billing account |

### Forecast Operations (4)
| Operation | Description |
|-----------|-------------|
| Get Subscription Forecast | Get cost forecast for a subscription |
| Get Resource Group Forecast | Get cost forecast for a resource group |
| Get Management Group Forecast | Get cost forecast for a management group |
| Get Billing Account Forecast | Get cost forecast for a billing account |

### Budget Operations (8)
| Operation | Description |
|-----------|-------------|
| List/Get/Create/Update/Delete Subscription Budgets | Full CRUD for subscription budgets |
| List/Get/Create/Update/Delete Resource Group Budgets | Full CRUD for resource group budgets |

### Dimension Operations (4)
| Operation | Description |
|-----------|-------------|
| List Subscription/Resource Group/Management Group/Billing Account Dimensions | List available dimensions for cost filtering |

### Alert Operations (3)
| Operation | Description |
|-----------|-------------|
| List Subscription Alerts | List all cost alerts for a subscription |
| Get Subscription Alert | Get a specific alert |
| Dismiss Subscription Alert | Dismiss an alert |

### Export Operations (5)
| Operation | Description |
|-----------|-------------|
| List Subscription Exports | List all exports |
| Get Subscription Export | Get export details |
| Create or Update Subscription Export | Create/update export schedule |
| Delete Subscription Export | Delete an export |
| Execute Subscription Export | Trigger immediate export |

### View Operations (4)
| Operation | Description |
|-----------|-------------|
| List Subscription Views | List saved views |
| Get Subscription View | Get view details |
| Create or Update Subscription View | Save a view |
| Delete Subscription View | Delete a view |

### Scheduled Action Operations (5)
| Operation | Description |
|-----------|-------------|
| List Subscription Scheduled Actions | List scheduled actions |
| Get Subscription Scheduled Action | Get action details |
| Create or Update Subscription Scheduled Action | Create/update action |
| Delete Subscription Scheduled Action | Delete action |
| Execute Subscription Scheduled Action | Run action immediately |

### Cost Allocation Rule Operations (4)
| Operation | Description |
|-----------|-------------|
| List Billing Account Cost Allocation Rules | List allocation rules |
| Get Billing Account Cost Allocation Rule | Get rule details |
| Create or Update Billing Account Cost Allocation Rule | Create/update rule |
| Delete Billing Account Cost Allocation Rule | Delete rule |

### Report Generation Operations (5)
| Operation | Description |
|-----------|-------------|
| Generate Subscription Cost Details Report | Trigger detailed cost report |
| Generate Billing Account Detailed Cost Report | Trigger billing account report |
| Generate Billing Account Reservation Details Report | Trigger reservation report |
| Get Operation Status | Check async operation status |
| Get Operation Results | Get completed report results |

### Benefit Operations (8)
| Operation | Description |
|-----------|-------------|
| List Benefit Recommendations | Get savings recommendations |
| List Benefit Utilization Summaries | Get utilization data |
| Generate Benefit Utilization Reports (6 scopes) | Trigger utilization reports |
| Get Benefit Utilization Report Results | Get report results |

### Settings Operations (4)
| Operation | Description |
|-----------|-------------|
| List/Get/Create/Update/Delete Subscription Settings | Manage Cost Management settings |

### Price Sheet Operations (2)
| Operation | Description |
|-----------|-------------|
| Download Billing Account Price Sheet By Period | Get prices for billing period |
| Download Billing Account Price Sheet By Invoice | Get prices for invoice |

## Configuration

### App Registration

An app registration has been pre-configured:

| Property | Value |
|----------|-------|
| **App Name** | Azure Cost Management Connector |
| **Client ID** | `c2205975-75ed-4c64-b0f0-00dc1e95ae38` |
| **Sign-in Audience** | Multi-tenant |
| **Redirect URI** | `https://global.consent.azure-apim.net/redirect` |
| **API Permission** | Azure Service Management - user_impersonation |

### Deploy the Connector

Use the Power Platform CLI to deploy:

```powershell
pac connector create --environment <environment-id> `
  --api-definition-file apiDefinition.swagger.json `
  --api-properties-file apiProperties.json `
  --script-file script.csx
```

When creating a connection, you'll be prompted to sign in and consent to the permissions.

## Query Examples

### Example: Query Monthly Costs by Service

```json
{
  "type": "Usage",
  "timeframe": "MonthToDate",
  "dataset": {
    "granularity": "Daily",
    "aggregation": {
      "totalCost": {
        "name": "Cost",
        "function": "Sum"
      }
    },
    "grouping": [
      {
        "type": "Dimension",
        "name": "ServiceName"
      }
    ]
  }
}
```

### Example: Query Costs with Date Filter

```json
{
  "type": "ActualCost",
  "timeframe": "Custom",
  "timePeriod": {
    "from": "2025-01-01T00:00:00Z",
    "to": "2025-01-31T23:59:59Z"
  },
  "dataset": {
    "granularity": "Monthly",
    "aggregation": {
      "totalCost": {
        "name": "CostUSD",
        "function": "Sum"
      }
    }
  }
}
```

### Example: Create a Budget

```json
{
  "properties": {
    "category": "Cost",
    "amount": 1000,
    "timeGrain": "Monthly",
    "timePeriod": {
      "startDate": "2025-01-01T00:00:00Z",
      "endDate": "2025-12-31T00:00:00Z"
    },
    "notifications": {
      "alert80": {
        "enabled": true,
        "operator": "GreaterThan",
        "threshold": 80,
        "thresholdType": "Actual",
        "contactEmails": ["admin@contoso.com"],
        "frequency": "Daily"
      }
    }
  }
}
```

## MCP Support for Copilot Studio

This connector includes **Model Context Protocol (MCP)** support, enabling integration with Microsoft Copilot Studio and other AI orchestrators.

### MCP Server Configuration

| Property | Value |
|----------|-------|
| **Server Name** | `azure-cost-management` |
| **Server Version** | `1.0.0` |
| **Protocol Version** | `2025-03-26` |

### Available MCP Tools

The connector exposes the following tools for AI orchestration:

| Tool | Description |
|------|-------------|
| `query_subscription_costs` | Query cost data for a subscription with grouping and filtering |
| `query_resource_group_costs` | Query cost data for a specific resource group |
| `get_subscription_forecast` | Get cost forecast predictions based on historical patterns |
| `list_subscription_budgets` | List all budgets configured for a subscription |
| `get_budget` | Get details of a specific budget including utilization |
| `create_budget` | Create a new cost budget with spending alerts |
| `list_dimensions` | List available dimensions for filtering/grouping |
| `list_cost_alerts` | List cost alerts (budget, anomaly, quota) |
| `list_exports` | List scheduled cost data exports |
| `run_export` | Manually trigger a cost export |

### MCP Example Usage

**Initialize the MCP server:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-03-26",
    "clientInfo": { "name": "copilot-studio", "version": "1.0" }
  }
}
```

**List available tools:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list"
}
```

**Query subscription costs:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "query_subscription_costs",
    "arguments": {
      "subscriptionId": "00000000-0000-0000-0000-000000000000",
      "queryType": "ActualCost",
      "timeframe": "MonthToDate",
      "granularity": "Daily",
      "groupBy": "ServiceName"
    }
  }
}
```

**Create a budget:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "create_budget",
    "arguments": {
      "subscriptionId": "00000000-0000-0000-0000-000000000000",
      "budgetName": "MonthlySpend",
      "amount": 5000,
      "timeGrain": "Monthly",
      "startDate": "2025-01-01",
      "alertThresholds": [50, 80, 100],
      "alertEmails": ["admin@contoso.com"]
    }
  }
}
```

### Copilot Studio Integration

To use with Copilot Studio:

1. Deploy the connector to your Power Platform environment
2. Create a connection using the app registration credentials
3. Add the connector as an agent action in Copilot Studio
4. The MCP protocol enables natural language cost analysis:
   - "What are my Azure costs this month?"
   - "Show me costs grouped by resource group"
   - "Create a $10,000 monthly budget with 80% alerts"
   - "What's my projected spend for next month?"

## Script Transformations

The included `script.csx` provides:

### MCP Protocol Support
- **JSON-RPC 2.0**: Full MCP protocol implementation with initialize, tools/list, tools/call handlers
- **Tool Definitions**: 10 tools with JSON Schema for input validation
- **Error Handling**: Proper MCP error codes and messages

### Response Transformations
- **Query/Forecast Results**: Converts raw row arrays into named objects based on column definitions
- **Budget Results**: Adds computed properties like `utilizationPercentage` and formatted currency values

### Application Insights Telemetry

The script includes built-in telemetry for monitoring connector usage:

| Event | Description |
|-------|-------------|
| `CostManagement_RequestReceived` | Logged when a REST request starts |
| `CostManagement_QueryProcessed` | Logged for query/forecast operations with row count |
| `CostManagement_BudgetProcessed` | Logged for budget operations |
| `CostManagement_RequestCompleted` | Logged on success with duration |
| `CostManagement_RequestError` | Logged on failure with error details |
| `MCPMethod` | Logged for each MCP method invocation |
| `MCPToolCall` | Logged when an MCP tool is called |
| `MCPToolError` | Logged when an MCP tool execution fails |
| `MCPUnknownMethod` | Logged for unrecognized MCP methods |

**To enable telemetry**, update the connection string in `script.csx`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=xxx;IngestionEndpoint=https://xxx.in.applicationinsights.azure.com/";
```

## Error Handling

The API may return the following error codes:

| Code | Description |
|------|-------------|
| 400 | Bad Request - Invalid query or parameters |
| 401 | Unauthorized - Authentication failed |
| 403 | Forbidden - Insufficient permissions |
| 404 | Not Found - Resource doesn't exist |
| 429 | Too Many Requests - Rate limit exceeded |

## Rate Limits

The Cost Management API has rate limits. If you encounter 429 errors, implement exponential backoff retry logic.

## Additional Resources

- [Azure Cost Management REST API Documentation](https://learn.microsoft.com/en-us/rest/api/cost-management/)
- [Cost Management Best Practices](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/cost-mgt-best-practices)

## Support

For issues with this connector, please open an issue in the repository or contact the publisher.

# Azure Resource Graph

## Overview

A hybrid Power Platform MCP custom connector for [Azure Resource Graph](https://learn.microsoft.com/azure/governance/resource-graph/overview), enabling Copilot Studio agents and Power Automate / Power Apps to query Azure resources at scale using Kusto Query Language (KQL).

This connector provides:
- **21 MCP tools** for Copilot Studio agents (resource querying, governance, saved queries)
- **8 REST operations** for Power Automate / Power Apps (all Resource Graph API endpoints)
- **Azure AD OAuth** authentication (user delegated)

## Prerequisites

- An Azure subscription with resources to query
- An Entra ID (Azure AD) app registration with `https://management.azure.com/.default` scope
- Power Platform environment with custom connector support

## Supported Operations

### REST Operations (Power Automate / Power Apps)

| Operation | Method | Description |
|-----------|--------|-------------|
| Query Resources | POST | Execute a KQL query against Azure Resource Graph |
| List Operations | GET | List available Resource Graph API operations |
| List Saved Queries by Subscription | GET | List saved queries in a subscription |
| List Saved Queries | GET | List saved queries in a resource group |
| Get Saved Query | GET | Get a saved query by name |
| Create or Update Saved Query | PUT | Create or replace a saved query |
| Update Saved Query | PATCH | Partially update a saved query |
| Delete Saved Query | DELETE | Delete a saved query |

### MCP Tools (Copilot Studio)

#### Core Query Tools
| Tool | Description |
|------|-------------|
| `query_resources` | Execute arbitrary KQL queries with subscription/management group scoping and pagination |
| `list_tables` | List all ~40 Resource Graph tables with descriptions |
| `list_resource_types` | List distinct resource types, optionally filtered by prefix |

#### Infrastructure Discovery Tools
| Tool | Description |
|------|-------------|
| `list_subscriptions` | List accessible Azure subscriptions |
| `list_resource_groups` | List resource groups, optionally scoped by subscription |
| `list_management_groups` | List management groups |
| `get_resource_by_id` | Look up a resource by its full ARM resource ID |

#### Change Tracking Tools
| Tool | Description |
|------|-------------|
| `query_resource_changes` | Query resource config changes (last 14 days) |

#### Governance Tools
| Tool | Description |
|------|-------------|
| `query_policy_compliance` | Query Azure Policy compliance state |
| `query_advisor_recommendations` | Query Advisor recommendations by category |
| `query_security_assessments` | Query Defender for Cloud security assessments |
| `query_health_status` | Query resource health and availability |
| `query_service_health` | Query active service health events |
| `query_role_assignments` | Query role assignments (RBAC) |

#### Convenience Aggregation Tools
| Tool | Description |
|------|-------------|
| `summarize_resources` | Summarize resources by type, location, subscription, or resource group |
| `count_resources` | Count resources with optional filters |

#### Saved Query Tools
| Tool | Description |
|------|-------------|
| `list_saved_queries` | List saved Resource Graph queries |
| `get_saved_query` | Get a saved query by name |
| `create_saved_query` | Create or update a saved query |
| `delete_saved_query` | Delete a saved query |
| `run_saved_query` | Get and execute a saved query in one step |

## Setup

### 1. App Registration

1. Go to [Azure Portal > App Registrations](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Click **New registration**
3. Name: `Azure Resource Graph Connector`
4. Supported account types: **Accounts in any organizational directory**
5. Redirect URI: `https://global.consent.azure-apim.net/redirect`
6. After creation, note the **Application (client) ID**
7. Under **Certificates & secrets**, create a new client secret
8. Under **API permissions**, add:
   - `https://management.azure.com/user_impersonation` (delegated)
9. Grant admin consent

### 2. Deploy Connector

1. Install [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction)
2. Create a deploy copy and replace the client ID:
   - Copy `apiProperties.json` to `apiProperties.deploy.json`
   - Replace `[[REPLACE_WITH_CLIENT_ID]]` with your app registration client ID
3. Deploy:
   ```bash
   pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.deploy.json --script-file script.csx
   ```

### 3. Create Connection

1. In Power Automate or Copilot Studio, add the **Azure Resource Graph** connector
2. Sign in with your Azure AD credentials
3. The connection will use your identity to query resources (you'll only see resources you have read access to)

## Example KQL Queries

### Count all virtual machines
```kql
Resources
| where type =~ 'microsoft.compute/virtualmachines'
| summarize count()
```

### List resources by location
```kql
Resources
| summarize count() by location
| order by count_ desc
```

### Find resources with a specific tag
```kql
Resources
| where tags['environment'] =~ 'production'
| project name, type, resourceGroup, location
```

### Non-compliant policy resources
```kql
policyresources
| where type == 'microsoft.policyinsights/policystates'
| where properties.complianceState == 'NonCompliant'
| summarize count() by tostring(properties.policyDefinitionName)
```

### Recent resource changes
```kql
resourcechanges
| where properties.changeAttributes.timestamp > ago(24h)
| project properties.targetResourceId, properties.changeType, properties.changeAttributes.timestamp
| order by properties_changeAttributes_timestamp desc
```

### Unhealthy resources
```kql
healthresources
| where type == 'microsoft.resourcehealth/availabilitystatuses'
| where properties.availabilityState != 'Available'
| project properties.targetResourceId, properties.availabilityState
```

## Application Insights Logging

To enable telemetry, edit `script.csx` and replace the placeholder instrumentation key:

```csharp
private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
```

Replace with your Application Insights instrumentation key:

```csharp
private const string APP_INSIGHTS_KEY = "00000000-0000-0000-0000-000000000000";
```

Telemetry is optional — if the key is not configured, all logging is silently skipped.

## API Reference

- [Azure Resource Graph Overview](https://learn.microsoft.com/azure/governance/resource-graph/overview)
- [Supported Tables and Resource Types](https://learn.microsoft.com/azure/governance/resource-graph/reference/supported-tables-resources)
- [REST API 2024-04-01](https://github.com/Azure/azure-rest-api-specs/tree/master/specification/resourcegraph/resource-manager/Microsoft.ResourceGraph/stable/2024-04-01)
- [KQL Query Language Reference](https://learn.microsoft.com/azure/governance/resource-graph/concepts/query-language)
- [Starter Queries](https://learn.microsoft.com/azure/governance/resource-graph/samples/starter)
- [Advanced Queries](https://learn.microsoft.com/azure/governance/resource-graph/samples/advanced)
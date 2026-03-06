# Azure Arc Data Services

Power Platform custom connector for managing Azure Arc-enabled SQL Server instances, databases, availability groups, failover groups, SQL Server licenses, ESU licenses, data controllers, and SQL Managed Instances through Azure Resource Manager. Query Azure Resource Graph for cross-subscription inventory.

**Features:**
- **25 REST operations** covering instances, databases, availability groups, failover groups, licenses, ESU licenses, data controllers, managed instances, and Resource Graph
- **25 MCP tools** for Copilot Studio integration with AI agents
- **Azure AD OAuth** delegated authentication with user_impersonation scope
- **Full CRUD** for Arc-enabled SQL Server instance registrations, licenses, ESU licenses
- **PATCH support** for partial updates to instances (tags, license type, monitoring, backup policy)
- **Azure Resource Graph** query for cross-subscription inventory search
- **Application Insights** telemetry support for request/error monitoring

## Prerequisites

### Azure Requirements

1. **Azure subscription** with Azure Arc-enabled SQL Server instances
2. **Azure AD (Entra ID) app registration** with the following API permissions:
   - `https://management.azure.com/user_impersonation` (delegated)
3. **Role assignment** on the target subscription or resource group:
   - `Reader` for list/get operations
   - `Contributor` for create/update/delete operations

### App Registration Setup

1. Navigate to [Azure Portal > Microsoft Entra ID > App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Click **New registration**
3. Configure:
   - **Name**: Power Platform - Azure Arc Data Services
   - **Supported account types**: Accounts in any organizational directory (Multitenant)
   - **Redirect URI**: Web - `https://global.consent.azure-apim.net/redirect`
4. After creation, go to **API permissions**:
   - Click **Add a permission > Azure Service Management**
   - Select **Delegated permissions > user_impersonation**
   - Click **Grant admin consent** (if required by your org)
5. Go to **Certificates & secrets**:
   - Create a new **Client secret**
   - Copy the **Application (client) ID** and **Client secret value**

## Installation

### Using PAC CLI

```powershell
# Authenticate to your Power Platform environment
pac auth create --environment "https://yourorg.crm.dynamics.com"

# Create the connector
pac connector create --api-definition apiDefinition.swagger.json --api-properties apiProperties.json --script script.csx

# Or update an existing connector
pac connector update --connector-id <CONNECTOR_ID> --api-definition apiDefinition.swagger.json --api-properties apiProperties.json --script script.csx
```

### Manual Upload

1. Go to [Power Automate](https://make.powerautomate.com) or [Power Apps](https://make.powerapps.com)
2. Navigate to **Data > Custom Connectors**
3. Click **+ New custom connector > Import an OpenAPI file**
4. Upload `apiDefinition.swagger.json`
5. On the **Security** tab, enter your Client ID and Client Secret
6. On the **Code** tab, upload `script.csx` and enable the code toggle
7. Select all operations in the **Operations** dropdown
8. Save and test the connection

## Configuration

After creating the connector, update the `apiProperties.json`:

1. Replace `YOUR_CLIENT_ID` with your Application (client) ID from the app registration
2. When creating a connection, you will be prompted to sign in with Azure AD
3. The signed-in user must have appropriate Azure RBAC roles on the target subscription/resource group

## Application Insights (Optional)

To enable request and error telemetry:

1. Create an Application Insights resource in Azure Portal
2. Copy the **Connection String** from the resource overview
3. Paste the connection string into the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`
4. Redeploy the connector

The following events are logged:
- `RequestReceived` - every incoming request with operation ID and auth details
- `RequestCompleted` - successful completion with status code and duration
- `RequestError` - unhandled exceptions with error type and message
- `MCPRequest` - MCP JSON-RPC method received
- `MCPToolCall` - MCP tool invocation with tool name
- `MCPToolError` - MCP tool execution failures
- `OAuthFailure_Passthrough` - 401/403 responses on REST operations

## API Coverage

### SQL Server Instances (6 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListSqlServerInstancesBySubscription` | GET | List all Arc-enabled SQL Server instances across a subscription |
| `ListSqlServerInstances` | GET | List Arc-enabled SQL Server instances in a resource group |
| `GetSqlServerInstance` | GET | Get details of a specific instance (version, edition, status, licensing) |
| `CreateOrUpdateSqlServerInstance` | PUT | Register or update an Arc-enabled SQL Server instance |
| `UpdateSqlServerInstance` | PATCH | Partially update instance (tags, license type, monitoring, backup policy) |
| `DeleteSqlServerInstance` | DELETE | Remove an Arc-enabled SQL Server instance registration |

### Databases (2 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListDatabases` | GET | List all databases on an Arc-enabled SQL Server instance |
| `GetDatabase` | GET | Get database details (state, recovery mode, size, backup info) |

### Availability Groups (2 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListAvailabilityGroups` | GET | List Always On availability groups on an instance |
| `GetAvailabilityGroup` | GET | Get AG details including replicas, databases, and sync state |

### Failover Groups (2 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListFailoverGroups` | GET | List failover groups on an instance |
| `GetFailoverGroup` | GET | Get failover group details including role and partner info |

### SQL Server Licenses (4 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListSqlServerLicenses` | GET | List all SQL Server license resources in a subscription |
| `GetSqlServerLicense` | GET | Get details of a specific SQL Server license |
| `CreateOrUpdateSqlServerLicense` | PUT | Create or update a SQL Server license resource |
| `DeleteSqlServerLicense` | DELETE | Delete a SQL Server license resource |

### ESU Licenses (4 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListSqlServerEsuLicenses` | GET | List all Extended Security Update license resources |
| `GetSqlServerEsuLicense` | GET | Get details of a specific ESU license |
| `CreateOrUpdateSqlServerEsuLicense` | PUT | Create or update an ESU license resource |
| `DeleteSqlServerEsuLicense` | DELETE | Delete an ESU license resource |

### Data Controllers (2 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListDataControllers` | GET | List Azure Arc data controllers in a resource group |
| `GetDataController` | GET | Get details of a specific data controller |

### SQL Managed Instances (2 operations)

| Operation | Method | Description |
|-----------|--------|-------------|
| `ListSqlManagedInstances` | GET | List SQL Managed Instances in a resource group |
| `GetSqlManagedInstance` | GET | Get details of a specific SQL Managed Instance |

### Azure Resource Graph (1 operation)

| Operation | Method | Description |
|-----------|--------|-------------|
| `QueryResourceGraph` | POST | Execute a KQL query across subscriptions for Arc SQL resources |

## MCP Tools (Copilot Studio)

All REST operations are also available as MCP tools for use with Copilot Studio agents:

| Tool | Description |
|------|-------------|
| `listSqlServerInstancesBySubscription` | List all Arc SQL instances in a subscription |
| `listSqlServerInstances` | List Arc SQL instances in a resource group |
| `getSqlServerInstance` | Get instance details |
| `createOrUpdateSqlServerInstance` | Create or update instance registration |
| `updateSqlServerInstance` | Partially update instance (tags, license, monitoring, backup) |
| `deleteSqlServerInstance` | Delete instance registration |
| `listDatabases` | List databases on an instance |
| `getDatabase` | Get database details |
| `listAvailabilityGroups` | List availability groups |
| `getAvailabilityGroup` | Get availability group details |
| `listFailoverGroups` | List failover groups |
| `getFailoverGroup` | Get failover group details |
| `listSqlServerLicenses` | List SQL Server licenses in a subscription |
| `getSqlServerLicense` | Get SQL Server license details |
| `createOrUpdateSqlServerLicense` | Create or update a license |
| `deleteSqlServerLicense` | Delete a license |
| `listSqlServerEsuLicenses` | List ESU licenses in a subscription |
| `getSqlServerEsuLicense` | Get ESU license details |
| `createOrUpdateSqlServerEsuLicense` | Create or update an ESU license |
| `deleteSqlServerEsuLicense` | Delete an ESU license |
| `listDataControllers` | List data controllers in a resource group |
| `getDataController` | Get data controller details |
| `listSqlManagedInstances` | List SQL Managed Instances in a resource group |
| `getSqlManagedInstance` | Get SQL Managed Instance details |
| `queryResourceGraph` | Query Azure Resource Graph with KQL |

### Example Agent Prompts

- "List all Arc-enabled SQL Server instances in my subscription"
- "What databases are on the SQL Server instance named SQLPROD01?"
- "Show me the availability group details for AG-Finance"
- "What is the status and edition of my Arc SQL instances in the East US resource group?"
- "Update the license type of instance SQLPROD01 to PAYG"
- "List all SQL Server licenses in my subscription"
- "Show me all ESU licenses that are activated"
- "What data controllers are deployed in my resource group?"
- "Find all Arc SQL instances across all subscriptions using Resource Graph"

## Key Properties Returned

### SQL Server Instance
- **Version**: SQL Server 2012-2025
- **Edition**: Enterprise, Standard, Web, Developer, Express, Evaluation
- **Status**: Connected, Disconnected, Registered
- **License Type**: Paid, Free, HADR, PAYG, ServerCAL, LicenseOnly
- **Host Type**: Azure VM, Physical Server, VMware, AWS, GCP
- **Service Type**: Engine, SSAS, SSIS, SSRS, PBIRS
- **Azure Defender Status**: Protected, Unprotected
- **Backup Policy**: Retention days, full/differential/log backup intervals
- **Monitoring**: Enabled/disabled status
- **Authentication**: Windows, Mixed mode
- **Best Practices Assessment**: Enabled with cron schedule

### Database
- **State**: Online, Offline, Restoring, Recovering, Suspect, Emergency
- **Recovery Mode**: Full, Bulk-logged, Simple
- **Size/Space**: Size in MB and available space
- **Backup Info**: Last full, differential, and log backup timestamps
- **Compatibility Level**: SQL Server compatibility level
- **Encryption**: TDE status
- **Features**: In-Memory OLTP, Stretch Database, Auto Close/Shrink

### Availability Group
- **Replicas**: Name, role (Primary/Secondary), availability mode, failover mode, sync health
- **Databases**: Name, sync state, suspended status, commit participation
- **Configuration**: Failure condition level, health check timeout, DTC support

### SQL Server License
- **Billing Plan**: Paid, Free
- **Physical Cores**: Core count for licensing
- **Activation State**: Activated, Deactivated
- **Scope Type**: Subscription, ResourceGroup, Tenant

### ESU License
- **Version**: SQL Server 2012, SQL Server 2014
- **ESU Year**: 1, 2, or 3
- **Billing Plan**: Paid, Free
- **Termination Date**: ESU coverage end date

### Data Controller
- **Infrastructure**: Azure, GCP, AWS, Alibaba, on-premises
- **Last Upload**: Timestamp of last data upload from on-premises

### SQL Managed Instance
- **Data Controller**: Associated data controller resource
- **vCores**: CPU allocation
- **License Type**: BasePrice, LicenseIncluded, DisasterRecovery

## ARM API Versions

- **Azure Arc Data Services**: `2024-01-01` for all Microsoft.AzureArcData resources
- **Azure Resource Graph**: `2021-03-01` for cross-subscription queries

The `api-version` parameter is automatically set as an internal parameter on all REST operations and injected by the script for MCP tool calls.

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| 403 Forbidden | Missing RBAC role | Assign Reader/Contributor role on the subscription or resource group |
| 401 Unauthorized | Token expired or invalid | Re-authenticate the connection |
| No instances returned | Wrong subscription/resource group | Verify the subscription ID and resource group name |
| MCP tools not appearing | Connector not added to agent | Add the connector as an action in Copilot Studio |
| Resource Graph returns empty | No matching resources | Verify KQL query syntax and subscription scope |
| App Insights not logging | Empty connection string | Set `APP_INSIGHTS_CONNECTION_STRING` in script.csx |

## Resources

- [Azure Arc-enabled SQL Server Overview](https://learn.microsoft.com/en-us/azure/architecture/hybrid/azure-arc-sql-server)
- [Azure Arc Data Services REST API](https://learn.microsoft.com/en-us/rest/api/azurearcdata/)
- [Azure Resource Graph Overview](https://learn.microsoft.com/en-us/azure/governance/resource-graph/overview)
- [Power Platform Custom Connectors](https://learn.microsoft.com/en-us/connectors/custom-connectors/)
- [Application Insights Overview](https://learn.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview)

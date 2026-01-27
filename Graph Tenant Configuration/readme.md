# Graph Tenant Configuration

Microsoft Graph Unified Tenant Configuration Management (UTCM) API connector for Power Platform. This connector enables monitoring of tenant configuration drift, creating configuration baselines, and extracting configuration snapshots.

## Overview

The Unified Tenant Configuration Management API helps IT administrators:
- **Monitor Configuration Drift**: Detect when tenant settings change from a known baseline
- **Create Baselines**: Define expected configuration states for monitoring
- **Extract Snapshots**: Capture current tenant configuration for backup or migration
- **Track Results**: View monitoring run history and drift counts

This connector supports both direct API operations and MCP (Model Context Protocol) for use in Copilot Studio agents.

## Prerequisites

1. **Azure AD App Registration** with the following permissions:
   - `ConfigurationMonitoring.Read.All` - Read configuration monitors and monitoring results
   - `ConfigurationMonitoring.ReadWrite.All` - Create/update monitors and run snapshots
   - `User.Read` - Read user profile

2. **UTCM Service Principal** configured in your tenant (see [Set up UTCM Service Principal](#set-up-utcm-service-principal))

3. **Admin Consent** - These permissions require admin consent for your tenant

4. **Privileged Role** - User must have a privileged admin role to manage monitors

## Supported Workloads

The UTCM API supports monitoring these Microsoft 365 workloads:
- **Microsoft Defender** - Security settings and policies
- **Microsoft Entra** - Identity, access policies, conditional access (38 resource types)
- **Microsoft Exchange Online** - Mail flow, policies, connectors (58 resource types)
- **Microsoft Intune** - Device management, compliance policies (65+ resource types)
- **Microsoft Purview** - Data protection, compliance, DLP (28 resource types)
- **Microsoft Teams** - Meeting policies, messaging, calling (60+ resource types)

### Resource Types Reference

UTCM supports **300+ resource types** that can be monitored. When creating a configuration monitor, specify the resource types you want to track (e.g., `microsoft.entra.conditionalAccessPolicy`, `microsoft.teams.meetingPolicy`).

**Full documentation:**
- [Supported workloads and resource types](https://learn.microsoft.com/en-us/graph/utcm-supported-resourcetypes)
- [JSON Schema with all resource types](https://json.schemastore.org/utcm-monitor.json)

## Setup

### 1. Register an Azure AD Application

1. Go to [Azure Portal](https://portal.azure.com) → Microsoft Entra ID → App registrations
2. Click **New registration**
3. Enter a name (e.g., "Tenant Configuration Connector")
4. Set supported account types to **Accounts in any organizational directory**
5. Add redirect URI: `https://global.consent.azure-apim.net/redirect`
6. Click **Register**

### 2. Configure API Permissions

1. Go to **API permissions** → **Add a permission**
2. Select **Microsoft Graph**
3. Add the following **Delegated permissions**:
   - `ConfigurationMonitoring.Read.All`
   - `ConfigurationMonitoring.ReadWrite.All`
   - `User.Read`
4. Click **Grant admin consent**

### 3. Create Client Secret

1. Go to **Certificates & secrets** → **New client secret**
2. Enter a description and select expiration
3. Copy the **Value** (you'll need this for the connector)

### 4. Set up UTCM Service Principal

The UTCM service principal must be added to your tenant and granted permissions for the workloads you want to monitor.

**UTCM Service Principal App ID:** `03b07b79-c5bc-4b5e-9bfa-13acf4a99998`

#### Option A: Using PowerShell

```powershell
# Install required modules
Install-Module Microsoft.Graph.Authentication
Install-Module Microsoft.Graph.Applications

# Connect to Microsoft Graph
Connect-MgGraph -Scopes 'Application.ReadWrite.All'

# Create the UTCM service principal
New-MgServicePrincipal -AppId '03b07b79-c5bc-4b5e-9bfa-13acf4a99998'
```

#### Option B: Using Microsoft Graph API

```http
POST https://graph.microsoft.com/v1.0/servicePrincipals
Content-Type: application/json

{
  "appId": "03b07b79-c5bc-4b5e-9bfa-13acf4a99998"
}
```

### 5. Grant Permissions to UTCM Service Principal

Grant the UTCM service principal permissions for the workloads you want to monitor:

```powershell
# Example: Grant User.ReadWrite.All and Policy.Read.All permissions
$permissions = @('User.ReadWrite.All', 'Policy.Read.All')
$Graph = Get-MgServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"
$UTCM = Get-MgServicePrincipal -Filter "AppId eq '03b07b79-c5bc-4b5e-9bfa-13acf4a99998'"

foreach ($requestedPermission in $permissions) {
    $AppRole = $Graph.AppRoles | Where-Object { $_.Value -eq $requestedPermission }
    $body = @{
        AppRoleId = $AppRole.Id
        ResourceId = $Graph.Id
        PrincipalId = $UTCM.Id
    }
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $UTCM.Id -BodyParameter $body
}
```

> **Note:** For Exchange Online permissions, see [Application access policies in Exchange Online](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac).

### 6. Update Connector Configuration

Update `apiProperties.json` with your App Registration details:
- Replace `clientId` with your Application (client) ID

### 7. Deploy the Connector

#### Option A: Create New Connector

```powershell
pac connector create `
  --environment [ENVIRONMENT_ID] `
  --api-definition-file "apiDefinition.swagger.json" `
  --api-properties-file "apiProperties.json" `
  --script-file "script.csx"
```

#### Option B: Update Existing Connector

```powershell
pac connector update `
  --environment [ENVIRONMENT_ID] `
  --connector-id [CONNECTOR_ID] `
  --api-definition-file "apiDefinition.swagger.json" `
  --api-properties-file "apiProperties.json" `
  --script-file "script.csx"
```

## API Operations

### Configuration Monitors

| Operation | Description |
|-----------|-------------|
| **ListConfigurationMonitors** | Get all configuration monitors |
| **CreateConfigurationMonitor** | Create a new monitor with baseline |
| **GetConfigurationMonitor** | Get a specific monitor by ID |
| **UpdateConfigurationMonitor** | Update monitor name, description, or baseline |
| **DeleteConfigurationMonitor** | Delete a monitor and all its data |
| **GetMonitorBaseline** | Get the baseline attached to a monitor |

### Configuration Drifts

| Operation | Description |
|-----------|-------------|
| **ListConfigurationDrifts** | List all detected configuration drifts |
| **GetConfigurationDrift** | Get details of a specific drift |

### Configuration Snapshots

| Operation | Description |
|-----------|-------------|
| **ListConfigurationSnapshotJobs** | List all snapshot jobs (max 12 visible) |
| **GetConfigurationSnapshotJob** | Get a specific snapshot job |
| **DeleteConfigurationSnapshotJob** | Delete a snapshot to make room for new ones |
| **CreateSnapshotFromBaseline** | Create a new snapshot from a baseline |

### Monitoring Results

| Operation | Description |
|-----------|-------------|
| **ListConfigurationMonitoringResults** | List monitoring run results |
| **GetConfigurationMonitoringResult** | Get details of a specific run result |

### Baselines

| Operation | Description |
|-----------|-------------|
| **GetConfigurationBaseline** | Get baseline resources and parameters |

## MCP Integration (Copilot Studio)

This connector implements the Model Context Protocol for use as a Copilot Studio action. The MCP endpoint exposes the following tools:

### MCP Tools

| Tool | Description |
|------|-------------|
| `list_monitors` | List all configuration monitors |
| `create_monitor` | Create a new configuration monitor |
| `get_monitor` | Get a specific monitor by ID |
| `delete_monitor` | Delete a monitor |
| `list_drifts` | List all detected drifts |
| `get_drift` | Get drift details |
| `list_snapshots` | List all snapshot jobs |
| `get_snapshot` | Get snapshot job details |
| `create_snapshot` | Create a new snapshot |
| `delete_snapshot` | Delete a snapshot job |
| `list_results` | List monitoring run results |
| `get_baseline` | Get baseline details |

### MCP Prompts

| Prompt | Description |
|--------|-------------|
| `check_drift_status` | Check current drift status and summarize changes |
| `create_security_monitor` | Create a security-focused configuration monitor |
| `export_configuration` | Create a snapshot to export configuration |

### Using with Copilot Studio

1. Import the connector to your Power Platform environment
2. Create a connection using OAuth
3. In Copilot Studio, add the connector as an action
4. The MCP endpoint (`InvokeMCP`) will be available as an agentic action

## Example Scenarios

### Monitor Security Configuration

```
1. Create a monitor for security settings
   → Use CreateConfigurationMonitor with security-related resources
   
2. Wait for monitor to run (every 6 hours)
   → Or check ListConfigurationMonitoringResults for status
   
3. Check for drifts
   → Use ListConfigurationDrifts to see changes
   
4. Review specific changes
   → Use GetConfigurationDrift for detailed property changes
```

### Export Tenant Configuration

```
1. Get a baseline ID from an existing monitor
   → Use GetMonitorBaseline or GetConfigurationBaseline
   
2. Create a snapshot
   → Use CreateSnapshotFromBaseline with the baseline ID
   
3. Poll for completion
   → Use GetConfigurationSnapshotJob to check status
   
4. Download the snapshot
   → The resourceLocation property contains the download URL
```

### Clean Up Old Snapshots

```
1. List existing snapshots
   → Use ListConfigurationSnapshotJobs
   
2. Delete old snapshots (only 12 can be visible)
   → Use DeleteConfigurationSnapshotJob for unwanted snapshots
```

## Application Insights (Optional)

Enable telemetry to monitor connector usage, track errors, and analyze performance.

### Setup

1. Create an Application Insights resource in Azure Portal
2. Copy the **Connection String** from Overview → Connection String
3. Update `script.csx`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=xxx;IngestionEndpoint=https://xxx.in.applicationinsights.azure.com/;...";
```

### Telemetry Events

| Event | Description |
|-------|-------------|
| `OperationStarted` | API operation invoked |
| `OperationCompleted` | Operation finished with duration |
| `OperationError` | Operation failed |
| `McpRequest` | MCP method called |
| `ToolCallStarted` | MCP tool execution started |
| `ToolCallCompleted` | MCP tool finished |
| `ToolCallError` | MCP tool failed |
| `GraphApiError` | Microsoft Graph API returned error |

## Limitations

- Snapshot jobs: Maximum 12 visible at a time
- Monitor frequency: Fixed at 6-hour intervals
- API version: Beta (subject to change)
- Permissions: Requires admin consent

## References

- [UTCM API Overview](https://learn.microsoft.com/en-us/graph/api/resources/unified-tenant-configuration-management-api-overview?view=graph-rest-beta)
- [Configuration Monitor](https://learn.microsoft.com/en-us/graph/api/resources/configurationmonitor?view=graph-rest-beta)
- [Configuration Drift](https://learn.microsoft.com/en-us/graph/api/resources/configurationdrift?view=graph-rest-beta)
- [Configuration Snapshot Job](https://learn.microsoft.com/en-us/graph/api/resources/configurationsnapshotjob?view=graph-rest-beta)

## Support

For issues or questions about this connector, please open an issue in the repository.

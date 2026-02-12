# ServiceNow MCP

Comprehensive Power Platform custom connector for ServiceNow based on the **Zurich release** REST APIs. Provides full access to Table API, Attachment API, Aggregate API, Import Set API, CMDB Instance API, Service Catalog API, Batch API, Knowledge Management API, Email API, Performance Analytics API, Change Management API, User Administration API, and Event Management API.

**Features:**
- **93+ operations** covering all major ServiceNow REST APIs
- **Dynamic Table Schema** - Body fields auto-populate based on selected table
- **MCP-enabled** for Copilot Studio integration with AI agents
- **Application Insights** telemetry for monitoring and debugging

## Prerequisites

### ServiceNow Instance Requirements

1. **ServiceNow Zurich Instance** (or later)
2. **Required Plugins** (activated in ServiceNow):
   - `com.snc.platform.security.oauth` - OAuth 2.0
   - `com.glide.rest` - REST API Provider
   - `com.glide.auth.scope` - Authentication Scopes
   - `com.glide.rest.auth.scope` - REST API Auth Scope Plugin

3. **System Property Configuration**:
   - Navigate to **System Properties > All Properties**
   - Search for: `glide.oauth.inbound.client.credential.grant_type.enabled`
   - Set to: `true`

4. **Enable Unscoped API Access** (if you get "Access to unscoped api is not allowed" error):
   - Navigate to `sys_properties.list`
   - Search for: `glide.security.oauth_allow_unscoped_clients`
   - Create if missing, set to: `true`

### OAuth 2.0 Application Setup

1. Navigate to **System OAuth > Application Registry**
2. Click **New** and select **Create an OAuth API endpoint for external clients**
3. Configure the application:
   - **Name**: Power Platform Connector
   - **Client ID**: (auto-generated - copy this)
   - **Client Secret**: Click **Generate Secret** and copy
   - **Redirect URL**: `https://global.consent.azure-apim.net/redirect`
   - **Token Lifespan**: 3600 (or as desired)
   - **Refresh Token Lifespan**: 2592000 (30 days)
4. Save the application

## Installation

### Using PAC CLI

```powershell
# Authenticate to your Power Platform environment
pac auth create --environment "https://yourorg.crm.dynamics.com"

# Create the connector
pac connector create --api-definition apiDefinition.swagger.json --api-properties apiProperties.json

# Or update an existing connector
pac connector update --connector-id <YOUR_CONNECTOR_ID> --api-definition apiDefinition.swagger.json --api-properties apiProperties.json
```

### Manual Upload

1. Go to [Power Automate](https://make.powerautomate.com) or [Power Apps](https://make.powerapps.com)
2. Navigate to **Data > Custom Connectors**
3. Click **+ New custom connector > Import an OpenAPI file**
4. Upload `apiDefinition.swagger.json`
5. Configure connection settings with your OAuth credentials

## Configuration

After creating the connector, update the `apiProperties.json`:

1. Replace `[REPLACE_WITH_CLIENT_ID]` with your ServiceNow OAuth Client ID
2. When creating a connection, you'll be prompted for:
   - **ServiceNow Instance**: Your instance name (e.g., `dev12345` for `dev12345.service-now.com`)
   - **OAuth Sign-in**: Will redirect to ServiceNow for authentication

## API Coverage

This connector includes **93+ operations** across 13 ServiceNow REST APIs:

### Table API (7 operations)
| Operation | Description |
|-----------|-------------|
| `ListRecords` | Retrieve multiple records from any table |
| `GetRecord` | Get a single record by sys_id |
| `CreateRecord` | Create a new record (dynamic fields) |
| `UpdateRecord` | Partial update - PATCH (dynamic fields) |
| `ReplaceRecord` | Full replacement - PUT (dynamic fields) |
| `DeleteRecord` | Delete a record |
| `GetTableSchema` | Get field schema for a table (internal) |

### Attachment API (5 operations)
| Operation | Description |
|-----------|-------------|
| `ListAttachments` | List attachment metadata |
| `GetAttachmentMetadata` | Get single attachment info |
| `DownloadAttachment` | Download file content |
| `UploadAttachment` | Upload binary file |
| `DeleteAttachment` | Delete an attachment |

### Aggregate API (1 operation)
| Operation | Description |
|-----------|-------------|
| `GetTableStatistics` | Count, sum, avg, min, max with grouping |

### Import Set API (3 operations)
| Operation | Description |
|-----------|-------------|
| `ImportRecord` | Import single record with transform |
| `ImportMultipleRecords` | Bulk import with async transform |
| `GetImportResult` | Check import/transform status |

### CMDB Instance API (7 operations)
| Operation | Description |
|-----------|-------------|
| `CMDB_ListCIs` | List Configuration Items by class |
| `CMDB_GetCI` | Get CI with relationships |
| `CMDB_CreateCI` | Create CI with relations |
| `CMDB_UpdateCI` | Full CI update |
| `CMDB_PatchCI` | Partial CI update |
| `CMDB_AddRelations` | Add CI relationships |
| `CMDB_DeleteRelation` | Remove a relationship |

### Service Catalog API (25+ operations)
| Category | Operations |
|----------|------------|
| **Catalogs** | List, Get, Get Categories |
| **Categories** | Get details |
| **Items** | List, Get, Order Now, Add to Cart, Submit Producer, Add to Wishlist, Checkout Guide, Submit Guide |
| **Cart** | Get, Update Item, Delete Item, Empty, Checkout, Submit Order, Get Delivery Address |
| **Wishlist** | Get, Get Item |
| **Delegation** | Check Rights, Get Invalid Users |
| **Variables** | Get Display Value |

### Batch API (1 operation)
| Operation | Description |
|-----------|-------------|
| `Batch_SendRequests` | Execute multiple API calls in one request |

### Knowledge Management API (9 operations)
| Operation | Description |
|-----------|-------------|
| `Knowledge_SearchArticles` | Search knowledge articles |
| `Knowledge_GetArticle` | Get article by sys_id |
| `Knowledge_RecordView` | Record article view |
| `Knowledge_RateArticle` | Submit article rating |
| `Knowledge_MarkHelpful` | Mark article helpful/not helpful |
| `Knowledge_FlagArticle` | Flag for review |
| `Knowledge_ListCategories` | List KB categories |
| `Knowledge_ListKnowledgeBases` | List knowledge bases |

### Email Outbound API (1 operation)
| Operation | Description |
|-----------|-------------|
| `Email_Send` | Send outbound email |

### Performance Analytics API (6 operations)
| Operation | Description |
|-----------|-------------|
| `PA_ListScorecards` | List scorecards |
| `PA_GetScorecard` | Get scorecard with indicators |
| `PA_ListIndicators` | List PA indicators |
| `PA_GetIndicatorScores` | Get indicator scores |
| `PA_ListBreakdownSources` | List breakdown sources |

### Change Management API (14 operations)
| Operation | Description |
|-----------|-------------|
| `Change_ListChanges` | List change requests |
| `Change_GetChange` | Get change details |
| `Change_CreateNormal` | Create normal change |
| `Change_CreateStandard` | Create standard change from template |
| `Change_ListStandardTemplates` | List standard change templates |
| `Change_CreateEmergency` | Create emergency change |
| `Change_GetRiskAssessment` | Get risk assessment |
| `Change_CalculateRisk` | Calculate risk |
| `Change_CheckConflicts` | Check schedule conflicts |
| `Change_GetApprovals` | Get approval records |
| `Change_GetTasks` | Get change tasks |
| `Change_CreateTask` | Create change task |

### User Administration API (4 operations)
| Operation | Description |
|-----------|-------------|
| `User_IdentifyReconcile` | Identify or create/update user |
| `Group_ListMembers` | List group members |
| `Group_AddMember` | Add user to group |
| `Group_RemoveMember` | Remove user from group |

### Event Management API (8 operations)
| Operation | Description |
|-----------|-------------|
| `Event_CreateEvent` | Push event to Event Management |
| `Event_GetAlerts` | List alerts |
| `Event_GetAlert` | Get alert by sys_id |
| `Event_AcknowledgeAlert` | Acknowledge an alert |
| `Event_UnacknowledgeAlert` | Remove acknowledgement |
| `Event_CloseAlert` | Close an alert |
| `Event_ReopenAlert` | Reopen closed alert |
| `Event_AddAlertComment` | Add comment to alert |

## Common Query Parameters

Most GET operations support these parameters:

| Parameter | Description | Example |
|-----------|-------------|---------|
| `sysparm_query` | Encoded query filter | `active=true^priority=1` |
| `sysparm_fields` | Fields to return | `sys_id,number,short_description` |
| `sysparm_limit` | Max records (default: 100) | `50` |
| `sysparm_offset` | Pagination offset | `100` |
| `sysparm_display_value` | Value format | `true`, `false`, `all` |

## Example Usage

### Create an Incident

```json
POST /now/table/incident
{
  "short_description": "VPN connection issue",
  "description": "User unable to connect to VPN from home office",
  "urgency": "2",
  "impact": "2",
  "caller_id": "<user_sys_id>"
}
```

### Query High Priority Incidents

```
GET /now/table/incident?sysparm_query=priority=1^state!=7&sysparm_fields=number,short_description,assigned_to&sysparm_limit=10
```

### Order a Catalog Item

```json
POST /sn_sc/servicecatalog/items/{sys_id}/order_now
{
  "sysparm_quantity": 1,
  "sysparm_requested_for": "<user_sys_id>",
  "variables": {
    "laptop_model": "Dell XPS 15",
    "memory": "32GB"
  }
}
```

### Batch Multiple Requests

```json
POST /now/batch
{
  "batch_request_id": "my-batch-001",
  "rest_requests": [
    {
      "id": "1",
      "method": "GET",
      "url": "/api/now/table/incident?sysparm_limit=5"
    },
    {
      "id": "2",
      "method": "GET",
      "url": "/api/now/table/sys_user?sysparm_limit=5"
    }
  ]
}
```

### Search Knowledge Articles

```
GET /sn_km/knowledge/articles?query=password+reset&language=en&limit=10
```

### Create Normal Change Request

```json
POST /sn_chg_rest/change/normal
{
  "short_description": "Server patch deployment",
  "description": "Apply latest security patches to production servers",
  "justification": "Critical security vulnerabilities need to be addressed",
  "implementation_plan": "1. Backup servers\n2. Apply patches\n3. Reboot\n4. Verify services",
  "backout_plan": "Restore from backup if issues arise",
  "assignment_group": "<group_sys_id>",
  "start_date": "2026-02-15 02:00:00",
  "end_date": "2026-02-15 04:00:00"
}
```

### Send Email Notification

```json
POST /now/email
{
  "to": "user@example.com",
  "cc": "manager@example.com",
  "subject": "Change Request Approved",
  "body": "Your change request CHG0012345 has been approved.",
  "importance": "normal"
}
```

### Get Performance Analytics Indicator Scores

```
GET /now/pa/indicators/{uuid}/scores?sysparm_breakdown={breakdown_uuid}&sysparm_display_value=true
```

### Push Event to Event Management

```json
POST /now/em/event
{
  "source": "Azure Monitor",
  "node": "prod-web-01",
  "type": "CPU Utilization",
  "severity": 3,
  "resource": "CPU",
  "description": "CPU utilization exceeded 90%",
  "message_key": "prod-web-01-cpu-high"
}
```

### Acknowledge an Alert

```
POST /now/em/alert/{sys_id}/acknowledge
```

## Query Syntax Reference

ServiceNow uses encoded query syntax for filtering:

| Operator | Syntax | Example |
|----------|--------|---------|
| Equals | `field=value` | `active=true` |
| Not Equals | `field!=value` | `state!=7` |
| Contains | `fieldLIKEvalue` | `short_descriptionLIKEvpn` |
| Starts With | `fieldSTARTSWITHvalue` | `numberSTARTSWITHINC` |
| Greater Than | `field>value` | `priority>2` |
| Less Than | `field<value` | `priority<3` |
| In List | `fieldINlist` | `stateIN1,2,3` |
| AND | `^` | `active=true^priority=1` |
| OR | `^OR` | `priority=1^ORpriority=2` |
| Order By | `^ORDERBY` | `^ORDERBYsys_created_on` |
| Order Desc | `^ORDERBYDESC` | `^ORDERBYDESCpriority` |

## Common Tables

| Table | Description |
|-------|-------------|
| `incident` | Incident Management |
| `sc_request` | Service Catalog Requests |
| `sc_req_item` | Requested Items |
| `sc_task` | Catalog Tasks |
| `change_request` | Change Management |
| `problem` | Problem Management |
| `sys_user` | Users |
| `sys_user_group` | Groups |
| `cmdb_ci` | Configuration Items (base) |
| `cmdb_ci_computer` | Computers |
| `cmdb_ci_server` | Servers |
| `kb_knowledge` | Knowledge Articles |

## Dynamic Table Schema

The connector includes dynamic schema support for Table API operations. When you select a table name in Power Automate or Power Apps, the body input fields automatically populate with the actual fields from that ServiceNow table.

**How it works:**
1. Select a table (e.g., `incident`, `change_request`)
2. The connector queries ServiceNow's `sys_dictionary` for that table's field definitions
3. Body parameters show table-specific fields with proper labels and types

**Supported operations:**
- CreateRecord
- UpdateRecord  
- ReplaceRecord

**Note:** System fields (prefixed with `sys_`) are excluded from dynamic schema to reduce clutter.

## Copilot Studio Integration (MCP)

This connector includes Model Context Protocol (MCP) support for integration with Copilot Studio AI agents. The MCP endpoint exposes all ServiceNow operations as tools that AI agents can invoke.

### Available MCP Tools

| Tool | Description |
|------|-------------|
| `list_records` | Query any ServiceNow table |
| `get_record` | Get record by sys_id |
| `create_record` | Create record in any table |
| `update_record` | Update record fields |
| `create_incident` | Quick incident creation |
| `search_knowledge` | Search KB articles |
| `list_changes` | List change requests |
| `create_normal_change` | Create change request |
| `create_event` | Push event to EM |
| `list_alerts` | List EM alerts |
| `acknowledge_alert` | Acknowledge alert |
| `close_alert` | Close alert |
| `list_cis` | Query CMDB |
| `list_catalog_items` | Browse catalog |
| `order_catalog_item` | Order from catalog |
| `send_email` | Send email |
| `get_table_stats` | Aggregate queries |
| `batch_requests` | Multiple API calls |

### Using with Copilot Studio

1. Create a new agent in Copilot Studio
2. Add this connector as an action
3. The AI agent will automatically discover available tools
4. Configure appropriate permissions for the service account

## Application Insights Logging

To enable telemetry and monitoring, configure Application Insights:

1. Create an Application Insights resource in Azure
2. Copy the connection string
3. Update `script.csx` and set `APP_INSIGHTS_CONNECTION_STRING`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=xxx;IngestionEndpoint=https://xxx.in.applicationinsights.azure.com/";
```

### Logged Events

| Event | Properties |
|-------|------------|
| `RequestReceived` | CorrelationId, OperationId, Path |
| `RequestCompleted` | CorrelationId, StatusCode, DurationMs |
| `RequestError` | CorrelationId, ErrorMessage, ErrorType |
| `MCPRequest` | CorrelationId, Method, HasParams |
| `MCPToolCall` | CorrelationId, Tool, HasArguments |
| `MCPToolError` | CorrelationId, Tool, ErrorMessage |

## Troubleshooting

### Common Issues

1. **401 Unauthorized**
   - Verify OAuth credentials are correct
   - Ensure user has appropriate roles (e.g., `itil`, `catalog`)
   - Check token hasn't expired

2. **403 Forbidden**
   - User lacks table/record ACL permissions
   - CMDB operations require ITIL role

3. **404 Not Found**
   - Verify table name is correct
   - Check sys_id exists
   - Confirm staging table exists for import operations

4. **OAuth Token Fails**
   - Verify `glide.oauth.inbound.client.credential.grant_type.enabled = true`
   - Check redirect URL matches exactly
   - Ensure OAuth plugins are activated

5. **"Access to unscoped api is not allowed"**
   - Set `glide.security.oauth_allow_unscoped_clients = true` in sys_properties
   - Or add specific OAuth scopes to your application registry

### Testing with Postman

1. Import the swagger file into Postman
2. Configure OAuth 2.0 authorization
3. Set instance variable for your ServiceNow instance
4. Test individual endpoints before using in Power Platform

## Resources

- [ServiceNow REST API Documentation](https://developer.servicenow.com/dev.do#!/reference/api/zurich/rest/)
- [ServiceNow OAuth Setup Guide](https://docs.servicenow.com/bundle/zurich-platform-security/page/administer/security/concept/oauth-application-registry.html)
- [Power Platform Custom Connectors](https://docs.microsoft.com/en-us/connectors/custom-connectors/)

## License

This connector is provided as-is for educational and development purposes.

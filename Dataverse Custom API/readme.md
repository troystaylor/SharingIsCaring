# Dataverse Custom API Power MCP

## Overview

Dataverse Custom API Power MCP is a metadata-driven Model Context Protocol server that **automatically discovers** Custom APIs from your Dataverse environment and exposes them as MCP tools for Microsoft Copilot Studio. Unlike hardcoded connectors, this solution adapts to your environment—any Custom API you create is immediately available without code changes.

## Key Features

### Automatic Discovery
- **Metadata-Driven**: Discovers Custom APIs from Dataverse `customapis` table at runtime
- **Zero Configuration**: No hardcoding—adapts to any environment automatically
- **Smart Caching**: 30-minute TTL reduces API calls while staying current
- **Private API Support**: Configurable filtering to include/exclude private Custom APIs

### Complete Custom API Support
- **All Binding Types**: Global unbound (0), Entity-bound (1), EntityCollection-bound (2)
- **Functions & Actions**: Handles GET (Functions) with URL parameters and POST (Actions) with body parameters
- **All 13 Parameter Types**: Boolean, DateTime, Decimal, Entity, EntityCollection, EntityReference, Float, Integer, Money, Picklist, String, StringArray, Guid
- **Open Types**: Validates and auto-injects `@odata.type` for Entity/EntityCollection parameters without `LogicalEntityName`

### Custom API Management
- **Create Custom APIs**: Define new Custom APIs from Copilot Studio with full parameter support
- **Add Parameters & Properties**: Build request parameters and response properties dynamically
- **Update & Delete**: Modify Custom API metadata or remove APIs with cascading delete
- **Solution Integration**: Create Custom APIs directly in solutions for proper ALM
- **Self-Bootstrapping**: New Custom APIs immediately appear as MCP tools after creation
- **List Solutions**: Discover available solutions (managed/unmanaged) for API placement

### Production Features
- **Correlation IDs**: Request tracing with `x-ms-correlation-request-id` headers for debugging
- **OAuth Token Forwarding**: Seamless authentication passthrough to Dataverse Web API
- **OData 4.0 Headers**: Full compliance with Dataverse Web API standards
- **Comprehensive Error Handling**: Detailed errors with correlation IDs for troubleshooting
- **Entity Set Mapping**: 35+ common entities mapped, automatic pluralization for custom entities

## Operations

### InvokeMCP
**Single MCP protocol endpoint** (`/mcp`) supporting JSON-RPC 2.0 methods:

| Method | Description | Returns |
|--------|-------------|---------|
| `initialize` | Protocol handshake with capability negotiation | Server info, protocol version, capabilities |
| `notifications/initialized` | Confirms initialization complete | HTTP 200 OK |
| `tools/list` | Discovers Custom APIs + lists 6 management tools | Array of tool definitions with inputSchema |
| `tools/call` | Executes Custom API or management tool | Content array with execution results |
| `ping` | Health check | Empty result object |

### Management Tools (exposed via tools/list)
**6 built-in tools for Custom API lifecycle management:**

| Tool Name | Description | Required Privilege |
|-----------|-------------|--------------------|
| `dataverse_management_create_custom_api` | Create new Custom API definition | `prvCreateCustomAPI` |
| `dataverse_management_create_api_parameter` | Add request parameter to Custom API | `prvCreateCustomAPIRequestParameter` |
| `dataverse_management_create_api_property` | Add response property to Custom API | `prvCreateCustomAPIResponseProperty` |
| `dataverse_management_update_custom_api` | Update Custom API metadata (description, private flag) | `prvWriteCustomAPI` |
| `dataverse_management_delete_custom_api` | Delete Custom API (cascades to params/properties) | `prvDeleteCustomAPI` |
| `dataverse_management_list_solutions` | List solutions for ALM (managed/unmanaged filter) | `prvReadSolution` |

**Key Behaviors:**
- `tools/list`: Cached for 30 minutes per environment; discovers all Custom APIs matching filter criteria + 6 management tools
- `tools/call`: Routes management tools first, then validates Custom API tool name format (`dataverse_{uniquename}`)
- Management tools auto-invalidate cache after CRUD operations for immediate discovery
- All methods return JSON-RPC 2.0 compliant responses with `jsonrpc`, `id`, and `result` or `error`

## Azure AD App Setup

### 1. Register Application
1. Navigate to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Click **New registration**
3. **Name**: `Dataverse Custom API MCP` (or your preferred name)
4. **Supported account types**: **Accounts in any organizational directory (Any Azure AD directory - Multitenant)**
5. **Redirect URI**: 
   - Platform: **Web**
   - URI: `https://global.consent.azure-apim.net/redirect`
6. Click **Register**

### 2. Configure API Permissions
1. Go to **API permissions** → **Add a permission**
2. Select **Dynamics CRM**
3. Choose **Delegated permissions**
4. Check **user_impersonation**
5. Click **Add permissions**
6. *Optional*: Grant admin consent for your organization

### 3. Note Application Details
- **Client ID**: Copy from the Overview page (used in `apiProperties.json`)
- Example: `a08ac176-6f85-4b74-b580-0dfe304ac725`

## Connection Parameters

**environmentUrl** (Required)
- Dataverse environment subdomain (e.g., `myorg` for `https://myorg.crm.dynamics.com`)
- Provided when creating connection in Power Platform
- Used to construct resource URI: `https://{environmentUrl}.crm.dynamics.com`

**OAuth 2.0 Configuration**
- **Login URI**: `https://login.microsoftonline.com`
- **Tenant ID**: `common` (multi-tenant support)
- **Authorization URL**: `https://login.microsoftonline.com/common/oauth2/authorize`
- **Token URL**: `https://login.microsoftonline.com/common/oauth2/token`

## Configuration Options

### Private Custom APIs
By default, private Custom APIs (IsPrivate=true) are filtered out. To include them:

1. Open `script.csx`
2. Locate line 16: `private const bool INCLUDE_PRIVATE_APIS = false;`
3. Change to: `private const bool INCLUDE_PRIVATE_APIS = true;`
4. Save and update connector in Power Platform

**Note**: Private APIs are typically internal/system APIs. Only enable if you need to expose them to Copilot Studio.

## Deployment

### Prerequisites
```powershell
# Install Power Platform CLI
winget install Microsoft.PowerPlatformCLI

# Authenticate
paconn login
```

### Validation
```powershell
cd "Dataverse Custom API"
paconn validate --api-def apiDefinition.swagger.json
```

### Import to Power Platform
1. Navigate to Power Platform maker portal
2. **Data** → **Custom connectors** → **New custom connector** → **Import an OpenAPI file**
3. Upload `apiDefinition.swagger.json`
4. Enable custom code on **Code** tab
5. Paste contents of `script.csx`
6. **Create connector**
7. Test connection with OAuth flow

## Usage Examples

### MCP Protocol Handshake (Initialize)

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": {},
    "clientInfo": {
      "name": "copilot-studio",
      "version": "1.0.0"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": {
      "tools": { "listChanged": false }
    },
    "serverInfo": {
      "name": "dataverse-custom-api-mcp",
      "version": "1.0.0"
    }
  }
}
```

### Discover Custom APIs (tools/list)

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list"
}
```

**Response Example:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "tools": [
      {
        "name": "dataverse_my_CalculateDiscount",
        "description": "Calculate discount for opportunity (Global unbound Function)",
        "inputSchema": {
          "type": "object",
          "properties": {
            "OpportunityId": {
              "type": "string",
              "format": "uuid",
              "description": "Opportunity GUID"
            },
            "DiscountPercent": {
              "type": "number",
              "description": "Discount percentage"
            }
          },
          "required": ["OpportunityId", "DiscountPercent"]
        }
      },
      {
        "name": "dataverse_my_UpdateAccountStatus",
        "description": "Update account status (Bound to account entity Action)",
        "inputSchema": {
          "type": "object",
          "properties": {
            "Target": {
              "type": "string",
              "description": "GUID of the account record"
            },
            "NewStatus": {
              "type": "string",
              "description": "New status value"
            }
          },
          "required": ["Target", "NewStatus"]
        }
      }
    ]
  }
}
```

### Execute Global Unbound Function (tools/call)

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "dataverse_my_CalculateDiscount",
    "arguments": {
      "OpportunityId": "12345678-1234-1234-1234-123456789012",
      "DiscountPercent": 15.5
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"DiscountedAmount\":8500.00,\"Message\":\"Discount applied successfully\"}"
      }
    ]
  }
}
```

### Execute Entity-Bound Action (tools/call)

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "dataverse_my_UpdateAccountStatus",
    "arguments": {
      "Target": "87654321-4321-4321-4321-210987654321",
      "NewStatus": "Active"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"Success\":true,\"UpdatedOn\":\"2026-01-06T10:30:00Z\"}"
      }
    ]
  }
}
```

### Execute with EntityReference Parameter

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "dataverse_my_LinkContacts",
    "arguments": {
      "PrimaryContact": {
        "logicalName": "contact",
        "id": "11111111-1111-1111-1111-111111111111"
      },
      "RelatedAccount": {
        "logicalName": "account",
        "id": "22222222-2222-2222-2222-222222222222"
      }
    }
  }
}
```

### Execute with Open Type Entity Parameter

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "tools/call",
  "params": {
    "name": "dataverse_my_CreateRecord",
    "arguments": {
      "Record": {
        "@odata.type": "Microsoft.Dynamics.CRM.contact",
        "firstname": "John",
        "lastname": "Doe",
        "emailaddress1": "john.doe@example.com"
      }
    }
  }
}
```

**Note**: Open type parameters (Entity/EntityCollection without `LogicalEntityName`) **require** `@odata.type` property. Typed parameters auto-inject `@odata.type` if not provided.

### Management Tools Examples

#### List Solutions (for ALM)
**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "tools/call",
  "params": {
    "name": "dataverse_management_list_solutions",
    "arguments": {
      "IncludeManaged": false
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Found 3 solution(s):\n\n- My Custom Solutions (MyPublisher)\n  Version: 1.0.0.0\n  Type: Unmanaged\n  Description: Custom APIs for business logic\n\n- Common Data Services Default Solution (Default)\n  Version: 1.0\n  Type: Unmanaged\n..."
      }
    ]
  }
}
```

#### Create Custom API
**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "tools/call",
  "params": {
    "name": "dataverse_management_create_custom_api",
    "arguments": {
      "UniqueName": "new_ValidateEmail",
      "DisplayName": "Validate Email Address",
      "Description": "Validates email format and checks against blocklist",
      "BindingType": 0,
      "IsFunction": true,
      "IsPrivate": false,
      "SolutionUniqueName": "MyPublisher"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Custom API 'new_ValidateEmail' created successfully. ID: 12345678-abcd-efgh-ijkl-1234567890ab"
      }
    ]
  }
}
```

**Note**: After creation, call `tools/list` again (or wait for cache expiry) to see the new `dataverse_new_ValidateEmail` tool.

#### Add Request Parameter
**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 9,
  "method": "tools/call",
  "params": {
    "name": "dataverse_management_create_api_parameter",
    "arguments": {
      "CustomAPIUniqueName": "new_ValidateEmail",
      "UniqueName": "EmailAddress",
      "Name": "EmailAddress",
      "DisplayName": "Email Address",
      "Description": "Email address to validate",
      "Type": 10,
      "IsOptional": false
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 9,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Parameter 'EmailAddress' (String) added to Custom API 'new_ValidateEmail'"
      }
    ]
  }
}
```

#### Add Response Property
**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "dataverse_management_create_api_property",
    "arguments": {
      "CustomAPIUniqueName": "new_ValidateEmail",
      "UniqueName": "IsValid",
      "Name": "IsValid",
      "DisplayName": "Is Valid",
      "Description": "True if email is valid",
      "Type": 0
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Response property 'IsValid' (Boolean) added to Custom API 'new_ValidateEmail'"
      }
    ]
  }
}
```

#### Update Custom API
**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "method": "tools/call",
  "params": {
    "name": "dataverse_management_update_custom_api",
    "arguments": {
      "UniqueName": "new_ValidateEmail",
      "Description": "Validates email format, checks blocklist, and verifies DNS MX records",
      "IsPrivate": true
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Custom API 'new_ValidateEmail' updated successfully"
      }
    ]
  }
}
```

#### Delete Custom API
**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 12,
  "method": "tools/call",
  "params": {
    "name": "dataverse_management_delete_custom_api",
    "arguments": {
      "UniqueName": "new_ValidateEmail"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 12,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Custom API 'new_ValidateEmail' deleted successfully. All parameters and response properties were also deleted."
      }
    ]
  }
}
```

**Warning**: Deletion is permanent. Ensure Custom API is not referenced by other components before deleting.

## Custom API Discovery Details

### Metadata Query
The connector retrieves Custom API metadata from Dataverse using a single optimized query:

```
GET /api/data/v9.2/customapis
  ?$select=uniquename,displayname,description,bindingtype,boundentitylogicalname,isfunction,isprivate
  &$expand=CustomAPIRequestParameters($select=uniquename,name,displayname,description,type,logicalentityname,isoptional),
           CustomAPIResponseProperties($select=uniquename,name,displayname,description,type,logicalentityname)
  &$filter=isprivate eq false  // When INCLUDE_PRIVATE_APIS=false
```

### Binding Types
- **0 - Global Unbound**: `/api/data/v9.2/{uniquename}`
- **1 - Entity-Bound**: `/api/data/v9.2/{entityset}({id})/Microsoft.Dynamics.CRM.{uniquename}` (auto-adds `Target` parameter)
- **2 - EntityCollection-Bound**: `/api/data/v9.2/{entityset}/Microsoft.Dynamics.CRM.{uniquename}`

### Parameter Types (13 Supported)
| Dataverse Type | JSON Schema | Notes |
|----------------|-------------|-------|
| Boolean (0) | `boolean` | Lowercase in URLs (`true`/`false`) |
| DateTime (1) | `string` | Format: `date-time` (ISO 8601) |
| Decimal (2) | `number` | Precision preserved |
| Entity (3) | `object` | Requires `@odata.type` for open types |
| EntityCollection (4) | `array` | Validates `@odata.type` on each entity |
| EntityReference (5) | `object` | Converts to `{@odata.type, {logicalName}id}` |
| Float (6) | `number` | Standard JSON number |
| Integer (7) | `integer` | Whole numbers |
| Money (8) | `number` | Wrapped in `{Value: x}` for body params |
| Picklist (9) | `integer` | Option set value |
| String (10) | `string` | Quoted/escaped in URLs |
| StringArray (11) | `array` | Array of strings |
| Guid (12) | `string` | Format: `uuid` |

### Caching Strategy
- **Cache Key**: Environment URL (e.g., `myorg.crm.dynamics.com`)
- **TTL**: 30 minutes
- **Invalidation**: Automatic on expiry; no manual refresh needed
- **Scope**: Static dictionary (persists across requests in same runtime instance)

## Limitations & Capabilities

### What This Connector Does
- **Discovers** Custom APIs automatically from metadata
- **Executes** Custom APIs with proper parameter formatting
- **Creates** Custom API definitions via management tools (NEW in 2.0)
- **Modifies** Custom API metadata (description, private flag) (NEW in 2.0)
- **Deletes** Custom APIs with cascading parameter/property removal (NEW in 2.0)
- **Routes** to correct binding type (Global/Entity/EntityCollection)
- **Forwards** OAuth authentication to Dataverse
- **Returns** response properties or full response
- **Invalidates cache** automatically after CRUD operations (NEW in 2.0)

### What This Connector Does NOT Do
This connector provides Custom API **definition management and execution**. It does not:
- Implement Custom API business logic (logic is in plug-ins registered on the Custom API)
- Control Custom API security privileges (`ExecutePrivilegeName`)
- Manage Custom API extension points (`AllowedCustomProcessingStepType`)
- Enable/disable Custom APIs for Power Automate workflows (`WorkflowSdkStepEnabled`)
- Catalog Custom APIs as business events (for Power Automate triggers)
- Register plug-in assemblies or types (use Plugin Registration Tool)

**Note**: Business logic, security, and workflow integration are configured **on the Custom API itself** in Dataverse, not in this connector.

### Custom API Features Respected by Connector
| Custom API Feature | How Connector Handles It |
|--------------------|--------------------------|
| `ExecutePrivilegeName` | Dataverse enforces privilege check; connector returns privilege error if user lacks permission |
| `AllowedCustomProcessingStepType` | Connector executes main operation; Dataverse handles registered plug-in steps (None/Async Only/Sync and Async) |
| `WorkflowSdkStepEnabled` | Not applicable (connector bypasses workflow designer, calls Web API directly) |
| `IsCustomizable` managed property | Connector reflects current state; respects if API locked in managed solution |
| Business Events | Connector can trigger async logic if Custom API configured with `AllowedCustomProcessingStepType=Async Only` |

### Security Considerations
- **OAuth Token**: User's token forwarded to Dataverse; user must have appropriate security roles
- **Execute Privileges**: Custom APIs with `ExecutePrivilegeName` require user to have that privilege via security role
- **Table Permissions**: Entity-bound APIs (BindingType=1) require read access to bound entity record
- **Private APIs**: `INCLUDE_PRIVATE_APIS=false` hides private APIs from discovery (IsPrivate=true excluded from metadata)
- **Management Tools (NEW in 2.0)**: Creating/modifying/deleting Custom APIs requires highly privileged security roles:
  - `prvCreateCustomAPI`, `prvWriteCustomAPI`, `prvDeleteCustomAPI` (System Customizer role or higher)
  - Solution-aware creation requires `prvReadSolution` and write access to target solution
  - **Best Practice**: Restrict management tool usage to administrators/developers only in production environments

### Performance Characteristics
- **Discovery**: Single metadata query with $expand (optimized)
- **Caching**: 30-minute TTL per environment (reduces metadata queries)
- **Execution**: Direct Web API calls (no middleware overhead)
- **Correlation**: Full request tracing for debugging
- **Error Handling**: Correlation IDs in all errors for support troubleshooting

## Troubleshooting

### Correlation IDs
Every request generates a correlation ID for tracing:
- **Header**: `x-ms-correlation-request-id` sent to Dataverse
- **Logging**: Included in all log statements and error responses
- **Usage**: Search Dataverse logs or connector logs with correlation ID to trace request flow

**Example Error with Correlation ID:**
```json
{
  "error": {
    "statusCode": 400,
    "message": "Required parameter missing: OpportunityId",
    "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
  }
}
```

### Common Issues

#### "Custom API not found" Error
- **Cause**: API not discovered in metadata, or cache is stale
- **Solution**: 
  1. Verify Custom API exists in environment using [Power Apps](https://make.powerapps.com)
  2. Check `IsPrivate` flag—set `INCLUDE_PRIVATE_APIS=true` if needed
  3. Wait 30 minutes for cache refresh, or restart connector to clear cache

#### "Target parameter is required" Error
- **Cause**: Entity-bound Custom API (BindingType=1) missing `Target` parameter
- **Solution**: Include `Target` with entity GUID in `arguments`:
  ```json
  "arguments": {
    "Target": "12345678-1234-1234-1234-123456789012",
    ...
  }
  ```

#### "Open type requires @odata.type" Error
- **Cause**: Entity/EntityCollection parameter without `LogicalEntityName` is missing `@odata.type`
- **Solution**: Add `@odata.type` to entity object:
  ```json
  "Record": {
    "@odata.type": "Microsoft.Dynamics.CRM.contact",
    "firstname": "Jane"
  }
  ```

#### Entity Set Name Not Found
- **Cause**: Custom entity with non-standard plural form
- **Solution**: Check `GetEntitySetName()` in `script.csx` (lines 584-637). Add mapping:
  ```csharp
  { "customentity", "customentities" }, // Add your mapping
  ```

### Debug Mode
Enable detailed logging in Power Platform:
1. Go to connector **Test** tab
2. Create test connection with OAuth
3. Send MCP request via **Test operation**
4. Review logs in **Response** section (includes correlation IDs)

## Architecture

### Request Flow
```
Copilot Studio
    ↓ (MCP JSON-RPC 2.0)
Power Platform Connector Runtime
    ↓ (script.csx routes by method)
HandleMCPRequest()
    ├─ initialize → Protocol handshake
    ├─ tools/list → DiscoverCustomAPIsAsync()
    │       ↓ (if cache miss)
    │   Dataverse Web API (/api/data/v9.2/customapis)
    │       ↓
    │   GenerateMCPToolDefinition() × N
    │       ↓
    │   Return tools array
    │
    └─ tools/call → HandleToolsCall()
            ↓
        ExecuteCustomAPIAsync()
            ↓ (BuildCustomAPIUrl by binding type)
        Dataverse Web API (/{uniquename} or /{entityset}(...))
            ↓ (GET for Functions, POST for Actions)
        ParseCustomAPIResponse()
            ↓
        Return MCP content
```

### MCP Protocol Implementation
- **Protocol Version**: 2024-11-05
- **Server Name**: `dataverse-custom-api-mcp`
- **Capabilities**: `tools` (listChanged: false)
- **Supported Methods**:
  - `initialize` - Client/server handshake
  - `notifications/initialized` - Initialization confirmation
  - `tools/list` - Discover Custom APIs (cached 30 min)
  - `tools/call` - Execute Custom API with parameters
  - `ping` - Health check

## References
- [Dataverse Custom APIs](https://learn.microsoft.com/power-apps/developer/data-platform/custom-api)
- [Dataverse Web API](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/overview)
- [Custom API Parameters](https://learn.microsoft.com/power-apps/developer/data-platform/custom-api#custom-api-request-parameters)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Power Platform Custom Connectors](https://learn.microsoft.com/connectors/custom-connectors/)
- [Custom Code Connectors](https://learn.microsoft.com/connectors/custom-connectors/write-code)
---

## Version History

### 1.0.0 (January 2026)
**Initial Release**
- 6 Management Tools: Create, update, delete Custom APIs from Copilot Studio
- Create Custom APIs: Full definition creation with solution integration
- Add Parameters & Properties: Dynamic schema building via MCP tools
- Solution Integration: `SolutionUniqueName` parameter for proper ALM
- Auto Cache Invalidation: Newly created/modified APIs immediately available
- List Solutions: Discover managed/unmanaged solutions for API placement
- Self-Bootstrapping: Create Custom APIs that become tools without code changes
- Enhanced Security: Management tools require `prvCreate/Write/DeleteCustomAPI` privileges
- Type Descriptions: Human-readable type names in success messages
- Automatic Custom API discovery from Dataverse metadata
- Support for all 3 binding types (Global/Entity/EntityCollection)
- Support for all 13 Dataverse parameter types
- Functions (GET) and Actions (POST) handling
- Open type validation with `@odata.type` requirements
- 30-minute metadata caching with environment-scoped keys
- Correlation ID request tracing
- Private API filtering (configurable)
- OAuth token forwarding with OData 4.0 headers
- MCP Protocol 2024-11-05 compliance
- Entity set name mapping for 35+ common entities

---

**Version**: 2.0.0  
**Developer**: Troy Taylor  
**Last Updated**: January 6, 2026  
**Brand Color**: #da3b01 (Microsoft Orange Red)  
**License**: Use with Power Platform subscription

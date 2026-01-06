# Dataverse Power Agent

## Overview

Dataverse Power Agent is an AI-driven custom code connector that exposes comprehensive Dataverse operations through both natural language and typed operations. It acts as an agent orchestrator inside Power Automate or Copilot Studio, letting an AI model plan and execute Dataverse operations (CRUD, bulk operations, relationships, metadata discovery, analytics, ownership, security) without any external servers.

## Key Features
- **Dual-Mode Operations**: Natural language agent endpoint + typed query operations with IntelliSense
- **AI Orchestration**: Uses GitHub Models, Azure OpenAI, Anthropic, or OpenAI for reasoning and tool selection
- **45 Specialized Tools**: Complete Dataverse coverage from basic CRUD to advanced security and record management
- **Dynamic Schema Support**: Power Automate IntelliSense for table columns via x-ms-dynamic-schema
- **Query Metadata Extraction**: Structured query parameters extracted from natural language for reuse
- **OAuth-Only Authentication**: Direct AAD v2 authentication with Dataverse (no environment variables)
- **Power Automate Optimized**: Plain-text input parameter for seamless flow integration
- **Dataverse Web API v9.2 Compliant**: OData 4.0 headers, optimistic concurrency, formatted values
- **No External Hosting**: Entire agent runs in the connector runtime

## Operations

### Agent Operation
**InvokeMCP** - Natural language interface to 45 Dataverse tools

**Input**: Plain text query or command
```
Show me active accounts with revenue over $100k
```

**Output**: Enhanced response with query metadata
```json
{
  "response": "Found 12 active accounts...",
  "execution": {
    "toolCalls": [...],
    "toolsExecuted": 2
  },
  "metadata": {
    "tokensUsed": 15420,
    "model": "gpt-4o-mini"
  },
  "suggestedQuery": {
    "table": "accounts",
    "entityName": "account",
    "select": "name,revenue,createdon",
    "filter": "statecode eq 0 and revenue gt 100000",
    "orderby": "revenue desc",
    "expand": "primarycontactid($select=fullname)",
    "top": 10,
    "count": true,
    "relationships": ["primarycontactid"]
  },
  "tablesAccessed": ["accounts"],
  "columnsUsed": ["name", "revenue", "createdon", "statecode"],
  "recordsAffected": 0
}
```

### Typed Query Operations
**ListRecords** - Retrieve records with dynamic schema and IntelliSense

**Parameters**:
- `table` (required) - Table name from dropdown (x-ms-dynamic-values)
- `select` - Comma-separated columns
- `filter` - OData $filter expression
- `top` - Max records (default 10, max 50)

**GetRecord** - Retrieve single record by ID with dynamic schema

**Parameters**:
- `table` (required) - Table name from dropdown
- `id` (required) - Record GUID
- `select` - Comma-separated columns

**GetTables** (internal) - Populates table dropdown in Power Automate

**GetTableSchema** (internal) - Generates JSON schema for selected table

### Response Schema Features

**suggestedQuery** - Extracted OData parameters for typed operations:
- `table` - Primary table name (plural)
- `entityName` - Singular logical name
- `select` - Comma-separated columns referenced
- `filter` - OData filter expression
- `orderby` - Sort expression (e.g., "createdon desc")
- `expand` - Related entities with nested $select
- `top` - Maximum records
- `skip` - Pagination offset
- `count` - Include total count flag
- `apply` - Aggregation expressions
- `recordId` - Specific GUID if single record targeted
- `relationships` - Array of relationship names

**tablesAccessed** - Array of all tables queried during execution

**columnsUsed** - Array of all columns referenced across operations

**recordsAffected** - Count of records created/updated/deleted

## Tools by Category

### READ Operations (7 tools)
- `retrieve_record` — Get a single record by ID with optional column selection
- `query_records` — Query records using FetchXML with pagination support
- `query_records_odata` — Query using OData syntax with $select, $filter, $expand, $orderby
- `get_entity_metadata` — Retrieve entity definition and schema
- `get_attribute_metadata` — Get detailed attribute/field information
- `get_relationship_metadata` — Discover entity relationships
- `get_optionset_metadata` — Retrieve picklist/choice values

### WRITE Operations (4 tools)
- `create_record` — Create new record with validation
- `update_record` — Update existing record (full or partial)
- `delete_record` — Delete record by ID
- `upsert_record` — Create or update based on alternate key

### BULK Operations (3 tools)
- `create_multiple_records` — Batch create up to 1000 records
- `update_multiple_records` — Batch update up to 1000 records
- `delete_multiple_records` — Batch delete up to 1000 records

### RELATIONSHIPS (2 tools)
- `associate_records` — Create many-to-many or lookup relationships
- `disassociate_records` — Remove relationship between records

### METADATA Discovery (6 tools)
- `list_entities` — Get all available tables in environment
- `discover_entity` — Full entity definition with attributes and relationships
- `search_metadata` — Find entities by name pattern
- `get_current_user` — Retrieve authenticated user details
- `list_saved_queries` — Get system/user views for entity
- `get_plugin_traces` — Retrieve plugin execution logs for troubleshooting

### ATTACHMENTS (2 tools)
- `upload_file` — Upload file as note attachment or to file column
- `download_file` — Download file from attachment or file column

### CHANGE TRACKING (1 tool)
- `get_changes` — Track data changes since last sync

### ASYNC Operations (2 tools)
- `execute_async_operation` — Run long operations asynchronously
- `get_async_status` — Check async job status and result

### OWNERSHIP & SECURITY (7 tools)
- `who_am_i` — Get current user ID, business unit, and organization
- `assign_record` — Change record owner (user or team)
- `share_record` — Grant access to user/team with specific permissions (Read, Write, Delete, Append, AppendTo, Assign, Share)
- `unshare_record` — Revoke access from user
- `modify_access` — Update existing access rights
- `retrieve_principal_access` — Check user's permissions on record
- `add_team_members` — Add users to team
- `remove_team_members` — Remove users from team

### RECORD MANAGEMENT (4 tools)
- `set_state` — Change record state and status (activate/deactivate/custom)
- `merge_records` — Merge duplicate records
- `initialize_from` — Create record from template
- `calculate_rollup` — Force rollup field recalculation

### ADVANCED (7 tools)
- `execute_action` — Run custom or system actions
- `execute_function` — Call custom or system functions
- `execute_batch` — Atomic transactions with multiple operations
- `get_duplicate_detection` — Find duplicate records
- `audit_details` — Retrieve audit history for record
- `detect_duplicates` — Check for duplicates before creating
- `retrieve_audit_partition` — Get audit partition information

## Advanced Features

### Formatted Values
Include `includeFormatted: true` in list_rows, get_row, or query_expand to retrieve display values for:
- Lookup fields (related record names)
- Option sets/picklists (label text instead of numeric codes)
- Money fields (formatted currency)
- Date fields (localized formats)

Formatted values appear with `@OData.Community.Display.V1.FormattedValue` annotation.

### Pagination
Large result sets return `@odata.nextLink` URL. Pass to `nextLink` parameter in subsequent calls:
```json
{ "table": "accounts", "top": 50, "nextLink": "https://org.crm.dynamics.com/api/data/v9.2/accounts?$skiptoken=..." }
```

### Alternate Keys
Update or delete records without GUID using business keys:
```json
{ "table": "accounts", "alternateKey": "accountnumber='ABC123'", "record": { "name": "Updated Name" } }
```

### Impersonation
Execute operations as another user (requires `prvActOnBehalfOfAnotherUser` privilege):
```json
{ "table": "accounts", "impersonateUserId": "00000000-0000-0000-0000-000000000000", "record": { "name": "Test" } }
```

### Request Telemetry
Track requests with correlation IDs for diagnostics (internal to SendDataverseRequest).

## Connection Parameters

**OAuth 2.0 Authentication (AAD v2)**
- **Authorization URL**: `https://login.microsoftonline.com/common/oauth2/v2.0/authorize`
- **Token URL**: `https://login.microsoftonline.com/common/oauth2/v2.0/token`
- **Scope**: `https://service.crm.dynamics.com/.default` (static)
- **Host Parameter**: `{environmentUrl}.crm.dynamics.com` (subdomain only)

**AI Configuration (Hardcoded in script.csx)**
- `DEFAULT_AI_KEY` — GitHub Models PAT, Azure AI Foundry key, Azure OpenAI key, OpenAI API key, or Anthropic API key
- `DEFAULT_BASE_URL` — AI endpoint:
  - GitHub Models: `https://models.inference.ai.azure.com`
  - Azure AI Foundry: `https://{resource}.cognitiveservices.azure.com`
  - Azure OpenAI: `https://{resource}.openai.azure.com`
  - OpenAI: `https://api.openai.com/v1`
  - Anthropic: `https://api.anthropic.com`
- `DEFAULT_MODEL` — Model name (e.g., `gpt-4o-mini`, `gpt-4o`, `claude-3-5-sonnet-20241022`)

> **Note**: OAuth token for Dataverse is injected by Power Platform runtime. AI Bearer token is hardcoded in script.csx.

## Usage

### Power Automate - Natural Language Mode
1. Add "Invoke Dataverse agent" action
2. Provide plain-text input:
   ```
   List the top 10 active accounts created this year with revenue over $50k
   ```
3. Connector authenticates to Dataverse via OAuth
4. AI agent parses request, selects appropriate tools, executes operations
5. Returns natural language response with execution metadata
6. Use `suggestedQuery` output to populate typed operations for validation

### Power Automate - Typed Query Mode
1. Add "List records" or "Get record" action
2. Select table from dropdown (dynamic, populated from Dataverse)
3. Power Automate shows IntelliSense for available columns
4. Fill in optional filter, select, orderby parameters
5. Response schema matches selected table structure

### Combining Modes
**Pattern**: Use agent for discovery, typed operations for validation
```
1. Agent: "Find accounts where contact email contains @contoso.com"
   Output: suggestedQuery.table = "accounts", suggestedQuery.filter = "..."
   
2. ListRecords: 
   - table = suggestedQuery.table
   - filter = suggestedQuery.filter
   - Validates filter syntax
   - Returns typed results with IntelliSense
```

### Copilot Studio (Future)
When MCP protocol support is enabled:
1. Connector exposes tools via `/mcp` endpoint
2. Copilot discovers tools through `tools/list`
3. Natural language triggers tool invocation
4. Connector executes against Dataverse and returns results

## Configuration

### AI Settings (script.csx constants)
```csharp
private const string DEFAULT_AI_KEY = "ghp_YourGitHubPATHere"; // or Azure/OpenAI/Anthropic key
private const string DEFAULT_BASE_URL = "https://models.inference.ai.azure.com"; // or other provider
private const string DEFAULT_MODEL = "gpt-4o-mini"; // or gpt-4o, claude-3-5-sonnet-20241022
private const int DEFAULT_MAX_TOOL_CALLS = 10;
private const int DEFAULT_MAX_TOKENS = 4000;
private const double DEFAULT_TEMPERATURE = 0.2;
```

### System Instructions
Customize `GetSystemInstructions()` to adjust agent behavior, tool selection preferences, and validation rules.

### Supported AI Providers
- **GitHub Models**: Set base URL to `https://models.inference.ai.azure.com`, use model name (e.g., `gpt-4o-mini`, `gpt-4o`), uses GitHub PAT token (Bearer authentication), **recommended for development/testing**
- **Azure AI Foundry**: Set base URL to `https://{resource}.cognitiveservices.azure.com`, use model name (e.g., `gpt-5.2-chat`), uses `/openai/responses` endpoint with Bearer authentication
- **Azure OpenAI**: Set base URL to `https://{resource}.openai.azure.com`, use deployment name as model, uses `/openai/deployments/{model}/chat/completions` with api-key header
- **OpenAI**: Set base URL to `https://api.openai.com/v1`, use model ID (e.g., `gpt-4-turbo`), uses Bearer authentication
- **Anthropic**: Set base URL to `https://api.anthropic.com`, use model ID (e.g., `claude-3-5-sonnet-20241022`), uses x-api-key header

> **Note**: The connector automatically detects Azure AI Foundry endpoints (`.cognitiveservices.azure.com`) and uses the `/openai/responses?api-version=2025-04-01-preview` endpoint with Bearer token authentication.

## Deployment

### Prerequisites
```powershell
# Install Power Platform CLI
winget install Microsoft.PowerPlatformCLI

# Authenticate to environment
paconn login  # Use device code flow
```

### Validation
```powershell
cd Dataverse
paconn validate --api-def apiDefinition.swagger.json
```

### Import to Power Platform
1. Navigate to [Power Platform maker portal](https://make.powerapps.com)
2. Select target environment
3. **Data** → **Custom connectors** → **New custom connector** → **Import an OpenAPI file**
4. Upload `apiDefinition.swagger.json`
5. On **Code** tab, enable custom code
6. Paste contents of `script.csx`
7. Update AI constants in script (DEFAULT_AI_KEY, DEFAULT_BASE_URL, DEFAULT_MODEL)
8. **Create connector**
9. Test connection with OAuth flow

### Testing

#### Natural Language Queries (InvokeMCP)

**Basic Queries**
- "List the first 5 accounts"
- "Get account with id {GUID}"
- "Show me active contacts created this month"
- "Count all open opportunities"

**Metadata Discovery**
- "What columns are available on the account table?"
- "Show me relationships for the contact table"
- "What is the schema for the opportunity entity?"

**Aggregations**
- "Calculate total revenue from won opportunities"
- "Show average deal size by account manager"
- "Count cases by status"

**Complex Operations**
- "Create an account named Contoso with website contoso.com"
- "Update account {GUID} to set phone to 425-555-0100"
- "Associate contact {GUID1} with account {GUID2} as primary contact"
- "Execute the CalculatePrice action on quote {GUID}"

**Bulk Operations**
- "Create 3 contacts with names Alice, Bob, Charlie"
- "Update all accounts where city is Seattle to set state to WA"

**Saved Queries**
- "Run the 'Active Accounts' saved view"
- "Execute my 'High Value Opportunities' query"

#### Typed Operations (ListRecords/GetRecord)

**ListRecords Examples**
- table: `accounts`, select: `name,revenue,createdon`, filter: `statecode eq 0`, top: 10
- table: `contacts`, filter: `emailaddress1 ne null`, orderby: `createdon desc`
- table: `opportunities`, select: `name,estimatedvalue`, filter: `statecode eq 0 and estimatedvalue gt 50000`

**GetRecord Examples**
- table: `accounts`, id: `{GUID}`, select: `name,address1_city,primarycontactid`
- table: `systemusers`, id: `{GUID}`, select: `fullname,internalemailaddress,businessunitid`

**Dynamic Schema Testing**
1. Add ListRecords action to flow
2. Select table from dropdown (e.g., "accounts")
3. Verify Power Automate shows IntelliSense for account columns
4. Add filter parameter
5. Test autocomplete for column names in subsequent actions

## Limitations
- OAuth authentication only (no service principal or certificate auth)
- AI key hardcoded in script (no Key Vault integration for connector runtime)
- No streaming (Power Platform connectors are request/response only)
- ListRecords/GetRecord limited to 50 records per call (use pagination with skip parameter)
- Agent bulk operations capped at 100 records for safety
- 5-10 second execution timeout per connector invocation
- Batch operations limited by Dataverse batch size limits (1000 requests)
- Dynamic schema requires at least one table selection before IntelliSense activates
- Internal metadata operations (GetTables, GetTableSchema) not visible in Power Automate action list

## Safety & Guardrails
- AI system instructions prioritize read operations over writes
- Table names and GUIDs validated before mutations
- Bulk operations capped at 100 records for safety
- Errors from Dataverse surfaced in tool results with enhanced parsing (error code, message, inner error details)
- AI can self-correct based on error messages or report failures
- Impersonation requires explicit privileges (prevents unauthorized delegation)
- Optimistic concurrency with `If-Match: *` headers (allows overwrites, prevents accidental concurrent updates)

## Error Handling

### Enhanced Error Responses
Dataverse errors include structured details:
```json
{
  "status": 400,
  "errorCode": "0x80040203",
  "message": "The specified table does not exist",
  "details": "Table 'invalidtable' could not be found"
}
```

### Common Errors
- **401 Unauthorized**: OAuth token expired or invalid scope
- **403 Forbidden**: Insufficient privileges for operation
- **404 Not Found**: Table, row, or relationship doesn't exist
- **412 Precondition Failed**: Optimistic concurrency conflict (row modified by another user)
- **429 Too Many Requests**: API throttling limits exceeded

## Architecture

### Request Flow - Natural Language Mode
1. Power Automate action receives plain-text input
2. Script.csx routes to agent mode (InvokeMCP operation)
3. Agent constructs system instructions with 45 tools
4. AI model analyzes input, selects appropriate tools
5. Script executes tools against Dataverse Web API with OAuth token
6. OData headers ensure compliance (OData-MaxVersion/Version 4.0)
7. Results aggregated and returned as natural language response
8. Query metadata extracted into suggestedQuery, tablesAccessed, columnsUsed, recordsAffected

### Request Flow - Typed Query Mode
1. Power Automate action invokes ListRecords or GetRecord
2. Script.csx routes by path (/query/list or /query/get)
3. For first-time setup, GetTables populates table dropdown
4. After table selection, GetTableSchema generates JSON schema for IntelliSense
5. Script constructs OData URL with parameters (select, filter, orderby, top)
6. Dataverse Web API returns records
7. Response schema matches selected table structure with IntelliSense support

### Path Routing
```csharp
/mcp → HandleAgentRequest() → AI orchestration with 45 tools
/query/list → HandleListRecords() → OData query with dynamic schema
/query/get → HandleGetRecord() → Single record retrieval with dynamic schema
/metadata/tables → HandleGetTables() → Table list for dropdown (internal)
/metadata/schema → HandleGetTableSchema() → JSON schema generation (internal)
```

### Dataverse Web API Compliance
- **Base URL**: `https://{environmentUrl}.crm.dynamics.com/api/data/v9.2/`
- **OData Version**: 4.0 (headers: OData-MaxVersion, OData-Version)
- **Prefer Headers**: `return=representation` (get created/updated records), `odata.include-annotations="*"` (formatted values)
- **Optimistic Concurrency**: `If-Match: *` on PATCH/DELETE operations
- **Impersonation**: `MSCRMCallerID` header with user GUID
- **Telemetry**: `x-ms-correlation-request-id` for request tracking

## Performance Optimization

### Best Practices
- Use `count_rows` instead of `list_rows` when only total is needed
- Use `query_expand` instead of multiple `get_row` calls for related data
- Use bulk operations (create_multiple, update_multiple) for >3 records
- Use `batch` for atomic multi-operation transactions
- Specify `$select` to retrieve only needed columns
- Use `$top` parameter to limit result sets (default: 5, max: 50)
- Use saved queries for complex pre-defined filters

### Caching Strategies
- Metadata tools (get_entity_metadata, get_attribute_metadata) - results rarely change, safe to cache
- GetTables operation - table list stable across environment, cache with long TTL
- GetTableSchema operation - schema changes infrequent, safe to cache per table
- Saved query execution - FetchXML definition can be cached
- Count queries - consider caching with TTL for dashboard scenarios
- suggestedQuery output - cache and reuse for typed operations to avoid redundant AI calls

## References
- [Dataverse Web API v9.2](https://learn.microsoft.com/power-apps/developer/data-platform/webapi/overview)
- [OData 4.0 Protocol](https://www.odata.org/documentation/)
- [Power Platform Custom Connectors](https://learn.microsoft.com/connectors/custom-connectors/)
- [Custom Connector Code](https://learn.microsoft.com/connectors/custom-connectors/write-code)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Dataverse Security](https://learn.microsoft.com/power-platform/admin/security-roles-privileges)
- [FetchXML Reference](https://learn.microsoft.com/power-apps/developer/data-platform/fetchxml/overview)

## Troubleshooting

### Connection Issues
**Problem**: OAuth flow fails  
**Solution**: Verify environment URL format is subdomain only (e.g., `myorg` not `https://myorg.crm.dynamics.com`)

**Problem**: "Unauthorized" errors  
**Solution**: Ensure OAuth scope is `https://service.crm.dynamics.com/.default` and user has Dataverse access

### Tool Execution Issues
**Problem**: "Unknown tool" error  
**Solution**: Verify tool name matches exactly (case-sensitive, underscore format)

**Problem**: Alternate key not found  
**Solution**: Ensure alternate keys are defined on table and use format `key1=value1,key2=value2`

**Problem**: Impersonation fails  
**Solution**: Verify caller has `prvActOnBehalfOfAnotherUser` privilege and impersonateUserId is valid GUID

### Typed Operation Issues
**Problem**: Table dropdown is empty  
**Solution**: Verify OAuth connection has permissions to read EntityDefinitions, check connector logs for GetTables errors

**Problem**: IntelliSense not showing columns after table selection  
**Solution**: Ensure GetTableSchema operation is marked `x-ms-visibility: internal`, verify table parameter passes correctly to schema operation

**Problem**: Dynamic schema returns generic object instead of typed properties  
**Solution**: Check GetTableSchema returns proper JSON schema with `properties` object, verify `value-path: schema` in x-ms-dynamic-schema configuration

**Problem**: Filter parameter validation errors  
**Solution**: Use OData 4.0 syntax (`eq`, `ne`, `gt`, `lt`, `and`, `or`), ensure column names are exact matches (case-sensitive)

### AI Response Issues
**Problem**: AI doesn't select correct tool  
**Solution**: Update system instructions in `GetSystemInstructions()` with more specific guidance

**Problem**: AI returns incomplete data  
**Solution**: Increase `DEFAULT_MAX_TOKENS` or simplify query to reduce response size

---

**Version**: 1.0.0  
**Last Updated**: January 2026  
**Brand Color**: #da3b01 (Microsoft Orange Red)  
**Operations**: 5 (InvokeMCP, ListRecords, GetRecord, GetTables, GetTableSchema)  
**AI Tools**: 45 Dataverse operations  
**AI Providers**: GitHub Models (recommended), Azure AI Foundry, Azure OpenAI, OpenAI, Anthropic

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Dataverse MCP Agent: AI-orchestrated MCP server for Dataverse CRUD via Power Automate
/// Based on MS Learn MCP connector pattern with agent + MCP modes.
/// </summary>
public class Script : ScriptBase
{
    // Agent toggle: when true all requests route through agent/InvokeMCP; MCP JSON-RPC still supported when jsonrpc present
    private const bool AI_AGENT_ONLY = true;

    // AI provider defaults - supports GitHub Models or Azure AI Foundry
    private const string DEFAULT_BASE_URL = "https://models.inference.ai.azure.com"; // GitHub: models.inference.ai.azure.com | Azure: {resource}.cognitiveservices.azure.com
    private const string DEFAULT_MODEL = "gpt-4o-mini"; // GitHub: gpt-4o, gpt-4o-mini, Phi-4, Llama-3.3-70B | Azure: gpt-5.2-chat, gpt-4o
    private const string DEFAULT_AI_KEY = ""; // GitHub: GitHub PAT token | Azure: Foundry Bearer token
    private const int DEFAULT_MAX_TOKENS = 16000;
    private const double DEFAULT_TEMPERATURE = 0.7;
    private const int DEFAULT_MAX_TOOL_CALLS = 8;

    // Tool names
    private const string TOOL_LIST_ROWS = "dataverse_list_rows";
    private const string TOOL_GET_ROW = "dataverse_get_row";
    private const string TOOL_CREATE_ROW = "dataverse_create_row";
    private const string TOOL_UPDATE_ROW = "dataverse_update_row";
    private const string TOOL_DELETE_ROW = "dataverse_delete_row";
    private const string TOOL_FETCHXML = "dataverse_fetchxml";
    private const string TOOL_EXECUTE_ACTION = "dataverse_execute_action";
    private const string TOOL_ASSOCIATE = "dataverse_associate";
    private const string TOOL_DISASSOCIATE = "dataverse_disassociate";
    private const string TOOL_UPSERT = "dataverse_upsert";
    private const string TOOL_CREATE_MULTIPLE = "dataverse_create_multiple";
    private const string TOOL_UPDATE_MULTIPLE = "dataverse_update_multiple";
    private const string TOOL_UPSERT_MULTIPLE = "dataverse_upsert_multiple";
    private const string TOOL_BATCH = "dataverse_batch";
    private const string TOOL_EXECUTE_FUNCTION = "dataverse_execute_function";
    private const string TOOL_QUERY_EXPAND = "dataverse_query_expand";
    private const string TOOL_GET_ENTITY_METADATA = "dataverse_get_entity_metadata";
    private const string TOOL_GET_ATTRIBUTE_METADATA = "dataverse_get_attribute_metadata";
    private const string TOOL_GET_RELATIONSHIPS = "dataverse_get_relationships";
    private const string TOOL_COUNT_ROWS = "dataverse_count_rows";
    private const string TOOL_AGGREGATE = "dataverse_aggregate";
    private const string TOOL_EXECUTE_SAVED_QUERY = "dataverse_execute_saved_query";
    private const string TOOL_UPLOAD_ATTACHMENT = "dataverse_upload_attachment";
    private const string TOOL_DOWNLOAD_ATTACHMENT = "dataverse_download_attachment";
    private const string TOOL_TRACK_CHANGES = "dataverse_track_changes";
    private const string TOOL_GET_GLOBAL_OPTIONSETS = "dataverse_get_global_optionsets";
    private const string TOOL_GET_BUSINESS_RULES = "dataverse_get_business_rules";
    private const string TOOL_GET_SECURITY_ROLES = "dataverse_get_security_roles";
    private const string TOOL_GET_ASYNC_OPERATION = "dataverse_get_async_operation";
    private const string TOOL_LIST_ASYNC_OPERATIONS = "dataverse_list_async_operations";
    private const string TOOL_DETECT_DUPLICATES = "dataverse_detect_duplicates";
    private const string TOOL_GET_AUDIT_HISTORY = "dataverse_get_audit_history";
    private const string TOOL_GET_PLUGIN_TRACES = "dataverse_get_plugin_traces";
    private const string TOOL_WHO_AM_I = "dataverse_whoami";
    private const string TOOL_SET_STATE = "dataverse_set_state";
    private const string TOOL_ASSIGN = "dataverse_assign";
    private const string TOOL_MERGE = "dataverse_merge";
    private const string TOOL_SHARE = "dataverse_share";
    private const string TOOL_UNSHARE = "dataverse_unshare";
    private const string TOOL_MODIFY_ACCESS = "dataverse_modify_access";
    private const string TOOL_ADD_TEAM_MEMBERS = "dataverse_add_team_members";
    private const string TOOL_REMOVE_TEAM_MEMBERS = "dataverse_remove_team_members";
    private const string TOOL_RETRIEVE_PRINCIPAL_ACCESS = "dataverse_retrieve_principal_access";
    private const string TOOL_INITIALIZE_FROM = "dataverse_initialize_from";
    private const string TOOL_CALCULATE_ROLLUP = "dataverse_calculate_rollup";

    // System instructions tailored for Dataverse
    private string GetSystemInstructions() => @"You are an AI assistant for Microsoft Dataverse with 45 specialized tools:

        READ: list_rows (query with $select/$filter/$top/$orderby), get_row (single record), query_expand (related data), fetchxml (complex queries), count_rows (efficient totals), aggregate (sum/avg/min/max), execute_saved_query (run saved views)
        WRITE: create_row, update_row (supports alternate keys), delete_row (supports alternate keys), upsert (alternate keys)
        BULK: create_multiple, update_multiple, upsert_multiple (optimized performance)
        RELATIONSHIPS: associate, disassociate (link/unlink records)
        METADATA: get_entity_metadata (schema/keys), get_attribute_metadata (column types/formats), get_relationships (many-to-one/one-to-many), get_global_optionsets (picklist values), get_business_rules (validation rules), get_security_roles (permissions)
        ATTACHMENTS: upload_attachment (add files to notes/emails), download_attachment (retrieve file content)
        CHANGE_TRACKING: track_changes (incremental sync with deltatoken)
        ASYNC: get_async_operation (job status), list_async_operations (background jobs)
        OWNERSHIP_SECURITY: whoami (current user), assign (change owner), share/unshare/modify_access (record sharing), retrieve_principal_access (check permissions), add_team_members/remove_team_members (team management)
        RECORD_MANAGEMENT: set_state (activate/deactivate), merge (combine duplicates), initialize_from (create from template), calculate_rollup (force rollup recalc)
        ADVANCED: execute_action (business logic), execute_function (calculations), batch (multiple ops), detect_duplicates (find similar records), get_audit_history (change log), get_plugin_traces (diagnostic logs)

        Dual-Mode Architecture:
        - Your responses populate the 'suggestedQuery' field with OData parameters (table, entityName, select, filter, orderby, expand, top, skip, count, apply, recordId, relationships)
        - Users can copy suggestedQuery values to typed operations (ListRecords, GetRecord) for validation and IntelliSense
        - When users need repeatable queries or Power Automate integration, recommend using typed operations with your suggestedQuery as a template
        - The 'tablesAccessed', 'columnsUsed', and 'recordsAffected' fields help users understand query scope and impact

        Guidelines:
        - Prefer read operations unless user explicitly requests changes
        - Use count_rows for totals without retrieving data
        - Use aggregate for calculations (sum, avg, min, max)
        - Use metadata tools to discover table/column schema when unknown
        - Use whoami to get current user context (userId, businessUnitId, organizationId)
        - Use assign to change record ownership
        - Use set_state to activate/deactivate records or change status
        - Use merge instead of delete when consolidating duplicate records
        - Use share/unshare/modify_access for record-level permissions
        - Use retrieve_principal_access to check if user has access to a record
        - Use initialize_from to create records based on existing templates
        - Use track_changes for incremental sync scenarios instead of full queries
        - Use detect_duplicates before creating records to prevent duplicates
        - Use get_audit_history to track who changed what and when
        - Use upload_attachment/download_attachment for file operations
        - Use async operation tools to monitor long-running jobs
        - Use bulk operations for multiple records (>3)
        - Use query_expand for related data instead of multiple get_row calls
        - Use batch for atomic multi-operation transactions
        - Use execute_saved_query to run predefined views by name
        - Validate table names (plural, lowercase) and GUIDs before mutations
        - Include includeFormatted=true for lookups/optionsets/money to get display values
        - Include impersonateUserId when acting on behalf of another user (requires privileges)
        - Use alternateKey parameter in update_row/delete_row when GUID is unknown
        - Always specify table and id context in responses
        - Populate suggestedQuery with complete OData parameters when using list_rows or get_row tools
        - If a tool fails, surface the error and suggest a corrected approach";

    // Server metadata
    private JObject GetServerInfo() => new JObject
    {
        ["name"] = "dataverse-mcp-agent",
        ["version"] = "1.0.0",
        ["title"] = "Dataverse MCP Agent",
        ["description"] = "AI-orchestrated MCP server for Dataverse CRUD"
    };

    private JObject GetServerCapabilities() => new JObject
    {
        ["tools"] = new JObject { ["listChanged"] = false },
        ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
        ["prompts"] = new JObject { ["listChanged"] = false },
        ["logging"] = new JObject(),
        ["completions"] = new JObject()
    };

    // Dataverse tool definitions
    private JArray GetDefinedTools() => new JArray
    {
        new JObject
        {
            ["name"] = TOOL_LIST_ROWS,
            ["description"] = "List Dataverse rows from a table with optional $select, $filter, $orderby, $top, and pagination",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural name of the table, e.g., accounts, contacts" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated columns to select" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData $filter expression" },
                    ["orderby"] = new JObject { ["type"] = "string", ["description"] = "OData $orderby expression (e.g., 'createdon desc')" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max rows per page (default 5, max 50)" },
                    ["includeFormatted"] = new JObject { ["type"] = "boolean", ["description"] = "Include formatted values for lookups/optionsets (default false)" },
                    ["nextLink"] = new JObject { ["type"] = "string", ["description"] = "Pagination URL from previous response @odata.nextLink" },
                    ["impersonateUserId"] = new JObject { ["type"] = "string", ["description"] = "User GUID to impersonate (requires privileges)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_ROW,
            ["description"] = "Get a Dataverse row by table and GUID id with optional $select",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural name of the table" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Row GUID" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated columns" },
                    ["includeFormatted"] = new JObject { ["type"] = "boolean", ["description"] = "Include formatted values for lookups/optionsets (default false)" },
                    ["impersonateUserId"] = new JObject { ["type"] = "string", ["description"] = "User GUID to impersonate (requires privileges)" }
                },
                ["required"] = new JArray { "table", "id" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_CREATE_ROW,
            ["description"] = "Create a Dataverse row with provided fields",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural name of the table" },
                    ["record"] = new JObject { ["type"] = "object", ["description"] = "JSON body of the record" },
                    ["impersonateUserId"] = new JObject { ["type"] = "string", ["description"] = "User GUID to impersonate (requires privileges)" }
                },
                ["required"] = new JArray { "table", "record" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_UPDATE_ROW,
            ["description"] = "Update a Dataverse row by GUID with partial record body",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Row GUID or omit if using alternateKey" },
                    ["alternateKey"] = new JObject { ["type"] = "string", ["description"] = "Alternate key in format: key1=value1,key2=value2" },
                    ["record"] = new JObject { ["type"] = "object", ["description"] = "Partial fields to update" },
                    ["impersonateUserId"] = new JObject { ["type"] = "string", ["description"] = "User GUID to impersonate (requires privileges)" }
                },
                ["required"] = new JArray { "table", "record" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_DELETE_ROW,
            ["description"] = "Delete a Dataverse row by GUID",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Row GUID or omit if using alternateKey" },
                    ["alternateKey"] = new JObject { ["type"] = "string", ["description"] = "Alternate key in format: key1=value1,key2=value2" },
                    ["impersonateUserId"] = new JObject { ["type"] = "string", ["description"] = "User GUID to impersonate (requires privileges)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_FETCHXML,
            ["description"] = "Execute FetchXML against a Dataverse table",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["fetchXml"] = new JObject { ["type"] = "string", ["description"] = "FetchXML query" }
                },
                ["required"] = new JArray { "table", "fetchXml" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_EXECUTE_ACTION,
            ["description"] = "Execute a Dataverse action (bound or unbound) with parameters",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["action"] = new JObject { ["type"] = "string", ["description"] = "Action name (e.g., WinOpportunity, CloseIncident)" },
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Table name for bound actions (optional)" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record GUID for bound actions (optional)" },
                    ["parameters"] = new JObject { ["type"] = "object", ["description"] = "Action input parameters as JSON object" }
                },
                ["required"] = new JArray { "action" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_ASSOCIATE,
            ["description"] = "Associate two Dataverse records via a relationship",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Primary table name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Primary record GUID" },
                    ["navigationProperty"] = new JObject { ["type"] = "string", ["description"] = "Relationship navigation property name" },
                    ["relatedTable"] = new JObject { ["type"] = "string", ["description"] = "Related table name" },
                    ["relatedId"] = new JObject { ["type"] = "string", ["description"] = "Related record GUID" }
                },
                ["required"] = new JArray { "table", "id", "navigationProperty", "relatedTable", "relatedId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_DISASSOCIATE,
            ["description"] = "Disassociate two Dataverse records by removing a relationship",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Primary table name" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Primary record GUID" },
                    ["navigationProperty"] = new JObject { ["type"] = "string", ["description"] = "Relationship navigation property name" },
                    ["relatedId"] = new JObject { ["type"] = "string", ["description"] = "Related record GUID (optional for single-valued nav props)" }
                },
                ["required"] = new JArray { "table", "id", "navigationProperty" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_UPSERT,
            ["description"] = "Upsert (create or update) a Dataverse row using alternate keys",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["keys"] = new JObject { ["type"] = "object", ["description"] = "Alternate key name-value pairs (e.g., {accountnumber:'123'})" },
                    ["record"] = new JObject { ["type"] = "object", ["description"] = "Record data to upsert" }
                },
                ["required"] = new JArray { "table", "keys", "record" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_CREATE_MULTIPLE,
            ["description"] = "Create multiple Dataverse rows in a single optimized request",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["records"] = new JObject { ["type"] = "array", ["description"] = "Array of record objects to create", ["items"] = new JObject { ["type"] = "object" } }
                },
                ["required"] = new JArray { "table", "records" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_UPDATE_MULTIPLE,
            ["description"] = "Update multiple Dataverse rows in a single optimized request",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["records"] = new JObject { ["type"] = "array", ["description"] = "Array of record objects with ID and fields to update", ["items"] = new JObject { ["type"] = "object" } }
                },
                ["required"] = new JArray { "table", "records" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_UPSERT_MULTIPLE,
            ["description"] = "Upsert multiple Dataverse rows in a single optimized request using alternate keys",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural table name" },
                    ["records"] = new JObject { ["type"] = "array", ["description"] = "Array of record objects to upsert", ["items"] = new JObject { ["type"] = "object" } }
                },
                ["required"] = new JArray { "table", "records" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_BATCH,
            ["description"] = "Execute multiple Dataverse operations in a single batch request",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["requests"] = new JObject { ["type"] = "array", ["description"] = "Array of request objects with method, url, headers, body", ["items"] = new JObject { ["type"] = "object" } }
                },
                ["required"] = new JArray { "requests" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_EXECUTE_FUNCTION,
            ["description"] = "Execute a Dataverse function (bound or unbound) with parameters",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["function"] = new JObject { ["type"] = "string", ["description"] = "Function name (e.g., WhoAmI, RetrieveVersion, CalculateRollupField)" },
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Table name for bound functions (optional)" },
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Record GUID for bound functions (optional)" },
                    ["parameters"] = new JObject { ["type"] = "object", ["description"] = "Function parameters as query string key-value pairs" }
                },
                ["required"] = new JArray { "function" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_QUERY_EXPAND,
            ["description"] = "Query Dataverse rows with $expand to retrieve related records in a single request",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural name of the table" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated columns to select" },
                    ["expand"] = new JObject { ["type"] = "string", ["description"] = "Navigation properties to expand (e.g., 'primarycontactid($select=fullname)')" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData $filter expression" },
                    ["orderby"] = new JObject { ["type"] = "string", ["description"] = "OData $orderby expression (e.g., 'createdon desc')" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max rows per page (default 5, max 50)" },
                    ["includeFormatted"] = new JObject { ["type"] = "boolean", ["description"] = "Include formatted values for lookups/optionsets (default false)" },
                    ["nextLink"] = new JObject { ["type"] = "string", ["description"] = "Pagination URL from previous response @odata.nextLink" }
                },
                ["required"] = new JArray { "table", "expand" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_ENTITY_METADATA,
            ["description"] = "Retrieve table (entity) metadata including schema name, columns, keys",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table (e.g., account, contact)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_ATTRIBUTE_METADATA,
            ["description"] = "Retrieve column (attribute) metadata for a table including type, format, options",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["attribute"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the attribute/column (optional - returns all if omitted)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_RELATIONSHIPS,
            ["description"] = "Retrieve relationship metadata for a table",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_COUNT_ROWS,
            ["description"] = "Get count of rows matching filter criteria (efficient for totals without retrieving data)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural name of the table" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData $filter expression (optional)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_AGGREGATE,
            ["description"] = "Retrieve aggregate values (sum, avg, min, max, count) using FetchXML",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["aggregateAttribute"] = new JObject { ["type"] = "string", ["description"] = "Attribute to aggregate" },
                    ["aggregateFunction"] = new JObject { ["type"] = "string", ["description"] = "Function: sum, avg, min, max, count, countcolumn", ["enum"] = new JArray { "sum", "avg", "min", "max", "count", "countcolumn" } },
                    ["groupBy"] = new JObject { ["type"] = "string", ["description"] = "Optional attribute to group by" },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "Optional filter attribute name" },
                    ["filterOperator"] = new JObject { ["type"] = "string", ["description"] = "Filter operator (e.g., eq, ne, gt, lt)" },
                    ["filterValue"] = new JObject { ["type"] = "string", ["description"] = "Filter value" }
                },
                ["required"] = new JArray { "table", "aggregateAttribute", "aggregateFunction" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_EXECUTE_SAVED_QUERY,
            ["description"] = "Execute a saved query (system view or user view) by name",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["viewName"] = new JObject { ["type"] = "string", ["description"] = "Name of the saved view" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max rows (default 5, max 50)" }
                },
                ["required"] = new JArray { "table", "viewName" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_UPLOAD_ATTACHMENT,
            ["description"] = "Upload a file attachment to a note (annotation) or email",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["regarding"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record to attach to" },
                    ["regardingType"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the regarding record's table" },
                    ["fileName"] = new JObject { ["type"] = "string", ["description"] = "Name of the file" },
                    ["mimeType"] = new JObject { ["type"] = "string", ["description"] = "MIME type (e.g., application/pdf, image/png)" },
                    ["content"] = new JObject { ["type"] = "string", ["description"] = "Base64-encoded file content" },
                    ["subject"] = new JObject { ["type"] = "string", ["description"] = "Note subject/title (optional)" }
                },
                ["required"] = new JArray { "regarding", "regardingType", "fileName", "mimeType", "content" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_DOWNLOAD_ATTACHMENT,
            ["description"] = "Download a file attachment from a note (annotation) by ID",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["annotationId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the annotation record" }
                },
                ["required"] = new JArray { "annotationId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_TRACK_CHANGES,
            ["description"] = "Retrieve changes to records since last sync using delta token (change tracking)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Plural name of the table (must have change tracking enabled)" },
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Comma-separated columns to select" },
                    ["deltaToken"] = new JObject { ["type"] = "string", ["description"] = "Delta token from previous response (omit for initial sync)" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_GLOBAL_OPTIONSETS,
            ["description"] = "Retrieve global option set (picklist) definitions with label/value pairs",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["optionSetName"] = new JObject { ["type"] = "string", ["description"] = "Name of the global option set (optional - returns all if omitted)" }
                },
                ["required"] = new JArray()
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_BUSINESS_RULES,
            ["description"] = "Retrieve business rules (validation/automation) for a table",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" }
                },
                ["required"] = new JArray { "table" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_SECURITY_ROLES,
            ["description"] = "List security roles with privileges",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["roleName"] = new JObject { ["type"] = "string", ["description"] = "Name of specific role (optional - returns all if omitted)" }
                },
                ["required"] = new JArray()
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_ASYNC_OPERATION,
            ["description"] = "Get status of an asynchronous operation (background job)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["asyncOperationId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the async operation" }
                },
                ["required"] = new JArray { "asyncOperationId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_LIST_ASYNC_OPERATIONS,
            ["description"] = "List recent asynchronous operations with optional filter by status",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["status"] = new JObject { ["type"] = "string", ["description"] = "Filter by status: InProgress, Succeeded, Failed, Canceled (optional)" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max rows (default 10, max 50)" }
                },
                ["required"] = new JArray()
            }
        },
        new JObject
        {
            ["name"] = TOOL_DETECT_DUPLICATES,
            ["description"] = "Detect duplicate records based on duplicate detection rules",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["record"] = new JObject { ["type"] = "object", ["description"] = "Record data to check for duplicates" }
                },
                ["required"] = new JArray { "table", "record" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_AUDIT_HISTORY,
            ["description"] = "Retrieve audit history (change log) for a record",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max audit records (default 10, max 50)" }
                },
                ["required"] = new JArray { "table", "recordId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_GET_PLUGIN_TRACES,
            ["description"] = "Retrieve plugin trace logs for diagnostics",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["correlationId"] = new JObject { ["type"] = "string", ["description"] = "Correlation ID to filter traces (optional)" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max trace records (default 10, max 50)" }
                },
                ["required"] = new JArray()
            }
        },
        new JObject
        {
            ["name"] = TOOL_WHO_AM_I,
            ["description"] = "Get current user context (userId, businessUnitId, organizationId)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        },
        new JObject
        {
            ["name"] = TOOL_SET_STATE,
            ["description"] = "Change record state and status (activate/deactivate or custom states)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record" },
                    ["state"] = new JObject { ["type"] = "integer", ["description"] = "State code (e.g., 0=Active, 1=Inactive)" },
                    ["status"] = new JObject { ["type"] = "integer", ["description"] = "Status code (depends on table)" }
                },
                ["required"] = new JArray { "table", "recordId", "state", "status" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_ASSIGN,
            ["description"] = "Assign a record to a different user or team (change owner)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record to assign" },
                    ["assigneeId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the user or team to assign to" },
                    ["assigneeType"] = new JObject { ["type"] = "string", ["description"] = "Type of assignee: systemuser or team" }
                },
                ["required"] = new JArray { "table", "recordId", "assigneeId", "assigneeType" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_MERGE,
            ["description"] = "Merge two records (subordinate record merged into target, subordinate deleted)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["targetId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the target record (kept)" },
                    ["subordinateId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the subordinate record (deleted)" },
                    ["updateContent"] = new JObject { ["type"] = "object", ["description"] = "Optional data to update on target after merge" }
                },
                ["required"] = new JArray { "table", "targetId", "subordinateId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_SHARE,
            ["description"] = "Grant access to a record for a specific user or team",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record to share" },
                    ["principalId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the user or team" },
                    ["principalType"] = new JObject { ["type"] = "string", ["description"] = "Type: systemuser or team" },
                    ["accessMask"] = new JObject { ["type"] = "string", ["description"] = "Access rights: Read, Write, Delete, Append, AppendTo, Assign, Share (comma-separated)" }
                },
                ["required"] = new JArray { "table", "recordId", "principalId", "principalType", "accessMask" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_UNSHARE,
            ["description"] = "Revoke access to a record for a specific user or team",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record" },
                    ["principalId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the user or team" }
                },
                ["required"] = new JArray { "table", "recordId", "principalId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_MODIFY_ACCESS,
            ["description"] = "Modify existing access rights for a user or team on a record",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record" },
                    ["principalId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the user or team" },
                    ["accessMask"] = new JObject { ["type"] = "string", ["description"] = "New access rights: Read, Write, Delete, Append, AppendTo, Assign, Share (comma-separated)" }
                },
                ["required"] = new JArray { "table", "recordId", "principalId", "accessMask" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_ADD_TEAM_MEMBERS,
            ["description"] = "Add users to a team",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["teamId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the team" },
                    ["memberIds"] = new JObject { ["type"] = "array", ["description"] = "Array of user GUIDs to add", ["items"] = new JObject { ["type"] = "string" } }
                },
                ["required"] = new JArray { "teamId", "memberIds" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_REMOVE_TEAM_MEMBERS,
            ["description"] = "Remove users from a team",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["teamId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the team" },
                    ["memberIds"] = new JObject { ["type"] = "array", ["description"] = "Array of user GUIDs to remove", ["items"] = new JObject { ["type"] = "string" } }
                },
                ["required"] = new JArray { "teamId", "memberIds" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_RETRIEVE_PRINCIPAL_ACCESS,
            ["description"] = "Check what access rights a user or team has to a specific record",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record" },
                    ["principalId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the user or team" }
                },
                ["required"] = new JArray { "table", "recordId", "principalId" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_INITIALIZE_FROM,
            ["description"] = "Create a new record initialized with values from an existing record (template pattern)",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sourceTable"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the source table" },
                    ["sourceId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the source record" },
                    ["targetTable"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the target table to create" }
                },
                ["required"] = new JArray { "sourceTable", "sourceId", "targetTable" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_CALCULATE_ROLLUP,
            ["description"] = "Force recalculation of a rollup field on a record",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["table"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the table" },
                    ["recordId"] = new JObject { ["type"] = "string", ["description"] = "GUID of the record" },
                    ["fieldName"] = new JObject { ["type"] = "string", ["description"] = "Logical name of the rollup field" }
                },
                ["required"] = new JArray { "table", "recordId", "fieldName" }
            }
        }
    };

    private JArray GetDefinedResources() => new JArray();
    private JArray GetDefinedResourceTemplates() => new JArray();
    private JArray GetDefinedPrompts() => new JArray();

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        this.Context.Logger.LogInformation("Dataverse MCP Agent request received");
        try
        {
            var requestPath = this.Context.Request.RequestUri.AbsolutePath;
            
            // Route metadata/query operations
            if (requestPath.EndsWith("/metadata/tables", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetTables metadata operation");
                return await HandleGetTables().ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/metadata/schema", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetTableSchema metadata operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                var table = query["table"];
                return await HandleGetTableSchema(table).ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/query/list", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to ListRecords query operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                return await HandleListRecords(
                    query["table"],
                    query["select"],
                    query["filter"],
                    string.IsNullOrEmpty(query["top"]) ? 10 : int.Parse(query["top"])
                ).ConfigureAwait(false);
            }
            if (requestPath.EndsWith("/query/get", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.Logger.LogInformation("Routing to GetRecord query operation");
                var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
                return await HandleGetRecord(
                    query["table"],
                    query["id"],
                    query["select"]
                ).ConfigureAwait(false);
            }

            // Default: Agent/MCP mode
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            this.Context.Logger.LogDebug($"Request body length: {body?.Length ?? 0} characters");
            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning("Empty request body received");
                return CreateAgentErrorResponse("Empty request body", "Body required", "ValidationError", "EMPTY_BODY");
            }

            JObject payload;
            try 
            { 
                payload = JObject.Parse(body); 
            }
            catch (JsonException ex) 
            { 
                // Body is plain text (from Power Platform string parameter), wrap it as JSON
                this.Context.Logger.LogInformation("Plain text input detected, wrapping as JSON");
                payload = new JObject { ["input"] = body };
            }

            // Route by payload type
            if (payload.ContainsKey("jsonrpc"))
            {
                this.Context.Logger.LogInformation("Routing to MCP protocol handler");
                return await HandleMCPRequest(payload).ConfigureAwait(false);
            }

            if (!AI_AGENT_ONLY && payload.ContainsKey("mode"))
            {
                this.Context.Logger.LogInformation("Routing to agent mode (mode specified)");
                return await HandleAgentRequest(payload).ConfigureAwait(false);
            }

            // Default: agent mode using input/options
            this.Context.Logger.LogInformation("Routing to agent mode (default)");
            return await HandleAgentRequest(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return CreateAgentErrorResponse("Unexpected error", ex.Message, "InternalError", "UNEXPECTED");
        }
    }

    // ---------- Agent Mode ----------
    private async Task<HttpResponseMessage> HandleAgentRequest(JObject requestJson)
    {
        var input = requestJson["input"]?.ToString();
        this.Context.Logger.LogInformation($"Agent request: {input?.Substring(0, Math.Min(input.Length, 100))}");
        if (string.IsNullOrWhiteSpace(input))
        {
            this.Context.Logger.LogWarning("Missing input in agent request");
            return CreateAgentErrorResponse("'input' is required", null, "ValidationError", "MISSING_INPUT");
        }

        var options = requestJson["options"] as JObject ?? new JObject();
        var autoExecute = options["autoExecuteTools"]?.Value<bool?>() ?? true;
        var maxToolCalls = options["maxToolCalls"]?.Value<int?>() ?? DEFAULT_MAX_TOOL_CALLS;
        var temperature = options["temperature"]?.Value<double?>() ?? DEFAULT_TEMPERATURE;
        var maxTokens = options["maxTokens"]?.Value<int?>() ?? DEFAULT_MAX_TOKENS;

        var apiKey = DEFAULT_AI_KEY;
        var baseUrl = DEFAULT_BASE_URL;
        var model = DEFAULT_MODEL;

        try
        {
            var tools = GetDefinedTools();
            var systemPrompt = GetSystemInstructions();

            var messages = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = systemPrompt },
                new JObject { ["role"] = "user", ["content"] = input }
            };

            var functions = ConvertMCPToolsToFunctions(tools);
            var (responseText, executedTools, tokensUsed) = await ExecuteAIWithFunctions(
                apiKey, model, baseUrl, messages, functions, temperature, maxTokens, autoExecute, maxToolCalls).ConfigureAwait(false);

            var result = new JObject
            {
                ["response"] = responseText,
                ["execution"] = new JObject
                {
                    ["toolCalls"] = executedTools,
                    ["toolsExecuted"] = executedTools?.Count ?? 0
                },
                ["metadata"] = new JObject
                {
                    ["tokensUsed"] = tokensUsed,
                    ["model"] = model
                },
                ["suggestedQuery"] = ExtractSuggestedQuery(executedTools ?? new JArray()),
                ["tablesAccessed"] = ExtractTablesAccessed(executedTools ?? new JArray()),
                ["columnsUsed"] = ExtractColumnsUsed(executedTools ?? new JArray()),
                ["recordsAffected"] = CountRecordsAffected(executedTools ?? new JArray()),
                ["error"] = null
            };

            var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(result.ToString()) };
            return http;
        }
        catch (Exception ex)
        {
            return CreateAgentErrorResponse("Agent execution failed", ex.Message, "AgentError", "AGENT_FAILURE");
        }
    }

    // ---------- MCP Mode ----------
    private async Task<HttpResponseMessage> HandleMCPRequest(JObject request)
    {
        var method = request["method"]?.ToString();
        var id = request["id"];
        this.Context.Logger.LogInformation($"MCP method: {method}");

        try
        {
            switch (method)
            {
                case "initialize":
                    return CreateSuccessResponse(new JObject
                    {
                        ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                        ["capabilities"] = GetServerCapabilities(),
                        ["serverInfo"] = GetServerInfo()
                    }, id);
                case "initialized":
                case "ping":
                case "notifications/cancelled":
                    return CreateSuccessResponse(new JObject(), id);
                case "tools/list":
                    return CreateSuccessResponse(new JObject { ["tools"] = GetDefinedTools() }, id);
                case "tools/call":
                    return await HandleToolsCall(request["params"] as JObject, id).ConfigureAwait(false);
                case "resources/list":
                    return CreateSuccessResponse(new JObject { ["resources"] = GetDefinedResources() }, id);
                case "resources/templates/list":
                    return CreateSuccessResponse(new JObject { ["resourceTemplates"] = GetDefinedResourceTemplates() }, id);
                case "resources/read":
                    return CreateErrorResponse(-32601, "resources/read not implemented", id);
                case "prompts/list":
                    return CreateSuccessResponse(new JObject { ["prompts"] = GetDefinedPrompts() }, id);
                case "prompts/get":
                    return CreateErrorResponse(-32000, "prompts not implemented", id);
                case "completion/complete":
                    return CreateSuccessResponse(new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } }, id);
                case "logging/setLevel":
                    return CreateSuccessResponse(new JObject(), id);
                default:
                    return CreateErrorResponse(-32601, $"Method not found: {method}", id);
            }
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", id);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject parms, JToken id)
    {
        if (parms == null) return CreateErrorResponse(-32602, "params object required", id);
        var toolName = parms["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(toolName)) return CreateErrorResponse(-32602, "Tool name required", id);

        var tools = GetDefinedTools();
        if (!tools.Any(t => t["name"]?.ToString() == toolName)) return CreateErrorResponse(-32601, $"Unknown tool: {toolName}", id);

        var arguments = parms["arguments"] as JObject ?? new JObject();
        try
        {
            var result = await ExecuteToolByName(toolName, arguments).ConfigureAwait(false);
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString() } },
                ["isError"] = false
            }, id);
        }
        catch (ArgumentException ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
        catch (Exception ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    // ---------- Tool Execution ----------
    private async Task<JObject> ExecuteToolByName(string toolName, JObject args)
    {
        this.Context.Logger.LogInformation($"Executing tool: {toolName}");
        this.Context.Logger.LogDebug($"Tool arguments: {args?.ToString(Newtonsoft.Json.Formatting.None)}");
        switch (toolName)
        {
            case TOOL_LIST_ROWS:
                return await ExecuteListRows(args).ConfigureAwait(false);
            case TOOL_GET_ROW:
                return await ExecuteGetRow(args).ConfigureAwait(false);
            case TOOL_CREATE_ROW:
                return await ExecuteCreateRow(args).ConfigureAwait(false);
            case TOOL_UPDATE_ROW:
                return await ExecuteUpdateRow(args).ConfigureAwait(false);
            case TOOL_DELETE_ROW:
                return await ExecuteDeleteRow(args).ConfigureAwait(false);
            case TOOL_FETCHXML:
                return await ExecuteFetchXml(args).ConfigureAwait(false);
            case TOOL_EXECUTE_ACTION:
                return await ExecuteAction(args).ConfigureAwait(false);
            case TOOL_ASSOCIATE:
                return await ExecuteAssociate(args).ConfigureAwait(false);
            case TOOL_DISASSOCIATE:
                return await ExecuteDisassociate(args).ConfigureAwait(false);
            case TOOL_UPSERT:
                return await ExecuteUpsert(args).ConfigureAwait(false);
            case TOOL_CREATE_MULTIPLE:
                return await ExecuteCreateMultiple(args).ConfigureAwait(false);
            case TOOL_UPDATE_MULTIPLE:
                return await ExecuteUpdateMultiple(args).ConfigureAwait(false);
            case TOOL_UPSERT_MULTIPLE:
                return await ExecuteUpsertMultiple(args).ConfigureAwait(false);
            case TOOL_BATCH:
                return await ExecuteBatch(args).ConfigureAwait(false);
            case TOOL_EXECUTE_FUNCTION:
                return await ExecuteFunction(args).ConfigureAwait(false);
            case TOOL_QUERY_EXPAND:
                return await ExecuteQueryExpand(args).ConfigureAwait(false);
            case TOOL_GET_ENTITY_METADATA:
                return await ExecuteGetEntityMetadata(args).ConfigureAwait(false);
            case TOOL_GET_ATTRIBUTE_METADATA:
                return await ExecuteGetAttributeMetadata(args).ConfigureAwait(false);
            case TOOL_GET_RELATIONSHIPS:
                return await ExecuteGetRelationships(args).ConfigureAwait(false);
            case TOOL_COUNT_ROWS:
                return await ExecuteCountRows(args).ConfigureAwait(false);
            case TOOL_AGGREGATE:
                return await ExecuteAggregate(args).ConfigureAwait(false);
            case TOOL_EXECUTE_SAVED_QUERY:
                return await ExecuteSavedQuery(args).ConfigureAwait(false);
            case TOOL_UPLOAD_ATTACHMENT:
                return await ExecuteUploadAttachment(args).ConfigureAwait(false);
            case TOOL_DOWNLOAD_ATTACHMENT:
                return await ExecuteDownloadAttachment(args).ConfigureAwait(false);
            case TOOL_TRACK_CHANGES:
                return await ExecuteTrackChanges(args).ConfigureAwait(false);
            case TOOL_GET_GLOBAL_OPTIONSETS:
                return await ExecuteGetGlobalOptionSets(args).ConfigureAwait(false);
            case TOOL_GET_BUSINESS_RULES:
                return await ExecuteGetBusinessRules(args).ConfigureAwait(false);
            case TOOL_GET_SECURITY_ROLES:
                return await ExecuteGetSecurityRoles(args).ConfigureAwait(false);
            case TOOL_GET_ASYNC_OPERATION:
                return await ExecuteGetAsyncOperation(args).ConfigureAwait(false);
            case TOOL_LIST_ASYNC_OPERATIONS:
                return await ExecuteListAsyncOperations(args).ConfigureAwait(false);
            case TOOL_DETECT_DUPLICATES:
                return await ExecuteDetectDuplicates(args).ConfigureAwait(false);
            case TOOL_GET_AUDIT_HISTORY:
                return await ExecuteGetAuditHistory(args).ConfigureAwait(false);
            case TOOL_GET_PLUGIN_TRACES:
                return await ExecuteGetPluginTraces(args).ConfigureAwait(false);
            case TOOL_WHO_AM_I:
                return await ExecuteWhoAmI(args).ConfigureAwait(false);
            case TOOL_SET_STATE:
                return await ExecuteSetState(args).ConfigureAwait(false);
            case TOOL_ASSIGN:
                return await ExecuteAssign(args).ConfigureAwait(false);
            case TOOL_MERGE:
                return await ExecuteMerge(args).ConfigureAwait(false);
            case TOOL_SHARE:
                return await ExecuteShare(args).ConfigureAwait(false);
            case TOOL_UNSHARE:
                return await ExecuteUnshare(args).ConfigureAwait(false);
            case TOOL_MODIFY_ACCESS:
                return await ExecuteModifyAccess(args).ConfigureAwait(false);
            case TOOL_ADD_TEAM_MEMBERS:
                return await ExecuteAddTeamMembers(args).ConfigureAwait(false);
            case TOOL_REMOVE_TEAM_MEMBERS:
                return await ExecuteRemoveTeamMembers(args).ConfigureAwait(false);
            case TOOL_RETRIEVE_PRINCIPAL_ACCESS:
                return await ExecuteRetrievePrincipalAccess(args).ConfigureAwait(false);
            case TOOL_INITIALIZE_FROM:
                return await ExecuteInitializeFrom(args).ConfigureAwait(false);
            case TOOL_CALCULATE_ROLLUP:
                return await ExecuteCalculateRollup(args).ConfigureAwait(false);
            default:
                throw new Exception($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> ExecuteListRows(JObject args)
    {
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var nextLink = args["nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            // Follow pagination link
            var includeFmtPage = args["includeFormatted"]?.Value<bool?>() ?? false;
            return await SendDataverseRequest(HttpMethod.Get, nextLink, null, includeFmtPage, impersonateUserId).ConfigureAwait(false);
        }

        var table = Require(args, "table");
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 5;
        var includeFormatted = args["includeFormatted"]?.Value<bool?>() ?? false;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null, includeFormatted, impersonateUserId).ConfigureAwait(false);
        return resp;
    }

    private async Task<JObject> ExecuteGetRow(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var select = args["select"]?.ToString();
        var includeFmtGetRow = args["includeFormatted"]?.Value<bool?>() ?? false;
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var qs = string.IsNullOrWhiteSpace(select) ? string.Empty : $"?$select={Uri.EscapeDataString(select)}";
        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)}){qs}");
        return await SendDataverseRequest(HttpMethod.Get, url, null, includeFmtGetRow, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateRow(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        var url = BuildDataverseUrl(table);
        return await SendDataverseRequest(HttpMethod.Post, url, record, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateRow(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        
        string urlPath;
        var alternateKey = args["alternateKey"]?.ToString();
        if (!string.IsNullOrWhiteSpace(alternateKey))
        {
            urlPath = $"{table}({alternateKey})";
        }
        else
        {
            var id = Require(args, "id");
            urlPath = $"{table}({SanitizeGuid(id)})";
        }
        
        var url = BuildDataverseUrl(urlPath);
        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteRow(JObject args)
    {
        var table = Require(args, "table");
        var impersonateUserId = args["impersonateUserId"]?.ToString();
        
        string urlPath;
        var alternateKey = args["alternateKey"]?.ToString();
        if (!string.IsNullOrWhiteSpace(alternateKey))
        {
            urlPath = $"{table}({alternateKey})";
        }
        else
        {
            var id = Require(args, "id");
            urlPath = $"{table}({SanitizeGuid(id)})";
        }
        
        var url = BuildDataverseUrl(urlPath);
        return await SendDataverseRequest(HttpMethod.Delete, url, null, false, impersonateUserId).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteFetchXml(JObject args)
    {
        var table = Require(args, "table");
        var fetchXml = Require(args, "fetchXml");
        var url = BuildDataverseUrl($"{table}?fetchXml={Uri.EscapeDataString(fetchXml)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAction(JObject args)
    {
        var action = Require(args, "action");
        var table = args["table"]?.ToString();
        var id = args["id"]?.ToString();
        var parameters = args["parameters"] as JObject ?? new JObject();

        string url;
        if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(id))
        {
            // Bound action: POST /table(id)/Microsoft.Dynamics.CRM.action
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/Microsoft.Dynamics.CRM.{action}");
        }
        else
        {
            // Unbound action: POST /action
            url = BuildDataverseUrl(action);
        }

        return await SendDataverseRequest(HttpMethod.Post, url, parameters).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssociate(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var navigationProperty = Require(args, "navigationProperty");
        var relatedTable = Require(args, "relatedTable");
        var relatedId = Require(args, "relatedId");

        var url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}/$ref");
        var relatedUri = BuildDataverseUrl($"{relatedTable}({SanitizeGuid(relatedId)})");
        
        // Need to build full URI for @odata.id
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var body = new JObject
        {
            ["@odata.id"] = $"{baseUrl}{relatedUri}"
        };

        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDisassociate(JObject args)
    {
        var table = Require(args, "table");
        var id = Require(args, "id");
        var navigationProperty = Require(args, "navigationProperty");
        var relatedId = args["relatedId"]?.ToString();

        string url;
        if (!string.IsNullOrWhiteSpace(relatedId))
        {
            // Collection-valued navigation property: DELETE /table(id)/navprop(relatedId)/$ref
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}({SanitizeGuid(relatedId)})/$ref");
        }
        else
        {
            // Single-valued navigation property: DELETE /table(id)/navprop/$ref
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/{navigationProperty}/$ref");
        }

        return await SendDataverseRequest(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpsert(JObject args)
    {
        var table = Require(args, "table");
        var keys = RequireObject(args, "keys");
        var record = RequireObject(args, "record");

        // Build alternate key selector: table(key1=value1,key2=value2)
        var keyPairs = new List<string>();
        foreach (var prop in keys.Properties())
        {
            var val = prop.Value.ToString();
            // Quote string values
            var quotedVal = prop.Value.Type == JTokenType.String ? $"'{val}'" : val;
            keyPairs.Add($"{prop.Name}={quotedVal}");
        }
        var keySelector = string.Join(",", keyPairs);
        var url = BuildDataverseUrl($"{table}({keySelector})");

        return await SendDataverseRequest(new HttpMethod("PATCH"), url, record).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("CreateMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("UpdateMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpsertMultiple(JObject args)
    {
        var table = Require(args, "table");
        var recordsToken = args["records"];
        if (recordsToken == null || recordsToken.Type != JTokenType.Array)
            throw new ArgumentException("records must be an array");

        var records = recordsToken as JArray;
        var targets = new JArray();
        foreach (var rec in records)
        {
            var recObj = rec as JObject;
            if (recObj != null)
            {
                recObj["@odata.type"] = $"Microsoft.Dynamics.CRM.{table.TrimEnd('s')}";
                targets.Add(recObj);
            }
        }

        var body = new JObject { ["Targets"] = targets };
        var url = BuildDataverseUrl("UpsertMultiple");
        return await SendDataverseRequest(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteBatch(JObject args)
    {
        var requestsToken = args["requests"];
        if (requestsToken == null || requestsToken.Type != JTokenType.Array)
            throw new ArgumentException("requests must be an array");

        var requests = requestsToken as JArray;
        var batchId = Guid.NewGuid().ToString();
        var batchBoundary = $"batch_{batchId}";
        var changesetId = Guid.NewGuid().ToString();
        var changesetBoundary = $"changeset_{changesetId}";

        var batchContent = new StringBuilder();
        batchContent.AppendLine($"--{batchBoundary}");
        batchContent.AppendLine($"Content-Type: multipart/mixed;boundary={changesetBoundary}");
        batchContent.AppendLine();

        int contentId = 1;
        foreach (var req in requests)
        {
            var reqObj = req as JObject;
            if (reqObj == null) continue;

            var method = reqObj["method"]?.ToString()?.ToUpper() ?? "GET";
            var url = reqObj["url"]?.ToString() ?? "";
            var bodyObj = reqObj["body"] as JObject;

            batchContent.AppendLine($"--{changesetBoundary}");
            batchContent.AppendLine("Content-Type: application/http");
            batchContent.AppendLine("Content-Transfer-Encoding: binary");
            batchContent.AppendLine($"Content-ID: {contentId++}");
            batchContent.AppendLine();
            batchContent.AppendLine($"{method} {url} HTTP/1.1");
            batchContent.AppendLine("Content-Type: application/json");
            batchContent.AppendLine();
            if (bodyObj != null)
            {
                batchContent.AppendLine(bodyObj.ToString(Newtonsoft.Json.Formatting.None));
            }
            batchContent.AppendLine();
        }

        batchContent.AppendLine($"--{changesetBoundary}--");
        batchContent.AppendLine($"--{batchBoundary}--");

        var batchReq = new HttpRequestMessage(HttpMethod.Post, BuildDataverseUrl("$batch"));
        batchReq.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        batchReq.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        batchReq.Content = new StringContent(batchContent.ToString(), Encoding.UTF8, $"multipart/mixed;boundary={batchBoundary}");

        var response = await this.Context.SendAsync(batchReq, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase,
                ["body"] = content
            };
        }

        // Parse multipart response (simplified - return raw for now)
        return new JObject
        {
            ["status"] = (int)response.StatusCode,
            ["batchResponse"] = content
        };
    }

    private async Task<JObject> ExecuteFunction(JObject args)
    {
        var function = Require(args, "function");
        var table = args["table"]?.ToString();
        var id = args["id"]?.ToString();
        var parameters = args["parameters"] as JObject ?? new JObject();

        // Build query string from parameters
        var queryParts = new List<string>();
        foreach (var prop in parameters.Properties())
        {
            var val = prop.Value.ToString();
            var quotedVal = prop.Value.Type == JTokenType.String ? $"'{val}'" : val;
            queryParts.Add($"{prop.Name}={quotedVal}");
        }
        var queryString = queryParts.Any() ? "?" + string.Join("&", queryParts) : "";

        string url;
        if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(id))
        {
            // Bound function: GET /table(id)/Microsoft.Dynamics.CRM.function(params)
            url = BuildDataverseUrl($"{table}({SanitizeGuid(id)})/Microsoft.Dynamics.CRM.{function}{queryString}");
        }
        else
        {
            // Unbound function: GET /function(params)
            url = BuildDataverseUrl($"{function}{queryString}");
        }

        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteQueryExpand(JObject args)
    {
        var nextLink = args["nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            // Follow pagination link
            var includeFmtNext = args["includeFormatted"]?.Value<bool?>() ?? false;
            return await SendDataverseRequest(HttpMethod.Get, nextLink, null, includeFmtNext).ConfigureAwait(false);
        }

        var table = Require(args, "table");
        var expand = Require(args, "expand");
        var select = args["select"]?.ToString();
        var filter = args["filter"]?.ToString();
        var orderby = args["orderby"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 5;
        var includeFmt = args["includeFormatted"]?.Value<bool?>() ?? false;
        top = Math.Min(Math.Max(top, 1), 50);

        var query = new List<string>();
        query.Add("$expand=" + Uri.EscapeDataString(expand));
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        if (!string.IsNullOrWhiteSpace(filter)) query.Add("$filter=" + Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderby)) query.Add("$orderby=" + Uri.EscapeDataString(orderby));
        query.Add("$top=" + top);

        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null, includeFmt).ConfigureAwait(false);
        return resp;
    }

    private async Task<JObject> ExecuteGetEntityMetadata(JObject args)
    {
        var table = Require(args, "table");
        var url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')?$select=LogicalName,SchemaName,DisplayName,PrimaryIdAttribute,PrimaryNameAttribute,EntitySetName");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAttributeMetadata(JObject args)
    {
        var table = Require(args, "table");
        var attribute = args["attribute"]?.ToString();

        string url;
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{attribute}')?$select=LogicalName,SchemaName,DisplayName,AttributeType,RequiredLevel,MaxLength,Format");
        }
        else
        {
            url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,SchemaName,DisplayName,AttributeType,RequiredLevel,MaxLength,Format");
        }

        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetRelationships(JObject args)
    {
        var table = Require(args, "table");
        var url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/ManyToOneRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute");
        var manyToOne = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);

        url = BuildDataverseUrl($"EntityDefinitions(LogicalName='{table}')/OneToManyRelationships?$select=SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute");
        var oneToMany = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);

        return new JObject
        {
            ["manyToOne"] = manyToOne["value"] ?? new JArray(),
            ["oneToMany"] = oneToMany["value"] ?? new JArray()
        };
    }

    private async Task<JObject> ExecuteCountRows(JObject args)
    {
        var table = Require(args, "table");
        var filter = args["filter"]?.ToString();
        
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query.Add("$filter=" + Uri.EscapeDataString(filter));
        }
        query.Add("$count=true");
        query.Add("$top=0"); // Don't return any rows, just the count
        
        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var resp = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
        
        return new JObject
        {
            ["count"] = resp["@odata.count"] ?? 0
        };
    }

    private async Task<JObject> ExecuteAggregate(JObject args)
    {
        var table = Require(args, "table");
        var aggregateAttribute = Require(args, "aggregateAttribute");
        var aggregateFunction = Require(args, "aggregateFunction");
        var groupBy = args["groupBy"]?.ToString();
        var filter = args["filter"]?.ToString();
        var filterOperator = args["filterOperator"]?.ToString();
        var filterValue = args["filterValue"]?.ToString();
        
        // Build FetchXML for aggregation
        var fetchXml = $"<fetch aggregate='true'>";
        fetchXml += $"<entity name='{table}'>";
        fetchXml += $"<attribute name='{aggregateAttribute}' alias='result' aggregate='{aggregateFunction}' />";
        
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            fetchXml += $"<attribute name='{groupBy}' alias='groupby' groupby='true' />";
        }
        
        if (!string.IsNullOrWhiteSpace(filter) && !string.IsNullOrWhiteSpace(filterOperator) && !string.IsNullOrWhiteSpace(filterValue))
        {
            fetchXml += $"<filter><condition attribute='{filter}' operator='{filterOperator}' value='{System.Security.SecurityElement.Escape(filterValue)}' /></filter>";
        }
        
        fetchXml += "</entity></fetch>";
        
        var encodedFetch = Uri.EscapeDataString(fetchXml);
        var url = BuildDataverseUrl($"{table}?fetchXml={encodedFetch}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSavedQuery(JObject args)
    {
        var table = Require(args, "table");
        var viewName = Require(args, "viewName");
        var top = args["top"]?.Value<int?>() ?? 5;
        top = Math.Min(Math.Max(top, 1), 50);
        
        // Lookup the saved query by name
        var queryFilter = $"returnedtypecode eq '{table}' and name eq '{viewName.Replace("'", "''")}'";
        var queryUrl = BuildDataverseUrl($"savedqueries?$select=fetchxml&$filter={Uri.EscapeDataString(queryFilter)}&$top=1");
        var queryResult = await SendDataverseRequest(HttpMethod.Get, queryUrl, null).ConfigureAwait(false);
        
        var savedQueries = queryResult["value"] as JArray;
        if (savedQueries == null || savedQueries.Count == 0)
        {
            // Try user query
            queryUrl = BuildDataverseUrl($"userqueries?$select=fetchxml&$filter={Uri.EscapeDataString(queryFilter)}&$top=1");
            queryResult = await SendDataverseRequest(HttpMethod.Get, queryUrl, null).ConfigureAwait(false);
            savedQueries = queryResult["value"] as JArray;
            
            if (savedQueries == null || savedQueries.Count == 0)
            {
                return new JObject
                {
                    ["error"] = $"Saved query '{viewName}' not found for table '{table}'"
                };
            }
        }
        
        var fetchXml = savedQueries[0]["fetchxml"]?.ToString();
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            return new JObject { ["error"] = "FetchXML is empty" };
        }
        
        // Modify FetchXML to apply top limit
        fetchXml = System.Text.RegularExpressions.Regex.Replace(
            fetchXml, 
            "<fetch", 
            $"<fetch top='{top}'", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        var encodedFetch = Uri.EscapeDataString(fetchXml);
        var url = BuildDataverseUrl($"{table}?fetchXml={encodedFetch}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUploadAttachment(JObject args)
    {
        var regarding = Require(args, "regarding");
        var regardingType = Require(args, "regardingType");
        var fileName = Require(args, "fileName");
        var mimeType = Require(args, "mimeType");
        var content = Require(args, "content");
        var subject = args["subject"]?.ToString() ?? fileName;
        
        var annotation = new JObject
        {
            ["subject"] = subject,
            ["filename"] = fileName,
            ["mimetype"] = mimeType,
            ["documentbody"] = content,
            ["objectid_" + regardingType + "@odata.bind"] = $"/{regardingType}s({SanitizeGuid(regarding)})"
        };
        
        var url = BuildDataverseUrl("annotations");
        return await SendDataverseRequest(HttpMethod.Post, url, annotation).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDownloadAttachment(JObject args)
    {
        var annotationId = Require(args, "annotationId");
        var url = BuildDataverseUrl($"annotations({SanitizeGuid(annotationId)})?$select=filename,mimetype,documentbody,filesize");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteTrackChanges(JObject args)
    {
        var table = Require(args, "table");
        var select = args["select"]?.ToString();
        var deltaToken = args["deltaToken"]?.ToString();
        
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(select)) query.Add("$select=" + Uri.EscapeDataString(select));
        
        if (!string.IsNullOrWhiteSpace(deltaToken))
        {
            query.Add("$deltatoken=" + Uri.EscapeDataString(deltaToken));
        }
        
        var url = BuildDataverseUrl($"{table}?{string.Join("&", query)}");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        req.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        req.Headers.TryAddWithoutValidation("Prefer", "odata.track-changes");
        
        var response = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            return new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["error"] = "Change tracking may not be enabled for this table",
                ["body"] = TryParseJson(content)
            };
        }
        
        return TryParseJson(content);
    }

    private async Task<JObject> ExecuteGetGlobalOptionSets(JObject args)
    {
        var optionSetName = args["optionSetName"]?.ToString();
        
        string url;
        if (!string.IsNullOrWhiteSpace(optionSetName))
        {
            url = BuildDataverseUrl($"GlobalOptionSetDefinitions(Name='{optionSetName}')?$select=Name,DisplayName,Options");
        }
        else
        {
            url = BuildDataverseUrl("GlobalOptionSetDefinitions?$select=Name,DisplayName,Options");
        }
        
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBusinessRules(JObject args)
    {
        var table = Require(args, "table");
        
        // Query workflows where category = 2 (business rule) and primary entity matches
        var filter = $"category eq 2 and primaryentity eq '{table}'";
        var url = BuildDataverseUrl($"workflows?$select=name,description,statecode,statuscode,xaml&$filter={Uri.EscapeDataString(filter)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetSecurityRoles(JObject args)
    {
        var roleName = args["roleName"]?.ToString();
        
        string url;
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var filter = $"name eq '{roleName.Replace("'", "''")}'";
            url = BuildDataverseUrl($"roles?$select=name,roleid,businessunitid&$filter={Uri.EscapeDataString(filter)}");
        }
        else
        {
            url = BuildDataverseUrl("roles?$select=name,roleid,businessunitid&$top=50");
        }
        
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAsyncOperation(JObject args)
    {
        var asyncOperationId = Require(args, "asyncOperationId");
        var url = BuildDataverseUrl($"asyncoperations({SanitizeGuid(asyncOperationId)})?$select=name,statuscode,statecode,message,friendlymessage,errorcode,createdon,completedon");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListAsyncOperations(JObject args)
    {
        var status = args["status"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        var query = new List<string>();
        query.Add("$select=name,statuscode,statecode,message,createdon,completedon");
        query.Add("$orderby=createdon desc");
        query.Add($"$top={top}");
        
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Map friendly status to statuscode values
            var statusCode = status.ToLower() switch
            {
                "inprogress" => "20",
                "succeeded" => "30",
                "failed" => "31",
                "canceled" => "32",
                _ => null
            };
            
            if (statusCode != null)
            {
                query.Add($"$filter=statuscode eq {statusCode}");
            }
        }
        
        var url = BuildDataverseUrl($"asyncoperations?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDetectDuplicates(JObject args)
    {
        var table = Require(args, "table");
        var record = RequireObject(args, "record");
        
        // Use RetrieveDuplicates action
        var requestBody = new JObject
        {
            ["BusinessEntity"] = record,
            ["MatchingEntityName"] = table,
            ["PagingInfo"] = new JObject
            {
                ["PageNumber"] = 1,
                ["Count"] = 50
            }
        };
        
        var url = BuildDataverseUrl("RetrieveDuplicates");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetAuditHistory(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        // Query audit table for the specific record
        var filter = $"objectid/Id eq {SanitizeGuid(recordId)} and objecttypecode eq '{table}'";
        var url = BuildDataverseUrl($"audits?$select=createdon,action,userid,attributemask,changedata&$filter={Uri.EscapeDataString(filter)}&$orderby=createdon desc&$top={top}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetPluginTraces(JObject args)
    {
        var correlationId = args["correlationId"]?.ToString();
        var top = args["top"]?.Value<int?>() ?? 10;
        top = Math.Min(Math.Max(top, 1), 50);
        
        var query = new List<string>();
        query.Add("$select=typename,messageblock,exceptiondetails,createdon,correlationid");
        query.Add("$orderby=createdon desc");
        query.Add($"$top={top}");
        
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query.Add($"$filter=correlationid eq {SanitizeGuid(correlationId)}");
        }
        
        var url = BuildDataverseUrl($"plugintracelog?{string.Join("&", query)}");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteWhoAmI(JObject args)
    {
        var url = BuildDataverseUrl("WhoAmI");
        return await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSetState(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var state = args["state"]?.Value<int?>() ?? throw new Exception("'state' is required");
        var status = args["status"]?.Value<int?>() ?? throw new Exception("'status' is required");
        
        var requestBody = new JObject
        {
            ["EntityMoniker"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["State"] = state,
            ["Status"] = status
        };
        
        var url = BuildDataverseUrl("SetState");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssign(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var assigneeId = Require(args, "assigneeId");
        var assigneeType = Require(args, "assigneeType");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Assignee"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{assigneeType}",
                [assigneeType + "id"] = SanitizeGuid(assigneeId)
            }
        };
        
        var url = BuildDataverseUrl("Assign");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteMerge(JObject args)
    {
        var table = Require(args, "table");
        var targetId = Require(args, "targetId");
        var subordinateId = Require(args, "subordinateId");
        var updateContent = args["updateContent"] as JObject;
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(targetId)
            },
            ["SubordinateId"] = SanitizeGuid(subordinateId),
            ["PerformParentingChecks"] = false
        };
        
        if (updateContent != null)
        {
            requestBody["UpdateContent"] = updateContent;
        }
        
        var url = BuildDataverseUrl("Merge");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteShare(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        var principalType = Require(args, "principalType");
        var accessMask = Require(args, "accessMask");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["PrincipalAccess"] = new JObject
            {
                ["Principal"] = new JObject
                {
                    ["@odata.type"] = $"Microsoft.Dynamics.CRM.{principalType}",
                    [principalType + "id"] = SanitizeGuid(principalId)
                },
                ["AccessMask"] = accessMask
            }
        };
        
        var url = BuildDataverseUrl("GrantAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUnshare(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Revokee"] = new JObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                ["systemuserid"] = SanitizeGuid(principalId)
            }
        };
        
        var url = BuildDataverseUrl("RevokeAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteModifyAccess(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        var accessMask = Require(args, "accessMask");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["PrincipalAccess"] = new JObject
            {
                ["Principal"] = new JObject
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                    ["systemuserid"] = SanitizeGuid(principalId)
                },
                ["AccessMask"] = accessMask
            }
        };
        
        var url = BuildDataverseUrl("ModifyAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAddTeamMembers(JObject args)
    {
        var teamId = Require(args, "teamId");
        var memberIds = args["memberIds"] as JArray ?? throw new Exception("'memberIds' must be an array");
        
        var results = new JArray();
        foreach (var memberId in memberIds)
        {
            var requestBody = new JObject
            {
                ["TeamId"] = SanitizeGuid(teamId),
                ["MemberId"] = SanitizeGuid(memberId.ToString())
            };
            
            var url = BuildDataverseUrl("AddMembersTeam");
            var result = await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
            results.Add(result);
        }
        
        return new JObject { ["results"] = results };
    }

    private async Task<JObject> ExecuteRemoveTeamMembers(JObject args)
    {
        var teamId = Require(args, "teamId");
        var memberIds = args["memberIds"] as JArray ?? throw new Exception("'memberIds' must be an array");
        
        var results = new JArray();
        foreach (var memberId in memberIds)
        {
            var requestBody = new JObject
            {
                ["TeamId"] = SanitizeGuid(teamId),
                ["MemberId"] = SanitizeGuid(memberId.ToString())
            };
            
            var url = BuildDataverseUrl("RemoveMembersTeam");
            var result = await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
            results.Add(result);
        }
        
        return new JObject { ["results"] = results };
    }

    private async Task<JObject> ExecuteRetrievePrincipalAccess(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var principalId = Require(args, "principalId");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["Principal"] = new JObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.systemuser",
                ["systemuserid"] = SanitizeGuid(principalId)
            }
        };
        
        var url = BuildDataverseUrl("RetrievePrincipalAccess");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteInitializeFrom(JObject args)
    {
        var sourceTable = Require(args, "sourceTable");
        var sourceId = Require(args, "sourceId");
        var targetTable = Require(args, "targetTable");
        
        var requestBody = new JObject
        {
            ["EntityMoniker"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{sourceTable}",
                [sourceTable + "id"] = SanitizeGuid(sourceId)
            },
            ["TargetEntityName"] = targetTable,
            ["TargetFieldType"] = 0
        };
        
        var url = BuildDataverseUrl("InitializeFrom");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCalculateRollup(JObject args)
    {
        var table = Require(args, "table");
        var recordId = Require(args, "recordId");
        var fieldName = Require(args, "fieldName");
        
        var requestBody = new JObject
        {
            ["Target"] = new JObject
            {
                ["@odata.type"] = $"Microsoft.Dynamics.CRM.{table}",
                [table + "id"] = SanitizeGuid(recordId)
            },
            ["FieldName"] = fieldName
        };
        
        var url = BuildDataverseUrl("CalculateRollupField");
        return await SendDataverseRequest(HttpMethod.Post, url, requestBody).ConfigureAwait(false);
    }

    private string BuildDataverseUrl(string relativePath)
    {
        var clean = relativePath.TrimStart('/');
        return $"/api/data/v9.2/{clean}";
    }

    private async Task<JObject> SendDataverseRequest(HttpMethod method, string url, JObject body, bool includeFormatted = false, string impersonateUserId = null, string correlationId = null)
    {
        // Ensure absolute URL for Dataverse requests
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
            url = $"{baseUrl}{url}";
            this.Context.Logger.LogDebug($"Constructed absolute URL: {url}");
        }
        
        var req = new HttpRequestMessage(method, url);
        
        // Copy OAuth token from incoming request to Dataverse request
        if (this.Context.Request.Headers.Authorization != null)
        {
            req.Headers.Authorization = this.Context.Request.Headers.Authorization;
            this.Context.Logger.LogDebug("OAuth token forwarded to Dataverse request");
        }
        
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        req.Headers.TryAddWithoutValidation("OData-Version", "4.0");

        // Impersonation header
        if (!string.IsNullOrWhiteSpace(impersonateUserId))
        {
            req.Headers.TryAddWithoutValidation("MSCRMCallerID", SanitizeGuid(impersonateUserId));
        }

        // Telemetry/correlation header for request tracking
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            req.Headers.TryAddWithoutValidation("x-ms-correlation-request-id", correlationId);
        }

        // Include formatted values for lookups/optionsets/money fields
        if (includeFormatted)
        {
            req.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");
        }

        // Ask Dataverse to return representations on writes
        if (method == HttpMethod.Post || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            var preferValue = includeFormatted ? "return=representation,odata.include-annotations=\"*\"" : "return=representation";
            req.Headers.Remove("Prefer");
            req.Headers.TryAddWithoutValidation("Prefer", preferValue);
        }

        // Use wildcard ETag to allow overwrite when no specific ETag is supplied
        if (method == HttpMethod.Delete || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.TryAddWithoutValidation("If-Match", "*");
        }

        if (body != null)
        {
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"Dataverse API error: {response.StatusCode} - {url}");
            // Enhanced error parsing for Dataverse errors
            var errorObj = new JObject
            {
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase
            };

            try
            {
                var errorBody = JObject.Parse(content);
                var error = errorBody["error"];
                if (error != null)
                {
                    errorObj["errorCode"] = error["code"];
                    errorObj["message"] = error["message"];
                    errorObj["details"] = error["innererror"]?["message"] ?? error["message"];
                }
                else
                {
                    errorObj["body"] = errorBody;
                }
            }
            catch
            {
                errorObj["body"] = content;
            }

            return errorObj;
        }

        return TryParseJson(content);
    }

    private JObject TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new JObject();
        try { return JObject.Parse(text); }
        catch { return new JObject { ["text"] = text }; }
    }

    private string Require(JObject obj, string name)
    {
        var val = obj?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"{name} is required");
        return val;
    }

    private JObject RequireObject(JObject obj, string name)
    {
        var token = obj?[name] as JObject;
        if (token == null) throw new ArgumentException($"{name} must be an object");
        return token;
    }

    private string SanitizeGuid(string id)
    {
        var trimmed = id.Trim();
        if (Guid.TryParse(trimmed, out var g)) return g.ToString();
        throw new ArgumentException("id must be a GUID");
    }

    // ---------- AI Function Calling Helpers ----------
    private JArray ConvertMCPToolsToFunctions(JArray mcpTools)
    {
        var functions = new JArray();
        foreach (var tool in mcpTools)
        {
            functions.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = tool["name"],
                    ["description"] = tool["description"],
                    ["parameters"] = tool["inputSchema"]
                }
            });
        }
        return functions;
    }

    private async Task<(string response, JArray toolCalls, int tokensUsed)> ExecuteAIWithFunctions(
        string apiKey,
        string model,
        string baseUrl,
        JArray messages,
        JArray functions,
        double temperature,
        int maxTokens,
        bool autoExecute,
        int maxIterations)
    {
        var allToolCalls = new JArray();
        var totalTokens = 0;
        var iterations = 0;

        while (iterations < maxIterations)
        {
            iterations++;
            var isAnthropic = baseUrl.IndexOf("anthropic.com", StringComparison.OrdinalIgnoreCase) >= 0;
            var isAzureOpenAI = baseUrl.IndexOf(".openai.azure.com", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                baseUrl.IndexOf(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase) >= 0;
            var isOpenAI = !isAnthropic && !isAzureOpenAI;

            HttpRequestMessage request;
            JObject body;

            if (isAnthropic)
            {
                body = new JObject
                {
                    ["model"] = model,
                    ["max_tokens"] = maxTokens,
                    ["temperature"] = temperature,
                    ["messages"] = ConvertToAnthropicMessages(messages)
                };
                if (functions.Any()) body["tools"] = ConvertToAnthropicTools(functions);
                var sys = messages.FirstOrDefault(m => m["role"]?.ToString() == "system");
                if (sys != null) body["system"] = sys["content"];
                request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages");
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (isAzureOpenAI)
            {
                body = new JObject
                {
                    ["model"] = model,
                    ["messages"] = messages,
                    ["temperature"] = temperature,
                    ["max_tokens"] = maxTokens
                };
                if (functions.Any()) { body["tools"] = functions; body["tool_choice"] = "auto"; }
                // Azure AI Foundry uses /openai/responses endpoint with Bearer auth
                request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/openai/responses?api-version=2025-04-01-preview");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
            else
            {
                body = new JObject
                {
                    ["model"] = model,
                    ["messages"] = messages,
                    ["temperature"] = temperature,
                    ["max_tokens"] = maxTokens
                };
                if (functions.Any()) { body["tools"] = functions; body["tool_choice"] = "auto"; }
                request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            this.Context.Logger.LogInformation($"Calling AI API: {model}");
            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this.Context.Logger.LogError($"AI API error: {response.StatusCode} - {responseBody}");
                throw new Exception($"AI API error: {response.StatusCode} - {responseBody}");
            }

            var json = JObject.Parse(responseBody);
            JArray toolCalls;
            string content;

            if (isAnthropic)
            {
                var usage = json["usage"];
                totalTokens += (usage?["input_tokens"]?.Value<int>() ?? 0) + (usage?["output_tokens"]?.Value<int>() ?? 0);
                toolCalls = new JArray();
                content = "";
                var blocks = json["content"] as JArray;
                if (blocks != null)
                {
                    foreach (var b in blocks)
                    {
                        var t = b["type"]?.ToString();
                        if (t == "text") content += b["text"]?.ToString();
                        if (t == "tool_use")
                        {
                            toolCalls.Add(new JObject
                            {
                                ["id"] = b["id"],
                                ["type"] = "function",
                                ["function"] = new JObject
                                {
                                    ["name"] = b["name"],
                                    ["arguments"] = b["input"]?.ToString()
                                }
                            });
                        }
                    }
                }
                if (!toolCalls.Any()) return (content, allToolCalls, totalTokens);
                // add assistant tool call message
                var assistantContent = new JArray();
                foreach (var tc in toolCalls)
                {
                    assistantContent.Add(new JObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = tc["id"],
                        ["name"] = tc["function"]?["name"],
                        ["input"] = JObject.Parse(tc["function"]?["arguments"]?.ToString() ?? "{}")
                    });
                }
                messages.Add(new JObject { ["role"] = "assistant", ["content"] = assistantContent });
            }
            else
            {
                totalTokens += json["usage"]?["total_tokens"]?.Value<int>() ?? 0;
                var choice = json["choices"]?[0];
                var msg = choice?["message"] as JObject;
                toolCalls = msg?["tool_calls"] as JArray;
                if (toolCalls == null || !toolCalls.Any())
                {
                    content = msg?["content"]?.ToString() ?? "";
                    return (content, allToolCalls, totalTokens);
                }
                messages.Add(new JObject { ["role"] = "assistant", ["tool_calls"] = toolCalls, ["content"] = null });
            }

            if (!autoExecute)
            {
                return ("Tool calls ready (autoExecuteTools=false)", allToolCalls, totalTokens);
            }

            this.Context.Logger.LogInformation($"AI requested {toolCalls.Count} tool calls");
            foreach (var toolCall in toolCalls)
            {
                var fn = toolCall["function"] as JObject;
                var fnName = fn?["name"]?.ToString();
                this.Context.Logger.LogDebug($"AI tool call: {fnName}");
                var args = fn?["arguments"]?.ToString() ?? "{}";
                JObject parsedArgs;
                try { parsedArgs = JObject.Parse(args); } catch { parsedArgs = new JObject(); }

                JObject toolResult;
                bool success = true;
                string error = null;
                try
                {
                    toolResult = await ExecuteToolByName(fnName, parsedArgs).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    success = false;
                    error = ex.Message;
                    toolResult = new JObject { ["error"] = ex.Message };
                }

                allToolCalls.Add(new JObject
                {
                    ["tool"] = fnName,
                    ["arguments"] = parsedArgs,
                    ["result"] = toolResult,
                    ["success"] = success,
                    ["error"] = error
                });

                if (isAnthropic)
                {
                    messages.Add(new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "tool_result",
                                ["tool_use_id"] = toolCall["id"],
                                ["content"] = toolResult.ToString(Newtonsoft.Json.Formatting.None)
                            }
                        }
                    });
                }
                else
                {
                    messages.Add(new JObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = toolCall["id"],
                        ["content"] = toolResult.ToString(Newtonsoft.Json.Formatting.None)
                    });
                }
            }
        }

        return ("Max tool call iterations reached", allToolCalls, totalTokens);
    }

    private JArray ConvertToAnthropicMessages(JArray messages)
    {
        var arr = new JArray();
        foreach (var m in messages)
        {
            if (m["role"]?.ToString() != "system") arr.Add(m);
        }
        return arr;
    }

    private JArray ConvertToAnthropicTools(JArray openAiTools)
    {
        var arr = new JArray();
        foreach (var t in openAiTools)
        {
            var fn = t["function"] as JObject;
            arr.Add(new JObject
            {
                ["name"] = fn?["name"],
                ["description"] = fn?["description"],
                ["input_schema"] = fn?["parameters"]
            });
        }
        return arr;
    }

    // ---------- Query Extraction Helpers ----------
    private JObject ExtractSuggestedQuery(JArray toolCalls)
    {
        var suggestedQuery = new JObject();
        var relationships = new HashSet<string>();
        
        foreach (var call in toolCalls)
        {
            var toolName = call["tool"]?.ToString();
            var args = call["arguments"] as JObject;
            
            if (args == null) continue;
            
            // Extract table from various operations
            if (string.IsNullOrEmpty(suggestedQuery["table"]?.ToString()))
            {
                var table = args["table_name"] ?? args["entity_name"];
                if (table != null) suggestedQuery["table"] = table;
            }
            
            // Extract entity name (singular)
            if (string.IsNullOrEmpty(suggestedQuery["entityName"]?.ToString()))
            {
                var entityName = args["entity_name"];
                if (entityName != null) suggestedQuery["entityName"] = entityName;
            }
            
            // Extract select columns
            if (string.IsNullOrEmpty(suggestedQuery["select"]?.ToString()))
            {
                var select = args["columns"] ?? args["select"];
                if (select != null) suggestedQuery["select"] = select;
            }
            
            // Extract filter
            if (string.IsNullOrEmpty(suggestedQuery["filter"]?.ToString()))
            {
                var filter = args["filter"];
                if (filter != null) suggestedQuery["filter"] = filter;
            }
            
            // Extract orderby
            if (string.IsNullOrEmpty(suggestedQuery["orderby"]?.ToString()))
            {
                var orderby = args["orderby"] ?? args["order_by"] ?? args["sort"];
                if (orderby != null) suggestedQuery["orderby"] = orderby;
            }
            
            // Extract expand
            if (string.IsNullOrEmpty(suggestedQuery["expand"]?.ToString()))
            {
                var expand = args["expand"];
                if (expand != null) suggestedQuery["expand"] = expand;
            }
            
            // Extract top
            if (suggestedQuery["top"] == null)
            {
                var top = args["top"] ?? args["max_results"] ?? args["limit"];
                if (top != null && int.TryParse(top.ToString(), out int topValue))
                {
                    suggestedQuery["top"] = topValue;
                }
            }
            
            // Extract skip
            if (suggestedQuery["skip"] == null)
            {
                var skip = args["skip"] ?? args["offset"];
                if (skip != null && int.TryParse(skip.ToString(), out int skipValue))
                {
                    suggestedQuery["skip"] = skipValue;
                }
            }
            
            // Extract count
            if (suggestedQuery["count"] == null)
            {
                var count = args["count"] ?? args["include_count"];
                if (count != null && bool.TryParse(count.ToString(), out bool countValue))
                {
                    suggestedQuery["count"] = countValue;
                }
            }
            
            // Extract apply
            if (string.IsNullOrEmpty(suggestedQuery["apply"]?.ToString()))
            {
                var apply = args["apply"];
                if (apply != null) suggestedQuery["apply"] = apply;
            }
            
            // Extract record ID
            if (string.IsNullOrEmpty(suggestedQuery["recordId"]?.ToString()))
            {
                var id = args["row_id"] ?? args["id"] ?? args["record_id"];
                if (id != null) suggestedQuery["recordId"] = id;
            }
            
            // Extract relationships from expand or navigation properties
            var expandValue = args["expand"]?.ToString();
            if (!string.IsNullOrEmpty(expandValue))
            {
                // Parse expand like "primarycontactid($select=fullname)"
                var relationshipNames = expandValue.Split(new[] { ',', '(' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var rel in relationshipNames)
                {
                    var cleaned = rel.Trim();
                    if (!string.IsNullOrEmpty(cleaned) && !cleaned.StartsWith("$"))
                    {
                        relationships.Add(cleaned);
                    }
                }
            }
        }
        
        if (relationships.Count > 0)
        {
            suggestedQuery["relationships"] = new JArray(relationships);
        }
        
        return suggestedQuery;
    }
    
    private JArray ExtractTablesAccessed(JArray toolCalls)
    {
        var tables = new HashSet<string>();
        
        foreach (var call in toolCalls)
        {
            var args = call["arguments"] as JObject;
            if (args == null) continue;
            
            var table = args["table_name"] ?? args["entity_name"];
            if (table != null)
            {
                tables.Add(table.ToString());
            }
        }
        
        return new JArray(tables);
    }
    
    private JArray ExtractColumnsUsed(JArray toolCalls)
    {
        var columns = new HashSet<string>();
        
        foreach (var call in toolCalls)
        {
            var args = call["arguments"] as JObject;
            if (args == null) continue;
            
            var select = args["columns"] ?? args["select"];
            if (select != null)
            {
                var columnList = select.ToString().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var col in columnList)
                {
                    columns.Add(col.Trim());
                }
            }
        }
        
        return new JArray(columns);
    }
    
    private int CountRecordsAffected(JArray toolCalls)
    {
        int count = 0;
        
        foreach (var call in toolCalls)
        {
            var toolName = call["tool"]?.ToString();
            var result = call["result"] as JObject;
            
            if (result == null) continue;
            
            // Count based on operation type
            if (toolName?.Contains("create") == true || toolName?.Contains("update") == true || toolName?.Contains("delete") == true)
            {
                // Single record operations
                if (call["success"]?.Value<bool>() == true)
                {
                    count++;
                }
            }
            else if (toolName?.Contains("batch") == true || toolName?.Contains("multiple") == true)
            {
                // Multiple record operations - try to extract count from result
                var records = result["records"] as JArray;
                if (records != null)
                {
                    count += records.Count;
                }
            }
        }
        
        return count;
    }

    // ---------- Query/Metadata Handlers ----------
    private async Task<HttpResponseMessage> HandleGetTables()
    {
        try
        {
            // Query EntityDefinitions for common tables
            var url = "/api/data/v9.2/EntityDefinitions?$select=LogicalName,DisplayName&$filter=IsValidForAdvancedFind eq true and IsCustomizable/Value eq true";
            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve tables",
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }

            var entities = data["value"] as JArray;
            var tables = new JArray();
            
            if (entities != null)
            {
                foreach (var entity in entities)
                {
                    var logicalName = entity["LogicalName"]?.ToString();
                    var displayName = entity["DisplayName"]?["UserLocalizedLabel"]?["Label"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(logicalName))
                    {
                        tables.Add(new JObject
                        {
                            ["name"] = logicalName,
                            ["displayName"] = displayName ?? logicalName
                        });
                    }
                }
            }

            return CreateHttpResponse(HttpStatusCode.OK, tables);
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleGetTableSchema(string table)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }

            // Query EntityDefinition for attributes
            var url = $"/api/data/v9.2/EntityDefinitions(LogicalName='{table}')?$select=LogicalName&$expand=Attributes($select=LogicalName,DisplayName,AttributeType)";
            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve table schema",
                    ["table"] = table,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }

            var attributes = data["Attributes"] as JArray;
            var properties = new JObject();
            
            if (attributes != null)
            {
                foreach (var attr in attributes)
                {
                    var logicalName = attr["LogicalName"]?.ToString();
                    if (!string.IsNullOrEmpty(logicalName))
                    {
                        properties[logicalName] = new JObject
                        {
                            ["type"] = "string",  // Simplified - all properties as string
                            ["description"] = attr["DisplayName"]?["UserLocalizedLabel"]?["Label"]?.ToString() ?? logicalName
                        };
                    }
                }
            }

            var schema = new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = properties
                }
            };

            return CreateHttpResponse(HttpStatusCode.OK, new JObject
            {
                ["schema"] = schema
            });
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleListRecords(string table, string select, string filter, int top)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }

            // Build OData URL
            var url = $"/api/data/v9.2/{table}";
            var queryParams = new List<string>();
            
            if (!string.IsNullOrEmpty(select))
                queryParams.Add($"$select={select}");
            if (!string.IsNullOrEmpty(filter))
                queryParams.Add($"$filter={filter}");
            if (top > 0 && top <= 50)
                queryParams.Add($"$top={top}");

            if (queryParams.Any())
                url += "?" + string.Join("&", queryParams);

            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve records",
                    ["table"] = table,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }
            
            return CreateHttpResponse(HttpStatusCode.OK, data["value"] ?? new JArray());
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    private async Task<HttpResponseMessage> HandleGetRecord(string table, string id, string select)
    {
        try
        {
            if (string.IsNullOrEmpty(table))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Table parameter required"
                });
            }
            if (string.IsNullOrEmpty(id))
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "ID parameter required"
                });
            }

            // Build OData URL
            var url = $"/api/data/v9.2/{table}({id})";
            if (!string.IsNullOrEmpty(select))
                url += $"?$select={select}";

            var data = await SendDataverseRequest(HttpMethod.Get, url, null).ConfigureAwait(false);
            
            if (data["status"] != null)
            {
                return CreateHttpResponse(HttpStatusCode.BadRequest, new JObject
                {
                    ["error"] = "Failed to retrieve record",
                    ["table"] = table,
                    ["id"] = id,
                    ["status"] = data["status"],
                    ["message"] = data["message"]
                });
            }
            
            return CreateHttpResponse(HttpStatusCode.OK, data);
        }
        catch (Exception ex)
        {
            return CreateHttpResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    // ---------- Helpers ----------
    // Connection parameters are not used (OAuth handles Dataverse token; AI key is constant)

    private HttpResponseMessage CreateHttpResponse(HttpStatusCode statusCode, JToken content)
    {
        var resp = new HttpResponseMessage(statusCode);
        resp.Content = CreateJsonContent(content.ToString());
        return resp;
    }

    private HttpResponseMessage CreateSuccessResponse(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    private HttpResponseMessage CreateErrorResponse(int code, string message, JToken id)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject { ["code"] = code, ["message"] = message },
            ["id"] = id
        };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    private HttpResponseMessage CreateAgentErrorResponse(string message, string details, string errorType, string errorCode)
    {
        var payload = new JObject
        {
            ["response"] = null,
            ["error"] = message,
            ["errorType"] = errorType,
            ["errorCode"] = errorCode,
            ["details"] = details
        };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(payload.ToString()) };
        return resp;
    }
}




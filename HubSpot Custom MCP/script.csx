using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string HUBSPOT_BASE = "https://api.hubapi.com";
    private const string PROTOCOL_VERSION = "2024-11-05";
    private const string SERVER_NAME = "hubspot-custom-mcp";
    private const string SERVER_VERSION = "1.0.0";

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);
            var method = request["method"]?.ToString() ?? "";
            var requestId = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            await LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method
            });

            HttpResponseMessage result;
            switch (method)
            {
                case "initialize":
                    result = HandleInitialize(requestId);
                    break;

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    result = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;

                case "tools/list":
                    result = HandleToolsList(requestId);
                    break;

                case "tools/call":
                    result = await HandleToolsCall(@params, requestId, correlationId);
                    break;

                case "resources/list":
                    result = CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                    break;

                case "ping":
                    result = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;

                default:
                    result = CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
                    break;
            }

            await LogToAppInsights("McpRequestCompleted", new
            {
                CorrelationId = correlationId,
                Method = method,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            return result;
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("McpRequestError", new
            {
                CorrelationId = correlationId,
                Error = ex.Message,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
            return CreateJsonRpcErrorResponse(null, -32603, "Internal error", ex.Message);
        }
    }

    // ── MCP: initialize ──────────────────────────────────────────────────

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    // ── MCP: tools/list ──────────────────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // ── Companies ────────────────────────────────────────────
            Tool("list_companies", "List HubSpot companies. Returns paginated results with optional property selection.",
                Props(
                    P("properties", "string", "Comma-separated property names to return (e.g., name,domain,industry)", false),
                    P("limit", "integer", "Max results per page (1-100, default 10)", false),
                    P("after", "string", "Pagination cursor from previous response", false)
                )),
            Tool("get_company", "Get a single HubSpot company by ID.",
                Props(
                    P("companyId", "string", "The company record ID", true),
                    P("properties", "string", "Comma-separated property names to return", false)
                ), "companyId"),
            Tool("create_company", "Create a new HubSpot company.",
                Props(
                    P("properties", "object", "Company properties object (e.g., {name, domain, industry, phone})", true)
                ), "properties"),
            Tool("update_company", "Update an existing HubSpot company by ID.",
                Props(
                    P("companyId", "string", "The company record ID to update", true),
                    P("properties", "object", "Properties to update", true)
                ), "companyId", "properties"),
            Tool("delete_company", "Delete a HubSpot company by ID (moves to recycling bin).",
                Props(P("companyId", "string", "The company record ID to delete", true)),
                "companyId"),
            Tool("search_companies", "Search HubSpot companies using filters and query.",
                Props(
                    P("query", "string", "Text query to search across default searchable properties", false),
                    P("filterGroups", "array", "Filter groups array with filters [{propertyName, operator, value}]. Operators: EQ, NEQ, LT, LTE, GT, GTE, CONTAINS_TOKEN, NOT_CONTAINS_TOKEN", false),
                    P("properties", "array", "Array of property names to return in results", false),
                    P("sorts", "array", "Array of sort objects [{propertyName, direction}] where direction is ASCENDING or DESCENDING", false),
                    P("limit", "integer", "Max results (1-100, default 10)", false),
                    P("after", "integer", "Pagination offset", false)
                )),

            // ── Owners (Sales Reps) ──────────────────────────────────
            Tool("list_owners", "List HubSpot owners (sales reps/users). Returns all owners in the account.",
                Props(
                    P("email", "string", "Filter by owner email address", false),
                    P("limit", "integer", "Max results per page (default 100)", false),
                    P("after", "string", "Pagination cursor from previous response", false)
                )),
            Tool("get_owner", "Get a single HubSpot owner (sales rep) by ID.",
                Props(P("ownerId", "string", "The owner ID", true)),
                "ownerId"),

            // ── Emails ───────────────────────────────────────────────
            Tool("list_emails", "List HubSpot email engagements. Returns logged email activities.",
                Props(
                    P("properties", "string", "Comma-separated property names (e.g., hs_email_subject,hs_email_text,hs_email_direction)", false),
                    P("limit", "integer", "Max results per page (1-100, default 10)", false),
                    P("after", "string", "Pagination cursor from previous response", false)
                )),
            Tool("get_email", "Get a single HubSpot email engagement by ID.",
                Props(
                    P("emailId", "string", "The email engagement record ID", true),
                    P("properties", "string", "Comma-separated property names to return", false)
                ), "emailId"),
            Tool("create_email", "Create a new HubSpot email engagement (log an email).",
                Props(
                    P("properties", "object", "Email properties (e.g., {hs_email_subject, hs_email_text, hs_email_direction: EMAIL_SENT|EMAIL_RECEIVED, hs_timestamp})", true),
                    P("associations", "array", "Array of association objects [{to:{id}, types:[{associationCategory, associationTypeId}]}]", false)
                ), "properties"),
            Tool("update_email", "Update an existing HubSpot email engagement.",
                Props(
                    P("emailId", "string", "The email engagement record ID", true),
                    P("properties", "object", "Properties to update", true)
                ), "emailId", "properties"),
            Tool("delete_email", "Delete a HubSpot email engagement by ID.",
                Props(P("emailId", "string", "The email engagement record ID", true)),
                "emailId"),
            Tool("search_emails", "Search HubSpot email engagements using filters.",
                Props(
                    P("query", "string", "Text query to search email content", false),
                    P("filterGroups", "array", "Filter groups array with filters [{propertyName, operator, value}]", false),
                    P("properties", "array", "Array of property names to return", false),
                    P("sorts", "array", "Array of sort objects [{propertyName, direction}]", false),
                    P("limit", "integer", "Max results (1-100, default 10)", false),
                    P("after", "integer", "Pagination offset", false)
                )),

            // ── Sequences ────────────────────────────────────────────
            Tool("list_sequences", "List HubSpot sales sequences available in the account.",
                Props(
                    P("limit", "integer", "Max results per page (default 10)", false),
                    P("after", "string", "Pagination cursor", false)
                )),
            Tool("get_sequence", "Get a single HubSpot sequence by ID.",
                Props(P("sequenceId", "string", "The sequence ID", true)),
                "sequenceId"),
            Tool("enroll_in_sequence", "Enroll a contact in a HubSpot sequence.",
                Props(
                    P("sequenceId", "string", "The sequence ID to enroll in", true),
                    P("contactId", "string", "The contact ID to enroll", true),
                    P("senderEmail", "string", "The sender email address (must be a connected email in HubSpot)", true),
                    P("startingStepOrder", "integer", "Step to start at (0-based, default 0)", false)
                ), "sequenceId", "contactId", "senderEmail"),

            // ── Tasks (Reminders) ────────────────────────────────────
            Tool("list_tasks", "List HubSpot tasks (reminders/to-dos).",
                Props(
                    P("properties", "string", "Comma-separated property names (e.g., hs_task_subject,hs_task_body,hs_task_status,hs_task_priority)", false),
                    P("limit", "integer", "Max results per page (1-100, default 10)", false),
                    P("after", "string", "Pagination cursor", false)
                )),
            Tool("get_task", "Get a single HubSpot task by ID.",
                Props(
                    P("taskId", "string", "The task record ID", true),
                    P("properties", "string", "Comma-separated property names", false)
                ), "taskId"),
            Tool("create_task", "Create a new HubSpot task (reminder/to-do).",
                Props(
                    P("properties", "object", "Task properties (e.g., {hs_task_subject, hs_task_body, hs_task_status: NOT_STARTED|COMPLETED|IN_PROGRESS|DEFERRED, hs_task_priority: LOW|MEDIUM|HIGH, hs_timestamp, hubspot_owner_id})", true),
                    P("associations", "array", "Array of association objects to link task to contacts/companies/deals", false)
                ), "properties"),
            Tool("update_task", "Update an existing HubSpot task.",
                Props(
                    P("taskId", "string", "The task record ID", true),
                    P("properties", "object", "Properties to update", true)
                ), "taskId", "properties"),
            Tool("delete_task", "Delete a HubSpot task by ID.",
                Props(P("taskId", "string", "The task record ID", true)),
                "taskId"),
            Tool("search_tasks", "Search HubSpot tasks using filters.",
                Props(
                    P("query", "string", "Text query to search tasks", false),
                    P("filterGroups", "array", "Filter groups array [{propertyName, operator, value}]", false),
                    P("properties", "array", "Array of property names to return", false),
                    P("sorts", "array", "Sort objects [{propertyName, direction}]", false),
                    P("limit", "integer", "Max results (1-100, default 10)", false),
                    P("after", "integer", "Pagination offset", false)
                )),

            // ── Deals (Contract Lifecycles) ──────────────────────────
            Tool("list_deals", "List HubSpot deals (contract lifecycles). Use to track contract status through pipeline stages.",
                Props(
                    P("properties", "string", "Comma-separated property names (e.g., dealname,dealstage,pipeline,amount,closedate,hubspot_owner_id)", false),
                    P("limit", "integer", "Max results per page (1-100, default 10)", false),
                    P("after", "string", "Pagination cursor", false)
                )),
            Tool("get_deal", "Get a single HubSpot deal (contract lifecycle) by ID.",
                Props(
                    P("dealId", "string", "The deal record ID", true),
                    P("properties", "string", "Comma-separated property names", false)
                ), "dealId"),
            Tool("create_deal", "Create a new HubSpot deal (contract lifecycle entry).",
                Props(
                    P("properties", "object", "Deal properties (e.g., {dealname, dealstage, pipeline, amount, closedate, hubspot_owner_id})", true),
                    P("associations", "array", "Array of association objects to link deal to contacts/companies", false)
                ), "properties"),
            Tool("update_deal", "Update an existing HubSpot deal (contract lifecycle).",
                Props(
                    P("dealId", "string", "The deal record ID", true),
                    P("properties", "object", "Properties to update (e.g., dealstage to move through pipeline)", true)
                ), "dealId", "properties"),
            Tool("delete_deal", "Delete a HubSpot deal by ID.",
                Props(P("dealId", "string", "The deal record ID", true)),
                "dealId"),
            Tool("search_deals", "Search HubSpot deals (contract lifecycles) using filters.",
                Props(
                    P("query", "string", "Text query to search deals", false),
                    P("filterGroups", "array", "Filter groups array [{propertyName, operator, value}]", false),
                    P("properties", "array", "Array of property names to return", false),
                    P("sorts", "array", "Sort objects [{propertyName, direction}]", false),
                    P("limit", "integer", "Max results (1-100, default 10)", false),
                    P("after", "integer", "Pagination offset", false)
                )),

            // ── Custom Objects ────────────────────────────────────────
            Tool("list_custom_object_schemas", "List all custom object schemas defined in HubSpot. Use this to discover available custom object types and their properties.",
                Props()),
            Tool("get_custom_object_schema", "Get the full schema definition for a specific custom object type, including all properties and associations.",
                Props(P("objectType", "string", "The custom object type name (e.g., p_my_object) or objectTypeId", true)),
                "objectType"),
            Tool("list_custom_objects", "List records of a HubSpot custom object type.",
                Props(
                    P("objectType", "string", "The custom object type name (e.g., p_my_object) or objectTypeId", true),
                    P("properties", "string", "Comma-separated property names to return", false),
                    P("limit", "integer", "Max results per page (1-100, default 10)", false),
                    P("after", "string", "Pagination cursor", false)
                ), "objectType"),
            Tool("get_custom_object", "Get a single HubSpot custom object record by ID.",
                Props(
                    P("objectType", "string", "The custom object type name or objectTypeId", true),
                    P("recordId", "string", "The record ID", true),
                    P("properties", "string", "Comma-separated property names to return", false)
                ), "objectType", "recordId"),
            Tool("create_custom_object", "Create a new HubSpot custom object record.",
                Props(
                    P("objectType", "string", "The custom object type name or objectTypeId", true),
                    P("properties", "object", "Record properties as key-value pairs", true),
                    P("associations", "array", "Array of association objects [{to:{id}, types:[{associationCategory, associationTypeId}]}]", false)
                ), "objectType", "properties"),
            Tool("update_custom_object", "Update an existing HubSpot custom object record.",
                Props(
                    P("objectType", "string", "The custom object type name or objectTypeId", true),
                    P("recordId", "string", "The record ID to update", true),
                    P("properties", "object", "Properties to update", true)
                ), "objectType", "recordId", "properties"),
            Tool("delete_custom_object", "Delete a HubSpot custom object record by ID.",
                Props(
                    P("objectType", "string", "The custom object type name or objectTypeId", true),
                    P("recordId", "string", "The record ID to delete", true)
                ), "objectType", "recordId"),
            Tool("search_custom_objects", "Search HubSpot custom object records using filters.",
                Props(
                    P("objectType", "string", "The custom object type name or objectTypeId", true),
                    P("query", "string", "Text query to search records", false),
                    P("filterGroups", "array", "Filter groups array [{propertyName, operator, value}]", false),
                    P("properties", "array", "Array of property names to return", false),
                    P("sorts", "array", "Sort objects [{propertyName, direction}]", false),
                    P("limit", "integer", "Max results (1-100, default 10)", false),
                    P("after", "integer", "Pagination offset", false)
                ), "objectType")
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    // ── MCP: tools/call ──────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("ToolCallStarted", new
        {
            CorrelationId = correlationId,
            Tool = toolName
        });

        try
        {
            JToken result;
            switch (toolName)
            {
                // Companies
                case "list_companies":
                    result = await ListObjects("companies", arguments);
                    break;
                case "get_company":
                    result = await GetObject("companies", Require(arguments, "companyId"), arguments);
                    break;
                case "create_company":
                    result = await CreateObject("companies", arguments);
                    break;
                case "update_company":
                    result = await UpdateObject("companies", Require(arguments, "companyId"), arguments);
                    break;
                case "delete_company":
                    result = await DeleteObject("companies", Require(arguments, "companyId"));
                    break;
                case "search_companies":
                    result = await SearchObjects("companies", arguments);
                    break;

                // Owners (Sales Reps)
                case "list_owners":
                    result = await ListOwners(arguments);
                    break;
                case "get_owner":
                    result = await GetOwner(Require(arguments, "ownerId"));
                    break;

                // Emails
                case "list_emails":
                    result = await ListObjects("emails", arguments);
                    break;
                case "get_email":
                    result = await GetObject("emails", Require(arguments, "emailId"), arguments);
                    break;
                case "create_email":
                    result = await CreateObject("emails", arguments);
                    break;
                case "update_email":
                    result = await UpdateObject("emails", Require(arguments, "emailId"), arguments);
                    break;
                case "delete_email":
                    result = await DeleteObject("emails", Require(arguments, "emailId"));
                    break;
                case "search_emails":
                    result = await SearchObjects("emails", arguments);
                    break;

                // Sequences
                case "list_sequences":
                    result = await ListSequences(arguments);
                    break;
                case "get_sequence":
                    result = await GetSequence(Require(arguments, "sequenceId"));
                    break;
                case "enroll_in_sequence":
                    result = await EnrollInSequence(arguments);
                    break;

                // Tasks (Reminders)
                case "list_tasks":
                    result = await ListObjects("tasks", arguments);
                    break;
                case "get_task":
                    result = await GetObject("tasks", Require(arguments, "taskId"), arguments);
                    break;
                case "create_task":
                    result = await CreateObject("tasks", arguments);
                    break;
                case "update_task":
                    result = await UpdateObject("tasks", Require(arguments, "taskId"), arguments);
                    break;
                case "delete_task":
                    result = await DeleteObject("tasks", Require(arguments, "taskId"));
                    break;
                case "search_tasks":
                    result = await SearchObjects("tasks", arguments);
                    break;

                // Deals (Contract Lifecycles)
                case "list_deals":
                    result = await ListObjects("deals", arguments);
                    break;
                case "get_deal":
                    result = await GetObject("deals", Require(arguments, "dealId"), arguments);
                    break;
                case "create_deal":
                    result = await CreateObject("deals", arguments);
                    break;
                case "update_deal":
                    result = await UpdateObject("deals", Require(arguments, "dealId"), arguments);
                    break;
                case "delete_deal":
                    result = await DeleteObject("deals", Require(arguments, "dealId"));
                    break;
                case "search_deals":
                    result = await SearchObjects("deals", arguments);
                    break;

                // Custom Objects
                case "list_custom_object_schemas":
                    result = await ListCustomObjectSchemas();
                    break;
                case "get_custom_object_schema":
                    result = await GetCustomObjectSchema(Require(arguments, "objectType"));
                    break;
                case "list_custom_objects":
                    result = await ListObjects(Require(arguments, "objectType"), arguments);
                    break;
                case "get_custom_object":
                    result = await GetObject(Require(arguments, "objectType"), Require(arguments, "recordId"), arguments);
                    break;
                case "create_custom_object":
                    result = await CreateObject(Require(arguments, "objectType"), arguments);
                    break;
                case "update_custom_object":
                    result = await UpdateObject(Require(arguments, "objectType"), Require(arguments, "recordId"), arguments);
                    break;
                case "delete_custom_object":
                    result = await DeleteObject(Require(arguments, "objectType"), Require(arguments, "recordId"));
                    break;
                case "search_custom_objects":
                    result = await SearchObjects(Require(arguments, "objectType"), arguments);
                    break;

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Unknown tool", toolName);
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result is JObject || result is JArray
                            ? result.ToString(Newtonsoft.Json.Formatting.Indented)
                            : result?.ToString() ?? ""
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("ToolCallFailed", new
            {
                CorrelationId = correlationId,
                Tool = toolName,
                Error = ex.Message
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ── CRM Object Operations ────────────────────────────────────────────

    private async Task<JToken> ListObjects(string objectType, JObject args)
    {
        var queryParams = new List<string>();
        var properties = args["properties"]?.ToString();
        if (!string.IsNullOrWhiteSpace(properties))
            queryParams.Add("properties=" + Uri.EscapeDataString(properties));
        var limit = args["limit"]?.Value<int?>() ?? 10;
        queryParams.Add("limit=" + Math.Min(Math.Max(limit, 1), 100));
        var after = args["after"]?.ToString();
        if (!string.IsNullOrWhiteSpace(after))
            queryParams.Add("after=" + Uri.EscapeDataString(after));

        var url = $"{HUBSPOT_BASE}/crm/v3/objects/{objectType}?{string.Join("&", queryParams)}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> GetObject(string objectType, string recordId, JObject args)
    {
        var queryParams = new List<string>();
        var properties = args["properties"]?.ToString();
        if (!string.IsNullOrWhiteSpace(properties))
            queryParams.Add("properties=" + Uri.EscapeDataString(properties));

        var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"{HUBSPOT_BASE}/crm/v3/objects/{objectType}/{Uri.EscapeDataString(recordId)}{qs}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> CreateObject(string objectType, JObject args)
    {
        var body = new JObject();
        if (args["properties"] != null)
            body["properties"] = args["properties"];
        if (args["associations"] != null)
            body["associations"] = args["associations"];

        var url = $"{HUBSPOT_BASE}/crm/v3/objects/{objectType}";
        return await SendHubSpotRequest(HttpMethod.Post, url, body);
    }

    private async Task<JToken> UpdateObject(string objectType, string recordId, JObject args)
    {
        var body = new JObject();
        if (args["properties"] != null)
            body["properties"] = args["properties"];

        var url = $"{HUBSPOT_BASE}/crm/v3/objects/{objectType}/{Uri.EscapeDataString(recordId)}";
        return await SendHubSpotRequest(new HttpMethod("PATCH"), url, body);
    }

    private async Task<JToken> DeleteObject(string objectType, string recordId)
    {
        var url = $"{HUBSPOT_BASE}/crm/v3/objects/{objectType}/{Uri.EscapeDataString(recordId)}";
        return await SendHubSpotRequest(HttpMethod.Delete, url);
    }

    private async Task<JToken> SearchObjects(string objectType, JObject args)
    {
        var body = new JObject();
        if (args["query"] != null) body["query"] = args["query"];
        if (args["filterGroups"] != null) body["filterGroups"] = args["filterGroups"];
        if (args["properties"] != null) body["properties"] = args["properties"];
        if (args["sorts"] != null) body["sorts"] = args["sorts"];
        body["limit"] = args["limit"]?.Value<int?>() ?? 10;
        if (args["after"] != null) body["after"] = args["after"];

        var url = $"{HUBSPOT_BASE}/crm/v3/objects/{objectType}/search";
        return await SendHubSpotRequest(HttpMethod.Post, url, body);
    }

    // ── Owners (Sales Reps) ──────────────────────────────────────────────

    private async Task<JToken> ListOwners(JObject args)
    {
        var queryParams = new List<string>();
        var email = args["email"]?.ToString();
        if (!string.IsNullOrWhiteSpace(email))
            queryParams.Add("email=" + Uri.EscapeDataString(email));
        var limit = args["limit"]?.Value<int?>() ?? 100;
        queryParams.Add("limit=" + Math.Min(Math.Max(limit, 1), 500));
        var after = args["after"]?.ToString();
        if (!string.IsNullOrWhiteSpace(after))
            queryParams.Add("after=" + Uri.EscapeDataString(after));

        var url = $"{HUBSPOT_BASE}/crm/v3/owners?{string.Join("&", queryParams)}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> GetOwner(string ownerId)
    {
        var url = $"{HUBSPOT_BASE}/crm/v3/owners/{Uri.EscapeDataString(ownerId)}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    // ── Custom Object Schemas ─────────────────────────────────────────────

    private async Task<JToken> ListCustomObjectSchemas()
    {
        var url = $"{HUBSPOT_BASE}/crm/v3/schemas";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> GetCustomObjectSchema(string objectType)
    {
        var url = $"{HUBSPOT_BASE}/crm/v3/schemas/{Uri.EscapeDataString(objectType)}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    // ── Sequences ────────────────────────────────────────────────────────

    private async Task<JToken> ListSequences(JObject args)
    {
        var queryParams = new List<string>();
        var limit = args["limit"]?.Value<int?>() ?? 10;
        queryParams.Add("limit=" + Math.Min(Math.Max(limit, 1), 100));
        var after = args["after"]?.ToString();
        if (!string.IsNullOrWhiteSpace(after))
            queryParams.Add("after=" + Uri.EscapeDataString(after));

        var url = $"{HUBSPOT_BASE}/automation/v3/sequences?{string.Join("&", queryParams)}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> GetSequence(string sequenceId)
    {
        var url = $"{HUBSPOT_BASE}/automation/v3/sequences/{Uri.EscapeDataString(sequenceId)}";
        return await SendHubSpotRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> EnrollInSequence(JObject args)
    {
        var sequenceId = Require(args, "sequenceId");
        var contactId = Require(args, "contactId");
        var senderEmail = Require(args, "senderEmail");
        var startingStepOrder = args["startingStepOrder"]?.Value<int?>() ?? 0;

        var body = new JObject
        {
            ["sequenceId"] = sequenceId,
            ["contactId"] = contactId,
            ["senderEmail"] = senderEmail,
            ["startingStepOrder"] = startingStepOrder
        };

        var url = $"{HUBSPOT_BASE}/automation/v4/sequences/enrollments";
        return await SendHubSpotRequest(HttpMethod.Post, url, body);
    }

    // ── HTTP Helper ──────────────────────────────────────────────────────

    private async Task<JToken> SendHubSpotRequest(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (body != null)
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (method == HttpMethod.Delete && response.IsSuccessStatusCode)
        {
            return new JObject { ["status"] = "deleted", ["statusCode"] = (int)response.StatusCode };
        }

        if (!response.IsSuccessStatusCode)
        {
            JToken errorBody;
            try { errorBody = JObject.Parse(content); }
            catch { errorBody = content; }

            return new JObject
            {
                ["error"] = true,
                ["statusCode"] = (int)response.StatusCode,
                ["message"] = response.ReasonPhrase,
                ["details"] = errorBody
            };
        }

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["status"] = "success", ["statusCode"] = (int)response.StatusCode };

        try { return JToken.Parse(content); }
        catch { return new JObject { ["raw"] = content }; }
    }

    // ── Tool Definition Helpers ──────────────────────────────────────────

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required != null && required.Length > 0)
            schema["required"] = new JArray(required);

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema
        };
    }

    private static JObject Props(params JProperty[] props)
    {
        var obj = new JObject();
        foreach (var p in props) obj.Add(p);
        return obj;
    }

    private static JProperty P(string name, string type, string description, bool required)
    {
        var prop = new JObject
        {
            ["type"] = type,
            ["description"] = description
        };
        return new JProperty(name, prop);
    }

    private static string Require(JObject args, string name)
    {
        var val = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val))
            throw new ArgumentException($"Required parameter '{name}' is missing.");
        return val;
    }

    // ── JSON-RPC Helpers ─────────────────────────────────────────────────

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ── Application Insights ─────────────────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        return key == "IngestionEndpoint" ? "https://dc.services.visualstudio.com/" : null;
    }
}
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
    private const string PROTOCOL_VERSION = "2025-03-26";
    private const string SERVER_NAME = "netsuite-mcp";
    private const string SERVER_VERSION = "1.0.0";

    // Developer: replace with your NetSuite account ID (e.g., 1234567 or TSTDRV1234567_SB1)
    private const string NETSUITE_BASE_URL = "https://[[REPLACE_WITH_ACCOUNT_ID]].suitetalk.api.netsuite.com/services/rest";
    private const string NETSUITE_RESTLET_URL = "https://[[REPLACE_WITH_ACCOUNT_ID]].restlets.api.netsuite.com/app/site/hosting/restlet.nl";

    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        // MCP JSON-RPC routing
        if (operationId == "InvokeMCP")
        {
            return await HandleMcpRequest();
        }

        // Direct operation routing
        return await HandleDirectOperation(operationId);
    }

    // ── Direct Operation Routing ─────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleDirectOperation(string operationId)
    {
        try
        {
            switch (operationId)
            {
                case "RunSuiteQL":
                    return await ForwardSuiteQL();
                case "ListRecords":
                    return await ForwardListRecords();
                case "GetRecord":
                    return await ForwardGetRecord();
                case "CreateRecord":
                    return await ForwardCreateRecord();
                case "UpdateRecord":
                    return await ForwardUpdateRecord();
                case "DeleteRecord":
                    return await ForwardDeleteRecord();
                case "ListRecordTypes":
                    return await ForwardListRecordTypes();
                case "GetRecordMetadata":
                    return await ForwardGetRecordMetadata();
                case "GetSublist":
                    return await ForwardGetSublist();
                case "AddSublistLine":
                    return await ForwardAddSublistLine();
                case "UpdateSublistLine":
                    return await ForwardUpdateSublistLine();
                case "DeleteSublistLine":
                    return await ForwardDeleteSublistLine();
                case "CallRESTletGet":
                    return await ForwardCallRESTletGet();
                case "CallRESTletPost":
                    return await ForwardCallRESTletPost();
                case "ListRESTletScripts":
                    return await ForwardListRESTletScripts();
                case "ListRESTletDeployments":
                    return await ForwardListRESTletDeployments();
                default:
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent($"Unknown operation: {operationId}", Encoding.UTF8, "application/json")
                    };
            }
        }
        catch (Exception ex)
        {
            var error = new JObject
            {
                ["error"] = true,
                ["message"] = ex.Message
            };
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(error.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
        }
    }

    // ── Direct Forwarding Methods ────────────────────────────────────────

    private async Task<HttpResponseMessage> ForwardSuiteQL()
    {
        var body = await ReadRequestBody();
        var query = body?["q"]?.ToString();
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Required parameter 'q' is missing from request body.");

        var limit = GetQueryParam("limit") ?? "100";
        var offset = GetQueryParam("offset") ?? "0";

        var url = $"/query/v1/suiteql?limit={Uri.EscapeDataString(limit)}&offset={Uri.EscapeDataString(offset)}";
        return await ForwardToNetSuite(HttpMethod.Post, url, body, preferTransient: true);
    }

    private async Task<HttpResponseMessage> ForwardListRecords()
    {
        var recordType = GetPathSegment("recordType");
        var queryParams = new List<string>();

        var q = GetQueryParam("q");
        if (!string.IsNullOrWhiteSpace(q))
            queryParams.Add("q=" + Uri.EscapeDataString(q));
        var fields = GetQueryParam("fields");
        if (!string.IsNullOrWhiteSpace(fields))
            queryParams.Add("fields=" + Uri.EscapeDataString(fields));
        var limit = GetQueryParam("limit") ?? "100";
        queryParams.Add("limit=" + Uri.EscapeDataString(limit));
        var offset = GetQueryParam("offset");
        if (!string.IsNullOrWhiteSpace(offset) && offset != "0")
            queryParams.Add("offset=" + Uri.EscapeDataString(offset));
        var expand = GetQueryParam("expandSubResources");
        if (!string.IsNullOrWhiteSpace(expand) && expand.Equals("true", StringComparison.OrdinalIgnoreCase))
            queryParams.Add("expandSubResources=true");

        var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}{qs}";
        return await ForwardToNetSuite(HttpMethod.Get, url);
    }

    private async Task<HttpResponseMessage> ForwardGetRecord()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var queryParams = new List<string>();

        var fields = GetQueryParam("fields");
        if (!string.IsNullOrWhiteSpace(fields))
            queryParams.Add("fields=" + Uri.EscapeDataString(fields));
        var expand = GetQueryParam("expandSubResources");
        if (!string.IsNullOrWhiteSpace(expand) && expand.Equals("true", StringComparison.OrdinalIgnoreCase))
            queryParams.Add("expandSubResources=true");

        var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}{qs}";
        return await ForwardToNetSuite(HttpMethod.Get, url);
    }

    private async Task<HttpResponseMessage> ForwardCreateRecord()
    {
        var recordType = GetPathSegment("recordType");
        var body = await ReadRequestBody();
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}";
        return await ForwardToNetSuite(HttpMethod.Post, url, body);
    }

    private async Task<HttpResponseMessage> ForwardUpdateRecord()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var body = await ReadRequestBody();
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}";
        return await ForwardToNetSuite(new HttpMethod("PATCH"), url, body);
    }

    private async Task<HttpResponseMessage> ForwardDeleteRecord()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}";
        return await ForwardToNetSuite(HttpMethod.Delete, url);
    }

    private async Task<HttpResponseMessage> ForwardListRecordTypes()
    {
        return await ForwardToNetSuite(HttpMethod.Get, "/record/v1/metadata-catalog",
            headers: new Dictionary<string, string> { { "Accept", "application/schema+json" } });
    }

    private async Task<HttpResponseMessage> ForwardGetRecordMetadata()
    {
        var recordType = GetPathSegment("recordType");
        var url = $"/record/v1/metadata-catalog/{Uri.EscapeDataString(recordType)}";
        return await ForwardToNetSuite(HttpMethod.Get, url,
            headers: new Dictionary<string, string> { { "Accept", "application/schema+json" } });
    }

    private async Task<HttpResponseMessage> ForwardGetSublist()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var sublistId = GetPathSegment("sublistId");
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}";
        return await ForwardToNetSuite(HttpMethod.Get, url);
    }

    private async Task<HttpResponseMessage> ForwardAddSublistLine()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var sublistId = GetPathSegment("sublistId");
        var body = await ReadRequestBody();
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}";
        return await ForwardToNetSuite(HttpMethod.Post, url, body);
    }

    private async Task<HttpResponseMessage> ForwardUpdateSublistLine()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var sublistId = GetPathSegment("sublistId");
        var lineId = GetPathSegment("lineId");
        var body = await ReadRequestBody();
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}/{Uri.EscapeDataString(lineId)}";
        return await ForwardToNetSuite(new HttpMethod("PATCH"), url, body);
    }

    private async Task<HttpResponseMessage> ForwardDeleteSublistLine()
    {
        var recordType = GetPathSegment("recordType");
        var recordId = GetPathSegment("recordId");
        var sublistId = GetPathSegment("sublistId");
        var lineId = GetPathSegment("lineId");
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}/{Uri.EscapeDataString(lineId)}";
        return await ForwardToNetSuite(HttpMethod.Delete, url);
    }

    // ── RESTlet Forwarding ───────────────────────────────────────────────

    private async Task<HttpResponseMessage> ForwardCallRESTletGet()
    {
        var scriptId = GetQueryParam("scriptId");
        var deployId = GetQueryParam("deployId");
        var additionalParams = GetQueryParam("params");

        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Required parameter 'scriptId' is missing.");
        if (string.IsNullOrWhiteSpace(deployId))
            throw new ArgumentException("Required parameter 'deployId' is missing.");

        return await ForwardToRestlet(HttpMethod.Get, scriptId, deployId, additionalParams: additionalParams);
    }

    private async Task<HttpResponseMessage> ForwardCallRESTletPost()
    {
        var scriptId = GetQueryParam("scriptId");
        var deployId = GetQueryParam("deployId");
        var body = await ReadRequestBody();

        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Required parameter 'scriptId' is missing.");
        if (string.IsNullOrWhiteSpace(deployId))
            throw new ArgumentException("Required parameter 'deployId' is missing.");

        return await ForwardToRestlet(HttpMethod.Post, scriptId, deployId, body: body);
    }

    private async Task<HttpResponseMessage> ForwardToRestlet(
        HttpMethod method,
        string scriptId,
        string deployId,
        JObject body = null,
        string additionalParams = null)
    {
        var url = $"{NETSUITE_RESTLET_URL}?script={Uri.EscapeDataString(scriptId)}&deploy={Uri.EscapeDataString(deployId)}";
        if (!string.IsNullOrWhiteSpace(additionalParams))
            url += "&" + additionalParams;

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

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ForwardListRESTletScripts()
    {
        var suiteqlBody = new JObject { ["q"] = "SELECT s.id AS scriptid, s.name FROM script s WHERE s.scripttype = 'RESTLET' ORDER BY s.name" };
        var url = "/query/v1/suiteql?limit=1000&offset=0";
        return await ForwardToNetSuite(HttpMethod.Post, url, suiteqlBody, preferTransient: true);
    }

    private async Task<HttpResponseMessage> ForwardListRESTletDeployments()
    {
        var scriptId = GetQueryParam("scriptId");
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Required parameter 'scriptId' is missing.");

        var suiteqlBody = new JObject { ["q"] = $"SELECT sd.primarykey AS deployid, sd.title FROM scriptdeployment sd WHERE sd.script = {Uri.EscapeDataString(scriptId)} AND sd.status = 'Released' ORDER BY sd.title" };
        var url = "/query/v1/suiteql?limit=1000&offset=0";
        return await ForwardToNetSuite(HttpMethod.Post, url, suiteqlBody, preferTransient: true);
    }

    // ── Direct Forwarding HTTP Helper ────────────────────────────────────

    private async Task<HttpResponseMessage> ForwardToNetSuite(
        HttpMethod method,
        string relativePath,
        JObject body = null,
        bool preferTransient = false,
        Dictionary<string, string> headers = null)
    {
        var url = NETSUITE_BASE_URL + relativePath;

        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (preferTransient)
            request.Headers.Add("Prefer", "transient");

        if (headers != null)
        {
            foreach (var kv in headers)
            {
                if (kv.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(kv.Value));
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

        if (body != null)
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JObject> ReadRequestBody()
    {
        if (this.Context.Request.Content == null) return null;
        var raw = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return JObject.Parse(raw);
    }

    private string GetPathSegment(string name)
    {
        var segments = this.Context.Request.RequestUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        // Path patterns:
        //   /record/{recordType}                        -> segments: [record, {recordType}]
        //   /record/{recordType}/{recordId}              -> segments: [record, {recordType}, {recordId}]
        //   /record/{recordType}/{recordId}/{sublistId}  -> segments: [record, {recordType}, {recordId}, {sublistId}]
        //   /record/{recordType}/{recordId}/{sublistId}/{lineId} -> segments: [record, {recordType}, {recordId}, {sublistId}, {lineId}]
        //   /metadata/{recordType}                       -> segments: [metadata, {recordType}]
        //   /suiteql                                     -> segments: [suiteql]

        switch (name)
        {
            case "recordType":
                return segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : null;
            case "recordId":
                return segments.Length > 2 ? Uri.UnescapeDataString(segments[2]) : null;
            case "sublistId":
                return segments.Length > 3 ? Uri.UnescapeDataString(segments[3]) : null;
            case "lineId":
                return segments.Length > 4 ? Uri.UnescapeDataString(segments[4]) : null;
            default:
                return null;
        }
    }

    private string GetQueryParam(string name)
    {
        var query = this.Context.Request.RequestUri.Query;
        if (string.IsNullOrWhiteSpace(query)) return null;

        var pairs = query.TrimStart('?').Split('&');
        foreach (var pair in pairs)
        {
            var kv = pair.Split(new[] { '=' }, 2);
            if (kv.Length == 2 && kv[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    // ── MCP JSON-RPC Handler ─────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpRequest()
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
            // ── SuiteQL ──────────────────────────────────────────────
            Tool("run_suiteql", "Execute a SuiteQL query against NetSuite. SuiteQL is SQL-like and supports SELECT, JOIN, GROUP BY, ORDER BY, aggregate functions, and subqueries. Returns up to 100,000 results with pagination. Common tables: transaction, customer, vendor, employee, item, contact, salesorder, purchaseorder, invoice, account, department, location, subsidiary.",
                Props(
                    P("query", "string", "SuiteQL query string (e.g., SELECT id, companyname, email FROM customer WHERE isinactive = 'F' ORDER BY companyname)", true),
                    P("limit", "integer", "Max results per page (1-1000, default 100)", false),
                    P("offset", "integer", "Number of results to skip for pagination (default 0)", false)
                ), "query"),

            // ── Record CRUD ──────────────────────────────────────────
            Tool("list_records", "List all instances of a NetSuite record type with optional collection filtering. Filter syntax: field OPERATOR value (e.g., email START_WITH barbara). Operators vary by type: String (CONTAIN, IS, START_WITH, END_WITH), Number (EQUAL, GREATER, LESS, BETWEEN, ANY_OF), Boolean (IS), Date (AFTER, BEFORE, ON, ON_OR_AFTER, ON_OR_BEFORE). Join conditions with AND/OR.",
                Props(
                    P("recordType", "string", "The NetSuite record type (e.g., customer, vendor, salesorder, purchaseorder, invoice, employee, item, contact, inventoryitem, journalentry, creditmemo, account, department, location, subsidiary, opportunity)", true),
                    P("filter", "string", "Collection filter query (e.g., companyname START_WITH \"Acme\" AND isinactive IS false). Use quotation marks around values with spaces.", false),
                    P("fields", "string", "Comma-separated field names to return (e.g., id,companyname,email)", false),
                    P("limit", "integer", "Max results per page (1-1000, default 100)", false),
                    P("offset", "integer", "Number of results to skip for pagination (default 0)", false),
                    P("expandSubResources", "boolean", "If true, expand sub-resources (sublists) inline", false)
                ), "recordType"),

            Tool("get_record", "Get a single NetSuite record by its type and internal ID. Returns all body fields and sublist links. Use expandSubResources to include sublists inline.",
                Props(
                    P("recordType", "string", "The NetSuite record type (e.g., customer, salesorder, invoice)", true),
                    P("recordId", "string", "The internal ID of the record", true),
                    P("fields", "string", "Comma-separated field names to return", false),
                    P("expandSubResources", "boolean", "If true, expand sub-resources (sublists) inline", false)
                ), "recordType", "recordId"),

            Tool("create_record", "Create a new NetSuite record. Provide the record type and a JSON body with field values. Use get_record_metadata to discover available fields and required values.",
                Props(
                    P("recordType", "string", "The NetSuite record type to create (e.g., customer, salesorder)", true),
                    P("body", "object", "Record field values as key-value pairs (e.g., {companyname: \"Acme Corp\", subsidiary: {id: \"1\"}, email: \"info@acme.com\"})", true)
                ), "recordType", "body"),

            Tool("update_record", "Update an existing NetSuite record by type and ID. Only include fields you want to change. Use PATCH semantics — omitted fields are not modified.",
                Props(
                    P("recordType", "string", "The NetSuite record type (e.g., customer, salesorder)", true),
                    P("recordId", "string", "The internal ID of the record to update", true),
                    P("body", "object", "Fields to update as key-value pairs", true)
                ), "recordType", "recordId", "body"),

            Tool("delete_record", "Delete a NetSuite record by type and internal ID. This action is permanent and cannot be undone.",
                Props(
                    P("recordType", "string", "The NetSuite record type (e.g., customer, invoice)", true),
                    P("recordId", "string", "The internal ID of the record to delete", true)
                ), "recordType", "recordId"),

            // ── Metadata ─────────────────────────────────────────────
            Tool("get_record_metadata", "Get the metadata (schema) for a NetSuite record type. Returns available fields, their types, whether they are required, filterable, and available sublists. Use this to discover field names before creating or updating records.",
                Props(
                    P("recordType", "string", "The NetSuite record type (e.g., customer, salesorder, invoice)", true)
                ), "recordType"),

            Tool("list_record_types", "List all available NetSuite record types accessible via REST Web Services. Returns record type names and links to their metadata.",
                Props()),

            // ── Sublist Operations ────────────────────────────────────
            Tool("get_sublist", "Get a sublist (line items) for a NetSuite record. For example, get all items on a sales order or address book entries on a customer.",
                Props(
                    P("recordType", "string", "The parent record type (e.g., salesorder, purchaseorder)", true),
                    P("recordId", "string", "The internal ID of the parent record", true),
                    P("sublistId", "string", "The sublist ID (e.g., item, addressbook, partners, salesteam)", true)
                ), "recordType", "recordId", "sublistId"),

            Tool("upsert_sublist_line", "Add or update a line on a record's sublist. For new lines omit lineId. For existing lines provide lineId to update in place.",
                Props(
                    P("recordType", "string", "The parent record type (e.g., salesorder)", true),
                    P("recordId", "string", "The internal ID of the parent record", true),
                    P("sublistId", "string", "The sublist ID (e.g., item)", true),
                    P("lineId", "string", "The line internal ID to update (omit to create a new line)", false),
                    P("body", "object", "Line field values (e.g., {item: {id: \"42\"}, quantity: 5, rate: 100})", true)
                ), "recordType", "recordId", "sublistId", "body"),

            Tool("delete_sublist_line", "Remove a line from a record's sublist.",
                Props(
                    P("recordType", "string", "The parent record type", true),
                    P("recordId", "string", "The internal ID of the parent record", true),
                    P("sublistId", "string", "The sublist ID (e.g., item)", true),
                    P("lineId", "string", "The line internal ID to delete", true)
                ), "recordType", "recordId", "sublistId", "lineId"),

            // ── RESTlet ───────────────────────────────────────────────
            Tool("call_restlet", "Call a deployed NetSuite RESTlet. RESTlets are custom SuiteScript endpoints. Common use case: calling a RESTlet that uses File.getContents() to retrieve file content by internal ID. The response format depends on the RESTlet script implementation.",
                Props(
                    P("scriptId", "string", "The script ID of the deployed RESTlet (numeric ID or customscript ID)", true),
                    P("deployId", "string", "The deployment ID of the RESTlet (numeric ID or customdeploy ID, typically 1)", true),
                    P("method", "string", "HTTP method to use: GET or POST (default GET)", false),
                    P("body", "object", "JSON body to send with POST requests (e.g., {\"fileId\": \"123\"})", false),
                    P("params", "string", "Additional query parameters for GET requests as key=value pairs separated by & (e.g., fileId=123&format=json)", false)
                ), "scriptId", "deployId")
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
                // SuiteQL
                case "run_suiteql":
                    result = await RunSuiteQL(arguments);
                    break;

                // Record CRUD
                case "list_records":
                    result = await ListRecords(arguments);
                    break;
                case "get_record":
                    result = await GetRecord(arguments);
                    break;
                case "create_record":
                    result = await CreateRecord(arguments);
                    break;
                case "update_record":
                    result = await UpdateRecord(arguments);
                    break;
                case "delete_record":
                    result = await DeleteRecord(arguments);
                    break;

                // Metadata
                case "get_record_metadata":
                    result = await GetRecordMetadata(arguments);
                    break;
                case "list_record_types":
                    result = await ListRecordTypes();
                    break;

                // Sublists
                case "get_sublist":
                    result = await GetSublist(arguments);
                    break;
                case "upsert_sublist_line":
                    result = await UpsertSublistLine(arguments);
                    break;
                case "delete_sublist_line":
                    result = await DeleteSublistLine(arguments);
                    break;

                // RESTlet
                case "call_restlet":
                    result = await CallRESTlet(arguments);
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

    // ── SuiteQL ──────────────────────────────────────────────────────────

    private async Task<JToken> RunSuiteQL(JObject args)
    {
        var query = Require(args, "query");
        var limit = args["limit"]?.Value<int?>() ?? 100;
        var offset = args["offset"]?.Value<int?>() ?? 0;

        limit = Math.Min(Math.Max(limit, 1), 1000);

        var url = $"/query/v1/suiteql?limit={limit}&offset={offset}";
        var body = new JObject { ["q"] = query };

        return await SendNetSuiteRequest(HttpMethod.Post, url, body, preferTransient: true);
    }

    // ── Record CRUD ──────────────────────────────────────────────────────

    private async Task<JToken> ListRecords(JObject args)
    {
        var recordType = Require(args, "recordType");
        var queryParams = new List<string>();

        var filter = args["filter"]?.ToString();
        if (!string.IsNullOrWhiteSpace(filter))
            queryParams.Add("q=" + Uri.EscapeDataString(filter));

        var fields = args["fields"]?.ToString();
        if (!string.IsNullOrWhiteSpace(fields))
            queryParams.Add("fields=" + Uri.EscapeDataString(fields));

        var limit = args["limit"]?.Value<int?>() ?? 100;
        queryParams.Add("limit=" + Math.Min(Math.Max(limit, 1), 1000));

        var offset = args["offset"]?.Value<int?>() ?? 0;
        if (offset > 0)
            queryParams.Add("offset=" + offset);

        var expand = args["expandSubResources"]?.Value<bool?>() ?? false;
        if (expand)
            queryParams.Add("expandSubResources=true");

        var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}{qs}";

        return await SendNetSuiteRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> GetRecord(JObject args)
    {
        var recordType = Require(args, "recordType");
        var recordId = Require(args, "recordId");
        var queryParams = new List<string>();

        var fields = args["fields"]?.ToString();
        if (!string.IsNullOrWhiteSpace(fields))
            queryParams.Add("fields=" + Uri.EscapeDataString(fields));

        var expand = args["expandSubResources"]?.Value<bool?>() ?? false;
        if (expand)
            queryParams.Add("expandSubResources=true");

        var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}{qs}";

        return await SendNetSuiteRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> CreateRecord(JObject args)
    {
        var recordType = Require(args, "recordType");
        var body = args["body"] as JObject;
        if (body == null)
            throw new ArgumentException("Required parameter 'body' is missing.");

        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}";
        return await SendNetSuiteRequest(HttpMethod.Post, url, body);
    }

    private async Task<JToken> UpdateRecord(JObject args)
    {
        var recordType = Require(args, "recordType");
        var recordId = Require(args, "recordId");
        var body = args["body"] as JObject;
        if (body == null)
            throw new ArgumentException("Required parameter 'body' is missing.");

        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}";
        return await SendNetSuiteRequest(new HttpMethod("PATCH"), url, body);
    }

    private async Task<JToken> DeleteRecord(JObject args)
    {
        var recordType = Require(args, "recordType");
        var recordId = Require(args, "recordId");

        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}";
        return await SendNetSuiteRequest(HttpMethod.Delete, url);
    }

    // ── Metadata ─────────────────────────────────────────────────────────

    private async Task<JToken> GetRecordMetadata(JObject args)
    {
        var recordType = Require(args, "recordType");
        var url = $"/record/v1/metadata-catalog/{Uri.EscapeDataString(recordType)}";
        return await SendNetSuiteRequest(HttpMethod.Get, url, headers: new Dictionary<string, string>
        {
            { "Accept", "application/schema+json" }
        });
    }

    private async Task<JToken> ListRecordTypes()
    {
        var url = "/record/v1/metadata-catalog";
        return await SendNetSuiteRequest(HttpMethod.Get, url, headers: new Dictionary<string, string>
        {
            { "Accept", "application/schema+json" }
        });
    }

    // ── Sublist Operations ────────────────────────────────────────────────

    private async Task<JToken> GetSublist(JObject args)
    {
        var recordType = Require(args, "recordType");
        var recordId = Require(args, "recordId");
        var sublistId = Require(args, "sublistId");

        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}";
        return await SendNetSuiteRequest(HttpMethod.Get, url);
    }

    private async Task<JToken> UpsertSublistLine(JObject args)
    {
        var recordType = Require(args, "recordType");
        var recordId = Require(args, "recordId");
        var sublistId = Require(args, "sublistId");
        var lineId = args["lineId"]?.ToString();
        var body = args["body"] as JObject;
        if (body == null)
            throw new ArgumentException("Required parameter 'body' is missing.");

        if (!string.IsNullOrWhiteSpace(lineId))
        {
            var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}/{Uri.EscapeDataString(lineId)}";
            return await SendNetSuiteRequest(new HttpMethod("PATCH"), url, body);
        }
        else
        {
            var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}";
            return await SendNetSuiteRequest(HttpMethod.Post, url, body);
        }
    }

    private async Task<JToken> DeleteSublistLine(JObject args)
    {
        var recordType = Require(args, "recordType");
        var recordId = Require(args, "recordId");
        var sublistId = Require(args, "sublistId");
        var lineId = Require(args, "lineId");

        var url = $"/record/v1/{Uri.EscapeDataString(recordType)}/{Uri.EscapeDataString(recordId)}/{Uri.EscapeDataString(sublistId)}/{Uri.EscapeDataString(lineId)}";
        return await SendNetSuiteRequest(HttpMethod.Delete, url);
    }

    // ── RESTlet ──────────────────────────────────────────────────────────

    private async Task<JToken> CallRESTlet(JObject args)
    {
        var scriptId = Require(args, "scriptId");
        var deployId = Require(args, "deployId");
        var method = args["method"]?.ToString()?.ToUpperInvariant() ?? "GET";
        var additionalParams = args["params"]?.ToString();
        var body = args["body"] as JObject;

        var httpMethod = method == "POST" ? HttpMethod.Post : HttpMethod.Get;
        return await SendRestletRequest(httpMethod, scriptId, deployId, body, additionalParams);
    }

    private async Task<JToken> SendRestletRequest(
        HttpMethod method,
        string scriptId,
        string deployId,
        JObject body = null,
        string additionalParams = null)
    {
        var url = $"{NETSUITE_RESTLET_URL}?script={Uri.EscapeDataString(scriptId)}&deploy={Uri.EscapeDataString(deployId)}";
        if (!string.IsNullOrWhiteSpace(additionalParams))
            url += "&" + additionalParams;

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

        if (!response.IsSuccessStatusCode)
        {
            JToken errorBody;
            try { errorBody = JToken.Parse(content); }
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

    // ── HTTP Helper ──────────────────────────────────────────────────────

    private async Task<JToken> SendNetSuiteRequest(
        HttpMethod method,
        string relativePath,
        JObject body = null,
        bool preferTransient = false,
        Dictionary<string, string> headers = null)
    {
        var url = NETSUITE_BASE_URL + relativePath;

        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (preferTransient)
            request.Headers.Add("Prefer", "transient");

        if (headers != null)
        {
            foreach (var kv in headers)
            {
                if (kv.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(kv.Value));
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }

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
            try { errorBody = JToken.Parse(content); }
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

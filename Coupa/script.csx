using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights connection string
    // Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;LiveEndpoint=https://REGION.livediagnostics.monitor.azure.com/
    // Leave empty to disable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    private const string SERVER_NAME = "coupa-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-11-25";

    private static readonly HashSet<string> REST_OPERATIONS = new HashSet<string>
    {
        "ListSuppliers", "GetSupplier", "CreateSupplier", "UpdateSupplier",
        "ListUsers", "GetUser", "CreateUser", "UpdateUser",
        "ListAccounts", "GetAccount",
        "ListDepartments", "GetDepartment",
        "ListAddresses", "GetAddress",
        "ListItems", "GetItem",
        "ListCurrencies", "ListPaymentTerms", "ListExchangeRates",
        "ListLookupValues", "ListProjects", "GetProject",
        "ListPurchaseOrders", "GetPurchaseOrder", "CreatePurchaseOrder", "UpdatePurchaseOrder",
        "IssuePurchaseOrder", "CancelPurchaseOrder", "ClosePurchaseOrder",
        "ListPurchaseOrderLines",
        "ListInvoices", "GetInvoice", "CreateInvoice", "UpdateInvoice",
        "SubmitInvoice", "VoidInvoice",
        "ListRequisitions", "GetRequisition", "CreateRequisition", "UpdateRequisition",
        "ListApprovals", "GetApproval",
        "ListExpenseReports", "GetExpenseReport",
        "ListContracts", "GetContract",
        "ListReceipts"
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // Route REST operations — passthrough with Accept header
        if (REST_OPERATIONS.Contains(this.Context.OperationId))
        {
            await LogToAppInsights("RestOperation", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });
            return await HandleRestPassthroughAsync().ConfigureAwait(false);
        }

        // MCP handler (InvokeMCP)
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);

            var method = request["method"]?.ToString();
            var id = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            await LogToAppInsights("McpRequest", new { CorrelationId = correlationId, Method = method });

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id);
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateJsonRpcSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCall(@params, id).ConfigureAwait(false);
                case "resources/list":
                    return CreateJsonRpcSuccess(new JObject { ["resources"] = new JArray() }, id);
                case "prompts/list":
                    return CreateJsonRpcSuccess(new JObject { ["prompts"] = new JArray() }, id);
                case "ping":
                    return CreateJsonRpcSuccess(new JObject(), id);
                default:
                    return CreateJsonRpcError(id, -32601, "Method not found", method);
            }
        }
        catch (JsonException ex)
        {
            await LogToAppInsights("McpError", new { CorrelationId = correlationId, ErrorType = "ParseError", Error = ex.Message });
            return CreateJsonRpcError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("McpError", new { CorrelationId = correlationId, ErrorType = ex.GetType().Name, Error = ex.Message });
            return CreateJsonRpcError(null, -32603, "Internal error", ex.Message);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            await LogToAppInsights("RequestCompleted", new { CorrelationId = correlationId, DurationMs = duration.TotalMilliseconds });
        }
    }

    // ── REST Passthrough ─────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleRestPassthroughAsync()
    {
        this.Context.Request.Headers.Accept.Clear();
        this.Context.Request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (this.Context.Request.Content != null)
        {
            var contentType = this.Context.Request.Content.Headers.ContentType;
            if (contentType == null || string.IsNullOrEmpty(contentType.MediaType))
            {
                this.Context.Request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }
        }

        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);
    }

    // ── Initialize ───────────────────────────────────────────────────────

    private HttpResponseMessage HandleInitialize(JToken id)
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
                ["version"] = SERVER_VERSION,
                ["title"] = "Coupa Procurement MCP",
                ["description"] = "Purchase orders, invoices, requisitions, suppliers, contracts, and other procurement operations for the Coupa platform"
            }
        };
        return CreateJsonRpcSuccess(result, id);
    }

    // ── Tools List ───────────────────────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var tools = new JArray
        {
            Tool("list_purchase_orders",
                "Query purchase orders in Coupa. Returns PO number, status, supplier, total, and line items. Use query parameters to filter by status, supplier name, PO number, or updated date.",
                Props(
                    P("status", "string", "Filter by PO status: draft, issued, closed, cancelled, buyer_hold, supplier_hold"),
                    P("po_number", "string", "Filter by PO number"),
                    P("supplier_name", "string", "Filter by supplier name"),
                    P("updated_after", "string", "Filter by updated date (YYYY-MM-DDTHH:MM:SSZ)"),
                    P("limit", "integer", "Max records to return (default 50)"),
                    P("offset", "integer", "Starting record offset for pagination")
                )),

            Tool("get_purchase_order",
                "Get a specific purchase order by ID. Returns full PO details including all line items, supplier info, status, and totals.",
                Props(P("id", "integer", "Purchase order ID")),
                "id"),

            Tool("list_invoices",
                "Query invoices in Coupa. Returns invoice number, date, status, supplier, and totals. Use query parameters to filter.",
                Props(
                    P("status", "string", "Filter by status: draft, pending_approval, approved, voided, disputed, pending_receipt, rejected, on_hold"),
                    P("invoice_number", "string", "Filter by invoice number"),
                    P("supplier_name", "string", "Filter by supplier name"),
                    P("updated_after", "string", "Filter by updated date (YYYY-MM-DDTHH:MM:SSZ)"),
                    P("limit", "integer", "Max records to return (default 50)"),
                    P("offset", "integer", "Starting record offset")
                )),

            Tool("get_invoice",
                "Get a specific invoice by ID. Returns full invoice details including line items, supplier, payment terms, and tax information.",
                Props(P("id", "integer", "Invoice ID")),
                "id"),

            Tool("list_requisitions",
                "Query requisitions in Coupa. Returns status, requester, lines, and totals.",
                Props(
                    P("status", "string", "Filter by status: draft, pending_buyer_action, pending_approval, approved, ordered, partially_received, received, abandoned"),
                    P("updated_after", "string", "Filter by updated date"),
                    P("limit", "integer", "Max records to return"),
                    P("offset", "integer", "Starting record offset")
                )),

            Tool("get_requisition",
                "Get a specific requisition by ID with full details including all lines.",
                Props(P("id", "integer", "Requisition ID")),
                "id"),

            Tool("list_suppliers",
                "Query suppliers in Coupa. Returns name, number, status, payment method, and contact info.",
                Props(
                    P("name", "string", "Filter by supplier name"),
                    P("status", "string", "Filter by status: active, inactive, draft"),
                    P("limit", "integer", "Max records to return"),
                    P("offset", "integer", "Starting record offset")
                )),

            Tool("get_supplier",
                "Get a specific supplier by ID with full details.",
                Props(P("id", "integer", "Supplier ID")),
                "id"),

            Tool("list_approvals",
                "Query pending approvals. Returns approval status, type of object being approved, and approver info.",
                Props(
                    P("status", "string", "Filter by status: pending_approval, approved, rejected"),
                    P("approvable_type", "string", "Filter by type of object being approved"),
                    P("limit", "integer", "Max records to return"),
                    P("offset", "integer", "Starting record offset")
                )),

            Tool("list_contracts",
                "Query contracts in Coupa. Returns contract name, number, status, dates, and supplier.",
                Props(
                    P("status", "string", "Filter by contract status"),
                    P("updated_after", "string", "Filter by updated date"),
                    P("limit", "integer", "Max records to return"),
                    P("offset", "integer", "Starting record offset")
                )),

            Tool("get_contract",
                "Get a specific contract by ID with full details.",
                Props(P("id", "integer", "Contract ID")),
                "id"),

            Tool("list_users",
                "Query users in Coupa. Returns user login, email, name, and active status.",
                Props(
                    P("email", "string", "Filter by email"),
                    P("login", "string", "Filter by login name"),
                    P("active", "boolean", "Filter by active status"),
                    P("limit", "integer", "Max records to return"),
                    P("offset", "integer", "Starting record offset")
                )),

            Tool("list_expense_reports",
                "Query expense reports in Coupa.",
                Props(
                    P("status", "string", "Filter by status"),
                    P("updated_after", "string", "Filter by updated date"),
                    P("limit", "integer", "Max records to return"),
                    P("offset", "integer", "Starting record offset")
                ))
        };

        return CreateJsonRpcSuccess(new JObject { ["tools"] = tools }, id);
    }

    // ── Tools Call ───────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken id)
    {
        var toolName = @params["name"]?.ToString();
        var args = @params["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return CreateJsonRpcError(id, -32602, "Tool name required");

        try
        {
            JToken result;
            switch (toolName)
            {
                case "list_purchase_orders":
                    result = await QueryResourceAsync("/api/purchase_orders", args, "status", "po-number", "supplier[name]");
                    break;
                case "get_purchase_order":
                    result = await GetResourceAsync("/api/purchase_orders", args);
                    break;
                case "list_invoices":
                    result = await QueryResourceAsync("/api/invoices", args, "status", "invoice-number", "supplier[name]");
                    break;
                case "get_invoice":
                    result = await GetResourceAsync("/api/invoices", args);
                    break;
                case "list_requisitions":
                    result = await QueryResourceAsync("/api/requisitions", args, "status");
                    break;
                case "get_requisition":
                    result = await GetResourceAsync("/api/requisitions", args);
                    break;
                case "list_suppliers":
                    result = await QueryResourceAsync("/api/suppliers", args, "name", "status");
                    break;
                case "get_supplier":
                    result = await GetResourceAsync("/api/suppliers", args);
                    break;
                case "list_approvals":
                    result = await QueryResourceAsync("/api/approvals", args, "status", "approvable-type");
                    break;
                case "list_contracts":
                    result = await QueryResourceAsync("/api/contracts", args, "status");
                    break;
                case "get_contract":
                    result = await GetResourceAsync("/api/contracts", args);
                    break;
                case "list_users":
                    result = await QueryResourceAsync("/api/users", args, "email", "login", "active");
                    break;
                case "list_expense_reports":
                    result = await QueryResourceAsync("/api/expense_reports", args, "status");
                    break;
                default:
                    return CreateToolResult($"Unknown tool: {toolName}", true, id);
            }

            return CreateToolResult(result.ToString(Newtonsoft.Json.Formatting.Indented), false, id);
        }
        catch (Exception ex)
        {
            return CreateToolResult($"Tool execution failed: {ex.Message}", true, id);
        }
    }

    // ── Resource Helpers ─────────────────────────────────────────────────

    private async Task<JToken> QueryResourceAsync(string basePath, JObject args, params string[] filterFields)
    {
        var queryParams = new List<string>();

        var limit = args["limit"]?.ToString();
        if (!string.IsNullOrEmpty(limit))
            queryParams.Add($"limit={Uri.EscapeDataString(limit)}");

        var offset = args["offset"]?.ToString();
        if (!string.IsNullOrEmpty(offset))
            queryParams.Add($"offset={Uri.EscapeDataString(offset)}");

        var updatedAfter = args["updated_after"]?.ToString();
        if (!string.IsNullOrEmpty(updatedAfter))
            queryParams.Add($"updated-at[gt_or_eq]={Uri.EscapeDataString(updatedAfter)}");

        var argMappings = new Dictionary<string, string>
        {
            { "status", "status" },
            { "name", "name" },
            { "po_number", "po-number" },
            { "po-number", "po-number" },
            { "supplier_name", "supplier[name]" },
            { "supplier[name]", "supplier[name]" },
            { "invoice_number", "invoice-number" },
            { "invoice-number", "invoice-number" },
            { "approvable_type", "approvable-type" },
            { "approvable-type", "approvable-type" },
            { "email", "email" },
            { "login", "login" },
            { "active", "active" }
        };

        foreach (var field in filterFields)
        {
            var snakeField = field.Replace("-", "_").Replace("[", "_").Replace("]", "");
            var value = args[snakeField]?.ToString() ?? args[field]?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                var queryField = argMappings.ContainsKey(field) ? argMappings[field] : field;
                queryParams.Add($"{Uri.EscapeDataString(queryField)}={Uri.EscapeDataString(value)}");
            }
        }

        var url = basePath;
        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        return await SendGetAsync(url);
    }

    private async Task<JToken> GetResourceAsync(string basePath, JObject args)
    {
        var idValue = args["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(idValue))
            throw new ArgumentException("'id' is required");

        return await SendGetAsync($"{basePath}/{Uri.EscapeDataString(idValue)}");
    }

    // ── HTTP Helpers ─────────────────────────────────────────────────────

    private async Task<JToken> SendGetAsync(string path)
    {
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Coupa API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["status"] = "no_data", ["statusCode"] = (int)response.StatusCode };

        try { return JToken.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    // ── JSON-RPC Helpers ─────────────────────────────────────────────────

    private HttpResponseMessage CreateJsonRpcSuccess(JObject result, JToken id)
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

    private HttpResponseMessage CreateJsonRpcError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
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

    private HttpResponseMessage CreateToolResult(string text, bool isError, JToken id)
    {
        var result = new JObject
        {
            ["content"] = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = text }
            },
            ["isError"] = isError
        };
        return CreateJsonRpcSuccess(result, id);
    }

    // ── Tool Definition Helpers ──────────────────────────────────────────

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = properties };
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

    private static JProperty P(string name, string type, string description)
    {
        var prop = new JObject { ["type"] = type, ["description"] = description };
        return new JProperty(name, prop);
    }

    // ── Application Insights ─────────────────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey=");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint=")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey)) return;

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetry = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = instrumentationKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(propsDict)
                    }
                }
            };

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string prefix)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

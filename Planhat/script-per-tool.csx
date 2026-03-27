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

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Planhat — Per-Tool MCP Connector (Alternative)                             ║
// ║                                                                            ║
// ║  15 curated tools for the most common Customer Success agent scenarios.      ║
// ║  Use this script when you prefer explicit tool registration over Mission     ║
// ║  Command's scan/launch/sequence pattern.                                     ║
// ║                                                                            ║
// ║  SYNC: This connector ships a paired script. When adding or removing tools, ║
// ║  also update script.csx (Mission Command variant).                           ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string SERVER_NAME = "planhat-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-11-25";
    private const string BASE_URL = "https://api.planhat.com";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            var request = JObject.Parse(body);

            var method = request["method"]?.ToString();
            var id = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

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
        catch (JsonException ex) { return CreateJsonRpcError(null, -32700, "Parse error", ex.Message); }
        catch (Exception ex) { return CreateJsonRpcError(null, -32603, "Internal error", ex.Message); }
    }

    private HttpResponseMessage HandleInitialize(JToken id)
    {
        var result = new JObject
        {
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION,
                ["title"] = "Planhat MCP (Per-Tool)",
                ["description"] = "Planhat Customer Success platform — curated tools for companies, endusers, licenses, notes, tasks, and conversations"
            }
        };
        return CreateJsonRpcSuccess(result, id);
    }

    // ── Tools List (15 curated tools) ────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var tools = new JArray
        {
            // Companies
            Tool("list_companies", "List companies in Planhat. Returns paginated results with optional field selection and sorting.",
                Props(P("limit", "integer", "Max results (default 100)"), P("offset", "integer", "Offset for pagination"), P("sort", "string", "Sort field (prefix with - for descending)"), P("select", "string", "Comma-separated field names to return"))),

            Tool("get_company", "Get a single company by ID. Returns full company details including health score, MRR, phase, and custom fields.",
                Props(P("id", "string", "Company ID", true)),
                "id"),

            Tool("create_company", "Create a new company in Planhat.",
                Props(P("name", "string", "Company name", true), P("externalId", "string", "External ID (e.g., CRM ID)"), P("phase", "string", "Lifecycle phase"), P("owner", "string", "Owner user ID")),
                "name"),

            Tool("update_company", "Update an existing company.",
                Props(P("id", "string", "Company ID", true), P("name", "string", "Company name"), P("phase", "string", "Lifecycle phase"), P("status", "string", "Status")),
                "id"),

            // Endusers
            Tool("list_endusers", "List endusers (contacts) with optional company filter.",
                Props(P("companyId", "string", "Filter by company ID"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("create_enduser", "Create a new enduser (contact) linked to a company.",
                Props(P("companyId", "string", "Company ID", true), P("email", "string", "Email address"), P("firstName", "string", "First name"), P("lastName", "string", "Last name")),
                "companyId"),

            // Licenses
            Tool("list_licenses", "List licenses with optional company filter.",
                Props(P("companyId", "string", "Filter by company ID"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("create_license", "Create a new license for a company.",
                Props(P("companyId", "string", "Company ID", true), P("product", "string", "Product name"), P("mrr", "number", "Monthly recurring revenue"), P("startDate", "string", "Start date (ISO 8601)"), P("renewalDate", "string", "Renewal date (ISO 8601)")),
                "companyId"),

            // Notes
            Tool("list_notes", "List notes with optional company filter.",
                Props(P("companyId", "string", "Filter by company ID"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("create_note", "Create a new note for a company. Use for meeting notes, internal comments, or general documentation.",
                Props(P("companyId", "string", "Company ID", true), P("title", "string", "Note title"), P("text", "string", "Note body text"), P("type", "string", "Note type")),
                "companyId"),

            // Tasks
            Tool("list_tasks", "List tasks with optional company and status filter.",
                Props(P("companyId", "string", "Filter by company ID"), P("status", "string", "Filter by status"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("create_task", "Create a new task for a company.",
                Props(P("companyId", "string", "Company ID", true), P("title", "string", "Task title", true), P("description", "string", "Task description"), P("dueDate", "string", "Due date (ISO 8601)"), P("owner", "string", "Owner user ID"), P("status", "string", "Task status")),
                "companyId", "title"),

            // Conversations
            Tool("list_conversations", "List conversations (emails, calls, meetings) with optional company filter.",
                Props(P("companyId", "string", "Filter by company ID"), P("type", "string", "Filter by type (email, call, meeting)"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("create_conversation", "Log a new conversation (email, call, or meeting) for a company.",
                Props(P("companyId", "string", "Company ID", true), P("type", "string", "Type: email, call, or meeting"), P("title", "string", "Conversation title"), P("description", "string", "Description/notes"), P("date", "string", "Date (ISO 8601)")),
                "companyId"),

            // NPS
            Tool("list_nps", "List NPS (Net Promoter Score) responses with optional company filter.",
                Props(P("companyId", "string", "Filter by company ID"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset")))
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
                case "list_companies": result = await ApiGetAsync("/companies", args, "limit", "offset", "sort", "select"); break;
                case "get_company": result = await ApiGetAsync($"/companies/{Req(args, "id")}"); break;
                case "create_company": result = await ApiPostAsync("/companies", args, "name", "externalId", "phase", "status", "owner", "coOwner"); break;
                case "update_company": result = await ApiPutAsync($"/companies/{Req(args, "id")}", args, "name", "phase", "status", "owner"); break;
                case "list_endusers": result = await ApiGetAsync("/endusers", args, "companyId", "limit", "offset"); break;
                case "create_enduser": result = await ApiPostAsync("/endusers", args, "companyId", "email", "firstName", "lastName", "externalId"); break;
                case "list_licenses": result = await ApiGetAsync("/licenses", args, "companyId", "limit", "offset"); break;
                case "create_license": result = await ApiPostAsync("/licenses", args, "companyId", "product", "mrr", "startDate", "renewalDate"); break;
                case "list_notes": result = await ApiGetAsync("/notes", args, "companyId", "limit", "offset"); break;
                case "create_note": result = await ApiPostAsync("/notes", args, "companyId", "title", "text", "type"); break;
                case "list_tasks": result = await ApiGetAsync("/tasks", args, "companyId", "status", "limit", "offset"); break;
                case "create_task": result = await ApiPostAsync("/tasks", args, "companyId", "title", "description", "dueDate", "owner", "status"); break;
                case "list_conversations": result = await ApiGetAsync("/conversations", args, "companyId", "type", "limit", "offset"); break;
                case "create_conversation": result = await ApiPostAsync("/conversations", args, "companyId", "type", "title", "description", "date"); break;
                case "list_nps": result = await ApiGetAsync("/nps", args, "companyId", "limit", "offset"); break;
                default: return CreateToolResult($"Unknown tool: {toolName}", true, id);
            }
            return CreateToolResult(result.ToString(Newtonsoft.Json.Formatting.Indented), false, id);
        }
        catch (Exception ex) { return CreateToolResult($"Tool execution failed: {ex.Message}", true, id); }
    }

    // ── API Helpers ──────────────────────────────────────────────────────

    private string GetApiToken()
    {
        try
        {
            var token = this.Context.ConnectionParameters["apiToken"]?.ToString();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("API token not configured");
            return token;
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            throw new InvalidOperationException("API token not configured");
        }
    }

    private async Task<JToken> ApiGetAsync(string path, JObject args = null, params string[] queryFields)
    {
        var url = BASE_URL + path;
        if (args != null && queryFields.Length > 0)
        {
            var qp = new List<string>();
            foreach (var f in queryFields)
            {
                var v = args[f]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) qp.Add($"{Uri.EscapeDataString(f)}={Uri.EscapeDataString(v)}");
            }
            if (qp.Count > 0) url += "?" + string.Join("&", qp);
        }
        return await SendAsync(HttpMethod.Get, url);
    }

    private async Task<JToken> ApiPostAsync(string path, JObject args, params string[] bodyFields)
    {
        var body = new JObject();
        foreach (var f in bodyFields)
        {
            if (args[f] != null) body[f] = args[f];
        }
        return await SendAsync(HttpMethod.Post, BASE_URL + path, body);
    }

    private async Task<JToken> ApiPutAsync(string path, JObject args, params string[] bodyFields)
    {
        var body = new JObject();
        foreach (var f in bodyFields)
        {
            if (args[f] != null) body[f] = args[f];
        }
        return await SendAsync(HttpMethod.Put, BASE_URL + path, body);
    }

    private async Task<JToken> SendAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {GetApiToken()}");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && body.Count > 0)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Planhat API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["statusCode"] = (int)response.StatusCode };

        try { return JToken.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    // ── JSON-RPC Helpers ─────────────────────────────────────────────────

    private HttpResponseMessage CreateJsonRpcSuccess(JObject result, JToken id)
    {
        var r = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json") };
    }

    private HttpResponseMessage CreateJsonRpcError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;
        var r = new JObject { ["jsonrpc"] = "2.0", ["error"] = error, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json") };
    }

    private HttpResponseMessage CreateToolResult(string text, bool isError, JToken id)
    {
        return CreateJsonRpcSuccess(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        }, id);
    }

    private static JObject Tool(string name, string desc, JObject props, params string[] required)
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = props };
        if (required != null && required.Length > 0) schema["required"] = new JArray(required);
        return new JObject { ["name"] = name, ["description"] = desc, ["inputSchema"] = schema };
    }

    private static JObject Props(params JProperty[] props) { var o = new JObject(); foreach (var p in props) o.Add(p); return o; }
    private static JProperty P(string name, string type, string desc, bool req = false) => new JProperty(name, new JObject { ["type"] = type, ["description"] = desc });
    private static string Req(JObject args, string name) { var v = args[name]?.ToString(); if (string.IsNullOrWhiteSpace(v)) throw new ArgumentException($"'{name}' is required"); return v; }
}

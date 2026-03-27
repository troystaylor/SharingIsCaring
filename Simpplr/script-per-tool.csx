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
// ║  Simpplr — Per-Tool MCP Connector (Alternative)                             ║
// ║                                                                            ║
// ║  15 curated tools for the most common Copilot Studio agent scenarios.        ║
// ║  Use this script when you prefer explicit tool registration over Mission     ║
// ║  Command's scan/launch/sequence pattern.                                     ║
// ║                                                                            ║
// ║  SYNC: This connector ships a paired script. When adding or removing tools, ║
// ║  also update script.csx (Mission Command variant).                           ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string SERVER_NAME = "simpplr-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-11-25";
    private const string BASE_URL = "https://platform.app.simpplr.com/v1/b2b";

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
                ["title"] = "Simpplr MCP (Per-Tool)",
                ["description"] = "Simpplr employee experience platform — curated tools for content, feeds, sites, users, search, and service desk"
            }
        };
        return CreateJsonRpcSuccess(result, id);
    }

    // ── Tools List (15 curated tools) ────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var tools = new JArray
        {
            // Content
            Tool("search_content", "Search Simpplr content across sites. Returns pages, events, albums, and other content types.",
                Props(P("siteId", "string", "Filter by site ID"), P("type", "string", "Content type filter"), P("status", "string", "Status filter"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset for pagination"))),

            Tool("get_content", "Get a specific content item by site and content ID.",
                Props(P("siteId", "string", "Site ID", true), P("contentId", "string", "Content ID", true)),
                "siteId", "contentId"),

            Tool("add_page", "Create a new page in a Simpplr site.",
                Props(P("siteId", "string", "Site ID", true), P("title", "string", "Page title", true), P("body", "string", "Page body HTML"), P("categoryId", "string", "Category ID")),
                "siteId", "title"),

            // Feed
            Tool("create_feed", "Create a new feed post in Simpplr.",
                Props(P("body", "string", "Feed post body text", true), P("siteId", "string", "Target site ID"), P("audienceId", "string", "Target audience ID")),
                "body"),

            Tool("get_feeds", "Get feed posts. Returns a paginated list of feed entries.",
                Props(P("limit", "integer", "Max results (default 25)"), P("offset", "integer", "Offset for pagination"))),

            // Sites
            Tool("search_sites", "Search for Simpplr sites.",
                Props(P("limit", "integer", "Max results"), P("offset", "integer", "Offset for pagination"))),

            Tool("get_site_members", "Get members of a Simpplr site.",
                Props(P("siteId", "string", "Site ID", true), P("limit", "integer", "Max results"), P("offset", "integer", "Offset")),
                "siteId"),

            // Users
            Tool("get_users", "Get list of Simpplr users with optional search.",
                Props(P("search", "string", "Search query"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("get_user", "Get a specific user by ID.",
                Props(P("userId", "string", "User ID", true)),
                "userId"),

            // Enterprise Search
            Tool("enterprise_search", "Perform enterprise-wide search across all Simpplr content including pages, events, feeds, files, and people.",
                Props(P("query", "string", "Search query", true), P("type", "string", "Filter by type"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset")),
                "query"),

            Tool("smart_answers", "Get AI-powered smart answers from Simpplr content. Returns synthesized answers based on enterprise knowledge.",
                Props(P("query", "string", "Question to answer", true)),
                "query"),

            // Alerts
            Tool("search_alerts", "Search for alerts.",
                Props(P("status", "string", "Status filter"), P("limit", "integer", "Max results"), P("offset", "integer", "Offset"))),

            Tool("create_alert", "Create a new alert.",
                Props(P("title", "string", "Alert title", true), P("body", "string", "Alert body"), P("type", "string", "Alert type"), P("severity", "string", "Severity level")),
                "title"),

            // Service Desk
            Tool("create_ticket", "Create a new service desk ticket.",
                Props(P("subject", "string", "Ticket subject", true), P("description", "string", "Ticket description"), P("categoryId", "string", "Category ID"), P("priority", "string", "Priority level")),
                "subject"),

            Tool("get_ticket", "Get a specific service desk ticket by ID.",
                Props(P("ticketId", "string", "Ticket ID", true)),
                "ticketId")
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
                case "search_content": result = await ApiGetAsync("/content/sites/content", args, "siteId", "type", "status", "limit", "offset"); break;
                case "get_content": result = await ApiGetAsync($"/content/sites/{Req(args, "siteId")}/content/{Req(args, "contentId")}"); break;
                case "add_page": result = await ApiPostAsync($"/content/sites/{Req(args, "siteId")}/content/page", args, "title", "body", "categoryId"); break;
                case "create_feed": result = await ApiPostAsync("/feed", args, "body", "siteId", "audienceId"); break;
                case "get_feeds": result = await ApiGetAsync("/feed", args, "limit", "offset"); break;
                case "search_sites": result = await ApiGetAsync("/sites", args, "limit", "offset"); break;
                case "get_site_members": result = await ApiGetAsync($"/sites/{Req(args, "siteId")}/members", args, "limit", "offset"); break;
                case "get_users": result = await ApiGetAsync("/identity/users", args, "search", "limit", "offset"); break;
                case "get_user": result = await ApiGetAsync($"/identity/users/{Req(args, "userId")}"); break;
                case "enterprise_search": result = await ApiPostAsync("/enterprise-search/search", args, "query", "type", "limit", "offset"); break;
                case "smart_answers": result = await ApiPostAsync("/enterprise-search/smartanswers", args, "query"); break;
                case "search_alerts": result = await ApiGetAsync("/alerts", args, "status", "limit", "offset"); break;
                case "create_alert": result = await ApiPostAsync("/alerts", args, "title", "body", "type", "severity"); break;
                case "create_ticket": result = await ApiPostAsync("/service-desk/tickets", args, "subject", "description", "categoryId", "priority"); break;
                case "get_ticket": result = await ApiGetAsync($"/service-desk/tickets/{Req(args, "ticketId")}"); break;
                default: return CreateToolResult($"Unknown tool: {toolName}", true, id);
            }
            return CreateToolResult(result.ToString(Newtonsoft.Json.Formatting.Indented), false, id);
        }
        catch (Exception ex) { return CreateToolResult($"Tool execution failed: {ex.Message}", true, id); }
    }

    // ── API Helpers ──────────────────────────────────────────────────────

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
        var url = BASE_URL + path;
        var body = new JObject();
        foreach (var f in bodyFields)
        {
            if (args[f] != null) body[f] = args[f];
        }
        return await SendAsync(HttpMethod.Post, url, body);
    }

    private string _cachedAccessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private async Task<string> GetAccessTokenAsync()
    {
        if (_cachedAccessToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedAccessToken;

        var clientId = GetConnectionParameter("clientId");
        var clientSecret = GetConnectionParameter("clientSecret");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Simpplr clientId and clientSecret are required");

        var tokenUrl = "https://platform.app.simpplr.com/v1/b2b/identity/oauth/token";
        var tokenBody = new JObject { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret };
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new StringContent(tokenBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        var tokenResponse = await this.Context.SendAsync(tokenRequest, this.CancellationToken).ConfigureAwait(false);
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new Exception($"Simpplr OAuth token error ({(int)tokenResponse.StatusCode}): {tokenContent}");
        var tokenObj = JObject.Parse(tokenContent);
        _cachedAccessToken = tokenObj["access_token"]?.ToString() ?? throw new Exception("No access_token in OAuth response");
        var expiresIn = tokenObj["expires_in"]?.Value<int>() ?? 3600;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        return _cachedAccessToken;
    }

    private string GetConnectionParameter(string name)
    {
        try { var raw = this.Context.ConnectionParameters[name]?.ToString(); return string.IsNullOrWhiteSpace(raw) ? null : raw; }
        catch { return null; }
    }

    private async Task<JToken> SendAsync(HttpMethod method, string url, JObject body = null)
    {
        var accessToken = await GetAccessTokenAsync().ConfigureAwait(false);
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && body.Count > 0)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Simpplr API error ({(int)response.StatusCode}): {content}");

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

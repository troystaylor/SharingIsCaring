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
// ║  6sense Revenue AI — Per-Tool MCP Connector                                 ║
// ║                                                                            ║
// ║  Tools: identify_company, enrich_company, score_and_enrich_lead,            ║
// ║         score_lead, enrich_people, search_people, search_people_dictionary   ║
// ║                                                                            ║
// ║  Auth: API Token (Authorization: Token <api_token>)                         ║
// ║  Hosts: epsilon.6sense.com, api.6sense.com, scribe.6sense.com               ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string SERVER_NAME = "6sense-mcp";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-11-25";

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
        catch (JsonException ex)
        {
            return CreateJsonRpcError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcError(null, -32603, "Internal error", ex.Message);
        }
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
                ["title"] = "6sense Revenue AI MCP",
                ["description"] = "Company identification, firmographic enrichment, lead scoring, people enrichment, and people search tools for 6sense Revenue AI"
            }
        };
        return CreateJsonRpcSuccess(result, id);
    }

    // ── Tools List ───────────────────────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var tools = new JArray
        {
            Tool("identify_company",
                "Identify an anonymous website visitor's company by IP address. Returns company firmographics, segments, and 6QA scores. Uses the Company Identification API (GET epsilon.6sense.com/v3/company/details).",
                Props(
                    P("ip", "string", "IPv4 address of the website visitor", true)
                ), "ip"),

            Tool("enrich_company",
                "Enrich a lead with company firmographics using an email address. Returns company name, industry, employee count, revenue, segments, and more. Uses the Company Firmographics API v3 (POST api.6sense.com/v1/enrichment/company).",
                Props(
                    P("email", "string", "Email address of the lead", false),
                    P("domain", "string", "Company domain (e.g., 6sense.com)", false),
                    P("company", "string", "Company name", false),
                    P("country", "string", "Country name", false),
                    P("industry", "string", "Industry name", false),
                    P("title", "string", "Job title", false),
                    P("role", "string", "Job role", false),
                    P("firstname", "string", "First name", false),
                    P("lastname", "string", "Last name", false),
                    P("leadsource", "string", "Lead source", false)
                )),

            Tool("score_and_enrich_lead",
                "Combine lead scoring and company firmographics enrichment in a single call. Returns predictive scores (company and contact level) plus company firmographics and segments. Uses Lead Scoring and Firmographics API (POST scribe.6sense.com/v2/people/full).",
                Props(
                    P("email", "string", "Email address of the lead", true),
                    P("company", "string", "Company name", false),
                    P("website", "string", "Company website", false),
                    P("country", "string", "Country name", false),
                    P("title", "string", "Job title", false),
                    P("role", "string", "Job role", false),
                    P("firstname", "string", "First name", false),
                    P("lastname", "string", "Last name", false),
                    P("leadsource", "string", "Lead source", false),
                    P("industry", "string", "Industry name", false)
                ), "email"),

            Tool("score_lead",
                "Score a lead with 6sense predictive scores by email address. Returns company and contact-level intent, profile, and buying stage scores per product category. Uses Lead Scoring API (POST scribe.6sense.com/v2/people/score).",
                Props(
                    P("email", "string", "Email address of the lead", true),
                    P("company", "string", "Company name", false),
                    P("website", "string", "Company website", false),
                    P("country", "string", "Country name", false),
                    P("title", "string", "Job title", false),
                    P("role", "string", "Job role", false),
                    P("firstname", "string", "First name", false),
                    P("lastname", "string", "Last name", false),
                    P("leadsource", "string", "Lead source", false),
                    P("industry", "string", "Industry name", false)
                ), "email"),

            Tool("enrich_people",
                "Enrich contacts with detailed person-level data including job title, skills, education, contact info, and company details. Accepts up to 25 queries per call using email, LinkedIn URL, or peopleId. Uses People Enrichment API v2 (POST api.6sense.com/v2/enrichment/people).",
                Props(
                    P("queries", "array", "Array of query objects. Each must have at least one of: email, linkedInUrl, or peopleId. Optional: referenceKeys object for tracking.", true)
                ), "queries"),

            Tool("search_people",
                "Search for B2B contacts using filters like domain, job title, industry, location, and more. Returns contact metadata (not email/phone directly—use enrich_people with the returned peopleId for contact details). Uses People Search API v2 (POST api.6sense.com/v2/search/people).",
                Props(
                    P("domain", "array", "Array of company domains to search (e.g., [\"6sense.com\"])", false),
                    P("industryNAICS", "array", "Array of NAICS industry codes", false),
                    P("email", "array", "Array of email addresses to search", false),
                    P("linkedinUrl", "array", "Array of LinkedIn URLs to search", false),
                    P("country", "array", "Array of country names (e.g., [\"US\", \"Canada\"])", false),
                    P("jobTitle", "array", "Array of job titles to search", false),
                    P("function", "array", "Array of job functions (e.g., [\"CEO\", \"Marketing\"])", false),
                    P("division", "array", "Array of divisions", false),
                    P("level", "array", "Array of seniority levels (e.g., [\"Director\", \"VP\"])", false),
                    P("city", "array", "Array of cities", false),
                    P("state", "array", "Array of states", false),
                    P("hasEmail", "boolean", "Filter to contacts with email available", false),
                    P("emailConfidence", "string", "Minimum email confidence (e.g., A+)", false),
                    P("hasPhone", "boolean", "Filter to contacts with phone available", false),
                    P("pageNo", "integer", "Page number (default 1)", false),
                    P("pageSize", "integer", "Results per page (max 1000, default 50)", false)
                )),

            Tool("search_people_dictionary",
                "Get available filter values (cities, states, countries, titles, industries) for a specific domain. Use this before search_people to discover valid filter values. Uses People Search Dictionary API (GET api.6sense.com/v1/dictionary/peopleSearch).",
                Props(
                    P("domainName", "string", "Company domain to get dictionary values for (e.g., 6sense.com)", true),
                    P("pageNumber", "integer", "Page number (default 1)", false),
                    P("pageSize", "integer", "Results per page (default 999)", false)
                ), "domainName")
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
                case "identify_company":
                    result = await IdentifyCompanyAsync(args);
                    break;
                case "enrich_company":
                    result = await EnrichCompanyAsync(args);
                    break;
                case "score_and_enrich_lead":
                    result = await ScoreAndEnrichLeadAsync(args);
                    break;
                case "score_lead":
                    result = await ScoreLeadAsync(args);
                    break;
                case "enrich_people":
                    result = await EnrichPeopleAsync(args);
                    break;
                case "search_people":
                    result = await SearchPeopleAsync(args);
                    break;
                case "search_people_dictionary":
                    result = await SearchPeopleDictionaryAsync(args);
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

    // ── Tool Implementations ─────────────────────────────────────────────

    private async Task<JToken> IdentifyCompanyAsync(JObject args)
    {
        var ip = Require(args, "ip");
        return await SendGetAsync($"https://epsilon.6sense.com/v3/company/details?ip={Uri.EscapeDataString(ip)}");
    }

    private async Task<JToken> EnrichCompanyAsync(JObject args)
    {
        var formData = BuildFormData(args, "email", "domain", "company", "country", "industry", "title", "role", "firstname", "lastname", "leadsource");
        if (formData.Count == 0)
            throw new ArgumentException("At least one of 'email', 'domain', or 'company' is required for enrichment");
        return await SendFormPostAsync("https://api.6sense.com/v1/enrichment/company", formData);
    }

    private async Task<JToken> ScoreAndEnrichLeadAsync(JObject args)
    {
        var formData = BuildFormData(args, "email", "company", "website", "country", "title", "role", "firstname", "lastname", "leadsource", "industry");
        return await SendFormPostAsync("https://scribe.6sense.com/v2/people/full", formData);
    }

    private async Task<JToken> ScoreLeadAsync(JObject args)
    {
        var formData = BuildFormData(args, "email", "company", "website", "country", "title", "role", "firstname", "lastname", "leadsource", "industry");
        return await SendFormPostAsync("https://scribe.6sense.com/v2/people/score", formData);
    }

    private async Task<JToken> EnrichPeopleAsync(JObject args)
    {
        var queries = args["queries"] as JArray;
        if (queries == null || queries.Count == 0)
            throw new ArgumentException("'queries' array is required and must contain at least one query");
        if (queries.Count > 25)
            throw new ArgumentException("Maximum 25 queries per call");

        return await SendJsonPostAsync("https://api.6sense.com/v2/enrichment/people", queries);
    }

    private async Task<JToken> SearchPeopleAsync(JObject args)
    {
        var body = new JObject();
        var arrayFields = new[] { "domain", "industryNAICS", "email", "linkedinUrl", "country", "jobTitle", "function", "division", "level", "city", "state" };
        foreach (var field in arrayFields)
        {
            if (args[field] != null)
                body[field] = args[field];
        }
        var boolFields = new[] { "hasEmail", "hasPhone", "hasWorkPhone", "hasLinkedinUrl", "hasTwitterUrl", "hasFacebookUrl" };
        foreach (var field in boolFields)
        {
            if (args[field] != null)
                body[field] = args[field];
        }
        if (args["emailConfidence"] != null) body["emailConfidence"] = args["emailConfidence"];
        if (args["pageNo"] != null) body["pageNo"] = args["pageNo"];
        if (args["pageSize"] != null) body["pageSize"] = args["pageSize"];

        return await SendJsonPostAsync("https://api.6sense.com/v2/search/people", body);
    }

    private async Task<JToken> SearchPeopleDictionaryAsync(JObject args)
    {
        var domainName = Require(args, "domainName");
        var pageNumber = args["pageNumber"]?.ToString() ?? "1";
        var pageSize = args["pageSize"]?.ToString() ?? "999";
        var url = $"https://api.6sense.com/v1/dictionary/peopleSearch?domainName={Uri.EscapeDataString(domainName)}&pageNumber={pageNumber}&pageSize={pageSize}";
        return await SendGetAsync(url);
    }

    // ── HTTP Helpers ─────────────────────────────────────────────────────

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

    private async Task<JToken> SendGetAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Token {GetApiToken()}");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"6sense API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["status"] = "no_data", ["statusCode"] = (int)response.StatusCode };

        try { return JToken.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private async Task<JToken> SendFormPostAsync(string url, Dictionary<string, string> formData)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Token {GetApiToken()}");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(formData);

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"6sense API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["status"] = "no_data", ["statusCode"] = (int)response.StatusCode };

        try { return JToken.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private async Task<JToken> SendJsonPostAsync(string url, JToken body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Token {GetApiToken()}");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"6sense API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["status"] = "no_data", ["statusCode"] = (int)response.StatusCode };

        try { return JToken.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private Dictionary<string, string> BuildFormData(JObject args, params string[] fields)
    {
        var data = new Dictionary<string, string>();
        foreach (var field in fields)
        {
            var value = args[field]?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                data[field] = value;
        }
        return data;
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

    private static JProperty P(string name, string type, string description, bool required = false)
    {
        var prop = new JObject { ["type"] = type, ["description"] = description };
        return new JProperty(name, prop);
    }

    private static string Require(JObject args, string name)
    {
        var value = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }
}

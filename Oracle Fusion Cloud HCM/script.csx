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
// ║  ORACLE FUSION CLOUD HCM — MCP CONNECTOR                                        ║
// ║                                                                                ║
// ║  Dual-mode connector: exposes generic CRUD over the whole Oracle Fusion HCM    ║
// ║  ADF REST surface as MCP tools (for Copilot Studio) while the Swagger keeps     ║
// ║  typed operations (for Power Automate IntelliSense).                            ║
// ║                                                                                ║
// ║  Only the InvokeMCP operation is handled here. Typed operations are proxied     ║
// ║  directly to the Oracle pod by the platform.                                    ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Deployment Configuration ─────────────────────────────────────────
    //
    //   Replace POD_HOST with your Oracle Fusion pod host (no scheme, no path).
    //   It MUST match the `host` field in apiDefinition.swagger.json.
    //   Example: "myserver.fa.us2.oraclecloud.com"
    //
    private const string POD_HOST = "replace-pod-host.oraclecloud.com";

    /// <summary>REST resource collection base. HCM ships version 11.13.18.05.</summary>
    private const string API_BASE = "/hcmRestApi/resources/11.13.18.05";

    /// <summary>App Insights connection string (leave empty to disable telemetry).</summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "oracle-fusion-hcm",
            Version = "1.0.0",
            Title = "Oracle Fusion Cloud HCM",
            Description = "Generic CRUD over the Oracle Fusion Cloud HCM REST API (ADF REST)."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities { Tools = true },
        Instructions =
            "Tools operate against Oracle Fusion Cloud HCM REST resources (e.g. workers, emps, " +
            "jobs, positions, grades, departments, absences, publicWorkers). Use list_common_resources " +
            "to see frequently used resource names, describe_resource to inspect a resource's fields, " +
            "then query_records / get_record / create_record / update_record / delete_record. " +
            "Filters use the ADF 'q' RSQL syntax, e.g. \"PersonNumber='100010'\" or \"LastName LIKE 'Sm%'\"."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "ListResources": return await HandleListResourcesAsync().ConfigureAwait(false);
            case "ListResourceFields": return await HandleListResourceFieldsAsync().ConfigureAwait(false);
            case "ListChildResources": return await HandleListChildResourcesAsync().ConfigureAwait(false);
            case "GetResourceSchema": return await HandleGetResourceSchemaAsync().ConfigureAwait(false);
            default: return await HandleMcpAsync().ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var correlationId = Guid.NewGuid().ToString();

        var handler = new McpRequestHandler(Options);
        RegisterCapabilities(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Dynamic Resolvers (designer dropdowns and schemas) ───────────────

    /// <summary>Populates the resource dropdown from the live catalog, falling back to a curated list.</summary>
    private async Task<HttpResponseMessage> HandleListResourcesAsync()
    {
        var values = new JArray();
        try
        {
            var catalog = await SendOracleAsync(HttpMethod.Get, API_BASE).ConfigureAwait(false);
            if (catalog["links"] is JArray links)
            {
                foreach (var link in links)
                {
                    if ((link.Value<string>("rel") ?? "") != "child") continue;
                    var name = link.Value<string>("name");
                    if (!string.IsNullOrWhiteSpace(name))
                        values.Add(new JObject { ["name"] = name, ["title"] = name });
                }
            }
        }
        catch { /* fall back to curated list below */ }

        if (values.Count == 0 && CommonResources()["resources"] is JArray curated)
        {
            foreach (var r in curated)
            {
                var name = r.Value<string>("resource");
                values.Add(new JObject { ["name"] = name, ["title"] = name });
            }
        }

        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Populates a field picker (attributes) for the selected resource.</summary>
    private async Task<HttpResponseMessage> HandleListResourceFieldsAsync()
    {
        var values = new JArray();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            foreach (var attr in await GetAttributesAsync(resource).ConfigureAwait(false))
            {
                var name = attr.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                values.Add(new JObject { ["name"] = name, ["title"] = attr.Value<string>("title") ?? name });
            }
        }
        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Populates a child-resource picker (for expand) for the selected resource.</summary>
    private async Task<HttpResponseMessage> HandleListChildResourcesAsync()
    {
        var values = new JArray();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            var node = await GetResourceNodeAsync(resource).ConfigureAwait(false);
            if (node?["childResources"] is JObject children)
            {
                foreach (var p in children.Properties())
                    values.Add(new JObject { ["name"] = p.Name, ["title"] = p.Name });
            }
        }
        return JsonResponse(new JObject { ["value"] = values });
    }

    /// <summary>Builds a JSON Schema for the selected resource's writable attributes (dynamic body).</summary>
    private async Task<HttpResponseMessage> HandleGetResourceSchemaAsync()
    {
        var properties = new JObject();
        var resource = GetQueryParam("resource");
        if (!string.IsNullOrWhiteSpace(resource))
        {
            foreach (var attr in await GetAttributesAsync(resource).ConfigureAwait(false))
            {
                var name = attr.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = new JObject
                {
                    ["type"] = MapAdfType(attr.Value<string>("type")),
                    ["title"] = attr.Value<string>("title") ?? name
                };
                var description = attr.Value<string>("description");
                if (!string.IsNullOrWhiteSpace(description)) prop["description"] = description;
                properties[name] = prop;
            }
        }

        var schema = new JObject { ["type"] = "object", ["properties"] = properties };
        return JsonResponse(new JObject { ["schema"] = schema });
    }

    /// <summary>Returns the attributes array from a resource's describe document.</summary>
    private async Task<JArray> GetAttributesAsync(string resource)
    {
        var node = await GetResourceNodeAsync(resource).ConfigureAwait(false);
        return node?["attributes"] as JArray ?? new JArray();
    }

    /// <summary>Returns the metadata node for a resource from its describe document.</summary>
    private async Task<JObject> GetResourceNodeAsync(string resource)
    {
        var describe = await SendOracleAsync(HttpMethod.Get, $"{API_BASE}/{TrimResource(resource)}/describe").ConfigureAwait(false);
        if (!(describe["Resources"] is JObject resources)) return null;
        return (resources[TrimResource(resource)] ?? resources.Properties().FirstOrDefault()?.Value) as JObject;
    }

    private static string MapAdfType(string adfType)
    {
        switch ((adfType ?? "").ToLowerInvariant())
        {
            case "integer":
            case "long":
            case "int": return "integer";
            case "number":
            case "decimal":
            case "double":
            case "float": return "number";
            case "boolean": return "boolean";
            default: return "string";
        }
    }

    private string GetQueryParam(string name)
    {
        var query = this.Context.Request.RequestUri?.Query ?? "";
        if (query.StartsWith("?")) query = query.Substring(1);
        foreach (var pair in query.Split('&'))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = Uri.UnescapeDataString(pair.Substring(0, idx));
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair.Substring(idx + 1));
        }
        return null;
    }

    private static HttpResponseMessage JsonResponse(JToken token) => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(token.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
    };

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        handler.AddTool("list_common_resources",
            "Lists frequently used Oracle Fusion HCM REST resource names with a short description of each. " +
            "Use the returned names as the 'resource' argument for the other tools.",
            schema: s => { },
            handler: async (args, ct) => CommonResources(),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("describe_resource",
            "Returns the metadata (fields, actions, child resources) for a single HCM REST resource. " +
            "Call this before create/update to learn the required attributes.",
            schema: s => s.String("resource", "Resource collection name, e.g. 'workers' or 'jobs'.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                return await SendOracleAsync(HttpMethod.Get, $"{API_BASE}/{TrimResource(resource)}/describe").ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("query_records",
            "Queries records from an HCM REST resource. Supports the ADF query grammar: 'q' (RSQL filter), " +
            "'fields' (comma-separated attributes), 'expand' (child resources), 'orderBy', and 'limit'.",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'workers'.", required: true)
                .String("q", "RSQL filter, e.g. \"PersonNumber='100010'\" or \"LastName LIKE 'Sm%'\".")
                .String("fields", "Comma-separated attributes to return, e.g. 'PersonId,PersonNumber,DateOfBirth'.")
                .String("expand", "Child resources to expand, e.g. 'names,emails'.")
                .String("orderBy", "Sort expression, e.g. 'PersonNumber:asc'.")
                .Integer("limit", "Maximum number of records to return (default 25).", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var query = new Dictionary<string, string>
                {
                    ["onlyData"] = "true",
                    ["limit"] = (args.Value<int?>("limit") ?? 25).ToString()
                };
                AddIfPresent(query, "q", GetArgument(args, "q"));
                AddIfPresent(query, "fields", GetArgument(args, "fields"));
                AddIfPresent(query, "expand", GetArgument(args, "expand"));
                AddIfPresent(query, "orderBy", GetArgument(args, "orderBy"));
                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/{TrimResource(resource)}", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_record",
            "Retrieves a single HCM record by its resource key (the primary key value the API returns in 'links'/'self').",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'workers'.", required: true)
                .String("id", "The record key, e.g. a PersonId or the composite key from the resource's self link.", required: true)
                .String("fields", "Comma-separated attributes to return.")
                .String("expand", "Child resources to expand."),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                var query = new Dictionary<string, string> { ["onlyData"] = "true" };
                AddIfPresent(query, "fields", GetArgument(args, "fields"));
                AddIfPresent(query, "expand", GetArgument(args, "expand"));
                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_record",
            "Creates a new record in an HCM REST resource. Provide the attributes as a JSON object string.",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'emps'.", required: true)
                .String("record", "The record attributes as a JSON object string, e.g. '{\"LastName\":\"Smith\"}'.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var record = ParseRecord(RequireArgument(args, "record"));
                return await SendOracleAsync(HttpMethod.Post, $"{API_BASE}/{TrimResource(resource)}", record).ConfigureAwait(false);
            },
            annotations: a => { a["idempotentHint"] = false; });

        handler.AddTool("update_record",
            "Partially updates (PATCH) an existing HCM record. Provide only the attributes to change as a JSON object string.",
            schema: s => s
                .String("resource", "Resource collection name, e.g. 'emps'.", required: true)
                .String("id", "The record key.", required: true)
                .String("record", "The attributes to change as a JSON object string.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                var record = ParseRecord(RequireArgument(args, "record"));
                return await SendOracleAsync(new HttpMethod("PATCH"), $"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}", record).ConfigureAwait(false);
            });

        handler.AddTool("delete_record",
            "Deletes an HCM record by its resource key. Not all resources support delete.",
            schema: s => s
                .String("resource", "Resource collection name.", required: true)
                .String("id", "The record key.", required: true),
            handler: async (args, ct) =>
            {
                var resource = RequireArgument(args, "resource");
                var id = RequireArgument(args, "id");
                return await SendOracleAsync(HttpMethod.Delete, $"{API_BASE}/{TrimResource(resource)}/{Uri.EscapeDataString(id)}").ConfigureAwait(false);
            },
            annotations: a => { a["destructiveHint"] = true; });

        handler.AddTool("search_workers",
            "Convenience search over the 'workers' resource. Pass a free-text 'name' (matched against display name) " +
            "or a raw 'q' RSQL filter for precise queries.",
            schema: s => s
                .String("name", "Partial worker name to search for (matched with LIKE).")
                .String("q", "Raw RSQL filter to use instead of 'name', e.g. \"PersonNumber='100010'\".")
                .Integer("limit", "Maximum number of workers to return (default 25).", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var q = GetArgument(args, "q");
                var name = GetArgument(args, "name");
                if (string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(name))
                    q = $"DisplayName LIKE '%{name.Replace("'", "''")}%'";

                var query = new Dictionary<string, string>
                {
                    ["onlyData"] = "true",
                    ["limit"] = (args.Value<int?>("limit") ?? 25).ToString()
                };
                AddIfPresent(query, "q", q);
                return await SendOracleAsync(HttpMethod.Get, BuildPath($"{API_BASE}/workers", query)).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; });
    }

    // ── Oracle Request Helpers ───────────────────────────────────────────

    /// <summary>Send a request to the Oracle Fusion pod, forwarding the connector's OAuth token.</summary>
    private async Task<JObject> SendOracleAsync(HttpMethod method, string pathAndQuery, JObject body = null)
    {
        var url = $"https://{POD_HOST}{pathAndQuery}";
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Oracle HCM request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    /// <summary>Append a query string to a path, URL-encoding each value.</summary>
    private static string BuildPath(string path, IDictionary<string, string> query)
    {
        if (query == null || query.Count == 0) return path;
        var parts = query.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                         .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
        return $"{path}?{string.Join("&", parts)}";
    }

    private static void AddIfPresent(IDictionary<string, string> dict, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) dict[key] = value;
    }

    /// <summary>Strip leading/trailing slashes so callers can pass 'workers' or '/workers'.</summary>
    private static string TrimResource(string resource) => resource.Trim().Trim('/');

    private static JObject ParseRecord(string json)
    {
        try { return JObject.Parse(json); }
        catch (Exception ex) { throw new ArgumentException($"'record' must be a JSON object: {ex.Message}"); }
    }

    private static JObject CommonResources()
    {
        var list = new JArray
        {
            R("workers", "Read worker person, employment, and assignment data."),
            R("publicWorkers", "Directory-style worker data available to all employees."),
            R("emps", "Employee records used for hire, rehire, and employment changes."),
            R("workRelationships", "Work relationships for a person (employee, contingent worker)."),
            R("jobs", "Job definitions in the workforce structure."),
            R("positions", "Positions in the workforce structure."),
            R("grades", "Grades and grade ladders."),
            R("departments", "Departments / organizations."),
            R("locations", "Physical work locations."),
            R("absences", "Absence records (person absence entries)."),
            R("absenceTypes", "Configured absence types."),
            R("payrolls", "Payroll definitions."),
            R("salaries", "Worker salary records."),
            R("userAccounts", "User account provisioning data."),
            R("grievances", "Employee grievance cases.")
        };
        return new JObject
        {
            ["note"] = "This is a curated subset. The full resource catalog is available at /hcmRestApi/resources/11.13.18.05/. Use describe_resource for a resource's fields.",
            ["resources"] = list
        };
    }

    private static JObject R(string name, string description) =>
        new JObject { ["resource"] = name, ["description"] = description };

    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

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
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Suppress telemetry errors */ }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  MCP FRAMEWORK (spec 2025-11-25) — do not modify unless extending the framework.║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "array", ["description"] = description, ["items"] = itemSchema };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = _properties };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schemaConfig,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotationsConfig = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotations,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping":
                    return SerializeSuccess(id, new JObject());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "logging/setLevel":
                    return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = method, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = serverInfo
        };
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };
                if (jobj["structuredContent"] is JObject structured)
                    callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, ErrorResult($"Invalid arguments: {ex.Message}"));
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, ErrorResult($"Tool error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, ErrorResult($"Tool execution failed: {ex.Message}"));
        }
    }

    private static JObject ErrorResult(string message) => new JObject
    {
        ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = message } },
        ["isError"] = true
    };

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    private string SerializeSuccess(JToken id, JObject result) => new JObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    }.ToString(Newtonsoft.Json.Formatting.None);

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null) =>
        SerializeError(id, (int)code, message, data);

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) => OnLog?.Invoke(eventName, data);
}

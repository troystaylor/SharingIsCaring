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
// ║  AI Builder Credits MCP Server                                              ║
// ║                                                                            ║
// ║  Monitor AI Builder credit consumption in your Power Platform environment. ║
// ║  Queries the Dataverse msdyn_aievents table for usage data.                 ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "ai-builder-credits",
            Version = "1.0.0",
            Title = "AI Builder Credits",
            Description = "Monitor AI Builder credit consumption across your Power Platform environment"
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = false,
            Prompts = false,
            Logging = true,
            Completions = false
        },
        Instructions = "Use these tools to monitor AI Builder credit consumption. Query recent events, get summaries by model or source, and track usage over time."
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        var handler = new McpRequestHandler(Options);
        RegisterCapabilities(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        // ── List AI Events ───────────────────────────────────────────────
        handler.AddTool("list_ai_events", "List recent AI Builder events with credit consumption details. Returns model name, credits consumed, processing date, source (Power Automate, Power Apps, API, Copilot Studio), and status.",
            schema: s => s
                .Integer("top", "Maximum number of events to return (default: 25, max: 100)", defaultValue: 25)
                .String("source", "Filter by source: PowerAutomate, PowerApps, API, or CopilotStudio", required: false)
                .String("fromDate", "Filter events from this date (ISO 8601 format, e.g., 2025-01-01)", required: false),
            handler: async (args, ct) =>
            {
                var top = Math.Min(args.Value<int?>("top") ?? 25, 100);
                var source = args.Value<string>("source");
                var fromDate = args.Value<string>("fromDate");

                var filter = BuildFilter(source, fromDate);
                var query = BuildQuery(top, filter, expand: true);
                var response = await QueryDataverseAsync("msdyn_aievents", query, ct).ConfigureAwait(false);

                var events = response["value"] as JArray ?? new JArray();
                var results = new JArray();

                foreach (var evt in events)
                {
                    results.Add(new JObject
                    {
                        ["id"] = evt["msdyn_aieventid"],
                        ["modelName"] = evt["msdyn_AIModelId"]?["msdyn_name"] ?? evt["msdyn_name"],
                        ["credits"] = evt["msdyn_creditconsumed"],
                        ["date"] = evt["msdyn_processingdate"] ?? evt["createdon"],
                        ["source"] = GetSourceName(evt.Value<int?>("msdyn_consumptionsource")),
                        ["status"] = evt.Value<int?>("msdyn_processingstatus") == 0 ? "Processed" : "Pending",
                        ["automationName"] = evt["msdyn_automationname"],
                        ["dataType"] = evt["msdyn_datatype"]
                    });
                }

                return new JObject
                {
                    ["count"] = results.Count,
                    ["events"] = results
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Get Credit Summary ───────────────────────────────────────────
        handler.AddTool("get_credit_summary", "Get a summary of AI Builder credit consumption grouped by model and source for the current month or specified date range.",
            schema: s => s
                .String("fromDate", "Start date (ISO 8601 format, e.g., 2025-01-01). Defaults to first of current month.", required: false)
                .String("toDate", "End date (ISO 8601 format). Defaults to today.", required: false),
            handler: async (args, ct) =>
            {
                var fromDateStr = args.Value<string>("fromDate");
                var toDateStr = args.Value<string>("toDate");

                DateTime fromDate;
                if (!string.IsNullOrEmpty(fromDateStr))
                    DateTime.TryParse(fromDateStr, out fromDate);
                else
                    fromDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

                DateTime toDate;
                if (!string.IsNullOrEmpty(toDateStr))
                    DateTime.TryParse(toDateStr, out toDate);
                else
                    toDate = DateTime.UtcNow;

                var filter = $"msdyn_processingdate ge {fromDate:yyyy-MM-dd} and msdyn_processingdate le {toDate:yyyy-MM-dd}";
                var query = $"$select=msdyn_creditconsumed,msdyn_consumptionsource&$expand=msdyn_AIModelId($select=msdyn_name)&$filter={filter}";
                var response = await QueryDataverseAsync("msdyn_aievents", query, ct).ConfigureAwait(false);

                var events = response["value"] as JArray ?? new JArray();

                // Group by model
                var byModel = events
                    .GroupBy(e => (string)(e["msdyn_AIModelId"]?["msdyn_name"] ?? "(No Model)"))
                    .Select(g => new JObject
                    {
                        ["model"] = g.Key,
                        ["eventCount"] = g.Count(),
                        ["totalCredits"] = g.Sum(e => e.Value<int?>("msdyn_creditconsumed") ?? 0)
                    })
                    .OrderByDescending(m => m.Value<int>("totalCredits"));

                // Group by source
                var bySource = events
                    .GroupBy(e => GetSourceName(e.Value<int?>("msdyn_consumptionsource")))
                    .Select(g => new JObject
                    {
                        ["source"] = g.Key,
                        ["eventCount"] = g.Count(),
                        ["totalCredits"] = g.Sum(e => e.Value<int?>("msdyn_creditconsumed") ?? 0)
                    })
                    .OrderByDescending(s => s.Value<int>("totalCredits"));

                var totalCredits = events.Sum(e => e.Value<int?>("msdyn_creditconsumed") ?? 0);

                return new JObject
                {
                    ["period"] = new JObject
                    {
                        ["from"] = fromDate.ToString("yyyy-MM-dd"),
                        ["to"] = toDate.ToString("yyyy-MM-dd")
                    },
                    ["totalEvents"] = events.Count,
                    ["totalCredits"] = totalCredits,
                    ["byModel"] = new JArray(byModel),
                    ["bySource"] = new JArray(bySource)
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Get Daily Usage ───────────────────────────────────────────────
        handler.AddTool("get_daily_usage", "Get AI Builder credit consumption grouped by day for trend analysis.",
            schema: s => s
                .Integer("days", "Number of days to look back (default: 7, max: 30)", defaultValue: 7),
            handler: async (args, ct) =>
            {
                var days = Math.Min(args.Value<int?>("days") ?? 7, 30);
                var fromDate = DateTime.UtcNow.AddDays(-days);

                var filter = $"msdyn_processingdate ge {fromDate:yyyy-MM-dd}";
                var query = $"$select=msdyn_creditconsumed,msdyn_processingdate&$filter={filter}&$orderby=msdyn_processingdate desc";
                var response = await QueryDataverseAsync("msdyn_aievents", query, ct).ConfigureAwait(false);

                var events = response["value"] as JArray ?? new JArray();

                // Group by date
                var byDay = events
                    .GroupBy(e =>
                    {
                        var dateStr = e.Value<string>("msdyn_processingdate");
                        return DateTime.TryParse(dateStr, out var dt) ? dt.Date : DateTime.MinValue;
                    })
                    .Where(g => g.Key != DateTime.MinValue)
                    .Select(g => new JObject
                    {
                        ["date"] = g.Key.ToString("yyyy-MM-dd"),
                        ["eventCount"] = g.Count(),
                        ["credits"] = g.Sum(e => e.Value<int?>("msdyn_creditconsumed") ?? 0)
                    })
                    .OrderByDescending(d => d.Value<string>("date"));

                return new JObject
                {
                    ["days"] = days,
                    ["totalCredits"] = events.Sum(e => e.Value<int?>("msdyn_creditconsumed") ?? 0),
                    ["dailyUsage"] = new JArray(byDay)
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Get AI Event by ID ───────────────────────────────────────────
        handler.AddTool("get_ai_event", "Get details of a specific AI Builder event by its ID.",
            schema: s => s
                .String("eventId", "The unique identifier of the AI event", required: true),
            handler: async (args, ct) =>
            {
                var eventId = RequireArgument(args, "eventId");
                var query = "$expand=msdyn_AIModelId($select=msdyn_name,msdyn_templateid)";
                var response = await QueryDataverseAsync($"msdyn_aievents({eventId})", query, ct).ConfigureAwait(false);

                return new JObject
                {
                    ["id"] = response["msdyn_aieventid"],
                    ["modelName"] = response["msdyn_AIModelId"]?["msdyn_name"],
                    ["modelTemplate"] = response["msdyn_AIModelId"]?["msdyn_templateid"],
                    ["credits"] = response["msdyn_creditconsumed"],
                    ["date"] = response["msdyn_processingdate"],
                    ["source"] = GetSourceName(response.Value<int?>("msdyn_consumptionsource")),
                    ["status"] = response.Value<int?>("msdyn_processingstatus") == 0 ? "Processed" : "Pending",
                    ["automationName"] = response["msdyn_automationname"],
                    ["automationLink"] = response["msdyn_automationlink"],
                    ["dataType"] = response["msdyn_datatype"],
                    ["dataInfo"] = response["msdyn_datainfo"],
                    ["createdOn"] = response["createdon"]
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── List AI Models ───────────────────────────────────────────────
        handler.AddTool("list_ai_models", "List AI Builder models in the environment with their template types.",
            schema: s => s
                .Integer("top", "Maximum number of models to return (default: 50)", defaultValue: 50),
            handler: async (args, ct) =>
            {
                var top = args.Value<int?>("top") ?? 50;
                var query = $"$select=msdyn_name,msdyn_templateid,statecode,createdon&$top={top}&$orderby=msdyn_name";
                var response = await QueryDataverseAsync("msdyn_aimodels", query, ct).ConfigureAwait(false);

                var models = response["value"] as JArray ?? new JArray();
                var results = new JArray();

                foreach (var model in models)
                {
                    results.Add(new JObject
                    {
                        ["id"] = model["msdyn_aimodelid"],
                        ["name"] = model["msdyn_name"],
                        ["template"] = model["msdyn_templateid"],
                        ["status"] = model.Value<int?>("statecode") == 0 ? "Active" : "Inactive",
                        ["createdOn"] = model["createdon"]
                    });
                }

                return new JObject
                {
                    ["count"] = results.Count,
                    ["models"] = results
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string GetEnvironmentUrl()
    {
        try
        {
            var url = this.Context.ConnectionParameters["environment"]?.ToString();
            if (string.IsNullOrWhiteSpace(url))
                throw new Exception("Environment URL connection parameter is required");
            return url.TrimEnd('/');
        }
        catch
        {
            throw new Exception("Environment URL connection parameter is required");
        }
    }

    private async Task<JObject> QueryDataverseAsync(string entity, string query, CancellationToken ct)
    {
        var envUrl = GetEnvironmentUrl();
        var url = $"{envUrl}/api/data/v9.2/{entity}";
        if (!string.IsNullOrEmpty(query))
            url += "?" + query;

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Prefer", "odata.include-annotations=*");

        var response = await this.Context.SendAsync(request, ct).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Dataverse API error ({(int)response.StatusCode}): {content}");

        return JObject.Parse(content);
    }

    private static string BuildFilter(string source, string fromDate)
    {
        var filters = new List<string>();

        if (!string.IsNullOrEmpty(source))
        {
            var sourceCode = source.ToLowerInvariant() switch
            {
                "powerautomate" => 0,
                "powerapps" => 1,
                "api" => 2,
                "copilotstudio" or "mcs" => 3,
                _ => -1
            };
            if (sourceCode >= 0)
                filters.Add($"msdyn_consumptionsource eq {sourceCode}");
        }

        if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var dt))
            filters.Add($"msdyn_processingdate ge {dt:yyyy-MM-dd}");

        return filters.Count > 0 ? string.Join(" and ", filters) : null;
    }

    private static string BuildQuery(int top, string filter, bool expand)
    {
        var parts = new List<string>
        {
            "$select=msdyn_aieventid,msdyn_name,msdyn_creditconsumed,msdyn_processingdate,msdyn_consumptionsource,msdyn_processingstatus,msdyn_automationname,msdyn_datatype,createdon",
            $"$top={top}",
            "$orderby=createdon desc"
        };

        if (expand)
            parts.Add("$expand=msdyn_AIModelId($select=msdyn_name)");

        if (!string.IsNullOrEmpty(filter))
            parts.Add($"$filter={filter}");

        return string.Join("&", parts);
    }

    private static string GetSourceName(int? source) => source switch
    {
        0 => "Power Automate",
        1 => "Power Apps",
        2 => "API",
        3 => "Copilot Studio",
        _ => "Unknown"
    };

    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK                                                  ║
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

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };
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
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annot = null;
        if (annotations != null)
        {
            annot = new JObject();
            annotations(annot);
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            Annotations = annot,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

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
                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

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
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();

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

        Log("McpInitialized", new { Server = _options.ServerInfo.Name, Version = _options.ServerInfo.Version });
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

            string text;
            if (result is JObject plainObj)
                text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
            else if (result is string s)
                text = s;
            else if (result == null)
                text = "{}";
            else
                text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

            var callResult = new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                ["isError"] = false
            };

            Log("McpToolCallCompleted", new { Tool = toolName });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data))
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

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

// ── Configuration Types ──────────────────────────────────────────────────────

public class McpServerInfo
{
    public string Name { get; set; } = "gong-mcp-server";
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

// ── Error Handling ───────────────────────────────────────────────────────────

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

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

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
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
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
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

// ── Internal Tool Registration ───────────────────────────────────────────────

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

// ── McpRequestHandler ────────────────────────────────────────────────────────

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
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "resources/read":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "prompts/get":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");

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
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString()
            ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources)
            capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

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

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = clientProtocolVersion
        });

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
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
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

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: GONG MCP CONNECTOR                                            ║
// ║                                                                            ║
// ║  Gong Revenue Intelligence API exposed as MCP tools for Copilot Studio.    ║
// ║  All tools proxy to the Gong V2 REST API using the connector's OAuth token.║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string GONG_BASE = "https://api.gong.io/v2";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "gong-mcp-server",
            Version = "1.0.0",
            Title = "Gong MCP Server",
            Description = "Gong Revenue Intelligence API for Copilot Studio. Manage calls, users, stats, meetings, CRM data, permissions, engagement, and more."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Logging = true,
            Completions = true
        },
        Instructions = "Use these tools to interact with the Gong Revenue Intelligence platform. All date parameters use ISO 8601 format (e.g. 2025-01-01T00:00:00Z). Pagination uses cursor-based approach — pass the cursor from a previous response to get the next page."
    };

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpRequestAsync().ConfigureAwait(false);
            default:
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        var handler = new McpRequestHandler(Options);
        RegisterTools(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
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

    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        // ═══════════════════════════════════════════════════════════════
        // CALLS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_calls",
            "List calls in Gong by date range. Use when the user asks for recent calls, call history, or wants to find calls in a time period. Returns basic call metadata with pagination.",
            schema: s => s
                .String("fromDateTime", "Start date and time (ISO 8601, e.g. 2025-01-01T00:00:00Z)", required: true)
                .String("toDateTime", "End date and time (ISO 8601)", required: true)
                .String("workspaceId", "Optional workspace ID to filter calls")
                .String("cursor", "Pagination cursor from previous response"),
            handler: async (args, ct) =>
            {
                var qs = $"fromDateTime={U(R(args, "fromDateTime"))}&toDateTime={U(R(args, "toDateTime"))}";
                if (O(args, "workspaceId") != null) qs += $"&workspaceId={U(O(args, "workspaceId"))}";
                if (O(args, "cursor") != null) qs += $"&cursor={U(O(args, "cursor"))}";
                return await GongGetAsync($"/calls?{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_call",
            "Get details for a specific call by ID. Use when the user asks about a particular call.",
            schema: s => s.String("id", "The call ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/calls/{U(R(args, "id"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("add_call",
            "Add a new call to Gong. Use when the user wants to create/import a call record. After adding, use add_call_recording to upload the media.",
            schema: s => s
                .String("actualStart", "Actual start time of the call (ISO 8601)", required: true)
                .String("primaryUser", "Email address of the primary user (host)", required: true)
                .String("clientUniqueId", "A unique identifier for the call in your system")
                .String("direction", "Call direction", enumValues: new[] { "Inbound", "Outbound", "Conference", "Unknown" })
                .Number("duration", "Call duration in seconds")
                .String("title", "Call title")
                .String("meetingUrl", "URL of the meeting")
                .String("scheduledStart", "Scheduled start time (ISO 8601)")
                .String("scheduledEnd", "Scheduled end time (ISO 8601)")
                .String("workspaceId", "Workspace ID"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["actualStart"] = R(args, "actualStart"),
                    ["primaryUser"] = R(args, "primaryUser"),
                    ["parties"] = new JArray { new JObject { ["emailAddress"] = R(args, "primaryUser") } }
                };
                CopyIfPresent(args, body, "clientUniqueId", "direction", "duration", "title",
                    "meetingUrl", "scheduledStart", "scheduledEnd", "workspaceId");
                return await GongPostAsync("/calls", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_calls_extensive",
            "Get detailed call data including content (topics, trackers, brief, outline, highlights, key points), interaction stats (talk ratio, interactivity), collaboration (comments), and media URLs. Use when the user needs rich call insights, AI summaries, or analytics for specific calls.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)")
                .String("toDateTime", "End date (ISO 8601)")
                .String("callIds", "Comma-separated call IDs to retrieve")
                .String("workspaceId", "Workspace ID filter")
                .Boolean("includeTopics", "Include topics in response")
                .Boolean("includeTrackers", "Include trackers in response")
                .Boolean("includeBrief", "Include AI-generated brief")
                .Boolean("includeOutline", "Include call outline")
                .Boolean("includeHighlights", "Include highlights")
                .Boolean("includeKeyPoints", "Include key points")
                .Boolean("includeCallOutcome", "Include call outcome")
                .Boolean("includeInteraction", "Include interaction stats (talk ratio, patience, etc.)")
                .Boolean("includeCollaboration", "Include public comments")
                .Boolean("includeMedia", "Include audio/video URLs")
                .Boolean("includeParties", "Include participant details")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject();
                if (O(args, "callIds") != null)
                    filter["callIds"] = new JArray(O(args, "callIds").Split(',').Select(s => s.Trim()));
                if (O(args, "fromDateTime") != null) filter["fromDateTime"] = O(args, "fromDateTime");
                if (O(args, "toDateTime") != null) filter["toDateTime"] = O(args, "toDateTime");
                if (O(args, "workspaceId") != null) filter["workspaceId"] = O(args, "workspaceId");

                var body = new JObject { ["filter"] = filter };

                var content = new JObject();
                var interaction = new JObject();
                var collaboration = new JObject();
                var media = new JObject();

                if (args.Value<bool?>("includeTopics") == true) content["topics"] = true;
                if (args.Value<bool?>("includeTrackers") == true) content["trackers"] = true;
                if (args.Value<bool?>("includeBrief") == true) content["brief"] = true;
                if (args.Value<bool?>("includeOutline") == true) content["outline"] = true;
                if (args.Value<bool?>("includeHighlights") == true) content["highlights"] = true;
                if (args.Value<bool?>("includeKeyPoints") == true) content["keyPoints"] = true;
                if (args.Value<bool?>("includeCallOutcome") == true) content["callOutcome"] = true;
                if (args.Value<bool?>("includeInteraction") == true)
                {
                    interaction["interactivity"] = true;
                    interaction["talkRatio"] = true;
                    interaction["longestMonologue"] = true;
                    interaction["patience"] = true;
                    interaction["questionRate"] = true;
                }
                if (args.Value<bool?>("includeCollaboration") == true) collaboration["publicComments"] = true;
                if (args.Value<bool?>("includeMedia") == true) { media["audioUrl"] = true; media["videoUrl"] = true; }

                var exposedFields = new JObject();
                if (content.Count > 0) exposedFields["content"] = content;
                if (interaction.Count > 0) exposedFields["interaction"] = interaction;
                if (collaboration.Count > 0) exposedFields["collaboration"] = collaboration;
                if (media.Count > 0) exposedFields["media"] = media;
                if (args.Value<bool?>("includeParties") == true) exposedFields["parties"] = true;

                if (exposedFields.Count > 0)
                    body["contentSelector"] = new JObject { ["exposedFields"] = exposedFields };

                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");

                return await GongPostAsync("/calls/extensive", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_call_transcripts",
            "Get transcripts for calls. Use when the user asks for what was said in a call, call transcript, or conversation text. Returns speaker-labeled sentences with timestamps.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)")
                .String("toDateTime", "End date (ISO 8601)")
                .String("callIds", "Comma-separated call IDs")
                .String("workspaceId", "Workspace ID filter")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject();
                if (O(args, "callIds") != null)
                    filter["callIds"] = new JArray(O(args, "callIds").Split(',').Select(s => s.Trim()));
                if (O(args, "fromDateTime") != null) filter["fromDateTime"] = O(args, "fromDateTime");
                if (O(args, "toDateTime") != null) filter["toDateTime"] = O(args, "toDateTime");
                if (O(args, "workspaceId") != null) filter["workspaceId"] = O(args, "workspaceId");

                var body = new JObject { ["filter"] = filter };
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/calls/transcript", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("give_users_access_to_calls",
            "Grant specific users access to specific calls. Use when the user wants to share call access with team members.",
            schema: s => s
                .String("callId", "The call ID to grant access to", required: true)
                .String("userIds", "Comma-separated user IDs to grant access", required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["callAccessList"] = new JArray
                    {
                        new JObject
                        {
                            ["callId"] = R(args, "callId"),
                            ["userIds"] = new JArray(R(args, "userIds").Split(',').Select(s => s.Trim()))
                        }
                    }
                };
                return await GongRequestAsync(HttpMethod.Put, "/calls/users-access", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("remove_users_access_from_calls",
            "Remove specific users' access from specific calls.",
            schema: s => s
                .String("callId", "The call ID to remove access from", required: true)
                .String("userIds", "Comma-separated user IDs to remove access", required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["callAccessList"] = new JArray
                    {
                        new JObject
                        {
                            ["callId"] = R(args, "callId"),
                            ["userIds"] = new JArray(R(args, "userIds").Split(',').Select(s => s.Trim()))
                        }
                    }
                };
                return await GongRequestAsync(HttpMethod.Delete, "/calls/users-access", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ═══════════════════════════════════════════════════════════════
        // USERS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_users",
            "List all users in the Gong account. Use when the user asks for team members, reps, or user lookup.",
            schema: s => s
                .String("cursor", "Pagination cursor")
                .Boolean("includeAvatars", "Whether to include avatar URLs"),
            handler: async (args, ct) =>
            {
                var qs = "";
                if (O(args, "cursor") != null) qs += $"cursor={U(O(args, "cursor"))}&";
                if (args.Value<bool?>("includeAvatars") == true) qs += "includeAvatars=true&";
                return await GongGetAsync($"/users{(qs.Length > 0 ? "?" + qs.TrimEnd('&') : "")}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_user",
            "Get details for a specific user by ID.",
            schema: s => s.String("id", "The user ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/users/{U(R(args, "id"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_user_settings_history",
            "Get the settings change history for a specific user.",
            schema: s => s.String("id", "The user ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/users/{U(R(args, "id"))}/settings-history");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_users_extensive",
            "Get extensive user data for specific users or the entire account. Use when the user needs detailed user profiles with settings.",
            schema: s => s
                .String("userIds", "Comma-separated user IDs (optional, omit for all users)")
                .String("createdFromDateTime", "Filter users created from this date (ISO 8601)")
                .String("createdToDateTime", "Filter users created to this date (ISO 8601)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject();
                if (O(args, "userIds") != null)
                    filter["userIds"] = new JArray(O(args, "userIds").Split(',').Select(s => s.Trim()));
                if (O(args, "createdFromDateTime") != null) filter["createdFromDateTime"] = O(args, "createdFromDateTime");
                if (O(args, "createdToDateTime") != null) filter["createdToDateTime"] = O(args, "createdToDateTime");

                var body = new JObject();
                if (filter.Count > 0) body["filter"] = filter;
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/users/extensive", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // STATS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("get_scorecard_stats",
            "Get scorecard review statistics for calls. Use when the user asks about call scores, reviews, or coaching feedback.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)", required: true)
                .String("toDateTime", "End date (ISO 8601)", required: true)
                .String("callIds", "Comma-separated call IDs (optional)")
                .String("scorecardIds", "Comma-separated scorecard IDs (optional)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject
                {
                    ["fromDateTime"] = R(args, "fromDateTime"),
                    ["toDateTime"] = R(args, "toDateTime")
                };
                if (O(args, "callIds") != null)
                    filter["callIds"] = new JArray(O(args, "callIds").Split(',').Select(s => s.Trim()));
                if (O(args, "scorecardIds") != null)
                    filter["scorecardIds"] = new JArray(O(args, "scorecardIds").Split(',').Select(s => s.Trim()));

                var body = new JObject { ["filter"] = filter };
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/stats/activity/scorecards", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_activity_day_by_day",
            "Get day-by-day activity statistics for users. Use when the user asks for daily activity breakdowns, daily call counts, or coaching activity over time.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)", required: true)
                .String("toDateTime", "End date (ISO 8601)", required: true)
                .String("userIds", "Comma-separated user IDs (optional)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject
                {
                    ["fromDateTime"] = R(args, "fromDateTime"),
                    ["toDateTime"] = R(args, "toDateTime")
                };
                if (O(args, "userIds") != null)
                    filter["userIds"] = new JArray(O(args, "userIds").Split(',').Select(s => s.Trim()));

                var body = new JObject { ["filter"] = filter };
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/stats/activity/day-by-day", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_aggregate_activity",
            "Get aggregated activity statistics for users across a date range. Use when the user wants a summary of total calls hosted, attended, feedback given, etc.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)", required: true)
                .String("toDateTime", "End date (ISO 8601)", required: true)
                .String("userIds", "Comma-separated user IDs (optional)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject
                {
                    ["fromDateTime"] = R(args, "fromDateTime"),
                    ["toDateTime"] = R(args, "toDateTime")
                };
                if (O(args, "userIds") != null)
                    filter["userIds"] = new JArray(O(args, "userIds").Split(',').Select(s => s.Trim()));

                var body = new JObject { ["filter"] = filter };
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/stats/activity/aggregate", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_aggregate_activity_by_period",
            "Get aggregated activity statistics broken down by time period (week, month, or quarter). Use for trend analysis.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)", required: true)
                .String("toDateTime", "End date (ISO 8601)", required: true)
                .String("aggregateBy", "Time period to aggregate by", enumValues: new[] { "Week", "Month", "Quarter" })
                .String("userIds", "Comma-separated user IDs (optional)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject
                {
                    ["fromDateTime"] = R(args, "fromDateTime"),
                    ["toDateTime"] = R(args, "toDateTime")
                };
                if (O(args, "userIds") != null)
                    filter["userIds"] = new JArray(O(args, "userIds").Split(',').Select(s => s.Trim()));

                var body = new JObject { ["filter"] = filter };
                if (O(args, "aggregateBy") != null) body["aggregateBy"] = O(args, "aggregateBy");
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/stats/activity/aggregate-by-period", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_interaction_stats",
            "Get interaction statistics (talk ratio, interactivity, longest monologue, patience, question rate) for calls. Use when the user asks about conversation dynamics or coaching metrics.",
            schema: s => s
                .String("fromDateTime", "Start date (ISO 8601)", required: true)
                .String("toDateTime", "End date (ISO 8601)", required: true)
                .String("callIds", "Comma-separated call IDs (optional)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var filter = new JObject
                {
                    ["fromDateTime"] = R(args, "fromDateTime"),
                    ["toDateTime"] = R(args, "toDateTime")
                };
                if (O(args, "callIds") != null)
                    filter["callIds"] = new JArray(O(args, "callIds").Split(',').Select(s => s.Trim()));

                var body = new JObject { ["filter"] = filter };
                if (O(args, "cursor") != null) body["cursor"] = O(args, "cursor");
                return await GongPostAsync("/stats/interaction", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // SETTINGS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_scorecards",
            "List all configured scorecards. Use when the user asks about scorecards, review templates, or call evaluation criteria.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await GongGetAsync("/settings/scorecards");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("list_workspaces",
            "List all workspaces in the Gong account. Use when the user asks about workspaces or teams.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await GongGetAsync("/workspaces");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // LIBRARY
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_library_folders",
            "List all library folders. Use when the user asks about call libraries, saved collections, or content folders.",
            schema: s => s.String("workspaceId", "Optional workspace ID to filter folders"),
            handler: async (args, ct) =>
            {
                var qs = O(args, "workspaceId") != null ? $"?workspaceId={U(O(args, "workspaceId"))}" : "";
                return await GongGetAsync($"/library/folders{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_library_folder_content",
            "Get the content of a specific library folder. Returns the calls saved in the folder.",
            schema: s => s.String("folderId", "The library folder ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/library/folder-content?folderId={U(R(args, "folderId"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // MEETINGS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("create_meeting",
            "Create a new meeting in Gong (Beta). Use when the user wants to register an upcoming meeting for Gong recording.",
            schema: s => s
                .String("organizerEmail", "Email of the meeting organizer", required: true)
                .String("scheduledStart", "Scheduled start time (ISO 8601)", required: true)
                .String("scheduledEnd", "Scheduled end time (ISO 8601)", required: true)
                .String("title", "Meeting title")
                .String("meetingUrl", "URL of the meeting")
                .String("externalMeetingId", "External meeting identifier")
                .String("workspaceId", "Workspace ID"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["organizerEmail"] = R(args, "organizerEmail"),
                    ["scheduledStart"] = R(args, "scheduledStart"),
                    ["scheduledEnd"] = R(args, "scheduledEnd")
                };
                CopyIfPresent(args, body, "title", "meetingUrl", "externalMeetingId", "workspaceId");
                return await GongPostAsync("/meetings", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_meeting",
            "Update an existing meeting in Gong (Beta).",
            schema: s => s
                .String("meetingId", "The meeting ID", required: true)
                .String("title", "Meeting title")
                .String("scheduledStart", "Scheduled start time (ISO 8601)")
                .String("scheduledEnd", "Scheduled end time (ISO 8601)")
                .String("meetingUrl", "URL of the meeting"),
            handler: async (args, ct) =>
            {
                var body = new JObject();
                CopyIfPresent(args, body, "title", "scheduledStart", "scheduledEnd", "meetingUrl");
                return await GongRequestAsync(HttpMethod.Put, $"/meetings/{U(R(args, "meetingId"))}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("delete_meeting",
            "Delete an existing meeting in Gong (Beta).",
            schema: s => s.String("meetingId", "The meeting ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongRequestAsync(HttpMethod.Delete, $"/meetings/{U(R(args, "meetingId"))}");
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("validate_meeting_integration",
            "Validate the status of a meeting integration (Beta).",
            schema: s => s
                .String("integrationId", "The meeting integration ID", required: true)
                .String("status", "Integration status"),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["integrationId"] = R(args, "integrationId") };
                if (O(args, "status") != null) body["status"] = O(args, "status");
                return await GongPostAsync("/meetings/integration/status", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // DATA PRIVACY
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("get_data_for_email_address",
            "Get all data associated with a specific email address for data privacy / GDPR purposes.",
            schema: s => s.String("emailAddress", "The email address to look up", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/data-privacy/data-for-email-address?emailAddress={U(R(args, "emailAddress"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_data_for_phone_number",
            "Get all data associated with a specific phone number for data privacy / GDPR purposes.",
            schema: s => s.String("phoneNumber", "The phone number to look up", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/data-privacy/data-for-phone-number?phoneNumber={U(R(args, "phoneNumber"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("erase_data_for_email_address",
            "Purge all data associated with a specific email address. WARNING: This action cannot be undone.",
            schema: s => s.String("emailAddress", "The email address to purge data for", required: true),
            handler: async (args, ct) =>
            {
                return await GongPostAsync($"/data-privacy/erase-data-for-email-address?emailAddress={U(R(args, "emailAddress"))}", new JObject());
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("erase_data_for_phone_number",
            "Purge all data associated with a specific phone number. WARNING: This action cannot be undone.",
            schema: s => s.String("phoneNumber", "The phone number to purge data for", required: true),
            handler: async (args, ct) =>
            {
                return await GongPostAsync($"/data-privacy/erase-data-for-phone-number?phoneNumber={U(R(args, "phoneNumber"))}", new JObject());
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // CRM
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("get_crm_objects",
            "Retrieve CRM objects (accounts, contacts, deals, leads) uploaded to Gong.",
            schema: s => s
                .Integer("integrationId", "The CRM integration identifier", required: true)
                .String("objectType", "The type of CRM object", required: true, enumValues: new[] { "ACCOUNT", "CONTACT", "DEAL", "LEAD" })
                .String("objectsCrmIds", "Comma-separated CRM IDs of objects to retrieve"),
            handler: async (args, ct) =>
            {
                var qs = $"integrationId={args.Value<int>("integrationId")}&objectType={U(R(args, "objectType"))}";
                if (O(args, "objectsCrmIds") != null)
                {
                    foreach (var id in O(args, "objectsCrmIds").Split(',').Select(s => s.Trim()))
                        qs += $"&objectsCrmIds={U(id)}";
                }
                return await GongGetAsync($"/crm/entities?{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("register_crm_integration",
            "Register or update a CRM integration with Gong.",
            schema: s => s
                .String("integrationName", "Name for the CRM integration", required: true)
                .String("crmSystemType", "The CRM system type", required: true),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["integrationName"] = R(args, "integrationName"),
                    ["crmSystemType"] = R(args, "crmSystemType")
                };
                return await GongRequestAsync(HttpMethod.Put, "/crm/integrations", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_crm_integration",
            "Get details for a CRM integration.",
            schema: s => s.Integer("integrationId", "The CRM integration ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/crm/integrations?integrationId={args.Value<int>("integrationId")}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("delete_crm_integration",
            "Delete a CRM integration and all its data from Gong. WARNING: This action cannot be undone.",
            schema: s => s.Integer("integrationId", "The CRM integration ID to delete", required: true),
            handler: async (args, ct) =>
            {
                return await GongRequestAsync(HttpMethod.Delete, $"/crm/integrations?integrationId={args.Value<int>("integrationId")}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("set_crm_entity_schema",
            "Define the schema for a CRM entity type in an integration.",
            schema: s => s
                .Integer("integrationId", "The CRM integration ID", required: true)
                .String("objectType", "The type of CRM object", required: true, enumValues: new[] { "ACCOUNT", "CONTACT", "DEAL", "LEAD", "USER", "BUSINESS_USER", "STAGE" })
                .String("fieldsJson", "JSON array of field definitions: [{name, uniqueId, label, type (STRING/NUMBER/DATE/BOOLEAN/REFERENCE), referenceTo?, orderedValues?}]", required: true),
            handler: async (args, ct) =>
            {
                var fields = JArray.Parse(R(args, "fieldsJson"));
                var body = new JObject { ["fields"] = fields };
                var qs = $"integrationId={args.Value<int>("integrationId")}&objectType={U(R(args, "objectType"))}";
                return await GongRequestAsync(HttpMethod.Put, $"/crm/entity-schema?{qs}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_crm_entity_schema",
            "Retrieve the schema for a CRM entity type.",
            schema: s => s
                .Integer("integrationId", "The CRM integration ID", required: true)
                .String("objectType", "The type of CRM object", required: true, enumValues: new[] { "ACCOUNT", "CONTACT", "DEAL", "LEAD", "USER", "BUSINESS_USER", "STAGE" }),
            handler: async (args, ct) =>
            {
                var qs = $"integrationId={args.Value<int>("integrationId")}&objectType={U(R(args, "objectType"))}";
                return await GongGetAsync($"/crm/entity-schema?{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_crm_request_status",
            "Get the status of a CRM upload request.",
            schema: s => s
                .Integer("integrationId", "The CRM integration ID", required: true)
                .String("clientRequestId", "The client request ID from the upload", required: true),
            handler: async (args, ct) =>
            {
                var qs = $"integrationId={args.Value<int>("integrationId")}&clientRequestId={U(R(args, "clientRequestId"))}";
                return await GongGetAsync($"/crm/request-status?{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // ENGAGEMENT
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("report_content_shared",
            "Report a content-shared engagement event to Gong (Beta). Use when tracking content sharing activity.",
            schema: s => s
                .String("reportingSystem", "Name of the system reporting the event", required: true)
                .String("companyUserEmail", "Email of the user who shared content", required: true)
                .String("contentTitle", "Title of the shared content", required: true)
                .String("engagementTimestamp", "When the event occurred (ISO 8601)")
                .String("contentId", "Content identifier")
                .String("contentUrl", "URL of the shared content")
                .String("workspaceId", "Workspace ID"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["reportingSystem"] = R(args, "reportingSystem"),
                    ["companyUserEmail"] = R(args, "companyUserEmail"),
                    ["contentTitle"] = R(args, "contentTitle")
                };
                CopyIfPresent(args, body, "engagementTimestamp", "contentId", "contentUrl", "workspaceId");
                return await GongRequestAsync(HttpMethod.Put, "/customer-engagement/content/shared", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("report_content_viewed",
            "Report a content-viewed engagement event to Gong (Beta). Use when tracking content consumption.",
            schema: s => s
                .String("reportingSystem", "Name of the system reporting the event", required: true)
                .String("contentTitle", "Title of the viewed content", required: true)
                .String("engagementTimestamp", "When the event occurred (ISO 8601)")
                .String("contentId", "Content identifier")
                .String("contentUrl", "URL of the viewed content")
                .String("workspaceId", "Workspace ID"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["reportingSystem"] = R(args, "reportingSystem"),
                    ["contentTitle"] = R(args, "contentTitle")
                };
                CopyIfPresent(args, body, "engagementTimestamp", "contentId", "contentUrl", "workspaceId");
                return await GongRequestAsync(HttpMethod.Put, "/customer-engagement/content/viewed", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("report_custom_action",
            "Report a custom engagement action event to Gong (Beta).",
            schema: s => s
                .String("reportingSystem", "Name of the system reporting the event", required: true)
                .String("companyUserEmail", "Email of the company user", required: true)
                .String("contentTitle", "Title of the related content", required: true)
                .String("actionName", "Name of the custom action", required: true)
                .String("engagementTimestamp", "When the event occurred (ISO 8601)")
                .String("workspaceId", "Workspace ID"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["reportingSystem"] = R(args, "reportingSystem"),
                    ["companyUserEmail"] = R(args, "companyUserEmail"),
                    ["contentTitle"] = R(args, "contentTitle"),
                    ["actionName"] = R(args, "actionName")
                };
                CopyIfPresent(args, body, "engagementTimestamp", "workspaceId");
                return await GongRequestAsync(HttpMethod.Put, "/customer-engagement/action", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ═══════════════════════════════════════════════════════════════
        // AUDITING
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_logs",
            "Retrieve audit logs from Gong. Types: AccessLog, UserActivityLog, UserCallPlay, ExternallySharedCallAccess, ExternallySharedCallPlay.",
            schema: s => s
                .String("logType", "The type of log to retrieve", required: true, enumValues: new[] { "AccessLog", "UserActivityLog", "UserCallPlay", "ExternallySharedCallAccess", "ExternallySharedCallPlay" })
                .String("fromDateTime", "Start date (ISO 8601)")
                .String("toDateTime", "End date (ISO 8601)")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var qs = $"logType={U(R(args, "logType"))}";
                if (O(args, "fromDateTime") != null) qs += $"&fromDateTime={U(O(args, "fromDateTime"))}";
                if (O(args, "toDateTime") != null) qs += $"&toDateTime={U(O(args, "toDateTime"))}";
                if (O(args, "cursor") != null) qs += $"&cursor={U(O(args, "cursor"))}";
                return await GongGetAsync($"/logs?{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // PERMISSIONS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_permission_profiles",
            "List all permission profiles for a workspace.",
            schema: s => s.String("workspaceId", "The workspace ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/all-permission-profiles?workspaceId={U(R(args, "workspaceId"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_permission_profile",
            "Get a specific permission profile by ID.",
            schema: s => s.String("permissionProfileId", "The permission profile ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/permission-profile?permissionProfileId={U(R(args, "permissionProfileId"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("create_permission_profile",
            "Create a new permission profile.",
            schema: s => s
                .String("name", "Profile name", required: true)
                .String("workspaceId", "Workspace ID", required: true)
                .String("callsAccessLevel", "Level of access to calls")
                .String("libraryFolderAccess", "Level of access to library folders")
                .Boolean("canViewPrivateCalls", "Can view private calls")
                .Boolean("canEditCallDetails", "Can edit call details")
                .Boolean("canDeleteCalls", "Can delete calls")
                .Boolean("canShareCallsExternally", "Can share calls externally")
                .Boolean("canShareCallsInternally", "Can share calls internally")
                .Boolean("canCreateLibraryFolders", "Can create library folders")
                .Boolean("canManageTeamMembers", "Can manage team members")
                .Boolean("canViewScorecards", "Can view scorecards")
                .Boolean("canScoreCalls", "Can score calls")
                .Boolean("canViewDealBoard", "Can view deal board")
                .Boolean("canViewForecasting", "Can view forecasting"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["name"] = R(args, "name"),
                    ["workspaceId"] = R(args, "workspaceId")
                };
                CopyIfPresent(args, body, "callsAccessLevel", "libraryFolderAccess");
                CopyBoolIfPresent(args, body, "canViewPrivateCalls", "canEditCallDetails", "canDeleteCalls",
                    "canShareCallsExternally", "canShareCallsInternally", "canCreateLibraryFolders",
                    "canManageTeamMembers", "canViewScorecards", "canScoreCalls",
                    "canViewDealBoard", "canViewForecasting");
                return await GongPostAsync("/permission-profile", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_permission_profile",
            "Update an existing permission profile.",
            schema: s => s
                .String("permissionProfileId", "Profile ID to update", required: true)
                .String("name", "Profile name", required: true)
                .String("workspaceId", "Workspace ID", required: true)
                .String("callsAccessLevel", "Level of access to calls")
                .String("libraryFolderAccess", "Level of access to library folders")
                .Boolean("canViewPrivateCalls", "Can view private calls")
                .Boolean("canEditCallDetails", "Can edit call details")
                .Boolean("canDeleteCalls", "Can delete calls")
                .Boolean("canShareCallsExternally", "Can share calls externally")
                .Boolean("canShareCallsInternally", "Can share calls internally")
                .Boolean("canCreateLibraryFolders", "Can create library folders")
                .Boolean("canManageTeamMembers", "Can manage team members")
                .Boolean("canViewScorecards", "Can view scorecards")
                .Boolean("canScoreCalls", "Can score calls")
                .Boolean("canViewDealBoard", "Can view deal board")
                .Boolean("canViewForecasting", "Can view forecasting"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["permissionProfileId"] = R(args, "permissionProfileId"),
                    ["name"] = R(args, "name"),
                    ["workspaceId"] = R(args, "workspaceId")
                };
                CopyIfPresent(args, body, "callsAccessLevel", "libraryFolderAccess");
                CopyBoolIfPresent(args, body, "canViewPrivateCalls", "canEditCallDetails", "canDeleteCalls",
                    "canShareCallsExternally", "canShareCallsInternally", "canCreateLibraryFolders",
                    "canManageTeamMembers", "canViewScorecards", "canScoreCalls",
                    "canViewDealBoard", "canViewForecasting");
                return await GongRequestAsync(HttpMethod.Put, "/permission-profile", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("list_permission_profile_users",
            "List users assigned to a specific permission profile.",
            schema: s => s.String("permissionProfileId", "The permission profile ID", required: true),
            handler: async (args, ct) =>
            {
                return await GongGetAsync($"/permission-profile/users?permissionProfileId={U(R(args, "permissionProfileId"))}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        // ═══════════════════════════════════════════════════════════════
        // FLOWS (Engage)
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("list_flows",
            "List Engage flows for a user (Alpha). Use when the user asks about sales sequences or outreach flows.",
            schema: s => s
                .String("flowOwnerEmail", "Email of the flow owner", required: true)
                .String("workspaceId", "Workspace ID")
                .String("cursor", "Pagination cursor"),
            handler: async (args, ct) =>
            {
                var qs = $"flowOwnerEmail={U(R(args, "flowOwnerEmail"))}";
                if (O(args, "workspaceId") != null) qs += $"&workspaceId={U(O(args, "workspaceId"))}";
                if (O(args, "cursor") != null) qs += $"&cursor={U(O(args, "cursor"))}";
                return await GongGetAsync($"/flows?{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("get_flows_for_prospects",
            "Get flows associated with specific prospects (Alpha).",
            schema: s => s
                .String("prospectsCrmIds", "Comma-separated CRM IDs of the prospects", required: true)
                .String("gongFlowId", "Optional flow ID to filter"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["prospectsCrmIds"] = new JArray(R(args, "prospectsCrmIds").Split(',').Select(s => s.Trim()))
                };
                if (O(args, "gongFlowId") != null) body["gongFlowId"] = O(args, "gongFlowId");
                return await GongPostAsync("/flows/prospects", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("assign_flow_to_prospects",
            "Assign prospects to an Engage flow (Alpha). Use when the user wants to add contacts to a sales sequence.",
            schema: s => s
                .String("prospectsCrmIds", "Comma-separated CRM IDs of the prospects", required: true)
                .String("gongFlowId", "The flow ID to assign", required: true)
                .String("assignToEmail", "Email of the user to assign the flow to", required: true)
                .Integer("crmIntegrationId", "CRM integration ID"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["prospectsCrmIds"] = new JArray(R(args, "prospectsCrmIds").Split(',').Select(s => s.Trim())),
                    ["gongFlowId"] = R(args, "gongFlowId"),
                    ["assignToEmail"] = R(args, "assignToEmail")
                };
                if (args.Value<int?>("crmIntegrationId") != null)
                    body["crmIntegrationId"] = args.Value<int>("crmIntegrationId");
                return await GongPostAsync("/flows/prospects/assign", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ═══════════════════════════════════════════════════════════════
        // DIGITAL INTERACTIONS
        // ═══════════════════════════════════════════════════════════════

        handler.AddTool("add_digital_interaction",
            "Upload digital interaction events to Gong (Alpha). Use when importing external engagement data like chat, email, or web interactions.",
            schema: s => s
                .String("sourceSystemName", "Name of the source system", required: true)
                .String("eventsJson", "JSON array of events: [{companyUserEmail, timestamp (ISO 8601), eventType, sessionId?, nonCompanyParticipants?: [{email, name}], content?: {title, url}}]", required: true),
            handler: async (args, ct) =>
            {
                var events = JArray.Parse(R(args, "eventsJson"));
                var body = new JObject
                {
                    ["sourceSystemName"] = R(args, "sourceSystemName"),
                    ["events"] = events
                };
                return await GongPostAsync("/digital-interaction", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });
    }

    // ── Gong API Helpers ─────────────────────────────────────────────────

    private async Task<JObject> GongGetAsync(string path)
    {
        return await GongRequestAsync(HttpMethod.Get, path);
    }

    private async Task<JObject> GongPostAsync(string path, JObject body)
    {
        return await GongRequestAsync(HttpMethod.Post, path, body);
    }

    private async Task<JObject> GongRequestAsync(HttpMethod method, string path, JObject body = null)
    {
        var request = new HttpRequestMessage(method, $"{GONG_BASE}{path}");

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Gong API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    // ── Argument Helpers ─────────────────────────────────────────────────

    /// <summary>Get a required argument; throws if missing.</summary>
    private static string R(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an optional argument.</summary>
    private static string O(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>URI-encode a string.</summary>
    private static string U(string value) => Uri.EscapeDataString(value);

    /// <summary>Copy string properties from args to body if present.</summary>
    private static void CopyIfPresent(JObject args, JObject body, params string[] keys)
    {
        foreach (var key in keys)
        {
            var val = O(args, key);
            if (val != null) body[key] = val;
        }
    }

    /// <summary>Copy boolean properties from args to body if present.</summary>
    private static void CopyBoolIfPresent(JObject args, JObject body, params string[] keys)
    {
        foreach (var key in keys)
        {
            var val = args.Value<bool?>(key);
            if (val.HasValue) body[key] = val.Value;
        }
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
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
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
        catch
        {
            // Suppress telemetry errors
        }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

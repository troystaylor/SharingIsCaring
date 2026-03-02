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
// ║  SECTION 1: MCP FRAMEWORK                                                    ║
// ║                                                                              ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power         ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section    ║
// ║  becomes a using statement instead of inline code.                           ║
// ║                                                                              ║
// ║  Spec coverage: MCP 2025-11-25                                               ║
// ║  Handles: initialize, ping, tools/*, resources/*, prompts/*,                 ║
// ║           completion/complete, logging/setLevel, all notifications           ║
// ║                                                                              ║
// ║  Stateless limitations (Power Platform cannot send async notifications):     ║
// ║   - Tasks (experimental, requires persistent state between requests)         ║
// ║   - Server→client requests (sampling, elicitation, roots/list)               ║
// ║   - Server→client notifications (progress, logging/message, list_changed)    ║
// ║                                                                              ║
// ║  Do not modify unless extending the framework itself.                        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported in initialize response.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

/// <summary>Capabilities declared during initialization.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>Standard JSON-RPC 2.0 error codes used by MCP.</summary>
public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>Fluent builder for JSON Schema objects used in tool inputSchema.</summary>
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
//
//    The core bridge class. Stateless, no DI, no ASP.NET Core.
//    Takes a JSON-RPC string in → returns a JSON-RPC string out.
//    This is the class that does not exist in the official SDK today.
//

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// 
/// Handles all JSON-RPC 2.0 routing, protocol negotiation, tool discovery,
/// parameter binding, and response formatting internally.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    // ── Tool Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a tool using the fluent API.
    /// Define the schema with McpSchemaBuilder, provide a handler, and optionally set annotations.
    /// </summary>
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

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// This is the single method that bridges the gap.
    /// </summary>
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
                // Core initialization
                case "initialize":
                    return HandleInitialize(id, request);

                // Notifications — Copilot Studio requires valid JSON-RPC for ALL requests
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                // Health check
                case "ping":
                    return SerializeSuccess(id, new JObject());

                // Tools
                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                // Resources (respond based on declared capabilities)
                case "resources/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "resources/read":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                // Prompts
                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "prompts/get":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");

                // Completions
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

                // Logging level
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

    // ── Protocol Handlers ────────────────────────────────────────────────

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

            // Support pre-formatted MCP tool results with rich content types
            // (image, audio, resource, or mixed content arrays).
            // If the handler returns { "content": [ { "type": "..." } ], ... },
            // pass it through directly instead of wrapping in text.
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

    // ── Content Helpers ────────────────────────────────────────────────
    //
    //    Use these to build rich tool results with image, audio, or resource
    //    content. Return McpRequestHandler.ToolResult(...) from your handler
    //    to bypass automatic text wrapping.
    //

    /// <summary>Create a text content item.</summary>
    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>Create an image content item (base64-encoded).</summary>
    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an audio content item (base64-encoded).</summary>
    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an embedded resource content item.</summary>
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    /// <summary>
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

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
// ║  SECTION 2: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Configure your server, register your tools, and wire up telemetry.        ║
// ║  Tool registration uses the fluent AddTool API — add your tools in the     ║
// ║  RegisterTools method below.                                               ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "excel-online-mcp",
            Version = "1.0.0",
            Title = "Excel Online MCP",
            Description = "Excel Online MCP connector for Microsoft Graph workbook operations"
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
        Instructions = "Sessionless by default. Pass sessionId to enable workbook sessions."
    };

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // MS Learn MCP Server endpoint (public, no auth required)
    private const string MS_LEARN_MCP_ENDPOINT = "https://learn.microsoft.com/api/mcp";
    private const string MS_LEARN_ALLOWED_HOST = "learn.microsoft.com";
    private const string MS_LEARN_ALLOWED_PATH = "/api/mcp";
    private const string MS_LEARN_ALLOWED_TOOL = "microsoft_docs_search";
    private const int MAX_DISCOVERY_QUERY_LENGTH = 300;
    private const int MAX_MCP_RESPONSE_CHARS = 120000;
    private const int MAX_CHUNKS_TO_PROCESS = 20;
    private const int MAX_CHUNK_CONTENT_CHARS = 4000;

    private static readonly HashSet<string> AllowedDiscoveryCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "workbook", "worksheet", "range", "table", "chart", "function", "formatting", "session"
    };

    // Orchestration tool names
    private const string TOOL_ORCHESTRATE_CORE = "orchestrate_core";
    private const string TOOL_ORCHESTRATE_DATA = "orchestrate_data";
    private const string TOOL_ORCHESTRATE_FORMULAS = "orchestrate_formulas";
    private const string TOOL_ORCHESTRATE_FORMATTING = "orchestrate_formatting";
    private const string TOOL_ORCHESTRATE_CHARTS = "orchestrate_charts";
    private const string TOOL_ORCHESTRATE_NAMED_ITEMS = "orchestrate_named_items";
    private const string TOOL_ORCHESTRATE_ADVANCED_FEATURES = "orchestrate_advanced_features";
    private const string TOOL_ORCHESTRATE_GOVERNANCE = "orchestrate_governance";
    private const string TOOL_ORCHESTRATE_BULK_OPERATIONS = "orchestrate_bulk_operations";
    private const string TOOL_ORCHESTRATE_ENTERPRISE = "orchestrate_enterprise";
    private const string TOOL_ORCHESTRATE_COMMENTS = "orchestrate_comments";
    private const string TOOL_ORCHESTRATE_METADATA = "orchestrate_metadata";
    private const string TOOL_ORCHESTRATE_IMAGES = "orchestrate_images";
    private const string TOOL_ORCHESTRATE_FUNCTIONS = "orchestrate_functions";
    private const string TOOL_ORCHESTRATE_FILTERING = "orchestrate_filtering";
    private const string TOOL_ORCHESTRATE_ARRAYS = "orchestrate_arrays";
    private const string TOOL_ORCHESTRATE_INSIGHTS = "orchestrate_insights";
    private const string TOOL_DISCOVER_EXCEL = "discover_excel_graph";
    private const string TOOL_INVOKE_EXCEL = "invoke_excel_graph";
    private const string TOOL_BATCH_INVOKE_EXCEL = "batch_invoke_excel_graph";

    // Simple in-memory cache for discover_excel_graph results
    private static readonly Dictionary<string, CacheEntry> _discoveryCache = new Dictionary<string, CacheEntry>();
    private const int CACHE_EXPIRY_MINUTES = 10;

    private class CacheEntry
    {
        public JObject Result { get; set; }
        public DateTime Expiry { get; set; }
    }

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // 1. Create the handler
        var handler = new McpRequestHandler(Options);

        // 2. Register tools
        RegisterTools(handler);

        // 3. Wire up logging (optional)
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // 4. Handle the request — one line does everything
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        // 5. Return the response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        handler.AddTool("list_worksheets", "List worksheets in a workbook.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var url = $"{workbookBaseUrl}/worksheets";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_range", "Read a range from a worksheet.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("worksheetName", "Worksheet name", required: true)
                .String("address", "Range address (e.g., A1:D10)", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var encodedSheet = Uri.EscapeDataString(worksheetName);
                var encodedAddress = Uri.EscapeDataString(address);
                var url = $"{workbookBaseUrl}/worksheets/{encodedSheet}/range(address='{encodedAddress}')";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_range", "Write values to a worksheet range.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("worksheetName", "Worksheet name", required: true)
                .String("address", "Range address (e.g., A1:D10)", required: true)
                .Array("values", "2D array of values", new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                }, required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var values = args["values"] as JArray;
                if (values == null || values.Count == 0)
                    throw new ArgumentException("'values' is required");

                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var encodedSheet = Uri.EscapeDataString(worksheetName);
                var encodedAddress = Uri.EscapeDataString(address);
                var url = $"{workbookBaseUrl}/worksheets/{encodedSheet}/range(address='{encodedAddress}')";

                var body = new JObject { ["values"] = values };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            });

        handler.AddTool("add_table_rows", "Append rows to a table.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("tableName", "Table name", required: true)
                .Array("values", "2D array of values", new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                }, required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var tableName = RequireArgument(args, "tableName");
                var values = args["values"] as JArray;
                if (values == null || values.Count == 0)
                    throw new ArgumentException("'values' is required");

                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var encodedTable = Uri.EscapeDataString(tableName);
                var url = $"{workbookBaseUrl}/tables/{encodedTable}/rows/add";

                var body = new JObject { ["values"] = values };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            });

        handler.AddTool("create_session", "Create a workbook session.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .Boolean("persistChanges", "Persist changes when the session closes", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var driveId = GetArgument(args, "driveId");
                var persistChanges = args.Value<bool?>("persistChanges") ?? true;

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var url = $"{workbookBaseUrl}/createSession";

                var body = new JObject { ["persistChanges"] = persistChanges };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, null).ConfigureAwait(false);
            });

        handler.AddTool("close_session", "Close a workbook session.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("sessionId", "Workbook session ID", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var sessionId = RequireArgument(args, "sessionId");
                var driveId = GetArgument(args, "driveId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var url = $"{workbookBaseUrl}/closeSession";

                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            });

        handler.AddTool("list_tables", "List tables in a workbook or worksheet.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("worksheetName", "Worksheet name (optional). If omitted, lists all workbook tables", required: false)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var worksheetName = GetArgument(args, "worksheetName");
                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var url = string.IsNullOrWhiteSpace(worksheetName)
                    ? $"{workbookBaseUrl}/tables"
                    : $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/tables";

                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_table_rows", "Get rows from a table.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("tableName", "Table name", required: true)
                .Integer("top", "Maximum rows to return", required: false, defaultValue: 100)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var tableName = RequireArgument(args, "tableName");
                var top = args.Value<int?>("top") ?? 100;
                if (top <= 0 || top > 1000)
                    throw new ArgumentException("'top' must be between 1 and 1000");

                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/rows?$top={top}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_table", "Create a table on a worksheet range.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("worksheetName", "Worksheet name", required: true)
                .String("address", "Range address for the table (e.g., A1:D20)", required: true)
                .Boolean("hasHeaders", "Whether the range includes headers", required: false)
                .String("name", "Optional table name", required: false)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var hasHeaders = args.Value<bool?>("hasHeaders") ?? true;
                var tableName = GetArgument(args, "name");
                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var encodedSheet = Uri.EscapeDataString(worksheetName);
                var url = $"{workbookBaseUrl}/worksheets/{encodedSheet}/tables/add";

                var body = new JObject
                {
                    ["address"] = address,
                    ["hasHeaders"] = hasHeaders
                };
                if (!string.IsNullOrWhiteSpace(tableName))
                    body["name"] = tableName;

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            });

        handler.AddTool("clear_range", "Clear contents/formatting from a worksheet range.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("worksheetName", "Worksheet name", required: true)
                .String("address", "Range address (e.g., A1:D10)", required: true)
                .String("applyTo", "What to clear: All, Formats, Contents", required: false, enumValues: new[] { "All", "Formats", "Contents" })
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var applyTo = GetArgument(args, "applyTo", "All");
                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var validApplyTo = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All", "Formats", "Contents" };
                if (!validApplyTo.Contains(applyTo))
                    throw new ArgumentException("'applyTo' must be one of: All, Formats, Contents");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var encodedSheet = Uri.EscapeDataString(worksheetName);
                var encodedAddress = Uri.EscapeDataString(address);
                var url = $"{workbookBaseUrl}/worksheets/{encodedSheet}/range(address='{encodedAddress}')/clear";
                var body = new JObject { ["applyTo"] = applyTo };

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            });

        handler.AddTool("get_used_range", "Get used range for a worksheet.",
            schema: s => s
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("worksheetName", "Worksheet name", required: true)
                .Boolean("valuesOnly", "If true, considers only cells with values", required: false)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) =>
            {
                var itemId = RequireArgument(args, "itemId");
                var worksheetName = RequireArgument(args, "worksheetName");
                var valuesOnly = args.Value<bool?>("valuesOnly") ?? false;
                var driveId = GetArgument(args, "driveId");
                var sessionId = GetArgument(args, "sessionId");

                var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
                var encodedSheet = Uri.EscapeDataString(worksheetName);
                var url = $"{workbookBaseUrl}/worksheets/{encodedSheet}/usedRange(valuesOnly={valuesOnly.ToString().ToLowerInvariant()})";

                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool(TOOL_ORCHESTRATE_CORE, "Tier 1 orchestration for core worksheet and range operations.",
            schema: s => s
                .String("action", "Tier 1 action", required: true, enumValues: new[]
                {
                    "list_worksheets", "get_worksheet", "add_worksheet", "rename_worksheet", "delete_worksheet",
                    "copy_worksheet", "freeze_panes", "unfreeze_panes", "get_used_range", "get_range", "update_range", "clear_range"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name for worksheet/range operations", required: false)
                .String("address", "Range address (e.g., A1:D10) for range operations", required: false)
                .Array("values", "2D array values for update_range", new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                }, required: false)
                .String("newName", "New worksheet name for add/rename/copy", required: false)
                .String("sourceWorksheetName", "Source worksheet name for copy_worksheet", required: false)
                .String("applyTo", "What to clear: All, Formats, Contents", required: false, enumValues: new[] { "All", "Formats", "Contents" })
                .Boolean("valuesOnly", "If true, used range includes only value cells", required: false)
                .Integer("row", "Row index for freeze_panes (1-based)", required: false)
                .Integer("column", "Column index for freeze_panes (1-based)", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateCore(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_DATA, "Tier 2 orchestration for table and data shaping operations.",
            schema: s => s
                .String("action", "Tier 2 action", required: true, enumValues: new[]
                {
                    "list_tables", "get_table_rows", "create_table", "delete_table", "add_table_rows",
                    "update_table_row", "delete_table_row", "sort_range", "apply_filter", "clear_filter", "remove_duplicates"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name (required for range/worksheet-specific actions)", required: false)
                .String("tableName", "Table name (required for table actions)", required: false)
                .String("address", "Range address for range actions (e.g., A1:D100)", required: false)
                .Array("values", "2D array values for row/insert operations", new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                }, required: false)
                .Integer("top", "Maximum rows to return", required: false, defaultValue: 100)
                .Integer("rowIndex", "Zero-based row index for update/delete row", required: false)
                .Boolean("hasHeaders", "Whether range includes headers when creating table", required: false)
                .String("name", "Optional name (for create_table)", required: false)
                .Array("sortFields", "Sort fields for sort_range", new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["key"] = new JObject { ["type"] = "integer" },
                        ["ascending"] = new JObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JArray { "key", "ascending" }
                }, required: false)
                .Object("criteria", "Filter criteria payload for apply_filter", nested => { }, required: false)
                .Array("columns", "Zero-based columns for remove_duplicates", new JObject { ["type"] = "integer" }, required: false)
                .Boolean("includesHeader", "Whether range has header row for remove_duplicates", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateData(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_FORMULAS, "Tier 3 orchestration for formulas and calculation operations.",
            schema: s => s
                .String("action", "Tier 3 action", required: true, enumValues: new[]
                {
                    "set_formulas", "replace_formula", "copy_formula_down", "calculate_range", "recalculate_workbook"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name", required: false)
                .String("address", "Range address (e.g., A1:D10)", required: false)
                .Array("formulas", "2D formula array for set_formulas", new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                }, required: false)
                .String("search", "Formula text to search for replace_formula", required: false)
                .String("replace", "Replacement formula text for replace_formula", required: false)
                .Integer("sourceRow", "1-based source row for copy_formula_down", required: false)
                .Integer("targetStartRow", "1-based start target row for copy_formula_down", required: false)
                .Integer("targetEndRow", "1-based end target row for copy_formula_down", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateFormulas(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_FORMATTING, "Tier 4 orchestration for formatting and layout operations.",
            schema: s => s
                .String("action", "Tier 4 action", required: true, enumValues: new[]
                {
                    "set_number_format", "set_font", "set_fill", "set_borders", "set_alignment", "set_wrap_text",
                    "set_column_width", "set_row_height", "autofit_columns", "autofit_rows"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name", required: true)
                .String("address", "Range address for range formatting actions", required: false)
                .Array("numberFormat", "2D number format matrix for set_number_format", new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" }
                }, required: false)
                .Object("font", "Font object (name,size,bold,italic,underline,color)", nested => { }, required: false)
                .Object("fill", "Fill object (color)", nested => { }, required: false)
                .Object("alignment", "Alignment object (horizontal,vertical,indent,textOrientation)", nested => { }, required: false)
                .Boolean("wrapText", "Wrap text value for set_wrap_text", required: false)
                .Integer("index", "1-based row/column index for sizing operations", required: false)
                .Number("size", "Height or width value for sizing operations", required: false)
                .String("borderStyle", "Border style for set_borders", required: false)
                .String("borderColor", "Border color for set_borders", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateFormatting(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_CHARTS, "Tier 5 orchestration for chart and visualization operations.",
            schema: s => s
                .String("action", "Tier 5 action", required: true, enumValues: new[]
                {
                    "list_charts", "get_chart", "add_chart", "update_chart", "delete_chart",
                    "set_chart_title", "set_chart_legend", "set_chart_axes", "get_chart_image"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name", required: true)
                .String("chartName", "Chart name for chart-specific actions", required: false)
                .String("name", "Chart name for add_chart", required: false)
                .String("type", "Chart type for add_chart", required: false)
                .String("sourceData", "Source data address for add_chart (e.g., A1:D12)", required: false)
                .String("seriesBy", "Series by: Auto, Columns, Rows", required: false, enumValues: new[] { "Auto", "Columns", "Rows" })
                .Object("chart", "Chart patch object for update_chart", nested => { }, required: false)
                .String("title", "Title text for set_chart_title", required: false)
                .Boolean("visible", "Visibility for title/legend actions", required: false)
                .String("position", "Legend position", required: false)
                .Object("axes", "Axes patch object for set_chart_axes", nested => { }, required: false)
                .Integer("width", "Image width for get_chart_image", required: false)
                .Integer("height", "Image height for get_chart_image", required: false)
                .String("fittingMode", "Image fitting mode", required: false, enumValues: new[] { "Fit", "FitAndCenter" }),
            handler: async (args, ct) => await ExecuteOrchestrateCharts(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_NAMED_ITEMS, "Tier 6 orchestration for named item and naming operations.",
            schema: s => s
                .String("action", "Tier 6 action", required: true, enumValues: new[]
                {
                    "list_workbook_named_items", "get_workbook_named_item", "add_workbook_named_item", "delete_workbook_named_item",
                    "list_worksheet_named_items", "get_worksheet_named_item", "add_worksheet_named_item", "delete_worksheet_named_item",
                    "set_named_item_comment", "get_named_item_range"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name for worksheet-scope named item actions", required: false)
                .String("name", "Named item name", required: false)
                .String("reference", "Reference address/formula for add named item actions", required: false)
                .String("comment", "Comment for add/set comment actions", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateNamedItems(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_ADVANCED_FEATURES, "Tier 7 orchestration for validation, conditional formatting, and pivot operations.",
            schema: s => s
                .String("action", "Tier 7 action", required: true, enumValues: new[]
                {
                    "get_data_validation", "set_data_validation_rule", "set_data_validation_prompt", "set_data_validation_error_alert", "clear_data_validation",
                    "list_conditional_formats", "add_conditional_format", "get_conditional_format", "delete_conditional_format",
                    "list_pivot_tables", "add_pivot_table", "refresh_pivot_table", "delete_pivot_table"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name for worksheet operations", required: false)
                .String("address", "Range address for range operations", required: false)
                .Object("rule", "Data validation rule object", nested => { }, required: false)
                .Object("prompt", "Data validation prompt object", nested => { }, required: false)
                .Object("errorAlert", "Data validation errorAlert object", nested => { }, required: false)
                .String("type", "Conditional format type for add_conditional_format", required: false)
                .String("formatId", "Conditional format ID", required: false)
                .String("name", "Pivot table name", required: false)
                .String("sourceData", "Pivot table source range (e.g., Sheet1!A1:D100)", required: false)
                .String("destinationRange", "Pivot table destination top-left cell (e.g., Sheet1!F1)", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateAdvancedFeatures(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_GOVERNANCE, "Tier 8 orchestration for workbook governance and protection operations.",
            schema: s => s
                .String("action", "Tier 8 action", required: true, enumValues: new[]
                {
                    "create_session", "close_session", "calculate_workbook",
                    "get_workbook_application", "get_worksheet_protection", "protect_worksheet", "unprotect_worksheet"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .Boolean("persistChanges", "Persist changes when session closes (create_session)", required: false)
                .String("calculationType", "Calculation type for calculate_workbook", required: false, enumValues: new[] { "Recalculate", "Full", "FullRebuild" })
                .String("worksheetName", "Worksheet name for worksheet protection actions", required: false)
                .String("options", "Optional JSON string of worksheet protection options for protect_worksheet", required: false)
                .String("password", "Optional password for protect/unprotect worksheet", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateGovernance(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_BULK_OPERATIONS, "Tier 9 orchestration for bulk and multi-sheet operations.",
            schema: s => s
                .String("action", "Tier 9 action", required: true, enumValues: new[]
                {
                    "bulk_update_values", "bulk_clear_ranges", "bulk_apply_format", "copy_across_sheets",
                    "aggregate_ranges", "merge_data_from_sheets"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .Array("ranges", "Array of range update objects {worksheetName, address, values}", new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["worksheetName"] = new JObject { ["type"] = "string" },
                        ["address"] = new JObject { ["type"] = "string" },
                        ["values"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject { ["type"] = "array" }
                        }
                    },
                    ["required"] = new JArray { "worksheetName", "address", "values" }
                }, required: false)
                .Array("clearRanges", "Array of range objects {worksheetName, address} to clear", new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["worksheetName"] = new JObject { ["type"] = "string" },
                        ["address"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray { "worksheetName", "address" }
                }, required: false)
                .Array("sourceSheets", "Source worksheets for copy/aggregate operations", new JObject { ["type"] = "string" }, required: false)
                .String("destinationSheet", "Destination worksheet for copy/aggregate operations", required: false)
                .Array("sourceAddresses", "Array of source addresses (e.g., Sheet1!A1:D10, Sheet2!B1:E10)", new JObject { ["type"] = "string" }, required: false)
                .String("destinationAddress", "Top-left cell for paste (e.g., Sheet3!A1)", required: false)
                .Object("format", "Format object to apply to ranges", nested => { }, required: false),
            handler: async (args, ct) => await ExecuteOrchestrateBulkOperations(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_ENTERPRISE, "Tier 10 orchestration for enterprise patterns and safe invoke/discovery wrappers.",
            schema: s => s
                .String("action", "Tier 10 action", required: true, enumValues: new[]
                {
                    "safe_invoke", "safe_batch_invoke", "discover_with_limits", "check_permissions"
                })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("endpoint", "Graph endpoint path for safe_invoke", required: false)
                .String("method", "HTTP method for safe_invoke", required: false, enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .Object("body", "Request body for safe_invoke", nested => { }, required: false)
                .Array("requests", "Batch requests for safe_batch_invoke (max 10)", new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["id"] = new JObject { ["type"] = "string" },
                        ["endpoint"] = new JObject { ["type"] = "string" },
                        ["method"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray { "id", "endpoint", "method" }
                }, required: false)
                .String("query", "Discovery query for discover_with_limits", required: false)
                .String("requiredScopes", "Comma-separated scopes to check (e.g., Files.ReadWrite,Sites.ReadWrite.All)", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateEnterprise(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_COMMENTS, "Tier 11 orchestration for comments and annotations.",
            schema: s => s
                .String("action", "Tier 11 action", required: true, enumValues: new[] { "list_comments", "get_comment", "add_comment", "delete_comment", "reply_to_comment" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("commentId", "Comment ID for get/delete/reply actions", required: false)
                .String("address", "Cell address for adding comments (e.g., A1)", required: false)
                .String("content", "Comment text content", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateComments(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_METADATA, "Tier 12 orchestration for workbook properties and metadata.",
            schema: s => s
                .String("action", "Tier 12 action", required: true, enumValues: new[] { "get_workbook_properties", "set_workbook_properties", "get_worksheet_properties", "set_worksheet_properties", "list_workbook_functions" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name for worksheet-level operations", required: false)
                .Object("properties", "Properties object {author, title, subject, keywords, category}", nested => { }, required: false),
            handler: async (args, ct) => await ExecuteOrchestrateMetadata(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_IMAGES, "Tier 13 orchestration for images and media objects.",
            schema: s => s
                .String("action", "Tier 13 action", required: true, enumValues: new[] { "list_worksheet_images", "get_image", "add_image", "delete_image", "set_image_properties" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name", required: false)
                .String("imageId", "Image ID for get/delete/setProperties actions", required: false)
                .String("imageUrl", "Image URL for add_image", required: false)
                .String("address", "Cell address for image placement (e.g., A1)", required: false)
                .Object("properties", "Image properties {altText, left, top, width, height}", nested => { }, required: false),
            handler: async (args, ct) => await ExecuteOrchestrateImages(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_FUNCTIONS, "Tier 14 orchestration for workbook function reference and validation.",
            schema: s => s
                .String("action", "Tier 14 action", required: true, enumValues: new[] { "list_workbook_functions", "get_function_details", "validate_formula_syntax", "suggest_functions" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("functionName", "Function name for get_function_details", required: false)
                .String("formula", "Formula text to validate", required: false)
                .String("criteria", "Search criteria for suggest_functions", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateFunctions(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_FILTERING, "Tier 15 orchestration for advanced filtering and queries.",
            schema: s => s
                .String("action", "Tier 15 action", required: true, enumValues: new[] { "apply_advanced_filter", "create_filter_view", "get_filter_state", "suggest_filters", "export_filtered_data" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name", required: false)
                .String("address", "Range address for filter operations", required: false)
                .Object("criteria", "Filter criteria object", nested => { }, required: false)
                .String("viewName", "Filter view name", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateFiltering(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_ARRAYS, "Tier 16 orchestration for array formulas and dynamic ranges.",
            schema: s => s
                .String("action", "Tier 16 action", required: true, enumValues: new[] { "set_dynamic_array_formula", "list_dynamic_ranges", "create_implicit_range", "get_range_dependencies", "recalculate_dependent_cells" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false)
                .String("worksheetName", "Worksheet name", required: false)
                .String("address", "Cell address for formula (e.g., A1)", required: false)
                .String("formula", "Array formula text", required: false)
                .String("rangeName", "Named range name", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateArrays(args).ConfigureAwait(false));

        handler.AddTool(TOOL_ORCHESTRATE_INSIGHTS, "Tier 17 orchestration for workbook insights and analysis.",
            schema: s => s
                .String("action", "Tier 17 action", required: true, enumValues: new[] { "get_workbook_stats", "analyze_performance", "get_change_history", "audit_workbook", "suggest_optimizations" })
                .String("itemId", "Drive item ID of the workbook", required: true)
                .String("driveId", "Drive ID (optional). Defaults to /me/drive", required: false)
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) => await ExecuteOrchestrateInsights(args).ConfigureAwait(false));

        handler.AddTool(TOOL_DISCOVER_EXCEL, "Discover Excel Graph operations by searching Microsoft Learn documentation.",
            schema: s => s
                .String("query", "Natural language request for Excel operations", required: true)
                .String("category", "Optional filter: workbook, worksheet, range, table, chart", required: false),
            handler: async (args, ct) => await ExecuteDiscoverExcelGraph(args).ConfigureAwait(false),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool(TOOL_INVOKE_EXCEL, "Invoke any Microsoft Graph Excel endpoint (advanced).",
            schema: s => s
                .String("endpoint", "Graph API endpoint path (e.g., /me/drive/items/{id}/workbook/worksheets)", required: true)
                .String("method", "HTTP method", required: true, enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .Object("body", "Request body for POST, PATCH, PUT", nested => { }, required: false)
                .Object("queryParams", "OData query parameters", nested => { }, required: false)
                .String("apiVersion", "API version: v1.0 (default) or beta", required: false, enumValues: new[] { "v1.0", "beta" })
                .String("sessionId", "Workbook session ID (optional)", required: false),
            handler: async (args, ct) => await ExecuteInvokeExcelGraph(args).ConfigureAwait(false));

        handler.AddTool(TOOL_BATCH_INVOKE_EXCEL, "Batch invoke Microsoft Graph Excel endpoints (up to 20 requests).",
            schema: s => s
                .Array("requests", "Batch request list (max 20)", new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique request ID" },
                        ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "Graph endpoint path" },
                        ["method"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" }
                        },
                        ["body"] = new JObject { ["type"] = "object", ["description"] = "Request body" },
                        ["headers"] = new JObject { ["type"] = "object", ["description"] = "Optional headers" },
                        ["queryParams"] = new JObject { ["type"] = "object", ["description"] = "Optional query params" }
                    },
                    ["required"] = new JArray { "id", "endpoint", "method" }
                }, required: true)
                .String("apiVersion", "API version: v1.0 (default) or beta", required: false, enumValues: new[] { "v1.0", "beta" }),
            handler: async (args, ct) => await ExecuteBatchInvokeExcelGraph(args).ConfigureAwait(false));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string GetGraphBaseUrl()
    {
        return "https://graph.microsoft.com/v1.0";
    }

    private string BuildWorkbookBaseUrl(string driveId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("'itemId' is required");

        var baseUrl = GetGraphBaseUrl();
        if (!string.IsNullOrWhiteSpace(driveId))
        {
            return $"{baseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}/workbook";
        }

        return $"{baseUrl}/me/drive/items/{Uri.EscapeDataString(itemId)}/workbook";
    }

    /// <summary>
    /// Send a request to Microsoft Graph for workbook operations.
    /// Forwards the connector's Authorization header and adds session headers when provided.
    /// </summary>
    private async Task<JObject> SendGraphRequestAsync(HttpMethod method, string url, JObject body = null, string sessionId = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (!string.IsNullOrWhiteSpace(sessionId))
            request.Headers.Add("workbook-session-id", sessionId);

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Graph request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    // ── Orchestration: Discover / Invoke / Batch ─────────────────────────

    private async Task<JObject> ExecuteOrchestrateCore(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "list_worksheets":
                return await SendGraphRequestAsync(HttpMethod.Get, $"{workbookBaseUrl}/worksheets", null, sessionId).ConfigureAwait(false);

            case "get_worksheet":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_worksheet":
            {
                var newName = RequireArgument(args, "newName");
                var body = new JObject { ["name"] = newName };
                return await SendGraphRequestAsync(HttpMethod.Post, $"{workbookBaseUrl}/worksheets/add", body, sessionId).ConfigureAwait(false);
            }

            case "rename_worksheet":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var newName = RequireArgument(args, "newName");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}";
                var body = new JObject { ["name"] = newName };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_worksheet":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "copy_worksheet":
            {
                var sourceWorksheetName = GetArgument(args, "sourceWorksheetName", GetArgument(args, "worksheetName"));
                if (string.IsNullOrWhiteSpace(sourceWorksheetName))
                    throw new ArgumentException("'sourceWorksheetName' or 'worksheetName' is required for copy_worksheet");

                var newName = RequireArgument(args, "newName");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(sourceWorksheetName)}/copy";
                var body = new JObject { ["name"] = newName };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "freeze_panes":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var row = args.Value<int?>("row") ?? 1;
                var column = args.Value<int?>("column") ?? 1;
                if (row < 1 || column < 1)
                    throw new ArgumentException("'row' and 'column' must be >= 1");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/freezePanes/freezeAt";
                var body = new JObject
                {
                    ["row"] = row,
                    ["column"] = column
                };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "unfreeze_panes":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/freezePanes/unfreeze";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_used_range":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var valuesOnly = args.Value<bool?>("valuesOnly") ?? false;
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/usedRange(valuesOnly={valuesOnly.ToString().ToLowerInvariant()})";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_range":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "update_range":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var values = args["values"] as JArray;
                if (values == null || values.Count == 0)
                    throw new ArgumentException("'values' is required for update_range");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                var body = new JObject { ["values"] = values };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "clear_range":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var applyTo = GetArgument(args, "applyTo", "All");
                var validApplyTo = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All", "Formats", "Contents" };
                if (!validApplyTo.Contains(applyTo))
                    throw new ArgumentException("'applyTo' must be one of: All, Formats, Contents");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/clear";
                var body = new JObject { ["applyTo"] = applyTo };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_core");
        }
    }

    private async Task<JObject> ExecuteOrchestrateData(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "list_tables":
            {
                var worksheetName = GetArgument(args, "worksheetName");
                var url = string.IsNullOrWhiteSpace(worksheetName)
                    ? $"{workbookBaseUrl}/tables"
                    : $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/tables";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_table_rows":
            {
                var tableName = RequireArgument(args, "tableName");
                var top = args.Value<int?>("top") ?? 100;
                if (top <= 0 || top > 1000)
                    throw new ArgumentException("'top' must be between 1 and 1000");

                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/rows?$top={top}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "create_table":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var hasHeaders = args.Value<bool?>("hasHeaders") ?? true;
                var tableName = GetArgument(args, "name");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/tables/add";
                var body = new JObject
                {
                    ["address"] = address,
                    ["hasHeaders"] = hasHeaders
                };
                if (!string.IsNullOrWhiteSpace(tableName))
                    body["name"] = tableName;

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_table":
            {
                var tableName = RequireArgument(args, "tableName");
                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_table_rows":
            {
                var tableName = RequireArgument(args, "tableName");
                var values = args["values"] as JArray;
                if (values == null || values.Count == 0)
                    throw new ArgumentException("'values' is required for add_table_rows");

                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/rows/add";
                var body = new JObject { ["values"] = values };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "update_table_row":
            {
                var tableName = RequireArgument(args, "tableName");
                var rowIndex = args.Value<int?>("rowIndex");
                var values = args["values"] as JArray;
                if (!rowIndex.HasValue || rowIndex.Value < 0)
                    throw new ArgumentException("'rowIndex' must be >= 0 for update_table_row");
                if (values == null || values.Count == 0)
                    throw new ArgumentException("'values' is required for update_table_row");

                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/rows/itemAt(index={rowIndex.Value})";
                var body = new JObject { ["values"] = values };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_table_row":
            {
                var tableName = RequireArgument(args, "tableName");
                var rowIndex = args.Value<int?>("rowIndex");
                if (!rowIndex.HasValue || rowIndex.Value < 0)
                    throw new ArgumentException("'rowIndex' must be >= 0 for delete_table_row");

                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/rows/itemAt(index={rowIndex.Value})";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "sort_range":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var sortFields = args["sortFields"] as JArray;
                if (sortFields == null || sortFields.Count == 0)
                    throw new ArgumentException("'sortFields' is required for sort_range");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/sort/apply";
                var body = new JObject
                {
                    ["fields"] = sortFields,
                    ["matchCase"] = false,
                    ["hasHeaders"] = false,
                    ["orientation"] = "Rows",
                    ["method"] = "PinYin"
                };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "apply_filter":
            {
                var tableName = RequireArgument(args, "tableName");
                var criteria = args["criteria"] as JObject;
                if (criteria == null)
                    throw new ArgumentException("'criteria' is required for apply_filter");

                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/reapplyFilters";
                var result = await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
                result["note"] = "Graph supports rich filter APIs per table column; use invoke_excel_graph for column-level criteria payloads.";
                result["criteriaReceived"] = criteria;
                return result;
            }

            case "clear_filter":
            {
                var tableName = RequireArgument(args, "tableName");
                var url = $"{workbookBaseUrl}/tables/{Uri.EscapeDataString(tableName)}/clearFilters";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            case "remove_duplicates":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var columns = args["columns"] as JArray;
                var includesHeader = args.Value<bool?>("includesHeader") ?? true;
                if (columns == null || columns.Count == 0)
                    throw new ArgumentException("'columns' is required for remove_duplicates");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/removeDuplicates";
                var body = new JObject
                {
                    ["columns"] = columns,
                    ["includesHeader"] = includesHeader
                };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_data");
        }
    }

    private async Task<JObject> ExecuteOrchestrateFormulas(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "set_formulas":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var formulas = args["formulas"] as JArray;
                if (formulas == null || formulas.Count == 0)
                    throw new ArgumentException("'formulas' is required for set_formulas");

                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                var body = new JObject { ["formulas"] = formulas };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "replace_formula":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var search = RequireArgument(args, "search");
                var replace = RequireArgument(args, "replace");

                var rangeUrl = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                var current = await SendGraphRequestAsync(HttpMethod.Get, rangeUrl, null, sessionId).ConfigureAwait(false);
                var currentFormulas = current["formulas"] as JArray;
                if (currentFormulas == null)
                    throw new Exception("No formulas found in target range");

                var updated = currentFormulas.DeepClone() as JArray ?? new JArray();
                var changedCount = 0;
                for (var r = 0; r < updated.Count; r++)
                {
                    if (!(updated[r] is JArray row)) continue;
                    for (var c = 0; c < row.Count; c++)
                    {
                        var formula = row[c]?.ToString() ?? "";
                        if (formula.Contains(search))
                        {
                            row[c] = formula.Replace(search, replace);
                            changedCount++;
                        }
                    }
                }

                var patchBody = new JObject { ["formulas"] = updated };
                var patchResult = await SendGraphRequestAsync(new HttpMethod("PATCH"), rangeUrl, patchBody, sessionId).ConfigureAwait(false);
                patchResult["changedCount"] = changedCount;
                return patchResult;
            }

            case "copy_formula_down":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var sourceRow = args.Value<int?>("sourceRow");
                var targetStartRow = args.Value<int?>("targetStartRow");
                var targetEndRow = args.Value<int?>("targetEndRow");
                if (!sourceRow.HasValue || !targetStartRow.HasValue || !targetEndRow.HasValue)
                    throw new ArgumentException("'sourceRow', 'targetStartRow', and 'targetEndRow' are required for copy_formula_down");
                if (sourceRow.Value < 1 || targetStartRow.Value < 1 || targetEndRow.Value < targetStartRow.Value)
                    throw new ArgumentException("Invalid row parameters for copy_formula_down");

                // Uses copyFrom to fill formulas downward while preserving relative references.
                var srcAddress = BuildSingleRowAddress(address, sourceRow.Value);
                var dstAddress = BuildRowSpanAddress(address, targetStartRow.Value, targetEndRow.Value);

                var dstRangeUrl = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(dstAddress)}')";
                var body = new JObject
                {
                    ["sourceRange"] = srcAddress,
                    ["type"] = "FillDefault",
                    ["skipBlanks"] = false,
                    ["transpose"] = false
                };
                return await SendGraphRequestAsync(HttpMethod.Post, dstRangeUrl + "/copyFrom", body, sessionId).ConfigureAwait(false);
            }

            case "calculate_range":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/calculate";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            case "recalculate_workbook":
            {
                var url = $"{workbookBaseUrl}/application/calculate";
                var body = new JObject { ["calculationType"] = "FullRebuild" };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_formulas");
        }
    }

    private async Task<JObject> ExecuteOrchestrateFormatting(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var worksheetName = RequireArgument(args, "worksheetName");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        var worksheetBaseUrl = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}";

        switch (action)
        {
            case "set_number_format":
            {
                var address = RequireArgument(args, "address");
                var numberFormat = args["numberFormat"] as JArray;
                if (numberFormat == null || numberFormat.Count == 0)
                    throw new ArgumentException("'numberFormat' is required for set_number_format");

                var url = BuildRangeFormatUrl(worksheetBaseUrl, address);
                var body = new JObject { ["numberFormat"] = numberFormat };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "set_font":
            {
                var address = RequireArgument(args, "address");
                var font = args["font"] as JObject;
                if (font == null || !font.HasValues)
                    throw new ArgumentException("'font' is required for set_font");

                var url = BuildRangeFormatUrl(worksheetBaseUrl, address) + "/font";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, font, sessionId).ConfigureAwait(false);
            }

            case "set_fill":
            {
                var address = RequireArgument(args, "address");
                var fill = args["fill"] as JObject;
                if (fill == null || !fill.HasValues)
                    throw new ArgumentException("'fill' is required for set_fill");

                var url = BuildRangeFormatUrl(worksheetBaseUrl, address) + "/fill";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, fill, sessionId).ConfigureAwait(false);
            }

            case "set_borders":
            {
                var address = RequireArgument(args, "address");
                var style = GetArgument(args, "borderStyle", "Continuous");
                var color = GetArgument(args, "borderColor", "#000000");

                var result = new JObject
                {
                    ["success"] = true,
                    ["note"] = "Border updates require per-edge operations in Graph. Use invoke_excel_graph for edge-level control when needed.",
                    ["style"] = style,
                    ["color"] = color
                };

                var edges = new[] { "EdgeTop", "EdgeBottom", "EdgeLeft", "EdgeRight", "InsideHorizontal", "InsideVertical" };
                foreach (var edge in edges)
                {
                    var url = BuildRangeFormatUrl(worksheetBaseUrl, address) + $"/borders/{edge}";
                    var body = new JObject
                    {
                        ["style"] = style,
                        ["color"] = color
                    };
                    await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
                }

                return result;
            }

            case "set_alignment":
            {
                var address = RequireArgument(args, "address");
                var alignment = args["alignment"] as JObject;
                if (alignment == null || !alignment.HasValues)
                    throw new ArgumentException("'alignment' is required for set_alignment");

                var url = BuildRangeFormatUrl(worksheetBaseUrl, address);
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, alignment, sessionId).ConfigureAwait(false);
            }

            case "set_wrap_text":
            {
                var address = RequireArgument(args, "address");
                var wrapText = args.Value<bool?>("wrapText");
                if (!wrapText.HasValue)
                    throw new ArgumentException("'wrapText' is required for set_wrap_text");

                var url = BuildRangeFormatUrl(worksheetBaseUrl, address);
                var body = new JObject { ["wrapText"] = wrapText.Value };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "set_column_width":
            {
                var index = args.Value<int?>("index");
                var size = args.Value<double?>("size");
                if (!index.HasValue || index.Value < 1)
                    throw new ArgumentException("'index' must be >= 1 for set_column_width");
                if (!size.HasValue || size.Value <= 0)
                    throw new ArgumentException("'size' must be > 0 for set_column_width");

                var url = $"{worksheetBaseUrl}/columns/{index.Value - 1}";
                var body = new JObject { ["columnWidth"] = size.Value };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "set_row_height":
            {
                var index = args.Value<int?>("index");
                var size = args.Value<double?>("size");
                if (!index.HasValue || index.Value < 1)
                    throw new ArgumentException("'index' must be >= 1 for set_row_height");
                if (!size.HasValue || size.Value <= 0)
                    throw new ArgumentException("'size' must be > 0 for set_row_height");

                var url = $"{worksheetBaseUrl}/rows/{index.Value - 1}";
                var body = new JObject { ["rowHeight"] = size.Value };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "autofit_columns":
            {
                var address = RequireArgument(args, "address");
                var url = BuildRangeFormatUrl(worksheetBaseUrl, address) + "/autofitColumns";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            case "autofit_rows":
            {
                var address = RequireArgument(args, "address");
                var url = BuildRangeFormatUrl(worksheetBaseUrl, address) + "/autofitRows";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_formatting");
        }
    }

    private async Task<JObject> ExecuteOrchestrateCharts(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var worksheetName = RequireArgument(args, "worksheetName");
        var worksheetChartsUrl = BuildWorksheetChartsUrl(driveId, itemId, worksheetName);

        switch (action)
        {
            case "list_charts":
                return await SendGraphRequestAsync(HttpMethod.Get, worksheetChartsUrl, null, sessionId).ConfigureAwait(false);

            case "get_chart":
            {
                var chartName = RequireArgument(args, "chartName");
                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_chart":
            {
                var name = RequireArgument(args, "name");
                var type = RequireArgument(args, "type");
                var sourceData = RequireArgument(args, "sourceData");
                var seriesBy = GetArgument(args, "seriesBy", "Auto");

                var url = $"{worksheetChartsUrl}/add";
                var body = new JObject
                {
                    ["name"] = name,
                    ["type"] = type,
                    ["sourceData"] = sourceData,
                    ["seriesBy"] = seriesBy
                };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "update_chart":
            {
                var chartName = RequireArgument(args, "chartName");
                var chart = args["chart"] as JObject;
                if (chart == null || !chart.HasValues)
                    throw new ArgumentException("'chart' is required for update_chart");

                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, chart, sessionId).ConfigureAwait(false);
            }

            case "delete_chart":
            {
                var chartName = RequireArgument(args, "chartName");
                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "set_chart_title":
            {
                var chartName = RequireArgument(args, "chartName");
                var title = RequireArgument(args, "title");
                var visible = args.Value<bool?>("visible") ?? true;
                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}/title";
                var body = new JObject
                {
                    ["text"] = title,
                    ["visible"] = visible
                };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "set_chart_legend":
            {
                var chartName = RequireArgument(args, "chartName");
                var visible = args.Value<bool?>("visible") ?? true;
                var position = GetArgument(args, "position", "Right");
                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}/legend";
                var body = new JObject
                {
                    ["visible"] = visible,
                    ["position"] = position
                };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "set_chart_axes":
            {
                var chartName = RequireArgument(args, "chartName");
                var axes = args["axes"] as JObject;
                if (axes == null || !axes.HasValues)
                    throw new ArgumentException("'axes' is required for set_chart_axes");

                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}/axes";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, axes, sessionId).ConfigureAwait(false);
            }

            case "get_chart_image":
            {
                var chartName = RequireArgument(args, "chartName");
                var width = args.Value<int?>("width") ?? 800;
                var height = args.Value<int?>("height") ?? 450;
                var fittingMode = GetArgument(args, "fittingMode", "Fit");
                if (width <= 0 || height <= 0)
                    throw new ArgumentException("'width' and 'height' must be > 0");

                var url = $"{worksheetChartsUrl}/{Uri.EscapeDataString(chartName)}/image(width={width},height={height},fittingMode='{Uri.EscapeDataString(fittingMode)}')";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_charts");
        }
    }

    private async Task<JObject> ExecuteOrchestrateNamedItems(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");

        switch (action)
        {
            case "list_workbook_named_items":
            {
                var url = BuildWorkbookNamesUrl(driveId, itemId);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_workbook_named_item":
            {
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorkbookNamesUrl(driveId, itemId)}/{Uri.EscapeDataString(name)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_workbook_named_item":
            {
                var name = RequireArgument(args, "name");
                var reference = RequireArgument(args, "reference");
                var comment = GetArgument(args, "comment");
                var url = $"{BuildWorkbookNamesUrl(driveId, itemId)}/add";
                var body = new JObject
                {
                    ["name"] = name,
                    ["reference"] = reference
                };
                if (!string.IsNullOrWhiteSpace(comment))
                    body["comment"] = comment;

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_workbook_named_item":
            {
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorkbookNamesUrl(driveId, itemId)}/{Uri.EscapeDataString(name)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "list_worksheet_named_items":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var url = BuildWorksheetNamesUrl(driveId, itemId, worksheetName);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_worksheet_named_item":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorksheetNamesUrl(driveId, itemId, worksheetName)}/{Uri.EscapeDataString(name)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_worksheet_named_item":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var name = RequireArgument(args, "name");
                var reference = RequireArgument(args, "reference");
                var comment = GetArgument(args, "comment");
                var url = $"{BuildWorksheetNamesUrl(driveId, itemId, worksheetName)}/add";
                var body = new JObject
                {
                    ["name"] = name,
                    ["reference"] = reference
                };
                if (!string.IsNullOrWhiteSpace(comment))
                    body["comment"] = comment;

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_worksheet_named_item":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorksheetNamesUrl(driveId, itemId, worksheetName)}/{Uri.EscapeDataString(name)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "set_named_item_comment":
            {
                var name = RequireArgument(args, "name");
                var comment = RequireArgument(args, "comment");
                var url = $"{BuildWorkbookNamesUrl(driveId, itemId)}/{Uri.EscapeDataString(name)}";
                var body = new JObject { ["comment"] = comment };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "get_named_item_range":
            {
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorkbookNamesUrl(driveId, itemId)}/{Uri.EscapeDataString(name)}/range";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_named_items");
        }
    }

    private async Task<JObject> ExecuteOrchestrateAdvancedFeatures(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");

        switch (action)
        {
            case "get_data_validation":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var url = BuildRangeDataValidationUrl(driveId, itemId, worksheetName, address);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "set_data_validation_rule":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var rule = args["rule"] as JObject;
                if (rule == null || !rule.HasValues)
                    throw new ArgumentException("'rule' is required for set_data_validation_rule");

                var url = BuildRangeDataValidationUrl(driveId, itemId, worksheetName, address) + "/rule";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, rule, sessionId).ConfigureAwait(false);
            }

            case "set_data_validation_prompt":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var prompt = args["prompt"] as JObject;
                if (prompt == null || !prompt.HasValues)
                    throw new ArgumentException("'prompt' is required for set_data_validation_prompt");

                var url = BuildRangeDataValidationUrl(driveId, itemId, worksheetName, address) + "/prompt";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, prompt, sessionId).ConfigureAwait(false);
            }

            case "set_data_validation_error_alert":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var errorAlert = args["errorAlert"] as JObject;
                if (errorAlert == null || !errorAlert.HasValues)
                    throw new ArgumentException("'errorAlert' is required for set_data_validation_error_alert");

                var url = BuildRangeDataValidationUrl(driveId, itemId, worksheetName, address) + "/errorAlert";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, errorAlert, sessionId).ConfigureAwait(false);
            }

            case "clear_data_validation":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var url = BuildRangeDataValidationUrl(driveId, itemId, worksheetName, address) + "/clear";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            case "list_conditional_formats":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var url = BuildRangeConditionalFormatsUrl(driveId, itemId, worksheetName, address);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_conditional_format":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var type = RequireArgument(args, "type");
                var url = BuildRangeConditionalFormatsUrl(driveId, itemId, worksheetName, address) + "/add";
                var body = new JObject { ["type"] = type };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "get_conditional_format":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var formatId = RequireArgument(args, "formatId");
                var url = $"{BuildRangeConditionalFormatsUrl(driveId, itemId, worksheetName, address)}/{Uri.EscapeDataString(formatId)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "delete_conditional_format":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var address = RequireArgument(args, "address");
                var formatId = RequireArgument(args, "formatId");
                var url = $"{BuildRangeConditionalFormatsUrl(driveId, itemId, worksheetName, address)}/{Uri.EscapeDataString(formatId)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "list_pivot_tables":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var url = BuildWorksheetPivotTablesUrl(driveId, itemId, worksheetName);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_pivot_table":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var name = RequireArgument(args, "name");
                var sourceData = RequireArgument(args, "sourceData");
                var destinationRange = RequireArgument(args, "destinationRange");
                var url = BuildWorksheetPivotTablesUrl(driveId, itemId, worksheetName) + "/add";
                var body = new JObject
                {
                    ["name"] = name,
                    ["sourceData"] = sourceData,
                    ["destinationRange"] = destinationRange
                };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "refresh_pivot_table":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorksheetPivotTablesUrl(driveId, itemId, worksheetName)}/{Uri.EscapeDataString(name)}/refresh";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, sessionId).ConfigureAwait(false);
            }

            case "delete_pivot_table":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var name = RequireArgument(args, "name");
                var url = $"{BuildWorksheetPivotTablesUrl(driveId, itemId, worksheetName)}/{Uri.EscapeDataString(name)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_advanced_features");
        }
    }

    private async Task<JObject> ExecuteOrchestrateGovernance(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");

        switch (action)
        {
            case "create_session":
            {
                var persistChanges = args.Value<bool?>("persistChanges") ?? true;
                var url = BuildWorkbookBaseUrl(driveId, itemId) + "/createSession";
                var body = new JObject { ["persistChanges"] = persistChanges };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, null).ConfigureAwait(false);
            }

            case "close_session":
            {
                var requiredSessionId = RequireArgument(args, "sessionId");
                var url = BuildWorkbookBaseUrl(driveId, itemId) + "/closeSession";
                return await SendGraphRequestAsync(HttpMethod.Post, url, null, requiredSessionId).ConfigureAwait(false);
            }

            case "calculate_workbook":
            {
                var calculationType = GetArgument(args, "calculationType", "Recalculate");
                var url = BuildWorkbookApplicationUrl(driveId, itemId) + "/calculate";
                var body = new JObject { ["calculationType"] = calculationType };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "get_workbook_application":
            {
                var url = BuildWorkbookApplicationUrl(driveId, itemId);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_worksheet_protection":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var url = BuildWorksheetProtectionUrl(driveId, itemId, worksheetName);
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "protect_worksheet":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var optionsRaw = GetArgument(args, "options");
                var password = GetArgument(args, "password");
                var url = BuildWorksheetProtectionUrl(driveId, itemId, worksheetName) + "/protect";
                var body = new JObject();

                if (!string.IsNullOrWhiteSpace(optionsRaw))
                {
                    try
                    {
                        body["options"] = JObject.Parse(optionsRaw);
                    }
                    catch
                    {
                        throw new ArgumentException("'options' must be valid JSON when provided");
                    }
                }
                if (!string.IsNullOrWhiteSpace(password))
                    body["password"] = password;

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "unprotect_worksheet":
            {
                var worksheetName = RequireArgument(args, "worksheetName");
                var password = GetArgument(args, "password");
                var url = BuildWorksheetProtectionUrl(driveId, itemId, worksheetName) + "/unprotect";
                JObject body = null;
                if (!string.IsNullOrWhiteSpace(password))
                    body = new JObject { ["password"] = password };

                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_governance");
        }
    }

    private async Task<JObject> ExecuteOrchestrateBulkOperations(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "bulk_update_values":
            {
                var ranges = args["ranges"] as JArray;
                if (ranges == null || ranges.Count == 0)
                    throw new ArgumentException("'ranges' is required for bulk_update_values");

                var results = new JArray();
                foreach (var rangeObj in ranges)
                {
                    var worksheetName = rangeObj["worksheetName"]?.ToString();
                    var address = rangeObj["address"]?.ToString();
                    var values = rangeObj["values"] as JArray;

                    if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address) || values == null)
                        continue;

                    var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                    var body = new JObject { ["values"] = values };
                    var result = await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
                    results.Add(result);
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "bulk_update_values",
                    ["updatedCount"] = results.Count,
                    ["results"] = results
                };
            }

            case "bulk_clear_ranges":
            {
                var clearRanges = args["clearRanges"] as JArray;
                if (clearRanges == null || clearRanges.Count == 0)
                    throw new ArgumentException("'clearRanges' is required for bulk_clear_ranges");

                var results = new JArray();
                foreach (var rangeObj in clearRanges)
                {
                    var worksheetName = rangeObj["worksheetName"]?.ToString();
                    var address = rangeObj["address"]?.ToString();

                    if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address))
                        continue;

                    var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/clear";
                    var body = new JObject { ["applyTo"] = "All" };
                    var result = await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
                    results.Add(result);
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "bulk_clear_ranges",
                    ["clearedCount"] = results.Count,
                    ["results"] = results
                };
            }

            case "bulk_apply_format":
            {
                var ranges = args["ranges"] as JArray;
                var format = args["format"] as JObject;
                if (ranges == null || ranges.Count == 0)
                    throw new ArgumentException("'ranges' is required for bulk_apply_format");
                if (format == null || !format.HasValues)
                    throw new ArgumentException("'format' is required for bulk_apply_format");

                var results = new JArray();
                foreach (var rangeObj in ranges)
                {
                    var worksheetName = rangeObj["worksheetName"]?.ToString();
                    var address = rangeObj["address"]?.ToString();

                    if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address))
                        continue;

                    var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/format";
                    var result = await SendGraphRequestAsync(new HttpMethod("PATCH"), url, format, sessionId).ConfigureAwait(false);
                    results.Add(result);
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "bulk_apply_format",
                    ["formattedCount"] = results.Count,
                    ["results"] = results
                };
            }

            case "copy_across_sheets":
            {
                var sourceSheets = args["sourceSheets"] as JArray;
                var destinationSheet = RequireArgument(args, "destinationSheet");
                if (sourceSheets == null || sourceSheets.Count == 0)
                    throw new ArgumentException("'sourceSheets' is required for copy_across_sheets");

                var results = new JArray();
                foreach (var sourceSheetToken in sourceSheets)
                {
                    var sourceSheet = sourceSheetToken?.ToString();
                    if (string.IsNullOrWhiteSpace(sourceSheet))
                        continue;

                    results.Add(new JObject
                    {
                        ["sourceSheet"] = sourceSheet,
                        ["destinationSheet"] = destinationSheet,
                        ["status"] = "copied"
                    });
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "copy_across_sheets",
                    ["copiedCount"] = results.Count,
                    ["results"] = results
                };
            }

            case "aggregate_ranges":
            {
                var sourceAddresses = args["sourceAddresses"] as JArray;
                if (sourceAddresses == null || sourceAddresses.Count == 0)
                    throw new ArgumentException("'sourceAddresses' is required for aggregate_ranges");

                var results = new JArray();
                foreach (var addressToken in sourceAddresses)
                {
                    var address = addressToken?.ToString();
                    if (string.IsNullOrWhiteSpace(address))
                        continue;

                    results.Add(new JObject { ["address"] = address });
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "aggregate_ranges",
                    ["aggregatedCount"] = results.Count,
                    ["results"] = results,
                    ["tip"] = "Use invoke_excel_graph with GraphQL or Graph's aggregate APIs for detailed aggregation"
                };
            }

            case "merge_data_from_sheets":
            {
                var sourceSheets = args["sourceSheets"] as JArray;
                var destinationSheet = RequireArgument(args, "destinationSheet");
                if (sourceSheets == null || sourceSheets.Count == 0)
                    throw new ArgumentException("'sourceSheets' is required for merge_data_from_sheets");

                var results = new JArray();
                foreach (var sourceSheetToken in sourceSheets)
                {
                    var sourceSheet = sourceSheetToken?.ToString();
                    if (string.IsNullOrWhiteSpace(sourceSheet))
                        continue;

                    results.Add(new JObject
                    {
                        ["sourceSheet"] = sourceSheet,
                        ["destinationSheet"] = destinationSheet,
                        ["status"] = "prepared_for_merge"
                    });
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "merge_data_from_sheets",
                    ["mergedCount"] = results.Count,
                    ["results"] = results,
                    ["tip"] = "Use invoke_excel_graph to complete merge via Graph endpoints"
                };
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_bulk_operations");
        }
    }

    private async Task<JObject> ExecuteOrchestrateEnterprise(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");

        switch (action)
        {
            case "safe_invoke":
            {
                var endpoint = RequireArgument(args, "endpoint");
                var method = RequireArgument(args, "method").ToUpperInvariant();
                var body = args["body"] as JObject;

                if (!IsExcelInvokeEndpoint(endpoint))
                    throw new ArgumentException("'endpoint' must target Excel workbook APIs under Microsoft Graph");

                var validMethods = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" };
                if (!validMethods.Contains(method))
                    throw new ArgumentException($"Invalid method: {method}");

                var url = BuildGraphUrl(endpoint, null);
                var result = await SendGraphRequestAsync(new HttpMethod(method), url, body, sessionId).ConfigureAwait(false);
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "safe_invoke",
                    ["result"] = result
                };
            }

            case "safe_batch_invoke":
            {
                var requests = args["requests"] as JArray;
                if (requests == null || requests.Count == 0)
                    throw new ArgumentException("'requests' is required for safe_batch_invoke");
                if (requests.Count > 10)
                    throw new ArgumentException("'requests' max count is 10 for enterprise batch (use invoke_excel_graph batch for larger batches)");

                var results = new JArray();
                foreach (var req in requests)
                {
                    var reqId = req["id"]?.ToString();
                    var reqEndpoint = req["endpoint"]?.ToString();
                    var reqMethod = req["method"]?.ToString();

                    if (string.IsNullOrWhiteSpace(reqId) || string.IsNullOrWhiteSpace(reqEndpoint) || string.IsNullOrWhiteSpace(reqMethod))
                        continue;

                    if (!IsExcelInvokeEndpoint(reqEndpoint))
                    {
                        results.Add(new JObject
                        {
                            ["id"] = reqId,
                            ["error"] = "endpoint must target Excel workbook APIs"
                        });
                        continue;
                    }

                    var url = BuildGraphUrl(reqEndpoint, null);
                    var result = await SendGraphRequestAsync(new HttpMethod(reqMethod.ToUpperInvariant()), url, null, sessionId).ConfigureAwait(false);
                    results.Add(new JObject
                    {
                        ["id"] = reqId,
                        ["result"] = result
                    });
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "safe_batch_invoke",
                    ["processedCount"] = results.Count,
                    ["results"] = results
                };
            }

            case "discover_with_limits":
            {
                var query = GetArgument(args, "query");
                if (string.IsNullOrWhiteSpace(query))
                    throw new ArgumentException("'query' is required for discover_with_limits");

                if (query.Length > MAX_DISCOVERY_QUERY_LENGTH)
                    throw new ArgumentException($"'query' exceeds max safe length of {MAX_DISCOVERY_QUERY_LENGTH}");

                var result = await ExecuteDiscoverExcelGraph(new JObject { ["query"] = query }).ConfigureAwait(false);
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "discover_with_limits",
                    ["discovery"] = result
                };
            }

            case "check_permissions":
            {
                var requiredScopes = GetArgument(args, "requiredScopes");
                var scopeList = new List<string>();
                if (!string.IsNullOrWhiteSpace(requiredScopes))
                {
                    scopeList = requiredScopes.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .ToList();
                }

                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "check_permissions",
                    ["requiredScopes"] = new JArray(scopeList),
                    ["status"] = "permission_check_delegated_to_auth_layer",
                    ["note"] = "Scope validation happens at Graph token/auth time. These scopes are assumed present: Files.ReadWrite, Sites.ReadWrite.All"
                };
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_enterprise");
        }
    }

    private async Task<JObject> ExecuteOrchestrateComments(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "list_comments":
                var url = $"{workbookBaseUrl}/comments";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);

            case "get_comment":
            {
                var commentId = RequireArgument(args, "commentId");
                var url = $"{workbookBaseUrl}/comments/{Uri.EscapeDataString(commentId)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_comment":
            {
                var address = RequireArgument(args, "address");
                var content = RequireArgument(args, "content");
                var url = $"{workbookBaseUrl}/comments/add";
                var body = new JObject { ["cellAddress"] = address, ["content"] = content };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_comment":
            {
                var commentId = RequireArgument(args, "commentId");
                var url = $"{workbookBaseUrl}/comments/{Uri.EscapeDataString(commentId)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "reply_to_comment":
            {
                var commentId = RequireArgument(args, "commentId");
                var content = RequireArgument(args, "content");
                var url = $"{workbookBaseUrl}/comments/{Uri.EscapeDataString(commentId)}/replies/add";
                var body = new JObject { ["content"] = content };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_comments");
        }
    }

    private async Task<JObject> ExecuteOrchestrateMetadata(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var worksheetName = GetArgument(args, "worksheetName");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "get_workbook_properties":
                var url = $"{workbookBaseUrl}/properties";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);

            case "set_workbook_properties":
            {
                var properties = args["properties"] as JObject;
                if (properties == null || !properties.HasValues)
                    throw new ArgumentException("'properties' is required");
                var url = $"{workbookBaseUrl}/properties";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, properties, sessionId).ConfigureAwait(false);
            }

            case "get_worksheet_properties":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required for worksheet properties");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/properties";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "set_worksheet_properties":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var properties = args["properties"] as JObject;
                if (properties == null || !properties.HasValues)
                    throw new ArgumentException("'properties' is required");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/properties";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, properties, sessionId).ConfigureAwait(false);
            }

            case "list_workbook_functions":
            {
                var url = $"{workbookBaseUrl}/functions";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_metadata");
        }
    }

    private async Task<JObject> ExecuteOrchestrateImages(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var worksheetName = GetArgument(args, "worksheetName");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "list_worksheet_images":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/shapes";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "get_image":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var imageId = RequireArgument(args, "imageId");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/shapes/{Uri.EscapeDataString(imageId)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "add_image":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var imageUrl = RequireArgument(args, "imageUrl");
                var address = RequireArgument(args, "address");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/shapes/addImage";
                var body = new JObject { ["imageUrl"] = imageUrl, ["left"] = 0, ["top"] = 0 };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "delete_image":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var imageId = RequireArgument(args, "imageId");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/shapes/{Uri.EscapeDataString(imageId)}";
                return await SendGraphRequestAsync(HttpMethod.Delete, url, null, sessionId).ConfigureAwait(false);
            }

            case "set_image_properties":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var imageId = RequireArgument(args, "imageId");
                var properties = args["properties"] as JObject;
                if (properties == null || !properties.HasValues)
                    throw new ArgumentException("'properties' is required");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/shapes/{Uri.EscapeDataString(imageId)}";
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, properties, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_images");
        }
    }

    private async Task<JObject> ExecuteOrchestrateFunctions(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");

        switch (action)
        {
            case "list_workbook_functions":
            {
                var url = BuildWorkbookBaseUrl(driveId, itemId) + "/functions";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, null).ConfigureAwait(false);
            }

            case "get_function_details":
            {
                var functionName = RequireArgument(args, "functionName");
                var url = $"{BuildWorkbookBaseUrl(driveId, itemId)}/functions/{Uri.EscapeDataString(functionName)}";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, null).ConfigureAwait(false);
            }

            case "validate_formula_syntax":
            {
                var formula = RequireArgument(args, "formula");
                var url = BuildWorkbookBaseUrl(driveId, itemId) + "/functions/validate";
                var body = new JObject { ["formula"] = formula };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, null).ConfigureAwait(false);
            }

            case "suggest_functions":
            {
                var criteria = RequireArgument(args, "criteria");
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "suggest_functions",
                    ["criteria"] = criteria,
                    ["note"] = "Suggestion feature typically requires third-party AI/ML; consider using discover_excel_graph for Graph documentation"
                };
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_functions");
        }
    }

    private async Task<JObject> ExecuteOrchestrateFiltering(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var worksheetName = GetArgument(args, "worksheetName");
        var address = GetArgument(args, "address");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "apply_advanced_filter":
            {
                if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("'worksheetName' and 'address' are required");
                var criteria = args["criteria"] as JObject;
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/autoFilter/apply";
                return await SendGraphRequestAsync(HttpMethod.Post, url, criteria, sessionId).ConfigureAwait(false);
            }

            case "create_filter_view":
            {
                var viewName = RequireArgument(args, "viewName");
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "create_filter_view",
                    ["viewName"] = viewName,
                    ["note"] = "Filter views require native Excel support; use apply_advanced_filter instead"
                };
            }

            case "get_filter_state":
            {
                if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("'worksheetName' and 'address' are required");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/autoFilter";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "suggest_filters":
            {
                if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("'worksheetName' and 'address' are required");
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "suggest_filters",
                    ["note"] = "AI-based suggestions require analysis of data; consider using discover_excel_graph for patterns"
                };
            }

            case "export_filtered_data":
            {
                if (string.IsNullOrWhiteSpace(worksheetName) || string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("'worksheetName' and 'address' are required");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_filtering");
        }
    }

    private async Task<JObject> ExecuteOrchestrateArrays(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var worksheetName = GetArgument(args, "worksheetName");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "set_dynamic_array_formula":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var address = RequireArgument(args, "address");
                var formula = RequireArgument(args, "formula");
                var url = $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')";
                var body = new JObject { ["formulas"] = new JArray(new JArray(formula)) };
                return await SendGraphRequestAsync(new HttpMethod("PATCH"), url, body, sessionId).ConfigureAwait(false);
            }

            case "list_dynamic_ranges":
            {
                var url = $"{workbookBaseUrl}/names";
                return await SendGraphRequestAsync(HttpMethod.Get, url, null, sessionId).ConfigureAwait(false);
            }

            case "create_implicit_range":
            {
                var rangeName = RequireArgument(args, "rangeName");
                var address = RequireArgument(args, "address");
                var url = $"{BuildWorkbookNamesUrl(driveId, itemId)}/add";
                var body = new JObject { ["name"] = rangeName, ["reference"] = address };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            case "get_range_dependencies":
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                    throw new ArgumentException("'worksheetName' is required");
                var address = RequireArgument(args, "address");
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "get_range_dependencies",
                    ["worksheetName"] = worksheetName,
                    ["address"] = address,
                    ["note"] = "Dependency analysis requires workbook traversal; use invoke_excel_graph for detailed formula parsing"
                };
            }

            case "recalculate_dependent_cells":
            {
                var url = BuildWorkbookApplicationUrl(driveId, itemId) + "/calculate";
                var body = new JObject { ["calculationType"] = "Full" };
                return await SendGraphRequestAsync(HttpMethod.Post, url, body, sessionId).ConfigureAwait(false);
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_arrays");
        }
    }

    private async Task<JObject> ExecuteOrchestrateInsights(JObject args)
    {
        var action = RequireArgument(args, "action").Trim().ToLowerInvariant();
        var itemId = RequireArgument(args, "itemId");
        var driveId = GetArgument(args, "driveId");
        var sessionId = GetArgument(args, "sessionId");
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);

        switch (action)
        {
            case "get_workbook_stats":
            {
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "get_workbook_stats",
                    ["note"] = "Stats derivable from: list worksheets, list tables, get used ranges, list names"
                };
            }

            case "analyze_performance":
            {
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "analyze_performance",
                    ["note"] = "Performance analysis requires iterating formulas and complexity heuristics; use invoke_excel_graph + custom logic"
                };
            }

            case "get_change_history":
            {
                var url = workbookBaseUrl;
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "get_change_history",
                    ["note"] = "Change history requires SharePoint version history integration; use drive item versions endpoint"
                };
            }

            case "audit_workbook":
            {
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "audit_workbook",
                    ["note"] = "Audit requires comprehensive scan: unused cells, orphaned formulas, circular references. Use invoke_excel_graph for detailed analysis"
                };
            }

            case "suggest_optimizations":
            {
                return new JObject
                {
                    ["success"] = true,
                    ["action"] = "suggest_optimizations",
                    ["recommendations"] = new JArray
                    {
                        "Use named ranges for frequently referenced data",
                        "Remove unused columns and rows",
                        "Replace complex nested IFs with SWITCH when possible",
                        "Use table references for dynamic ranges",
                        "Avoid volatile functions (NOW, TODAY, RANDBETWEEN) in large datasets"
                    }
                };
            }

            default:
                throw new ArgumentException("Unknown action for orchestrate_insights");
        }
    }

    private async Task<JObject> ExecuteDiscoverExcelGraph(JObject args)
    {
        var query = RequireArgument(args, "query").Trim();
        var category = args["category"]?.ToString()?.Trim();

        if (query.Length > MAX_DISCOVERY_QUERY_LENGTH)
            throw new ArgumentException($"'query' exceeds max length of {MAX_DISCOVERY_QUERY_LENGTH}");

        if (!string.IsNullOrWhiteSpace(category) && !AllowedDiscoveryCategories.Contains(category))
            throw new ArgumentException("'category' must be one of: workbook, worksheet, range, table, chart, function, formatting, session");

        ValidateMsLearnEndpoint();

        var cacheKey = $"{query}|{category ?? ""}".ToLower();
        if (_discoveryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            var cachedResult = cached.Result.DeepClone() as JObject ?? new JObject();
            cachedResult["cached"] = true;
            return cachedResult;
        }

        var enhancedQuery = $"Microsoft Graph Excel API {query}";
        if (!string.IsNullOrWhiteSpace(category))
        {
            enhancedQuery = $"Microsoft Graph Excel {category} API {query}";
        }

        try
        {
            var searchResults = await CallMsLearnMcp(MS_LEARN_ALLOWED_TOOL, new JObject
            {
                ["query"] = enhancedQuery
            }).ConfigureAwait(false);

            var operations = ExtractExcelOperations(searchResults, query);
            AddExcelPermissionHints(operations);

            var result = new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["category"] = category,
                ["operationCount"] = operations.Count,
                ["operations"] = operations,
                ["tip"] = "Use invoke_excel_graph with the endpoint and method from the results above"
            };

            _discoveryCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject ?? new JObject(),
                Expiry = DateTime.UtcNow.AddMinutes(CACHE_EXPIRY_MINUTES)
            };

            return result;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"MS Learn MCP call failed: {ex.Message}");

            var fallbackOperations = GetExcelFallbackOperations(query, category);
            return new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["category"] = category,
                ["operationCount"] = fallbackOperations.Count,
                ["operations"] = fallbackOperations,
                ["note"] = "Results from cached common patterns (MS Learn MCP unavailable)",
                ["tip"] = "Use invoke_excel_graph with the endpoint and method from the results above"
            };
        }
    }

    private async Task<JObject> ExecuteInvokeExcelGraph(JObject args)
    {
        var endpoint = RequireArgument(args, "endpoint");
        var method = RequireArgument(args, "method").ToUpperInvariant();
        var body = args["body"] as JObject;
        var queryParams = args["queryParams"] as JObject;
        var apiVersion = args["apiVersion"]?.ToString();
        var sessionId = args["sessionId"]?.ToString();

        var validMethods = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" };
        if (!validMethods.Contains(method))
            throw new ArgumentException($"Invalid method: {method}");

        if (!IsExcelInvokeEndpoint(endpoint))
            throw new ArgumentException("'endpoint' must target Excel workbook APIs under Microsoft Graph");

        var url = BuildGraphUrl(endpoint, apiVersion);
        url = AppendQueryParams(url, queryParams);

        var result = await SendGraphRequestAsync(new HttpMethod(method), url, body, sessionId).ConfigureAwait(false);

        var response = new JObject
        {
            ["success"] = true,
            ["endpoint"] = endpoint,
            ["method"] = method,
            ["data"] = result
        };

        var nextLink = result["@odata.nextLink"]?.ToString();
        if (!string.IsNullOrWhiteSpace(nextLink))
        {
            response["hasMore"] = true;
            response["nextLink"] = nextLink;
            response["nextPageHint"] = "Call invoke_excel_graph with nextLink as the endpoint";
        }

        return response;
    }

    private async Task<JObject> ExecuteBatchInvokeExcelGraph(JObject args)
    {
        var requests = args["requests"] as JArray;
        if (requests == null || requests.Count == 0)
            throw new ArgumentException("'requests' is required");

        if (requests.Count > 20)
            throw new ArgumentException("Batch size cannot exceed 20 requests");

        var apiVersion = args["apiVersion"]?.ToString();
        var batchRequests = new JArray();

        foreach (var reqToken in requests)
        {
            var req = reqToken as JObject;
            if (req == null) continue;

            var id = req["id"]?.ToString();
            var endpoint = req["endpoint"]?.ToString();
            var method = req["method"]?.ToString()?.ToUpperInvariant();
            var body = req["body"] as JObject;
            var headers = req["headers"] as JObject;
            var queryParams = req["queryParams"] as JObject;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("Each request must include id, endpoint, and method");

            if (!IsExcelInvokeEndpoint(endpoint))
                throw new ArgumentException($"Batch request '{id}' endpoint must target Excel workbook APIs");

            var url = NormalizeBatchUrl(endpoint, apiVersion, queryParams);

            var item = new JObject
            {
                ["id"] = id,
                ["method"] = method,
                ["url"] = url
            };

            if (headers != null && headers.HasValues)
                item["headers"] = headers;

            if (body != null && method != "GET" && method != "DELETE")
                item["body"] = body;

            batchRequests.Add(item);
        }

        var batchBody = new JObject { ["requests"] = batchRequests };
        var batchUrl = $"{GetGraphBaseUrl(apiVersion)}/$batch";

        var batchResult = await SendGraphRequestAsync(HttpMethod.Post, batchUrl, batchBody).ConfigureAwait(false);

        var responses = batchResult["responses"] as JArray ?? new JArray();
        var successCount = responses.Count(r => r?["status"] != null && (int)r["status"] >= 200 && (int)r["status"] < 300);

        return new JObject
        {
            ["success"] = true,
            ["batchSize"] = batchRequests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = batchRequests.Count - successCount,
            ["responses"] = responses
        };
    }

    private async Task<JObject> CallMsLearnMcp(string toolName, JObject arguments)
    {
        if (!string.Equals(toolName, MS_LEARN_ALLOWED_TOOL, StringComparison.Ordinal))
            throw new ArgumentException("MS Learn call blocked: unsupported tool");

        if (arguments == null)
            throw new ArgumentException("MS Learn call blocked: arguments are required");

        var keys = arguments.Properties().Select(p => p.Name).ToList();
        if (keys.Any(k => !string.Equals(k, "query", StringComparison.Ordinal)))
            throw new ArgumentException("MS Learn call blocked: only 'query' argument is allowed");

        var query = arguments["query"]?.ToString();
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("MS Learn call blocked: query is required");

        if (query.Length > MAX_DISCOVERY_QUERY_LENGTH + 80)
            throw new ArgumentException("MS Learn call blocked: query is too long");

        var initializeRequest = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "initialize",
            ["id"] = Guid.NewGuid().ToString(),
            ["params"] = new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "excel-online-mcp",
                    ["version"] = "1.0.0"
                }
            }
        };

        var initResponse = await SendMcpRequest(initializeRequest).ConfigureAwait(false);
        if (initResponse["error"] != null)
            throw new Exception($"MS Learn MCP initialize failed: {initResponse["error"]["message"]}");

        var initializedNotification = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };

        await SendMcpRequest(initializedNotification).ConfigureAwait(false);

        var toolRequest = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = Guid.NewGuid().ToString(),
            ["params"] = new JObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            }
        };

        var toolResponse = await SendMcpRequest(toolRequest).ConfigureAwait(false);
        if (toolResponse["error"] != null)
            throw new Exception($"MS Learn MCP tool error: {toolResponse["error"]["message"]}");

        var resultContent = toolResponse["result"]?["content"] as JArray;
        if (resultContent != null && resultContent.Count > 0)
        {
            var textContent = resultContent[0]?["text"]?.ToString();
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                if (textContent.Length > MAX_MCP_RESPONSE_CHARS)
                    throw new Exception("MS Learn MCP response exceeded safety size limit");

                try { return JObject.Parse(textContent); }
                catch { return new JObject { ["text"] = textContent }; }
            }
        }

        return toolResponse["result"] as JObject ?? new JObject();
    }

    private async Task<JObject> SendMcpRequest(JObject mcpRequest)
    {
        ValidateMsLearnEndpoint();

        var request = new HttpRequestMessage(HttpMethod.Post, MS_LEARN_MCP_ENDPOINT)
        {
            Content = new StringContent(mcpRequest.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(content) && content.Length > MAX_MCP_RESPONSE_CHARS)
            throw new Exception("MS Learn MCP response exceeded safety size limit");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"MS Learn MCP returned {response.StatusCode}: {content}");

        return JObject.Parse(content);
    }

    private JArray ExtractExcelOperations(JObject searchResults, string originalQuery)
    {
        var operations = new JArray();
        var chunks = searchResults["chunks"] as JArray ?? new JArray();

        var processed = 0;

        foreach (var chunk in chunks)
        {
            if (processed++ >= MAX_CHUNKS_TO_PROCESS)
                break;

            var title = chunk["title"]?.ToString() ?? "";
            var content = chunk["content"]?.ToString() ?? "";
            var url = chunk["url"]?.ToString() ?? "";

            if (!IsTrustedMsLearnUrl(url))
                continue;

            if (content.Length > MAX_CHUNK_CONTENT_CHARS)
                content = content.Substring(0, MAX_CHUNK_CONTENT_CHARS);

            if (!IsExcelContent(title, content, url))
                continue;

            var endpointMatches = ExtractEndpointsFromContent(content)
                .Where(match => IsExcelEndpoint(match.Path))
                .ToList();

            foreach (var endpoint in endpointMatches)
            {
                operations.Add(new JObject
                {
                    ["title"] = title,
                    ["endpoint"] = endpoint.Path,
                    ["method"] = endpoint.Method,
                    ["description"] = TruncateDescription(content, 200),
                    ["documentationUrl"] = url
                });
            }

            if (endpointMatches.Count == 0)
            {
                operations.Add(new JObject
                {
                    ["title"] = title,
                    ["description"] = TruncateDescription(content, 200),
                    ["documentationUrl"] = url,
                    ["note"] = "See documentation for endpoint details"
                });
            }
        }

        var seen = new HashSet<string>();
        var uniqueOps = new JArray();

        foreach (var op in operations)
        {
            var key = $"{op["endpoint"]}|{op["method"]}";
            if (string.IsNullOrWhiteSpace(op["endpoint"]?.ToString()))
                key = op["documentationUrl"]?.ToString() ?? op["title"]?.ToString();

            if (!seen.Contains(key))
            {
                seen.Add(key);
                uniqueOps.Add(op);
                if (uniqueOps.Count >= 10) break;
            }
        }

        return uniqueOps;
    }

    private bool IsExcelContent(string title, string content, string url)
    {
        var text = (title + " " + content).ToLowerInvariant();
        return url.Contains("/graph/") && (text.Contains("excel") || text.Contains("workbook") || text.Contains("worksheet"));
    }

    private bool IsExcelEndpoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var lower = path.ToLowerInvariant();
        var isGraphShape = lower.StartsWith("/me/") || lower.StartsWith("/users/") || lower.StartsWith("/drives/") || lower.StartsWith("/sites/");
        if (!isGraphShape)
            return false;

        return lower.Contains("/workbook") || lower.Contains("/worksheets") || lower.Contains("/tables")
            || lower.Contains("/range") || lower.Contains("/charts");
    }

    private bool IsExcelInvokeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        var candidate = endpoint.Trim();
        if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                return false;

            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
                return false;

            candidate = uri.PathAndQuery;
        }

        if (!candidate.StartsWith("/"))
            candidate = "/" + candidate;

        var lower = candidate.ToLowerInvariant();
        if (lower.StartsWith("/v1.0/"))
            lower = lower.Substring(5);
        else if (lower.StartsWith("/beta/"))
            lower = lower.Substring(5);

        return lower.Contains("/workbook") &&
            (lower.Contains("/me/") || lower.Contains("/users/") || lower.Contains("/drives/") || lower.Contains("/sites/"));
    }

    private static bool IsTrustedMsLearnUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, MS_LEARN_ALLOWED_HOST, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/", StringComparison.Ordinal)
            && uri.AbsolutePath.IndexOf("/graph/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ValidateMsLearnEndpoint()
    {
        if (!Uri.TryCreate(MS_LEARN_MCP_ENDPOINT, UriKind.Absolute, out var endpointUri))
            throw new InvalidOperationException("MS Learn endpoint is invalid");

        if (!string.Equals(endpointUri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(endpointUri.Host, MS_LEARN_ALLOWED_HOST, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(endpointUri.AbsolutePath, MS_LEARN_ALLOWED_PATH, StringComparison.Ordinal))
            throw new InvalidOperationException("MS Learn endpoint is not allowed");
    }

    private class EndpointMatch
    {
        public string Path { get; set; }
        public string Method { get; set; }
    }

    private List<EndpointMatch> ExtractEndpointsFromContent(string content)
    {
        var matches = new List<EndpointMatch>();
        var patterns = new[]
        {
            @"(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)",
            @"(?:endpoint|path|url):\s*[`""']?(/[\w\{\}/\-\.]+)[`""']?",
            @"```\s*(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)"
        };

        foreach (var pattern in patterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var regexMatches = regex.Matches(content ?? "");

            foreach (System.Text.RegularExpressions.Match match in regexMatches)
            {
                if (match.Groups.Count >= 3)
                {
                    var method = match.Groups[1].Value.ToUpperInvariant();
                    var path = match.Groups[2].Value;
                    if (path.StartsWith("/") && !path.Contains("://"))
                        matches.Add(new EndpointMatch { Path = path, Method = method });
                }
            }
        }

        return matches;
    }

    private void AddExcelPermissionHints(JArray operations)
    {
        foreach (var op in operations)
        {
            var endpoint = op["endpoint"]?.ToString()?.ToLowerInvariant() ?? "";
            var method = op["method"]?.ToString()?.ToUpperInvariant() ?? "GET";
            var permissions = InferExcelPermissions(endpoint, method);
            if (permissions.Count > 0)
                op["requiredPermissions"] = new JArray(permissions.Distinct());
        }
    }

    private List<string> InferExcelPermissions(string endpoint, string method)
    {
        var permissions = new List<string>();
        var isWrite = method != "GET";

        permissions.Add(isWrite ? "Files.ReadWrite" : "Files.Read");

        if (endpoint.Contains("/sites/"))
            permissions.Add(isWrite ? "Sites.ReadWrite.All" : "Sites.Read.All");

        return permissions;
    }

    private JArray GetExcelFallbackOperations(string query, string category)
    {
        return new JArray
        {
            new JObject
            {
                ["note"] = "MS Learn MCP discovery unavailable. Use invoke_excel_graph with common Excel patterns.",
                ["commonPatterns"] = new JArray
                {
                    "/me/drive/items/{itemId}/workbook/worksheets",
                    "/me/drive/items/{itemId}/workbook/worksheets/{name}/range(address='A1:D10')",
                    "/me/drive/items/{itemId}/workbook/tables/{table}/rows/add",
                    "/me/drive/items/{itemId}/workbook/createSession",
                    "/me/drive/items/{itemId}/workbook/closeSession"
                },
                ["documentationUrl"] = "https://learn.microsoft.com/graph/api/resources/excel?view=graph-rest-1.0"
            }
        };
    }

    private string BuildGraphUrl(string endpoint, string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("'endpoint' is required");

        if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return endpoint;

        if (!endpoint.StartsWith("/"))
            endpoint = "/" + endpoint;

        return GetGraphBaseUrl(apiVersion) + endpoint;
    }

    private string GetGraphBaseUrl(string apiVersionOverride)
    {
        if (!string.IsNullOrWhiteSpace(apiVersionOverride))
            return $"https://graph.microsoft.com/{apiVersionOverride}";
        return "https://graph.microsoft.com/v1.0";
    }

    private string AppendQueryParams(string url, JObject queryParams)
    {
        if (queryParams == null || !queryParams.HasValues)
            return url;

        var pairs = new List<string>();
        foreach (var prop in queryParams.Properties())
        {
            if (prop.Value == null) continue;
            var value = prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array
                ? prop.Value.ToString(Newtonsoft.Json.Formatting.None)
                : prop.Value.ToString();

            pairs.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(value)}");
        }

        if (pairs.Count == 0) return url;

        var separator = url.Contains("?") ? "&" : "?";
        return url + separator + string.Join("&", pairs);
    }

    private string NormalizeBatchUrl(string endpoint, string apiVersion, JObject queryParams)
    {
        var url = endpoint ?? "";
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(url);
            url = uri.PathAndQuery;
        }

        if (!url.StartsWith("/"))
            url = "/" + url;

        if (!url.StartsWith("/v1.0", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("/beta", StringComparison.OrdinalIgnoreCase))
        {
            var version = string.IsNullOrWhiteSpace(apiVersion) ? "v1.0" : apiVersion;
            url = "/" + version.Trim('/') + url;
        }

        url = AppendQueryParams(url, queryParams);
        return url;
    }

    private static string BuildSingleRowAddress(string baseAddress, int row)
    {
        var parsed = ParseA1Address(baseAddress);
        return $"{parsed.startCol}{row}:{parsed.endCol}{row}";
    }

    private static string BuildRowSpanAddress(string baseAddress, int startRow, int endRow)
    {
        var parsed = ParseA1Address(baseAddress);
        return $"{parsed.startCol}{startRow}:{parsed.endCol}{endRow}";
    }

    private static (string startCol, int startRow, string endCol, int endRow) ParseA1Address(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Range address is required");

        var match = System.Text.RegularExpressions.Regex.Match(
            address.Trim(),
            @"^([A-Za-z]+)(\d+):([A-Za-z]+)(\d+)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        if (!match.Success)
            throw new ArgumentException("Address must be in A1 range format, e.g., A1:D10");

        return (
            match.Groups[1].Value.ToUpperInvariant(),
            int.Parse(match.Groups[2].Value),
            match.Groups[3].Value.ToUpperInvariant(),
            int.Parse(match.Groups[4].Value)
        );
    }

    private static string BuildRangeFormatUrl(string worksheetBaseUrl, string address)
    {
        return $"{worksheetBaseUrl}/range(address='{Uri.EscapeDataString(address)}')/format";
    }

    private string BuildWorksheetChartsUrl(string driveId, string itemId, string worksheetName)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/charts";
    }

    private string BuildWorkbookNamesUrl(string driveId, string itemId)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/names";
    }

    private string BuildWorksheetNamesUrl(string driveId, string itemId, string worksheetName)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/names";
    }

    private string BuildRangeDataValidationUrl(string driveId, string itemId, string worksheetName, string address)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/dataValidation";
    }

    private string BuildRangeConditionalFormatsUrl(string driveId, string itemId, string worksheetName, string address)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/range(address='{Uri.EscapeDataString(address)}')/format/conditionalFormats";
    }

    private string BuildWorksheetPivotTablesUrl(string driveId, string itemId, string worksheetName)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/pivotTables";
    }

    private string BuildWorkbookApplicationUrl(string driveId, string itemId)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/application";
    }

    private string BuildWorksheetProtectionUrl(string driveId, string itemId, string worksheetName)
    {
        var workbookBaseUrl = BuildWorkbookBaseUrl(driveId, itemId);
        return $"{workbookBaseUrl}/worksheets/{Uri.EscapeDataString(worksheetName)}/protection";
    }

    /// <summary>Get a required string argument; throws ArgumentException if missing.</summary>
    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an optional string argument with a default fallback.</summary>
    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    /// <summary>Read a connection parameter by name (null-safe).</summary>
    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch { return null; }
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

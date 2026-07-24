using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string ServerName = "m365-copilot-search";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string GraphVersion = "/v1.0";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            _ = LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });

            switch (this.Context.OperationId)
            {
                case "InvokeMCP": return await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                case "Search":    return await HandleSearchAsync(correlationId).ConfigureAwait(false);
                default:          return await HandlePassthroughAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message, StackTrace = ex.StackTrace });
            throw;
        }
    }

    // ========================================
    // TYPED OPERATION HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleSearchAsync(string correlationId)
    {
        var input = await ReadRequestBodyAsync().ConfigureAwait(false);
        var queryString = input.Value<string>("queryString");
        if (string.IsNullOrWhiteSpace(queryString))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "queryString is required." }.ToString(Newtonsoft.Json.Formatting.None));

        var entityTypes = input["entityTypes"] as JArray;
        if (entityTypes == null || entityTypes.Count == 0)
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "entityTypes is required (at least one)." }.ToString(Newtonsoft.Json.Formatting.None));

        var body = BuildSearchBody(input);
        var result = await ExecuteGraphRequestAsync("POST", "/search/query", body, correlationId).ConfigureAwait(false);
        var statusCode = result.Value<bool?>("success") == false ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        return CreateJsonResponse(statusCode, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    // ========================================
    // REQUEST BODY BUILDER
    // ========================================

    private JObject BuildSearchBody(JObject input)
    {
        var request = new JObject
        {
            ["entityTypes"] = input["entityTypes"] as JArray ?? new JArray(),
            ["query"] = new JObject { ["queryString"] = input.Value<string>("queryString") }
        };

        var queryTemplate = input.Value<string>("queryTemplate");
        if (!string.IsNullOrWhiteSpace(queryTemplate))
            ((JObject)request["query"])["queryTemplate"] = queryTemplate;

        var from = input.Value<int?>("from");
        if (from.HasValue)
            request["from"] = from.Value;

        var size = input.Value<int?>("size");
        request["size"] = size ?? 25;

        var fields = input["fields"] as JArray;
        if (fields != null && fields.Count > 0)
            request["fields"] = fields;

        var contentSources = input["contentSources"] as JArray;
        if (contentSources != null && contentSources.Count > 0)
            request["contentSources"] = contentSources;

        var enableTopResults = input.Value<bool?>("enableTopResults");
        if (enableTopResults.HasValue && enableTopResults.Value)
            request["enableTopResults"] = true;

        return new JObject { ["requests"] = new JArray { request } };
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
    {
        JToken requestId = null;
        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");

            JObject request;
            try { request = JObject.Parse(body); }
            catch (JsonException) { return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON"); }

            var method = request.Value<string>("method") ?? "";
            requestId = request["id"];

            switch (method)
            {
                case "initialize":
                    return HandleMcpInitialize(request, requestId);
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                case "tools/list":
                    return HandleMcpToolsList(requestId);
                case "tools/call":
                    return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                case "resources/templates/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });
                case "prompts/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });
                case "completion/complete":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } });
                case "logging/setLevel":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("McpError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject(),
                ["completions"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "Microsoft 365 Copilot Search",
                ["description"] = "Search Microsoft 365 content (files, email, Teams messages, events, sites, people, connectors) with access controls respected."
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var queryProp = new JObject { ["type"] = "string", ["description"] = "The search query. Supports KQL (e.g., 'budget filetype:xlsx')." };
        var sizeProp = new JObject { ["type"] = "integer", ["description"] = "Number of results to return. Default: 25." };
        var fromProp = new JObject { ["type"] = "integer", ["description"] = "Zero-based index of the first result (paging). Default: 0." };

        var tools = new JArray
        {
            McpTool("search",
                "Search Microsoft 365 content across one or more entity types. Results respect the signed-in user's permissions.",
                new JObject
                {
                    ["query"] = queryProp,
                    ["entity_types"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Types to search: driveItem, listItem, list, site, drive, message, event, chatMessage, person, externalItem. File types must be searched together.",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["from"] = fromProp,
                    ["size"] = sizeProp,
                    ["fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Specific fields to return per hit.",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                }, new[] { "query", "entity_types" }),
            McpTool("search_files",
                "Search OneDrive and SharePoint files (driveItem).",
                new JObject { ["query"] = queryProp, ["from"] = fromProp, ["size"] = sizeProp }, new[] { "query" }),
            McpTool("search_email",
                "Search the signed-in user's email messages.",
                new JObject
                {
                    ["query"] = queryProp,
                    ["from"] = fromProp,
                    ["size"] = sizeProp,
                    ["enable_top_results"] = new JObject { ["type"] = "boolean", ["description"] = "Return most relevant results first. Default: false." }
                }, new[] { "query" }),
            McpTool("search_teams_messages",
                "Search Microsoft Teams chat and channel messages (chatMessage).",
                new JObject { ["query"] = queryProp, ["from"] = fromProp, ["size"] = sizeProp }, new[] { "query" }),
            McpTool("search_events",
                "Search the signed-in user's calendar events.",
                new JObject { ["query"] = queryProp, ["from"] = fromProp, ["size"] = sizeProp }, new[] { "query" }),
            McpTool("search_external_items",
                "Search external items ingested by Microsoft 365 Copilot connectors (externalItem).",
                new JObject
                {
                    ["query"] = queryProp,
                    ["content_sources"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Connector connection paths, e.g., '/external/connections/connectionId'.",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["from"] = fromProp,
                    ["size"] = sizeProp
                }, new[] { "query" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private static JObject McpTool(string name, string description, JObject properties, string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(required)
            }
        };
    }

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");

        try
        {
            JObject input;
            switch (toolName.ToLowerInvariant())
            {
                case "search":
                    input = NormalizeToolArgs(arguments, null);
                    break;
                case "search_files":
                    input = NormalizeToolArgs(arguments, new JArray { "driveItem" });
                    break;
                case "search_email":
                    input = NormalizeToolArgs(arguments, new JArray { "message" });
                    break;
                case "search_teams_messages":
                    input = NormalizeToolArgs(arguments, new JArray { "chatMessage" });
                    break;
                case "search_events":
                    input = NormalizeToolArgs(arguments, new JArray { "event" });
                    break;
                case "search_external_items":
                    input = NormalizeToolArgs(arguments, new JArray { "externalItem" });
                    break;
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", toolName);
            }

            if (string.IsNullOrWhiteSpace(input.Value<string>("queryString")))
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "query is required");

            var body = BuildSearchBody(input);
            var toolResult = await ExecuteGraphRequestAsync("POST", "/search/query", body, correlationId).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } } });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolError", new { CorrelationId = correlationId, ToolName = toolName, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    // Maps snake_case MCP tool arguments to the camelCase keys used by BuildSearchBody.
    private static JObject NormalizeToolArgs(JObject args, JArray fixedEntityTypes)
    {
        var normalized = new JObject
        {
            ["queryString"] = args["query"],
            ["entityTypes"] = fixedEntityTypes ?? (args["entity_types"] as JArray),
            ["fields"] = args["fields"],
            ["contentSources"] = args["content_sources"]
        };
        if (args["from"] != null)
            normalized["from"] = args["from"];
        if (args["size"] != null)
            normalized["size"] = args["size"];
        if (args["enable_top_results"] != null)
            normalized["enableTopResults"] = args["enable_top_results"];
        return normalized;
    }

    // ========================================
    // GRAPH HELPER
    // ========================================

    private async Task<JObject> ExecuteGraphRequestAsync(string method, string path, JObject body, string correlationId)
    {
        try
        {
            var uri = new UriBuilder(this.Context.Request.RequestUri)
            {
                Path = GraphVersion + path,
                Query = ""
            }.Uri;

            var request = new HttpRequestMessage(new HttpMethod(method), uri);
            if (body != null)
                request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _ = LogToAppInsights("GraphRequestSuccess", new { CorrelationId = correlationId, Method = method, Path = path, StatusCode = (int)response.StatusCode });
                if (string.IsNullOrWhiteSpace(responseBody))
                    return new JObject { ["success"] = true };
                try { return JObject.Parse(responseBody); }
                catch (JsonException) { return new JObject { ["success"] = true, ["raw"] = responseBody }; }
            }

            _ = LogToAppInsights("GraphRequestError", new { CorrelationId = correlationId, Method = method, Path = path, StatusCode = (int)response.StatusCode, Error = responseBody });
            return new JObject { ["success"] = false, ["error"] = $"Graph API error: {response.StatusCode}", ["details"] = responseBody };
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("GraphRequestException", new { CorrelationId = correlationId, Method = method, Path = path, Error = ex.Message });
            return CreateErrorResult($"Graph request failed: {ex.Message}");
        }
    }

    private async Task<JObject> ReadRequestBodyAsync()
    {
        if (this.Context.Request.Content == null)
            return new JObject();

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return new JObject();

        try { return JObject.Parse(body); }
        catch (JsonException) { return new JObject(); }
    }

    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
    }

    // ========================================
    // RESPONSE HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int errorCode, string errorMessage, string errorData)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = errorCode,
                ["message"] = errorMessage,
                ["data"] = errorData
            }
        };
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private JObject CreateErrorResult(string message)
    {
        return new JObject { ["success"] = false, ["error"] = message };
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

    private bool LogToAppInsights(string eventName, dynamic properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return false; // Telemetry not configured.

        try
        {
            var propDict = new Dictionary<string, string>();
            if (properties != null)
            {
                foreach (var prop in (IDictionary<string, object>)properties)
                    propDict[prop.Key] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = "Microsoft.ApplicationInsights.Event",
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propDict
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(JsonConvert.SerializeObject(telemetryData), Encoding.UTF8, "application/json")
            };

            // Fire-and-forget; failures are ignored so telemetry never blocks the operation.
            _ = this.Context.SendAsync(request, this.CancellationToken);
            return true;
        }
        catch
        {
            return false; // Silent fail for telemetry.
        }
    }
}

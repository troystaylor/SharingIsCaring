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
    private const string ServerName = "m365-copilot-chat";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string GraphVersion = "/beta";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            _ = LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });

            switch (this.Context.OperationId)
            {
                case "InvokeMCP":         return await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                case "AskCopilot":        return await HandleAskCopilotAsync(correlationId).ConfigureAwait(false);
                case "CreateConversation": return await HandleCreateConversationAsync(correlationId).ConfigureAwait(false);
                case "SendChatMessage":   return await HandleSendChatMessageAsync(correlationId).ConfigureAwait(false);
                default:                  return await HandlePassthroughAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new { CorrelationId = correlationId, ErrorMessage = ex.Message, StackTrace = ex.StackTrace });
            throw;
        }
    }

    // ========================================
    // TYPED OPERATION HANDLERS
    // ========================================

    private async Task<HttpResponseMessage> HandleCreateConversationAsync(string correlationId)
    {
        var result = await ExecuteGraphRequestAsync("POST", "/copilot/conversations", new JObject(), correlationId).ConfigureAwait(false);
        var statusCode = result.Value<bool?>("success") == false ? HttpStatusCode.BadRequest : HttpStatusCode.Created;
        return CreateJsonResponse(statusCode, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<HttpResponseMessage> HandleSendChatMessageAsync(string correlationId)
    {
        var conversationId = ExtractConversationIdFromPath();
        if (string.IsNullOrWhiteSpace(conversationId))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "conversationId is required in the path." }.ToString(Newtonsoft.Json.Formatting.None));

        var input = await ReadRequestBodyAsync().ConfigureAwait(false);
        var text = input.Value<string>("text");
        if (string.IsNullOrWhiteSpace(text))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "text is required." }.ToString(Newtonsoft.Json.Formatting.None));

        var chatBody = BuildChatBody(input);
        var result = await ExecuteGraphRequestAsync("POST", $"/copilot/conversations/{conversationId}/chat", chatBody, correlationId).ConfigureAwait(false);
        var statusCode = result.Value<bool?>("success") == false ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        return CreateJsonResponse(statusCode, result.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<HttpResponseMessage> HandleAskCopilotAsync(string correlationId)
    {
        var input = await ReadRequestBodyAsync().ConfigureAwait(false);
        var text = input.Value<string>("text");
        if (string.IsNullOrWhiteSpace(text))
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = "text is required." }.ToString(Newtonsoft.Json.Formatting.None));

        var askResult = await AskCopilotCoreAsync(input, correlationId).ConfigureAwait(false);
        var statusCode = askResult.Value<bool?>("success") == false ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        return CreateJsonResponse(statusCode, askResult.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<JObject> AskCopilotCoreAsync(JObject input, string correlationId)
    {
        var conversation = await ExecuteGraphRequestAsync("POST", "/copilot/conversations", new JObject(), correlationId).ConfigureAwait(false);
        if (conversation.Value<bool?>("success") == false)
            return conversation;

        var conversationId = conversation.Value<string>("id");
        if (string.IsNullOrWhiteSpace(conversationId))
            return CreateErrorResult("Failed to create conversation: no conversation ID returned.");

        var chatBody = BuildChatBody(input);
        var chatResult = await ExecuteGraphRequestAsync("POST", $"/copilot/conversations/{conversationId}/chat", chatBody, correlationId).ConfigureAwait(false);
        if (chatResult.Value<bool?>("success") == false)
            return chatResult;

        return new JObject
        {
            ["conversationId"] = conversationId,
            ["reply"] = ExtractReply(chatResult),
            ["turnCount"] = chatResult["turnCount"],
            ["conversation"] = chatResult
        };
    }

    // ========================================
    // REQUEST BODY BUILDERS
    // ========================================

    private JObject BuildChatBody(JObject input)
    {
        var timeZone = input.Value<string>("timeZone");
        if (string.IsNullOrWhiteSpace(timeZone))
            timeZone = "UTC";

        var body = new JObject
        {
            ["message"] = new JObject { ["text"] = input.Value<string>("text") },
            ["locationHint"] = new JObject { ["timeZone"] = timeZone }
        };

        var additionalContext = input["additionalContext"] as JArray;
        if (additionalContext != null && additionalContext.Count > 0)
        {
            var contextArray = new JArray();
            foreach (var item in additionalContext)
            {
                var contextText = item?.ToString();
                if (!string.IsNullOrWhiteSpace(contextText))
                    contextArray.Add(new JObject { ["text"] = contextText });
            }
            if (contextArray.Count > 0)
                body["additionalContext"] = contextArray;
        }

        var contextualResources = new JObject();

        var fileUris = input["fileUris"] as JArray;
        if (fileUris != null && fileUris.Count > 0)
        {
            var files = new JArray();
            foreach (var uri in fileUris)
            {
                var uriText = uri?.ToString();
                if (!string.IsNullOrWhiteSpace(uriText))
                    files.Add(new JObject { ["uri"] = uriText });
            }
            if (files.Count > 0)
                contextualResources["files"] = files;
        }

        var isWebEnabled = input.Value<bool?>("isWebEnabled");
        if (isWebEnabled.HasValue && isWebEnabled.Value == false)
            contextualResources["webContext"] = new JObject { ["isWebEnabled"] = false };

        if (contextualResources.HasValues)
            body["contextualResources"] = contextualResources;

        return body;
    }

    private static string ExtractReply(JObject conversation)
    {
        var messages = conversation["messages"] as JArray;
        if (messages == null || messages.Count == 0)
            return "";

        // Copilot's reply is the last message in the conversation turn.
        var last = messages[messages.Count - 1] as JObject;
        return last?.Value<string>("text") ?? "";
    }

    private string ExtractConversationIdFromPath()
    {
        // Path form: /beta/copilot/conversations/{conversationId}/chat
        var segments = this.Context.Request.RequestUri.Segments
            .Select(s => s.TrimEnd('/'))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var idx = segments.FindIndex(s => string.Equals(s, "conversations", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < segments.Count)
            return Uri.UnescapeDataString(segments[idx + 1]);

        return null;
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
                ["title"] = "Microsoft 365 Copilot Chat",
                ["description"] = "Send prompts to Microsoft 365 Copilot and receive grounded, enterprise-aware responses."
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("ask_copilot",
                "Ask Microsoft 365 Copilot a question in a new conversation and get the reply. Grounded in the signed-in user's enterprise data (email, calendar, chats, files) with access controls respected.",
                new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "The prompt to send to Copilot." },
                    ["time_zone"] = new JObject { ["type"] = "string", ["description"] = "IANA time zone for time-relative prompts (e.g., 'America/New_York'). Default: UTC." },
                    ["additional_context"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Extra free-text grounding (e.g., excerpts or facts).",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["file_uris"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "OneDrive/SharePoint file URLs to use as grounding context.",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["is_web_enabled"] = new JObject { ["type"] = "boolean", ["description"] = "Allow web search grounding. Set false for enterprise-only. Default: true." }
                }, new[] { "text" }),
            McpTool("create_conversation",
                "Create a new Microsoft 365 Copilot conversation and return its ID for multi-turn chat.",
                new JObject(), new string[0]),
            McpTool("send_message",
                "Send a prompt to an existing Copilot conversation (multi-turn). Requires a conversation ID from create_conversation.",
                new JObject
                {
                    ["conversation_id"] = new JObject { ["type"] = "string", ["description"] = "The conversation ID to continue." },
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "The prompt to send to Copilot." },
                    ["time_zone"] = new JObject { ["type"] = "string", ["description"] = "IANA time zone for time-relative prompts. Default: UTC." },
                    ["additional_context"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Extra free-text grounding.",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["file_uris"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "OneDrive/SharePoint file URLs to use as grounding context.",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["is_web_enabled"] = new JObject { ["type"] = "boolean", ["description"] = "Allow web search grounding. Default: true." }
                }, new[] { "conversation_id", "text" })
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
            JObject toolResult;
            switch (toolName.ToLowerInvariant())
            {
                case "ask_copilot":
                    toolResult = await AskCopilotCoreAsync(NormalizeToolArgs(arguments), correlationId).ConfigureAwait(false);
                    break;
                case "create_conversation":
                    toolResult = await ExecuteGraphRequestAsync("POST", "/copilot/conversations", new JObject(), correlationId).ConfigureAwait(false);
                    break;
                case "send_message":
                    toolResult = await ExecuteSendMessageToolAsync(arguments, correlationId).ConfigureAwait(false);
                    break;
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", toolName);
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } } });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolError", new { CorrelationId = correlationId, ToolName = toolName, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private async Task<JObject> ExecuteSendMessageToolAsync(JObject args, string correlationId)
    {
        var conversationId = args.Value<string>("conversation_id");
        if (string.IsNullOrWhiteSpace(conversationId))
            return CreateErrorResult("conversation_id is required.");

        var input = NormalizeToolArgs(args);
        if (string.IsNullOrWhiteSpace(input.Value<string>("text")))
            return CreateErrorResult("text is required.");

        var chatBody = BuildChatBody(input);
        var chatResult = await ExecuteGraphRequestAsync("POST", $"/copilot/conversations/{conversationId}/chat", chatBody, correlationId).ConfigureAwait(false);
        if (chatResult.Value<bool?>("success") == false)
            return chatResult;

        return new JObject
        {
            ["conversationId"] = conversationId,
            ["reply"] = ExtractReply(chatResult),
            ["turnCount"] = chatResult["turnCount"],
            ["conversation"] = chatResult
        };
    }

    // Maps snake_case MCP tool arguments to the camelCase keys used by BuildChatBody.
    private static JObject NormalizeToolArgs(JObject args)
    {
        var normalized = new JObject
        {
            ["text"] = args["text"],
            ["timeZone"] = args["time_zone"],
            ["additionalContext"] = args["additional_context"],
            ["fileUris"] = args["file_uris"]
        };
        if (args["is_web_enabled"] != null)
            normalized["isWebEnabled"] = args["is_web_enabled"];
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

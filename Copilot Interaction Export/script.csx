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
    private const string ServerName = "m365-copilot-interaction-export";
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
                ["title"] = "Microsoft 365 Copilot Interaction Export",
                ["description"] = "Export Microsoft 365 Copilot interaction history (prompts and responses) for compliance and analytics."
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("get_user_interactions",
                "Get all Microsoft 365 Copilot interactions for a user (prompts and responses across Microsoft 365 apps). Requires the AiEnterpriseInteraction.Read.All application permission.",
                new JObject
                {
                    ["user_id"] = new JObject { ["type"] = "string", ["description"] = "The user's object ID or user principal name." },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Number of interactions to return. Recommended: 100." },
                    ["filter"] = new JObject { ["type"] = "string", ["description"] = "Optional OData filter, e.g., \"appClass eq 'IPM.SkypeTeams.Message.Copilot.BizChat'\"." }
                }, new[] { "user_id" }),
            McpTool("get_user_interactions_by_app",
                "Get a user's Microsoft 365 Copilot interactions filtered to a specific app class (e.g., Teams, BizChat).",
                new JObject
                {
                    ["user_id"] = new JObject { ["type"] = "string", ["description"] = "The user's object ID or user principal name." },
                    ["app_class"] = new JObject { ["type"] = "string", ["description"] = "The appClass to filter by, e.g., 'IPM.SkypeTeams.Message.Copilot.BizChat' or 'IPM.SkypeTeams.Message.Copilot.Teams'." },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Number of interactions to return. Recommended: 100." }
                }, new[] { "user_id", "app_class" })
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
            var userId = arguments.Value<string>("user_id");
            if (string.IsNullOrWhiteSpace(userId))
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "user_id is required");

            var top = arguments.Value<int?>("top");
            string filter;

            switch (toolName.ToLowerInvariant())
            {
                case "get_user_interactions":
                    filter = arguments.Value<string>("filter");
                    break;
                case "get_user_interactions_by_app":
                    var appClass = arguments.Value<string>("app_class");
                    if (string.IsNullOrWhiteSpace(appClass))
                        return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "app_class is required");
                    filter = $"appClass eq '{appClass.Replace("'", "''")}'";
                    break;
                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", toolName);
            }

            var path = BuildInteractionsPath(userId, top, filter);
            var toolResult = await ExecuteGraphRequestAsync("GET", path, correlationId).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } } });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolError", new { CorrelationId = correlationId, ToolName = toolName, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private static string BuildInteractionsPath(string userId, int? top, string filter)
    {
        var path = $"/copilot/users/{Uri.EscapeDataString(userId)}/interactionHistory/getAllEnterpriseInteractions";
        var query = new List<string>();
        query.Add("$top=" + (top ?? 100));
        if (!string.IsNullOrWhiteSpace(filter))
            query.Add("$filter=" + Uri.EscapeDataString(filter));
        return path + "?" + string.Join("&", query);
    }

    // ========================================
    // GRAPH HELPER
    // ========================================

    private async Task<JObject> ExecuteGraphRequestAsync(string method, string pathAndQuery, string correlationId)
    {
        try
        {
            var split = pathAndQuery.Split(new[] { '?' }, 2);
            var uri = new UriBuilder(this.Context.Request.RequestUri)
            {
                Path = GraphVersion + split[0],
                Query = split.Length > 1 ? split[1] : ""
            }.Uri;

            var request = new HttpRequestMessage(new HttpMethod(method), uri);

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _ = LogToAppInsights("GraphRequestSuccess", new { CorrelationId = correlationId, Method = method, Path = split[0], StatusCode = (int)response.StatusCode });
                if (string.IsNullOrWhiteSpace(responseBody))
                    return new JObject { ["success"] = true };
                try { return JObject.Parse(responseBody); }
                catch (JsonException) { return new JObject { ["success"] = true, ["raw"] = responseBody }; }
            }

            _ = LogToAppInsights("GraphRequestError", new { CorrelationId = correlationId, Method = method, Path = split[0], StatusCode = (int)response.StatusCode, Error = responseBody });
            return new JObject { ["success"] = false, ["error"] = $"Graph API error: {response.StatusCode}", ["details"] = responseBody };
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("GraphRequestException", new { CorrelationId = correlationId, Method = method, Path = pathAndQuery, Error = ex.Message });
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

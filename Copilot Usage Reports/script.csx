using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string ServerName = "m365-copilot-usage-reports";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private static readonly Regex SafeValue = new Regex("^[A-Za-z0-9]+$", RegexOptions.Compiled);

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            _ = LogToAppInsights("RequestReceived", new { CorrelationId = correlationId, OperationId = this.Context.OperationId });

            switch (this.Context.OperationId)
            {
                case "InvokeMCP":      return await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                case "GetUsageReport": return await HandleGetUsageReportAsync(correlationId).ConfigureAwait(false);
                default:               return await HandlePassthroughAsync().ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> HandleGetUsageReportAsync(string correlationId)
    {
        var query = ParseQuery(this.Context.Request.RequestUri.Query);
        var period = query.ContainsKey("period") ? query["period"] : null;
        var version = query.ContainsKey("version") ? query["version"] : null;
        var format = query.ContainsKey("format") ? query["format"] : null;

        var error = ValidateReportParams(ref period, ref version, ref format);
        if (error != null)
            return CreateJsonResponse(HttpStatusCode.BadRequest, new JObject { ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None));

        // JSON is served by the beta Copilot reports namespace; CSV by v1.0.
        var graphVersion = format == "csv" ? "/v1.0" : "/beta";
        var path = $"/copilot/reports/getMicrosoft365CopilotUsageUserDetail(period='{period}',version='{version}')";

        var uri = new UriBuilder(this.Context.Request.RequestUri) { Path = graphVersion + path, Query = "" }.Uri;
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var mediaType = response.Content?.Headers?.ContentType?.MediaType ?? (format == "csv" ? "text/csv" : "application/json");

        _ = LogToAppInsights("UsageReport", new { CorrelationId = correlationId, Period = period, Version = version, Format = format, StatusCode = (int)response.StatusCode });

        return new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(content ?? "", Encoding.UTF8, mediaType)
        };
    }

    private string ValidateReportParams(ref string period, ref string version, ref string format)
    {
        period = string.IsNullOrWhiteSpace(period) ? "D7" : period.Trim();
        version = string.IsNullOrWhiteSpace(version) ? "v2" : version.Trim().ToLowerInvariant();
        format = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToLowerInvariant();

        if (!SafeValue.IsMatch(period))
            return "Invalid period.";
        if (version != "v1" && version != "v2")
            return "version must be 'v1' or 'v2'.";
        if (format != "json" && format != "csv")
            return "format must be 'json' or 'csv'.";

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;
        foreach (var pair in query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split(new[] { '=' }, 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            result[key] = value;
        }
        return result;
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
                ["title"] = "Microsoft 365 Copilot Usage Reports",
                ["description"] = "Retrieve Microsoft 365 Copilot per-user usage detail and prompt activity."
            }
        });
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("get_usage_report",
                "Get Microsoft 365 Copilot usage detail for enabled users over a period, as JSON. v2 adds prompt counts, active usage days, and agent activity. Requires Reports.Read.All and a supported admin role.",
                new JObject
                {
                    ["period"] = new JObject { ["type"] = "string", ["description"] = "Days to aggregate. v1: D7, D30, D90, D180, ALL. v2: D7, D28, D90, D180, ALL." },
                    ["version"] = new JObject { ["type"] = "string", ["description"] = "Report version: 'v1' or 'v2' (default). v2 includes prompt counts and agent activity." }
                }, new[] { "period" })
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

        if (!string.Equals(toolName, "get_usage_report", StringComparison.OrdinalIgnoreCase))
            return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", toolName);

        try
        {
            var period = arguments.Value<string>("period");
            var version = arguments.Value<string>("version");
            string format = "json";

            var error = ValidateReportParams(ref period, ref version, ref format);
            if (error != null)
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", error);

            var path = $"/copilot/reports/getMicrosoft365CopilotUsageUserDetail(period='{period}',version='{version}')";
            var toolResult = await ExecuteGraphJsonAsync("/beta", path, correlationId).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) } } });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolError", new { CorrelationId = correlationId, ToolName = toolName, Error = ex.Message });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    // ========================================
    // GRAPH HELPERS
    // ========================================

    private async Task<JObject> ExecuteGraphJsonAsync(string graphVersion, string path, string correlationId)
    {
        try
        {
            var uri = new UriBuilder(this.Context.Request.RequestUri) { Path = graphVersion + path, Query = "" }.Uri;
            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _ = LogToAppInsights("GraphRequestSuccess", new { CorrelationId = correlationId, Path = path, StatusCode = (int)response.StatusCode });
                if (string.IsNullOrWhiteSpace(responseBody))
                    return new JObject { ["success"] = true };
                try { return JObject.Parse(responseBody); }
                catch (JsonException) { return new JObject { ["success"] = true, ["raw"] = responseBody }; }
            }

            _ = LogToAppInsights("GraphRequestError", new { CorrelationId = correlationId, Path = path, StatusCode = (int)response.StatusCode, Error = responseBody });
            return new JObject { ["success"] = false, ["error"] = $"Graph API error: {response.StatusCode}", ["details"] = responseBody };
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("GraphRequestException", new { CorrelationId = correlationId, Path = path, Error = ex.Message });
            return new JObject { ["success"] = false, ["error"] = $"Graph request failed: {ex.Message}" };
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

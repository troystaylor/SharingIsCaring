using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string ServerName = "copilot-studio-agent-evaluation";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";

    // Optional hardcoded telemetry (Mission Control style)
    private const bool APP_INSIGHTS_ENABLED = false;
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            if (this.Context.OperationId == "InvokeMCP")
            {
                return await HandleMcpRequestAsync().ConfigureAwait(false);
            }

            // Standard REST operations pass through unchanged.
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync(ex, "ExecuteAsync").ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(new { error = ex.Message }),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: empty request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: invalid JSON");
        }

        var method = request.Value<string>("method");
        var requestId = request["id"];

        await LogToAppInsightsAsync("MCP_Request", new Dictionary<string, string>
        {
            { "method", method ?? string.Empty },
            { "requestId", requestId?.ToString() ?? "null" }
        }).ConfigureAwait(false);

        // JSON-RPC notification: no response body required
        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        switch (method)
        {
            case "initialize":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false },
                        ["resources"] = new JObject { ["listChanged"] = false },
                        ["prompts"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = ServerName,
                        ["version"] = ServerVersion
                    }
                });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCallAsync(request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, $"Method not found: {method}");
        }
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool(
                "get_test_sets",
                "Retrieve all test sets for a specific Copilot Studio agent.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                        ["botId"] = new JObject { ["type"] = "string", ["description"] = "Agent (bot) ID" }
                    },
                    ["required"] = new JArray { "environmentId", "botId" }
                }),

            McpTool(
                "get_test_set_details",
                "Retrieve details of a specific test set for a Copilot Studio agent.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                        ["botId"] = new JObject { ["type"] = "string", ["description"] = "Agent (bot) ID" },
                        ["testSetId"] = new JObject { ["type"] = "string", ["description"] = "Test set ID" }
                    },
                    ["required"] = new JArray { "environmentId", "botId", "testSetId" }
                }),

            McpTool(
                "start_evaluation",
                "Start an asynchronous evaluation run for a test set.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                        ["botId"] = new JObject { ["type"] = "string", ["description"] = "Agent (bot) ID" },
                        ["testSetId"] = new JObject { ["type"] = "string", ["description"] = "Test set ID" },
                        ["mcsConnectionId"] = new JObject { ["type"] = "string", ["description"] = "Optional Copilot Studio connection ID" }
                    },
                    ["required"] = new JArray { "environmentId", "botId", "testSetId" }
                }),

            McpTool(
                "get_run_details",
                "Retrieve detailed results for a specific evaluation run.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                        ["botId"] = new JObject { ["type"] = "string", ["description"] = "Agent (bot) ID" },
                        ["testRunId"] = new JObject { ["type"] = "string", ["description"] = "Test run ID" }
                    },
                    ["required"] = new JArray { "environmentId", "botId", "testRunId" }
                }),

            McpTool(
                "list_test_runs",
                "List all evaluation runs for a specific agent.",
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                        ["botId"] = new JObject { ["type"] = "string", ["description"] = "Agent (bot) ID" }
                    },
                    ["required"] = new JArray { "environmentId", "botId" }
                })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private JObject McpTool(string name, string description, JObject inputSchema)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params");
        }

        var toolName = paramsObj.Value<string>("name");
        var args = paramsObj["arguments"] as JObject ?? new JObject();

        try
        {
            string endpoint;

            switch ((toolName ?? string.Empty).ToLowerInvariant())
            {
                case "get_test_sets":
                    endpoint = BuildEndpoint(
                        args,
                        requiredKeys: new[] { "environmentId", "botId" },
                        pathTemplate: "/environments/{environmentId}/bots/{botId}/api/makerevaluation/testsets");
                    break;

                case "get_test_set_details":
                    endpoint = BuildEndpoint(
                        args,
                        requiredKeys: new[] { "environmentId", "botId", "testSetId" },
                        pathTemplate: "/environments/{environmentId}/bots/{botId}/api/makerevaluation/testsets/{testSetId}");
                    break;

                case "start_evaluation":
                    endpoint = BuildEndpoint(
                        args,
                        requiredKeys: new[] { "environmentId", "botId", "testSetId" },
                        pathTemplate: "/environments/{environmentId}/bots/{botId}/api/makerevaluation/testsets/{testSetId}/run",
                        optionalQuery: new Dictionary<string, string>
                        {
                            { "mcsConnectionId", args.Value<string>("mcsConnectionId") }
                        });
                    break;

                case "get_run_details":
                    endpoint = BuildEndpoint(
                        args,
                        requiredKeys: new[] { "environmentId", "botId", "testRunId" },
                        pathTemplate: "/environments/{environmentId}/bots/{botId}/api/makerevaluation/testruns/{testRunId}");
                    break;

                case "list_test_runs":
                    endpoint = BuildEndpoint(
                        args,
                        requiredKeys: new[] { "environmentId", "botId" },
                        pathTemplate: "/environments/{environmentId}/bots/{botId}/api/makerevaluation/testruns");
                    break;

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32602, $"Unknown tool: {toolName}");
            }

            var response = await InvokeBackendGetAsync(endpoint).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            JToken parsed;
            try { parsed = JToken.Parse(content); }
            catch { parsed = new JValue(content ?? string.Empty); }

            await LogToAppInsightsAsync("MCP_ToolCall", new Dictionary<string, string>
            {
                { "tool", toolName ?? string.Empty },
                { "status", ((int)response.StatusCode).ToString() }
            }).ConfigureAwait(false);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = parsed.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = !response.IsSuccessStatusCode
            });
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync(ex, "HandleToolsCallAsync").ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    private string BuildEndpoint(
        JObject args,
        string[] requiredKeys,
        string pathTemplate,
        IDictionary<string, string> optionalQuery = null)
    {
        foreach (var key in requiredKeys)
        {
            if (string.IsNullOrWhiteSpace(args.Value<string>(key)))
            {
                throw new ArgumentException($"Missing required argument: {key}");
            }
        }

        var path = pathTemplate;
        foreach (var key in requiredKeys)
        {
            path = path.Replace("{" + key + "}", Uri.EscapeDataString(args.Value<string>(key)));
        }

        var queryParts = new List<string> { "api-version=2024-10-01" };
        if (optionalQuery != null)
        {
            foreach (var kvp in optionalQuery)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    queryParts.Add(Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value));
                }
            }
        }

        return path + "?" + string.Join("&", queryParts);
    }

    private async Task<HttpResponseMessage> InvokeBackendGetAsync(string endpointWithQuery)
    {
        var url = "https://api.powerplatform.com/copilotstudio" + endpointWithQuery;
        var outbound = new HttpRequestMessage(HttpMethod.Get, url);

        if (this.Context.Request.Headers.Contains("Authorization"))
        {
            outbound.Headers.Add("Authorization", this.Context.Request.Headers.GetValues("Authorization"));
        }

        return await this.Context.SendAsync(outbound, this.CancellationToken).ConfigureAwait(false);
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = result
                }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JObject
                    {
                        ["code"] = code,
                        ["message"] = message
                    }
                }.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static bool IsAppInsightsConfigured()
    {
        return APP_INSIGHTS_ENABLED
            && !string.IsNullOrEmpty(APP_INSIGHTS_KEY)
            && !APP_INSIGHTS_KEY.Contains("INSERT_YOUR");
    }

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (!IsAppInsightsConfigured())
            return;

        try
        {
            var payload = new
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
                        properties = properties ?? new Dictionary<string, string>()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail for telemetry
        }
    }

    private async Task LogExceptionToAppInsightsAsync(Exception ex, string operation)
    {
        if (!IsAppInsightsConfigured())
            return;

        try
        {
            var payload = new
            {
                name = "Microsoft.ApplicationInsights.Exception",
                time = DateTime.UtcNow.ToString("O"),
                iKey = APP_INSIGHTS_KEY,
                data = new
                {
                    baseType = "ExceptionData",
                    baseData = new
                    {
                        ver = 2,
                        exceptions = new[]
                        {
                            new
                            {
                                typeName = ex.GetType().Name,
                                message = ex.Message,
                                hasFullStack = true,
                                stack = ex.StackTrace
                            }
                        },
                        severityLevel = 3,
                        properties = new Dictionary<string, string>
                        {
                            { "operation", operation },
                            { "connector", "Copilot Studio Agent Evaluation" }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail for telemetry
        }
    }
}

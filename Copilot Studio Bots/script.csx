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
    private const string ServerName = "copilot-studio-bots";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string ApiVersion = "2024-10-01";
    private const string BackendBaseUrl = "https://api.powerplatform.com/copilotstudio";
    private const int MaxInlineSnapshotBytes = 4 * 1024 * 1024;

    private const string BotPath = "/environments/{environmentId}/bots/{botId}";

    // The environment picker is served from a different Power Platform API surface
    // than the Bots operations, so it is addressed absolutely rather than via basePath.
    private const string EnvironmentsUrl =
        "https://api.powerplatform.com/environmentmanagement/environments?api-version=" + ApiVersion;

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

            if (this.Context.OperationId == "GetEnvironmentDropdown")
            {
                return await HandleEnvironmentDropdownAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Backs the environment dropdown. Flattens the environmentmanagement response
    /// into the { id, name } shape that x-ms-dynamic-values expects.
    /// </summary>
    private async Task<HttpResponseMessage> HandleEnvironmentDropdownAsync()
    {
        var outbound = new HttpRequestMessage(HttpMethod.Get, EnvironmentsUrl);
        outbound.Headers.Add("Accept", "application/json");

        if (this.Context.Request.Headers.Contains("Authorization"))
        {
            outbound.Headers.Add("Authorization", this.Context.Request.Headers.GetValues("Authorization"));
        }

        var response = await this.Context.SendAsync(outbound, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(content ?? string.Empty, Encoding.UTF8, "application/json")
            };
        }

        var dropdown = new JArray();
        try
        {
            var environments = JObject.Parse(content)["value"] as JArray ?? new JArray();
            foreach (var env in environments)
            {
                var id = ResolveEnvironmentId(env);
                if (string.IsNullOrEmpty(id)) continue;

                dropdown.Add(new JObject
                {
                    ["id"] = id,
                    ["name"] = env["properties"]?["displayName"]?.ToString() ?? id
                });
            }
        }
        catch
        {
            // An unparseable payload yields an empty picker rather than a broken designer.
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                dropdown.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    /// <summary>
    /// The environments API may report the identifier as a bare GUID in "name" or as an
    /// ARM-style resource path in "id". The Bots operations need the bare GUID either way.
    /// </summary>
    private static string ResolveEnvironmentId(JToken environment)
    {
        var name = environment["name"]?.ToString();
        if (IsGuid(name)) return name;

        var id = environment["id"]?.ToString();
        if (IsGuid(id)) return id;

        if (!string.IsNullOrEmpty(id))
        {
            var segments = id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                if (IsGuid(segments[i])) return segments[i];
            }
        }

        return string.IsNullOrEmpty(name) ? id : name;
    }

    private static bool IsGuid(string value)
    {
        Guid parsed;
        return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out parsed);
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
            // --- Maker evaluation ---
            McpTool(
                "get_test_sets",
                "Retrieve all test sets for a specific Copilot Studio agent.",
                AgentSchema()),

            McpTool(
                "get_test_set_details",
                "Retrieve details of a specific test set for a Copilot Studio agent.",
                AgentSchema(
                    new JObject { ["testSetId"] = Prop("Test set ID") },
                    "testSetId")),

            McpTool(
                "start_evaluation",
                "Start an asynchronous evaluation run for a test set. By default evaluates the draft (unpublished) version.",
                AgentSchema(
                    new JObject
                    {
                        ["testSetId"] = Prop("Test set ID"),
                        ["mcsConnectionId"] = Prop("Optional Copilot Studio connection ID for authenticated evaluation"),
                        ["runOnPublishedBot"] = Prop("When true, evaluates the published version. Defaults to false (draft).", "boolean"),
                        ["evaluationRunName"] = Prop("Optional name for this evaluation run for dashboards and reports.")
                    },
                    "testSetId")),

            McpTool(
                "list_test_runs",
                "List all evaluation runs for a specific agent.",
                AgentSchema()),

            McpTool(
                "get_run_details",
                "Retrieve detailed results for a specific evaluation run.",
                AgentSchema(
                    new JObject { ["testRunId"] = Prop("Test run ID") },
                    "testRunId")),

            McpTool(
                "download_evaluation_snapshot",
                "Download the agent content snapshot captured for an evaluation run as a ZIP file.",
                AgentSchema(
                    new JObject { ["testRunId"] = Prop("Test run ID") },
                    "testRunId")),

            // --- Administration ---
            McpTool(
                "get_quarantine_status",
                "Retrieve the quarantine status of a Copilot Studio agent.",
                AgentSchema()),

            McpTool(
                "quarantine_agent",
                "Quarantine a Copilot Studio agent, blocking end user access while leaving it available to makers and administrators.",
                AgentSchema()),

            McpTool(
                "unquarantine_agent",
                "Release a Copilot Studio agent from quarantine, restoring end user access.",
                AgentSchema()),

            McpTool(
                "get_connector_consent_bypass",
                "Get the admin connector consent bypass setting for a Copilot Studio agent.",
                AgentSchema()),

            McpTool(
                "set_connector_consent_bypass",
                "Set the admin connector consent bypass setting for a Copilot Studio agent. Enabling the bypass suppresses the end user connection consent prompt.",
                AgentSchema(
                    new JObject
                    {
                        ["adminConsentBypass"] = Prop("True to suppress the end user connection consent prompt, false to require consent.", "boolean")
                    },
                    "adminConsentBypass")),

            McpTool(
                "reassign_agent",
                "Reassign ownership of a Copilot Studio agent to a different Microsoft Entra user.",
                AgentSchema(
                    new JObject { ["newOwnerAadUserId"] = Prop("Microsoft Entra object ID of the new owner") },
                    "newOwnerAadUserId")),

            McpTool(
                "delete_agent",
                "Permanently delete a Copilot Studio agent. This cannot be undone, so confirm must be set to true.",
                AgentSchema(
                    new JObject { ["confirm"] = Prop("Must be true to acknowledge that deletion is permanent.", "boolean") },
                    "confirm"))
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private static JObject Prop(string description, string type = "string")
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    /// <summary>
    /// Builds a tool input schema that always requires environmentId and botId,
    /// plus any tool-specific properties and their required keys.
    /// </summary>
    private static JObject AgentSchema(JObject extraProperties = null, params string[] extraRequired)
    {
        var properties = new JObject
        {
            ["environmentId"] = Prop("Environment ID"),
            ["botId"] = Prop("Agent (bot) ID")
        };

        if (extraProperties != null)
        {
            foreach (var p in extraProperties.Properties())
            {
                properties[p.Name] = p.Value;
            }
        }

        var required = new JArray { "environmentId", "botId" };
        if (extraRequired != null)
        {
            foreach (var key in extraRequired)
            {
                required.Add(key);
            }
        }

        return new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
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
            // Binary download is handled separately so the ZIP payload is never read as text.
            if (string.Equals(toolName, "download_evaluation_snapshot", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleSnapshotDownloadAsync(args, requestId).ConfigureAwait(false);
            }

            string endpoint;
            HttpMethod httpMethod;
            JObject requestBody = null;

            switch ((toolName ?? string.Empty).ToLowerInvariant())
            {
                case "get_test_sets":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/makerevaluation/testsets");
                    break;

                case "get_test_set_details":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "testSetId" },
                        BotPath + "/api/makerevaluation/testsets/{testSetId}");
                    break;

                case "start_evaluation":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "testSetId" },
                        BotPath + "/api/makerevaluation/testsets/{testSetId}/run");
                    requestBody = new JObject();
                    if (!string.IsNullOrWhiteSpace(args.Value<string>("mcsConnectionId")))
                        requestBody["mcsConnectionId"] = args.Value<string>("mcsConnectionId");
                    if (args["runOnPublishedBot"] != null)
                        requestBody["RunOnPublishedBot"] = ToBoolean(args["runOnPublishedBot"], "runOnPublishedBot");
                    if (!string.IsNullOrWhiteSpace(args.Value<string>("evaluationRunName")))
                        requestBody["EvaluationRunName"] = args.Value<string>("evaluationRunName");
                    break;

                case "list_test_runs":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/makerevaluation/testruns");
                    break;

                case "get_run_details":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "testRunId" },
                        BotPath + "/api/makerevaluation/testruns/{testRunId}");
                    break;

                case "get_quarantine_status":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botQuarantine");
                    break;

                case "quarantine_agent":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botQuarantine/SetAsQuarantined");
                    break;

                case "unquarantine_agent":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botQuarantine/SetAsUnquarantined");
                    break;

                case "get_connector_consent_bypass":
                    httpMethod = HttpMethod.Get;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/connectorConsentBypass");
                    break;

                case "set_connector_consent_bypass":
                    if (args["adminConsentBypass"] == null)
                    {
                        throw new ArgumentException("Missing required argument: adminConsentBypass");
                    }

                    httpMethod = HttpMethod.Put;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/connectorConsentBypass");
                    requestBody = new JObject
                    {
                        ["adminConsentBypass"] = ToBoolean(args["adminConsentBypass"], "adminConsentBypass")
                    };
                    break;

                case "reassign_agent":
                    httpMethod = HttpMethod.Post;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId", "newOwnerAadUserId" },
                        BotPath + "/api/botAdminOperations/reassign");
                    requestBody = new JObject
                    {
                        ["NewOwnerAadUserId"] = args.Value<string>("newOwnerAadUserId")
                    };
                    break;

                case "delete_agent":
                    if (!ToBoolean(args["confirm"], "confirm"))
                    {
                        throw new ArgumentException("Deleting an agent is permanent. Set confirm to true to proceed.");
                    }

                    httpMethod = HttpMethod.Delete;
                    endpoint = BuildEndpoint(args,
                        new[] { "environmentId", "botId" },
                        BotPath + "/api/botAdminOperations");
                    break;

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32602, $"Unknown tool: {toolName}");
            }

            var response = await InvokeBackendAsync(httpMethod, endpoint, requestBody).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            JToken parsed;
            if (string.IsNullOrWhiteSpace(content))
            {
                // Operations such as reassign return 204 No Content on success.
                parsed = new JObject
                {
                    ["status"] = (int)response.StatusCode,
                    ["succeeded"] = response.IsSuccessStatusCode
                };
            }
            else
            {
                try { parsed = JToken.Parse(content); }
                catch { parsed = new JValue(content); }
            }

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

    private async Task<HttpResponseMessage> HandleSnapshotDownloadAsync(JObject args, JToken requestId)
    {
        var endpoint = BuildEndpoint(args,
            new[] { "environmentId", "botId", "testRunId" },
            BotPath + "/api/makerevaluation/testruns/{testRunId}/snapshot");

        var response = await InvokeBackendAsync(HttpMethod.Get, endpoint, null).ConfigureAwait(false);

        await LogToAppInsightsAsync("MCP_ToolCall", new Dictionary<string, string>
        {
            { "tool", "download_evaluation_snapshot" },
            { "status", ((int)response.StatusCode).ToString() }
        }).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Snapshot download failed with status {(int)response.StatusCode}. {errorText}"
                    }
                },
                ["isError"] = true
            });
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var fileName = GetSnapshotFileName(response, args.Value<string>("testRunId"));

        var summary = new JObject
        {
            ["fileName"] = fileName,
            ["mimeType"] = "application/zip",
            ["sizeBytes"] = bytes.Length
        };

        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = summary.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };

        if (bytes.Length <= MaxInlineSnapshotBytes)
        {
            content.Add(new JObject
            {
                ["type"] = "resource",
                ["resource"] = new JObject
                {
                    ["uri"] = "copilotstudio://evaluation-snapshot/" + fileName,
                    ["mimeType"] = "application/zip",
                    ["blob"] = Convert.ToBase64String(bytes)
                }
            });
        }
        else
        {
            content.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = $"The snapshot is {bytes.Length} bytes, which exceeds the {MaxInlineSnapshotBytes} byte inline limit. Use the Download Agent Evaluation Snapshot REST operation in a flow to save the file to storage."
            });
        }

        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = content,
            ["isError"] = false
        });
    }

    private static string GetSnapshotFileName(HttpResponseMessage response, string testRunId)
    {
        try
        {
            var disposition = response.Content?.Headers?.ContentDisposition;
            var name = disposition?.FileNameStar ?? disposition?.FileName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim('"');
            }
        }
        catch
        {
            // Fall through to the generated name when the header is malformed.
        }

        return $"evaluation-snapshot-{testRunId}.zip";
    }

    private static bool ToBoolean(JToken value, string argumentName)
    {
        if (value == null || value.Type == JTokenType.Null)
        {
            throw new ArgumentException($"Missing required argument: {argumentName}");
        }

        if (value.Type == JTokenType.Boolean)
        {
            return value.Value<bool>();
        }

        bool parsedValue;
        if (bool.TryParse(value.ToString(), out parsedValue))
        {
            return parsedValue;
        }

        throw new ArgumentException($"Argument {argumentName} must be a boolean.");
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

        var queryParts = new List<string> { "api-version=" + ApiVersion };
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

    private async Task<HttpResponseMessage> InvokeBackendAsync(HttpMethod method, string endpointWithQuery, JObject body)
    {
        var outbound = new HttpRequestMessage(method, BackendBaseUrl + endpointWithQuery);

        if (body != null)
        {
            outbound.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

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
                            { "connector", "Copilot Studio Bots" }
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

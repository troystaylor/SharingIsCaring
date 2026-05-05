using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Power A2A Client — Agent2Agent Protocol Connector
/// 
/// Dual-protocol connector:
///   Inbound:  MCP from Copilot Studio (InvokeMCP operation)
///   Outbound: A2A to external agent (all other operations)
///
/// Pre-configured for Work IQ. Edit the A2A_ENDPOINT and OAuth settings
/// in apiProperties.json to target other A2A agents.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // A2A CLIENT CONFIGURATION
    // ========================================

    /// <summary>A2A agent endpoint URL (include trailing slash for JSON-RPC binding).</summary>
    private const string A2A_ENDPOINT = "https://workiq.svc.cloud.microsoft/a2a/";

    /// <summary>Protocol binding: "jsonrpc" (Work IQ, default) or "httpjson" (REST binding).</summary>
    private const string A2A_PROTOCOL_BINDING = "jsonrpc";

    /// <summary>A2A protocol version sent in the A2A-Version header.</summary>
    private const string A2A_VERSION = "1.0";

    /// <summary>Default agent ID for multi-agent gateways (empty = default agent).</summary>
    private const string A2A_DEFAULT_AGENT_ID = "";

    /// <summary>Tenant prefix for multi-tenant endpoints (empty = no tenant).</summary>
    private const string A2A_TENANT = "";

    // ========================================
    // MCP SERVER CONFIGURATION
    // ========================================

    private const string MCP_SERVER_NAME = "agent2agent";
    private const string MCP_SERVER_VERSION = "1.0.0";

    // ========================================
    // APPLICATION INSIGHTS (OPTIONAL)
    // ========================================

    /// <summary>
    /// Application Insights connection string. Leave empty to disable telemetry.
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // ========================================
    // MCP TOOL DEFINITIONS
    // ========================================

    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "send_message",
            ["description"] = "Send a natural language message to the A2A agent and wait for a response. Returns the agent's completed answer.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["message"] = new JObject { ["type"] = "string", ["description"] = "The message to send to the agent." },
                    ["context_id"] = new JObject { ["type"] = "string", ["description"] = "Context ID for multi-turn conversations. Pass from a previous response." },
                    ["agent_id"] = new JObject { ["type"] = "string", ["description"] = "Target a specific agent on multi-agent gateways." },
                    ["time_zone"] = new JObject { ["type"] = "string", ["description"] = "IANA time zone (e.g., America/Los_Angeles) for time-sensitive queries." },
                    ["time_zone_offset"] = new JObject { ["type"] = "integer", ["description"] = "UTC offset in minutes (e.g., -480 for PST)." }
                },
                ["required"] = new JArray { "message" }
            }
        },
        new JObject
        {
            ["name"] = "send_message_async",
            ["description"] = "Send a message to the A2A agent and return immediately with a task ID. Use get_task to poll for the result.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["message"] = new JObject { ["type"] = "string", ["description"] = "The message to send to the agent." },
                    ["context_id"] = new JObject { ["type"] = "string", ["description"] = "Context ID for multi-turn conversations." },
                    ["agent_id"] = new JObject { ["type"] = "string", ["description"] = "Target a specific agent on multi-agent gateways." },
                    ["time_zone"] = new JObject { ["type"] = "string", ["description"] = "IANA time zone for time-sensitive queries." },
                    ["time_zone_offset"] = new JObject { ["type"] = "integer", ["description"] = "UTC offset in minutes." }
                },
                ["required"] = new JArray { "message" }
            }
        },
        new JObject
        {
            ["name"] = "get_task",
            ["description"] = "Get the current status and artifacts of a task by its ID. Use after send_message_async to poll for completion.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["task_id"] = new JObject { ["type"] = "string", ["description"] = "The task ID to retrieve." },
                    ["history_length"] = new JObject { ["type"] = "integer", ["description"] = "Max messages from history to include." }
                },
                ["required"] = new JArray { "task_id" }
            }
        },
        new JObject
        {
            ["name"] = "list_tasks",
            ["description"] = "List tasks filtered by context or state.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["context_id"] = new JObject { ["type"] = "string", ["description"] = "Filter by context ID." },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "Filter by state (submitted, working, completed, failed, canceled, input_required, auth_required, rejected)." },
                    ["page_size"] = new JObject { ["type"] = "integer", ["description"] = "Max tasks to return (1-100)." }
                }
            }
        },
        new JObject
        {
            ["name"] = "cancel_task",
            ["description"] = "Cancel an in-progress task.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["task_id"] = new JObject { ["type"] = "string", ["description"] = "The task ID to cancel." }
                },
                ["required"] = new JArray { "task_id" }
            }
        },
        new JObject
        {
            ["name"] = "get_agent_card",
            ["description"] = "Discover the agent's identity, skills, capabilities, and authentication requirements.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject {}
            }
        }
    };

    // ========================================
    // MAIN ENTRY POINT
    // ========================================

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var operationId = this.Context.OperationId;

        try
        {
            switch (operationId)
            {
                case "InvokeMCP":
                    return await HandleMcpRequestAsync().ConfigureAwait(false);

                case "SendMessage":
                    return await HandleSendMessageAsync(returnImmediately: false).ConfigureAwait(false);

                case "SendMessageAsync":
                    return await HandleSendMessageAsync(returnImmediately: true).ConfigureAwait(false);

                case "GetTask":
                    return await HandleGetTaskAsync().ConfigureAwait(false);

                case "ListTasks":
                    return await HandleListTasksAsync().ConfigureAwait(false);

                case "CancelTask":
                    return await HandleCancelTaskAsync().ConfigureAwait(false);

                case "GetAgentCard":
                    return await HandleGetAgentCardAsync().ConfigureAwait(false);

                case "CreatePushNotificationConfig":
                    return await HandleCreatePushConfigAsync().ConfigureAwait(false);

                case "GetPushNotificationConfig":
                    return await HandleGetPushConfigAsync().ConfigureAwait(false);

                case "ListPushNotificationConfigs":
                    return await HandleListPushConfigsAsync().ConfigureAwait(false);

                case "DeletePushNotificationConfig":
                    return await HandleDeletePushConfigAsync().ConfigureAwait(false);

                default:
                    return CreateResponse(HttpStatusCode.BadRequest, new JObject
                    {
                        ["error"] = $"Unknown operation: {operationId}"
                    });
            }
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("OperationError", new Dictionary<string, string>
            {
                ["OperationId"] = operationId,
                ["Error"] = ex.Message
            });

            return CreateResponse(HttpStatusCode.InternalServerError, new JObject
            {
                ["error"] = ex.Message
            });
        }
    }

    // ========================================
    // REST OPERATION HANDLERS
    // ========================================

    private async Task<HttpResponseMessage> HandleSendMessageAsync(bool returnImmediately)
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var message = RequireField(body, "message");
        var contextId = body.Value<string>("contextId");
        var agentId = body.Value<string>("agentId") ?? A2A_DEFAULT_AGENT_ID;
        var timeZone = body.Value<string>("timeZone");
        var timeZoneOffset = body.Value<int?>("timeZoneOffset");
        var historyLength = body.Value<int?>("historyLength");
        var acceptedOutputModes = body["acceptedOutputModes"] as JArray;

        var a2aMessage = BuildA2AMessage(message, contextId, agentId, timeZone, timeZoneOffset);
        var a2aParams = new JObject { ["message"] = a2aMessage };

        if (returnImmediately || historyLength.HasValue || acceptedOutputModes != null)
        {
            var config = new JObject();
            if (returnImmediately) config["return_immediately"] = true;
            if (historyLength.HasValue) config["history_length"] = historyLength.Value;
            if (acceptedOutputModes != null) config["accepted_output_modes"] = acceptedOutputModes;
            a2aParams["configuration"] = config;
        }

        var result = await SendA2ARequestAsync("SendMessage", a2aParams).ConfigureAwait(false);
        return CreateResponse(HttpStatusCode.OK, NormalizeTaskResponse(result));
    }

    private async Task<HttpResponseMessage> HandleGetTaskAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var taskId = RequireField(body, "taskId");
        var historyLength = body.Value<int?>("historyLength");

        var a2aParams = new JObject { ["id"] = taskId };
        if (historyLength.HasValue) a2aParams["history_length"] = historyLength.Value;

        var result = await SendA2ARequestAsync("GetTask", a2aParams).ConfigureAwait(false);
        return CreateResponse(HttpStatusCode.OK, NormalizeTaskResponse(result));
    }

    private async Task<HttpResponseMessage> HandleListTasksAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var a2aParams = new JObject();

        var contextId = body.Value<string>("contextId");
        if (!string.IsNullOrWhiteSpace(contextId)) a2aParams["context_id"] = contextId;

        var state = body.Value<string>("state");
        if (!string.IsNullOrWhiteSpace(state)) a2aParams["status"] = MapStateToEnum(state);

        var pageSize = body.Value<int?>("pageSize");
        if (pageSize.HasValue) a2aParams["page_size"] = pageSize.Value;

        var pageToken = body.Value<string>("pageToken");
        if (!string.IsNullOrWhiteSpace(pageToken)) a2aParams["page_token"] = pageToken;

        var result = await SendA2ARequestAsync("ListTasks", a2aParams).ConfigureAwait(false);

        var tasks = new JArray();
        var rawTasks = result["tasks"] as JArray ?? new JArray();
        foreach (var t in rawTasks)
        {
            tasks.Add(NormalizeTaskResponse(new JObject { ["task"] = t }));
        }

        return CreateResponse(HttpStatusCode.OK, new JObject
        {
            ["tasks"] = tasks,
            ["totalSize"] = result.Value<int?>("total_size") ?? tasks.Count,
            ["nextPageToken"] = result.Value<string>("next_page_token")
        });
    }

    private async Task<HttpResponseMessage> HandleCancelTaskAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var taskId = RequireField(body, "taskId");

        var result = await SendA2ARequestAsync("CancelTask", new JObject { ["id"] = taskId }).ConfigureAwait(false);
        return CreateResponse(HttpStatusCode.OK, NormalizeTaskResponse(result));
    }

    private async Task<HttpResponseMessage> HandleGetAgentCardAsync()
    {
        var cardUrl = A2A_ENDPOINT.TrimEnd('/');
        var baseUri = new Uri(cardUrl);
        var wellKnownUrl = $"{baseUri.Scheme}://{baseUri.Host}/.well-known/agent-card.json";

        var request = new HttpRequestMessage(HttpMethod.Get, wellKnownUrl);
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Agent card request failed ({(int)response.StatusCode}): {content}");

        var card = JObject.Parse(content);
        return CreateResponse(HttpStatusCode.OK, card);
    }

    // ── Push Notification Handlers ──────────────────────────────────────

    private async Task<HttpResponseMessage> HandleCreatePushConfigAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var taskId = RequireField(body, "taskId");
        var url = RequireField(body, "url");

        var a2aParams = new JObject
        {
            ["task_id"] = taskId,
            ["url"] = url
        };

        var token = body.Value<string>("token");
        if (!string.IsNullOrWhiteSpace(token)) a2aParams["token"] = token;

        var authScheme = body.Value<string>("authScheme");
        var authCreds = body.Value<string>("authCredentials");
        if (!string.IsNullOrWhiteSpace(authScheme))
        {
            a2aParams["authentication"] = new JObject
            {
                ["scheme"] = authScheme,
                ["credentials"] = authCreds ?? ""
            };
        }

        var result = await SendA2ARequestAsync("CreateTaskPushNotificationConfig", a2aParams).ConfigureAwait(false);
        return CreateResponse(HttpStatusCode.OK, new JObject
        {
            ["id"] = result.Value<string>("id"),
            ["taskId"] = result.Value<string>("task_id"),
            ["url"] = result.Value<string>("url")
        });
    }

    private async Task<HttpResponseMessage> HandleGetPushConfigAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var taskId = RequireField(body, "taskId");
        var configId = RequireField(body, "configId");

        var result = await SendA2ARequestAsync("GetTaskPushNotificationConfig",
            new JObject { ["task_id"] = taskId, ["id"] = configId }).ConfigureAwait(false);

        return CreateResponse(HttpStatusCode.OK, new JObject
        {
            ["id"] = result.Value<string>("id"),
            ["taskId"] = result.Value<string>("task_id"),
            ["url"] = result.Value<string>("url")
        });
    }

    private async Task<HttpResponseMessage> HandleListPushConfigsAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var taskId = RequireField(body, "taskId");

        var result = await SendA2ARequestAsync("ListTaskPushNotificationConfigs",
            new JObject { ["task_id"] = taskId }).ConfigureAwait(false);

        var configs = new JArray();
        foreach (var c in result["configs"] as JArray ?? new JArray())
        {
            configs.Add(new JObject
            {
                ["id"] = c.Value<string>("id"),
                ["taskId"] = c.Value<string>("task_id"),
                ["url"] = c.Value<string>("url")
            });
        }

        return CreateResponse(HttpStatusCode.OK, new JObject
        {
            ["configs"] = configs,
            ["nextPageToken"] = result.Value<string>("next_page_token")
        });
    }

    private async Task<HttpResponseMessage> HandleDeletePushConfigAsync()
    {
        var body = await ReadBodyAsync().ConfigureAwait(false);
        var taskId = RequireField(body, "taskId");
        var configId = RequireField(body, "configId");

        await SendA2ARequestAsync("DeleteTaskPushNotificationConfig",
            new JObject { ["task_id"] = taskId, ["id"] = configId }).ConfigureAwait(false);

        return CreateResponse(HttpStatusCode.OK, new JObject { ["success"] = true });
    }

    // ========================================
    // A2A CLIENT CORE — DUAL BINDING SUPPORT
    // ========================================

    private async Task<JObject> SendA2ARequestAsync(string method, JObject a2aParams, int retryCount = 0)
    {
        HttpRequestMessage request;

        if (A2A_PROTOCOL_BINDING == "httpjson")
        {
            request = BuildHttpJsonRequest(method, a2aParams);
        }
        else
        {
            request = BuildJsonRpcRequest(method, a2aParams);
        }

        // Forward connector auth
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        // A2A-Version header is injected by policy, but also set here for robustness
        if (!request.Headers.Contains("A2A-Version"))
            request.Headers.TryAddWithoutValidation("A2A-Version", A2A_VERSION);

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Handle 429 with retry
        if ((int)response.StatusCode == 429 && retryCount < 3)
        {
            var retryAfter = 5;
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (int.TryParse(val, out var seconds))
                    retryAfter = Math.Min(seconds, 30);
            }
            await Task.Delay(retryAfter * 1000).ConfigureAwait(false);
            return await SendA2ARequestAsync(method, a2aParams, retryCount + 1).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
            throw new Exception($"A2A request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject();

        var parsed = JObject.Parse(content);

        // JSON-RPC: unwrap result/error envelope
        if (A2A_PROTOCOL_BINDING != "httpjson" && parsed["jsonrpc"] != null)
        {
            if (parsed["error"] != null)
            {
                var errMsg = parsed["error"].Value<string>("message") ?? "Unknown A2A error";
                var errCode = parsed["error"].Value<int?>("code") ?? -1;
                throw new Exception($"A2A error ({errCode}): {errMsg}");
            }
            return parsed["result"] as JObject ?? new JObject();
        }

        return parsed;
    }

    private HttpRequestMessage BuildJsonRpcRequest(string method, JObject a2aParams)
    {
        var endpoint = A2A_ENDPOINT;
        if (!string.IsNullOrWhiteSpace(A2A_TENANT))
        {
            a2aParams["tenant"] = A2A_TENANT;
        }

        var envelope = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["method"] = method,
            ["params"] = a2aParams
        };

        return new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                envelope.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    private HttpRequestMessage BuildHttpJsonRequest(string method, JObject a2aParams)
    {
        var baseUrl = A2A_ENDPOINT.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(A2A_TENANT))
            baseUrl = $"{baseUrl}/{A2A_TENANT}";

        string path;
        HttpMethod httpMethod;

        switch (method)
        {
            case "SendMessage":
                path = "/message:send"; httpMethod = HttpMethod.Post; break;
            case "GetTask":
                var taskId = a2aParams.Value<string>("id") ?? "";
                path = $"/tasks/{Uri.EscapeDataString(taskId)}"; httpMethod = HttpMethod.Get; break;
            case "ListTasks":
                path = "/tasks"; httpMethod = HttpMethod.Get; break;
            case "CancelTask":
                var cancelId = a2aParams.Value<string>("id") ?? "";
                path = $"/tasks/{Uri.EscapeDataString(cancelId)}:cancel"; httpMethod = HttpMethod.Post; break;
            case "CreateTaskPushNotificationConfig":
                var pushTaskId = a2aParams.Value<string>("task_id") ?? "";
                path = $"/tasks/{Uri.EscapeDataString(pushTaskId)}/pushNotificationConfigs"; httpMethod = HttpMethod.Post; break;
            case "GetTaskPushNotificationConfig":
                var getTaskId = a2aParams.Value<string>("task_id") ?? "";
                var getConfigId = a2aParams.Value<string>("id") ?? "";
                path = $"/tasks/{Uri.EscapeDataString(getTaskId)}/pushNotificationConfigs/{Uri.EscapeDataString(getConfigId)}"; httpMethod = HttpMethod.Get; break;
            case "ListTaskPushNotificationConfigs":
                var listTaskId = a2aParams.Value<string>("task_id") ?? "";
                path = $"/tasks/{Uri.EscapeDataString(listTaskId)}/pushNotificationConfigs"; httpMethod = HttpMethod.Get; break;
            case "DeleteTaskPushNotificationConfig":
                var delTaskId = a2aParams.Value<string>("task_id") ?? "";
                var delConfigId = a2aParams.Value<string>("id") ?? "";
                path = $"/tasks/{Uri.EscapeDataString(delTaskId)}/pushNotificationConfigs/{Uri.EscapeDataString(delConfigId)}"; httpMethod = HttpMethod.Delete; break;
            default:
                path = "/message:send"; httpMethod = HttpMethod.Post; break;
        }

        var request = new HttpRequestMessage(httpMethod, baseUrl + path);

        if (httpMethod == HttpMethod.Post)
        {
            request.Content = new StringContent(
                a2aParams.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    // ========================================
    // A2A MESSAGE BUILDER
    // ========================================

    private JObject BuildA2AMessage(string text, string contextId, string agentId, string timeZone, int? timeZoneOffset)
    {
        var msg = new JObject
        {
            ["role"] = "ROLE_USER",
            ["messageId"] = Guid.NewGuid().ToString("N"),
            ["parts"] = new JArray { new JObject { ["text"] = text } }
        };

        if (!string.IsNullOrWhiteSpace(contextId))
            msg["contextId"] = contextId;

        // Location metadata — required by Work IQ for time-sensitive queries
        if (!string.IsNullOrWhiteSpace(timeZone) || timeZoneOffset.HasValue)
        {
            var location = new JObject();
            if (!string.IsNullOrWhiteSpace(timeZone)) location["timeZone"] = timeZone;
            if (timeZoneOffset.HasValue) location["timeZoneOffset"] = timeZoneOffset.Value;
            msg["metadata"] = new JObject { ["Location"] = location };
        }

        return msg;
    }

    // ========================================
    // RESPONSE NORMALIZATION
    // ========================================

    private JObject NormalizeTaskResponse(JObject result)
    {
        var task = result["task"] as JObject;
        if (task == null)
        {
            // Response might be a direct message instead of a task
            var msg = result["message"] as JObject;
            if (msg != null)
            {
                return new JObject
                {
                    ["responseText"] = ExtractTextFromParts(msg["parts"] as JArray),
                    ["state"] = "completed"
                };
            }
            return result;
        }

        var state = NormalizeState(task["status"]?["state"]?.ToString());
        var responseText = ExtractTextFromArtifacts(task["artifacts"] as JArray);
        var statusMessage = task["status"]?["message"]?["parts"]?[0]?["text"]?.ToString();

        var normalized = new JObject
        {
            ["taskId"] = task.Value<string>("id"),
            ["contextId"] = task.Value<string>("context_id") ?? task.Value<string>("contextId"),
            ["state"] = state,
            ["responseText"] = responseText,
            ["statusMessage"] = statusMessage
        };

        // Include structured artifacts
        var artifacts = task["artifacts"] as JArray;
        if (artifacts != null && artifacts.Count > 0)
        {
            var normalizedArtifacts = new JArray();
            foreach (var artifact in artifacts)
            {
                normalizedArtifacts.Add(new JObject
                {
                    ["artifactId"] = artifact.Value<string>("artifactId") ?? artifact.Value<string>("artifact_id"),
                    ["name"] = artifact.Value<string>("name"),
                    ["parts"] = artifact["parts"]
                });
            }
            normalized["artifacts"] = normalizedArtifacts;
        }

        return normalized;
    }

    private string ExtractTextFromArtifacts(JArray artifacts)
    {
        if (artifacts == null || artifacts.Count == 0) return null;

        var texts = new List<string>();
        foreach (var artifact in artifacts)
        {
            var parts = artifact["parts"] as JArray;
            if (parts == null) continue;
            var partText = ExtractTextFromParts(parts);
            if (!string.IsNullOrWhiteSpace(partText)) texts.Add(partText);
        }
        return texts.Count > 0 ? string.Join("\n\n", texts) : null;
    }

    private string ExtractTextFromParts(JArray parts)
    {
        if (parts == null || parts.Count == 0) return null;

        var texts = new List<string>();
        foreach (var part in parts)
        {
            var text = part.Value<string>("text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
                continue;
            }

            var url = part.Value<string>("url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                texts.Add(url);
                continue;
            }

            var data = part["data"];
            if (data != null)
            {
                texts.Add(data.ToString(Newtonsoft.Json.Formatting.Indented));
            }
        }
        return texts.Count > 0 ? string.Join("\n", texts) : null;
    }

    private static string NormalizeState(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return "unknown";
        // Strip TASK_STATE_ prefix if present (SCREAMING_SNAKE_CASE → lowercase)
        return state
            .Replace("TASK_STATE_", "")
            .ToLowerInvariant();
    }

    private static string MapStateToEnum(string friendlyState)
    {
        if (string.IsNullOrWhiteSpace(friendlyState)) return null;
        return "TASK_STATE_" + friendlyState.Trim().ToUpperInvariant();
    }

    // ========================================
    // MCP HANDLER (INLINE, COMPACT PATTERN)
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        string body;
        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return McpError(null, -32700, "Parse error", "Unable to read request body");
        }

        if (string.IsNullOrWhiteSpace(body))
            return McpError(null, -32600, "Invalid Request", "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return McpError(null, -32700, "Parse error", "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        try
        {
            switch (method)
            {
                case "initialize":
                    return McpSuccess(id, new JObject
                    {
                        ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? "2025-11-25",
                        ["capabilities"] = new JObject
                        {
                            ["tools"] = new JObject { ["listChanged"] = false }
                        },
                        ["serverInfo"] = new JObject
                        {
                            ["name"] = MCP_SERVER_NAME,
                            ["version"] = MCP_SERVER_VERSION,
                            ["title"] = "Power A2A Client",
                            ["description"] = "A2A protocol client for connecting to external agents as MCP tools."
                        }
                    });

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return McpSuccess(id, new JObject());

                case "ping":
                    return McpSuccess(id, new JObject());

                case "tools/list":
                    return McpSuccess(id, new JObject { ["tools"] = AVAILABLE_TOOLS });

                case "tools/call":
                    return await HandleMcpToolCallAsync(id, request["params"] as JObject).ConfigureAwait(false);

                case "resources/list":
                    return McpSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return McpSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return McpSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "completion/complete":
                    return McpSuccess(id, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                case "logging/setLevel":
                    return McpSuccess(id, new JObject());

                default:
                    return McpError(id, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            return McpError(id, -32603, "Internal error", ex.Message);
        }
    }

    private async Task<HttpResponseMessage> HandleMcpToolCallAsync(JToken id, JObject paramsObj)
    {
        var toolName = paramsObj?.Value<string>("name");
        var args = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return McpError(id, -32602, "Invalid params", "Tool name is required");

        try
        {
            JObject toolResult;

            switch (toolName)
            {
                case "send_message":
                    toolResult = await McpSendMessageAsync(args, returnImmediately: false).ConfigureAwait(false);
                    break;
                case "send_message_async":
                    toolResult = await McpSendMessageAsync(args, returnImmediately: true).ConfigureAwait(false);
                    break;
                case "get_task":
                    toolResult = await McpGetTaskAsync(args).ConfigureAwait(false);
                    break;
                case "list_tasks":
                    toolResult = await McpListTasksAsync(args).ConfigureAwait(false);
                    break;
                case "cancel_task":
                    toolResult = await McpCancelTaskAsync(args).ConfigureAwait(false);
                    break;
                case "get_agent_card":
                    toolResult = await McpGetAgentCardAsync().ConfigureAwait(false);
                    break;
                default:
                    return McpSuccess(id, new JObject
                    {
                        ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Unknown tool: {toolName}" } },
                        ["isError"] = true
                    });
            }

            return McpSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented) }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            return McpSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // MCP TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> McpSendMessageAsync(JObject args, bool returnImmediately)
    {
        var message = args.Value<string>("message");
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("'message' is required");

        var contextId = args.Value<string>("context_id");
        var agentId = args.Value<string>("agent_id") ?? A2A_DEFAULT_AGENT_ID;
        var timeZone = args.Value<string>("time_zone");
        var timeZoneOffset = args.Value<int?>("time_zone_offset");

        var a2aMessage = BuildA2AMessage(message, contextId, agentId, timeZone, timeZoneOffset);
        var a2aParams = new JObject { ["message"] = a2aMessage };

        if (returnImmediately)
        {
            a2aParams["configuration"] = new JObject { ["return_immediately"] = true };
        }

        var result = await SendA2ARequestAsync("SendMessage", a2aParams).ConfigureAwait(false);
        return NormalizeTaskResponse(result);
    }

    private async Task<JObject> McpGetTaskAsync(JObject args)
    {
        var taskId = args.Value<string>("task_id");
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("'task_id' is required");

        var a2aParams = new JObject { ["id"] = taskId };
        var historyLength = args.Value<int?>("history_length");
        if (historyLength.HasValue) a2aParams["history_length"] = historyLength.Value;

        var result = await SendA2ARequestAsync("GetTask", a2aParams).ConfigureAwait(false);
        return NormalizeTaskResponse(result);
    }

    private async Task<JObject> McpListTasksAsync(JObject args)
    {
        var a2aParams = new JObject();

        var contextId = args.Value<string>("context_id");
        if (!string.IsNullOrWhiteSpace(contextId)) a2aParams["context_id"] = contextId;

        var state = args.Value<string>("state");
        if (!string.IsNullOrWhiteSpace(state)) a2aParams["status"] = MapStateToEnum(state);

        var pageSize = args.Value<int?>("page_size");
        if (pageSize.HasValue) a2aParams["page_size"] = pageSize.Value;

        var result = await SendA2ARequestAsync("ListTasks", a2aParams).ConfigureAwait(false);

        var tasks = new JArray();
        foreach (var t in result["tasks"] as JArray ?? new JArray())
        {
            tasks.Add(NormalizeTaskResponse(new JObject { ["task"] = t }));
        }

        return new JObject
        {
            ["tasks"] = tasks,
            ["totalSize"] = result.Value<int?>("total_size") ?? tasks.Count
        };
    }

    private async Task<JObject> McpCancelTaskAsync(JObject args)
    {
        var taskId = args.Value<string>("task_id");
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("'task_id' is required");

        var result = await SendA2ARequestAsync("CancelTask", new JObject { ["id"] = taskId }).ConfigureAwait(false);
        return NormalizeTaskResponse(result);
    }

    private async Task<JObject> McpGetAgentCardAsync()
    {
        var cardUrl = A2A_ENDPOINT.TrimEnd('/');
        var baseUri = new Uri(cardUrl);
        var wellKnownUrl = $"{baseUri.Scheme}://{baseUri.Host}/.well-known/agent-card.json";

        var request = new HttpRequestMessage(HttpMethod.Get, wellKnownUrl);
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Agent card request failed ({(int)response.StatusCode}): {content}");

        return JObject.Parse(content);
    }

    // ========================================
    // MCP JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage McpSuccess(JToken id, JObject result)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage McpError(JToken id, int code, string message, string data = null)
    {
        var err = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) err["data"] = data;
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = err, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // COMMON HELPERS
    // ========================================

    private async Task<JObject> ReadBodyAsync()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return new JObject();
        return JObject.Parse(content);
    }

    private static string RequireField(JObject body, string fieldName)
    {
        var value = body?.Value<string>(fieldName);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{fieldName}' is required");
        return value;
    }

    private HttpResponseMessage CreateResponse(HttpStatusCode statusCode, JObject body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS (OPTIONAL)
    // ========================================

    private async Task LogToAppInsights(string eventName, IDictionary<string, string> properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsObj = new JObject();
            propsObj["ServerName"] = MCP_SERVER_NAME;
            if (properties != null)
            {
                foreach (var kvp in properties)
                    propsObj[kvp.Key] = kvp.Value;
            }

            var telemetryData = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = instrumentationKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = propsObj
                    }
                }
            };

            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(
                    telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
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

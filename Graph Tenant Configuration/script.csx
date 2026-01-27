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
    private const string ServerName = "utcm-mcp-server";
    private const string ServerVersion = "1.0.0";
    private const string ServerTitle = "Graph Tenant Configuration";
    private const string ServerDescription = "Microsoft Graph UTCM API for monitoring tenant configuration drift, creating baselines, and extracting configuration snapshots.";
    private const string ProtocolVersion = "2025-01-26";
    private const string ServerInstructions = "Use this server to monitor and manage Microsoft 365 tenant configuration across Defender, Entra, Exchange, Intune, Purview, and Teams. Start by listing monitors to see active monitoring, check for drifts to find configuration changes from baselines, and create snapshots to capture current configuration state. Note: UTCM service principal must be configured in your tenant first.";
    private const string GraphBaseUrl = "https://graph.microsoft.com/beta";
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static string _currentLogLevel = "info";
    private static readonly string[] ValidLogLevels = { "debug", "info", "notice", "warning", "error", "critical", "alert", "emergency" };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var operationId = this.Context.OperationId;
        
        this.Context.Logger.LogInformation($"[{correlationId}] Processing operation: {operationId}");
        _ = LogToAppInsights("OperationStarted", new { CorrelationId = correlationId, OperationId = operationId });

        try
        {
            HttpResponseMessage response;
            
            switch (operationId)
            {
                case "InvokeMCP":
                    response = await HandleMCPAsync(correlationId).ConfigureAwait(false);
                    break;
                case "ListConfigurationMonitors":
                    response = await HandleListConfigurationMonitorsAsync(correlationId).ConfigureAwait(false);
                    break;
                case "CreateConfigurationMonitor":
                    response = await HandleCreateConfigurationMonitorAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetConfigurationMonitor":
                    response = await HandleGetConfigurationMonitorAsync(correlationId).ConfigureAwait(false);
                    break;
                case "UpdateConfigurationMonitor":
                    response = await HandleUpdateConfigurationMonitorAsync(correlationId).ConfigureAwait(false);
                    break;
                case "DeleteConfigurationMonitor":
                    response = await HandleDeleteConfigurationMonitorAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetMonitorBaseline":
                    response = await HandleGetMonitorBaselineAsync(correlationId).ConfigureAwait(false);
                    break;
                case "ListConfigurationDrifts":
                    response = await HandleListConfigurationDriftsAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetConfigurationDrift":
                    response = await HandleGetConfigurationDriftAsync(correlationId).ConfigureAwait(false);
                    break;
                case "ListConfigurationSnapshotJobs":
                    response = await HandleListConfigurationSnapshotJobsAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetConfigurationSnapshotJob":
                    response = await HandleGetConfigurationSnapshotJobAsync(correlationId).ConfigureAwait(false);
                    break;
                case "DeleteConfigurationSnapshotJob":
                    response = await HandleDeleteConfigurationSnapshotJobAsync(correlationId).ConfigureAwait(false);
                    break;
                case "ListConfigurationMonitoringResults":
                    response = await HandleListConfigurationMonitoringResultsAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetConfigurationMonitoringResult":
                    response = await HandleGetConfigurationMonitoringResultAsync(correlationId).ConfigureAwait(false);
                    break;
                case "GetConfigurationBaseline":
                    response = await HandleGetConfigurationBaselineAsync(correlationId).ConfigureAwait(false);
                    break;
                case "CreateSnapshotFromBaseline":
                    response = await HandleCreateSnapshotFromBaselineAsync(correlationId).ConfigureAwait(false);
                    break;
                default:
                    response = new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent($"Unknown operation: {operationId}")
                    };
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Error: {ex.Message}");
            _ = LogToAppInsights("OperationError", new { CorrelationId = correlationId, OperationId = operationId, Error = ex.Message });
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(JsonConvert.SerializeObject(new { error = ex.Message }), Encoding.UTF8, "application/json")
            };
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");
            _ = LogToAppInsights("OperationCompleted", new { CorrelationId = correlationId, OperationId = operationId, DurationMs = duration.TotalMilliseconds });
        }
    }

    private async Task<HttpResponseMessage> HandleMCPAsync(string correlationId)
    {
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("["))
            {
                return await HandleBatchRequestAsync(correlationId, body).ConfigureAwait(false);
            }

            request = JObject.Parse(body);
            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            this.Context.Logger.LogInformation($"[{correlationId}] MCP method: {method}");
            _ = LogToAppInsights("McpRequest", new { CorrelationId = correlationId, Method = method });

            HttpResponseMessage response;
            switch (method)
            {
                case "initialize":
                    response = HandleInitialize(correlationId, request, requestId);
                    break;
                case "initialized":
                case "notifications/initialized":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;
                case "ping":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;
                case "tools/list":
                    response = HandleToolsList(correlationId, request, requestId);
                    break;
                case "tools/call":
                    response = await HandleToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;
                case "resources/list":
                    response = HandleResourcesList(correlationId, request, requestId);
                    break;
                case "resources/read":
                    response = HandleResourcesRead(correlationId, request, requestId);
                    break;
                case "prompts/list":
                    response = HandlePromptsList(correlationId, request, requestId);
                    break;
                case "prompts/get":
                    response = HandlePromptsGet(correlationId, request, requestId);
                    break;
                case "completion/complete":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject 
                    { 
                        ["completion"] = new JObject 
                        { 
                            ["values"] = new JArray(), 
                            ["total"] = 0, 
                            ["hasMore"] = false 
                        } 
                    });
                    break;
                case "logging/setLevel":
                    response = HandleLoggingSetLevel(correlationId, request, requestId);
                    break;
                default:
                    response = CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
                    break;
            }

            return response;
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(string correlationId, JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
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
                ["title"] = ServerTitle,
                ["description"] = ServerDescription
            },
            ["instructions"] = ServerInstructions
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(string correlationId, JObject request, JToken requestId)
    {
        var tools = BuildToolsList();
        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

        try
        {
            _ = LogToAppInsights("ToolCallStarted", new { CorrelationId = correlationId, ToolName = toolName });
            
            JObject toolResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);
            
            _ = LogToAppInsights("ToolCallCompleted", new { CorrelationId = correlationId, ToolName = toolName, IsError = false });

            var result = new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Formatting.Indented)
                    }
                },
                ["isError"] = false
            };

            if (toolResult.Count > 0)
            {
                result["structuredContent"] = toolResult;
            }

            return CreateJsonRpcSuccessResponse(requestId, result);
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolCallError", new { CorrelationId = correlationId, ToolName = toolName, Error = ex.Message });
            
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    private HttpResponseMessage HandleResourcesList(string correlationId, JObject request, JToken requestId)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
    }

    private HttpResponseMessage HandleResourcesRead(string correlationId, JObject request, JToken requestId)
    {
        var uri = request["params"]?["uri"]?.ToString();
        return CreateJsonRpcErrorResponse(requestId, -32602, "Resource not found", uri);
    }

    private HttpResponseMessage HandlePromptsList(string correlationId, JObject request, JToken requestId)
    {
        var prompts = BuildPromptsList();
        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = prompts });
    }

    private HttpResponseMessage HandlePromptsGet(string correlationId, JObject request, JToken requestId)
    {
        var promptName = request["params"]?["name"]?.ToString();
        var promptArgs = request["params"]?["arguments"] as JObject ?? new JObject();

        var messages = GetPromptMessages(promptName, promptArgs);
        var result = new JObject
        {
            ["description"] = GetPromptDescription(promptName),
            ["messages"] = messages
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleLoggingSetLevel(string correlationId, JObject request, JToken requestId)
    {
        var level = request["params"]?["level"]?.ToString()?.ToLowerInvariant() ?? "info";
        if (ValidLogLevels.Contains(level))
        {
            _currentLogLevel = level;
        }
        return CreateJsonRpcSuccessResponse(requestId, new JObject());
    }

    private JArray BuildToolsList()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = "list_monitors",
                ["description"] = "List all tenant configuration monitors. Monitors run periodically to detect configuration drift by comparing current settings against a baseline.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["top"] = new JObject { ["type"] = "integer", ["description"] = "Number of monitors to return (default: 10)" },
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData filter (e.g., status eq 'active')" }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "create_monitor",
                ["description"] = "Create a new configuration monitor with a baseline to track configuration drift. The monitor runs every 6 hours automatically.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["displayName"] = new JObject { ["type"] = "string", ["description"] = "Name for the monitor" },
                        ["description"] = new JObject { ["type"] = "string", ["description"] = "Description of what this monitor tracks" },
                        ["baselineName"] = new JObject { ["type"] = "string", ["description"] = "Name for the configuration baseline" },
                        ["baselineDescription"] = new JObject { ["type"] = "string", ["description"] = "Description of the baseline" },
                        ["resources"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "List of resources and properties to monitor",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["resourceType"] = new JObject { ["type"] = "string" },
                                    ["displayName"] = new JObject { ["type"] = "string" },
                                    ["properties"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "object" } }
                                }
                            }
                        }
                    },
                    ["required"] = new JArray { "displayName" }
                }
            },
            new JObject
            {
                ["name"] = "get_monitor",
                ["description"] = "Get details of a specific configuration monitor by its ID, including its current status and baseline.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["monitorId"] = new JObject { ["type"] = "string", ["description"] = "The unique ID of the monitor" },
                        ["expand"] = new JObject { ["type"] = "string", ["description"] = "Expand related entities (e.g., 'baseline')" }
                    },
                    ["required"] = new JArray { "monitorId" }
                }
            },
            new JObject
            {
                ["name"] = "delete_monitor",
                ["description"] = "Delete a configuration monitor and all its associated monitoring results and drifts.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["monitorId"] = new JObject { ["type"] = "string", ["description"] = "The unique ID of the monitor to delete" }
                    },
                    ["required"] = new JArray { "monitorId" }
                }
            },
            new JObject
            {
                ["name"] = "list_drifts",
                ["description"] = "List all detected configuration drifts across all monitors. Shows where current configuration differs from baseline.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["top"] = new JObject { ["type"] = "integer", ["description"] = "Number of drifts to return (default: 20)" },
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData filter (e.g., status eq 'active')" },
                        ["monitorId"] = new JObject { ["type"] = "string", ["description"] = "Filter by specific monitor ID" }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "get_drift",
                ["description"] = "Get detailed information about a specific configuration drift including the properties that changed.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["driftId"] = new JObject { ["type"] = "string", ["description"] = "The unique ID of the drift" }
                    },
                    ["required"] = new JArray { "driftId" }
                }
            },
            new JObject
            {
                ["name"] = "list_snapshots",
                ["description"] = "List all configuration snapshot jobs. Snapshots capture the current tenant configuration for backup or analysis.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["top"] = new JObject { ["type"] = "integer", ["description"] = "Number of snapshots to return (max visible: 12)" },
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData filter (e.g., status eq 'succeeded')" }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "get_snapshot",
                ["description"] = "Get details of a specific snapshot job including its status and download location.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["snapshotJobId"] = new JObject { ["type"] = "string", ["description"] = "The unique ID of the snapshot job" }
                    },
                    ["required"] = new JArray { "snapshotJobId" }
                }
            },
            new JObject
            {
                ["name"] = "create_snapshot",
                ["description"] = "Create a new configuration snapshot to capture the current tenant configuration. Requires a baseline ID.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["baselineId"] = new JObject { ["type"] = "string", ["description"] = "The baseline ID to use for the snapshot" },
                        ["displayName"] = new JObject { ["type"] = "string", ["description"] = "Name for the snapshot" },
                        ["description"] = new JObject { ["type"] = "string", ["description"] = "Description of the snapshot" }
                    },
                    ["required"] = new JArray { "baselineId", "displayName" }
                }
            },
            new JObject
            {
                ["name"] = "delete_snapshot",
                ["description"] = "Delete a snapshot job to make room for new snapshots (max 12 visible).",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["snapshotJobId"] = new JObject { ["type"] = "string", ["description"] = "The unique ID of the snapshot to delete" }
                    },
                    ["required"] = new JArray { "snapshotJobId" }
                }
            },
            new JObject
            {
                ["name"] = "list_results",
                ["description"] = "List monitoring run results showing when monitors ran and how many drifts were detected.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["top"] = new JObject { ["type"] = "integer", ["description"] = "Number of results to return" },
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData filter (e.g., monitorId eq 'guid')" },
                        ["orderby"] = new JObject { ["type"] = "string", ["description"] = "Order by (e.g., 'runInitiationDateTime desc')" }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "get_baseline",
                ["description"] = "Get details of a configuration baseline including its resources and parameters.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["baselineId"] = new JObject { ["type"] = "string", ["description"] = "The unique ID of the baseline" }
                    },
                    ["required"] = new JArray { "baselineId" }
                }
            }
        };
    }

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject arguments)
    {
        switch (toolName.ToLowerInvariant())
        {
            case "list_monitors":
                return await ExecuteListMonitorsToolAsync(arguments).ConfigureAwait(false);
            case "create_monitor":
                return await ExecuteCreateMonitorToolAsync(arguments).ConfigureAwait(false);
            case "get_monitor":
                return await ExecuteGetMonitorToolAsync(arguments).ConfigureAwait(false);
            case "delete_monitor":
                return await ExecuteDeleteMonitorToolAsync(arguments).ConfigureAwait(false);
            case "list_drifts":
                return await ExecuteListDriftsToolAsync(arguments).ConfigureAwait(false);
            case "get_drift":
                return await ExecuteGetDriftToolAsync(arguments).ConfigureAwait(false);
            case "list_snapshots":
                return await ExecuteListSnapshotsToolAsync(arguments).ConfigureAwait(false);
            case "get_snapshot":
                return await ExecuteGetSnapshotToolAsync(arguments).ConfigureAwait(false);
            case "create_snapshot":
                return await ExecuteCreateSnapshotToolAsync(arguments).ConfigureAwait(false);
            case "delete_snapshot":
                return await ExecuteDeleteSnapshotToolAsync(arguments).ConfigureAwait(false);
            case "list_results":
                return await ExecuteListResultsToolAsync(arguments).ConfigureAwait(false);
            case "get_baseline":
                return await ExecuteGetBaselineToolAsync(arguments).ConfigureAwait(false);
            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> ExecuteListMonitorsToolAsync(JObject arguments)
    {
        var top = arguments["top"]?.Value<int>() ?? 10;
        var filter = arguments["filter"]?.ToString();
        
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationMonitors?$top={top}";
        if (!string.IsNullOrEmpty(filter))
        {
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }
        
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateMonitorToolAsync(JObject arguments)
    {
        var displayName = RequireArgument(arguments, "displayName");
        var description = GetArgument(arguments, "description", "");
        var baselineName = GetArgument(arguments, "baselineName", displayName + " Baseline");
        var baselineDescription = GetArgument(arguments, "baselineDescription", "");
        var resources = arguments["resources"] as JArray ?? new JArray();

        var body = new JObject
        {
            ["displayName"] = displayName,
            ["description"] = description,
            ["baseline"] = new JObject
            {
                ["displayName"] = baselineName,
                ["description"] = baselineDescription,
                ["resources"] = resources
            }
        };

        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationMonitors";
        return await SendGraphRequestAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetMonitorToolAsync(JObject arguments)
    {
        var monitorId = RequireArgument(arguments, "monitorId");
        var expand = arguments["expand"]?.ToString();
        
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationMonitors/{monitorId}";
        if (!string.IsNullOrEmpty(expand))
        {
            url += $"?$expand={Uri.EscapeDataString(expand)}";
        }
        
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteMonitorToolAsync(JObject arguments)
    {
        var monitorId = RequireArgument(arguments, "monitorId");
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationMonitors/{monitorId}";
        return await SendGraphRequestAsync(HttpMethod.Delete, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListDriftsToolAsync(JObject arguments)
    {
        var top = arguments["top"]?.Value<int>() ?? 20;
        var filter = arguments["filter"]?.ToString();
        var monitorId = arguments["monitorId"]?.ToString();
        
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationDrifts?$top={top}";
        
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(filter))
        {
            filters.Add(filter);
        }
        if (!string.IsNullOrEmpty(monitorId))
        {
            filters.Add($"monitorId eq '{monitorId}'");
        }
        if (filters.Count > 0)
        {
            url += $"&$filter={Uri.EscapeDataString(string.Join(" and ", filters))}";
        }
        
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetDriftToolAsync(JObject arguments)
    {
        var driftId = RequireArgument(arguments, "driftId");
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationDrifts/{driftId}";
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListSnapshotsToolAsync(JObject arguments)
    {
        var top = arguments["top"]?.Value<int>() ?? 10;
        var filter = arguments["filter"]?.ToString();
        
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationSnapshotJobs?$top={top}";
        if (!string.IsNullOrEmpty(filter))
        {
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }
        
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetSnapshotToolAsync(JObject arguments)
    {
        var snapshotJobId = RequireArgument(arguments, "snapshotJobId");
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationSnapshotJobs/{snapshotJobId}";
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateSnapshotToolAsync(JObject arguments)
    {
        var baselineId = RequireArgument(arguments, "baselineId");
        var displayName = RequireArgument(arguments, "displayName");
        var description = GetArgument(arguments, "description", "");

        var body = new JObject
        {
            ["displayName"] = displayName,
            ["description"] = description
        };

        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationBaselines/{baselineId}/createSnapshot";
        return await SendGraphRequestAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteSnapshotToolAsync(JObject arguments)
    {
        var snapshotJobId = RequireArgument(arguments, "snapshotJobId");
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationSnapshotJobs/{snapshotJobId}";
        return await SendGraphRequestAsync(HttpMethod.Delete, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListResultsToolAsync(JObject arguments)
    {
        var top = arguments["top"]?.Value<int>() ?? 20;
        var filter = arguments["filter"]?.ToString();
        var orderby = arguments["orderby"]?.ToString() ?? "runInitiationDateTime desc";
        
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationMonitoringResults?$top={top}&$orderby={Uri.EscapeDataString(orderby)}";
        if (!string.IsNullOrEmpty(filter))
        {
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }
        
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBaselineToolAsync(JObject arguments)
    {
        var baselineId = RequireArgument(arguments, "baselineId");
        var url = $"{GraphBaseUrl}/admin/configurationManagement/configurationBaselines/{baselineId}";
        return await SendGraphRequestAsync(HttpMethod.Get, url).ConfigureAwait(false);
    }

    private JArray BuildPromptsList()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = "check_drift_status",
                ["description"] = "Check the current drift status across all monitors and summarize any configuration changes.",
                ["arguments"] = new JArray()
            },
            new JObject
            {
                ["name"] = "create_security_monitor",
                ["description"] = "Create a monitor focused on security-related tenant configurations.",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "monitorName",
                        ["description"] = "Name for the security monitor",
                        ["required"] = false
                    }
                }
            },
            new JObject
            {
                ["name"] = "export_configuration",
                ["description"] = "Create a snapshot to export current tenant configuration for backup or migration.",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "baselineId",
                        ["description"] = "Baseline ID to use for the export",
                        ["required"] = true
                    }
                }
            }
        };
    }

    private string GetPromptDescription(string promptName)
    {
        switch (promptName)
        {
            case "check_drift_status":
                return "Check the current drift status across all monitors and summarize any configuration changes.";
            case "create_security_monitor":
                return "Create a monitor focused on security-related tenant configurations.";
            case "export_configuration":
                return "Create a snapshot to export current tenant configuration for backup or migration.";
            default:
                return "";
        }
    }

    private JArray GetPromptMessages(string promptName, JObject arguments)
    {
        switch (promptName)
        {
            case "check_drift_status":
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = "Please check the current drift status by listing all active configuration drifts. Summarize which settings have changed from their baseline values and recommend any remediation actions."
                        }
                    }
                };

            case "create_security_monitor":
                var monitorName = arguments["monitorName"]?.ToString() ?? "Security Configuration Monitor";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Please create a new configuration monitor named '{monitorName}' that tracks security-related tenant settings. Focus on authentication, conditional access, and identity protection configurations."
                        }
                    }
                };

            case "export_configuration":
                var baselineId = arguments["baselineId"]?.ToString() ?? "";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Please create a configuration snapshot using baseline ID '{baselineId}' to export the current tenant configuration. Name it with today's date for easy identification."
                        }
                    }
                };

            default:
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = "No prompt template defined for: " + promptName
                        }
                    }
                };
        }
    }

    private async Task<HttpResponseMessage> HandleListConfigurationMonitorsAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleCreateConfigurationMonitorAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetConfigurationMonitorAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleUpdateConfigurationMonitorAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleDeleteConfigurationMonitorAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetMonitorBaselineAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleListConfigurationDriftsAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetConfigurationDriftAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleListConfigurationSnapshotJobsAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetConfigurationSnapshotJobAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleDeleteConfigurationSnapshotJobAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleListConfigurationMonitoringResultsAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetConfigurationMonitoringResultAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleGetConfigurationBaselineAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleCreateSnapshotFromBaselineAsync(string correlationId)
    {
        return await ForwardToGraphAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ForwardToGraphAsync()
    {
        var originalRequest = this.Context.Request;
        var uri = originalRequest.RequestUri;
        
        var graphUrl = $"{GraphBaseUrl}{uri.PathAndQuery}";
        
        var request = new HttpRequestMessage(originalRequest.Method, graphUrl);
        
        if (originalRequest.Headers.Authorization != null)
        {
            request.Headers.Authorization = originalRequest.Headers.Authorization;
        }
        
        if (originalRequest.Content != null && (originalRequest.Method == HttpMethod.Post || 
            originalRequest.Method.Method == "PATCH" || originalRequest.Method == HttpMethod.Put))
        {
            var content = await originalRequest.Content.ReadAsStringAsync().ConfigureAwait(false);
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<JObject> SendGraphRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method == HttpMethod.Put))
        {
            request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _ = LogToAppInsights("GraphApiError", new { Url = url, StatusCode = (int)response.StatusCode, Error = content });
            throw new Exception($"Graph API error ({(int)response.StatusCode}): {content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };
        }

        try
        {
            return JObject.Parse(content);
        }
        catch
        {
            return new JObject { ["text"] = content };
        }
    }

    private async Task<HttpResponseMessage> HandleBatchRequestAsync(string correlationId, string body)
    {
        JArray requests;
        try
        {
            requests = JArray.Parse(body);
        }
        catch (JsonException)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON array");
        }

        if (requests.Count == 0)
        {
            return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty batch");
        }

        var responses = new JArray();
        foreach (var req in requests)
        {
            if (req is JObject reqObj)
            {
                var method = reqObj.Value<string>("method") ?? "";
                var requestId = reqObj["id"];
                
                HttpResponseMessage singleResponse;
                switch (method)
                {
                    case "initialize":
                        singleResponse = HandleInitialize(correlationId, reqObj, requestId);
                        break;
                    case "ping":
                        singleResponse = CreateJsonRpcSuccessResponse(requestId, new JObject());
                        break;
                    case "tools/list":
                        singleResponse = HandleToolsList(correlationId, reqObj, requestId);
                        break;
                    case "tools/call":
                        singleResponse = await HandleToolsCallAsync(correlationId, reqObj, requestId).ConfigureAwait(false);
                        break;
                    default:
                        singleResponse = CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
                        break;
                }
                
                var content = await singleResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                responses.Add(JObject.Parse(content));
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JToken result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        
        if (!string.IsNullOrEmpty(data))
        {
            error["data"] = data;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };
        
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private string RequireArgument(JObject arguments, string name)
    {
        var value = arguments[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Required argument '{name}' is missing");
        }
        return value;
    }

    private string GetArgument(JObject arguments, string name, string defaultValue = "")
    {
        return arguments[name]?.ToString() ?? defaultValue;
    }

    private async Task LogToAppInsights(string eventName, object properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_CONNECTION_STRING)) return;

        try
        {
            var parts = APP_INSIGHTS_CONNECTION_STRING.Split(';')
                .Select(p => p.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

            if (!parts.TryGetValue("InstrumentationKey", out var instrumentationKey)) return;
            if (!parts.TryGetValue("IngestionEndpoint", out var ingestionEndpoint))
            {
                ingestionEndpoint = "https://dc.services.visualstudio.com";
            }

            var telemetryUrl = $"{ingestionEndpoint.TrimEnd('/')}/v2/track";
            var telemetry = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["iKey"] = instrumentationKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties)
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(telemetry.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Silently ignore telemetry failures
        }
    }
}

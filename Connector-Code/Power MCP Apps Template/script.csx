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
/// Power MCP Template with MCP Apps Support
/// Model Context Protocol implementation for Power Platform Custom Connectors
/// 
/// See readme.md for full documentation, SDK reference, and setup instructions.
/// 
/// Quick Start:
/// 1. Update SERVER CONFIGURATION with your server details
/// 2. Define tools in BuildToolsList() - add _meta.ui.resourceUri for UI tools
/// 3. Add tool handlers in ExecuteToolAsync()
/// 4. For UI tools: register in BuildUIResourcesList() and GetUIResourceContent()
/// 
/// MCP Apps SDK (Copilot Studio support):
/// ✅ connect(), ontoolinput, ontoolresult, updateModelContext(), onerror
/// ⏳ sendMessage(), callServerTool(), requestDisplayMode(), openLink(), etc.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // SERVER CONFIGURATION
    // ========================================
    
    /// <summary>Server name reported in MCP initialize response (use lowercase-with-dashes)</summary>
    private const string ServerName = "power-mcp-server";
    
    /// <summary>Server version</summary>
    private const string ServerVersion = "1.0.0";
    
    /// <summary>Human-readable title for the server</summary>
    private const string ServerTitle = "Power MCP Server";
    
    /// <summary>Description of what this server does</summary>
    private const string ServerDescription = "Power Platform custom connector implementing Model Context Protocol";
    
    /// <summary>MCP protocol version supported (2026-01-26 includes MCP Apps extension)</summary>
    private const string ProtocolVersion = "2026-01-26";

    /// <summary>Optional instructions for the client (initialize response)</summary>
    private const string ServerInstructions = ""; // Set to guidance for client if needed

    // ========================================
    // APPLICATION INSIGHTS CONFIGURATION
    // ========================================
    
    /// <summary>
    /// Application Insights connection string
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;LiveEndpoint=https://REGION.livediagnostics.monitor.azure.com/
    /// Get from: Azure Portal → Application Insights resource → Overview → Connection String
    /// Leave empty to disable telemetry
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // ========================================
    // LOGGING LEVEL INFRASTRUCTURE
    // ========================================

    /// <summary>Current logging level (static for persistence across requests)</summary>
    private static string _currentLogLevel = "info";
    
    /// <summary>Valid log levels per MCP specification (ordered by severity)</summary>
    private static readonly string[] ValidLogLevels = { "debug", "info", "notice", "warning", "error", "critical", "alert", "emergency" };

    /// <summary>
    /// Check if a log level should be emitted based on current level
    /// </summary>
    private bool ShouldLog(string level)
    {
        var currentIndex = Array.IndexOf(ValidLogLevels, _currentLogLevel.ToLowerInvariant());
        var requestedIndex = Array.IndexOf(ValidLogLevels, level.ToLowerInvariant());
        if (currentIndex < 0) currentIndex = 1; // Default to info
        if (requestedIndex < 0) return true; // Unknown level, log anyway
        return requestedIndex >= currentIndex;
    }

    // ========================================
    // CANCELLATION INFRASTRUCTURE
    // ========================================

    /// <summary>
    /// Track in-progress operations for cancellation support
    /// Key: requestId, Value: CancellationTokenSource
    /// </summary>
    private static readonly Dictionary<string, System.Threading.CancellationTokenSource> _pendingOperations = 
        new Dictionary<string, System.Threading.CancellationTokenSource>();
    private static readonly object _pendingOperationsLock = new object();

    /// <summary>Register an operation that can be cancelled</summary>
    private System.Threading.CancellationTokenSource RegisterCancellableOperation(string requestId)
    {
        var cts = new System.Threading.CancellationTokenSource();
        lock (_pendingOperationsLock)
        {
            _pendingOperations[requestId] = cts;
        }
        return cts;
    }

    /// <summary>Unregister a completed operation</summary>
    private void UnregisterOperation(string requestId)
    {
        lock (_pendingOperationsLock)
        {
            if (_pendingOperations.TryGetValue(requestId, out var cts))
            {
                cts.Dispose();
                _pendingOperations.Remove(requestId);
            }
        }
    }

    /// <summary>Cancel a pending operation by request ID</summary>
    private bool CancelOperation(string requestId)
    {
        lock (_pendingOperationsLock)
        {
            if (_pendingOperations.TryGetValue(requestId, out var cts))
            {
                cts.Cancel();
                return true;
            }
        }
        return false;
    }

    // ========================================
    // MAIN ENTRY POINT
    // ========================================
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        this.Context.Logger.LogInformation($"[{correlationId}] MCP request received");
        
        string body = null;
        JObject request = null;
        string method = null;
        JToken requestId = null;

        try
        {
            // Read and parse request body
            try
            {
                body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                _ = LogToAppInsights("ParseError", new { CorrelationId = correlationId, Error = "Unable to read request body" });
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
            }

            // Handle empty body gracefully
            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning($"[{correlationId}] Empty request body received");
                return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
            }

            // Check if this is a batch request (JSON array)
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("["))
            {
                return await HandleBatchRequestAsync(correlationId, body).ConfigureAwait(false);
            }

            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                _ = LogToAppInsights("ParseError", new { CorrelationId = correlationId, Error = "Invalid JSON" });
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            // Log incoming request (fire-and-forget for performance)
            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method,
                HasId = requestId != null,
                Path = this.Context.Request.RequestUri.AbsolutePath
            });

            this.Context.Logger.LogInformation($"[{correlationId}] Processing MCP method: {method}");

            // Route to appropriate handler - Copilot Studio compatible
            HttpResponseMessage response;
            switch (method)
            {
                // Core initialization
                case "initialize":
                    response = HandleInitialize(correlationId, request, requestId);
                    break;

                // Notifications (no response body required, but return empty success)
                case "initialized":
                case "notifications/initialized":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;

                case "notifications/cancelled":
                    response = HandleCancellation(correlationId, request, requestId);
                    break;

                // Health check
                case "ping":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
                    break;

                // Tools
                case "tools/list":
                    response = HandleToolsList(correlationId, request, requestId);
                    break;

                case "tools/call":
                    response = await HandleToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;

                // Resources - includes UI resources for MCP Apps
                case "resources/list":
                    response = HandleResourcesList(correlationId, request, requestId);
                    break;

                case "resources/templates/list":
                    response = HandleResourceTemplatesList(correlationId, request, requestId);
                    break;

                case "resources/read":
                    response = await HandleResourcesReadAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;

                // Prompts
                case "prompts/list":
                    response = HandlePromptsList(correlationId, request, requestId);
                    break;

                case "prompts/get":
                    response = await HandlePromptsGetAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;

                // Completions (required by some MCP clients)
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

                // Logging level
                case "logging/setLevel":
                    response = HandleLoggingSetLevel(correlationId, request, requestId);
                    break;

                default:
                    _ = LogToAppInsights("McpMethodNotFound", new { CorrelationId = correlationId, Method = method });
                    response = CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
                    break;
            }

            return response;
        }
        catch (JsonException ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] JSON parse error: {ex.Message}");
            _ = LogToAppInsights("McpError", new
            {
                CorrelationId = correlationId,
                Method = method,
                ErrorType = "JsonException",
                ErrorMessage = ex.Message
            });
            return CreateJsonRpcErrorResponse(requestId, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Internal error: {ex.Message}, StackTrace: {ex.StackTrace}");
            _ = LogToAppInsights("McpError", new
            {
                CorrelationId = correlationId,
                Method = method,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace?.Substring(0, Math.Min(1000, ex.StackTrace?.Length ?? 0))
            });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"[{correlationId}] Request completed in {duration.TotalMilliseconds}ms");
            _ = LogToAppInsights("McpRequestCompleted", new
            {
                CorrelationId = correlationId,
                Method = method,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // ========================================
    // BATCH REQUEST HANDLER
    // ========================================

    /// <summary>
    /// Handle JSON-RPC batch requests (array of requests)
    /// Per JSON-RPC 2.0 spec, returns array of responses in same order
    /// </summary>
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

        _ = LogToAppInsights("McpBatchRequest", new { CorrelationId = correlationId, RequestCount = requests.Count });

        var responses = new JArray();
        foreach (var req in requests)
        {
            if (req is JObject reqObj)
            {
                var singleBody = reqObj.ToString(Newtonsoft.Json.Formatting.None);
                // Recursively process each request
                var singleResponse = await ProcessSingleRequestAsync(correlationId, singleBody).ConfigureAwait(false);
                if (singleResponse != null)
                {
                    responses.Add(singleResponse);
                }
            }
            else
            {
                responses.Add(new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JObject
                    {
                        ["code"] = -32600,
                        ["message"] = "Invalid Request"
                    }
                });
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Process a single request and return the response as JObject (for batch handling)
    /// </summary>
    private async Task<JObject> ProcessSingleRequestAsync(string correlationId, string body)
    {
        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["error"] = new JObject { ["code"] = -32700, ["message"] = "Parse error" }
            };
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var requestId = request["id"];

        // Notifications (no id) don't require response
        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            // Process but don't return response for notifications
            return null;
        }

        // Get response from normal handler
        HttpResponseMessage response;
        switch (method)
        {
            case "initialize":
                response = HandleInitialize(correlationId, request, requestId);
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
                response = await HandleResourcesReadAsync(correlationId, request, requestId).ConfigureAwait(false);
                break;
            default:
                response = CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
                break;
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JObject.Parse(content);
    }

    // ========================================
    // MCP PROTOCOL HANDLERS
    // ========================================

    /// <summary>
    /// Handle MCP initialize request - Copilot Studio compatible
    /// Returns server capabilities and info per MCP spec
    /// </summary>
    private HttpResponseMessage HandleInitialize(string correlationId, JObject request, JToken requestId)
    {
        // Use client's protocol version if provided, otherwise default
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
            ["instructions"] = string.IsNullOrWhiteSpace(ServerInstructions) ? null : ServerInstructions
        };

        _ = LogToAppInsights("McpInitialized", new
        {
            CorrelationId = correlationId,
            ServerName = ServerName,
            ServerVersion = ServerVersion,
            ProtocolVersion = clientProtocolVersion
        });

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    /// <summary>
    /// Handle tools/list request - returns available tools
    /// No initialization check required for Copilot Studio compatibility
    /// </summary>
    private HttpResponseMessage HandleToolsList(string correlationId, JObject request, JToken requestId)
    {
        // Support cursor param per spec (ignored for static list)
        var cursor = request["params"]?["cursor"]?.ToString();

        var tools = BuildToolsList();
        
        _ = LogToAppInsights("McpToolsListed", new
        {
            CorrelationId = correlationId,
            ToolCount = tools.Count
        });

        var result = new JObject { ["tools"] = tools };
        // No pagination for static list; omit nextCursor
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    /// <summary>
    /// Handle tools/call request - execute a specific tool
    /// Returns tool results in MCP content format
    /// </summary>
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

        this.Context.Logger.LogInformation($"[{correlationId}] Executing tool: {toolName}");
        
        _ = LogToAppInsights("McpToolCallStarted", new
        {
            CorrelationId = correlationId,
            ToolName = toolName,
            HasArguments = arguments.Count > 0
        });

        try
        {
            // Route to specific tool implementation
            JObject toolResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);

            _ = LogToAppInsights("McpToolCallCompleted", new
            {
                CorrelationId = correlationId,
                ToolName = toolName,
                IsError = false
            });

            // Return MCP-compliant tool result with both text and structuredContent
            var result = new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            };

            // Add structuredContent for MCP Apps (typed data without parsing)
            if (toolResult.Count > 0)
            {
                result["structuredContent"] = toolResult;
            }

            return CreateJsonRpcSuccessResponse(requestId, result);
        }
        catch (ArgumentException ex)
        {
            // Invalid arguments - return as tool error (not MCP error)
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Invalid arguments: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("McpToolCallError", new
            {
                CorrelationId = correlationId,
                ToolName = toolName,
                ErrorMessage = ex.Message
            });

            // Return tool error (not MCP protocol error)
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

    /// <summary>
    /// Route tool execution to the appropriate handler
    /// </summary>
    private async Task<JObject> ExecuteToolAsync(string toolName, JObject arguments)
    {
        switch (toolName.ToLowerInvariant())
        {
            case "echo":
                return await ExecuteEchoToolAsync(arguments).ConfigureAwait(false);

            case "get_data":
                return await ExecuteGetDataToolAsync(arguments).ConfigureAwait(false);

            // MCP Apps example tools with UI
            case "color_picker":
                return await ExecuteColorPickerToolAsync(arguments).ConfigureAwait(false);

            case "data_visualizer":
                return await ExecuteDataVisualizerToolAsync(arguments).ConfigureAwait(false);

            case "form_input":
                return await ExecuteFormInputToolAsync(arguments).ConfigureAwait(false);

            case "data_table":
                return await ExecuteDataTableToolAsync(arguments).ConfigureAwait(false);

            case "confirm_action":
                return await ExecuteConfirmActionToolAsync(arguments).ConfigureAwait(false);

            // TODO: Add your custom tools here
            // case "your_tool_name":
            //     return await ExecuteYourToolAsync(arguments).ConfigureAwait(false);

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    /// <summary>
    /// Handle resources/list request - returns available resources including UI resources for MCP Apps
    /// </summary>
    private HttpResponseMessage HandleResourcesList(string correlationId, JObject request, JToken requestId)
    {
        var resources = BuildUIResourcesList();
        
        _ = LogToAppInsights("McpResourcesListed", new
        {
            CorrelationId = correlationId,
            ResourceCount = resources.Count
        });

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = resources });
    }

    /// <summary>
    /// Handle resources/read request - serves UI resources for MCP Apps
    /// </summary>
    private Task<HttpResponseMessage> HandleResourcesReadAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
        {
            return Task.FromResult(CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Resource URI is required"));
        }

        _ = LogToAppInsights("McpResourceRead", new
        {
            CorrelationId = correlationId,
            ResourceUri = uri
        });

        // Handle UI resources (MCP Apps)
        if (uri.StartsWith("ui://"))
        {
            var uiContent = GetUIResourceContent(uri);
            if (uiContent != null)
            {
                return Task.FromResult(CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["contents"] = new JArray
                    {
                        new JObject
                        {
                            ["uri"] = uri,
                            ["mimeType"] = "text/html",
                            ["text"] = uiContent
                        }
                    }
                }));
            }
        }

        // Resource not found
        return Task.FromResult(CreateJsonRpcErrorResponse(requestId, -32602, "Resource not found", uri));
    }

    /// <summary>
    /// Handle prompts/get request - returns a specific prompt with resolved arguments
    /// </summary>
    private Task<HttpResponseMessage> HandlePromptsGetAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var promptArgs = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
        {
            return Task.FromResult(CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Prompt name is required"));
        }

        _ = LogToAppInsights("McpPromptGet", new
        {
            CorrelationId = correlationId,
            PromptName = promptName
        });

        // Look up prompt by name
        var prompts = BuildPromptsList();
        var promptDef = prompts.FirstOrDefault(p => p["name"]?.ToString() == promptName);
        
        if (promptDef == null)
        {
            return Task.FromResult(CreateJsonRpcErrorResponse(requestId, -32602, "Prompt not found", promptName));
        }

        // Get prompt messages and resolve argument placeholders
        var messages = GetPromptMessages(promptName, promptArgs);
        
        var result = new JObject
        {
            ["description"] = promptDef["description"]?.ToString() ?? "",
            ["messages"] = messages
        };

        return Task.FromResult(CreateJsonRpcSuccessResponse(requestId, result));
    }

    /// <summary>
    /// Get the messages for a prompt with resolved arguments
    /// Override this method to provide dynamic prompt content
    /// </summary>
    private JArray GetPromptMessages(string promptName, JObject arguments)
    {
        switch (promptName)
        {
            case "analyze_data":
                var dataType = arguments["dataType"]?.ToString() ?? "generic data";
                var context = arguments["context"]?.ToString() ?? "";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Please analyze the following {dataType}." +
                                      (string.IsNullOrEmpty(context) ? "" : $" Context: {context}")
                        }
                    }
                };

            case "summarize":
                var length = arguments["length"]?.ToString() ?? "medium";
                var style = arguments["style"]?.ToString() ?? "professional";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Please provide a {length}-length summary in a {style} style."
                        }
                    }
                };

            case "code_review":
                var language = arguments["language"]?.ToString() ?? "any";
                var focus = arguments["focus"]?.ToString() ?? "general";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"You are a code reviewer specializing in {language}. Focus on: {focus}"
                        }
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = "Please review the code I'm about to share and provide feedback."
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

    /// <summary>
    /// Handle prompts/list request - returns available prompts
    /// </summary>
    private HttpResponseMessage HandlePromptsList(string correlationId, JObject request, JToken requestId)
    {
        var prompts = BuildPromptsList();
        
        _ = LogToAppInsights("McpPromptsListed", new
        {
            CorrelationId = correlationId,
            PromptCount = prompts.Count
        });

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = prompts });
    }

    /// <summary>
    /// Handle resources/templates/list request - returns resource templates with URI patterns
    /// </summary>
    private HttpResponseMessage HandleResourceTemplatesList(string correlationId, JObject request, JToken requestId)
    {
        var templates = BuildResourceTemplatesList();
        
        _ = LogToAppInsights("McpResourceTemplatesListed", new
        {
            CorrelationId = correlationId,
            TemplateCount = templates.Count
        });

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = templates });
    }

    /// <summary>
    /// Handle logging/setLevel request - sets the minimum log level
    /// </summary>
    private HttpResponseMessage HandleLoggingSetLevel(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var level = paramsObj?.Value<string>("level")?.ToLowerInvariant() ?? "info";

        // Validate level
        if (!ValidLogLevels.Contains(level))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", 
                $"Invalid log level: {level}. Valid levels: {string.Join(", ", ValidLogLevels)}");
        }

        var previousLevel = _currentLogLevel;
        _currentLogLevel = level;

        _ = LogToAppInsights("McpLogLevelChanged", new
        {
            CorrelationId = correlationId,
            PreviousLevel = previousLevel,
            NewLevel = level
        });

        this.Context.Logger.LogInformation($"[{correlationId}] Log level changed from {previousLevel} to {level}");

        return CreateJsonRpcSuccessResponse(requestId, new JObject());
    }

    /// <summary>
    /// Handle notifications/cancelled - cancel a pending operation
    /// </summary>
    private HttpResponseMessage HandleCancellation(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var cancelRequestId = paramsObj?["requestId"]?.ToString();
        var reason = paramsObj?["reason"]?.ToString() ?? "Client requested cancellation";

        if (string.IsNullOrEmpty(cancelRequestId))
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject()); // No requestId to cancel
        }

        var cancelled = CancelOperation(cancelRequestId);

        _ = LogToAppInsights("McpOperationCancelled", new
        {
            CorrelationId = correlationId,
            CancelledRequestId = cancelRequestId,
            Reason = reason,
            WasPending = cancelled
        });

        if (cancelled)
        {
            this.Context.Logger.LogInformation($"[{correlationId}] Cancelled operation {cancelRequestId}: {reason}");
        }

        return CreateJsonRpcSuccessResponse(requestId, new JObject());
    }

    // ========================================
    // TOOL IMPLEMENTATIONS
    // ========================================

    /// <summary>
    /// Example tool: Echo - returns the input message
    /// </summary>
    private Task<JObject> ExecuteEchoToolAsync(JObject arguments)
    {
        var message = arguments.Value<string>("message") ?? "No message provided";
        
        return Task.FromResult(new JObject
        {
            ["echo"] = message,
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>
    /// Example tool: Get Data - demonstrates external API call
    /// </summary>
    private async Task<JObject> ExecuteGetDataToolAsync(JObject arguments)
    {
        var dataId = RequireArgument(arguments, "id");
        
        // TODO: Replace with actual API call
        // Example:
        // var apiUrl = $"https://api.example.com/data/{dataId}";
        // var result = await SendExternalRequestAsync(HttpMethod.Get, apiUrl, null).ConfigureAwait(false);
        // return result;
        
        return new JObject
        {
            ["id"] = dataId,
            ["data"] = "Sample data response",
            ["retrievedAt"] = DateTime.UtcNow.ToString("o")
        };
    }

    // ========================================
    // MCP APPS UI TOOL IMPLEMENTATIONS
    // ========================================

    /// <summary>
    /// MCP Apps example: Color Picker - returns selected color with interactive UI
    /// The host will render the UI from the ui://color-picker resource
    /// </summary>
    private Task<JObject> ExecuteColorPickerToolAsync(JObject arguments)
    {
        var defaultColor = GetArgument(arguments, "defaultColor", "#3B82F6");
        
        return Task.FromResult(new JObject
        {
            ["defaultColor"] = defaultColor,
            ["message"] = "Color picker UI is ready. Select a color using the interactive interface.",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>
    /// MCP Apps example: Data Visualizer - displays data as interactive chart
    /// The host will render the UI from the ui://data-visualizer resource
    /// </summary>
    private Task<JObject> ExecuteDataVisualizerToolAsync(JObject arguments)
    {
        var chartType = GetArgument(arguments, "chartType", "bar");
        var title = GetArgument(arguments, "title", "Data Visualization");
        var dataPoints = arguments["data"] as JArray ?? new JArray
        {
            new JObject { ["label"] = "January", ["value"] = 65 },
            new JObject { ["label"] = "February", ["value"] = 59 },
            new JObject { ["label"] = "March", ["value"] = 80 },
            new JObject { ["label"] = "April", ["value"] = 81 },
            new JObject { ["label"] = "May", ["value"] = 56 }
        };
        
        return Task.FromResult(new JObject
        {
            ["chartType"] = chartType,
            ["title"] = title,
            ["data"] = dataPoints,
            ["message"] = "Chart visualization is ready. Interact with the chart to explore the data.",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>
    /// MCP Apps example: Form Input - collects structured data from user
    /// The host will render the UI from the ui://form-input resource
    /// </summary>
    private Task<JObject> ExecuteFormInputToolAsync(JObject arguments)
    {
        var formTitle = GetArgument(arguments, "title", "Input Form");
        var submitLabel = GetArgument(arguments, "submitLabel", "Submit");
        var fields = arguments["fields"] as JArray ?? new JArray
        {
            new JObject { ["name"] = "name", ["label"] = "Name", ["type"] = "text", ["required"] = true },
            new JObject { ["name"] = "email", ["label"] = "Email", ["type"] = "email", ["required"] = true },
            new JObject { ["name"] = "message", ["label"] = "Message", ["type"] = "textarea" }
        };
        
        return Task.FromResult(new JObject
        {
            ["title"] = formTitle,
            ["submitLabel"] = submitLabel,
            ["fields"] = fields,
            ["message"] = "Form is ready. Fill in the fields and submit.",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>
    /// MCP Apps example: Data Table - displays tabular data with sorting/filtering
    /// The host will render the UI from the ui://data-table resource
    /// </summary>
    private Task<JObject> ExecuteDataTableToolAsync(JObject arguments)
    {
        var title = GetArgument(arguments, "title", "Data Table");
        var columns = arguments["columns"] as JArray ?? new JArray
        {
            new JObject { ["key"] = "id", ["label"] = "ID", ["sortable"] = true },
            new JObject { ["key"] = "name", ["label"] = "Name", ["sortable"] = true },
            new JObject { ["key"] = "status", ["label"] = "Status", ["sortable"] = true },
            new JObject { ["key"] = "date", ["label"] = "Date", ["sortable"] = true }
        };
        var rows = arguments["rows"] as JArray ?? new JArray
        {
            new JObject { ["id"] = "1", ["name"] = "Project Alpha", ["status"] = "Active", ["date"] = "2026-01-15" },
            new JObject { ["id"] = "2", ["name"] = "Project Beta", ["status"] = "Pending", ["date"] = "2026-01-20" },
            new JObject { ["id"] = "3", ["name"] = "Project Gamma", ["status"] = "Complete", ["date"] = "2026-01-10" }
        };
        
        return Task.FromResult(new JObject
        {
            ["title"] = title,
            ["columns"] = columns,
            ["rows"] = rows,
            ["selectable"] = GetBoolArgument(arguments, "selectable", true),
            ["message"] = "Table is ready. Click rows to select, click headers to sort.",
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>
    /// MCP Apps example: Confirm Action - shows confirmation dialog for destructive actions
    /// The host will render the UI from the ui://confirm-action resource
    /// </summary>
    private Task<JObject> ExecuteConfirmActionToolAsync(JObject arguments)
    {
        var title = GetArgument(arguments, "title", "Confirm Action");
        var message = GetArgument(arguments, "message", "Are you sure you want to proceed?");
        var confirmLabel = GetArgument(arguments, "confirmLabel", "Confirm");
        var cancelLabel = GetArgument(arguments, "cancelLabel", "Cancel");
        var variant = GetArgument(arguments, "variant", "warning"); // info, warning, danger
        
        return Task.FromResult(new JObject
        {
            ["title"] = title,
            ["message"] = message,
            ["confirmLabel"] = confirmLabel,
            ["cancelLabel"] = cancelLabel,
            ["variant"] = variant,
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        });
    }

    // ========================================
    // EXTERNAL API HELPERS
    // ========================================

    /// <summary>
    /// Send a request to an external API with optional retry logic
    /// Forwards the connector's authorization header if present
    /// </summary>
    /// <param name="method">HTTP method (GET, POST, PUT, PATCH, DELETE)</param>
    /// <param name="url">Full URL to call</param>
    /// <param name="body">Optional JSON body for POST/PUT/PATCH</param>
    /// <param name="maxRetries">Max retry attempts for transient failures (default 3)</param>
    /// <param name="retryDelayMs">Initial delay between retries in ms (doubles each retry)</param>
    private async Task<JObject> SendExternalRequestAsync(
        HttpMethod method, 
        string url, 
        JObject body = null,
        int maxRetries = 3,
        int retryDelayMs = 500)
    {
        Exception lastException = null;
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(method, url);
                
                // Forward OAuth token from connector (for delegated auth scenarios)
                if (this.Context.Request.Headers.Authorization != null)
                {
                    request.Headers.Authorization = this.Context.Request.Headers.Authorization;
                }
                
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                
                // Add body for POST, PUT, PATCH (not GET or DELETE)
                if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
                {
                    request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                }

                var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Don't retry client errors (4xx), only server errors (5xx) and specific codes
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    
                    // Retry on 429 (rate limit), 502, 503, 504 (transient server errors)
                    if (attempt < maxRetries && (statusCode == 429 || statusCode >= 502))
                    {
                        lastException = new Exception($"API returned {statusCode}: {content}");
                        await Task.Delay(retryDelayMs * (int)Math.Pow(2, attempt)).ConfigureAwait(false);
                        continue;
                    }
                    
                    throw new Exception($"API request failed ({statusCode}): {content}");
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
            catch (Exception ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                lastException = ex;
                await Task.Delay(retryDelayMs * (int)Math.Pow(2, attempt)).ConfigureAwait(false);
            }
        }

        throw lastException ?? new Exception("Request failed after retries");
    }

    /// <summary>
    /// Send a DELETE request to an external API
    /// </summary>
    private Task<JObject> SendDeleteRequestAsync(string url, int maxRetries = 3)
    {
        return SendExternalRequestAsync(HttpMethod.Delete, url, null, maxRetries);
    }

    /// <summary>
    /// Send a GET request to an external API
    /// </summary>
    private Task<JObject> SendGetRequestAsync(string url, int maxRetries = 3)
    {
        return SendExternalRequestAsync(HttpMethod.Get, url, null, maxRetries);
    }

    /// <summary>
    /// Send a POST request to an external API
    /// </summary>
    private Task<JObject> SendPostRequestAsync(string url, JObject body = null, int maxRetries = 3)
    {
        return SendExternalRequestAsync(HttpMethod.Post, url, body, maxRetries);
    }

    /// <summary>
    /// Determine if an exception is transient and worth retrying
    /// </summary>
    private bool IsTransientError(Exception ex)
    {
        // Timeout, network errors, etc.
        if (ex is TaskCanceledException || ex is HttpRequestException)
            return true;
        
        // Check for transient error messages
        var msg = ex.Message?.ToLowerInvariant() ?? "";
        return msg.Contains("timeout") || 
               msg.Contains("connection") || 
               msg.Contains("network") ||
               msg.Contains("temporarily");
    }

    // ========================================
    // CACHING INFRASTRUCTURE
    // ========================================

    /// <summary>
    /// Simple in-memory cache with TTL (static for persistence across requests in same connector instance)
    /// Note: Cache is lost when connector instance recycles. For production, consider external cache.
    /// </summary>
    private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();
    private static readonly object _cacheLock = new object();

    private class CacheEntry
    {
        public JToken Value { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Get item from cache if not expired
    /// </summary>
    /// <param name="key">Cache key</param>
    /// <returns>Cached value or null if not found/expired</returns>
    private JToken GetFromCache(string key)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow < entry.ExpiresAt)
                {
                    return entry.Value;
                }
                // Expired, remove it
                _cache.Remove(key);
            }
            return null;
        }
    }

    /// <summary>
    /// Add item to cache with TTL
    /// </summary>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to cache</param>
    /// <param name="ttlSeconds">Time-to-live in seconds (default 300 = 5 minutes)</param>
    private void SetInCache(string key, JToken value, int ttlSeconds = 300)
    {
        lock (_cacheLock)
        {
            _cache[key] = new CacheEntry
            {
                Value = value,
                ExpiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds)
            };
        }
    }

    /// <summary>
    /// Remove item from cache
    /// </summary>
    private void RemoveFromCache(string key)
    {
        lock (_cacheLock)
        {
            _cache.Remove(key);
        }
    }

    /// <summary>
    /// Clear all expired entries from cache (housekeeping)
    /// </summary>
    private void CleanupExpiredCache()
    {
        lock (_cacheLock)
        {
            var expiredKeys = _cache.Where(kv => DateTime.UtcNow >= kv.Value.ExpiresAt)
                                    .Select(kv => kv.Key)
                                    .ToList();
            foreach (var key in expiredKeys)
            {
                _cache.Remove(key);
            }
        }
    }

    /// <summary>
    /// Get cached or fetch from source with caching
    /// </summary>
    /// <param name="key">Cache key</param>
    /// <param name="fetchFunc">Function to fetch data if not cached</param>
    /// <param name="ttlSeconds">Cache TTL in seconds</param>
    private async Task<JObject> GetCachedOrFetchAsync(string key, Func<Task<JObject>> fetchFunc, int ttlSeconds = 300)
    {
        var cached = GetFromCache(key);
        if (cached != null)
        {
            return cached as JObject ?? new JObject { ["cached"] = cached };
        }

        var result = await fetchFunc().ConfigureAwait(false);
        SetInCache(key, result, ttlSeconds);
        return result;
    }

    // ========================================
    // PAGINATION HELPERS
    // ========================================

    /// <summary>
    /// Create a paginated response with cursor-based pagination
    /// </summary>
    /// <typeparam name="T">Item type (JObject)</typeparam>
    /// <param name="items">All items</param>
    /// <param name="cursor">Current cursor (null for first page)</param>
    /// <param name="pageSize">Items per page (default 20)</param>
    /// <returns>Paginated result with items and nextCursor</returns>
    private JObject CreatePaginatedResponse(JArray items, string cursor, int pageSize = 20)
    {
        var startIndex = 0;
        
        // Decode cursor (base64 encoded index)
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                if (int.TryParse(decoded, out var idx))
                {
                    startIndex = idx;
                }
            }
            catch { /* Invalid cursor, start from beginning */ }
        }

        // Get page of items
        var pageItems = items.Skip(startIndex).Take(pageSize).ToArray();
        var hasMore = startIndex + pageSize < items.Count;
        
        var result = new JObject
        {
            ["items"] = new JArray(pageItems),
            ["total"] = items.Count,
            ["hasMore"] = hasMore
        };

        // Only include nextCursor if there are more items
        if (hasMore)
        {
            var nextIndex = startIndex + pageSize;
            result["nextCursor"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(nextIndex.ToString()));
        }

        return result;
    }

    /// <summary>
    /// Encode a cursor value
    /// </summary>
    private string EncodeCursor(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Decode a cursor value
    /// </summary>
    private string DecodeCursor(string cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch
        {
            return null;
        }
    }

    // ========================================
    // IMAGE & BINARY CONTENT HELPERS
    // ========================================

    /// <summary>
    /// Create MCP image content from base64 data
    /// Use this to return images in tool results
    /// </summary>
    /// <param name="base64Data">Base64-encoded image data</param>
    /// <param name="mimeType">Image MIME type (e.g., "image/png", "image/jpeg")</param>
    /// <returns>MCP content object for images</returns>
    private JObject CreateImageContent(string base64Data, string mimeType = "image/png")
    {
        return new JObject
        {
            ["type"] = "image",
            ["data"] = base64Data,
            ["mimeType"] = mimeType
        };
    }

    /// <summary>
    /// Fetch an image from URL and return as MCP image content
    /// </summary>
    /// <param name="imageUrl">URL of the image</param>
    /// <param name="mimeType">Override MIME type (auto-detected if null)</param>
    /// <returns>MCP content object for images</returns>
    private async Task<JObject> FetchImageAsContentAsync(string imageUrl, string mimeType = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to fetch image: {response.StatusCode}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var base64 = Convert.ToBase64String(bytes);
        
        // Auto-detect MIME type from response or URL
        var contentType = mimeType 
            ?? response.Content.Headers.ContentType?.MediaType 
            ?? GuessMimeTypeFromUrl(imageUrl);

        return CreateImageContent(base64, contentType);
    }

    /// <summary>
    /// Create MCP resource content (for binary data embedded in tool results)
    /// </summary>
    /// <param name="base64Data">Base64-encoded data</param>
    /// <param name="mimeType">MIME type of the resource</param>
    /// <param name="uri">Optional URI identifier</param>
    /// <returns>MCP embedded resource content</returns>
    private JObject CreateResourceContent(string base64Data, string mimeType, string uri = null)
    {
        var content = new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject
            {
                ["blob"] = base64Data,
                ["mimeType"] = mimeType
            }
        };
        if (!string.IsNullOrEmpty(uri))
        {
            ((JObject)content["resource"])["uri"] = uri;
        }
        return content;
    }

    /// <summary>
    /// Guess MIME type from URL file extension
    /// </summary>
    private string GuessMimeTypeFromUrl(string url)
    {
        var uri = new Uri(url);
        var ext = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Create a tool result with multiple content types (text + image)
    /// </summary>
    /// <param name="textContent">Text description</param>
    /// <param name="imageBase64">Base64 image data (optional)</param>
    /// <param name="imageMimeType">Image MIME type</param>
    /// <returns>MCP content array</returns>
    private JArray CreateRichToolResult(string textContent, string imageBase64 = null, string imageMimeType = "image/png")
    {
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = textContent
            }
        };

        if (!string.IsNullOrEmpty(imageBase64))
        {
            content.Add(CreateImageContent(imageBase64, imageMimeType));
        }

        return content;
    }

    // ========================================
    // MCP APPS UI RESOURCES
    // ========================================

    /// <summary>
    /// Build the list of UI resources for MCP Apps
    /// Each resource is served via the ui:// scheme and rendered in a sandboxed iframe
    /// </summary>
    private JArray BuildUIResourcesList()
    {
        return new JArray
        {
            new JObject
            {
                ["uri"] = "ui://color-picker",
                ["name"] = "Color Picker",
                ["description"] = "Interactive color picker UI component",
                ["mimeType"] = "text/html"
            },
            new JObject
            {
                ["uri"] = "ui://data-visualizer",
                ["name"] = "Data Visualizer",
                ["description"] = "Interactive chart and data visualization component",
                ["mimeType"] = "text/html"
            },
            new JObject
            {
                ["uri"] = "ui://form-input",
                ["name"] = "Form Input",
                ["description"] = "Dynamic form for collecting structured user input",
                ["mimeType"] = "text/html"
            },
            new JObject
            {
                ["uri"] = "ui://data-table",
                ["name"] = "Data Table",
                ["description"] = "Interactive table with sorting and row selection",
                ["mimeType"] = "text/html"
            },
            new JObject
            {
                ["uri"] = "ui://confirm-action",
                ["name"] = "Confirmation Dialog",
                ["description"] = "Confirmation dialog for destructive or important actions",
                ["mimeType"] = "text/html"
            }
            // TODO: Add your custom UI resources here
        };
    }

    /// <summary>
    /// Get UI resource content by URI
    /// Returns bundled HTML/JS that uses @modelcontextprotocol/ext-apps SDK
    /// </summary>
    private string GetUIResourceContent(string uri)
    {
        switch (uri)
        {
            case "ui://color-picker":
                return GetColorPickerUI();

            case "ui://data-visualizer":
                return GetDataVisualizerUI();

            case "ui://form-input":
                return GetFormInputUI();

            case "ui://data-table":
                return GetDataTableUI();

            case "ui://confirm-action":
                return GetConfirmActionUI();

            // TODO: Add your custom UI resources here
            default:
                return null;
        }
    }

    /// <summary>
    /// Color Picker UI - bundled HTML/JS using MCP Apps SDK
    /// Complete reference implementation demonstrating ALL MCP Apps SDK methods
    /// </summary>
    private string GetColorPickerUI()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Color Picker</title>
    <script type=""module"">
        import { 
            App,
            applyDocumentTheme,
            applyHostStyleVariables,
            applyHostFonts
        } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        // ========================================
        // APP INITIALIZATION WITH TOOL CAPABILITY
        // ========================================
        // Declare capabilities.tools if this app provides tools the host can call
        const app = new App(
            { name: 'ColorPickerApp', version: '1.0.0' },
            { 
                tools: { listChanged: false } // Declare app provides tools
            }
        );
        
        let currentColor = '#3B82F6';
        let colorHistory = [];
        let isLoading = false;
        
        const log = (msg) => { 
            console.log('[ColorPicker]', msg); 
            const el = document.getElementById('log');
            if (el) {
                el.textContent = msg;
                el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
        };
        
        const updateStatus = (msg, type = 'info') => {
            const el = document.getElementById('status');
            if (el) {
                el.textContent = msg;
                el.className = 'status ' + type;
            }
        };
        
        // ========================================
        // NOTIFICATION HANDLERS (set BEFORE connect)
        // ========================================
        
        // ✅ ontoolinput - Complete tool arguments received BEFORE execution
        app.ontoolinput = (params) => {
            log('ontoolinput: ' + JSON.stringify(params.arguments));
            isLoading = true;
            updateStatus('Processing...', 'loading');
            if (params.arguments?.defaultColor) {
                currentColor = params.arguments.defaultColor;
                document.getElementById('colorInput').value = currentColor;
                updatePreview();
            }
        };
        
        // ✅ ontoolinputpartial - Streaming partial arguments for progressive rendering
        app.ontoolinputpartial = (params) => {
            log('ontoolinputpartial: streaming arguments...');
            updateStatus('Receiving data...', 'loading');
            // Progressively render as partial args stream in
            if (params.arguments?.defaultColor) {
                currentColor = params.arguments.defaultColor;
                document.getElementById('colorInput').value = currentColor;
                updatePreview();
            }
        };
        
        // ✅ ontoolresult - Tool execution results from server
        app.ontoolresult = (result) => {
            log('ontoolresult received');
            isLoading = false;
            updateStatus('Ready', 'success');
            
            // Prefer structuredContent for typed data
            const structured = result.structuredContent;
            if (structured?.defaultColor) {
                currentColor = structured.defaultColor;
                document.getElementById('colorInput').value = currentColor;
                updatePreview();
                return;
            }
            
            // Fallback to parsing text content
            const text = result.content?.find(c => c.type === 'text')?.text;
            if (text) {
                try {
                    const data = JSON.parse(text);
                    if (data.defaultColor) {
                        currentColor = data.defaultColor;
                        document.getElementById('colorInput').value = currentColor;
                        updatePreview();
                    }
                } catch (e) { /* not JSON */ }
            }
        };
        
        // ✅ ontoolcancelled - Tool was cancelled by user or host
        app.ontoolcancelled = (params) => {
            log('ontoolcancelled: ' + (params.reason || 'no reason'));
            isLoading = false;
            updateStatus('Cancelled', 'warning');
        };
        
        // ✅ onhostcontextchanged - Theme/locale/displayMode changed
        app.onhostcontextchanged = (ctx) => {
            log('onhostcontextchanged: ' + JSON.stringify(ctx));
            
            // Apply theme automatically using SDK helper
            if (ctx.theme) {
                applyDocumentTheme(ctx.theme);
                document.body.classList.remove('light', 'dark');
                document.body.classList.add(ctx.theme);
            }
            
            // Apply host style variables if provided
            if (ctx.styles?.variables) {
                applyHostStyleVariables(ctx.styles.variables);
            }
            
            // Apply host fonts if provided
            if (ctx.styles?.fonts) {
                applyHostFonts(ctx.styles.fonts);
            }
            
            // Handle display mode changes
            if (ctx.displayMode) {
                document.body.setAttribute('data-display-mode', ctx.displayMode);
            }
            
            // Handle locale changes
            if (ctx.locale) {
                document.documentElement.lang = ctx.locale;
            }
        };
        
        // ✅ onteardown - Graceful shutdown, save state before unmount
        app.onteardown = async (params) => {
            log('onteardown: saving state...');
            
            // Save any unsaved state (e.g., to localStorage for next session)
            try {
                localStorage.setItem('colorPicker_lastColor', currentColor);
                localStorage.setItem('colorPicker_history', JSON.stringify(colorHistory));
            } catch (e) { /* localStorage not available */ }
            
            // Close any open connections, timers, etc.
            // Return empty object to signal ready for unmount
            return {};
        };
        
        // ✅ onerror - Error handler
        app.onerror = (error) => {
            log('onerror: ' + error.message);
            console.error('[ColorPicker] Error:', error);
            updateStatus('Error: ' + error.message, 'error');
        };
        
        // ✅ oncalltool - Handle tool calls from host (app-side tool execution)
        app.oncalltool = async (params, extra) => {
            log('oncalltool: ' + params.name);
            
            switch (params.name) {
                case 'get_current_color':
                    return { 
                        content: [{ type: 'text', text: currentColor }],
                        structuredContent: { color: currentColor, format: 'hex' }
                    };
                    
                case 'set_color':
                    if (params.arguments?.color) {
                        currentColor = params.arguments.color;
                        document.getElementById('colorInput').value = currentColor;
                        updatePreview();
                        addToHistory(currentColor);
                    }
                    return { 
                        content: [{ type: 'text', text: 'Color set to ' + currentColor }],
                        structuredContent: { success: true, color: currentColor }
                    };
                    
                case 'get_color_history':
                    return {
                        content: [{ type: 'text', text: 'Color history: ' + colorHistory.join(', ') }],
                        structuredContent: { history: colorHistory }
                    };
                    
                case 'clear_history':
                    colorHistory = [];
                    renderHistory();
                    return { 
                        content: [{ type: 'text', text: 'History cleared' }],
                        structuredContent: { success: true }
                    };
                    
                default:
                    throw new Error('Unknown tool: ' + params.name);
            }
        };
        
        // ✅ onlisttools - Return available tools this app provides
        app.onlisttools = async () => {
            return {
                tools: [
                    {
                        name: 'get_current_color',
                        description: 'Get the currently selected color in hex format',
                        inputSchema: { type: 'object', properties: {} }
                    },
                    {
                        name: 'set_color',
                        description: 'Set the color picker to a specific color',
                        inputSchema: {
                            type: 'object',
                            properties: {
                                color: { type: 'string', description: 'Color in hex format (e.g., #FF5500)' }
                            },
                            required: ['color']
                        }
                    },
                    {
                        name: 'get_color_history',
                        description: 'Get the list of previously selected colors',
                        inputSchema: { type: 'object', properties: {} }
                    },
                    {
                        name: 'clear_history',
                        description: 'Clear the color selection history',
                        inputSchema: { type: 'object', properties: {} }
                    }
                ]
            };
        };
        
        // ========================================
        // UI HELPER FUNCTIONS
        // ========================================
        
        window.updatePreview = function() {
            currentColor = document.getElementById('colorInput').value;
            document.getElementById('preview').style.backgroundColor = currentColor;
            document.getElementById('hexValue').textContent = currentColor;
            
            // Calculate RGB values
            const r = parseInt(currentColor.slice(1, 3), 16);
            const g = parseInt(currentColor.slice(3, 5), 16);
            const b = parseInt(currentColor.slice(5, 7), 16);
            document.getElementById('rgbValue').textContent = `rgb(${r}, ${g}, ${b})`;
            
            // Notify host of size change (in case content changed)
            notifySizeChanged();
        };
        
        function addToHistory(color) {
            if (!colorHistory.includes(color)) {
                colorHistory.unshift(color);
                if (colorHistory.length > 8) colorHistory.pop();
                renderHistory();
            }
        }
        
        function renderHistory() {
            const container = document.getElementById('history');
            container.innerHTML = '';
            colorHistory.forEach(color => {
                const swatch = document.createElement('div');
                swatch.className = 'history-swatch';
                swatch.style.backgroundColor = color;
                swatch.title = color;
                swatch.onclick = () => {
                    currentColor = color;
                    document.getElementById('colorInput').value = color;
                    updatePreview();
                };
                container.appendChild(swatch);
            });
        }
        
        // ========================================
        // MCP APPS SDK METHODS - ALL IMPLEMENTED
        // ========================================
        
        // ✅ updateModelContext - Update context sent to model
        window.selectColor = async function() {
            try {
                addToHistory(currentColor);
                await app.updateModelContext({
                    content: [{ type: 'text', text: `User selected color: ${currentColor}` }],
                    structuredContent: { 
                        selectedColor: currentColor,
                        rgb: hexToRgb(currentColor),
                        timestamp: new Date().toISOString()
                    }
                });
                updateStatus('Color selected!', 'success');
                log('updateModelContext: sent ' + currentColor);
            } catch (e) {
                updateStatus('Failed to send', 'error');
                log('updateModelContext failed: ' + e.message);
            }
        };
        
        function hexToRgb(hex) {
            const r = parseInt(hex.slice(1, 3), 16);
            const g = parseInt(hex.slice(3, 5), 16);
            const b = parseInt(hex.slice(5, 7), 16);
            return { r, g, b };
        }
        
        // ✅ sendMessage - Send message to chat (triggers model response)
        window.sendMessageDemo = async function() {
            try {
                await app.sendMessage({
                    role: 'user',
                    content: [{ type: 'text', text: `Apply color ${currentColor} to my project theme` }]
                });
                updateStatus('Message sent!', 'success');
                log('sendMessage: sent');
            } catch (e) {
                updateStatus('sendMessage not available', 'warning');
                log('sendMessage: ' + e.message);
            }
        };
        
        // ✅ callServerTool - Call a tool on the MCP server
        window.callServerToolDemo = async function() {
            try {
                const result = await app.callServerTool({
                    name: 'echo',
                    arguments: { message: 'Color selected: ' + currentColor }
                });
                updateStatus('Server tool called!', 'success');
                log('callServerTool result: ' + JSON.stringify(result.content?.[0]?.text || result));
            } catch (e) {
                updateStatus('callServerTool not available', 'warning');
                log('callServerTool: ' + e.message);
            }
        };
        
        // ✅ requestDisplayMode - Change display mode
        window.requestDisplayMode = async function(mode) {
            try {
                const ctx = app.getHostContext();
                const available = ctx?.availableDisplayModes || [];
                
                if (available.length === 0) {
                    log('No display modes available');
                    return;
                }
                
                // Cycle through modes or use specified mode
                if (!mode) {
                    const current = ctx?.displayMode || 'embedded';
                    const idx = available.indexOf(current);
                    mode = available[(idx + 1) % available.length];
                }
                
                const result = await app.requestDisplayMode({ mode });
                updateStatus('Display: ' + result.mode, 'success');
                log('requestDisplayMode: ' + result.mode);
            } catch (e) {
                updateStatus('requestDisplayMode not available', 'warning');
                log('requestDisplayMode: ' + e.message);
            }
        };
        
        // ✅ openLink - Open external URL
        window.openLinkDemo = async function() {
            try {
                const result = await app.openLink({ 
                    url: 'https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_Colors' 
                });
                if (result.isError) {
                    updateStatus('Link blocked by host', 'warning');
                    log('openLink: blocked');
                } else {
                    updateStatus('Link opened!', 'success');
                    log('openLink: opened');
                }
            } catch (e) {
                updateStatus('openLink not available', 'warning');
                log('openLink: ' + e.message);
            }
        };
        
        // ✅ sendLog - Send log to host for debugging
        window.sendLogDemo = async function() {
            try {
                app.sendLog({ 
                    level: 'info', 
                    data: { 
                        message: 'User interacting with color picker',
                        currentColor,
                        historyCount: colorHistory.length
                    },
                    logger: 'ColorPickerApp'
                });
                updateStatus('Log sent!', 'success');
                log('sendLog: sent');
            } catch (e) {
                updateStatus('sendLog not available', 'warning');
                log('sendLog: ' + e.message);
            }
        };
        
        // ✅ sendSizeChanged - Notify host of preferred iframe size
        function notifySizeChanged() {
            try {
                const container = document.querySelector('.picker-container');
                if (container) {
                    app.sendSizeChanged({ 
                        width: container.offsetWidth,
                        height: container.offsetHeight
                    });
                }
            } catch (e) {
                // Size change notification not critical
            }
        }
        
        // ========================================
        // HOST CONTEXT ACCESSORS
        // ========================================
        
        window.showHostInfo = function() {
            const ctx = app.getHostContext();
            const caps = app.getHostCapabilities();
            const version = app.getHostVersion();
            
            const info = [
                'Host: ' + (version?.name || 'unknown'),
                'Version: ' + (version?.version || 'unknown'),
                'Theme: ' + (ctx?.theme || 'none'),
                'Display: ' + (ctx?.displayMode || 'embedded'),
                'Locale: ' + (ctx?.locale || navigator.language),
                'Tools: ' + (caps?.tools ? 'Yes' : 'No'),
                'Messages: ' + (caps?.messages ? 'Yes' : 'No'),
                'OpenLink: ' + (caps?.openLink ? 'Yes' : 'No')
            ];
            
            alert(info.join('\\n'));
            log('Host info displayed');
        };
        
        // ========================================
        // CONNECT TO HOST
        // ========================================
        
        // Restore saved state before connecting
        try {
            const savedColor = localStorage.getItem('colorPicker_lastColor');
            const savedHistory = localStorage.getItem('colorPicker_history');
            if (savedColor) currentColor = savedColor;
            if (savedHistory) colorHistory = JSON.parse(savedHistory);
        } catch (e) { /* localStorage not available */ }
        
        await app.connect();
        
        // Apply initial theme from host context
        const ctx = app.getHostContext();
        if (ctx?.theme) {
            applyDocumentTheme(ctx.theme);
            document.body.classList.add(ctx.theme);
        }
        if (ctx?.styles?.variables) {
            applyHostStyleVariables(ctx.styles.variables);
        }
        
        // Initialize UI
        document.getElementById('colorInput').value = currentColor;
        updatePreview();
        renderHistory();
        
        const hostInfo = app.getHostVersion();
        log('Connected to: ' + (hostInfo?.name || 'host'));
        updateStatus('Ready', 'success');
        
        // Notify initial size
        setTimeout(notifySizeChanged, 100);
    </script>
    <style>
        :root {
            --primary: #3B82F6;
            --primary-hover: #2563EB;
            --success: #10B981;
            --warning: #F59E0B;
            --error: #EF4444;
            --bg: #ffffff;
            --text: #1f2937;
            --text-secondary: #6B7280;
            --border: #e5e7eb;
        }
        
        body.dark {
            --bg: #1f2937;
            --text: #f9fafb;
            --text-secondary: #9CA3AF;
            --border: #374151;
        }
        
        body { 
            font-family: system-ui, -apple-system, sans-serif; 
            padding: 16px; 
            max-width: 340px; 
            margin: 0 auto;
            background: var(--bg);
            color: var(--text);
            transition: background 0.2s, color 0.2s;
        }
        
        .picker-container { display: flex; flex-direction: column; gap: 10px; }
        #preview { width: 100%; height: 70px; border-radius: 8px; border: 1px solid var(--border); transition: background-color 0.15s; }
        input[type=""color""] { width: 100%; height: 40px; cursor: pointer; border: none; border-radius: 6px; }
        .color-values { display: flex; justify-content: space-between; font-family: monospace; font-size: 13px; }
        #hexValue, #rgbValue { color: var(--text-secondary); }
        
        .status { font-size: 12px; text-align: center; min-height: 16px; padding: 4px; border-radius: 4px; }
        .status.success { color: var(--success); }
        .status.warning { color: var(--warning); }
        .status.error { color: var(--error); }
        .status.loading { color: var(--primary); }
        
        #log { font-size: 10px; color: var(--text-secondary); text-align: center; min-height: 14px; word-break: break-all; }
        
        .btn { 
            background: var(--primary); 
            color: white; 
            border: none; 
            padding: 10px; 
            border-radius: 6px; 
            cursor: pointer; 
            font-size: 13px; 
            width: 100%;
            transition: background 0.15s;
        }
        .btn:hover { background: var(--primary-hover); }
        .btn:active { transform: scale(0.98); }
        .btn.secondary { background: #6B7280; font-size: 11px; padding: 7px; }
        .btn.secondary:hover { background: #4B5563; }
        
        .btn-row { display: flex; gap: 5px; }
        .btn-row .btn { flex: 1; }
        
        .section-label { 
            margin: 12px 0 6px 0; 
            font-size: 11px; 
            color: var(--text-secondary); 
            text-transform: uppercase;
            letter-spacing: 0.5px;
            border-top: 1px solid var(--border); 
            padding-top: 10px; 
        }
        
        #history { display: flex; gap: 4px; flex-wrap: wrap; min-height: 24px; }
        .history-swatch { 
            width: 24px; 
            height: 24px; 
            border-radius: 4px; 
            border: 1px solid var(--border); 
            cursor: pointer;
            transition: transform 0.1s;
        }
        .history-swatch:hover { transform: scale(1.1); }
    </style>
</head>
<body>
    <div class=""picker-container"">
        <div id=""preview"" style=""background-color: #3B82F6;""></div>
        <input type=""color"" id=""colorInput"" value=""#3B82F6"" onchange=""updatePreview()"" oninput=""updatePreview()"">
        <div class=""color-values"">
            <span id=""hexValue"">#3B82F6</span>
            <span id=""rgbValue"">rgb(59, 130, 246)</span>
        </div>
        <button class=""btn"" onclick=""selectColor()"">Select Color</button>
        <div id=""status"" class=""status""></div>
        
        <div class=""section-label"">Recent Colors</div>
        <div id=""history""></div>
        
        <div class=""section-label"">SDK Methods</div>
        <div class=""btn-row"">
            <button class=""btn secondary"" onclick=""sendMessageDemo()"">sendMessage</button>
            <button class=""btn secondary"" onclick=""callServerToolDemo()"">callServerTool</button>
        </div>
        <div class=""btn-row"">
            <button class=""btn secondary"" onclick=""requestDisplayMode()"">displayMode</button>
            <button class=""btn secondary"" onclick=""openLinkDemo()"">openLink</button>
        </div>
        <div class=""btn-row"">
            <button class=""btn secondary"" onclick=""sendLogDemo()"">sendLog</button>
            <button class=""btn secondary"" onclick=""showHostInfo()"">hostInfo</button>
        </div>
        <div id=""log""></div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Data Visualizer UI - Complete MCP Apps SDK implementation with charts
    /// Demonstrates all SDK methods for data visualization scenarios
    /// </summary>
    private string GetDataVisualizerUI()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Data Visualizer</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>
    <script type=""module"">
        import { 
            App,
            applyDocumentTheme,
            applyHostStyleVariables,
            applyHostFonts
        } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        // ========================================
        // APP INITIALIZATION WITH TOOL CAPABILITY
        // ========================================
        const app = new App(
            { name: 'DataVisualizerApp', version: '1.0.0' },
            { tools: { listChanged: false } }
        );
        
        let chart = null;
        let chartConfig = { chartType: 'bar', title: 'Data Visualization', data: [] };
        let selectedDataPoint = null;
        
        const log = (msg) => {
            console.log('[DataVisualizer]', msg);
            const el = document.getElementById('log');
            if (el) el.textContent = msg;
        };
        
        const updateStatus = (msg, type = 'info') => {
            const el = document.getElementById('status');
            if (el) {
                el.textContent = msg;
                el.className = 'status ' + type;
            }
        };
        
        // ========================================
        // ALL NOTIFICATION HANDLERS
        // ========================================
        
        // ontoolinput - Called with args BEFORE tool executes
        app.ontoolinput = (params) => {
            log('ontoolinput: preparing chart...');
            updateStatus('Loading...', 'loading');
            if (params.arguments?.chartType) chartConfig.chartType = params.arguments.chartType;
            if (params.arguments?.title) {
                chartConfig.title = params.arguments.title;
                document.getElementById('title').textContent = chartConfig.title;
            }
        };
        
        // ontoolinputpartial - Streaming partial arguments
        app.ontoolinputpartial = (params) => {
            log('ontoolinputpartial: streaming...');
            updateStatus('Receiving data...', 'loading');
            // Progressively update UI as data streams in
            if (params.arguments?.data && Array.isArray(params.arguments.data)) {
                chartConfig.data = params.arguments.data;
                renderChart();
            }
        };
        
        // ontoolresult - Called with result AFTER tool executes
        app.ontoolresult = (result) => {
            log('ontoolresult: rendering chart');
            updateStatus('Ready', 'success');
            
            const structured = result.structuredContent;
            if (structured?.data) {
                chartConfig = { ...chartConfig, ...structured };
                renderChart();
                return;
            }
            
            const text = result.content?.find(c => c.type === 'text')?.text;
            if (text) {
                try {
                    const data = JSON.parse(text);
                    chartConfig = { ...chartConfig, ...data };
                    renderChart();
                } catch (e) { log('Error parsing data'); }
            }
        };
        
        // ontoolcancelled - Tool was cancelled
        app.ontoolcancelled = (params) => {
            log('ontoolcancelled: ' + (params.reason || 'unknown'));
            updateStatus('Cancelled', 'warning');
        };
        
        // onhostcontextchanged - Theme/locale/displayMode changed
        app.onhostcontextchanged = (ctx) => {
            log('onhostcontextchanged');
            if (ctx.theme) {
                applyDocumentTheme(ctx.theme);
                document.body.classList.remove('light', 'dark');
                document.body.classList.add(ctx.theme);
                // Re-render chart with theme-appropriate colors
                renderChart();
            }
            if (ctx.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
            if (ctx.styles?.fonts) applyHostFonts(ctx.styles.fonts);
        };
        
        // onteardown - Graceful shutdown
        app.onteardown = async (params) => {
            log('onteardown: saving state...');
            try {
                localStorage.setItem('dataVisualizer_config', JSON.stringify(chartConfig));
            } catch (e) {}
            return {};
        };
        
        // onerror - Error handler
        app.onerror = (error) => {
            log('onerror: ' + error.message);
            updateStatus('Error: ' + error.message, 'error');
        };
        
        // oncalltool - Handle tool calls from host
        app.oncalltool = async (params, extra) => {
            log('oncalltool: ' + params.name);
            
            switch (params.name) {
                case 'get_chart_data':
                    return {
                        content: [{ type: 'text', text: JSON.stringify(chartConfig.data) }],
                        structuredContent: { data: chartConfig.data, chartType: chartConfig.chartType }
                    };
                    
                case 'set_chart_type':
                    if (params.arguments?.chartType) {
                        chartConfig.chartType = params.arguments.chartType;
                        renderChart();
                    }
                    return {
                        content: [{ type: 'text', text: 'Chart type set to ' + chartConfig.chartType }],
                        structuredContent: { success: true, chartType: chartConfig.chartType }
                    };
                    
                case 'add_data_point':
                    if (params.arguments?.label && params.arguments?.value !== undefined) {
                        chartConfig.data.push({ label: params.arguments.label, value: params.arguments.value });
                        renderChart();
                    }
                    return {
                        content: [{ type: 'text', text: 'Data point added' }],
                        structuredContent: { success: true, dataCount: chartConfig.data.length }
                    };
                    
                case 'get_selected_point':
                    return {
                        content: [{ type: 'text', text: selectedDataPoint ? JSON.stringify(selectedDataPoint) : 'No selection' }],
                        structuredContent: { selection: selectedDataPoint }
                    };
                    
                default:
                    throw new Error('Unknown tool: ' + params.name);
            }
        };
        
        // onlisttools - Return available tools
        app.onlisttools = async () => {
            return {
                tools: [
                    { name: 'get_chart_data', description: 'Get current chart data', inputSchema: { type: 'object', properties: {} } },
                    { name: 'set_chart_type', description: 'Change chart type', inputSchema: { type: 'object', properties: { chartType: { type: 'string', enum: ['bar', 'line', 'pie', 'doughnut'] } }, required: ['chartType'] } },
                    { name: 'add_data_point', description: 'Add a data point', inputSchema: { type: 'object', properties: { label: { type: 'string' }, value: { type: 'number' } }, required: ['label', 'value'] } },
                    { name: 'get_selected_point', description: 'Get currently selected data point', inputSchema: { type: 'object', properties: {} } }
                ]
            };
        };
        
        // ========================================
        // CHART RENDERING
        // ========================================
        
        function getChartColors() {
            const isDark = document.body.classList.contains('dark');
            return isDark
                ? ['#60A5FA', '#34D399', '#FBBF24', '#F87171', '#A78BFA', '#22D3EE', '#F472B6']
                : ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#06B6D4', '#EC4899'];
        }
        
        function renderChart() {
            const ctx = document.getElementById('chart').getContext('2d');
            if (chart) chart.destroy();
            
            const labels = chartConfig.data?.map(d => d.label) || ['A', 'B', 'C'];
            const values = chartConfig.data?.map(d => d.value) || [10, 20, 30];
            const colors = getChartColors();
            
            const isDark = document.body.classList.contains('dark');
            
            chart = new Chart(ctx, {
                type: chartConfig.chartType || 'bar',
                data: {
                    labels: labels,
                    datasets: [{
                        label: chartConfig.title || 'Data',
                        data: values,
                        backgroundColor: colors,
                        borderWidth: 1,
                        borderColor: isDark ? '#374151' : '#e5e7eb'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: true,
                    plugins: { 
                        legend: { 
                            position: 'top',
                            labels: { color: isDark ? '#f9fafb' : '#1f2937' }
                        }
                    },
                    scales: chartConfig.chartType !== 'pie' && chartConfig.chartType !== 'doughnut' ? {
                        y: { ticks: { color: isDark ? '#9CA3AF' : '#6B7280' }, grid: { color: isDark ? '#374151' : '#e5e7eb' } },
                        x: { ticks: { color: isDark ? '#9CA3AF' : '#6B7280' }, grid: { color: isDark ? '#374151' : '#e5e7eb' } }
                    } : undefined,
                    onClick: handleChartClick
                }
            });
            
            document.getElementById('title').textContent = chartConfig.title || 'Data Visualization';
            notifySizeChanged();
        }
        
        function handleChartClick(evt, elements) {
            if (elements.length > 0) {
                const idx = elements[0].index;
                const label = chartConfig.data?.[idx]?.label || 'Unknown';
                const value = chartConfig.data?.[idx]?.value || 0;
                selectedDataPoint = { label, value, index: idx };
                
                app.updateModelContext({
                    content: [{ type: 'text', text: `User clicked: ${label} = ${value}` }],
                    structuredContent: { selection: selectedDataPoint }
                });
                updateStatus(`Selected: ${label} = ${value}`, 'success');
                log(`Selected: ${label}`);
            }
        }
        
        // ========================================
        // SDK METHODS
        // ========================================
        
        function notifySizeChanged() {
            try {
                const container = document.querySelector('.chart-container');
                if (container) {
                    app.sendSizeChanged({ width: container.offsetWidth, height: container.offsetHeight });
                }
            } catch (e) {}
        }
        
        window.exportSummary = async function() {
            const summary = {
                title: chartConfig.title,
                chartType: chartConfig.chartType,
                dataPoints: chartConfig.data?.length || 0,
                total: chartConfig.data?.reduce((sum, d) => sum + (d.value || 0), 0) || 0,
                average: chartConfig.data?.length ? (chartConfig.data.reduce((sum, d) => sum + (d.value || 0), 0) / chartConfig.data.length).toFixed(2) : 0
            };
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: `Chart Summary:\n- Title: ${summary.title}\n- Type: ${summary.chartType}\n- Points: ${summary.dataPoints}\n- Total: ${summary.total}\n- Average: ${summary.average}` }],
                    structuredContent: summary
                });
                updateStatus('Summary exported!', 'success');
                log('Summary sent');
            } catch (e) {
                updateStatus('Export failed', 'error');
                log('Export failed: ' + e.message);
            }
        };
        
        window.changeChartType = async function(type) {
            chartConfig.chartType = type;
            renderChart();
            log('Chart type: ' + type);
        };
        
        window.sendMessageDemo = async function() {
            try {
                await app.sendMessage({
                    role: 'user',
                    content: [{ type: 'text', text: `Analyze this ${chartConfig.chartType} chart with ${chartConfig.data?.length || 0} data points` }]
                });
                updateStatus('Message sent!', 'success');
            } catch (e) {
                updateStatus('sendMessage not available', 'warning');
                log('sendMessage: ' + e.message);
            }
        };
        
        window.callServerToolDemo = async function() {
            try {
                const result = await app.callServerTool({
                    name: 'echo',
                    arguments: { message: 'Chart has ' + chartConfig.data?.length + ' data points' }
                });
                updateStatus('Server tool called!', 'success');
                log('callServerTool: ' + (result.content?.[0]?.text || 'done'));
            } catch (e) {
                updateStatus('callServerTool not available', 'warning');
                log('callServerTool: ' + e.message);
            }
        };
        
        window.openDocsDemo = async function() {
            try {
                const result = await app.openLink({ url: 'https://www.chartjs.org/docs/latest/' });
                if (result.isError) updateStatus('Link blocked', 'warning');
                else updateStatus('Docs opened!', 'success');
            } catch (e) {
                updateStatus('openLink not available', 'warning');
                log('openLink: ' + e.message);
            }
        };
        
        // ========================================
        // CONNECT AND INITIALIZE
        // ========================================
        
        // Restore saved config
        try {
            const saved = localStorage.getItem('dataVisualizer_config');
            if (saved) chartConfig = { ...chartConfig, ...JSON.parse(saved) };
        } catch (e) {}
        
        await app.connect();
        
        // Apply initial theme
        const ctx = app.getHostContext();
        if (ctx?.theme) {
            applyDocumentTheme(ctx.theme);
            document.body.classList.add(ctx.theme);
        }
        if (ctx?.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
        
        renderChart();
        log('Connected - ready');
        updateStatus('Ready', 'success');
    </script>
    <style>
        :root {
            --bg: #ffffff;
            --text: #1f2937;
            --text-secondary: #6B7280;
            --border: #e5e7eb;
            --surface: #f9fafb;
        }
        body.dark {
            --bg: #1f2937;
            --text: #f9fafb;
            --text-secondary: #9CA3AF;
            --border: #374151;
            --surface: #111827;
        }
        body { font-family: system-ui, -apple-system, sans-serif; padding: 16px; margin: 0; background: var(--bg); color: var(--text); }
        .chart-container { background: var(--bg); padding: 16px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); max-width: 500px; }
        h2 { margin: 0 0 12px 0; font-size: 18px; }
        #chart { max-height: 280px; }
        .status { font-size: 12px; min-height: 16px; margin-top: 8px; }
        .status.success { color: #10B981; }
        .status.warning { color: #F59E0B; }
        .status.error { color: #EF4444; }
        .status.loading { color: #3B82F6; }
        #log { font-size: 10px; color: var(--text-secondary); margin-top: 4px; }
        .btn-row { display: flex; gap: 6px; margin-top: 10px; flex-wrap: wrap; }
        .btn { background: #3B82F6; color: white; border: none; padding: 8px 12px; border-radius: 6px; cursor: pointer; font-size: 12px; }
        .btn:hover { background: #2563EB; }
        .btn.secondary { background: #6B7280; }
        .btn.secondary:hover { background: #4B5563; }
        .chart-types { display: flex; gap: 4px; margin: 10px 0; }
        .chart-types button { padding: 6px 10px; font-size: 11px; background: var(--surface); border: 1px solid var(--border); color: var(--text); border-radius: 4px; cursor: pointer; }
        .chart-types button:hover { background: var(--border); }
    </style>
</head>
<body>
    <div class=""chart-container"">
        <h2 id=""title"">Data Visualization</h2>
        <canvas id=""chart""></canvas>
        <div class=""chart-types"">
            <button onclick=""changeChartType('bar')"">Bar</button>
            <button onclick=""changeChartType('line')"">Line</button>
            <button onclick=""changeChartType('pie')"">Pie</button>
            <button onclick=""changeChartType('doughnut')"">Donut</button>
        </div>
        <div id=""status"" class=""status""></div>
        <div class=""btn-row"">
            <button class=""btn"" onclick=""exportSummary()"">Export Summary</button>
            <button class=""btn secondary"" onclick=""sendMessageDemo()"">sendMessage</button>
        </div>
        <div class=""btn-row"">
            <button class=""btn secondary"" onclick=""callServerToolDemo()"">callServerTool</button>
            <button class=""btn secondary"" onclick=""openDocsDemo()"">openLink</button>
        </div>
        <div id=""log""></div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Form Input UI - dynamic form with validation for collecting structured data
    /// Shows: Dynamic field generation, validation, form submission via updateModelContext
    /// </summary>
    private string GetFormInputUI()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Form Input</title>
    <script type=""module"">
        import { 
            App,
            applyDocumentTheme,
            applyHostStyleVariables,
            applyHostFonts
        } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        // ========================================
        // APP INITIALIZATION WITH TOOL CAPABILITY
        // ========================================
        const app = new App(
            { name: 'FormInputApp', version: '1.0.0' },
            { tools: { listChanged: false } }
        );
        
        let formConfig = { title: 'Input Form', submitLabel: 'Submit', fields: [] };
        let formData = {};
        let isDirty = false;
        
        const log = (msg) => {
            console.log('[FormInput]', msg);
            const el = document.getElementById('log');
            if (el) el.textContent = msg;
        };
        
        const updateStatus = (msg, type = 'info') => {
            const el = document.getElementById('status');
            if (el) {
                el.textContent = msg;
                el.className = 'status ' + type;
            }
        };
        
        // ========================================
        // ALL NOTIFICATION HANDLERS
        // ========================================
        
        // ontoolinput - Setup form from input args BEFORE execution
        app.ontoolinput = (params) => {
            log('ontoolinput: configuring form...');
            updateStatus('Loading...', 'loading');
            if (params.arguments?.title) formConfig.title = params.arguments.title;
            if (params.arguments?.submitLabel) formConfig.submitLabel = params.arguments.submitLabel;
            document.getElementById('formTitle').textContent = formConfig.title;
            document.getElementById('submitBtn').textContent = formConfig.submitLabel;
        };
        
        // ontoolinputpartial - Streaming form config
        app.ontoolinputpartial = (params) => {
            log('ontoolinputpartial: streaming...');
            // Progressively build form as fields stream in
            if (params.arguments?.fields) {
                formConfig.fields = params.arguments.fields;
                renderForm();
            }
        };
        
        // ontoolresult - Render form fields from result
        app.ontoolresult = (result) => {
            log('ontoolresult: building form');
            updateStatus('', 'info');
            const structured = result.structuredContent;
            if (structured) {
                formConfig = { ...formConfig, ...structured };
            } else {
                const text = result.content?.find(c => c.type === 'text')?.text;
                if (text) {
                    try { formConfig = { ...formConfig, ...JSON.parse(text) }; } catch(e) {}
                }
            }
            renderForm();
        };
        
        // ontoolcancelled - Tool was cancelled
        app.ontoolcancelled = (params) => {
            log('ontoolcancelled: ' + (params.reason || 'unknown'));
            updateStatus('Cancelled', 'warning');
        };
        
        // onhostcontextchanged - Theme/locale/displayMode changed
        app.onhostcontextchanged = (ctx) => {
            log('onhostcontextchanged');
            if (ctx.theme) {
                applyDocumentTheme(ctx.theme);
                document.body.classList.remove('light', 'dark');
                document.body.classList.add(ctx.theme);
            }
            if (ctx.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
            if (ctx.styles?.fonts) applyHostFonts(ctx.styles.fonts);
        };
        
        // onteardown - Graceful shutdown, save form state
        app.onteardown = async (params) => {
            log('onteardown: saving state...');
            try {
                // Save current form data in case user needs to restore
                const currentData = collectFormData();
                localStorage.setItem('formInput_draft', JSON.stringify(currentData));
                localStorage.setItem('formInput_config', JSON.stringify(formConfig));
            } catch (e) {}
            return {};
        };
        
        // onerror - Error handler
        app.onerror = (error) => {
            log('onerror: ' + error.message);
            updateStatus('Error: ' + error.message, 'error');
        };
        
        // oncalltool - Handle tool calls from host
        app.oncalltool = async (params, extra) => {
            log('oncalltool: ' + params.name);
            
            switch (params.name) {
                case 'get_form_data':
                    return {
                        content: [{ type: 'text', text: JSON.stringify(collectFormData()) }],
                        structuredContent: { formData: collectFormData(), isDirty }
                    };
                    
                case 'set_field_value':
                    if (params.arguments?.fieldName && params.arguments?.value !== undefined) {
                        const el = document.getElementById(params.arguments.fieldName);
                        if (el) {
                            el.value = params.arguments.value;
                            isDirty = true;
                        }
                    }
                    return { content: [{ type: 'text', text: 'Field updated' }], structuredContent: { success: true } };
                    
                case 'validate_form':
                    const form = document.getElementById('dynamicForm');
                    const isValid = form.checkValidity();
                    return {
                        content: [{ type: 'text', text: isValid ? 'Form is valid' : 'Form has validation errors' }],
                        structuredContent: { isValid, formData: collectFormData() }
                    };
                    
                case 'clear_form':
                    document.getElementById('dynamicForm').reset();
                    isDirty = false;
                    return { content: [{ type: 'text', text: 'Form cleared' }], structuredContent: { success: true } };
                    
                default:
                    throw new Error('Unknown tool: ' + params.name);
            }
        };
        
        // onlisttools - Return available tools
        app.onlisttools = async () => {
            return {
                tools: [
                    { name: 'get_form_data', description: 'Get current form values', inputSchema: { type: 'object', properties: {} } },
                    { name: 'set_field_value', description: 'Set a field value', inputSchema: { type: 'object', properties: { fieldName: { type: 'string' }, value: { type: 'string' } }, required: ['fieldName', 'value'] } },
                    { name: 'validate_form', description: 'Check if form is valid', inputSchema: { type: 'object', properties: {} } },
                    { name: 'clear_form', description: 'Clear all form fields', inputSchema: { type: 'object', properties: {} } }
                ]
            };
        };
        
        // ========================================
        // FORM RENDERING
        // ========================================
        
        function collectFormData() {
            const data = {};
            (formConfig.fields || []).forEach(field => {
                const el = document.getElementById(field.name);
                if (el) data[field.name] = el.value;
            });
            return data;
        }
        
        function renderForm() {
            document.getElementById('formTitle').textContent = formConfig.title;
            document.getElementById('submitBtn').textContent = formConfig.submitLabel;
            
            const container = document.getElementById('fieldsContainer');
            container.innerHTML = '';
            
            (formConfig.fields || []).forEach((field, idx) => {
                const fieldDiv = document.createElement('div');
                fieldDiv.className = 'field';
                
                const label = document.createElement('label');
                label.textContent = field.label + (field.required ? ' *' : '');
                label.setAttribute('for', field.name);
                fieldDiv.appendChild(label);
                
                let input;
                if (field.type === 'textarea') {
                    input = document.createElement('textarea');
                    input.rows = 3;
                } else if (field.type === 'select' && field.options) {
                    input = document.createElement('select');
                    field.options.forEach(opt => {
                        const option = document.createElement('option');
                        option.value = opt.value || opt;
                        option.textContent = opt.label || opt;
                        input.appendChild(option);
                    });
                } else {
                    input = document.createElement('input');
                    input.type = field.type || 'text';
                }
                
                input.id = field.name;
                input.name = field.name;
                input.placeholder = field.placeholder || '';
                if (field.required) input.required = true;
                if (field.value) input.value = field.value;
                input.oninput = () => { isDirty = true; };
                
                fieldDiv.appendChild(input);
                container.appendChild(fieldDiv);
            });
            
            notifySizeChanged();
        }
        
        // ========================================
        // SDK METHODS
        // ========================================
        
        function notifySizeChanged() {
            try {
                const container = document.querySelector('.form-container');
                if (container) {
                    app.sendSizeChanged({ width: container.offsetWidth, height: container.offsetHeight });
                }
            } catch (e) {}
        }
        
        window.submitForm = async function() {
            const form = document.getElementById('dynamicForm');
            if (!form.checkValidity()) {
                form.reportValidity();
                return;
            }
            
            const data = collectFormData();
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: 'Form submitted: ' + JSON.stringify(data, null, 2) }],
                    structuredContent: { formData: data, submitted: true, timestamp: new Date().toISOString() }
                });
                updateStatus('Form submitted!', 'success');
                isDirty = false;
                log('updateModelContext: form data sent');
            } catch (e) {
                updateStatus('Submit failed', 'error');
                log('Error: ' + e.message);
            }
        };
        
        window.resetForm = function() {
            document.getElementById('dynamicForm').reset();
            isDirty = false;
            updateStatus('', 'info');
            log('Form reset');
        };
        
        window.sendMessageDemo = async function() {
            try {
                const data = collectFormData();
                await app.sendMessage({
                    role: 'user',
                    content: [{ type: 'text', text: 'Process this form data: ' + JSON.stringify(data) }]
                });
                updateStatus('Message sent!', 'success');
            } catch (e) {
                updateStatus('sendMessage not available', 'warning');
                log('sendMessage: ' + e.message);
            }
        };
        
        window.callServerToolDemo = async function() {
            try {
                const result = await app.callServerTool({
                    name: 'echo',
                    arguments: { message: 'Fields: ' + formConfig.fields.map(f => f.name).join(', ') }
                });
                updateStatus('Server tool called!', 'success');
            } catch (e) {
                updateStatus('callServerTool not available', 'warning');
                log('callServerTool: ' + e.message);
            }
        };
        
        // ========================================
        // CONNECT AND INITIALIZE
        // ========================================
        
        // Restore saved draft
        try {
            const savedDraft = localStorage.getItem('formInput_draft');
            const savedConfig = localStorage.getItem('formInput_config');
            if (savedConfig) formConfig = { ...formConfig, ...JSON.parse(savedConfig) };
        } catch (e) {}
        
        await app.connect();
        
        // Apply initial theme
        const ctx = app.getHostContext();
        if (ctx?.theme) {
            applyDocumentTheme(ctx.theme);
            document.body.classList.add(ctx.theme);
        }
        if (ctx?.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
        
        log('Connected - waiting for form config...');
    </script>
    <style>
        :root {
            --bg: #ffffff;
            --text: #1f2937;
            --text-secondary: #6B7280;
            --border: #d1d5db;
            --input-bg: #ffffff;
            --focus: #3B82F6;
        }
        body.dark {
            --bg: #1f2937;
            --text: #f9fafb;
            --text-secondary: #9CA3AF;
            --border: #4B5563;
            --input-bg: #374151;
        }
        body { font-family: system-ui, -apple-system, sans-serif; padding: 16px; margin: 0; max-width: 400px; background: var(--bg); color: var(--text); }
        .form-container { background: var(--bg); padding: 16px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
        h2 { margin: 0 0 14px 0; font-size: 18px; }
        .field { margin-bottom: 14px; }
        label { display: block; margin-bottom: 4px; font-size: 13px; font-weight: 500; }
        input, textarea, select { width: 100%; padding: 10px; border: 1px solid var(--border); border-radius: 6px; font-size: 14px; box-sizing: border-box; background: var(--input-bg); color: var(--text); }
        input:focus, textarea:focus, select:focus { outline: none; border-color: var(--focus); box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }
        .btn-row { display: flex; gap: 6px; margin-top: 14px; }
        .btn { flex: 1; padding: 10px; border: none; border-radius: 6px; cursor: pointer; font-size: 13px; font-weight: 500; }
        .btn.primary { background: #3B82F6; color: white; }
        .btn.primary:hover { background: #2563EB; }
        .btn.secondary { background: var(--border); color: var(--text); }
        .btn.secondary:hover { opacity: 0.8; }
        .status { font-size: 12px; text-align: center; margin-top: 10px; min-height: 16px; }
        .status.success { color: #10B981; }
        .status.warning { color: #F59E0B; }
        .status.error { color: #EF4444; }
        .status.loading { color: #3B82F6; }
        #log { font-size: 10px; color: var(--text-secondary); text-align: center; margin-top: 6px; }
        .section-label { font-size: 11px; color: var(--text-secondary); margin: 12px 0 6px 0; border-top: 1px solid var(--border); padding-top: 10px; text-transform: uppercase; }
    </style>
</head>
<body>
    <div class=""form-container"">
        <h2 id=""formTitle"">Input Form</h2>
        <form id=""dynamicForm"" onsubmit=""event.preventDefault(); submitForm();"">
            <div id=""fieldsContainer""></div>
            <div class=""btn-row"">
                <button type=""button"" class=""btn secondary"" onclick=""resetForm()"">Reset</button>
                <button type=""submit"" id=""submitBtn"" class=""btn primary"">Submit</button>
            </div>
        </form>
        <div id=""status"" class=""status""></div>
        <div class=""section-label"">SDK Methods</div>
        <div class=""btn-row"">
            <button type=""button"" class=""btn secondary"" onclick=""sendMessageDemo()"">sendMessage</button>
            <button type=""button"" class=""btn secondary"" onclick=""callServerToolDemo()"">callServerTool</button>
        </div>
        <div id=""log""></div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Data Table UI - interactive table with sorting and row selection
    /// Shows: Dynamic table rendering, sorting, row selection, bulk actions
    /// </summary>
    private string GetDataTableUI()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Data Table</title>
    <script type=""module"">
        import { 
            App,
            applyDocumentTheme,
            applyHostStyleVariables,
            applyHostFonts
        } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        // ========================================
        // APP INITIALIZATION WITH TOOL CAPABILITY
        // ========================================
        const app = new App(
            { name: 'DataTableApp', version: '1.0.0' },
            { tools: { listChanged: false } }
        );
        
        let tableConfig = { title: 'Data Table', columns: [], rows: [], selectable: true };
        let selectedRows = new Set();
        let sortColumn = null;
        let sortDirection = 'asc';
        let filterText = '';
        
        const log = (msg) => {
            console.log('[DataTable]', msg);
            const el = document.getElementById('log');
            if (el) el.textContent = msg;
        };
        
        const updateStatus = (msg, type = 'info') => {
            const el = document.getElementById('status');
            if (el) {
                el.textContent = msg;
                el.className = 'status ' + type;
            }
        };
        
        // ========================================
        // ALL NOTIFICATION HANDLERS
        // ========================================
        
        // ontoolinput - Args before execution
        app.ontoolinput = (params) => {
            log('ontoolinput: preparing table...');
            updateStatus('Loading...', 'loading');
            if (params.arguments?.title) {
                tableConfig.title = params.arguments.title;
                document.getElementById('tableTitle').textContent = tableConfig.title;
            }
        };
        
        // ontoolinputpartial - Streaming rows
        app.ontoolinputpartial = (params) => {
            log('ontoolinputpartial: streaming...');
            // Progressively add rows as they stream in
            if (params.arguments?.rows) {
                tableConfig.rows = params.arguments.rows;
                renderTable();
            }
        };
        
        // ontoolresult - Result after execution
        app.ontoolresult = (result) => {
            log('ontoolresult: rendering table');
            updateStatus('', 'info');
            const structured = result.structuredContent;
            if (structured) {
                tableConfig = { ...tableConfig, ...structured };
            } else {
                const text = result.content?.find(c => c.type === 'text')?.text;
                if (text) {
                    try { tableConfig = { ...tableConfig, ...JSON.parse(text) }; } catch(e) {}
                }
            }
            renderTable();
        };
        
        // ontoolcancelled - Cancelled
        app.ontoolcancelled = (params) => {
            log('ontoolcancelled: ' + (params.reason || 'unknown'));
            updateStatus('Cancelled', 'warning');
        };
        
        // onhostcontextchanged - Theme/locale changed
        app.onhostcontextchanged = (ctx) => {
            log('onhostcontextchanged');
            if (ctx.theme) {
                applyDocumentTheme(ctx.theme);
                document.body.classList.remove('light', 'dark');
                document.body.classList.add(ctx.theme);
            }
            if (ctx.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
            if (ctx.styles?.fonts) applyHostFonts(ctx.styles.fonts);
        };
        
        // onteardown - Graceful shutdown
        app.onteardown = async (params) => {
            log('onteardown: saving state...');
            try {
                localStorage.setItem('dataTable_sort', JSON.stringify({ column: sortColumn, direction: sortDirection }));
                localStorage.setItem('dataTable_selection', JSON.stringify([...selectedRows]));
            } catch (e) {}
            return {};
        };
        
        // onerror - Error handler
        app.onerror = (error) => {
            log('onerror: ' + error.message);
            updateStatus('Error: ' + error.message, 'error');
        };
        
        // oncalltool - Handle tool calls from host
        app.oncalltool = async (params, extra) => {
            log('oncalltool: ' + params.name);
            
            switch (params.name) {
                case 'get_selected_rows':
                    const selected = [...selectedRows].map(idx => tableConfig.rows[idx]);
                    return {
                        content: [{ type: 'text', text: JSON.stringify(selected) }],
                        structuredContent: { selectedRows: selected, count: selected.length }
                    };
                    
                case 'select_row':
                    if (params.arguments?.index !== undefined) {
                        selectedRows.add(params.arguments.index);
                        renderTable();
                    }
                    return { content: [{ type: 'text', text: 'Row selected' }], structuredContent: { success: true } };
                    
                case 'clear_selection':
                    selectedRows.clear();
                    renderTable();
                    return { content: [{ type: 'text', text: 'Selection cleared' }], structuredContent: { success: true } };
                    
                case 'sort_by':
                    if (params.arguments?.column) {
                        sortColumn = params.arguments.column;
                        sortDirection = params.arguments.direction || 'asc';
                        renderTable();
                    }
                    return { content: [{ type: 'text', text: 'Sorted by ' + sortColumn }], structuredContent: { success: true } };
                    
                case 'filter':
                    filterText = params.arguments?.text || '';
                    renderTable();
                    return { content: [{ type: 'text', text: 'Filter applied' }], structuredContent: { success: true, filterText } };
                    
                default:
                    throw new Error('Unknown tool: ' + params.name);
            }
        };
        
        // onlisttools - Return available tools
        app.onlisttools = async () => {
            return {
                tools: [
                    { name: 'get_selected_rows', description: 'Get currently selected row data', inputSchema: { type: 'object', properties: {} } },
                    { name: 'select_row', description: 'Select a row by index', inputSchema: { type: 'object', properties: { index: { type: 'number' } }, required: ['index'] } },
                    { name: 'clear_selection', description: 'Clear all row selections', inputSchema: { type: 'object', properties: {} } },
                    { name: 'sort_by', description: 'Sort table by column', inputSchema: { type: 'object', properties: { column: { type: 'string' }, direction: { type: 'string', enum: ['asc', 'desc'] } }, required: ['column'] } },
                    { name: 'filter', description: 'Filter rows by text', inputSchema: { type: 'object', properties: { text: { type: 'string' } } } }
                ]
            };
        };
        
        // ========================================
        // TABLE RENDERING
        // ========================================
        
        function renderTable() {
            document.getElementById('tableTitle').textContent = tableConfig.title;
            
            const thead = document.getElementById('tableHead');
            thead.innerHTML = '';
            const headerRow = document.createElement('tr');
            
            if (tableConfig.selectable) {
                const th = document.createElement('th');
                th.className = 'checkbox-col';
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.onchange = (e) => toggleAllRows(e.target.checked);
                th.appendChild(checkbox);
                headerRow.appendChild(th);
            }
            
            tableConfig.columns.forEach(col => {
                const th = document.createElement('th');
                th.textContent = col.label;
                if (col.sortable) {
                    th.className = 'sortable';
                    th.onclick = () => sortBy(col.key);
                    if (sortColumn === col.key) {
                        th.textContent += sortDirection === 'asc' ? ' ↑' : ' ↓';
                    }
                }
                headerRow.appendChild(th);
            });
            thead.appendChild(headerRow);
            
            // Filter rows
            let rows = [...tableConfig.rows];
            if (filterText) {
                const lower = filterText.toLowerCase();
                rows = rows.filter(row => 
                    Object.values(row).some(v => String(v).toLowerCase().includes(lower))
                );
            }
            
            // Sort rows
            if (sortColumn) {
                rows.sort((a, b) => {
                    const aVal = a[sortColumn] || '';
                    const bVal = b[sortColumn] || '';
                    const cmp = aVal.toString().localeCompare(bVal.toString());
                    return sortDirection === 'asc' ? cmp : -cmp;
                });
            }
            
            const tbody = document.getElementById('tableBody');
            tbody.innerHTML = '';
            
            rows.forEach((row, idx) => {
                const origIdx = tableConfig.rows.indexOf(row);
                const tr = document.createElement('tr');
                tr.className = selectedRows.has(origIdx) ? 'selected' : '';
                
                if (tableConfig.selectable) {
                    const td = document.createElement('td');
                    td.className = 'checkbox-col';
                    const checkbox = document.createElement('input');
                    checkbox.type = 'checkbox';
                    checkbox.checked = selectedRows.has(origIdx);
                    checkbox.onchange = () => toggleRow(origIdx);
                    td.appendChild(checkbox);
                    tr.appendChild(td);
                }
                
                tableConfig.columns.forEach(col => {
                    const td = document.createElement('td');
                    td.textContent = row[col.key] || '';
                    tr.appendChild(td);
                });
                
                tbody.appendChild(tr);
            });
            
            updateSelectionInfo();
            notifySizeChanged();
        }
        
        function sortBy(column) {
            if (sortColumn === column) {
                sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
            } else {
                sortColumn = column;
                sortDirection = 'asc';
            }
            renderTable();
        }
        
        function toggleRow(idx) {
            if (selectedRows.has(idx)) selectedRows.delete(idx);
            else selectedRows.add(idx);
            renderTable();
        }
        
        function toggleAllRows(checked) {
            if (checked) tableConfig.rows.forEach((_, idx) => selectedRows.add(idx));
            else selectedRows.clear();
            renderTable();
        }
        
        function updateSelectionInfo() {
            const info = document.getElementById('selectionInfo');
            if (selectedRows.size > 0) {
                info.textContent = selectedRows.size + ' row(s) selected';
            } else {
                info.textContent = tableConfig.rows.length + ' rows';
            }
        }
        
        // ========================================
        // SDK METHODS
        // ========================================
        
        function notifySizeChanged() {
            try {
                const container = document.querySelector('.table-container');
                if (container) app.sendSizeChanged({ width: container.offsetWidth, height: container.offsetHeight });
            } catch (e) {}
        }
        
        window.exportSelection = async function() {
            const selected = [...selectedRows].map(idx => tableConfig.rows[idx]);
            if (selected.length === 0) {
                updateStatus('No rows selected', 'warning');
                return;
            }
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: 'Selected ' + selected.length + ' row(s):\\n' + JSON.stringify(selected, null, 2) }],
                    structuredContent: { selectedRows: selected, count: selected.length }
                });
                updateStatus('Exported ' + selected.length + ' rows', 'success');
            } catch (e) {
                updateStatus('Export failed', 'error');
            }
        };
        
        window.filterTable = function(text) {
            filterText = text;
            renderTable();
        };
        
        window.sendMessageDemo = async function() {
            try {
                const selected = [...selectedRows].map(idx => tableConfig.rows[idx]);
                await app.sendMessage({
                    role: 'user',
                    content: [{ type: 'text', text: 'Analyze these ' + selected.length + ' selected rows' }]
                });
                updateStatus('Message sent!', 'success');
            } catch (e) {
                updateStatus('sendMessage not available', 'warning');
            }
        };
        
        window.callServerToolDemo = async function() {
            try {
                const result = await app.callServerTool({
                    name: 'echo',
                    arguments: { message: 'Table has ' + tableConfig.rows.length + ' rows, ' + selectedRows.size + ' selected' }
                });
                updateStatus('Server tool called!', 'success');
            } catch (e) {
                updateStatus('callServerTool not available', 'warning');
            }
        };
        
        // ========================================
        // CONNECT AND INITIALIZE
        // ========================================
        
        // Restore saved state
        try {
            const savedSort = localStorage.getItem('dataTable_sort');
            if (savedSort) {
                const { column, direction } = JSON.parse(savedSort);
                sortColumn = column;
                sortDirection = direction;
            }
        } catch (e) {}
        
        await app.connect();
        
        const ctx = app.getHostContext();
        if (ctx?.theme) {
            applyDocumentTheme(ctx.theme);
            document.body.classList.add(ctx.theme);
        }
        if (ctx?.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
        
        log('Connected - waiting for table data...');
    </script>
    <style>
        :root {
            --bg: #ffffff;
            --text: #1f2937;
            --text-secondary: #6b7280;
            --border: #e5e7eb;
            --surface: #f9fafb;
            --hover: #f3f4f6;
            --selected: #eff6ff;
        }
        body.dark {
            --bg: #1f2937;
            --text: #f9fafb;
            --text-secondary: #9CA3AF;
            --border: #374151;
            --surface: #111827;
            --hover: #374151;
            --selected: #1e3a5f;
        }
        body { font-family: system-ui, -apple-system, sans-serif; padding: 16px; margin: 0; background: var(--bg); color: var(--text); }
        .table-container { background: var(--bg); border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); overflow: hidden; max-width: 600px; }
        .table-header { padding: 12px 16px; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; gap: 10px; flex-wrap: wrap; }
        h2 { margin: 0; font-size: 16px; }
        .filter-input { padding: 6px 10px; border: 1px solid var(--border); border-radius: 4px; font-size: 12px; background: var(--bg); color: var(--text); }
        table { width: 100%; border-collapse: collapse; }
        th, td { padding: 10px 12px; text-align: left; border-bottom: 1px solid var(--border); }
        th { background: var(--surface); font-weight: 600; font-size: 11px; text-transform: uppercase; color: var(--text-secondary); }
        th.sortable { cursor: pointer; user-select: none; }
        th.sortable:hover { background: var(--hover); }
        .checkbox-col { width: 36px; text-align: center; }
        tr:hover { background: var(--hover); }
        tr.selected { background: var(--selected); }
        .table-footer { padding: 10px 16px; border-top: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 8px; }
        #selectionInfo { font-size: 12px; color: var(--text-secondary); }
        .status { font-size: 11px; }
        .status.success { color: #10B981; }
        .status.warning { color: #F59E0B; }
        .status.error { color: #EF4444; }
        .status.loading { color: #3B82F6; }
        .btn { background: #3B82F6; color: white; border: none; padding: 7px 12px; border-radius: 5px; cursor: pointer; font-size: 12px; }
        .btn:hover { background: #2563EB; }
        .btn.secondary { background: #6B7280; font-size: 11px; padding: 5px 8px; }
        .btn.secondary:hover { background: #4B5563; }
        .btn-row { display: flex; gap: 6px; }
        #log { font-size: 10px; color: var(--text-secondary); padding: 8px 16px; }
    </style>
</head>
<body>
    <div class=""table-container"">
        <div class=""table-header"">
            <h2 id=""tableTitle"">Data Table</h2>
            <input type=""text"" class=""filter-input"" placeholder=""Filter..."" oninput=""filterTable(this.value)"">
        </div>
        <table>
            <thead id=""tableHead""></thead>
            <tbody id=""tableBody""></tbody>
        </table>
        <div class=""table-footer"">
            <span id=""selectionInfo""></span>
            <div class=""btn-row"">
                <button class=""btn"" onclick=""exportSelection()"">Export</button>
                <button class=""btn secondary"" onclick=""sendMessageDemo()"">sendMessage</button>
                <button class=""btn secondary"" onclick=""callServerToolDemo()"">callServerTool</button>
            </div>
        </div>
        <div id=""status"" class=""status""></div>
        <div id=""log""></div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Confirmation Dialog UI - for destructive or important actions
    /// Shows: Simple confirm/cancel pattern, variant styling (info/warning/danger)
    /// </summary>
    private string GetConfirmActionUI()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Confirm Action</title>
    <script type=""module"">
        import { 
            App,
            applyDocumentTheme,
            applyHostStyleVariables,
            applyHostFonts
        } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        // ========================================
        // APP INITIALIZATION WITH TOOL CAPABILITY
        // ========================================
        const app = new App(
            { name: 'ConfirmActionApp', version: '1.0.0' },
            { tools: { listChanged: false } }
        );
        
        let dialogConfig = {
            title: 'Confirm Action',
            message: 'Are you sure you want to proceed?',
            confirmLabel: 'Confirm',
            cancelLabel: 'Cancel',
            variant: 'warning',
            details: null
        };
        let userResponse = null;
        
        const log = (msg) => {
            console.log('[ConfirmAction]', msg);
            const el = document.getElementById('log');
            if (el) el.textContent = msg;
        };
        
        const updateStatus = (msg, type = 'info') => {
            const el = document.getElementById('status');
            if (el) {
                el.textContent = msg;
                el.className = 'status ' + type;
            }
        };
        
        // ========================================
        // ALL NOTIFICATION HANDLERS
        // ========================================
        
        // ontoolinput - Configure dialog from args BEFORE execution
        app.ontoolinput = (params) => {
            log('ontoolinput: configuring dialog...');
            if (params.arguments) {
                dialogConfig = { ...dialogConfig, ...params.arguments };
                renderDialog();
            }
        };
        
        // ontoolinputpartial - Streaming args
        app.ontoolinputpartial = (params) => {
            log('ontoolinputpartial: streaming...');
            if (params.arguments) {
                dialogConfig = { ...dialogConfig, ...params.arguments };
                renderDialog();
            }
        };
        
        // ontoolresult - Update dialog from result
        app.ontoolresult = (result) => {
            log('ontoolresult: updating dialog');
            const structured = result.structuredContent;
            if (structured) {
                dialogConfig = { ...dialogConfig, ...structured };
            } else {
                const text = result.content?.find(c => c.type === 'text')?.text;
                if (text) {
                    try { dialogConfig = { ...dialogConfig, ...JSON.parse(text) }; } catch(e) {}
                }
            }
            renderDialog();
        };
        
        // ontoolcancelled - Cancelled
        app.ontoolcancelled = (params) => {
            log('ontoolcancelled: ' + (params.reason || 'unknown'));
            updateStatus('Cancelled', 'warning');
        };
        
        // onhostcontextchanged - Theme/locale changed
        app.onhostcontextchanged = (ctx) => {
            log('onhostcontextchanged');
            if (ctx.theme) {
                applyDocumentTheme(ctx.theme);
                document.body.classList.remove('light', 'dark');
                document.body.classList.add(ctx.theme);
            }
            if (ctx.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
            if (ctx.styles?.fonts) applyHostFonts(ctx.styles.fonts);
        };
        
        // onteardown - Graceful shutdown
        app.onteardown = async (params) => {
            log('onteardown: cleanup...');
            return {};
        };
        
        // onerror - Error handler
        app.onerror = (error) => {
            log('onerror: ' + error.message);
            updateStatus('Error: ' + error.message, 'error');
        };
        
        // oncalltool - Handle tool calls from host
        app.oncalltool = async (params, extra) => {
            log('oncalltool: ' + params.name);
            
            switch (params.name) {
                case 'get_response':
                    return {
                        content: [{ type: 'text', text: userResponse ? (userResponse.confirmed ? 'Confirmed' : 'Cancelled') : 'No response yet' }],
                        structuredContent: { response: userResponse }
                    };
                    
                case 'reset_dialog':
                    userResponse = null;
                    document.getElementById('confirmBtn').disabled = false;
                    document.getElementById('cancelBtn').disabled = false;
                    document.getElementById('result').textContent = '';
                    document.getElementById('result').className = 'result';
                    return { content: [{ type: 'text', text: 'Dialog reset' }], structuredContent: { success: true } };
                    
                case 'set_variant':
                    if (params.arguments?.variant) {
                        dialogConfig.variant = params.arguments.variant;
                        renderDialog();
                    }
                    return { content: [{ type: 'text', text: 'Variant changed' }], structuredContent: { success: true, variant: dialogConfig.variant } };
                    
                default:
                    throw new Error('Unknown tool: ' + params.name);
            }
        };
        
        // onlisttools - Return available tools
        app.onlisttools = async () => {
            return {
                tools: [
                    { name: 'get_response', description: 'Get current user response', inputSchema: { type: 'object', properties: {} } },
                    { name: 'reset_dialog', description: 'Reset dialog to allow new response', inputSchema: { type: 'object', properties: {} } },
                    { name: 'set_variant', description: 'Change dialog variant', inputSchema: { type: 'object', properties: { variant: { type: 'string', enum: ['info', 'warning', 'danger'] } }, required: ['variant'] } }
                ]
            };
        };
        
        // ========================================
        // DIALOG RENDERING
        // ========================================
        
        function renderDialog() {
            document.getElementById('dialogTitle').textContent = dialogConfig.title;
            document.getElementById('dialogMessage').textContent = dialogConfig.message;
            document.getElementById('confirmBtn').textContent = dialogConfig.confirmLabel;
            document.getElementById('cancelBtn').textContent = dialogConfig.cancelLabel;
            
            // Show details if provided
            const detailsEl = document.getElementById('details');
            if (dialogConfig.details) {
                detailsEl.textContent = dialogConfig.details;
                detailsEl.style.display = 'block';
            } else {
                detailsEl.style.display = 'none';
            }
            
            // Set variant styling
            const dialog = document.getElementById('dialog');
            dialog.className = 'dialog ' + (dialogConfig.variant || 'warning');
            
            // Set icon based on variant
            const iconEl = document.getElementById('icon');
            switch (dialogConfig.variant) {
                case 'danger': iconEl.textContent = '⛔'; break;
                case 'info': iconEl.textContent = 'ℹ️'; break;
                case 'warning': default: iconEl.textContent = '⚠️'; break;
            }
            
            notifySizeChanged();
        }
        
        // ========================================
        // SDK METHODS
        // ========================================
        
        function notifySizeChanged() {
            try {
                const dialog = document.getElementById('dialog');
                if (dialog) app.sendSizeChanged({ width: dialog.offsetWidth, height: dialog.offsetHeight });
            } catch (e) {}
        }
        
        window.confirmAction = async function() {
            userResponse = { confirmed: true, action: dialogConfig.title, timestamp: new Date().toISOString() };
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: 'User confirmed: ' + dialogConfig.title }],
                    structuredContent: userResponse
                });
                document.getElementById('result').textContent = 'Confirmed';
                document.getElementById('result').className = 'result confirmed';
                disableButtons();
                log('Action confirmed');
            } catch (e) {
                log('Error: ' + e.message);
            }
        };
        
        window.cancelAction = async function() {
            userResponse = { confirmed: false, action: dialogConfig.title, timestamp: new Date().toISOString() };
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: 'User cancelled: ' + dialogConfig.title }],
                    structuredContent: userResponse
                });
                document.getElementById('result').textContent = 'Cancelled';
                document.getElementById('result').className = 'result cancelled';
                disableButtons();
                log('Action cancelled');
            } catch (e) {
                log('Error: ' + e.message);
            }
        };
        
        function disableButtons() {
            document.getElementById('confirmBtn').disabled = true;
            document.getElementById('cancelBtn').disabled = true;
        }
        
        window.sendMessageDemo = async function() {
            try {
                await app.sendMessage({
                    role: 'user',
                    content: [{ type: 'text', text: 'I ' + (userResponse?.confirmed ? 'confirmed' : 'cancelled') + ' the action: ' + dialogConfig.title }]
                });
                updateStatus('Message sent!', 'success');
            } catch (e) {
                updateStatus('sendMessage not available', 'warning');
            }
        };
        
        window.openDocsDemo = async function() {
            try {
                const result = await app.openLink({ url: 'https://modelcontextprotocol.io/docs/apps' });
                if (result.isError) updateStatus('Link blocked', 'warning');
                else updateStatus('Docs opened!', 'success');
            } catch (e) {
                updateStatus('openLink not available', 'warning');
            }
        };
        
        // ========================================
        // CONNECT AND INITIALIZE
        // ========================================
        
        await app.connect();
        
        const ctx = app.getHostContext();
        if (ctx?.theme) {
            applyDocumentTheme(ctx.theme);
            document.body.classList.add(ctx.theme);
        }
        if (ctx?.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
        
        renderDialog();
        log('Connected - awaiting user response...');
    </script>
    <style>
        :root {
            --bg: #f3f4f6;
            --surface: #ffffff;
            --text: #1f2937;
            --text-secondary: #6b7280;
            --border: #d1d5db;
            --footer-bg: #f9fafb;
        }
        body.dark {
            --bg: #111827;
            --surface: #1f2937;
            --text: #f9fafb;
            --text-secondary: #9CA3AF;
            --border: #374151;
            --footer-bg: #374151;
        }
        body { font-family: system-ui, -apple-system, sans-serif; padding: 16px; margin: 0; display: flex; flex-direction: column; align-items: center; min-height: calc(100vh - 32px); background: var(--bg); color: var(--text); }
        .dialog { background: var(--surface); border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); max-width: 380px; width: 100%; overflow: hidden; }
        .dialog-header { padding: 18px 18px 0 18px; text-align: center; }
        #icon { font-size: 42px; margin-bottom: 10px; display: block; }
        h2 { margin: 0 0 6px 0; font-size: 18px; }
        .dialog-body { padding: 0 18px 16px 18px; text-align: center; }
        #dialogMessage { color: var(--text-secondary); font-size: 13px; line-height: 1.5; margin: 0; }
        #details { font-size: 11px; color: var(--text-secondary); background: var(--bg); padding: 8px; border-radius: 6px; margin-top: 10px; text-align: left; font-family: monospace; white-space: pre-wrap; display: none; }
        .dialog-footer { padding: 14px 18px; background: var(--footer-bg); display: flex; gap: 10px; }
        .btn { flex: 1; padding: 10px; border: none; border-radius: 7px; cursor: pointer; font-size: 13px; font-weight: 500; transition: all 0.15s; }
        .btn:disabled { opacity: 0.5; cursor: not-allowed; }
        #cancelBtn { background: var(--surface); color: var(--text); border: 1px solid var(--border); }
        #cancelBtn:hover:not(:disabled) { background: var(--bg); }
        .dialog.warning #confirmBtn { background: #F59E0B; color: white; }
        .dialog.warning #confirmBtn:hover:not(:disabled) { background: #D97706; }
        .dialog.danger #confirmBtn { background: #EF4444; color: white; }
        .dialog.danger #confirmBtn:hover:not(:disabled) { background: #DC2626; }
        .dialog.info #confirmBtn { background: #3B82F6; color: white; }
        .dialog.info #confirmBtn:hover:not(:disabled) { background: #2563EB; }
        .result { text-align: center; padding: 10px; font-weight: 500; min-height: 16px; }
        .result.confirmed { color: #10B981; }
        .result.cancelled { color: var(--text-secondary); }
        .status { font-size: 11px; text-align: center; }
        .status.success { color: #10B981; }
        .status.warning { color: #F59E0B; }
        .status.error { color: #EF4444; }
        #log { font-size: 10px; color: var(--text-secondary); text-align: center; padding: 6px; }
        .sdk-section { margin-top: 12px; display: flex; gap: 6px; justify-content: center; }
        .btn.secondary { background: #6B7280; color: white; padding: 6px 10px; font-size: 11px; flex: none; }
        .btn.secondary:hover { background: #4B5563; }
    </style>
</head>
<body>
    <div id=""dialog"" class=""dialog warning"">
        <div class=""dialog-header"">
            <span id=""icon"">⚠️</span>
            <h2 id=""dialogTitle"">Confirm Action</h2>
        </div>
        <div class=""dialog-body"">
            <p id=""dialogMessage"">Are you sure you want to proceed?</p>
            <div id=""details""></div>
        </div>
        <div class=""dialog-footer"">
            <button id=""cancelBtn"" class=""btn"" onclick=""cancelAction()"">Cancel</button>
            <button id=""confirmBtn"" class=""btn"" onclick=""confirmAction()"">Confirm</button>
        </div>
        <div id=""result"" class=""result""></div>
        <div class=""sdk-section"">
            <button class=""btn secondary"" onclick=""sendMessageDemo()"">sendMessage</button>
            <button class=""btn secondary"" onclick=""openDocsDemo()"">openLink</button>
        </div>
        <div id=""status"" class=""status""></div>
        <div id=""log""></div>
    </div>
</body>
</html>";
    }

    // ========================================
    // TOOL/RESOURCE/PROMPT DEFINITIONS
    // ========================================

    /// <summary>
    /// Build the list of available tools - define your tools here
    /// Each tool needs: name, description, and inputSchema (JSON Schema)
    /// For MCP Apps, add _meta.ui.resourceUri to declare UI resources
    /// </summary>
    private JArray BuildToolsList()
    {
        return new JArray
        {
            // Example: Echo tool - replace with your actual tools
            new JObject
            {
                ["name"] = "echo",
                ["description"] = "Echoes back the provided message. Useful for testing connectivity.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["message"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The message to echo back"
                        }
                    },
                    ["required"] = new JArray { "message" }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = true,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                }
            },
            // Example: Get Data tool - replace with your actual tools
            new JObject
            {
                ["name"] = "get_data",
                ["description"] = "Retrieves data by ID from the connected data source.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The unique identifier of the data to retrieve"
                        }
                    },
                    ["required"] = new JArray { "id" }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = true,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                }
            },
            // ========================================
            // MCP APPS UI TOOLS
            // ========================================
            // These tools declare UI resources via _meta.ui.resourceUri
            // The host renders the UI in a sandboxed iframe
            new JObject
            {
                ["name"] = "color_picker",
                ["description"] = "Opens an interactive color picker. User can select a color visually and the selection is reported back.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["defaultColor"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Default color in hex format (e.g., #3B82F6)"
                        }
                    }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = false,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                },
                ["_meta"] = new JObject
                {
                    ["ui"] = new JObject
                    {
                        ["resourceUri"] = "ui://color-picker"
                    }
                }
            },
            new JObject
            {
                ["name"] = "data_visualizer",
                ["description"] = "Displays data as an interactive chart. Supports bar, line, pie, and doughnut charts. Users can interact with the visualization.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["chartType"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Type of chart: bar, line, pie, doughnut",
                            ["enum"] = new JArray { "bar", "line", "pie", "doughnut" }
                        },
                        ["title"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Title for the chart"
                        },
                        ["data"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Array of data points with label and value",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["label"] = new JObject { ["type"] = "string" },
                                    ["value"] = new JObject { ["type"] = "number" }
                                }
                            }
                        }
                    }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = true,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                },
                ["_meta"] = new JObject
                {
                    ["ui"] = new JObject
                    {
                        ["resourceUri"] = "ui://data-visualizer"
                    }
                }
            },
            new JObject
            {
                ["name"] = "form_input",
                ["description"] = "Displays a dynamic form to collect structured input from the user. Supports text, email, number, textarea, select, and checkbox fields with validation.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["title"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Title displayed at the top of the form"
                        },
                        ["submitLabel"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Label for the submit button"
                        },
                        ["fields"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Array of field definitions",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Field identifier" },
                                    ["label"] = new JObject { ["type"] = "string", ["description"] = "Display label" },
                                    ["type"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "text", "email", "number", "textarea", "select", "checkbox" } },
                                    ["required"] = new JObject { ["type"] = "boolean" },
                                    ["options"] = new JObject { ["type"] = "array", ["description"] = "Options for select fields" }
                                }
                            }
                        }
                    }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = false,
                    ["idempotentHint"] = false,
                    ["openWorldHint"] = false
                },
                ["_meta"] = new JObject
                {
                    ["ui"] = new JObject
                    {
                        ["resourceUri"] = "ui://form-input"
                    }
                }
            },
            new JObject
            {
                ["name"] = "data_table",
                ["description"] = "Displays data in an interactive table with sorting and row selection. Users can click rows to select them and click column headers to sort.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["title"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Title displayed above the table"
                        },
                        ["columns"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Column definitions with key, label, and sortable flag",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["key"] = new JObject { ["type"] = "string" },
                                    ["label"] = new JObject { ["type"] = "string" },
                                    ["sortable"] = new JObject { ["type"] = "boolean" }
                                }
                            }
                        },
                        ["rows"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Array of row objects matching column keys"
                        },
                        ["selectable"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether rows can be selected"
                        }
                    }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = true,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                },
                ["_meta"] = new JObject
                {
                    ["ui"] = new JObject
                    {
                        ["resourceUri"] = "ui://data-table"
                    }
                }
            },
            new JObject
            {
                ["name"] = "confirm_action",
                ["description"] = "Shows a confirmation dialog before executing a potentially destructive or important action. Returns the user's choice (confirmed or cancelled).",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["title"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Dialog title"
                        },
                        ["message"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Confirmation message to display"
                        },
                        ["confirmLabel"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Label for confirm button"
                        },
                        ["cancelLabel"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Label for cancel button"
                        },
                        ["variant"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Visual style: info, warning, or danger",
                            ["enum"] = new JArray { "info", "warning", "danger" }
                        }
                    },
                    ["required"] = new JArray { "message" }
                },
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = false,
                    ["idempotentHint"] = true,
                    ["openWorldHint"] = false
                },
                ["_meta"] = new JObject
                {
                    ["ui"] = new JObject
                    {
                        ["resourceUri"] = "ui://confirm-action"
                    }
                }
            }
            // TODO: Add your custom tools here
            // new JObject
            // {
            //     ["name"] = "your_tool_name",
            //     ["description"] = "Description of what this tool does",
            //     ["inputSchema"] = new JObject
            //     {
            //         ["type"] = "object",
            //         ["properties"] = new JObject { ... },
            //         ["required"] = new JArray { "param1" }
            //     }
            // }
        };
    }

    /// <summary>
    /// Build the list of available prompts - define reusable prompt templates here
    /// Each prompt needs: name, description, and optional arguments
    /// </summary>
    private JArray BuildPromptsList()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = "analyze_data",
                ["description"] = "Analyze data with customizable context and data type",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "dataType",
                        ["description"] = "Type of data to analyze (e.g., 'sales data', 'user metrics')",
                        ["required"] = false
                    },
                    new JObject
                    {
                        ["name"] = "context",
                        ["description"] = "Additional context for the analysis",
                        ["required"] = false
                    }
                }
            },
            new JObject
            {
                ["name"] = "summarize",
                ["description"] = "Generate a summary with configurable length and style",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "length",
                        ["description"] = "Summary length: 'short', 'medium', or 'long'",
                        ["required"] = false
                    },
                    new JObject
                    {
                        ["name"] = "style",
                        ["description"] = "Writing style: 'professional', 'casual', 'technical'",
                        ["required"] = false
                    }
                }
            },
            new JObject
            {
                ["name"] = "code_review",
                ["description"] = "Review code with language-specific and focus-area guidance",
                ["arguments"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "language",
                        ["description"] = "Programming language (e.g., 'C#', 'Python', 'JavaScript')",
                        ["required"] = false
                    },
                    new JObject
                    {
                        ["name"] = "focus",
                        ["description"] = "Review focus: 'security', 'performance', 'readability', 'general'",
                        ["required"] = false
                    }
                }
            }
            // TODO: Add your custom prompts here
        };
    }

    /// <summary>
    /// Build the list of resource templates - dynamic URI patterns
    /// Templates use {parameter} syntax for URL parameters
    /// </summary>
    private JArray BuildResourceTemplatesList()
    {
        return new JArray
        {
            new JObject
            {
                ["uriTemplate"] = "data://{dataType}/{id}",
                ["name"] = "Data Resource",
                ["description"] = "Access data resources by type and ID",
                ["mimeType"] = "application/json"
            },
            new JObject
            {
                ["uriTemplate"] = "config://{section}",
                ["name"] = "Configuration",
                ["description"] = "Access configuration sections",
                ["mimeType"] = "application/json"
            },
            new JObject
            {
                ["uriTemplate"] = "log://{level}/{count}",
                ["name"] = "Log Entries",
                ["description"] = "Retrieve recent log entries by level",
                ["mimeType"] = "application/json"
            }
            // TODO: Add your custom resource templates here
        };
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Get a required argument from the tool arguments, throws if missing
    /// </summary>
    private string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' is required");
        }
        return value;
    }

    /// <summary>
    /// Get an optional argument from the tool arguments
    /// </summary>
    private string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    /// <summary>
    /// Get a connection parameter by name
    /// Note: Power Platform custom connectors access connection parameters via
    /// the request headers or query parameters set up in apiProperties.json
    /// This is a placeholder - implement based on your connector's auth setup
    /// </summary>
    private string GetConnectionParameter(string name)
    {
        // Connection parameters are typically passed as headers in Power Platform
        // Check if there's a custom header with this name
        if (this.Context.Request.Headers.TryGetValues($"x-{name}", out var values))
        {
            return values.FirstOrDefault();
        }
        // Or check query parameters
        var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        return query[name];
    }

    /// <summary>
    /// Get an integer argument from the tool arguments
    /// </summary>
    private int GetIntArgument(JObject args, string name, int defaultValue = 0)
    {
        var token = args?[name];
        if (token == null) return defaultValue;
        if (token.Type == JTokenType.Integer) return token.Value<int>();
        if (int.TryParse(token.ToString(), out var result)) return result;
        return defaultValue;
    }

    /// <summary>
    /// Get a required integer argument, throws if missing or invalid
    /// </summary>
    private int RequireIntArgument(JObject args, string name)
    {
        var token = args?[name];
        if (token == null) throw new ArgumentException($"'{name}' is required");
        if (token.Type == JTokenType.Integer) return token.Value<int>();
        if (int.TryParse(token.ToString(), out var result)) return result;
        throw new ArgumentException($"'{name}' must be an integer");
    }

    /// <summary>
    /// Get a boolean argument from the tool arguments
    /// </summary>
    private bool GetBoolArgument(JObject args, string name, bool defaultValue = false)
    {
        var token = args?[name];
        if (token == null) return defaultValue;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>();
        var str = token.ToString().ToLowerInvariant();
        return str == "true" || str == "1" || str == "yes";
    }

    /// <summary>
    /// Get a DateTime argument from the tool arguments
    /// </summary>
    private DateTime? GetDateTimeArgument(JObject args, string name, DateTime? defaultValue = null)
    {
        var token = args?[name];
        if (token == null) return defaultValue;
        if (token.Type == JTokenType.Date) return token.Value<DateTime>();
        if (DateTime.TryParse(token.ToString(), out var result)) return result;
        return defaultValue;
    }

    /// <summary>
    /// Get an array argument from the tool arguments
    /// </summary>
    private JArray GetArrayArgument(JObject args, string name, JArray defaultValue = null)
    {
        var token = args?[name];
        if (token == null) return defaultValue ?? new JArray();
        if (token is JArray arr) return arr;
        // Try to parse as JSON array if string
        if (token.Type == JTokenType.String)
        {
            try { return JArray.Parse(token.ToString()); }
            catch { return defaultValue ?? new JArray(); }
        }
        return defaultValue ?? new JArray();
    }

    /// <summary>
    /// Get an object argument from the tool arguments
    /// </summary>
    private JObject GetObjectArgument(JObject args, string name, JObject defaultValue = null)
    {
        var token = args?[name];
        if (token == null) return defaultValue ?? new JObject();
        if (token is JObject obj) return obj;
        if (token.Type == JTokenType.String)
        {
            try { return JObject.Parse(token.ToString()); }
            catch { return defaultValue ?? new JObject(); }
        }
        return defaultValue ?? new JObject();
    }

    /// <summary>
    /// Build a query string from key-value pairs
    /// Usage: BuildQueryString(("search", term), ("limit", "10"))
    /// </summary>
    private string BuildQueryString(params (string key, string value)[] parameters)
    {
        var pairs = parameters
            .Where(p => !string.IsNullOrEmpty(p.value))
            .Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value)}");
        var qs = string.Join("&", pairs);
        return string.IsNullOrEmpty(qs) ? "" : "?" + qs;
    }

    // ========================================
    // JSON-RPC RESPONSE HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS TELEMETRY
    // ========================================

    /// <summary>
    /// Send custom event to Application Insights
    /// </summary>
    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                // Telemetry disabled - connection string not configured
                return;
            }

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = ServerName,
                ["ServerVersion"] = ServerVersion
            };

            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = Newtonsoft.Json.Linq.JObject.Parse(propsJson);
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Suppress telemetry errors - don't fail the main request
            this.Context.Logger.LogWarning($"Telemetry error: {ex.Message}");
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString)) return null;

            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("InstrumentationKey=".Length);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionString))
                return "https://dc.services.visualstudio.com/";

            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("IngestionEndpoint=".Length);
                }
            }
            return "https://dc.services.visualstudio.com/";
        }
        catch
        {
            return "https://dc.services.visualstudio.com/";
        }
    }
}

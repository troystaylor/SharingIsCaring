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
                var singleBody = reqObj.ToString(Formatting.None);
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
            Content = new StringContent(responses.ToString(Formatting.None), Encoding.UTF8, "application/json")
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
                        ["text"] = toolResult.ToString(Formatting.Indented)
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
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Resource URI is required");
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
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
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
    /// </summary>
    private string GetColorPickerUI()
    {
        // This example demonstrates all MCP Apps SDK methods with compatibility notes
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Color Picker</title>
    <script type=""module"">
        import { App } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        // ========================================
        // APP INITIALIZATION
        // ========================================
        // Create App with implementation info and capabilities
        // capabilities: { tools?: { listChanged?: boolean } } - declare if app provides tools
        const app = new App(
            { name: 'ColorPickerApp', version: '1.0.0' },
            {} // capabilities - empty means no app-side tools
        );
        
        let currentColor = '#3B82F6';
        const log = (msg) => { console.log('[ColorPicker]', msg); logToUI(msg); };
        const logToUI = (msg) => { 
            const el = document.getElementById('log');
            if (el) el.textContent = msg;
        };
        
        // ========================================
        // NOTIFICATION HANDLERS (set BEFORE connect)
        // ========================================
        
        // ✅ COPILOT STUDIO: ontoolinput - Complete tool arguments received
        app.ontoolinput = (params) => {
            log('ontoolinput: ' + JSON.stringify(params.arguments));
            if (params.arguments?.defaultColor) {
                currentColor = params.arguments.defaultColor;
                document.getElementById('colorInput').value = currentColor;
                updatePreview();
            }
        };
        
        // ⏳ FUTURE: ontoolinputpartial - Streaming partial arguments (for progressive rendering)
        app.ontoolinputpartial = (params) => {
            log('ontoolinputpartial: streaming...');
            // Render loading state or partial UI while args stream in
        };
        
        // ✅ COPILOT STUDIO: ontoolresult - Tool execution results from server
        app.ontoolresult = (result) => {
            log('ontoolresult received');
            const text = result.content?.find(c => c.type === 'text')?.text;
            // Also check structuredContent for typed data
            const structured = result.structuredContent;
            if (text) {
                try {
                    const data = JSON.parse(text);
                    if (data.defaultColor) {
                        currentColor = data.defaultColor;
                        document.getElementById('colorInput').value = currentColor;
                        updatePreview();
                    }
                } catch (e) { /* not JSON, that's ok */ }
            }
        };
        
        // ⏳ FUTURE: ontoolcancelled - Tool was cancelled by user or host
        app.ontoolcancelled = (params) => {
            log('ontoolcancelled: ' + params.reason);
        };
        
        // ⏳ FUTURE: onhostcontextchanged - Theme/locale/displayMode changed
        app.onhostcontextchanged = (ctx) => {
            log('onhostcontextchanged: ' + (ctx.theme || 'no theme'));
            // Could apply theme: if (ctx.theme === 'dark') document.body.classList.add('dark');
        };
        
        // ⏳ FUTURE: onteardown - Graceful shutdown, save state before unmount
        app.onteardown = async (params) => {
            log('onteardown: cleaning up...');
            // Save state, close connections, etc.
            return {}; // Return empty object when ready
        };
        
        // ✅ COPILOT STUDIO: onerror - Error handler
        app.onerror = (error) => {
            log('onerror: ' + error.message);
            console.error('[ColorPicker] Error:', error);
        };
        
        // ⏳ FUTURE: oncalltool - Handle tool calls from host (if app declares tools)
        // app.oncalltool = async (params, extra) => {
        //     if (params.name === 'get_selected_color') {
        //         return { content: [{ type: 'text', text: currentColor }] };
        //     }
        //     throw new Error('Unknown tool: ' + params.name);
        // };
        
        // ⏳ FUTURE: onlisttools - Return available tools (if app declares tools)
        // app.onlisttools = async () => {
        //     return { tools: [{ name: 'get_selected_color', description: '...', inputSchema: {...} }] };
        // };
        
        // ========================================
        // UI FUNCTIONS
        // ========================================
        
        window.updatePreview = function() {
            currentColor = document.getElementById('colorInput').value;
            document.getElementById('preview').style.backgroundColor = currentColor;
            document.getElementById('hexValue').textContent = currentColor;
        };
        
        // ========================================
        // APP METHODS
        // ========================================
        
        // ✅ COPILOT STUDIO: updateModelContext - Update context sent to model
        window.selectColor = async function() {
            try {
                await app.updateModelContext({
                    // Option 1: content array (text, image, etc.)
                    content: [{ type: 'text', text: `User selected color: ${currentColor}` }],
                    // Option 2: structuredContent for typed data (also works)
                    // structuredContent: { selectedColor: currentColor }
                });
                document.getElementById('status').textContent = 'Color selected!';
                log('updateModelContext: sent ' + currentColor);
            } catch (e) {
                log('updateModelContext failed: ' + e.message);
            }
        };
        
        // ⏳ FUTURE: sendMessage - Send message to chat (triggers model response)
        window.sendMessageDemo = async function() {
            try {
                await app.sendMessage({
                    role: 'user', // or 'assistant'
                    content: [{ type: 'text', text: `Apply color ${currentColor} to my project` }]
                });
                log('sendMessage: sent');
            } catch (e) {
                log('sendMessage not supported yet: ' + e.message);
            }
        };
        
        // ⏳ FUTURE: callServerTool - Call a tool on the MCP server
        window.callServerToolDemo = async function() {
            try {
                const result = await app.callServerTool({
                    name: 'echo',
                    arguments: { message: 'Hello from UI!' }
                });
                log('callServerTool result: ' + JSON.stringify(result));
            } catch (e) {
                log('callServerTool not supported yet: ' + e.message);
            }
        };
        
        // ⏳ FUTURE: requestDisplayMode - Change display mode
        window.requestFullscreen = async function() {
            try {
                const ctx = app.getHostContext();
                if (ctx?.availableDisplayModes?.includes('fullscreen')) {
                    const result = await app.requestDisplayMode({ mode: 'fullscreen' });
                    log('displayMode changed to: ' + result.mode);
                } else {
                    log('fullscreen not available');
                }
            } catch (e) {
                log('requestDisplayMode not supported yet: ' + e.message);
            }
        };
        
        // ⏳ FUTURE: openLink - Open external URL
        window.openLinkDemo = async function() {
            try {
                const result = await app.openLink({ url: 'https://modelcontextprotocol.io' });
                if (result.isError) log('openLink blocked by host');
                else log('openLink: opened');
            } catch (e) {
                log('openLink not supported yet: ' + e.message);
            }
        };
        
        // ⏳ FUTURE: sendLog - Send log to host
        window.sendLogDemo = async function() {
            try {
                app.sendLog({ level: 'info', data: 'Test log from ColorPicker', logger: 'ColorPickerApp' });
                log('sendLog: sent');
            } catch (e) {
                log('sendLog not supported yet: ' + e.message);
            }
        };
        
        // ========================================
        // CONNECT TO HOST
        // ========================================
        await app.connect();
        
        // After connect, we can access host context
        const ctx = app.getHostContext();  // ⏳ PARTIAL: May not have all fields in Copilot Studio
        const caps = app.getHostCapabilities(); // ⏳ PARTIAL: Check what's supported
        const hostInfo = app.getHostVersion(); // ⏳ PARTIAL: Host implementation info
        
        log('Connected! Host: ' + (hostInfo?.name || 'unknown'));
        if (ctx?.theme) log('Theme: ' + ctx.theme);
        if (ctx?.toolInfo) log('Tool: ' + ctx.toolInfo.tool.name);
    </script>
    <style>
        body { font-family: system-ui, sans-serif; padding: 20px; max-width: 320px; margin: 0 auto; }
        .picker-container { display: flex; flex-direction: column; gap: 12px; }
        #preview { width: 100%; height: 80px; border-radius: 8px; border: 1px solid #e5e7eb; }
        input[type=""color""] { width: 100%; height: 44px; cursor: pointer; border: none; }
        #hexValue { font-family: monospace; font-size: 16px; text-align: center; }
        #status { font-size: 12px; color: #10B981; text-align: center; min-height: 16px; }
        #log { font-size: 11px; color: #6B7280; text-align: center; min-height: 16px; word-break: break-all; }
        .btn { background: #3B82F6; color: white; border: none; padding: 10px; border-radius: 6px; cursor: pointer; font-size: 14px; width: 100%; }
        .btn:hover { background: #2563EB; }
        .btn.secondary { background: #6B7280; font-size: 12px; padding: 8px; }
        .btn.secondary:hover { background: #4B5563; }
        .btn-row { display: flex; gap: 6px; }
        .btn-row .btn { flex: 1; }
        h4 { margin: 16px 0 8px 0; font-size: 12px; color: #374151; border-top: 1px solid #e5e7eb; padding-top: 12px; }
    </style>
</head>
<body>
    <div class=""picker-container"">
        <div id=""preview"" style=""background-color: #3B82F6;""></div>
        <input type=""color"" id=""colorInput"" value=""#3B82F6"" onchange=""updatePreview()"">
        <div id=""hexValue"">#3B82F6</div>
        <button class=""btn"" onclick=""selectColor()"">✅ Select Color (updateModelContext)</button>
        <div id=""status""></div>
        
        <h4>⏳ Future Methods (Not Yet in Copilot Studio)</h4>
        <div class=""btn-row"">
            <button class=""btn secondary"" onclick=""sendMessageDemo()"">sendMessage</button>
            <button class=""btn secondary"" onclick=""callServerToolDemo()"">callServerTool</button>
        </div>
        <div class=""btn-row"">
            <button class=""btn secondary"" onclick=""requestFullscreen()"">fullscreen</button>
            <button class=""btn secondary"" onclick=""openLinkDemo()"">openLink</button>
            <button class=""btn secondary"" onclick=""sendLogDemo()"">sendLog</button>
        </div>
        <div id=""log""></div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Data Visualizer UI - demonstrates helper functions and chart rendering
    /// Shows: ontoolinput (args before result), sendSizeChanged, structuredContent
    /// </summary>
    private string GetDataVisualizerUI()
    {
        // This example focuses on practical chart visualization patterns
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Data Visualizer</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>
    <script type=""module"">
        // ========================================
        // MCP APPS SDK HELPER FUNCTIONS DEMO
        // ========================================
        // In addition to the App class, the SDK provides helper functions
        // for common UI operations (theme, fonts, styles)
        
        import { 
            App,
            // ⏳ FUTURE: Helper functions (not yet in Copilot Studio)
            // applyDocumentTheme,    // Apply 'light' or 'dark' theme
            // applyHostStyleVariables, // Apply CSS custom properties
            // applyHostFonts          // Apply font CSS to document
        } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        const app = new App(
            { name: 'DataVisualizerApp', version: '1.0.0' },
            {} // capabilities
        );
        
        let chart = null;
        let chartConfig = { chartType: 'bar', title: 'Data Visualization', data: [] };
        const log = (msg) => document.getElementById('log').textContent = msg;
        
        // ========================================
        // HANDLERS - ontoolinput vs ontoolresult
        // ========================================
        
        // ✅ COPILOT STUDIO: ontoolinput - Called with args BEFORE tool executes
        // Use this for immediate UI setup from input parameters
        app.ontoolinput = (params) => {
            log('ontoolinput: preparing chart...');
            // Can render loading state or preliminary UI based on input
            if (params.arguments?.chartType) {
                chartConfig.chartType = params.arguments.chartType;
            }
            if (params.arguments?.title) {
                chartConfig.title = params.arguments.title;
                document.getElementById('title').textContent = chartConfig.title;
            }
            // Show loading until ontoolresult fires
            document.getElementById('status').textContent = 'Loading data...';
        };
        
        // ✅ COPILOT STUDIO: ontoolresult - Called with result AFTER tool executes
        // Use this for data that comes from the server
        app.ontoolresult = (result) => {
            log('ontoolresult: rendering chart');
            document.getElementById('status').textContent = '';
            
            // MCP Apps supports both 'content' array and 'structuredContent' object
            // structuredContent is better for typed data:
            const structured = result.structuredContent;
            if (structured?.data) {
                chartConfig = { ...chartConfig, ...structured };
                renderChart();
                return;
            }
            
            // Fallback: parse from text content
            const text = result.content?.find(c => c.type === 'text')?.text;
            if (text) {
                try {
                    const data = JSON.parse(text);
                    chartConfig = { ...chartConfig, ...data };
                    renderChart();
                } catch (e) { log('Error parsing data'); }
            }
        };
        
        // ========================================
        // CHART RENDERING
        // ========================================
        
        function renderChart() {
            const ctx = document.getElementById('chart').getContext('2d');
            if (chart) chart.destroy();
            
            const labels = chartConfig.data?.map(d => d.label) || ['A', 'B', 'C'];
            const values = chartConfig.data?.map(d => d.value) || [10, 20, 30];
            
            chart = new Chart(ctx, {
                type: chartConfig.chartType || 'bar',
                data: {
                    labels: labels,
                    datasets: [{
                        label: chartConfig.title || 'Data',
                        data: values,
                        backgroundColor: ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#06B6D4', '#EC4899'],
                        borderWidth: 1
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: true,
                    plugins: { legend: { position: 'top' } },
                    onClick: handleChartClick
                }
            });
            document.getElementById('title').textContent = chartConfig.title || 'Data Visualization';
            
            // ⏳ FUTURE: Notify host of size change after render
            notifySizeChange();
        }
        
        // Handle clicks on chart segments
        function handleChartClick(evt, elements) {
            if (elements.length > 0) {
                const idx = elements[0].index;
                const label = chartConfig.data?.[idx]?.label || 'Unknown';
                const value = chartConfig.data?.[idx]?.value || 0;
                
                // ✅ COPILOT STUDIO: Update context with selection
                app.updateModelContext({
                    content: [{ type: 'text', text: `User clicked: ${label} = ${value}` }],
                    // ⏳ FUTURE: structuredContent for typed data
                    // structuredContent: { selection: { label, value, index: idx } }
                });
                log(`Selected: ${label} = ${value}`);
            }
        }
        
        // ⏳ FUTURE: sendSizeChanged - Tells host optimal iframe size
        function notifySizeChange() {
            try {
                const container = document.querySelector('.chart-container');
                // app.sendSizeChanged would notify host of preferred height
                // app.sendSizeChanged({ width: container.offsetWidth, height: container.offsetHeight + 40 });
            } catch (e) { /* not supported yet */ }
        }
        
        // ========================================
        // EXPORT FUNCTION
        // ========================================
        
        window.exportSummary = async function() {
            const summary = {
                title: chartConfig.title,
                chartType: chartConfig.chartType,
                dataPoints: chartConfig.data?.length || 0,
                total: chartConfig.data?.reduce((sum, d) => sum + (d.value || 0), 0) || 0
            };
            
            await app.updateModelContext({
                content: [{ 
                    type: 'text', 
                    text: `Chart Summary:\n- Title: ${summary.title}\n- Type: ${summary.chartType}\n- Points: ${summary.dataPoints}\n- Total: ${summary.total}`
                }]
            });
            log('Summary sent!');
        };
        
        // ========================================
        // CONNECT AND INITIALIZE
        // ========================================
        
        await app.connect();
        
        // After connect, check host capabilities
        // const caps = app.getHostCapabilities();
        // if (caps?.styles?.variables) {
        //     applyHostStyleVariables(caps.styles.variables);
        // }
        
        log('Connected - waiting for data...');
    </script>
    <style>
        body { font-family: system-ui, -apple-system, sans-serif; padding: 16px; margin: 0; }
        .chart-container { background: white; padding: 16px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); max-width: 500px; }
        h2 { margin: 0 0 12px 0; color: #1f2937; font-size: 18px; }
        #chart { max-height: 300px; }
        #status { font-size: 12px; color: #6B7280; min-height: 16px; margin-top: 8px; }
        #log { font-size: 11px; color: #9CA3AF; margin-top: 4px; }
        .btn { background: #3B82F6; color: white; border: none; padding: 10px 16px; border-radius: 6px; cursor: pointer; font-size: 14px; margin-top: 12px; }
        .btn:hover { background: #2563EB; }
    </style>
</head>
<body>
    <div class=""chart-container"">
        <h2 id=""title"">Data Visualization</h2>
        <canvas id=""chart""></canvas>
        <div id=""status""></div>
        <button class=""btn"" onclick=""exportSummary()"">✅ Export Summary</button>
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
        import { App } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        const app = new App({ name: 'FormInputApp', version: '1.0.0' }, {});
        
        let formConfig = { title: 'Input Form', submitLabel: 'Submit', fields: [] };
        const log = (msg) => document.getElementById('log').textContent = msg;
        
        // ✅ COPILOT STUDIO: ontoolinput - Setup form from input args
        app.ontoolinput = (params) => {
            log('ontoolinput: configuring form...');
            if (params.arguments?.title) formConfig.title = params.arguments.title;
            if (params.arguments?.submitLabel) formConfig.submitLabel = params.arguments.submitLabel;
            document.getElementById('formTitle').textContent = formConfig.title;
            document.getElementById('submitBtn').textContent = formConfig.submitLabel;
        };
        
        // ✅ COPILOT STUDIO: ontoolresult - Render form fields from result
        app.ontoolresult = (result) => {
            log('ontoolresult: building form');
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
        
        app.onerror = (error) => log('Error: ' + error.message);
        
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
                
                fieldDiv.appendChild(input);
                container.appendChild(fieldDiv);
            });
        }
        
        window.submitForm = async function() {
            const form = document.getElementById('dynamicForm');
            if (!form.checkValidity()) {
                form.reportValidity();
                return;
            }
            
            const formData = {};
            (formConfig.fields || []).forEach(field => {
                const el = document.getElementById(field.name);
                if (el) formData[field.name] = el.value;
            });
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: 'Form submitted: ' + JSON.stringify(formData, null, 2) }],
                    structuredContent: { formData, submitted: true, timestamp: new Date().toISOString() }
                });
                document.getElementById('status').textContent = '✅ Form submitted!';
                document.getElementById('status').className = 'status success';
                log('updateModelContext: form data sent');
            } catch (e) {
                document.getElementById('status').textContent = '❌ Submit failed';
                document.getElementById('status').className = 'status error';
                log('Error: ' + e.message);
            }
        };
        
        window.resetForm = function() {
            document.getElementById('dynamicForm').reset();
            document.getElementById('status').textContent = '';
            log('Form reset');
        };
        
        await app.connect();
        log('Connected - waiting for form config...');
    </script>
    <style>
        body { font-family: system-ui, sans-serif; padding: 20px; margin: 0; max-width: 400px; }
        .form-container { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
        h2 { margin: 0 0 16px 0; color: #1f2937; font-size: 18px; }
        .field { margin-bottom: 16px; }
        label { display: block; margin-bottom: 4px; font-size: 14px; color: #374151; font-weight: 500; }
        input, textarea, select { width: 100%; padding: 10px; border: 1px solid #d1d5db; border-radius: 6px; font-size: 14px; box-sizing: border-box; }
        input:focus, textarea:focus, select:focus { outline: none; border-color: #3B82F6; box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }
        .btn-row { display: flex; gap: 8px; margin-top: 16px; }
        .btn { flex: 1; padding: 12px; border: none; border-radius: 6px; cursor: pointer; font-size: 14px; font-weight: 500; }
        .btn.primary { background: #3B82F6; color: white; }
        .btn.primary:hover { background: #2563EB; }
        .btn.secondary { background: #f3f4f6; color: #374151; }
        .btn.secondary:hover { background: #e5e7eb; }
        .status { font-size: 12px; text-align: center; margin-top: 12px; min-height: 16px; }
        .status.success { color: #10B981; }
        .status.error { color: #EF4444; }
        #log { font-size: 11px; color: #9CA3AF; text-align: center; margin-top: 8px; }
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
        import { App } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        const app = new App({ name: 'DataTableApp', version: '1.0.0' }, {});
        
        let tableConfig = { title: 'Data Table', columns: [], rows: [], selectable: true };
        let selectedRows = new Set();
        let sortColumn = null;
        let sortDirection = 'asc';
        
        const log = (msg) => document.getElementById('log').textContent = msg;
        
        // ✅ COPILOT STUDIO: ontoolinput
        app.ontoolinput = (params) => {
            log('ontoolinput: preparing table...');
            if (params.arguments?.title) {
                tableConfig.title = params.arguments.title;
                document.getElementById('tableTitle').textContent = tableConfig.title;
            }
        };
        
        // ✅ COPILOT STUDIO: ontoolresult
        app.ontoolresult = (result) => {
            log('ontoolresult: rendering table');
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
        
        app.onerror = (error) => log('Error: ' + error.message);
        
        function renderTable() {
            document.getElementById('tableTitle').textContent = tableConfig.title;
            
            // Render header
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
            
            // Sort rows if needed
            let rows = [...tableConfig.rows];
            if (sortColumn) {
                rows.sort((a, b) => {
                    const aVal = a[sortColumn] || '';
                    const bVal = b[sortColumn] || '';
                    const cmp = aVal.toString().localeCompare(bVal.toString());
                    return sortDirection === 'asc' ? cmp : -cmp;
                });
            }
            
            // Render body
            const tbody = document.getElementById('tableBody');
            tbody.innerHTML = '';
            
            rows.forEach((row, idx) => {
                const tr = document.createElement('tr');
                tr.className = selectedRows.has(idx) ? 'selected' : '';
                
                if (tableConfig.selectable) {
                    const td = document.createElement('td');
                    td.className = 'checkbox-col';
                    const checkbox = document.createElement('input');
                    checkbox.type = 'checkbox';
                    checkbox.checked = selectedRows.has(idx);
                    checkbox.onchange = () => toggleRow(idx);
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
            if (selectedRows.has(idx)) {
                selectedRows.delete(idx);
            } else {
                selectedRows.add(idx);
            }
            renderTable();
        }
        
        function toggleAllRows(checked) {
            if (checked) {
                tableConfig.rows.forEach((_, idx) => selectedRows.add(idx));
            } else {
                selectedRows.clear();
            }
            renderTable();
        }
        
        function updateSelectionInfo() {
            const info = document.getElementById('selectionInfo');
            if (selectedRows.size > 0) {
                info.textContent = `${selectedRows.size} row(s) selected`;
            } else {
                info.textContent = '';
            }
        }
        
        window.exportSelection = async function() {
            const selected = [...selectedRows].map(idx => tableConfig.rows[idx]);
            if (selected.length === 0) {
                log('No rows selected');
                return;
            }
            
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: `Selected ${selected.length} row(s):\n${JSON.stringify(selected, null, 2)}` }],
                    structuredContent: { selectedRows: selected, count: selected.length }
                });
                log('Selection exported: ' + selected.length + ' rows');
            } catch (e) {
                log('Error: ' + e.message);
            }
        };
        
        await app.connect();
        log('Connected - waiting for table data...');
    </script>
    <style>
        body { font-family: system-ui, sans-serif; padding: 16px; margin: 0; }
        .table-container { background: white; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); overflow: hidden; max-width: 600px; }
        .table-header { padding: 16px; border-bottom: 1px solid #e5e7eb; display: flex; justify-content: space-between; align-items: center; }
        h2 { margin: 0; color: #1f2937; font-size: 18px; }
        table { width: 100%; border-collapse: collapse; }
        th, td { padding: 12px; text-align: left; border-bottom: 1px solid #e5e7eb; }
        th { background: #f9fafb; font-weight: 600; font-size: 12px; text-transform: uppercase; color: #6b7280; }
        th.sortable { cursor: pointer; user-select: none; }
        th.sortable:hover { background: #f3f4f6; }
        .checkbox-col { width: 40px; text-align: center; }
        tr:hover { background: #f9fafb; }
        tr.selected { background: #eff6ff; }
        .table-footer { padding: 12px 16px; border-top: 1px solid #e5e7eb; display: flex; justify-content: space-between; align-items: center; }
        #selectionInfo { font-size: 13px; color: #6b7280; }
        .btn { background: #3B82F6; color: white; border: none; padding: 8px 16px; border-radius: 6px; cursor: pointer; font-size: 13px; }
        .btn:hover { background: #2563EB; }
        .btn:disabled { background: #9CA3AF; cursor: not-allowed; }
        #log { font-size: 11px; color: #9CA3AF; padding: 8px 16px; }
    </style>
</head>
<body>
    <div class=""table-container"">
        <div class=""table-header"">
            <h2 id=""tableTitle"">Data Table</h2>
        </div>
        <table>
            <thead id=""tableHead""></thead>
            <tbody id=""tableBody""></tbody>
        </table>
        <div class=""table-footer"">
            <span id=""selectionInfo""></span>
            <button class=""btn"" onclick=""exportSelection()"">Export Selection</button>
        </div>
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
        import { App } from 'https://esm.sh/@modelcontextprotocol/ext-apps@1.0.1';
        
        const app = new App({ name: 'ConfirmActionApp', version: '1.0.0' }, {});
        
        let dialogConfig = {
            title: 'Confirm Action',
            message: 'Are you sure you want to proceed?',
            confirmLabel: 'Confirm',
            cancelLabel: 'Cancel',
            variant: 'warning'
        };
        
        const log = (msg) => document.getElementById('log').textContent = msg;
        
        // ✅ COPILOT STUDIO: ontoolinput
        app.ontoolinput = (params) => {
            log('ontoolinput: configuring dialog...');
            if (params.arguments) {
                dialogConfig = { ...dialogConfig, ...params.arguments };
                renderDialog();
            }
        };
        
        // ✅ COPILOT STUDIO: ontoolresult
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
        
        app.onerror = (error) => log('Error: ' + error.message);
        
        function renderDialog() {
            document.getElementById('dialogTitle').textContent = dialogConfig.title;
            document.getElementById('dialogMessage').textContent = dialogConfig.message;
            document.getElementById('confirmBtn').textContent = dialogConfig.confirmLabel;
            document.getElementById('cancelBtn').textContent = dialogConfig.cancelLabel;
            
            // Set variant styling
            const dialog = document.getElementById('dialog');
            dialog.className = 'dialog ' + (dialogConfig.variant || 'warning');
            
            // Set icon based on variant
            const iconEl = document.getElementById('icon');
            switch (dialogConfig.variant) {
                case 'danger':
                    iconEl.textContent = '⛔';
                    break;
                case 'info':
                    iconEl.textContent = 'ℹ️';
                    break;
                case 'warning':
                default:
                    iconEl.textContent = '⚠️';
                    break;
            }
        }
        
        window.confirm = async function() {
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: `User confirmed: ${dialogConfig.title}` }],
                    structuredContent: { confirmed: true, action: dialogConfig.title, timestamp: new Date().toISOString() }
                });
                document.getElementById('result').textContent = '✅ Confirmed';
                document.getElementById('result').className = 'result confirmed';
                disableButtons();
                log('Action confirmed');
            } catch (e) {
                log('Error: ' + e.message);
            }
        };
        
        window.cancel = async function() {
            try {
                await app.updateModelContext({
                    content: [{ type: 'text', text: `User cancelled: ${dialogConfig.title}` }],
                    structuredContent: { confirmed: false, action: dialogConfig.title, timestamp: new Date().toISOString() }
                });
                document.getElementById('result').textContent = '❌ Cancelled';
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
        
        await app.connect();
        renderDialog();
        log('Connected - awaiting user response...');
    </script>
    <style>
        body { font-family: system-ui, sans-serif; padding: 20px; margin: 0; display: flex; justify-content: center; align-items: center; min-height: calc(100vh - 40px); background: #f3f4f6; }
        .dialog { background: white; border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); max-width: 400px; width: 100%; overflow: hidden; }
        .dialog-header { padding: 20px 20px 0 20px; text-align: center; }
        #icon { font-size: 48px; margin-bottom: 12px; display: block; }
        h2 { margin: 0 0 8px 0; color: #1f2937; font-size: 20px; }
        .dialog-body { padding: 0 20px 20px 20px; text-align: center; }
        #dialogMessage { color: #6b7280; font-size: 14px; line-height: 1.5; margin: 0; }
        .dialog-footer { padding: 16px 20px; background: #f9fafb; display: flex; gap: 12px; }
        .btn { flex: 1; padding: 12px; border: none; border-radius: 8px; cursor: pointer; font-size: 14px; font-weight: 500; transition: all 0.15s; }
        .btn:disabled { opacity: 0.5; cursor: not-allowed; }
        #cancelBtn { background: white; color: #374151; border: 1px solid #d1d5db; }
        #cancelBtn:hover:not(:disabled) { background: #f9fafb; }
        
        /* Variant-specific confirm button styles */
        .dialog.warning #confirmBtn { background: #F59E0B; color: white; }
        .dialog.warning #confirmBtn:hover:not(:disabled) { background: #D97706; }
        .dialog.danger #confirmBtn { background: #EF4444; color: white; }
        .dialog.danger #confirmBtn:hover:not(:disabled) { background: #DC2626; }
        .dialog.info #confirmBtn { background: #3B82F6; color: white; }
        .dialog.info #confirmBtn:hover:not(:disabled) { background: #2563EB; }
        
        .result { text-align: center; padding: 12px; font-weight: 500; }
        .result.confirmed { color: #10B981; }
        .result.cancelled { color: #6B7280; }
        #log { font-size: 11px; color: #9CA3AF; text-align: center; padding: 8px; }
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
        </div>
        <div class=""dialog-footer"">
            <button id=""cancelBtn"" class=""btn"" onclick=""cancel()"">Cancel</button>
            <button id=""confirmBtn"" class=""btn"" onclick=""confirm()"">Confirm</button>
        </div>
        <div id=""result"" class=""result""></div>
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
    /// </summary>
    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
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
            Content = new StringContent(responseObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
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
            Content = new StringContent(responseObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
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

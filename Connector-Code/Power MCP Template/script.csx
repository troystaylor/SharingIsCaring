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
/// Power MCP Template - Model Context Protocol implementation for Power Platform Custom Connectors
/// 
/// Features:
/// - Full JSON-RPC 2.0 protocol support
/// - MCP specification compliance (2025-11-25) - Copilot Studio compatible
/// - Application Insights telemetry integration (optional)
/// - Context.Logger for basic logging
/// - Comprehensive error handling with correlation IDs
/// - External API call helper with authorization forwarding
/// 
/// Usage:
/// 1. Update SERVER CONFIGURATION section with your server details
/// 2. Define your tools in BuildToolsList()
/// 3. Add tool handlers in ExecuteToolAsync()
/// 4. Implement tool logic in dedicated methods
/// 5. (Optional) Add APP_INSIGHTS_CONNECTION_STRING for telemetry
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
    
    /// <summary>MCP protocol version supported</summary>
    private const string ProtocolVersion = "2025-11-25";

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
                case "notifications/cancelled":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
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

                // Resources (required by MCP spec even if empty)
                case "resources/list":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                    break;

                case "resources/templates/list":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });
                    break;

                case "resources/read":
                    response = await HandleResourcesReadAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;

                // Prompts (required by MCP spec even if empty)
                case "prompts/list":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });
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

                // Logging level (acknowledge but no action needed)
                case "logging/setLevel":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject());
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

            // Return MCP-compliant tool result
            return CreateJsonRpcSuccessResponse(requestId, new JObject
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
            });
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

            // TODO: Add your custom tools here
            // case "your_tool_name":
            //     return await ExecuteYourToolAsync(arguments).ConfigureAwait(false);

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    /// <summary>
    /// Handle resources/read request
    /// </summary>
    private async Task<HttpResponseMessage> HandleResourcesReadAsync(string correlationId, JObject request, JToken requestId)
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

        // TODO: Implement resource reading logic based on URI
        // For now, return a not found error
        return CreateJsonRpcErrorResponse(requestId, -32602, "Resource not found", uri);
    }

    /// <summary>
    /// Handle prompts/get request
    /// </summary>
    private async Task<HttpResponseMessage> HandlePromptsGetAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");

        if (string.IsNullOrWhiteSpace(promptName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Prompt name is required");
        }

        _ = LogToAppInsights("McpPromptGet", new
        {
            CorrelationId = correlationId,
            PromptName = promptName
        });

        // TODO: Implement prompt retrieval logic
        // For now, return a not found error
        return CreateJsonRpcErrorResponse(requestId, -32602, "Prompt not found", promptName);
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
    // EXTERNAL API HELPERS
    // ========================================

    /// <summary>
    /// Send a request to an external API
    /// Forwards the connector's authorization header if present
    /// </summary>
    private async Task<JObject> SendExternalRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);
        
        // Forward OAuth token from connector (for delegated auth scenarios)
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
        {
            request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");
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

    // ========================================
    // TOOL/RESOURCE/PROMPT DEFINITIONS
    // ========================================

    /// <summary>
    /// Build the list of available tools - define your tools here
    /// Each tool needs: name, description, and inputSchema (JSON Schema)
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

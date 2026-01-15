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
/// Graph Power Orchestration: Power MCP tool server for Microsoft Graph
/// Features:
/// - Dynamic tool discovery via MS Learn MCP Server
/// - Chained MCP architecture (acts as both MCP server and MCP client)
/// - Delegated authentication (OBO) with comprehensive Graph scopes
/// - User-friendly permission error handling
/// Orchestration tools: discover_graph, invoke_graph
/// </summary>
public class Script : ScriptBase
{
    // Application Insights telemetry (optional - leave empty to disable)
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // MS Learn MCP Server endpoint (public, no auth required)
    private const string MS_LEARN_MCP_ENDPOINT = "https://learn.microsoft.com/api/mcp";

    // Tool names
    private const string TOOL_DISCOVER_GRAPH = "discover_graph";
    private const string TOOL_INVOKE_GRAPH = "invoke_graph";
    private const string TOOL_BATCH_INVOKE_GRAPH = "batch_invoke_graph";

    // Simple in-memory cache for discover_graph results (persists for connector instance lifetime)
    private static Dictionary<string, CacheEntry> _discoveryCache = new Dictionary<string, CacheEntry>();
    private const int CACHE_EXPIRY_MINUTES = 10;

    private class CacheEntry
    {
        public JObject Result { get; set; }
        public DateTime Expiry { get; set; }
    }

    // Tool handler registry
    private Dictionary<string, Func<JObject, Task<JObject>>> _toolHandlers;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation("Graph Power Orchestration request received");

        _ = LogToAppInsights("RequestReceived", new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["path"] = this.Context.Request.RequestUri.AbsolutePath,
            ["method"] = this.Context.Request.Method.Method
        });

        try
        {
            // MCP Protocol mode - JSON-RPC 2.0
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            this.Context.Logger.LogDebug($"Request body length: {body?.Length ?? 0} characters");
            
            if (string.IsNullOrWhiteSpace(body))
            {
                this.Context.Logger.LogWarning("Empty request body received");
                return CreateErrorResponse(-32600, "Empty request body", null);
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(body);
            }
            catch (JsonException ex)
            {
                return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", null);
            }

            // Log raw request for debugging
            _ = LogToAppInsights("RawRequest", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["body"] = body.Length > 1000 ? body.Substring(0, 1000) + "..." : body
            });

            return await HandleMCPRequest(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("RequestError", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["error"] = ex.Message,
                ["errorType"] = ex.GetType().Name
            });
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", null);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["durationMs"] = duration.TotalMilliseconds.ToString("F0")
            });
        }
    }

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleMCPRequest(JObject request)
    {
        var method = request["method"]?.ToString();
        var id = request["id"];
        this.Context.Logger.LogInformation($"MCP method: {method}");

        // Log which MCP method is being called for debugging
        _ = LogToAppInsights("MCPMethod", new Dictionary<string, string>
        {
            ["method"] = method ?? "null",
            ["hasId"] = (id != null).ToString(),
            ["requestKeys"] = string.Join(",", request.Properties().Select(p => p.Name))
        });

        try
        {
            switch (method)
            {
                case "initialize":
                    return CreateSuccessResponse(new JObject
                    {
                        ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                        ["capabilities"] = GetServerCapabilities(),
                        ["serverInfo"] = GetServerInfo()
                    }, id);
                    
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateSuccessResponse(new JObject(), id);
                    
                case "ping":
                    return CreateSuccessResponse(new JObject(), id);
                    
                case "tools/list":
                    return CreateSuccessResponse(new JObject { ["tools"] = GetToolDefinitions() }, id);
                    
                case "tools/call":
                    return await HandleToolsCall(request["params"] as JObject, id).ConfigureAwait(false);
                    
                case "resources/list":
                    return CreateSuccessResponse(new JObject { ["resources"] = new JArray() }, id);
                    
                case "resources/templates/list":
                    return CreateSuccessResponse(new JObject { ["resourceTemplates"] = new JArray() }, id);
                    
                case "prompts/list":
                    return CreateSuccessResponse(new JObject { ["prompts"] = new JArray() }, id);
                    
                case "completion/complete":
                    return CreateSuccessResponse(new JObject 
                    { 
                        ["completion"] = new JObject 
                        { 
                            ["values"] = new JArray(), 
                            ["total"] = 0, 
                            ["hasMore"] = false 
                        } 
                    }, id);
                    
                case "logging/setLevel":
                    return CreateSuccessResponse(new JObject(), id);
                    
                default:
                    return CreateErrorResponse(-32601, $"Method not found: {method}", id);
            }
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(-32700, $"Parse error: {ex.Message}", id);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(-32603, $"Internal error: {ex.Message}", id);
        }
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject parms, JToken id)
    {
        if (parms == null) return CreateErrorResponse(-32602, "params object required", id);
        
        var toolName = parms["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(toolName)) return CreateErrorResponse(-32602, "Tool name required", id);

        var tools = GetToolDefinitions();
        if (!tools.Any(t => t["name"]?.ToString() == toolName))
            return CreateErrorResponse(-32601, $"Unknown tool: {toolName}", id);

        var arguments = parms["arguments"] as JObject ?? new JObject();
        
        try
        {
            var result = await ExecuteToolByName(toolName, arguments).ConfigureAwait(false);
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString() } },
                ["isError"] = false
            }, id);
        }
        catch (PermissionException ex)
        {
            // Return permission errors as tool results, not MCP errors
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = ex.ToJson().ToString() } },
                ["isError"] = true
            }, id);
        }
        catch (ArgumentException ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
        catch (Exception ex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    #endregion

    #region Server Metadata

    private JObject GetServerInfo() => new JObject
    {
        ["name"] = "graph-power-orchestration",
        ["version"] = "1.0.0",
        ["title"] = "Graph Power Orchestration",
        ["description"] = "Power MCP tool server for Microsoft Graph with dynamic discovery via MS Learn MCP"
    };

    private JObject GetServerCapabilities() => new JObject
    {
        ["tools"] = new JObject { ["listChanged"] = false },
        ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
        ["prompts"] = new JObject { ["listChanged"] = false },
        ["logging"] = new JObject(),
        ["completions"] = new JObject()
    };

    private JArray GetToolDefinitions() => new JArray
    {
        new JObject
        {
            ["name"] = TOOL_DISCOVER_GRAPH,
            ["description"] = "Discover Microsoft Graph API operations by searching MS Learn documentation. Returns relevant Graph endpoints with their HTTP methods, parameters, and required permissions. Use this before invoke_graph to find the right API for your task.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject 
                    { 
                        ["type"] = "string", 
                        ["description"] = "Natural language description of what you want to do (e.g., 'list user's calendar events', 'send email', 'get team members')" 
                    },
                    ["category"] = new JObject 
                    { 
                        ["type"] = "string", 
                        ["description"] = "Optional Graph category filter: users, groups, teams, mail, calendar, files, sites, planner, tasks, contacts, security, reports" 
                    }
                },
                ["required"] = new JArray { "query" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_INVOKE_GRAPH,
            ["description"] = "Execute a Microsoft Graph API request. Use discover_graph first to find the correct endpoint, method, and parameters. Supports all Graph v1.0 and beta endpoints with the user's delegated permissions.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["endpoint"] = new JObject 
                    { 
                        ["type"] = "string", 
                        ["description"] = "Graph API endpoint path (e.g., '/me/messages', '/users/{id}/calendar/events', '/teams/{id}/channels')" 
                    },
                    ["method"] = new JObject 
                    { 
                        ["type"] = "string", 
                        ["description"] = "HTTP method: GET, POST, PATCH, PUT, DELETE",
                        ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" }
                    },
                    ["body"] = new JObject 
                    { 
                        ["type"] = "object", 
                        ["description"] = "Request body for POST, PATCH, PUT operations" 
                    },
                    ["queryParams"] = new JObject 
                    { 
                        ["type"] = "object", 
                        ["description"] = "OData query parameters ($select, $filter, $expand, $orderby, $top, $skip)" 
                    },
                    ["apiVersion"] = new JObject 
                    { 
                        ["type"] = "string", 
                        ["description"] = "API version: v1.0 (default) or beta",
                        ["enum"] = new JArray { "v1.0", "beta" },
                        ["default"] = "v1.0"
                    }
                },
                ["required"] = new JArray { "endpoint", "method" }
            }
        },
        new JObject
        {
            ["name"] = TOOL_BATCH_INVOKE_GRAPH,
            ["description"] = "Execute multiple Microsoft Graph API requests in a single batch call. More efficient than multiple invoke_graph calls. Supports up to 20 requests per batch. Use for workflows that need multiple Graph operations.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["requests"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of Graph API requests to execute in batch (max 20)",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique identifier for this request in the batch" },
                                ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "Graph API endpoint path" },
                                ["method"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" } },
                                ["body"] = new JObject { ["type"] = "object", ["description"] = "Request body for POST/PATCH/PUT" },
                                ["headers"] = new JObject { ["type"] = "object", ["description"] = "Optional headers for this request" }
                            },
                            ["required"] = new JArray { "id", "endpoint", "method" }
                        },
                        ["maxItems"] = 20
                    },
                    ["apiVersion"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "API version: v1.0 (default) or beta",
                        ["enum"] = new JArray { "v1.0", "beta" },
                        ["default"] = "v1.0"
                    }
                },
                ["required"] = new JArray { "requests" }
            }
        }
    };

    #endregion

    #region Tool Execution

    private void InitializeToolHandlers()
    {
        _toolHandlers = new Dictionary<string, Func<JObject, Task<JObject>>>
        {
            [TOOL_DISCOVER_GRAPH] = ExecuteDiscoverGraph,
            [TOOL_INVOKE_GRAPH] = ExecuteInvokeGraph,
            [TOOL_BATCH_INVOKE_GRAPH] = ExecuteBatchInvokeGraph
        };
    }

    private async Task<JObject> ExecuteToolByName(string toolName, JObject args)
    {
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation($"Executing tool: {toolName}");
        this.Context.Logger.LogDebug($"Tool arguments: {args?.ToString(Newtonsoft.Json.Formatting.None)}");

        if (_toolHandlers == null) InitializeToolHandlers();

        try
        {
            if (_toolHandlers.TryGetValue(toolName, out var handler))
            {
                var result = await handler(args).ConfigureAwait(false);

                _ = LogToAppInsights("ToolExecuted", new Dictionary<string, string>
                {
                    ["toolName"] = toolName,
                    ["success"] = "true",
                    ["durationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
                });

                return result;
            }

            throw new Exception($"Unknown tool: {toolName}");
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("ToolExecuted", new Dictionary<string, string>
            {
                ["toolName"] = toolName,
                ["success"] = "false",
                ["error"] = ex.Message,
                ["durationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
            });
            throw;
        }
    }

    #endregion

    #region discover_graph Implementation

    /// <summary>
    /// Discover Graph operations by calling MS Learn MCP Server (with caching)
    /// </summary>
    private async Task<JObject> ExecuteDiscoverGraph(JObject args)
    {
        var query = Require(args, "query");
        var category = args["category"]?.ToString();

        // Build cache key
        var cacheKey = $"{query}|{category ?? ""}".ToLower();
        
        // Check cache first
        if (_discoveryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            this.Context.Logger.LogDebug($"Returning cached discover_graph result for: {query}");
            var cachedResult = cached.Result.DeepClone() as JObject;
            cachedResult["cached"] = true;
            return cachedResult;
        }

        // Build enhanced query for Graph-specific search
        var enhancedQuery = $"Microsoft Graph API {query}";
        if (!string.IsNullOrWhiteSpace(category))
        {
            enhancedQuery = $"Microsoft Graph {category} API {query}";
        }

        this.Context.Logger.LogInformation($"Discovering Graph operations for: {enhancedQuery}");

        try
        {
            // Call MS Learn MCP Server
            var searchResults = await CallMsLearnMcp("microsoft_docs_search", new JObject
            {
                ["query"] = enhancedQuery
            }).ConfigureAwait(false);

            // Parse and extract Graph-relevant information
            var graphOperations = ExtractGraphOperations(searchResults, query);
            
            // Add permission hints to operations
            AddPermissionHints(graphOperations);

            var result = new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["category"] = category,
                ["operationCount"] = graphOperations.Count,
                ["operations"] = graphOperations,
                ["tip"] = "Use invoke_graph with the endpoint and method from the results above"
            };
            
            // Cache the result
            _discoveryCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject,
                Expiry = DateTime.UtcNow.AddMinutes(CACHE_EXPIRY_MINUTES)
            };
            
            return result;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"MS Learn MCP call failed: {ex.Message}");
            
            // Fallback: return common Graph patterns based on query keywords
            var fallbackOperations = GetFallbackOperations(query, category);
            
            return new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["category"] = category,
                ["operationCount"] = fallbackOperations.Count,
                ["operations"] = fallbackOperations,
                ["note"] = "Results from cached common patterns (MS Learn MCP unavailable)",
                ["tip"] = "Use invoke_graph with the endpoint and method from the results above"
            };
        }
    }

    /// <summary>
    /// Call MS Learn MCP Server with proper MCP handshake (public endpoint, no auth required)
    /// </summary>
    private async Task<JObject> CallMsLearnMcp(string toolName, JObject arguments)
    {
        // Step 1: Initialize handshake
        var initializeRequest = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "initialize",
            ["id"] = Guid.NewGuid().ToString(),
            ["params"] = new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "graph-power-orchestration",
                    ["version"] = "1.0.0"
                }
            }
        };

        var initResponse = await SendMcpRequest(initializeRequest).ConfigureAwait(false);
        
        // Check for initialize error
        if (initResponse["error"] != null)
        {
            throw new Exception($"MS Learn MCP initialize failed: {initResponse["error"]["message"]}");
        }

        this.Context.Logger.LogDebug($"MS Learn MCP initialized: {initResponse["result"]?["serverInfo"]?["name"]}");

        // Step 2: Send initialized notification
        var initializedNotification = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };

        await SendMcpRequest(initializedNotification).ConfigureAwait(false);

        // Step 3: Call the tool
        var toolRequest = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = Guid.NewGuid().ToString(),
            ["params"] = new JObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            }
        };

        var toolResponse = await SendMcpRequest(toolRequest).ConfigureAwait(false);
        
        // Check for tool error
        if (toolResponse["error"] != null)
        {
            throw new Exception($"MS Learn MCP tool error: {toolResponse["error"]["message"]}");
        }

        // Extract result content
        var resultContent = toolResponse["result"]?["content"] as JArray;
        if (resultContent != null && resultContent.Count > 0)
        {
            var textContent = resultContent[0]?["text"]?.ToString();
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                try
                {
                    return JObject.Parse(textContent);
                }
                catch
                {
                    return new JObject { ["text"] = textContent };
                }
            }
        }

        return toolResponse["result"] as JObject ?? new JObject();
    }

    /// <summary>
    /// Send a single MCP request to MS Learn endpoint
    /// </summary>
    private async Task<JObject> SendMcpRequest(JObject mcpRequest)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MS_LEARN_MCP_ENDPOINT)
        {
            Content = new StringContent(mcpRequest.ToString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"MS Learn MCP returned {response.StatusCode}: {content}");
        }

        return JObject.Parse(content);
    }

    /// <summary>
    /// Extract Graph operation details from MS Learn search results
    /// </summary>
    private JArray ExtractGraphOperations(JObject searchResults, string originalQuery)
    {
        var operations = new JArray();
        
        // Parse the search results and look for Graph API patterns
        var chunks = searchResults["chunks"] as JArray ?? new JArray();
        
        foreach (var chunk in chunks)
        {
            var title = chunk["title"]?.ToString() ?? "";
            var content = chunk["content"]?.ToString() ?? "";
            var url = chunk["url"]?.ToString() ?? "";
            
            // Skip non-Graph content
            if (!url.Contains("graph") && !title.ToLower().Contains("graph"))
                continue;
            
            // Try to extract endpoint patterns from content
            var endpointMatches = ExtractEndpointsFromContent(content);
            
            foreach (var endpoint in endpointMatches)
            {
                operations.Add(new JObject
                {
                    ["title"] = title,
                    ["endpoint"] = endpoint.Path,
                    ["method"] = endpoint.Method,
                    ["description"] = TruncateDescription(content, 200),
                    ["documentationUrl"] = url
                });
            }
            
            // If no endpoints found but it's Graph content, add as reference
            if (endpointMatches.Count == 0 && url.Contains("graph"))
            {
                operations.Add(new JObject
                {
                    ["title"] = title,
                    ["description"] = TruncateDescription(content, 200),
                    ["documentationUrl"] = url,
                    ["note"] = "See documentation for endpoint details"
                });
            }
        }
        
        // Deduplicate and limit results
        var seen = new HashSet<string>();
        var uniqueOps = new JArray();
        
        foreach (var op in operations)
        {
            var key = $"{op["endpoint"]}|{op["method"]}";
            if (string.IsNullOrWhiteSpace(op["endpoint"]?.ToString()))
                key = op["documentationUrl"]?.ToString() ?? op["title"]?.ToString();
                
            if (!seen.Contains(key))
            {
                seen.Add(key);
                uniqueOps.Add(op);
                if (uniqueOps.Count >= 10) break;
            }
        }
        
        return uniqueOps;
    }

    private class EndpointMatch
    {
        public string Path { get; set; }
        public string Method { get; set; }
    }

    /// <summary>
    /// Extract Graph API endpoints from documentation content
    /// </summary>
    private List<EndpointMatch> ExtractEndpointsFromContent(string content)
    {
        var matches = new List<EndpointMatch>();
        
        // Common patterns for Graph endpoints in documentation
        var patterns = new[]
        {
            // HTTP method followed by endpoint
            @"(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)",
            // Endpoint patterns like /me/messages, /users/{id}
            @"(?:endpoint|path|url):\s*[`""']?(/[\w\{\}/\-\.]+)[`""']?",
            // Code block patterns
            @"```\s*(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)"
        };
        
        foreach (var pattern in patterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var regexMatches = regex.Matches(content);
            
            foreach (System.Text.RegularExpressions.Match match in regexMatches)
            {
                if (match.Groups.Count >= 3)
                {
                    var method = match.Groups[1].Value.ToUpper();
                    var path = match.Groups[2].Value;
                    
                    // Validate it looks like a Graph endpoint
                    if (path.StartsWith("/") && !path.Contains("://"))
                    {
                        matches.Add(new EndpointMatch { Path = path, Method = method });
                    }
                }
            }
        }
        
        return matches;
    }

    private string TruncateDescription(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    /// Add permission hints to discovered operations based on endpoint patterns
    /// </summary>
    private void AddPermissionHints(JArray operations)
    {
        foreach (var op in operations)
        {
            var endpoint = op["endpoint"]?.ToString()?.ToLower() ?? "";
            var method = op["method"]?.ToString()?.ToUpper() ?? "GET";
            
            var permissions = InferPermissions(endpoint, method);
            if (permissions.Count > 0)
            {
                op["requiredPermissions"] = new JArray(permissions);
            }
        }
    }

    /// <summary>
    /// Infer required Graph permissions based on endpoint and method
    /// </summary>
    private List<string> InferPermissions(string endpoint, string method)
    {
        var permissions = new List<string>();
        var isWrite = method != "GET";
        
        // Mail permissions
        if (endpoint.Contains("/messages") || endpoint.Contains("/mailfolders") || endpoint.Contains("/sendmail"))
        {
            permissions.Add(isWrite ? "Mail.Send" : "Mail.Read");
        }
        // Calendar permissions
        else if (endpoint.Contains("/calendar") || endpoint.Contains("/events"))
        {
            permissions.Add(isWrite ? "Calendars.ReadWrite" : "Calendars.Read");
        }
        // User permissions
        else if (endpoint.Contains("/users") || endpoint == "/me")
        {
            permissions.Add(isWrite ? "User.ReadWrite.All" : "User.Read.All");
        }
        // Group permissions
        else if (endpoint.Contains("/groups"))
        {
            permissions.Add(isWrite ? "Group.ReadWrite.All" : "Group.Read.All");
        }
        // Teams permissions
        else if (endpoint.Contains("/teams") || endpoint.Contains("/channels"))
        {
            if (endpoint.Contains("/messages"))
            {
                permissions.Add("ChannelMessage.Send");
            }
            else
            {
                permissions.Add(isWrite ? "Team.ReadBasic.All" : "Team.ReadBasic.All");
            }
        }
        // Files/Drive permissions
        else if (endpoint.Contains("/drive") || endpoint.Contains("/items"))
        {
            permissions.Add(isWrite ? "Files.ReadWrite" : "Files.Read");
        }
        // Sites permissions
        else if (endpoint.Contains("/sites"))
        {
            permissions.Add(isWrite ? "Sites.ReadWrite.All" : "Sites.Read.All");
        }
        // Planner permissions
        else if (endpoint.Contains("/planner"))
        {
            permissions.Add(isWrite ? "Tasks.ReadWrite" : "Tasks.Read");
        }
        // To-Do permissions
        else if (endpoint.Contains("/todo"))
        {
            permissions.Add(isWrite ? "Tasks.ReadWrite" : "Tasks.Read");
        }
        // Contacts permissions
        else if (endpoint.Contains("/contacts"))
        {
            permissions.Add(isWrite ? "Contacts.ReadWrite" : "Contacts.Read");
        }
        
        return permissions;
    }

    /// <summary>
    /// Fallback guidance when MS Learn MCP is unavailable
    /// </summary>
    private JArray GetFallbackOperations(string query, string category)
    {
        // Return guidance instead of hardcoded operations
        return new JArray
        {
            new JObject
            {
                ["note"] = "MS Learn MCP discovery unavailable. Use invoke_graph directly with common patterns.",
                ["commonPatterns"] = new JArray
                {
                    "/me - Current user",
                    "/me/messages - Emails", 
                    "/me/calendar/events - Calendar",
                    "/me/drive/root/children - Files",
                    "/me/joinedTeams - Teams",
                    "/users - Users",
                    "/groups - Groups"
                },
                ["queryParamsTip"] = "Add $select to limit fields, $top for count, $filter for conditions",
                ["documentationUrl"] = "https://learn.microsoft.com/graph/api/overview"
            }
        };
    }

    private JObject CreateFallbackOp(string endpoint, string method, string description, string selectFields = null, JObject exampleBody = null)
    {
        var op = new JObject
        {
            ["endpoint"] = endpoint,
            ["method"] = method,
            ["description"] = description,
            ["documentationUrl"] = "https://learn.microsoft.com/graph/api/overview?view=graph-rest-1.0"
        };
        
        if (!string.IsNullOrEmpty(selectFields))
        {
            op["recommendedSelect"] = selectFields;
            op["queryParams"] = new JObject { ["$select"] = selectFields };
        }
        
        if (exampleBody != null)
        {
            op["exampleBody"] = exampleBody;
        }
        
        return op;
    }

    #endregion

    #region invoke_graph Implementation

    /// <summary>
    /// Execute a Graph API request
    /// </summary>
    private async Task<JObject> ExecuteInvokeGraph(JObject args)
    {
        var endpoint = Require(args, "endpoint");
        var method = Require(args, "method").ToUpper();
        var body = args["body"] as JObject;
        var queryParams = args["queryParams"] as JObject ?? new JObject();
        var apiVersion = args["apiVersion"]?.ToString() ?? "v1.0";

        // Validate method
        var validMethods = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" };
        if (!validMethods.Contains(method))
        {
            throw new ArgumentException($"Invalid method: {method}. Must be one of: {string.Join(", ", validMethods)}");
        }

        // Add intelligent defaults for calendar endpoints
        if (method == "GET" && IsCalendarEndpoint(endpoint))
        {
            AddCalendarDateDefaults(endpoint, queryParams);
        }

        // Validate endpoint before calling
        ValidateEndpoint(endpoint, method);

        // Build full URL (includes auto-adding $top for collections)
        var url = BuildGraphUrl(endpoint, apiVersion, queryParams, method);
        this.Context.Logger.LogInformation($"Invoking Graph: {method} {url}");

        // Execute request
        var result = await SendGraphRequest(new HttpMethod(method), url, body).ConfigureAwait(false);
        
        // Handle pagination metadata
        var response = new JObject
        {
            ["success"] = true,
            ["endpoint"] = endpoint,
            ["method"] = method,
            ["apiVersion"] = apiVersion,
            ["data"] = result
        };
        
        // Add pagination info if present
        if (result["@odata.nextLink"] != null)
        {
            response["hasMore"] = true;
            response["nextPageHint"] = "To get more results, call invoke_graph again with the full nextLink URL as the endpoint";
            response["nextLink"] = result["@odata.nextLink"];
        }
        
        if (result["@odata.count"] != null)
        {
            response["totalCount"] = result["@odata.count"];
        }
        
        return response;
    }

    /// <summary>
    /// Check if endpoint is calendar-related
    /// </summary>
    private bool IsCalendarEndpoint(string endpoint)
    {
        var lower = endpoint.ToLower();
        return lower.Contains("/calendar") || lower.Contains("/events") || lower.Contains("/calendarview");
    }

    /// <summary>
    /// Add intelligent date range defaults for calendar queries
    /// </summary>
    private void AddCalendarDateDefaults(string endpoint, JObject queryParams)
    {
        var lower = endpoint.ToLower();
        
        // calendarView requires startDateTime and endDateTime
        if (lower.Contains("/calendarview"))
        {
            if (queryParams["startDateTime"] == null && queryParams["$startDateTime"] == null)
            {
                // Default to start of today
                queryParams["startDateTime"] = DateTime.UtcNow.Date.ToString("o");
                this.Context.Logger.LogDebug("Auto-added startDateTime (today)");
            }
            if (queryParams["endDateTime"] == null && queryParams["$endDateTime"] == null)
            {
                // Default to 7 days from now
                queryParams["endDateTime"] = DateTime.UtcNow.Date.AddDays(7).ToString("o");
                this.Context.Logger.LogDebug("Auto-added endDateTime (7 days from now)");
            }
        }
        // For /events endpoint, add $orderby if not specified
        else if (lower.Contains("/events") && queryParams["$orderby"] == null && queryParams["orderby"] == null)
        {
            queryParams["$orderby"] = "start/dateTime";
            this.Context.Logger.LogDebug("Auto-added $orderby=start/dateTime");
        }
    }

    /// <summary>
    /// Validate endpoint for common mistakes before calling Graph
    /// </summary>
    private void ValidateEndpoint(string endpoint, string method)
    {
        var warnings = new List<string>();
        
        // Check for placeholder patterns that weren't replaced
        if (endpoint.Contains("{id}") || endpoint.Contains("{listId}") || endpoint.Contains("{channelId}"))
        {
            var placeholders = System.Text.RegularExpressions.Regex.Matches(endpoint, @"\{[^}]+\}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .ToList();
            throw new ArgumentException($"Endpoint contains unresolved placeholders: {string.Join(", ", placeholders)}. Replace these with actual IDs.");
        }
        
        // Validate method makes sense for endpoint
        if (method == "DELETE" && (endpoint.EndsWith("/messages") || endpoint.EndsWith("/events") || endpoint.EndsWith("/users")))
        {
            throw new ArgumentException($"Cannot DELETE a collection. Specify a specific item ID, e.g., {endpoint}/{{id}}");
        }
        
        // Check for double slashes
        if (endpoint.Contains("//"))
        {
            throw new ArgumentException($"Endpoint contains double slashes: {endpoint}. Check the path format.");
        }
        
        // Validate beta endpoints
        if (endpoint.StartsWith("beta/"))
        {
            throw new ArgumentException("Don't include 'beta/' in the endpoint. Use the apiVersion parameter instead.");
        }
        
        // Validate v1.0 prefix
        if (endpoint.StartsWith("v1.0/"))
        {
            throw new ArgumentException("Don't include 'v1.0/' in the endpoint. Use the apiVersion parameter instead.");
        }
    }

    // Default limit for collection queries to prevent huge responses
    private const int DEFAULT_TOP_LIMIT = 25;

    private string BuildGraphUrl(string endpoint, string apiVersion, JObject queryParams, string method)
    {
        // Normalize endpoint
        endpoint = endpoint.TrimStart('/');
        
        // Build base URL
        var url = $"https://graph.microsoft.com/{apiVersion}/{endpoint}";
        
        // Determine if this is a collection endpoint (likely returns multiple items)
        var isCollectionEndpoint = IsCollectionEndpoint(endpoint);
        
        // Build query parameters
        var queryParts = new List<string>();
        var hasTop = false;
        
        if (queryParams != null && queryParams.Count > 0)
        {
            foreach (var prop in queryParams.Properties())
            {
                var key = prop.Name.StartsWith("$") ? prop.Name : $"${prop.Name}";
                if (key == "$top") hasTop = true;
                queryParts.Add($"{key}={Uri.EscapeDataString(prop.Value.ToString())}");
            }
        }
        
        // Auto-add $top for GET requests on collection endpoints if not specified
        if (method == "GET" && isCollectionEndpoint && !hasTop)
        {
            queryParts.Add($"$top={DEFAULT_TOP_LIMIT}");
            this.Context.Logger.LogDebug($"Auto-added $top={DEFAULT_TOP_LIMIT} to limit response size");
        }
        
        if (queryParts.Count > 0)
        {
            url += "?" + string.Join("&", queryParts);
        }
        
        return url;
    }

    /// <summary>
    /// Determine if endpoint returns a collection (multiple items)
    /// </summary>
    private bool IsCollectionEndpoint(string endpoint)
    {
        // Endpoints that typically return collections
        var collectionPatterns = new[]
        {
            "/messages", "/events", "/users", "/groups", "/teams",
            "/channels", "/members", "/children", "/items", "/lists",
            "/tasks", "/contacts", "/calendars", "/drives", "/sites"
        };
        
        // Check if endpoint ends with a collection pattern (not followed by /{id})
        var lowerEndpoint = endpoint.ToLower();
        foreach (var pattern in collectionPatterns)
        {
            if (lowerEndpoint.EndsWith(pattern) || lowerEndpoint.Contains(pattern + "?"))
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Send request to Microsoft Graph API with proper error handling and retry logic
    /// </summary>
    private async Task<JObject> SendGraphRequest(HttpMethod method, string url, JObject body, int retryCount = 0)
    {
        const int MAX_RETRIES = 3;
        
        var request = new HttpRequestMessage(method, url);
        
        // Forward OAuth token from connector
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Handle throttling (429) with retry
        if (response.StatusCode == (HttpStatusCode)429 && retryCount < MAX_RETRIES)
        {
            var retryAfterSeconds = 5; // Default
            
            // Parse Retry-After header if present
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var retryValue = retryValues.FirstOrDefault();
                if (int.TryParse(retryValue, out var seconds))
                {
                    retryAfterSeconds = Math.Min(seconds, 30); // Cap at 30 seconds
                }
            }
            
            this.Context.Logger.LogWarning($"Throttled (429). Retry {retryCount + 1}/{MAX_RETRIES} after {retryAfterSeconds}s");
            
            await Task.Delay(retryAfterSeconds * 1000, this.CancellationToken).ConfigureAwait(false);
            return await SendGraphRequest(method, url, body, retryCount + 1).ConfigureAwait(false);
        }

        // Handle different status codes with user-friendly messages
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            
            // Parse Graph error response
            JObject errorBody = null;
            string errorCode = null;
            string errorMessage = null;
            
            try
            {
                errorBody = JObject.Parse(content);
                errorCode = errorBody["error"]?["code"]?.ToString();
                errorMessage = errorBody["error"]?["message"]?.ToString();
            }
            catch
            {
                errorMessage = content;
            }

            // Handle permission errors (401, 403)
            if (statusCode == 401 || statusCode == 403)
            {
                throw new PermissionException(statusCode, errorCode, errorMessage, url);
            }

            // Handle not found (404) - could be permission or actual not found
            if (statusCode == 404)
            {
                throw new PermissionException(statusCode, errorCode ?? "NotFound", 
                    errorMessage ?? "The requested resource was not found, or you don't have permission to view it.", 
                    url);
            }

            // Other errors
            return new JObject
            {
                ["error"] = true,
                ["status"] = statusCode,
                ["code"] = errorCode ?? response.ReasonPhrase,
                ["message"] = errorMessage ?? "An error occurred",
                ["details"] = errorBody
            };
        }

        // Parse successful response
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };
        }

        try
        {
            var result = JObject.Parse(content);
            
            // Summarize response to reduce size (strip large HTML bodies, etc.)
            SummarizeResponse(result);
            
            return result;
        }
        catch
        {
            return new JObject { ["text"] = content };
        }
    }

    /// <summary>
    /// Summarize response to reduce size - strips large HTML bodies, truncates long text
    /// </summary>
    private void SummarizeResponse(JToken token)
    {
        const int MAX_BODY_LENGTH = 500;
        const int MAX_TEXT_LENGTH = 1000;
        
        if (token is JObject obj)
        {
            // Process known large fields
            foreach (var prop in obj.Properties().ToList())
            {
                var name = prop.Name.ToLower();
                var value = prop.Value;
                
                // Handle body objects (email/event body with HTML content)
                if (name == "body" && value is JObject bodyObj)
                {
                    var content = bodyObj["content"]?.ToString();
                    if (!string.IsNullOrEmpty(content) && content.Length > MAX_BODY_LENGTH)
                    {
                        // Strip HTML and truncate
                        var plainText = StripHtml(content);
                        if (plainText.Length > MAX_BODY_LENGTH)
                        {
                            plainText = plainText.Substring(0, MAX_BODY_LENGTH) + "... [truncated]";
                        }
                        bodyObj["content"] = plainText;
                        bodyObj["contentType"] = "text";
                        bodyObj["_truncated"] = true;
                    }
                }
                // Handle bodyPreview (already short, but ensure limit)
                else if (name == "bodypreview" && value.Type == JTokenType.String)
                {
                    var text = value.ToString();
                    if (text.Length > MAX_TEXT_LENGTH)
                    {
                        obj[prop.Name] = text.Substring(0, MAX_TEXT_LENGTH) + "...";
                    }
                }
                // Recursively process nested objects and arrays
                else if (value is JObject || value is JArray)
                {
                    SummarizeResponse(value);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
            {
                SummarizeResponse(item);
            }
        }
    }

    /// <summary>
    /// Strip HTML tags from content
    /// </summary>
    private string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        
        // Remove script and style blocks
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", 
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Remove HTML tags
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
        
        // Decode common HTML entities
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
        
        // Collapse whitespace
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").Trim();
        
        return html;
    }

    #endregion

    #region batch_invoke_graph Implementation

    /// <summary>
    /// Execute multiple Graph API requests in a single batch call
    /// </summary>
    private async Task<JObject> ExecuteBatchInvokeGraph(JObject args)
    {
        var requests = args["requests"] as JArray;
        var apiVersion = args["apiVersion"]?.ToString() ?? "v1.0";

        if (requests == null || requests.Count == 0)
        {
            throw new ArgumentException("'requests' array is required and must contain at least one request");
        }

        if (requests.Count > 20)
        {
            throw new ArgumentException($"Batch requests limited to 20 items. You provided {requests.Count}. Split into multiple batch calls.");
        }

        this.Context.Logger.LogInformation($"Executing batch with {requests.Count} requests");

        // Build batch request body
        var batchRequests = new JArray();
        foreach (var req in requests)
        {
            var id = req["id"]?.ToString() ?? Guid.NewGuid().ToString();
            var endpoint = req["endpoint"]?.ToString();
            var method = req["method"]?.ToString()?.ToUpper() ?? "GET";
            var body = req["body"] as JObject;
            var headers = req["headers"] as JObject;

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new ArgumentException($"Request '{id}' is missing 'endpoint'");
            }

            // Normalize endpoint (ensure it starts with /)
            if (!endpoint.StartsWith("/"))
            {
                endpoint = "/" + endpoint;
            }

            var batchReq = new JObject
            {
                ["id"] = id,
                ["method"] = method,
                ["url"] = endpoint
            };

            // Add headers if specified
            if (headers != null && headers.Count > 0)
            {
                batchReq["headers"] = headers;
            }
            else if (body != null)
            {
                // Add Content-Type for requests with body
                batchReq["headers"] = new JObject { ["Content-Type"] = "application/json" };
            }

            // Add body for write operations
            if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
            {
                batchReq["body"] = body;
            }

            batchRequests.Add(batchReq);
        }

        var batchBody = new JObject
        {
            ["requests"] = batchRequests
        };

        // Call Graph $batch endpoint
        var batchUrl = $"https://graph.microsoft.com/{apiVersion}/$batch";
        var batchResult = await SendBatchRequest(batchUrl, batchBody).ConfigureAwait(false);

        // Process responses
        var responses = batchResult["responses"] as JArray ?? new JArray();
        var processedResponses = new JArray();
        var successCount = 0;
        var errorCount = 0;

        foreach (var resp in responses)
        {
            var respId = resp["id"]?.ToString();
            var status = resp["status"]?.Value<int>() ?? 0;
            var respBody = resp["body"];

            var processedResp = new JObject
            {
                ["id"] = respId,
                ["status"] = status,
                ["success"] = status >= 200 && status < 300
            };

            if (status >= 200 && status < 300)
            {
                successCount++;
                // Summarize the response body
                if (respBody is JObject respBodyObj)
                {
                    SummarizeResponse(respBodyObj);
                    processedResp["data"] = respBodyObj;
                }
                else
                {
                    processedResp["data"] = respBody;
                }
            }
            else
            {
                errorCount++;
                processedResp["error"] = respBody?["error"] ?? respBody;
            }

            processedResponses.Add(processedResp);
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = processedResponses
        };
    }

    /// <summary>
    /// Send batch request to Graph API
    /// </summary>
    private async Task<JObject> SendBatchRequest(string url, JObject batchBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        
        // Forward OAuth token from connector
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(batchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Batch request failed with status {(int)response.StatusCode}: {content}");
        }

        return JObject.Parse(content);
    }

    #endregion

    #region Permission Error Handling

    /// <summary>
    /// Custom exception for permission errors with user-friendly messaging
    /// </summary>
    private class PermissionException : Exception
    {
        public int StatusCode { get; }
        public string ErrorCode { get; }
        public string Resource { get; }
        public string ErrorType { get; }
        public string UserMessage { get; }
        public string Action { get; }

        public PermissionException(int statusCode, string errorCode, string originalMessage, string resource)
            : base(originalMessage)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
            Resource = resource;
            
            // Determine error type and user-friendly message
            switch (statusCode)
            {
                case 401:
                    ErrorType = "session_expired";
                    UserMessage = "Your session has expired. Please reconnect to the connector.";
                    Action = "Sign out and sign back in to refresh your connection.";
                    break;
                    
                case 403:
                    ErrorType = "permission_denied";
                    UserMessage = DeterminePermissionMessage(errorCode, originalMessage, resource);
                    Action = "Contact your IT administrator to request the necessary permissions.";
                    break;
                    
                case 404:
                    ErrorType = "not_found_or_no_access";
                    UserMessage = $"The requested resource was not found, or you don't have permission to access it.";
                    Action = "Verify the resource exists and that you have been granted access by your organization.";
                    break;
                    
                default:
                    ErrorType = "access_error";
                    UserMessage = originalMessage ?? "An access error occurred.";
                    Action = "Try again or contact your IT administrator if the problem persists.";
                    break;
            }
        }

        private string DeterminePermissionMessage(string errorCode, string originalMessage, string resource)
        {
            // Extract resource type from URL for more specific messaging
            var resourceType = ExtractResourceType(resource);
            
            if (errorCode?.Contains("Authorization_RequestDenied") == true)
            {
                return $"You don't have permission to access {resourceType}. This is controlled by your organization's Entra ID settings, not this connector.";
            }
            
            if (errorCode?.Contains("AccessDenied") == true || originalMessage?.Contains("Access denied") == true)
            {
                return $"Access to {resourceType} is denied. Your organization's policies control this access.";
            }
            
            if (originalMessage?.Contains("consent") == true)
            {
                return $"Additional consent is required for {resourceType}. Your IT administrator needs to grant consent for this operation.";
            }
            
            return $"You don't have permission to access {resourceType}. This is controlled by your organization's IT policies.";
        }

        private string ExtractResourceType(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource)) return "this resource";
            
            // Extract meaningful part of the Graph URL
            var parts = resource.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var part in parts)
            {
                switch (part.ToLower())
                {
                    case "messages": return "emails";
                    case "events": return "calendar events";
                    case "users": return "user information";
                    case "groups": return "groups";
                    case "teams": return "Teams";
                    case "channels": return "Teams channels";
                    case "drive": return "files";
                    case "sites": return "SharePoint sites";
                    case "planner": return "Planner";
                    case "tasks": return "tasks";
                    case "contacts": return "contacts";
                }
            }
            
            return "this resource";
        }

        public JObject ToJson() => new JObject
        {
            ["success"] = false,
            ["errorType"] = ErrorType,
            ["userMessage"] = UserMessage,
            ["action"] = Action,
            ["technicalDetails"] = new JObject
            {
                ["httpStatus"] = StatusCode,
                ["graphError"] = ErrorCode,
                ["resource"] = Resource,
                ["originalMessage"] = Message
            }
        };
    }

    #endregion

    #region Helpers

    private string Require(JObject obj, string name)
    {
        var val = obj?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"'{name}' is required");
        return val;
    }

    private HttpResponseMessage CreateSuccessResponse(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    private HttpResponseMessage CreateErrorResponse(int code, string message, JToken id)
    {
        var json = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JObject { ["code"] = code, ["message"] = message },
            ["id"] = id
        };
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString()) };
        return resp;
    }

    #endregion

    #region Application Insights Telemetry

    private async Task LogToAppInsights(string eventName, Dictionary<string, string> properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

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
                        ["properties"] = JObject.FromObject(properties)
                    }
                }
            };

            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");
            var request = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(telemetryData.ToString(), Encoding.UTF8, "application/json")
            };

            // Fire and forget - don't await
            _ = this.Context.SendAsync(request, this.CancellationToken);
        }
        catch
        {
            // Silently ignore telemetry errors
        }
    }

    private string ExtractConnectionStringPart(string connectionString, string partName)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            var keyValue = part.Split(new[] { '=' }, 2);
            if (keyValue.Length == 2 && keyValue[0].Trim().Equals(partName, StringComparison.OrdinalIgnoreCase))
            {
                return keyValue[1].Trim();
            }
        }

        return null;
    }

    #endregion
}

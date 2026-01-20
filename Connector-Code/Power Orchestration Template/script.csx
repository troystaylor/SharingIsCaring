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
/// Power Orchestration Template - Orchestration-first server (MCP as transport) for Power Platform Custom Connectors
/// 
/// Orchestration-first Features:
/// - Discovery-first tools: discover, invoke, batch_invoke
/// - Pluggable discovery providers (MS Learn MCP, HTTP JSON search, fallback patterns)
/// - Generic HTTP invoker with connector auth pass-through and optional baseUrl
/// - MCP specification compliance (2025-11-25) - Copilot Studio compatible (transport)
/// - Application Insights telemetry (optional)
/// - Comprehensive logging with correlation IDs
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // SERVER CONFIGURATION
    // ========================================
    private const string ServerName = "power-orchestration-template";
    private const string ServerVersion = "1.0.0";
    private const string ServerTitle = "Power Orchestration Template";
    private const string ServerDescription = "Orchestration-first server (MCP transport) for discovery + invoke";
    private const string ProtocolVersion = "2025-11-25";
    private const string ServerInstructions = "";

    // ========================================
    // APPLICATION INSIGHTS
    // ========================================
    private const string APP_INSIGHTS_CONNECTION_STRING = ""; // Set connection string to enable telemetry

    // ========================================
    // DISCOVERY SETTINGS (defaults)
    // ========================================
    private const string DEFAULT_DISCOVERY_MODE = "mslearn-graph"; // mslearn-graph | http-json | custom-mcp
    private const string DEFAULT_MS_LEARN_MCP_ENDPOINT = "https://learn.microsoft.com/api/mcp";
    private const string DEFAULT_MS_LEARN_TOOL = "microsoft_docs_search";
    private const int CACHE_EXPIRY_MINUTES = 10;
    private const bool ENABLE_CACHE = false; // stateless by default

    private static readonly Dictionary<string, CacheEntry> _discoveryCache = new Dictionary<string, CacheEntry>();

    private class CacheEntry
    {
        public JObject Result { get; set; }
        public DateTime Expiry { get; set; }
    }

    // ========================================
    // TOOL NAMES
    // ========================================
    private const string TOOL_DISCOVER = "discover";
    private const string TOOL_INVOKE = "invoke";
    private const string TOOL_BATCH_INVOKE = "batch_invoke";

    // ========================================
    // MAIN ENTRY
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
            try
            {
                body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                _ = LogToAppInsights("ParseError", new { CorrelationId = correlationId, Error = "Unable to read request body" });
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
            }

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

            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method,
                HasId = requestId != null,
                Path = this.Context.Request.RequestUri.AbsolutePath
            });

            this.Context.Logger.LogInformation($"[{correlationId}] Processing MCP method: {method}");

            HttpResponseMessage response;
            switch (method)
            {
                case "initialize":
                    response = HandleInitialize(correlationId, request, requestId);
                    break;
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
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
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });
                    break;
                case "resources/templates/list":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });
                    break;
                case "resources/read":
                    response = await HandleResourcesReadAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;
                case "prompts/list":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });
                    break;
                case "prompts/get":
                    response = await HandlePromptsGetAsync(correlationId, request, requestId).ConfigureAwait(false);
                    break;
                case "completion/complete":
                    response = CreateJsonRpcSuccessResponse(requestId, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });
                    break;
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
    // MCP HANDLERS
    // ========================================
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

    private HttpResponseMessage HandleToolsList(string correlationId, JObject request, JToken requestId)
    {
        var tools = BuildToolsList();

        _ = LogToAppInsights("McpToolsListed", new
        {
            CorrelationId = correlationId,
            ToolCount = tools.Count
        });

        var result = new JObject { ["tools"] = tools };
        return CreateJsonRpcSuccessResponse(requestId, result);
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

        this.Context.Logger.LogInformation($"[{correlationId}] Executing tool: {toolName}");
        _ = LogToAppInsights("McpToolCallStarted", new { CorrelationId = correlationId, ToolName = toolName, HasArguments = arguments.Count > 0 });

        try
        {
            JObject toolResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);

            _ = LogToAppInsights("McpToolCallCompleted", new { CorrelationId = correlationId, ToolName = toolName, IsError = false });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = toolResult.ToString(Formatting.Indented) }
                },
                ["isError"] = false
            });
        }
        catch (ArgumentException ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            _ = LogToAppInsights("McpToolCallError", new { CorrelationId = correlationId, ToolName = toolName, ErrorMessage = ex.Message });
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // TOOL ROUTER
    // ========================================
    private async Task<JObject> ExecuteToolAsync(string toolName, JObject arguments)
    {
        switch (toolName.ToLowerInvariant())
        {
            case TOOL_DISCOVER:
                return await ExecuteDiscoverAsync(arguments).ConfigureAwait(false);
            case TOOL_INVOKE:
                return await ExecuteInvokeAsync(arguments).ConfigureAwait(false);
            case TOOL_BATCH_INVOKE:
                return await ExecuteBatchInvokeAsync(arguments).ConfigureAwait(false);
            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    // ========================================
    // DISCOVER IMPLEMENTATION
    // ========================================
    private async Task<JObject> ExecuteDiscoverAsync(JObject args)
    {
        var query = RequireArgument(args, "query");
        var category = args["category"]?.ToString();
        var api = args["api"]?.ToString(); // e.g. graph, custom

        var discoveryMode = GetConnectionParameter("discoveryMode") ?? DEFAULT_DISCOVERY_MODE;
        var cacheKey = $"{discoveryMode}|{api}|{query}|{category}".ToLowerInvariant();

        if (ENABLE_CACHE && _discoveryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            var cachedResult = cached.Result.DeepClone() as JObject;
            cachedResult["cached"] = true;
            return cachedResult;
        }

        JObject result;
        switch (discoveryMode.ToLowerInvariant())
        {
            case "mslearn-graph":
                result = await DiscoverViaMsLearn(query, category).ConfigureAwait(false);
                break;
            case "http-json":
                result = await DiscoverViaHttpSearch(args).ConfigureAwait(false);
                break;
            case "custom-mcp":
                result = await DiscoverViaCustomMcp(args).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException($"Unsupported discoveryMode: {discoveryMode}");
        }

        if (ENABLE_CACHE)
        {
            _discoveryCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject,
                Expiry = DateTime.UtcNow.AddMinutes(CACHE_EXPIRY_MINUTES)
            };
        }

        return result;
    }

    private async Task<JObject> DiscoverViaMsLearn(string query, string category)
    {
        var enhancedQuery = !string.IsNullOrWhiteSpace(category) ? $"Microsoft Graph {category} API {query}" : $"Microsoft Graph API {query}";

        var searchResults = await CallMcpTool(
            endpoint: DEFAULT_MS_LEARN_MCP_ENDPOINT,
            toolName: DEFAULT_MS_LEARN_TOOL,
            arguments: new JObject { ["query"] = enhancedQuery }
        ).ConfigureAwait(false);

        var operations = ExtractGraphOperations(searchResults, query);
        AddPermissionHints(operations);

        return new JObject
        {
            ["success"] = true,
            ["query"] = query,
            ["category"] = category,
            ["operationCount"] = operations.Count,
            ["operations"] = operations,
            ["tip"] = "Use invoke with the endpoint and method from the results above"
        };
    }

    private async Task<JObject> DiscoverViaHttpSearch(JObject args)
    {
        var query = RequireArgument(args, "query");
        var category = args["category"]?.ToString();

        var endpoint = GetConnectionParameter("discoveryEndpoint");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("discoveryEndpoint connection parameter is required for http-json mode");
        }

        var method = (GetConnectionParameter("discoveryHttpMethod") ?? "GET").ToUpperInvariant();
        var request = new HttpRequestMessage(new HttpMethod(method), BuildDiscoveryUrl(endpoint, method, query, category));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (method == "POST")
        {
            var body = new JObject { ["query"] = query };
            if (!string.IsNullOrWhiteSpace(category)) body["category"] = category;
            request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Discovery endpoint returned {(int)response.StatusCode}: {content}");
        }

        JObject json;
        try
        {
            json = JObject.Parse(content);
        }
        catch
        {
            throw new Exception("Discovery endpoint did not return valid JSON");
        }

        var operations = json["operations"] as JArray ?? new JArray();
        // Normalize operations shape
        var normalized = new JArray();
        foreach (var op in operations)
        {
            var o = new JObject
            {
                ["name"] = op["name"] ?? op["title"],
                ["endpoint"] = op["endpoint"] ?? op["path"],
                ["method"] = (op["method"] ?? "GET").ToString().ToUpperInvariant(),
                ["description"] = op["description"] ?? op["summary"],
                ["documentationUrl"] = op["documentationUrl"] ?? op["url"],
                ["requiredPermissions"] = op["requiredPermissions"] ?? op["permissions"],
                ["example"] = op["example"] ?? op["sample"]
            };
            normalized.Add(o);
        }

        return new JObject
        {
            ["success"] = true,
            ["query"] = query,
            ["category"] = category,
            ["operationCount"] = normalized.Count,
            ["operations"] = normalized
        };
    }

    private string BuildDiscoveryUrl(string endpoint, string method, string query, string category)
    {
        if (method != "GET") return endpoint;
        var sb = new StringBuilder(endpoint);
        sb.Append(endpoint.Contains("?") ? "&" : "?");
        sb.Append($"query={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(category))
        {
            sb.Append($"&category={Uri.EscapeDataString(category)}");
        }
        return sb.ToString();
    }

    private async Task<JObject> DiscoverViaCustomMcp(JObject args)
    {
        var query = RequireArgument(args, "query");
        var category = args["category"]?.ToString();
        var endpoint = GetConnectionParameter("discoveryEndpoint") ?? DEFAULT_MS_LEARN_MCP_ENDPOINT;
        var toolName = GetConnectionParameter("discoveryToolName") ?? DEFAULT_MS_LEARN_TOOL;

        var toolArgs = new JObject { ["query"] = query };
        if (!string.IsNullOrWhiteSpace(category)) toolArgs["category"] = category;

        var response = await CallMcpTool(endpoint, toolName, toolArgs).ConfigureAwait(false);
        // If custom MCP returns a text content result, try parse
        var ops = response["operations"] as JArray ?? TryParseOperationsFromToolResult(response);

        return new JObject
        {
            ["success"] = true,
            ["query"] = query,
            ["category"] = category,
            ["operationCount"] = ops?.Count ?? 0,
            ["operations"] = ops ?? new JArray()
        };
    }

    private JArray TryParseOperationsFromToolResult(JObject response)
    {
        // Best-effort: if response has text content, attempt to parse JSON
        var text = response["text"]?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var json = JObject.Parse(text);
            return json["operations"] as JArray;
        }
        catch { return null; }
    }

    private JArray ExtractGraphOperations(JObject searchResults, string originalQuery)
    {
        var operations = new JArray();
        var chunks = searchResults["chunks"] as JArray ?? new JArray();

        foreach (var chunk in chunks)
        {
            var title = chunk["title"]?.ToString() ?? "";
            var content = chunk["content"]?.ToString() ?? "";
            var url = chunk["url"]?.ToString() ?? "";
            if (!url.Contains("graph") && !title.ToLower().Contains("graph")) continue;
            var endpointMatches = ExtractEndpointsFromContent(content);
            foreach (var endpoint in endpointMatches)
            {
                operations.Add(new JObject
                {
                    ["endpoint"] = endpoint.Path,
                    ["method"] = endpoint.Method,
                    ["title"] = title,
                    ["documentationUrl"] = url,
                    ["description"] = TruncateDescription(content, 200)
                });
            }
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

        var seen = new HashSet<string>();
        var uniqueOps = new JArray();
        foreach (var op in operations)
        {
            var key = $"{op["endpoint"]}|{op["method"]}";
            if (string.IsNullOrWhiteSpace(op["endpoint"]?.ToString())) key = op["documentationUrl"]?.ToString() ?? op["title"]?.ToString();
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

    // Extract endpoints like `GET /me/messages` or code blocks
    private List<EndpointMatch> ExtractEndpointsFromContent(string content)
    {
        var matches = new List<EndpointMatch>();
        var patterns = new[]
        {
            @"(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)",
            @"(?:endpoint|path|url):\s*[`""']?(/[\w\{\}/\-\.]+)[`""']?",
            @"```\s*(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)"
        };
        foreach (var pattern in patterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var regexMatches = regex.Matches(content);
            foreach (System.Text.RegularExpressions.Match match in regexMatches)
            {
                if (match.Groups.Count >= 3)
                {
                    var method = match.Groups[1].Value.ToUpperInvariant();
                    var path = match.Groups[2].Value;
                    if (path.StartsWith("/") && !path.Contains("://"))
                    {
                        matches.Add(new EndpointMatch { Method = method, Path = path });
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

    private List<string> InferPermissions(string endpoint, string method)
    {
        var permissions = new List<string>();
        var isWrite = method != "GET";
        if (endpoint.Contains("/messages") || endpoint.Contains("/mailfolders") || endpoint.Contains("/sendmail"))
        {
            permissions.Add(isWrite ? "Mail.Send" : "Mail.Read");
        }
        else if (endpoint.Contains("/calendar") || endpoint.Contains("/events"))
        {
            permissions.Add(isWrite ? "Calendars.ReadWrite" : "Calendars.Read");
        }
        else if (endpoint.Contains("/users") || endpoint == "/me")
        {
            permissions.Add(isWrite ? "User.ReadWrite.All" : "User.Read.All");
        }
        else if (endpoint.Contains("/groups"))
        {
            permissions.Add(isWrite ? "Group.ReadWrite.All" : "Group.Read.All");
        }
        else if (endpoint.Contains("/teams") || endpoint.Contains("/channels"))
        {
            permissions.Add("Team.ReadBasic.All");
        }
        else if (endpoint.Contains("/drive") || endpoint.Contains("/items"))
        {
            permissions.Add(isWrite ? "Files.ReadWrite" : "Files.Read");
        }
        else if (endpoint.Contains("/sites"))
        {
            permissions.Add(isWrite ? "Sites.ReadWrite.All" : "Sites.Read.All");
        }
        else if (endpoint.Contains("/planner") || endpoint.Contains("/todo") || endpoint.Contains("/tasks"))
        {
            permissions.Add(isWrite ? "Tasks.ReadWrite" : "Tasks.Read");
        }
        return permissions;
    }

    // ========================================
    // INVOKE IMPLEMENTATION
    // ========================================
    private async Task<JObject> ExecuteInvokeAsync(JObject args)
    {
        var method = RequireArgument(args, "method").ToUpperInvariant();
        var endpoint = RequireArgument(args, "endpoint");
        var baseUrl = args["baseUrl"]?.ToString() ?? GetConnectionParameter("baseUrl");
        var queryParams = args["queryParams"] as JObject ?? new JObject();
        var headers = args["headers"] as JObject ?? new JObject();
        var body = args["body"] as JObject;
        var useConnectorAuth = args["useConnectorAuth"]?.ToObject<bool?>() ?? true;

        ValidateMethod(method);

        var url = BuildInvokeUrl(endpoint, baseUrl, queryParams, method);
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        ApplyHeaders(request, headers);

        if (useConnectorAuth && this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
        {
            request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorObj = TryParseJson(content) ?? new JObject { ["text"] = content };
            return new JObject
            {
                ["success"] = false,
                ["status"] = (int)response.StatusCode,
                ["reason"] = response.ReasonPhrase,
                ["details"] = errorObj
            };
        }

        var parsed = TryParseJson(content);
        return new JObject
        {
            ["success"] = true,
            ["status"] = (int)response.StatusCode,
            ["data"] = parsed ?? new JObject { ["text"] = content }
        };
    }

    // ========================================
    // BATCH INVOKE IMPLEMENTATION
    // ========================================
    private async Task<JObject> ExecuteBatchInvokeAsync(JObject args)
    {
        var requests = args["requests"] as JArray;
        var baseUrl = args["baseUrl"]?.ToString() ?? GetConnectionParameter("baseUrl");
        var useConnectorAuth = args["useConnectorAuth"]?.ToObject<bool?>() ?? true;

        if (requests == null || requests.Count == 0)
        {
            throw new ArgumentException("'requests' array is required and must contain at least one request");
        }
        if (requests.Count > 20)
        {
            throw new ArgumentException("Batch requests limited to 20 items.");
        }

        var responses = new JArray();
        foreach (var req in requests)
        {
            var id = req["id"]?.ToString() ?? Guid.NewGuid().ToString();
            var method = req["method"]?.ToString()?.ToUpperInvariant() ?? "GET";
            var endpoint = req["endpoint"]?.ToString();
            var headers = req["headers"] as JObject ?? new JObject();
            var body = req["body"] as JObject;

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                responses.Add(new JObject { ["id"] = id, ["success"] = false, ["error"] = "endpoint is required" });
                continue;
            }

            try
            {
                ValidateMethod(method);
                var url = BuildInvokeUrl(endpoint, baseUrl, null, method);
                var request = new HttpRequestMessage(new HttpMethod(method), url);
                ApplyHeaders(request, headers);
                if (useConnectorAuth && this.Context.Request.Headers.Authorization != null)
                {
                    request.Headers.Authorization = this.Context.Request.Headers.Authorization;
                }
                if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
                {
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                }

                var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = TryParseJson(content);

                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = response.IsSuccessStatusCode,
                    ["status"] = (int)response.StatusCode,
                    ["data"] = parsed ?? new JObject { ["text"] = content }
                });
            }
            catch (Exception ex)
            {
                responses.Add(new JObject { ["id"] = id, ["success"] = false, ["error"] = ex.Message });
            }
        }

        return new JObject
        {
            ["success"] = responses.All(r => r["success"]?.ToObject<bool>() == true),
            ["responses"] = responses
        };
    }

    // ========================================
    // HELPERS
    // ========================================
    private void ValidateMethod(string method)
    {
        var validMethods = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" };
        if (!validMethods.Contains(method))
        {
            throw new ArgumentException($"Invalid method: {method}. Must be one of: {string.Join(", ", validMethods)}");
        }
    }

    private string BuildInvokeUrl(string endpoint, string baseUrl, JObject queryParams, string method)
    {
        var url = endpoint;
        if (!IsAbsoluteUrl(endpoint))
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("baseUrl is required when endpoint is relative");
            }
            url = CombineUrl(baseUrl, endpoint);
        }

        if (queryParams != null && queryParams.Count > 0)
        {
            var sb = new StringBuilder(url);
            sb.Append(url.Contains("?") ? "&" : "?");
            var qp = string.Join("&", queryParams.Properties().Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value.ToString())}"));
            sb.Append(qp);
            url = sb.ToString();
        }
        return url;
    }

    private bool IsAbsoluteUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    private string CombineUrl(string baseUrl, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return endpoint;
        return baseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');
    }

    private void ApplyHeaders(HttpRequestMessage request, JObject headers)
    {
        foreach (var prop in headers.Properties())
        {
            request.Headers.TryAddWithoutValidation(prop.Name, prop.Value.ToString());
        }
    }

    private JObject TryParseJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            return JObject.Parse(content);
        }
        catch
        {
            return null;
        }
    }

    // ========================================
    // TOOL DEFINITIONS
    // ========================================
    private JArray BuildToolsList()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = TOOL_DISCOVER,
                ["description"] = "Discover API operations by searching documentation or custom endpoints.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject { ["type"] = "string", ["description"] = "Natural language description of the task." },
                        ["category"] = new JObject { ["type"] = "string", ["description"] = "Optional category filter" },
                        ["api"] = new JObject { ["type"] = "string", ["description"] = "API identifier (e.g., graph, custom)" }
                    },
                    ["required"] = new JArray { "query" }
                },
                ["annotations"] = new JObject { ["readOnlyHint"] = true, ["idempotentHint"] = true }
            },
            new JObject
            {
                ["name"] = TOOL_INVOKE,
                ["description"] = "Invoke an API operation with optional baseUrl, headers, and body.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["method"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" } },
                        ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "Relative path or absolute URL" },
                        ["baseUrl"] = new JObject { ["type"] = "string", ["description"] = "Base URL for relative endpoints" },
                        ["queryParams"] = new JObject { ["type"] = "object" },
                        ["headers"] = new JObject { ["type"] = "object" },
                        ["body"] = new JObject { ["type"] = "object" },
                        ["useConnectorAuth"] = new JObject { ["type"] = "boolean", ["description"] = "Forward connector auth header" }
                    },
                    ["required"] = new JArray { "method", "endpoint" }
                }
            },
            new JObject
            {
                ["name"] = TOOL_BATCH_INVOKE,
                ["description"] = "Batch invoke multiple API operations (max 20).",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["requests"] = new JObject
                        {
                            ["type"] = "array",
                            ["maxItems"] = 20,
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["id"] = new JObject { ["type"] = "string" },
                                    ["method"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" } },
                                    ["endpoint"] = new JObject { ["type"] = "string" },
                                    ["headers"] = new JObject { ["type"] = "object" },
                                    ["body"] = new JObject { ["type"] = "object" }
                                },
                                ["required"] = new JArray { "method", "endpoint" }
                            }
                        },
                        ["baseUrl"] = new JObject { ["type"] = "string" },
                        ["useConnectorAuth"] = new JObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JArray { "requests" }
                }
            }
        };
    }
}
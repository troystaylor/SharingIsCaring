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
/// Power SkillPoint — Graph Power Orchestration V2
/// Merges Graph Power Orchestration (discover, invoke, batch)
/// with a skill layer (scan, save) stored in SharePoint Embedded.
/// Skills provide behavioral guidance and guardrails;
/// discover_graph handles API discovery; invoke_graph handles execution.
/// </summary>
public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string MS_LEARN_MCP_ENDPOINT = "https://learn.microsoft.com/api/mcp";
    private const int DEFAULT_TOP_LIMIT = 25;
    private const int MAX_BODY_LENGTH = 500;
    private const int MAX_RETRIES = 3;
    private const int CACHE_EXPIRY_MINUTES = 10;

    // Tool names
    private const string TOOL_SCAN = "scan";
    private const string TOOL_SAVE = "save";
    private const string TOOL_DISCOVER = "discover_graph";
    private const string TOOL_INVOKE = "invoke_graph";
    private const string TOOL_BATCH = "batch_invoke_graph";

    private static Dictionary<string, CacheEntry> _discoveryCache = new Dictionary<string, CacheEntry>();
    private class CacheEntry { public JObject Result { get; set; } public DateTime Expiry { get; set; } }

    private Dictionary<string, Func<JObject, Task<JObject>>> _toolHandlers;

    // ── Entry point ──────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var containerId = GetConnectionParameter("containerId");

        string body;
        try { body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false); }
        catch { return CreateErrorResponse(-32700, "Unable to read request body", null); }

        JObject request;
        try { request = JObject.Parse(body); }
        catch { return CreateErrorResponse(-32700, "Invalid JSON", null); }

        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        try
        {
            switch (method)
            {
                case "initialize":
                    return CreateSuccessResponse(new JObject
                    {
                        ["protocolVersion"] = request["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                        ["capabilities"] = new JObject
                        {
                            ["tools"] = new JObject { ["listChanged"] = false },
                            ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                            ["prompts"] = new JObject { ["listChanged"] = false }
                        },
                        ["serverInfo"] = new JObject
                        {
                            ["name"] = "power-skillpoint",
                            ["version"] = "2.0.0",
                            ["description"] = "Graph Power Orchestration V2 with skill-driven guidance"
                        }
                    }, id);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateSuccessResponse(new JObject(), id);

                case "tools/list":
                    return CreateSuccessResponse(new JObject { ["tools"] = GetToolDefinitions(containerId) }, id);

                case "tools/call":
                    return await HandleToolsCall(containerId, request["params"] as JObject, id).ConfigureAwait(false);

                case "resources/list":
                    return CreateSuccessResponse(new JObject { ["resources"] = new JArray() }, id);

                case "resources/templates/list":
                    return CreateSuccessResponse(new JObject { ["resourceTemplates"] = new JArray() }, id);

                case "prompts/list":
                    return CreateSuccessResponse(new JObject { ["prompts"] = new JArray() }, id);

                default:
                    return CreateErrorResponse(-32601, $"Method not found: {method}", id);
            }
        }
        catch (PermissionException pex)
        {
            return CreateSuccessResponse(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = pex.ToJson().ToString() } },
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
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    // ── Tool definitions ─────────────────────────────────────────

    private JArray GetToolDefinitions(string containerId)
    {
        var tools = new JArray
        {
            // ── Graph Power Orchestration tools ──
            new JObject
            {
                ["name"] = TOOL_DISCOVER,
                ["description"] = "Discover Microsoft Graph API operations by searching MS Learn documentation. " +
                    "Returns relevant endpoints with methods, parameters, and required permissions. " +
                    "Use this before invoke_graph to find the right API for your task. " +
                    "Also check for skills (scan) that provide behavioral guidance for the discovered operations.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Natural language description of the Graph operation to find (e.g. 'send email', 'list calendar events')"
                        },
                        ["category"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional category: mail, calendar, teams, users, groups, files, sites, tasks, contacts"
                        }
                    },
                    ["required"] = new JArray("query")
                }
            },
            new JObject
            {
                ["name"] = TOOL_INVOKE,
                ["description"] = "Execute a Microsoft Graph API request. Use discover_graph first to find the correct endpoint. " +
                    "Apply any guidance from skills (scan) when constructing the request.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["endpoint"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Graph API path (e.g. '/me/messages', '/me/sendMail')"
                        },
                        ["method"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "HTTP method",
                            ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" }
                        },
                        ["body"] = new JObject
                        {
                            ["type"] = "object",
                            ["description"] = "Request body for POST/PATCH/PUT"
                        },
                        ["queryParams"] = new JObject
                        {
                            ["type"] = "object",
                            ["description"] = "OData query parameters ($select, $filter, $expand, $orderby, $top)"
                        },
                        ["apiVersion"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "v1.0 (default) or beta",
                            ["enum"] = new JArray { "v1.0", "beta" }
                        }
                    },
                    ["required"] = new JArray("endpoint", "method")
                }
            },
            new JObject
            {
                ["name"] = TOOL_BATCH,
                ["description"] = "Execute multiple Graph API requests in a single batch call (up to 20). " +
                    "More efficient than multiple invoke_graph calls for multi-step workflows.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["requests"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Array of Graph requests (max 20)",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique request ID" },
                                    ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "Graph API path" },
                                    ["method"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "GET", "POST", "PATCH", "PUT", "DELETE" } },
                                    ["body"] = new JObject { ["type"] = "object", ["description"] = "Request body" },
                                    ["headers"] = new JObject { ["type"] = "object", ["description"] = "Optional headers" }
                                },
                                ["required"] = new JArray("id", "endpoint", "method")
                            },
                            ["maxItems"] = 20
                        },
                        ["apiVersion"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "v1.0", "beta" }
                        }
                    },
                    ["required"] = new JArray("requests")
                }
            }
        };

        // ── Skill tools (only if containerId is configured) ──
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            tools.Add(new JObject
            {
                ["name"] = TOOL_SCAN,
                ["description"] = "Find and read a skill from the agent's skill library. " +
                    "Skills provide behavioral guidance, guardrails, org standards, and user preferences. " +
                    "Check for relevant skills before executing Graph operations to apply the right constraints.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Search terms to find relevant skill (e.g. 'email guardrails', 'troy preferences', 'cab submission format')"
                        }
                    },
                    ["required"] = new JArray("query")
                }
            });

            tools.Add(new JObject
            {
                ["name"] = TOOL_SAVE,
                ["description"] = "Save a skill file to the agent's skill library. " +
                    "Use when a user corrects your behavior and you want to remember it, " +
                    "or when creating new org standards or guardrails.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Path (e.g. 'user-skills/troy-taylor/email-style/SKILL.md')"
                        },
                        ["content"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Full SKILL.md content (YAML frontmatter + Markdown body)"
                        },
                        ["shareWith"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: email to share as read-only"
                        }
                    },
                    ["required"] = new JArray("path", "content")
                }
            });
        }

        return tools;
    }

    // ── Tool dispatch ────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(string containerId, JObject parms, JToken id)
    {
        if (parms == null) return CreateErrorResponse(-32602, "params required", id);

        var toolName = parms.Value<string>("name");
        if (string.IsNullOrWhiteSpace(toolName)) return CreateErrorResponse(-32602, "Tool name required", id);

        var arguments = parms["arguments"] as JObject ?? new JObject();

        JObject result;
        switch (toolName)
        {
            case TOOL_DISCOVER:
                result = await ExecuteDiscoverGraph(arguments).ConfigureAwait(false);
                break;
            case TOOL_INVOKE:
                result = await ExecuteInvokeGraph(arguments).ConfigureAwait(false);
                break;
            case TOOL_BATCH:
                result = await ExecuteBatchInvokeGraph(arguments).ConfigureAwait(false);
                break;
            case TOOL_SCAN:
                if (string.IsNullOrWhiteSpace(containerId))
                    throw new ArgumentException("Skills not configured. Set containerId connection parameter.");
                result = await ExecuteScan(containerId, arguments).ConfigureAwait(false);
                break;
            case TOOL_SAVE:
                if (string.IsNullOrWhiteSpace(containerId))
                    throw new ArgumentException("Skills not configured. Set containerId connection parameter.");
                result = await ExecuteSave(containerId, arguments).ConfigureAwait(false);
                break;
            default:
                return CreateErrorResponse(-32601, $"Unknown tool: {toolName}", id);
        }

        return CreateSuccessResponse(new JObject
        {
            ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString() } },
            ["isError"] = false
        }, id);
    }

    // ── discover_graph (from Graph Power Orchestration) ──────────

    private async Task<JObject> ExecuteDiscoverGraph(JObject args)
    {
        var query = Require(args, "query");
        var category = args["category"]?.ToString();

        var cacheKey = $"{query}|{category ?? ""}".ToLower();
        if (_discoveryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            var cachedResult = cached.Result.DeepClone() as JObject;
            cachedResult["cached"] = true;
            return cachedResult;
        }

        var enhancedQuery = $"Microsoft Graph API {query}";
        if (!string.IsNullOrWhiteSpace(category))
            enhancedQuery = $"Microsoft Graph {category} API {query}";

        try
        {
            var searchResults = await CallMsLearnMcp("microsoft_docs_search", new JObject
            {
                ["query"] = enhancedQuery
            }).ConfigureAwait(false);

            var graphOperations = ExtractGraphOperations(searchResults, query);
            AddPermissionHints(graphOperations);

            var result = new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["category"] = category,
                ["operationCount"] = graphOperations.Count,
                ["operations"] = graphOperations,
                ["tip"] = "Use invoke_graph with the endpoint and method above. Check scan for behavioral skills that apply."
            };

            _discoveryCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject,
                Expiry = DateTime.UtcNow.AddMinutes(CACHE_EXPIRY_MINUTES)
            };

            return result;
        }
        catch (Exception ex)
        {
            var fallback = GetFallbackOperations(query, category);
            return new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["operationCount"] = fallback.Count,
                ["operations"] = fallback,
                ["note"] = $"Results from common patterns (MS Learn MCP unavailable: {ex.Message})",
                ["tip"] = "Use invoke_graph with the endpoint and method above"
            };
        }
    }

    private async Task<JObject> CallMsLearnMcp(string toolName, JObject arguments)
    {
        var initReq = new JObject
        {
            ["jsonrpc"] = "2.0", ["method"] = "initialize", ["id"] = Guid.NewGuid().ToString(),
            ["params"] = new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "power-skillpoint", ["version"] = "2.0.0" }
            }
        };
        var initResp = await SendMcpRequest(initReq).ConfigureAwait(false);
        if (initResp["error"] != null) throw new Exception($"MS Learn MCP init failed: {initResp["error"]["message"]}");

        await SendMcpRequest(new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }).ConfigureAwait(false);

        var toolReq = new JObject
        {
            ["jsonrpc"] = "2.0", ["method"] = "tools/call", ["id"] = Guid.NewGuid().ToString(),
            ["params"] = new JObject { ["name"] = toolName, ["arguments"] = arguments }
        };
        var toolResp = await SendMcpRequest(toolReq).ConfigureAwait(false);
        if (toolResp["error"] != null) throw new Exception($"MS Learn MCP error: {toolResp["error"]["message"]}");

        var content = toolResp["result"]?["content"] as JArray;
        if (content != null && content.Count > 0)
        {
            var text = content[0]?.Value<string>("text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { return JObject.Parse(text); } catch { return new JObject { ["text"] = text }; }
            }
        }
        return toolResp["result"] as JObject ?? new JObject();
    }

    private async Task<JObject> SendMcpRequest(JObject mcpRequest)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MS_LEARN_MCP_ENDPOINT)
        {
            Content = new StringContent(mcpRequest.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"MS Learn MCP returned {response.StatusCode}: {content}");

        return JObject.Parse(content);
    }

    private JArray ExtractGraphOperations(JObject searchResults, string originalQuery)
    {
        var operations = new JArray();
        var chunks = searchResults["chunks"] as JArray ?? new JArray();

        foreach (var chunk in chunks)
        {
            var title = chunk["title"]?.ToString() ?? "";
            var chunkContent = chunk["content"]?.ToString() ?? "";
            var url = chunk["url"]?.ToString() ?? "";

            if (!url.Contains("graph") && !title.ToLower().Contains("graph")) continue;

            var endpoints = ExtractEndpointsFromContent(chunkContent);
            foreach (var ep in endpoints)
            {
                operations.Add(new JObject
                {
                    ["title"] = title,
                    ["endpoint"] = ep.Path,
                    ["method"] = ep.Method,
                    ["description"] = TruncateText(chunkContent, 200),
                    ["documentationUrl"] = url
                });
            }

            if (endpoints.Count == 0 && url.Contains("graph"))
            {
                operations.Add(new JObject
                {
                    ["title"] = title,
                    ["description"] = TruncateText(chunkContent, 200),
                    ["documentationUrl"] = url,
                    ["note"] = "See documentation for endpoint details"
                });
            }
        }

        // Deduplicate
        var seen = new HashSet<string>();
        var unique = new JArray();
        foreach (var op in operations)
        {
            var key = $"{op["endpoint"]}|{op["method"]}";
            if (string.IsNullOrWhiteSpace(op["endpoint"]?.ToString()))
                key = op["documentationUrl"]?.ToString() ?? op["title"]?.ToString();
            if (!seen.Contains(key)) { seen.Add(key); unique.Add(op); if (unique.Count >= 10) break; }
        }
        return unique;
    }

    private class EndpointMatch { public string Path { get; set; } public string Method { get; set; } }

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
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(content))
            {
                if (m.Groups.Count >= 3)
                {
                    var method = m.Groups[1].Value.ToUpper();
                    var path = m.Groups[2].Value;
                    if (path.StartsWith("/") && !path.Contains("://"))
                        matches.Add(new EndpointMatch { Path = path, Method = method });
                }
            }
        }
        return matches;
    }

    private void AddPermissionHints(JArray operations)
    {
        foreach (var op in operations)
        {
            var endpoint = op["endpoint"]?.ToString()?.ToLower() ?? "";
            var method = op["method"]?.ToString()?.ToUpper() ?? "GET";
            var perms = InferPermissions(endpoint, method);
            if (perms.Count > 0) op["requiredPermissions"] = new JArray(perms);
        }
    }

    private List<string> InferPermissions(string endpoint, string method)
    {
        var perms = new List<string>();
        var w = method != "GET";

        if (endpoint.Contains("/messages") || endpoint.Contains("/mailfolders") || endpoint.Contains("/sendmail"))
            perms.Add(w ? "Mail.Send" : "Mail.Read");
        else if (endpoint.Contains("/calendar") || endpoint.Contains("/events"))
            perms.Add(w ? "Calendars.ReadWrite" : "Calendars.Read");
        else if (endpoint.Contains("/users") || endpoint == "/me")
            perms.Add(w ? "User.ReadWrite.All" : "User.Read.All");
        else if (endpoint.Contains("/groups"))
            perms.Add(w ? "Group.ReadWrite.All" : "Group.Read.All");
        else if (endpoint.Contains("/teams") || endpoint.Contains("/channels"))
            perms.Add(endpoint.Contains("/messages") ? "ChannelMessage.Send" : "Team.ReadBasic.All");
        else if (endpoint.Contains("/drive") || endpoint.Contains("/items"))
            perms.Add(w ? "Files.ReadWrite" : "Files.Read");
        else if (endpoint.Contains("/sites"))
            perms.Add(w ? "Sites.ReadWrite.All" : "Sites.Read.All");
        else if (endpoint.Contains("/planner") || endpoint.Contains("/todo"))
            perms.Add(w ? "Tasks.ReadWrite" : "Tasks.Read");
        else if (endpoint.Contains("/contacts"))
            perms.Add(w ? "Contacts.ReadWrite" : "Contacts.Read");

        return perms;
    }

    private JArray GetFallbackOperations(string query, string category)
    {
        return new JArray
        {
            new JObject
            {
                ["note"] = "MS Learn MCP discovery unavailable. Use invoke_graph directly with common patterns.",
                ["commonPatterns"] = new JArray
                {
                    "/me - Current user", "/me/messages - Emails",
                    "/me/calendar/events - Calendar", "/me/drive/root/children - Files",
                    "/me/joinedTeams - Teams", "/users - Users", "/groups - Groups"
                },
                ["tip"] = "Add $select to limit fields, $top for count, $filter for conditions"
            }
        };
    }

    // ── invoke_graph (from Graph Power Orchestration) ─────────────

    private async Task<JObject> ExecuteInvokeGraph(JObject args)
    {
        var endpoint = Require(args, "endpoint");
        var method = (args.Value<string>("method") ?? "GET").ToUpper();
        var body = args["body"] as JObject;
        var queryParams = args["queryParams"] as JObject ?? new JObject();
        var apiVersion = args.Value<string>("apiVersion") ?? "v1.0";

        var validMethods = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" };
        if (!validMethods.Contains(method))
            throw new ArgumentException($"Invalid method: {method}");

        if (method == "GET" && IsCalendarEndpoint(endpoint))
            AddCalendarDateDefaults(endpoint, queryParams);

        ValidateEndpoint(endpoint, method);
        var url = BuildGraphUrl(endpoint, apiVersion, queryParams, method);

        var result = await SendGraphRequest(new HttpMethod(method), url, body).ConfigureAwait(false);

        var response = new JObject
        {
            ["success"] = true, ["endpoint"] = endpoint,
            ["method"] = method, ["apiVersion"] = apiVersion, ["data"] = result
        };

        if (result["@odata.nextLink"] != null)
        {
            response["hasMore"] = true;
            response["nextLink"] = result["@odata.nextLink"];
        }
        if (result["@odata.count"] != null)
            response["totalCount"] = result["@odata.count"];

        SummarizeResponse(result);
        return response;
    }

    // ── batch_invoke_graph (from Graph Power Orchestration) ───────

    private async Task<JObject> ExecuteBatchInvokeGraph(JObject args)
    {
        var requests = args["requests"] as JArray;
        var apiVersion = args.Value<string>("apiVersion") ?? "v1.0";

        if (requests == null || requests.Count == 0)
            throw new ArgumentException("'requests' array is required with at least one request");
        if (requests.Count > 20)
            throw new ArgumentException($"Batch limited to 20 items. Got {requests.Count}.");

        var batchRequests = new JArray();
        foreach (var req in requests)
        {
            var id = req.Value<string>("id") ?? Guid.NewGuid().ToString();
            var endpoint = req.Value<string>("endpoint");
            var method = (req.Value<string>("method") ?? "GET").ToUpper();
            var body = req["body"] as JObject;
            var headers = req["headers"] as JObject;

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException($"Request '{id}' missing 'endpoint'");

            if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;

            var batchReq = new JObject { ["id"] = id, ["method"] = method, ["url"] = endpoint };
            if (headers != null) batchReq["headers"] = headers;
            else if (body != null) batchReq["headers"] = new JObject { ["Content-Type"] = "application/json" };
            if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
                batchReq["body"] = body;

            batchRequests.Add(batchReq);
        }

        var batchUrl = $"https://graph.microsoft.com/{apiVersion}/$batch";
        var batchResult = await SendBatchRequest(batchUrl, new JObject { ["requests"] = batchRequests }).ConfigureAwait(false);

        var responses = batchResult["responses"] as JArray ?? new JArray();
        var processed = new JArray();
        int ok = 0, err = 0;

        foreach (var r in responses)
        {
            var status = r["status"]?.Value<int>() ?? 0;
            var p = new JObject { ["id"] = r["id"], ["status"] = status, ["success"] = status >= 200 && status < 300 };

            if (status >= 200 && status < 300) { ok++; if (r["body"] is JObject rb) { SummarizeResponse(rb); p["data"] = rb; } else p["data"] = r["body"]; }
            else { err++; p["error"] = r["body"]?["error"] ?? r["body"]; }

            processed.Add(p);
        }

        return new JObject { ["success"] = err == 0, ["batchSize"] = requests.Count, ["successCount"] = ok, ["errorCount"] = err, ["responses"] = processed };
    }

    private async Task<JObject> SendBatchRequest(string url, JObject batchBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (this.Context.Request.Headers.Authorization != null) req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(batchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new Exception($"Batch failed {(int)resp.StatusCode}: {content}");
        return JObject.Parse(content);
    }

    // ── scan (Power SkillPoint) ──────────────────────────────────

    private async Task<JObject> ExecuteScan(string containerId, JObject args)
    {
        var query = Require(args, "query");

        var searchUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/root/search(q='{Uri.EscapeDataString(query + " SKILL")}')";
        searchUrl += "?$select=id,name,parentReference&$top=5";

        var searchResult = await SendGraphRequest(HttpMethod.Get, searchUrl, null).ConfigureAwait(false);

        var items = searchResult["value"] as JArray;
        string fileId = null, fileName = null;

        if (items != null)
        {
            foreach (var item in items)
            {
                var name = item.Value<string>("name") ?? "";
                if (name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    fileId = item.Value<string>("id");
                    var parentPath = item["parentReference"]?["path"]?.ToString() ?? "";
                    fileName = parentPath.Contains("/root:")
                        ? parentPath.Substring(parentPath.IndexOf("/root:") + 6).TrimStart('/') + "/" + name
                        : name;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return new JObject
            {
                ["found"] = false,
                ["message"] = $"No skill found matching '{query}'."
            };
        }

        // Read skill content
        var readUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/items/{fileId}/content";
        var readReq = new HttpRequestMessage(HttpMethod.Get, readUrl);
        if (this.Context.Request.Headers.Authorization != null) readReq.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var readResp = await this.Context.SendAsync(readReq, this.CancellationToken).ConfigureAwait(false);
        var skillContent = await readResp.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!readResp.IsSuccessStatusCode)
            throw new Exception($"Could not read skill: {skillContent}");

        return new JObject
        {
            ["found"] = true,
            ["path"] = fileName,
            ["skill"] = skillContent
        };
    }

    // ── save (Power SkillPoint) ──────────────────────────────────

    private async Task<JObject> ExecuteSave(string containerId, JObject args)
    {
        var path = Require(args, "path").TrimStart('/');
        var content = Require(args, "content");
        var shareWith = args.Value<string>("shareWith");

        var uploadUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/root:/{path}:/content";
        var uploadReq = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/plain")
        };
        if (this.Context.Request.Headers.Authorization != null) uploadReq.Headers.Authorization = this.Context.Request.Headers.Authorization;

        var uploadResp = await this.Context.SendAsync(uploadReq, this.CancellationToken).ConfigureAwait(false);
        var uploadBody = await uploadResp.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!uploadResp.IsSuccessStatusCode)
            throw new Exception($"Failed to save skill: {uploadBody}");

        var savedItem = JObject.Parse(uploadBody);
        var itemId = savedItem.Value<string>("id");
        var message = $"Skill saved to: {path}";

        if (!string.IsNullOrWhiteSpace(shareWith) && !string.IsNullOrWhiteSpace(itemId))
        {
            try
            {
                var shareUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/items/{itemId}/invite";
                var shareBody = new JObject
                {
                    ["requireSignIn"] = true, ["sendInvitation"] = true,
                    ["roles"] = new JArray("read"),
                    ["recipients"] = new JArray { new JObject { ["email"] = shareWith } },
                    ["message"] = "Here are the preferences I've learned for you. You can review them at any time."
                };
                await SendGraphRequest(HttpMethod.Post, shareUrl, shareBody).ConfigureAwait(false);
                message += $"\nShared as read-only with {shareWith}";
            }
            catch (Exception ex)
            {
                message += $"\nSaved but sharing failed: {ex.Message}";
            }
        }

        return new JObject { ["success"] = true, ["message"] = message };
    }

    // ── Graph HTTP helpers ───────────────────────────────────────

    private bool IsCalendarEndpoint(string ep)
    {
        var l = ep.ToLower();
        return l.Contains("/calendar") || l.Contains("/events") || l.Contains("/calendarview");
    }

    private void AddCalendarDateDefaults(string endpoint, JObject qp)
    {
        if (endpoint.ToLower().Contains("/calendarview"))
        {
            if (qp["startDateTime"] == null) qp["startDateTime"] = DateTime.UtcNow.Date.ToString("o");
            if (qp["endDateTime"] == null) qp["endDateTime"] = DateTime.UtcNow.Date.AddDays(7).ToString("o");
        }
        else if (endpoint.ToLower().Contains("/events") && qp["$orderby"] == null)
        {
            qp["$orderby"] = "start/dateTime";
        }
    }

    private void ValidateEndpoint(string endpoint, string method)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(endpoint, @"\{[^}]+\}"))
        {
            var placeholders = System.Text.RegularExpressions.Regex.Matches(endpoint, @"\{[^}]+\}")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
            throw new ArgumentException($"Unresolved placeholders: {string.Join(", ", placeholders)}. Replace with actual IDs.");
        }
        if (endpoint.Contains("//")) throw new ArgumentException($"Double slashes in endpoint: {endpoint}");
        if (endpoint.StartsWith("beta/") || endpoint.StartsWith("v1.0/"))
            throw new ArgumentException("Don't include version prefix. Use apiVersion parameter.");
        if (method == "DELETE" && (endpoint.EndsWith("/messages") || endpoint.EndsWith("/events") || endpoint.EndsWith("/users")))
            throw new ArgumentException($"Cannot DELETE a collection. Specify item ID: {endpoint}/{{id}}");
    }

    private string BuildGraphUrl(string endpoint, string apiVersion, JObject queryParams, string method)
    {
        endpoint = endpoint.TrimStart('/');
        var url = $"https://graph.microsoft.com/{apiVersion}/{endpoint}";
        var parts = new List<string>();
        var hasTop = false;

        if (queryParams != null)
        {
            foreach (var p in queryParams.Properties())
            {
                var key = p.Name.StartsWith("$") ? p.Name : $"${p.Name}";
                if (key == "$top") hasTop = true;
                parts.Add($"{key}={Uri.EscapeDataString(p.Value.ToString())}");
            }
        }

        if (method == "GET" && IsCollectionEndpoint(endpoint) && !hasTop)
            parts.Add($"$top={DEFAULT_TOP_LIMIT}");

        if (parts.Count > 0) url += "?" + string.Join("&", parts);
        return url;
    }

    private bool IsCollectionEndpoint(string ep)
    {
        var patterns = new[] { "/messages", "/events", "/users", "/groups", "/teams",
            "/channels", "/members", "/children", "/items", "/lists",
            "/tasks", "/contacts", "/calendars", "/drives", "/sites" };
        var l = ep.ToLower();
        return patterns.Any(p => l.EndsWith(p) || l.Contains(p + "?"));
    }

    private async Task<JObject> SendGraphRequest(HttpMethod method, string url, JObject body, int retry = 0)
    {
        var req = new HttpRequestMessage(method, url);
        if (this.Context.Request.Headers.Authorization != null) req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (resp.StatusCode == (HttpStatusCode)429 && retry < MAX_RETRIES)
        {
            var wait = 5;
            if (resp.Headers.TryGetValues("Retry-After", out var v) && int.TryParse(v.FirstOrDefault(), out var s))
                wait = Math.Min(s, 30);
            await Task.Delay(wait * 1000, this.CancellationToken).ConfigureAwait(false);
            return await SendGraphRequest(method, url, body, retry + 1).ConfigureAwait(false);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var code = (int)resp.StatusCode;
            string errCode = null, errMsg = null;
            try { var e = JObject.Parse(content); errCode = e["error"]?.Value<string>("code"); errMsg = e["error"]?.Value<string>("message"); }
            catch { errMsg = content; }

            if (code == 401 || code == 403 || code == 404)
                throw new PermissionException(code, errCode, errMsg, url);
            throw new InvalidOperationException($"Graph {code}: {errCode ?? ""} - {errMsg ?? content}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)resp.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    // ── Response summarization ───────────────────────────────────

    private void SummarizeResponse(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                if (prop.Name.ToLower() == "body" && prop.Value is JObject b)
                {
                    var c = b["content"]?.ToString();
                    if (!string.IsNullOrEmpty(c) && c.Length > MAX_BODY_LENGTH)
                    {
                        var plain = StripHtml(c);
                        b["content"] = plain.Length > MAX_BODY_LENGTH ? plain.Substring(0, MAX_BODY_LENGTH) + "... [truncated]" : plain;
                        b["contentType"] = "text";
                    }
                }
                else if (prop.Value is JObject || prop.Value is JArray) SummarizeResponse(prop.Value);
            }
        }
        else if (token is JArray arr) { foreach (var i in arr) SummarizeResponse(i); }
    }

    private string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
        return System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").Trim();
    }

    // ── Permission error handling ─────────────────────────────────

    private class PermissionException : Exception
    {
        public int StatusCode { get; }
        public string ErrorCode { get; }
        public string Resource { get; }
        public string UserMessage { get; }
        public string Action { get; }

        public PermissionException(int statusCode, string errorCode, string message, string resource) : base(message)
        {
            StatusCode = statusCode; ErrorCode = errorCode; Resource = resource;
            var resType = ExtractResourceType(resource);

            switch (statusCode)
            {
                case 401: UserMessage = "Session expired. Please reconnect."; Action = "Sign out and sign back in."; break;
                case 403:
                    UserMessage = errorCode?.Contains("Authorization_RequestDenied") == true
                        ? $"No permission to access {resType}. Controlled by your organization's Entra ID settings."
                        : $"Access to {resType} denied by organizational policies.";
                    Action = "Contact your IT administrator."; break;
                case 404: UserMessage = $"Resource not found or no permission to view it."; Action = "Verify the ID is correct."; break;
                default: UserMessage = message ?? "An access error occurred."; Action = "Try again or contact IT."; break;
            }
        }

        private static string ExtractResourceType(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource)) return "this resource";
            foreach (var p in resource.Split('/'))
            {
                switch (p.ToLower())
                {
                    case "messages": return "emails"; case "events": return "calendar events";
                    case "users": return "user information"; case "teams": return "Teams";
                    case "channels": return "Teams channels"; case "drive": return "files";
                    case "sites": return "SharePoint sites"; case "tasks": return "tasks";
                    case "contacts": return "contacts";
                }
            }
            return "this resource";
        }

        public JObject ToJson() => new JObject
        {
            ["success"] = false, ["errorType"] = StatusCode == 401 ? "session_expired" : StatusCode == 403 ? "permission_denied" : "not_found_or_no_access",
            ["userMessage"] = UserMessage, ["action"] = Action,
            ["technicalDetails"] = new JObject { ["httpStatus"] = StatusCode, ["graphError"] = ErrorCode, ["resource"] = Resource, ["originalMessage"] = Message }
        };
    }

    // ── Helpers ──────────────────────────────────────────────────

    private string Require(JObject obj, string name)
    {
        var val = obj?.Value<string>(name);
        if (string.IsNullOrWhiteSpace(val)) throw new ArgumentException($"'{name}' is required");
        return val;
    }

    private string TruncateText(string text, int max) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Length <= max ? text : text.Substring(0, max - 3) + "...";

    private HttpResponseMessage CreateSuccessResponse(JObject result, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString(Newtonsoft.Json.Formatting.None)) };
    }

    private HttpResponseMessage CreateErrorResponse(int code, string message, JToken id)
    {
        var json = new JObject { ["jsonrpc"] = "2.0", ["error"] = new JObject { ["code"] = code, ["message"] = message }, ["id"] = id };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent(json.ToString(Newtonsoft.Json.Formatting.None)) };
    }

    private string GetConnectionParameter(string name)
    {
        try { return this.Context.ConnectionParameters[name]?.ToString(); } catch { return null; }
    }

    private async Task LogToAppInsights(string eventName, Dictionary<string, string> props)
    {
        try
        {
            var iKey = ExtractConnStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var endpoint = ExtractConnStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint") ?? "https://dc.services.visualstudio.com/";
            if (string.IsNullOrEmpty(iKey)) return;

            var data = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{iKey}.Event", ["time"] = DateTime.UtcNow.ToString("o"), ["iKey"] = iKey,
                ["data"] = new JObject { ["baseType"] = "EventData", ["baseData"] = new JObject { ["ver"] = 2, ["name"] = eventName, ["properties"] = JObject.FromObject(props) } }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.TrimEnd('/') + "/v2/track"))
            { Content = new StringContent(data.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json") };
            _ = this.Context.SendAsync(req, this.CancellationToken);
        }
        catch { }
    }

    private string ExtractConnStringPart(string cs, string part)
    {
        if (string.IsNullOrWhiteSpace(cs)) return null;
        foreach (var s in cs.Split(';')) { var kv = s.Split(new[] { '=' }, 2); if (kv.Length == 2 && kv[0].Trim().Equals(part, StringComparison.OrdinalIgnoreCase)) return kv[1].Trim(); }
        return null;
    }
}
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
/// Power SkillPoint — skill-driven MCP connector.
/// Skills stored in SharePoint Embedded (not discoverable, shareable on demand).
/// Graph execution logic derived from Graph Power Orchestration.
/// Tools: scan (find + read skill), execute (call Graph API), save (write skill)
/// </summary>
public class Script : ScriptBase
{
    private const string ServerName = "PowerSkillPoint";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-12-01";
    private const int DEFAULT_TOP_LIMIT = 25;
    private const int MAX_BODY_LENGTH = 500;
    private const int MAX_RETRIES = 3;

    private static bool _isInitialized = false;

    // ── Entry point ──────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var containerId = GetConnectionParameter("containerId");
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return JsonRpcError(null, -32602, "Invalid params", "Missing connection parameter: containerId");
        }

        string body;
        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return JsonRpcError(null, -32700, "Parse error", "Unable to read request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return JsonRpcError(null, -32700, "Parse error", "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var requestId = request["id"];

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(requestId);

                case "notifications/initialized":
                    _isInitialized = true;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };

                case "tools/list":
                    return HandleToolsList(requestId);

                case "tools/call":
                    return await HandleToolsCall(containerId, request, requestId).ConfigureAwait(false);

                default:
                    return JsonRpcError(requestId, -32601, "Method not found", method);
            }
        }
        catch (PermissionException pex)
        {
            return ToolResult(requestId, pex.UserFriendlyMessage, true);
        }
        catch (Exception ex)
        {
            return ToolResult(requestId, $"Error: {ex.Message}", true);
        }
    }

    // ── Initialize ───────────────────────────────────────────────

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        _isInitialized = true;
        return JsonRpcSuccess(requestId, new JObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion
            }
        });
    }

    // ── Tools list ───────────────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        if (!_isInitialized)
        {
            return JsonRpcError(requestId, -32002, "Server not initialized", "Call initialize first");
        }

        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "scan",
                ["description"] = "Find and read a skill from the agent's skill library. " +
                    "Returns SKILL.md content that teaches you which Graph API endpoints to call. " +
                    "Use before executing any task to learn how to do it.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Search terms to find the right skill (e.g. 'email send', 'calendar meeting', 'weekly summary troy')"
                        }
                    },
                    ["required"] = new JArray("query")
                }
            },
            new JObject
            {
                ["name"] = "execute",
                ["description"] = "Execute a Microsoft Graph API request. " +
                    "The skill tells you which endpoint, method, and arguments to use. " +
                    "Supports all Graph v1.0 and beta endpoints with delegated permissions.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["endpoint"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Graph API endpoint path (e.g. '/me/messages', '/me/sendMail')"
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
                            ["description"] = "OData query parameters ($select, $filter, $expand, $orderby, $top)"
                        },
                        ["apiVersion"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "API version: v1.0 (default) or beta",
                            ["enum"] = new JArray { "v1.0", "beta" }
                        }
                    },
                    ["required"] = new JArray("endpoint", "method")
                }
            },
            new JObject
            {
                ["name"] = "save",
                ["description"] = "Save a skill file to the agent's skill library. " +
                    "Use when a user corrects your behavior and you want to remember it, " +
                    "or when creating a new org skill.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["path"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Path within the skills container (e.g. 'user-skills/troy-taylor/weekly-summary/SKILL.md')"
                        },
                        ["content"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Full SKILL.md content (YAML frontmatter + Markdown body)"
                        },
                        ["shareWith"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional: email address to share the saved skill with as read-only"
                        }
                    },
                    ["required"] = new JArray("path", "content")
                }
            }
        };

        return JsonRpcSuccess(requestId, new JObject { ["tools"] = tools });
    }

    // ── Tools call router ────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(string containerId, JObject request, JToken requestId)
    {
        if (!_isInitialized)
        {
            return JsonRpcError(requestId, -32002, "Server not initialized", "Call initialize first");
        }

        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return JsonRpcError(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        switch (toolName)
        {
            case "scan":
                return await HandleScan(containerId, arguments, requestId).ConfigureAwait(false);

            case "execute":
                return await HandleExecute(arguments, requestId).ConfigureAwait(false);

            case "save":
                return await HandleSave(containerId, arguments, requestId).ConfigureAwait(false);

            default:
                return JsonRpcError(requestId, -32601, "Method not found", $"Unknown tool: {toolName}");
        }
    }

    // ── Scan: find and read a skill from SharePoint Embedded ─────

    private async Task<HttpResponseMessage> HandleScan(string containerId, JObject arguments, JToken requestId)
    {
        var query = arguments.Value<string>("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonRpcError(requestId, -32602, "Invalid params", "query is required");
        }

        // Search within the SPE container's drive for SKILL.md files
        var searchUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/root/search(q='{Uri.EscapeDataString(query + " SKILL")}')";
        searchUrl += "?$select=id,name,parentReference&$top=5";

        JObject searchResult;
        try
        {
            searchResult = await SendGraphRequest(HttpMethod.Get, searchUrl, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResult(requestId, $"Skill search failed: {ex.Message}", true);
        }

        // Find first SKILL.md in results
        var items = searchResult["value"] as JArray;
        string fileId = null;
        string fileName = null;

        if (items != null)
        {
            foreach (var item in items)
            {
                var name = item.Value<string>("name") ?? "";
                if (name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    fileId = item.Value<string>("id");
                    var parentPath = item["parentReference"]?["path"]?.ToString() ?? "";
                    fileName = parentPath.Contains("/root:") 
                        ? parentPath.Substring(parentPath.IndexOf("/root:") + 6).TrimStart('/') + "/" + name
                        : name;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return ToolResult(requestId, $"No skill found matching '{query}'. The agent may not have a skill for this task yet.", false);
        }

        // Read the skill file content
        var readUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/items/{fileId}/content";

        try
        {
            var readRequest = new HttpRequestMessage(HttpMethod.Get, readUrl);
            if (this.Context.Request.Headers.Authorization != null)
            {
                readRequest.Headers.Authorization = this.Context.Request.Headers.Authorization;
            }

            var response = await this.Context.SendAsync(readRequest, this.CancellationToken).ConfigureAwait(false);
            var skillContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return ToolResult(requestId, $"Could not read skill file: {skillContent}", true);
            }

            var resultText = $"**Skill: {fileName}**\n\n{skillContent}";
            return ToolResult(requestId, resultText, false);
        }
        catch (Exception ex)
        {
            return ToolResult(requestId, $"Skill read failed: {ex.Message}", true);
        }
    }

    // ── Execute: call Microsoft Graph API ─────────────────────────
    // (derived from Graph Power Orchestration invoke_graph)

    private async Task<HttpResponseMessage> HandleExecute(JObject arguments, JToken requestId)
    {
        var endpoint = arguments.Value<string>("endpoint");
        var method = (arguments.Value<string>("method") ?? "GET").ToUpper();
        var body = arguments["body"] as JObject;
        var queryParams = arguments["queryParams"] as JObject ?? new JObject();
        var apiVersion = arguments.Value<string>("apiVersion") ?? "v1.0";

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return JsonRpcError(requestId, -32602, "Invalid params", "endpoint is required");
        }

        var validMethods = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" };
        if (!validMethods.Contains(method))
        {
            return ToolResult(requestId, $"Invalid method: {method}. Must be one of: {string.Join(", ", validMethods)}", true);
        }

        // Calendar smart defaults
        if (method == "GET" && IsCalendarEndpoint(endpoint))
        {
            AddCalendarDateDefaults(endpoint, queryParams);
        }

        // Validate endpoint
        ValidateEndpoint(endpoint, method);

        // Build URL with auto $top for collections
        var url = BuildGraphUrl(endpoint, apiVersion, queryParams, method);

        // Execute
        var result = await SendGraphRequest(new HttpMethod(method), url, body).ConfigureAwait(false);

        // Build response with pagination hints
        var response = new JObject
        {
            ["success"] = true,
            ["endpoint"] = endpoint,
            ["method"] = method,
            ["data"] = result
        };

        if (result["@odata.nextLink"] != null)
        {
            response["hasMore"] = true;
            response["nextLink"] = result["@odata.nextLink"];
        }

        SummarizeResponse(result);

        return ToolResult(requestId, response.ToString(Newtonsoft.Json.Formatting.None), false);
    }

    // ── Save: write a skill to SharePoint Embedded ───────────────

    private async Task<HttpResponseMessage> HandleSave(string containerId, JObject arguments, JToken requestId)
    {
        var path = arguments.Value<string>("path");
        var content = arguments.Value<string>("content");
        var shareWith = arguments.Value<string>("shareWith");

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(content))
        {
            return JsonRpcError(requestId, -32602, "Invalid params", "path and content are required");
        }

        // Normalize path
        path = path.TrimStart('/');

        // Upload file to SPE container
        var uploadUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/root:/{path}:/content";

        var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/plain")
        };

        if (this.Context.Request.Headers.Authorization != null)
        {
            uploadRequest.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        var uploadResponse = await this.Context.SendAsync(uploadRequest, this.CancellationToken).ConfigureAwait(false);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!uploadResponse.IsSuccessStatusCode)
        {
            return ToolResult(requestId, $"Failed to save skill: {uploadBody}", true);
        }

        var savedItem = JObject.Parse(uploadBody);
        var itemId = savedItem.Value<string>("id");
        var resultMessage = $"Skill saved to: {path}";

        // Share with user if requested
        if (!string.IsNullOrWhiteSpace(shareWith) && !string.IsNullOrWhiteSpace(itemId))
        {
            try
            {
                var shareUrl = $"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/drive/items/{itemId}/invite";
                var shareBody = new JObject
                {
                    ["requireSignIn"] = true,
                    ["sendInvitation"] = true,
                    ["roles"] = new JArray("read"),
                    ["recipients"] = new JArray
                    {
                        new JObject
                        {
                            ["email"] = shareWith
                        }
                    },
                    ["message"] = "Here are the preferences I've learned for you. You can review them at any time."
                };

                await SendGraphRequest(HttpMethod.Post, shareUrl, shareBody).ConfigureAwait(false);
                resultMessage += $"\nShared as read-only with {shareWith}";
            }
            catch (Exception ex)
            {
                resultMessage += $"\nSaved successfully but sharing failed: {ex.Message}";
            }
        }

        return ToolResult(requestId, resultMessage, false);
    }

    // ── Graph API helpers (from Graph Power Orchestration) ────────

    private bool IsCalendarEndpoint(string endpoint)
    {
        var lower = endpoint.ToLower();
        return lower.Contains("/calendar") || lower.Contains("/events") || lower.Contains("/calendarview");
    }

    private void AddCalendarDateDefaults(string endpoint, JObject queryParams)
    {
        var lower = endpoint.ToLower();
        if (lower.Contains("/calendarview"))
        {
            if (queryParams["startDateTime"] == null)
            {
                queryParams["startDateTime"] = DateTime.UtcNow.Date.ToString("o");
            }
            if (queryParams["endDateTime"] == null)
            {
                queryParams["endDateTime"] = DateTime.UtcNow.Date.AddDays(7).ToString("o");
            }
        }
        else if (lower.Contains("/events") && queryParams["$orderby"] == null)
        {
            queryParams["$orderby"] = "start/dateTime";
        }
    }

    private void ValidateEndpoint(string endpoint, string method)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(endpoint, @"\{[^}]+\}"))
        {
            var placeholders = System.Text.RegularExpressions.Regex.Matches(endpoint, @"\{[^}]+\}")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .ToList();
            throw new ArgumentException($"Endpoint contains unresolved placeholders: {string.Join(", ", placeholders)}. Replace with actual IDs.");
        }

        if (endpoint.Contains("//"))
            throw new ArgumentException($"Endpoint contains double slashes: {endpoint}");

        if (endpoint.StartsWith("beta/") || endpoint.StartsWith("v1.0/"))
            throw new ArgumentException("Don't include version prefix in endpoint. Use apiVersion parameter.");
    }

    private string BuildGraphUrl(string endpoint, string apiVersion, JObject queryParams, string method)
    {
        endpoint = endpoint.TrimStart('/');
        var url = $"https://graph.microsoft.com/{apiVersion}/{endpoint}";

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

        // Auto-add $top for GET on collection endpoints
        if (method == "GET" && IsCollectionEndpoint(endpoint) && !hasTop)
        {
            queryParts.Add($"$top={DEFAULT_TOP_LIMIT}");
        }

        if (queryParts.Count > 0)
        {
            url += "?" + string.Join("&", queryParts);
        }

        return url;
    }

    private bool IsCollectionEndpoint(string endpoint)
    {
        var patterns = new[] {
            "/messages", "/events", "/users", "/groups", "/teams",
            "/channels", "/members", "/children", "/items", "/lists",
            "/tasks", "/contacts", "/calendars", "/drives", "/sites"
        };
        var lower = endpoint.ToLower();
        return patterns.Any(p => lower.EndsWith(p) || lower.Contains(p + "?"));
    }

    private async Task<JObject> SendGraphRequest(HttpMethod method, string url, JObject body, int retryCount = 0)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Handle throttling with retry
        if (response.StatusCode == (HttpStatusCode)429 && retryCount < MAX_RETRIES)
        {
            var retryAfter = 5;
            if (response.Headers.TryGetValues("Retry-After", out var vals))
            {
                if (int.TryParse(vals.FirstOrDefault(), out var s))
                    retryAfter = Math.Min(s, 30);
            }
            await Task.Delay(retryAfter * 1000, this.CancellationToken).ConfigureAwait(false);
            return await SendGraphRequest(method, url, body, retryCount + 1).ConfigureAwait(false);
        }

        // Handle errors
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            string errorCode = null, errorMessage = null;

            try
            {
                var errBody = JObject.Parse(content);
                errorCode = errBody["error"]?.Value<string>("code");
                errorMessage = errBody["error"]?.Value<string>("message");
            }
            catch { errorMessage = content; }

            if (statusCode == 401 || statusCode == 403 || statusCode == 404)
            {
                throw new PermissionException(statusCode, errorCode, errorMessage, url);
            }

            throw new InvalidOperationException(
                $"Graph returned {statusCode}: {errorCode ?? ""} - {errorMessage ?? content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };
        }

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    // ── Response summarization ───────────────────────────────────

    private void SummarizeResponse(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                var name = prop.Name.ToLower();
                if (name == "body" && prop.Value is JObject bodyObj)
                {
                    var bodyContent = bodyObj["content"]?.ToString();
                    if (!string.IsNullOrEmpty(bodyContent) && bodyContent.Length > MAX_BODY_LENGTH)
                    {
                        bodyObj["content"] = StripHtml(bodyContent).Substring(0, Math.Min(StripHtml(bodyContent).Length, MAX_BODY_LENGTH)) + "... [truncated]";
                        bodyObj["contentType"] = "text";
                    }
                }
                else if (prop.Value is JObject || prop.Value is JArray)
                {
                    SummarizeResponse(prop.Value);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr) SummarizeResponse(item);
        }
    }

    private string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }

    // ── Permission error handling ─────────────────────────────────

    private class PermissionException : Exception
    {
        public string UserFriendlyMessage { get; }

        public PermissionException(int statusCode, string errorCode, string message, string url)
            : base(message)
        {
            if (statusCode == 401)
            {
                UserFriendlyMessage = "Authentication expired. Please reconnect the connector.";
            }
            else if (statusCode == 403)
            {
                UserFriendlyMessage = $"Permission denied for this operation. " +
                    $"The connector may need additional Graph API scopes. " +
                    $"Error: {errorCode ?? "Forbidden"} - {message}";
            }
            else if (statusCode == 404)
            {
                UserFriendlyMessage = $"Resource not found (or no permission to view it). " +
                    $"Check that the ID is correct. Error: {message}";
            }
            else
            {
                UserFriendlyMessage = $"Error {statusCode}: {message}";
            }
        }
    }

    // ── JSON-RPC response builders ───────────────────────────────

    private HttpResponseMessage ToolResult(JToken requestId, string text, bool isError)
    {
        var result = new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        };

        return JsonRpcSuccess(requestId, result);
    }

    private HttpResponseMessage JsonRpcSuccess(JToken id, JObject result)
    {
        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseObj.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage JsonRpcError(JToken id, int code, string message, string data = null)
    {
        var errorObj = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (!string.IsNullOrWhiteSpace(data))
            errorObj["data"] = data;

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = errorObj
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseObj.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json")
        };
    }

    // ── Helpers ──────────────────────────────────────────────────

    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch { return null; }
    }
}

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
    // App Insights telemetry
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string CONNECTOR_NAME = "ARD Discovery";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    return await HandleMcpRequestAsync().ConfigureAwait(false);

                case "SearchCapabilities":
                    return await HandleSearchAsync().ConfigureAwait(false);

                case "InvokeCapability":
                    return await HandleProxyAsync().ConfigureAwait(false);

                default:
                    // ExploreRegistry, ListAgents — pass through to backend
                    return await ForwardToBackendAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogToAppInsights("Error", new Dictionary<string, string>
            {
                { "operation", this.Context.OperationId },
                { "error", ex.Message }
            });
            throw;
        }
    }

    // ========================================================================
    // MCP Handler — Copilot Studio entry point
    // ========================================================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var request = JObject.Parse(body);

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        LogToAppInsights("MCP_Request", new Dictionary<string, string>
        {
            { "method", method ?? "null" }
        });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCallAsync(@params, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2025-03-26",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["elicitation"] = new JObject {}
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "ard-discovery",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "search_capabilities",
                ["description"] = "Search an ARD registry for agentic resources (MCP servers, A2A agents, skills) matching a natural language query. Returns ranked results with relevance scores, identifiers, and invocation URLs.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Natural language description of the capability needed (e.g., 'weather forecast tool', 'flight booking agent')."
                        },
                        ["type_filter"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Filter by artifact type: 'mcp' for MCP servers, 'a2a' for A2A agents, 'skill' for skills. Leave empty for all types."
                        },
                        ["tags"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Comma-separated tags to filter by."
                        },
                        ["federation"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Federation mode: 'auto' (default, merged), 'referrals' (results + registry links), 'none' (local only)."
                        }
                    },
                    ["required"] = new JArray { "text" }
                }
            },
            new JObject
            {
                ["name"] = "explore_registry",
                ["description"] = "Explore an ARD registry to see facet breakdowns — what types of resources exist, which publishers, etc. Useful for understanding what's available before searching.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["field"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Field to aggregate over: 'type', 'publisher', 'tags'."
                        },
                        ["text"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Natural language query to narrow the aggregation."
                        }
                    },
                    ["required"] = new JArray { "field" }
                }
            },
            new JObject
            {
                ["name"] = "invoke_capability",
                ["description"] = "Invoke a discovered MCP capability by proxying a request to its endpoint. If the target requires authentication, you will be prompted to sign in via a secure link (MCP elicitation). Use the URL from search_capabilities results.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["target_url"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The URL of the discovered MCP endpoint (from search results)."
                        },
                        ["method"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "MCP method to call: 'tools/list' to discover tools, 'tools/call' to invoke a tool."
                        },
                        ["tool_name"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "When method is 'tools/call', the name of the tool to invoke."
                        },
                        ["arguments"] = new JObject
                        {
                            ["type"] = "object",
                            ["description"] = "When method is 'tools/call', the arguments to pass to the tool."
                        }
                    },
                    ["required"] = new JArray { "target_url", "method" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken requestId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        try
        {
            switch (toolName)
            {
                case "search_capabilities":
                    return await ExecuteSearchToolAsync(arguments, requestId).ConfigureAwait(false);

                case "explore_registry":
                    return await ExecuteExploreToolAsync(arguments, requestId).ConfigureAwait(false);

                case "invoke_capability":
                    return await ExecuteInvokeToolAsync(arguments, requestId).ConfigureAwait(false);

                default:
                    return CreateToolErrorResponse(requestId, $"Unknown tool: {toolName}");
            }
        }
        catch (Exception ex)
        {
            LogToAppInsights("ToolError", new Dictionary<string, string>
            {
                { "tool", toolName ?? "null" },
                { "error", ex.Message }
            });
            return CreateToolErrorResponse(requestId, $"Tool execution failed: {ex.Message}");
        }
    }

    // ========================================================================
    // MCP Tool Implementations
    // ========================================================================

    private async Task<HttpResponseMessage> ExecuteSearchToolAsync(JObject arguments, JToken requestId)
    {
        var searchBody = BuildSearchPayload(arguments);
        var response = await SendToBackendAsync("/search", HttpMethod.Post, searchBody).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return CreateToolSuccessResponse(requestId, content);
    }

    private async Task<HttpResponseMessage> ExecuteExploreToolAsync(JObject arguments, JToken requestId)
    {
        var field = arguments["field"]?.ToString() ?? "type";
        var text = arguments["text"]?.ToString();

        var exploreBody = new JObject
        {
            ["resultType"] = new JObject
            {
                ["facets"] = new JArray
                {
                    new JObject
                    {
                        ["field"] = field,
                        ["limit"] = 20
                    }
                }
            }
        };

        if (!string.IsNullOrEmpty(text))
        {
            exploreBody["query"] = new JObject { ["text"] = text };
        }

        var response = await SendToBackendAsync("/explore", HttpMethod.Post, exploreBody).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return CreateToolSuccessResponse(requestId, content);
    }

    private async Task<HttpResponseMessage> ExecuteInvokeToolAsync(JObject arguments, JToken requestId)
    {
        var targetUrl = arguments["target_url"]?.ToString();
        var method = arguments["method"]?.ToString();
        var toolName = arguments["tool_name"]?.ToString();
        var toolArgs = arguments["arguments"] as JObject;

        if (string.IsNullOrEmpty(targetUrl) || string.IsNullOrEmpty(method))
        {
            return CreateToolErrorResponse(requestId, "target_url and method are required.");
        }

        // Build proxy payload and send to backend
        var proxyBody = new JObject
        {
            ["targetUrl"] = targetUrl,
            ["mcpRequest"] = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["id"] = 1
            }
        };

        if (method == "tools/call" && !string.IsNullOrEmpty(toolName))
        {
            proxyBody["mcpRequest"]["params"] = new JObject
            {
                ["name"] = toolName,
                ["arguments"] = toolArgs ?? new JObject()
            };
        }

        var response = await SendToBackendAsync("/proxy", HttpMethod.Post, proxyBody).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Handle HTTP errors from backend (Tier 3 auth failure with actionable message)
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var errorObj = JObject.Parse(responseBody);
                var errorMsg = errorObj["error"]?.ToString() ?? $"Proxy returned {(int)response.StatusCode}";
                return CreateToolErrorResponse(requestId, errorMsg);
            }
            catch
            {
                return CreateToolErrorResponse(requestId, $"Proxy returned HTTP {(int)response.StatusCode}");
            }
        }

        var parsed = JObject.Parse(responseBody);

        // Check if backend returned an elicitation request (auth needed, feature-flagged)
        if (parsed["elicitation"] != null)
        {
            var elicitation = parsed["elicitation"] as JObject;
            return HandleElicitationResponse(requestId, elicitation);
        }

        return CreateToolSuccessResponse(requestId, responseBody);
    }

    /// <summary>
    /// When the backend signals that auth is needed for a target domain,
    /// return an InputRequiredResult with a URL mode elicitation request.
    /// The user will be redirected to a secure page to authenticate.
    /// </summary>
    private HttpResponseMessage HandleElicitationResponse(JToken requestId, JObject elicitation)
    {
        var authUrl = elicitation["url"]?.ToString();
        var message = elicitation["message"]?.ToString() ?? "Authentication required for the discovered service.";
        var requestState = elicitation["requestState"]?.ToString();

        // Return InputRequiredResult per MCP elicitation spec
        var result = new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = message
                }
            },
            ["isError"] = false
        };

        // Attach elicitation input request
        var inputRequest = new JObject
        {
            ["method"] = "elicitation/create",
            ["params"] = new JObject
            {
                ["mode"] = "url",
                ["url"] = authUrl,
                ["message"] = message
            }
        };

        var responseObj = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = requestId
        };

        // Add inputRequests at the response level for MRTR pattern
        responseObj["inputRequests"] = new JArray { inputRequest };

        if (!string.IsNullOrEmpty(requestState))
        {
            responseObj["requestState"] = requestState;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseObj.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    // ========================================================================
    // Typed Operation Handlers (Power Automate)
    // ========================================================================

    private async Task<HttpResponseMessage> HandleSearchAsync()
    {
        // Pass through to backend, injecting registry URL from connection params
        return await ForwardToBackendAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleProxyAsync()
    {
        // Read the proxy request to validate and log
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var payload = JObject.Parse(body);

        var targetUrl = payload["targetUrl"]?.ToString();
        if (string.IsNullOrEmpty(targetUrl))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"targetUrl is required\"}",
                    Encoding.UTF8,
                    "application/json"
                )
            };
        }

        // Validate target URL is HTTPS
        if (!targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":\"targetUrl must use HTTPS\"}",
                    Encoding.UTF8,
                    "application/json"
                )
            };
        }

        LogToAppInsights("ProxyCall", new Dictionary<string, string>
        {
            { "targetUrl", targetUrl }
        });

        return await ForwardToBackendAsync().ConfigureAwait(false);
    }

    // ========================================================================
    // Backend Communication
    // ========================================================================

    private async Task<HttpResponseMessage> ForwardToBackendAsync()
    {
        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendToBackendAsync(string path, HttpMethod method, JObject body)
    {
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Content = new StringContent(
            body.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        // Forward auth headers
        if (this.Context.Request.Headers.Contains("x-api-key"))
        {
            request.Headers.Add("x-api-key",
                this.Context.Request.Headers.GetValues("x-api-key").First());
        }

        // Forward user identity headers for OBO token exchange (Tier 1)
        if (this.Context.Request.Headers.Contains("Authorization"))
        {
            request.Headers.Add("Authorization",
                this.Context.Request.Headers.GetValues("Authorization").First());
        }
        if (this.Context.Request.Headers.Contains("x-ms-token-aad-access-token"))
        {
            request.Headers.Add("x-ms-token-aad-access-token",
                this.Context.Request.Headers.GetValues("x-ms-token-aad-access-token").First());
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    // ========================================================================
    // Search Payload Builder
    // ========================================================================

    private JObject BuildSearchPayload(JObject arguments)
    {
        var text = arguments["text"]?.ToString();
        var typeFilter = arguments["type_filter"]?.ToString();
        var tags = arguments["tags"]?.ToString();
        var federation = arguments["federation"]?.ToString();

        var query = new JObject { ["text"] = text };
        var filter = new JObject();

        if (!string.IsNullOrEmpty(typeFilter))
        {
            var typeMap = new Dictionary<string, string>
            {
                { "mcp", "application/mcp-server-card+json" },
                { "a2a", "application/a2a-agent-card+json" },
                { "skill", "application/ai-skill" }
            };

            if (typeMap.TryGetValue(typeFilter.ToLower(), out var mediaType))
            {
                filter["type"] = new JArray { mediaType };
            }
            else
            {
                filter["type"] = new JArray { typeFilter };
            }
        }

        if (!string.IsNullOrEmpty(tags))
        {
            filter["tags"] = new JArray(
                tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToArray()
            );
        }

        if (filter.HasValues)
        {
            query["filter"] = filter;
        }

        var searchBody = new JObject { ["query"] = query };

        if (!string.IsNullOrEmpty(federation))
        {
            searchBody["federation"] = federation;
        }

        searchBody["pageSize"] = 10;

        return searchBody;
    }

    // ========================================================================
    // JSON-RPC Helpers
    // ========================================================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private HttpResponseMessage CreateToolSuccessResponse(JToken id, string resultText)
    {
        return CreateJsonRpcSuccessResponse(id, new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = resultText
                }
            },
            ["isError"] = false
        });
    }

    private HttpResponseMessage CreateToolErrorResponse(JToken id, string errorMessage)
    {
        return CreateJsonRpcSuccessResponse(id, new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = errorMessage
                }
            },
            ["isError"] = true
        });
    }

    // ========================================================================
    // App Insights Telemetry
    // ========================================================================

    private void LogToAppInsights(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.Contains("INSERT_YOUR"))
            return;

        try
        {
            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(
                            properties ?? new Dictionary<string, string>()
                        )
                    }
                }
            };

            // Add connector name to all events
            ((JObject)telemetryData["data"]["baseData"]["properties"])["connector"] = CONNECTOR_NAME;

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT);
            request.Content = new StringContent(
                telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );

            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}

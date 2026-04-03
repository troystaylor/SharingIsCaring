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
    // ========================================
    // CONFIGURATION
    // ========================================

    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private const string ServerName = "foundry-rerank";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";
    private const string DefaultModel = "Cohere-rerank-v4.0-pro";

    // ========================================
    // MAIN ENTRY POINT
    // ========================================

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        this.Context.Logger.LogInformation($"[{correlationId}] Request received: {this.Context.OperationId}");

        try
        {
            _ = LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId
            });

            HttpResponseMessage response;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpRequestAsync(correlationId).ConfigureAwait(false);
                    break;

                case "RerankDocuments":
                    response = await HandleRerankDocumentsAsync(correlationId).ConfigureAwait(false);
                    break;

                case "RerankAndFilter":
                    response = await HandleRerankAndFilterAsync(correlationId).ConfigureAwait(false);
                    break;

                default:
                    response = await HandlePassthroughAsync().ConfigureAwait(false);
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] Error: {ex.Message}");
            _ = LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message
            });
            throw;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _ = LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // ========================================
    // OPERATION HANDLERS
    // ========================================

    /// <summary>
    /// Handles RerankDocuments — forwards to Cohere v2/rerank API and enriches
    /// the response with the original document text for each result.
    /// </summary>
    private async Task<HttpResponseMessage> HandleRerankDocumentsAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var query = body.Value<string>("query");
        var documents = body["documents"] as JArray;
        var model = body.Value<string>("model") ?? DefaultModel;
        var topN = body["top_n"]?.Value<int?>();
        var maxTokens = body["max_tokens_per_doc"]?.Value<int?>() ?? 4096;

        if (string.IsNullOrWhiteSpace(query))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"query is required\"}")
            };
        }

        if (documents == null || documents.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"documents array is required and must not be empty\"}")
            };
        }

        _ = LogToAppInsights("RerankDocumentsRequest", new
        {
            CorrelationId = correlationId,
            Model = model,
            DocumentCount = documents.Count,
            TopN = topN
        });

        // Build the Cohere v2/rerank request
        var rerankRequest = new JObject
        {
            ["model"] = model,
            ["query"] = query,
            ["documents"] = documents,
            ["max_tokens_per_doc"] = maxTokens
        };
        if (topN.HasValue)
        {
            rerankRequest["top_n"] = topN.Value;
        }

        // Rewrite URL to /v2/rerank
        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/v2/rerank"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            rerankRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("RerankDocumentsError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Enrich results with original document text
        var results = result["results"] as JArray;
        if (results != null)
        {
            foreach (var item in results)
            {
                var index = item.Value<int>("index");
                if (index >= 0 && index < documents.Count)
                {
                    item["document"] = documents[index].ToString();
                }
            }
        }

        _ = LogToAppInsights("RerankDocumentsResult", new
        {
            CorrelationId = correlationId,
            ResultCount = results?.Count ?? 0,
            SearchUnits = result["meta"]?["billed_units"]?["search_units"]?.Value<int>() ?? 0
        });

        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    /// <summary>
    /// Handles RerankAndFilter — calls the rerank API, then filters results
    /// below the minimum score threshold. Returns enriched results with counts.
    /// </summary>
    private async Task<HttpResponseMessage> HandleRerankAndFilterAsync(string correlationId)
    {
        var bodyString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var body = JObject.Parse(bodyString);

        var query = body.Value<string>("query");
        var documents = body["documents"] as JArray;
        var model = body.Value<string>("model") ?? DefaultModel;
        var minScore = body["min_score"]?.Value<float>() ?? 0.0f;
        var topN = body["top_n"]?.Value<int?>();
        var maxTokens = body["max_tokens_per_doc"]?.Value<int?>() ?? 4096;

        if (string.IsNullOrWhiteSpace(query))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"query is required\"}")
            };
        }

        if (documents == null || documents.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent("{\"error\": \"documents array is required and must not be empty\"}")
            };
        }

        _ = LogToAppInsights("RerankAndFilterRequest", new
        {
            CorrelationId = correlationId,
            Model = model,
            DocumentCount = documents.Count,
            MinScore = minScore,
            TopN = topN
        });

        // Build the Cohere v2/rerank request
        var rerankRequest = new JObject
        {
            ["model"] = model,
            ["query"] = query,
            ["documents"] = documents,
            ["max_tokens_per_doc"] = maxTokens
        };

        // Rewrite URL to /v2/rerank
        var uri = new UriBuilder(this.Context.Request.RequestUri)
        {
            Path = "/v2/rerank"
        }.Uri;
        this.Context.Request.RequestUri = uri;
        this.Context.Request.Content = new StringContent(
            rerankRequest.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _ = LogToAppInsights("RerankAndFilterError", new
            {
                CorrelationId = correlationId,
                StatusCode = (int)response.StatusCode,
                ErrorBody = errorContent.Length > 500 ? errorContent.Substring(0, 500) : errorContent
            });
            return response;
        }

        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JObject.Parse(responseString);

        // Filter by minimum score and enrich with document text
        var results = result["results"] as JArray ?? new JArray();
        var totalInput = documents.Count;
        var filtered = new JArray();

        foreach (var item in results)
        {
            var score = item.Value<float>("relevance_score");
            if (score >= minScore)
            {
                var index = item.Value<int>("index");
                if (index >= 0 && index < documents.Count)
                {
                    item["document"] = documents[index].ToString();
                }
                filtered.Add(item);
            }
        }

        // Apply top_n after filtering
        if (topN.HasValue && filtered.Count > topN.Value)
        {
            var trimmed = new JArray();
            for (int i = 0; i < topN.Value; i++)
            {
                trimmed.Add(filtered[i]);
            }
            filtered = trimmed;
        }

        var formattedResponse = new JObject
        {
            ["results"] = filtered,
            ["total_input"] = totalInput,
            ["total_passed"] = filtered.Count,
            ["total_filtered"] = totalInput - filtered.Count
        };

        _ = LogToAppInsights("RerankAndFilterResult", new
        {
            CorrelationId = correlationId,
            TotalInput = totalInput,
            TotalPassed = filtered.Count,
            TotalFiltered = totalInput - filtered.Count,
            MinScore = minScore
        });

        response.Content = CreateJsonContent(formattedResponse.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    private async Task<HttpResponseMessage> HandlePassthroughAsync()
    {
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string correlationId)
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

            try
            {
                request = JObject.Parse(body);
            }
            catch (JsonException)
            {
                return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
            }

            method = request.Value<string>("method") ?? string.Empty;
            requestId = request["id"];

            _ = LogToAppInsights("McpRequestReceived", new
            {
                CorrelationId = correlationId,
                Method = method
            });

            switch (method)
            {
                case "initialize":
                    return HandleMcpInitialize(request, requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return HandleMcpToolsList(requestId);

                case "tools/call":
                    return await HandleMcpToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resourceTemplates"] = new JArray() });

                case "prompts/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

                case "completion/complete":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                case "logging/setLevel":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleMcpInitialize(JObject request, JToken requestId)
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
                ["title"] = "Microsoft Foundry Rerank",
                ["description"] = "Rerank documents by semantic relevance using Cohere Rerank v4 on Microsoft Foundry"
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    // ========================================
    // MCP TOOLS LIST
    // ========================================

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            new JObject
            {
                ["name"] = "rerank_documents",
                ["description"] = "Rerank a list of documents by semantic relevance to a query. Returns documents ordered by relevance score (highest first). Use after search results retrieval and before sending context to an LLM.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The search query to rank documents against"
                        },
                        ["documents"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject { ["type"] = "string" },
                            ["description"] = "Array of document texts to rerank (max 1,000 recommended)"
                        },
                        ["top_n"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Number of top results to return. Omit to return all."
                        },
                        ["model"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Model to use: 'Cohere-rerank-v4.0-pro' (default, best quality) or 'Cohere-rerank-v4.0-fast' (lower latency)"
                        }
                    },
                    ["required"] = new JArray { "query", "documents" }
                }
            },
            new JObject
            {
                ["name"] = "rerank_and_filter",
                ["description"] = "Rerank documents and filter out those below a minimum relevance score. Returns only documents above the threshold, ordered by relevance. Use for quality-gating context before sending to an LLM.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["query"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The search query to rank documents against"
                        },
                        ["documents"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject { ["type"] = "string" },
                            ["description"] = "Array of document texts to rerank"
                        },
                        ["min_score"] = new JObject
                        {
                            ["type"] = "number",
                            ["description"] = "Minimum relevance score (0-1). Documents below this are excluded. Example: 0.5"
                        },
                        ["top_n"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum results to return after filtering"
                        },
                        ["model"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Model to use: 'Cohere-rerank-v4.0-pro' (default) or 'Cohere-rerank-v4.0-fast'"
                        }
                    },
                    ["required"] = new JArray { "query", "documents", "min_score" }
                }
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    // ========================================
    // MCP TOOLS/CALL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(string correlationId, JObject request, JToken requestId)
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

        _ = LogToAppInsights("McpToolCall", new
        {
            CorrelationId = correlationId,
            ToolName = toolName
        });

        try
        {
            JObject toolResult;

            switch (toolName.ToLowerInvariant())
            {
                case "rerank_documents":
                    toolResult = await ExecuteRerankToolAsync(arguments, filterByScore: false).ConfigureAwait(false);
                    break;

                case "rerank_and_filter":
                    toolResult = await ExecuteRerankToolAsync(arguments, filterByScore: true).ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
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
            });
        }
        catch (Exception ex)
        {
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

    // ========================================
    // MCP TOOL IMPLEMENTATION
    // ========================================

    private async Task<JObject> ExecuteRerankToolAsync(JObject arguments, bool filterByScore)
    {
        var query = arguments.Value<string>("query");
        var documents = arguments["documents"] as JArray;
        var model = arguments.Value<string>("model") ?? DefaultModel;
        var topN = arguments["top_n"]?.Value<int?>();
        var minScore = arguments["min_score"]?.Value<float>() ?? 0.0f;

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("'query' is required");
        }

        if (documents == null || documents.Count == 0)
        {
            throw new ArgumentException("'documents' array is required and must not be empty");
        }

        var requestBody = new JObject
        {
            ["model"] = model,
            ["query"] = query,
            ["documents"] = documents,
            ["max_tokens_per_doc"] = 4096
        };

        if (!filterByScore && topN.HasValue)
        {
            requestBody["top_n"] = topN.Value;
        }

        var apiUrl = BuildApiUrl("/v2/rerank");
        var result = await SendApiRequestAsync(HttpMethod.Post, apiUrl, requestBody).ConfigureAwait(false);

        var results = result["results"] as JArray ?? new JArray();

        // Enrich with document text
        foreach (var item in results)
        {
            var index = item.Value<int>("index");
            if (index >= 0 && index < documents.Count)
            {
                item["document"] = documents[index].ToString();
            }
        }

        if (filterByScore)
        {
            var filtered = new JArray();
            foreach (var item in results)
            {
                if (item.Value<float>("relevance_score") >= minScore)
                {
                    filtered.Add(item);
                }
            }

            if (topN.HasValue && filtered.Count > topN.Value)
            {
                var trimmed = new JArray();
                for (int i = 0; i < topN.Value; i++)
                {
                    trimmed.Add(filtered[i]);
                }
                filtered = trimmed;
            }

            return new JObject
            {
                ["results"] = filtered,
                ["total_input"] = documents.Count,
                ["total_passed"] = filtered.Count,
                ["total_filtered"] = documents.Count - filtered.Count
            };
        }

        return new JObject
        {
            ["results"] = results,
            ["id"] = result["id"],
            ["search_units"] = result["meta"]?["billed_units"]?["search_units"]
        };
    }

    // ========================================
    // API HELPERS
    // ========================================

    private string BuildApiUrl(string path)
    {
        var baseUri = this.Context.Request.RequestUri;
        var host = baseUri.Host;
        return $"https://{host}{path}";
    }

    private async Task<JObject> SendApiRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        if (this.Context.Request.Headers.Contains("api-key"))
        {
            request.Headers.Add("api-key", this.Context.Request.Headers.GetValues("api-key"));
        }

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");
        }

        return JObject.Parse(content);
    }

    // ========================================
    // JSON-RPC HELPERS
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
        var error = new JObject { ["code"] = code, ["message"] = message };
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

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = ServerName,
                ["ServerVersion"] = ServerVersion
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

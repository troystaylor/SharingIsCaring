using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private static readonly string SERVER_NAME = "CopilotRetrievalMcpServer";
    private static readonly string SERVER_VERSION = "1.0.0";
    
    // Tool definitions for Microsoft 365 Copilot Retrieval API
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "retrieve_from_sharepoint",
            ["description"] = "Retrieve relevant text extracts from SharePoint sites based on a natural language query. Returns grounding data with relevance scores for RAG applications.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Natural language query (single sentence, max 1,500 characters). Be specific and avoid spelling errors."
                    },
                    ["filter"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional KQL filter expression (e.g., 'FileExtension:\"docx\" OR FileExtension:\"pdf\"')"
                    },
                    ["site_path"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional SharePoint site path to scope retrieval (e.g., 'https://contoso.sharepoint.com/sites/HR')"
                    },
                    ["max_results"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of results to return (1-25)",
                        ["minimum"] = 1,
                        ["maximum"] = 25,
                        ["default"] = 10
                    },
                    ["metadata_fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional metadata fields to return (e.g., ['title', 'author'])",
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    }
                },
                ["required"] = new JArray { "query" }
            }
        },
        new JObject
        {
            ["name"] = "retrieve_from_onedrive",
            ["description"] = "Retrieve relevant text extracts from OneDrive for Business based on a natural language query. Useful for finding user-specific documents and files.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Natural language query (single sentence, max 1,500 characters)"
                    },
                    ["filter"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional KQL filter expression (e.g., 'LastModifiedTime >= 2024-01-01')"
                    },
                    ["max_results"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of results to return (1-25)",
                        ["minimum"] = 1,
                        ["maximum"] = 25,
                        ["default"] = 10
                    },
                    ["metadata_fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional metadata fields to return",
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    }
                },
                ["required"] = new JArray { "query" }
            }
        },
        new JObject
        {
            ["name"] = "retrieve_from_copilot_connectors",
            ["description"] = "Retrieve relevant text extracts from Microsoft 365 Copilot connectors (external data sources). Useful for searching third-party knowledge bases integrated with Microsoft 365.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Natural language query (single sentence, max 1,500 characters)"
                    },
                    ["connection_ids"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional list of specific Copilot connector connection IDs to search",
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    },
                    ["filter"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional KQL filter expression using queryable connector properties"
                    },
                    ["max_results"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of results to return (1-25)",
                        ["minimum"] = 1,
                        ["maximum"] = 25,
                        ["default"] = 10
                    },
                    ["metadata_fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional metadata fields to return",
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    }
                },
                ["required"] = new JArray { "query" }
            }
        },
        new JObject
        {
            ["name"] = "retrieve_multi_source",
            ["description"] = "Retrieve from multiple Microsoft 365 sources sequentially (SharePoint, OneDrive, and Copilot connectors). Provides comprehensive cross-source search.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Natural language query (single sentence, max 1,500 characters)"
                    },
                    ["sources"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Data sources to search (options: 'sharePoint', 'oneDriveBusiness', 'externalItem')",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JArray { "sharePoint", "oneDriveBusiness", "externalItem" }
                        },
                        ["default"] = new JArray { "sharePoint", "oneDriveBusiness" }
                    },
                    ["max_results_per_source"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum results per source (1-25)",
                        ["minimum"] = 1,
                        ["maximum"] = 25,
                        ["default"] = 5
                    }
                },
                ["required"] = new JArray { "query" }
            }
        }
    };
    
    // Tool implementations
    private async Task<JObject> ExecuteRetrieveFromSharepointTool(JObject arguments)
    {
        var query = arguments.GetValue("query")?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("query parameter is required");
        }

        var requestBody = new JObject
        {
            ["queryString"] = query,
            ["dataSource"] = "sharePoint"
        };

        // Add optional parameters
        var maxResults = arguments.GetValue("max_results")?.ToObject<int?>() ?? 10;
        requestBody["maximumNumberOfResults"] = maxResults;

        // Build filter expression
        var filter = arguments.GetValue("filter")?.ToString();
        var sitePath = arguments.GetValue("site_path")?.ToString();
        
        if (!string.IsNullOrEmpty(sitePath) && !string.IsNullOrEmpty(filter))
        {
            requestBody["filterExpression"] = $"path:\"{sitePath}\" AND ({filter})";
        }
        else if (!string.IsNullOrEmpty(sitePath))
        {
            requestBody["filterExpression"] = $"path:\"{sitePath}\"";
        }
        else if (!string.IsNullOrEmpty(filter))
        {
            requestBody["filterExpression"] = filter;
        }

        // Add metadata fields if specified
        var metadataFields = arguments.GetValue("metadata_fields") as JArray;
        if (metadataFields != null && metadataFields.Count > 0)
        {
            requestBody["resourceMetadata"] = metadataFields;
        }

        var response = await MakeRetrievalApiCall(requestBody);
        return FormatRetrievalResponse(response, "SharePoint");
    }

    private async Task<JObject> ExecuteRetrieveFromOnedriveTool(JObject arguments)
    {
        var query = arguments.GetValue("query")?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("query parameter is required");
        }

        var requestBody = new JObject
        {
            ["queryString"] = query,
            ["dataSource"] = "oneDriveBusiness"
        };

        var maxResults = arguments.GetValue("max_results")?.ToObject<int?>() ?? 10;
        requestBody["maximumNumberOfResults"] = maxResults;

        var filter = arguments.GetValue("filter")?.ToString();
        if (!string.IsNullOrEmpty(filter))
        {
            requestBody["filterExpression"] = filter;
        }

        var metadataFields = arguments.GetValue("metadata_fields") as JArray;
        if (metadataFields != null && metadataFields.Count > 0)
        {
            requestBody["resourceMetadata"] = metadataFields;
        }

        var response = await MakeRetrievalApiCall(requestBody);
        return FormatRetrievalResponse(response, "OneDrive for Business");
    }

    private async Task<JObject> ExecuteRetrieveFromCopilotConnectorsTool(JObject arguments)
    {
        var query = arguments.GetValue("query")?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("query parameter is required");
        }

        var requestBody = new JObject
        {
            ["queryString"] = query,
            ["dataSource"] = "externalItem"
        };

        var maxResults = arguments.GetValue("max_results")?.ToObject<int?>() ?? 10;
        requestBody["maximumNumberOfResults"] = maxResults;

        // Add connection IDs if specified
        var connectionIds = arguments.GetValue("connection_ids") as JArray;
        if (connectionIds != null && connectionIds.Count > 0)
        {
            requestBody["dataSourceConfiguration"] = new JObject
            {
                ["externalItem"] = new JObject
                {
                    ["connections"] = new JArray(connectionIds.Select(id => new JObject
                    {
                        ["connectionId"] = id.ToString()
                    }))
                }
            };
        }

        var filter = arguments.GetValue("filter")?.ToString();
        if (!string.IsNullOrEmpty(filter))
        {
            requestBody["filterExpression"] = filter;
        }

        var metadataFields = arguments.GetValue("metadata_fields") as JArray;
        if (metadataFields != null && metadataFields.Count > 0)
        {
            requestBody["resourceMetadata"] = metadataFields;
        }

        var response = await MakeRetrievalApiCall(requestBody);
        return FormatRetrievalResponse(response, "Copilot Connectors");
    }

    private async Task<JObject> ExecuteRetrieveMultiSourceTool(JObject arguments)
    {
        var query = arguments.GetValue("query")?.ToString();
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("query parameter is required");
        }

        var sources = arguments.GetValue("sources") as JArray;
        if (sources == null || sources.Count == 0)
        {
            sources = new JArray { "sharePoint", "oneDriveBusiness" };
        }

        var maxResultsPerSource = arguments.GetValue("max_results_per_source")?.ToObject<int?>() ?? 5;
        var allResults = new System.Collections.Generic.List<string>();
        var sourceSummary = new System.Collections.Generic.List<string>();

        foreach (var source in sources)
        {
            var requestBody = new JObject
            {
                ["queryString"] = query,
                ["dataSource"] = source.ToString(),
                ["maximumNumberOfResults"] = maxResultsPerSource
            };

            try
            {
                var response = await MakeRetrievalApiCall(requestBody);
                var hits = response["retrievalHits"] as JArray;
                
                if (hits != null && hits.Count > 0)
                {
                    var sourceName = GetSourceDisplayName(source.ToString());
                    sourceSummary.Add($"{sourceName}: {hits.Count} result(s)");
                    
                    allResults.Add($"\n=== {sourceName} Results ===");
                    foreach (var hit in hits)
                    {
                        var webUrl = hit["webUrl"]?.ToString();
                        var extracts = hit["extracts"] as JArray;
                        
                        if (extracts != null && extracts.Count > 0)
                        {
                            var firstExtract = extracts[0];
                            var text = firstExtract["text"]?.ToString();
                            var relevanceScore = firstExtract["relevanceScore"]?.ToObject<double?>() ?? 0;
                            
                            allResults.Add($"\n• Source: {webUrl}");
                            allResults.Add($"  Relevance: {relevanceScore:F3}");
                            allResults.Add($"  Extract: {text?.Substring(0, Math.Min(200, text.Length))}...");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                allResults.Add($"\n=== {GetSourceDisplayName(source.ToString())} ===");
                allResults.Add($"Error: {ex.Message}");
            }
        }

        return new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Multi-Source Retrieval Results for: \"{query}\"\n\n" +
                              $"Summary: {string.Join(", ", sourceSummary)}\n" +
                              string.Join("\n", allResults)
                }
            }
        };
    }

    // Helper methods
    private async Task<JObject> MakeRetrievalApiCall(JObject requestBody)
    {
        try
        {
            // Use the connector's configured API version path via policy template
            var url = "https://graph.microsoft.com/copilot/retrieval";
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = CreateJsonContent(requestBody.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            
            var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                return JObject.Parse(content);
            }
            else
            {
                throw new HttpRequestException($"Microsoft Graph API Error ({response.StatusCode}): {content}");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse API response: {ex.Message}");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Retrieval API call failed: {ex.Message}");
        }
    }

    private JObject FormatRetrievalResponse(JObject response, string sourceName)
    {
        var hits = response["retrievalHits"] as JArray;
        
        if (hits == null || hits.Count == 0)
        {
            return new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"No relevant results found in {sourceName}"
                    }
                }
            };
        }

        var results = new System.Collections.Generic.List<string>
        {
            $"Retrieval Results from {sourceName} ({hits.Count} result(s)):\n"
        };

        foreach (var hit in hits.Take(10))
        {
            var webUrl = hit["webUrl"]?.ToString();
            var resourceType = hit["resourceType"]?.ToString();
            var extracts = hit["extracts"] as JArray;
            var metadata = hit["resourceMetadata"] as JObject;
            var sensitivityLabel = hit["sensitivityLabel"] as JObject;

            results.Add($"\n📄 Source: {webUrl}");
            results.Add($"   Type: {resourceType}");

            if (metadata != null && metadata.Count > 0)
            {
                var metadataStr = string.Join(", ", metadata.Properties().Select(p => $"{p.Name}: {p.Value}"));
                results.Add($"   Metadata: {metadataStr}");
            }

            if (sensitivityLabel != null)
            {
                var labelName = sensitivityLabel["displayName"]?.ToString();
                if (!string.IsNullOrEmpty(labelName))
                {
                    results.Add($"   🔒 Label: {labelName}");
                }
            }

            if (extracts != null && extracts.Count > 0)
            {
                results.Add($"   Extracts:");
                foreach (var extract in extracts)
                {
                    var text = extract["text"]?.ToString();
                    var relevanceScore = extract["relevanceScore"]?.ToObject<double?>();
                    
                    if (relevanceScore.HasValue)
                    {
                        results.Add($"   • [{relevanceScore.Value:F3}] {text}");
                    }
                    else
                    {
                        results.Add($"   • {text}");
                    }
                }
            }
        }

        return new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = string.Join("\n", results)
                }
            }
        };
    }

    private string GetSourceDisplayName(string dataSource)
    {
        switch (dataSource)
        {
            case "sharePoint":
                return "SharePoint";
            case "oneDriveBusiness":
                return "OneDrive for Business";
            case "externalItem":
                return "Copilot Connectors";
            default:
                return dataSource;
        }
    }

    // ****** DO NOT MODIFY BELOW THIS LINE ******
    // MCP Protocol Implementation
    private static readonly string PROTOCOL_VERSION = "2025-06-18";
    private static bool _isInitialized = false;
    private static readonly JObject SERVER_CAPABILITIES = new JObject
    {
        ["tools"] = new JObject
        {
            ["listChanged"] = true
        }
    };
    
    private static string[] GetToolNames()
    {
        return AVAILABLE_TOOLS.Select(tool => tool["name"]?.ToString()).Where(name => !string.IsNullOrEmpty(name)).ToArray();
    }
    
    private static string ConvertToMethodName(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return "";
        
        var parts = toolName.Split('_');
        var result = new StringBuilder();
        
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                result.Append(char.ToUpper(part[0]));
                if (part.Length > 1)
                {
                    result.Append(part.Substring(1).ToLower());
                }
            }
        }
        
        return result.ToString();
    }
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            var operationId = GetOperationId();
            
            if (operationId == "InvokeServer")
            {
                return await HandleMcpRequestAsync().ConfigureAwait(false);
            }
            else
            {
                return CreateJsonRpcErrorResponse(null, -32601, "Method not found", $"Unknown operation ID '{operationId}'");
            }
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(null, -32603, "Internal error", ex.Message);
        }
    }
    
    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var requestBody = await ParseRequestBodyAsync().ConfigureAwait(false);
        
        if (requestBody.Count == 0 || string.IsNullOrEmpty(GetStringProperty(requestBody, "method", "")))
        {
            return await HandleInitializedAsync().ConfigureAwait(false);
        }
        
        var method = GetStringProperty(requestBody, "method", "");
        var requestId = GetRequestId(requestBody);
        
        switch (method)
        {
            case "initialize":
                return await HandleInitializeAsync(requestBody, requestId).ConfigureAwait(false);
            case "notifications/initialized":
                return await HandleInitializedAsync().ConfigureAwait(false);
            case "tools/list":
                return await HandleToolsListAsync(requestId).ConfigureAwait(false);
            case "tools/call":
                return await HandleToolsCallAsync(requestBody, requestId).ConfigureAwait(false);
            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", $"Unknown method '{method}'");
        }
    }
    
    private async Task<HttpResponseMessage> HandleInitializeAsync(JObject requestBody, object requestId)
    {
        try
        {
            var paramsObj = requestBody["params"] as JObject;
            var clientVersion = GetStringProperty(paramsObj, "protocolVersion", "");
            
            if (string.IsNullOrEmpty(clientVersion))
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "protocolVersion is required");
            }
            
            var initializeResult = new JObject
            {
                ["protocolVersion"] = PROTOCOL_VERSION,
                ["capabilities"] = SERVER_CAPABILITIES,
                ["serverInfo"] = new JObject
                {
                    ["name"] = SERVER_NAME,
                    ["version"] = SERVER_VERSION
                },
                ["instructions"] = "Microsoft 365 Copilot Retrieval API MCP Server. Provides tools for retrieving relevant text extracts from SharePoint, OneDrive, and Copilot connectors for grounding AI applications."
            };
            
            return CreateJsonRpcSuccessResponse(requestId, initializeResult);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }
    
    private async Task<HttpResponseMessage> HandleInitializedAsync()
    {
        _isInitialized = true;
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var confirmationResponse = new JObject
        {
            ["status"] = "initialized",
            ["message"] = "MCP server initialization complete - ready for retrieval operations",
            ["serverName"] = SERVER_NAME,
            ["serverVersion"] = SERVER_VERSION,
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["capabilities"] = new JObject
            {
                ["tools"] = new JArray(GetToolNames())
            }
        };
        
        response.Content = CreateJsonContent(confirmationResponse.ToString());
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
    
    private async Task<HttpResponseMessage> HandleToolsListAsync(object requestId)
    {
        if (!_isInitialized)
        {
            return CreateJsonRpcErrorResponse(requestId, -32002, "Server not initialized", "Must call initialize first");
        }
        
        try
        {
            var result = new JObject
            {
                ["tools"] = AVAILABLE_TOOLS
            };
            
            return CreateJsonRpcSuccessResponse(requestId, result);
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }
    
    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject requestBody, object requestId)
    {
        if (!_isInitialized)
        {
            return CreateJsonRpcErrorResponse(requestId, -32002, "Server not initialized", "Must call initialize first");
        }
        
        try
        {
            var paramsObj = requestBody["params"] as JObject;
            if (paramsObj == null)
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
            }
            
            var toolName = GetStringProperty(paramsObj, "name", "");
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "tool name is required");
            }
            
            var methodName = "Execute" + ConvertToMethodName(toolName) + "Tool";
            var method = this.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method == null)
            {
                return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", $"Unknown tool: {toolName}");
            }
            
            var arguments = paramsObj["arguments"] as JObject ?? new JObject();
            
            if (method.ReturnType == typeof(Task<JObject>))
            {
                var task = method.Invoke(this, new object[] { arguments }) as Task<JObject>;
                var result = await task;
                return CreateJsonRpcSuccessResponse(requestId, result);
            }
            else
            {
                var result = method.Invoke(this, new object[] { arguments }) as JObject;
                return CreateJsonRpcSuccessResponse(requestId, result);
            }
        }
        catch (Exception ex)
        {
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }
    
    private string GetOperationId()
    {
        string operationId = this.Context.OperationId;
        
        if (string.IsNullOrEmpty(operationId))
        {
            return "InvokeServer";
        }
        
        if (operationId != "InvokeServer" && IsBase64String(operationId))
        {
            try 
            {
                byte[] data = Convert.FromBase64String(operationId);
                operationId = System.Text.Encoding.UTF8.GetString(data);
            }
            catch (FormatException) 
            {
            }
        }
        
        return operationId;
    }
    
    private bool IsBase64String(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.Length % 4 == 0 && 
               System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", System.Text.RegularExpressions.RegexOptions.None);
    }
    
    private async Task<JObject> ParseRequestBodyAsync()
    {
        var contentAsString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JObject.Parse(contentAsString);
    }
    
    private object GetRequestId(JObject requestBody)
    {
        var id = requestBody["id"];
        if (id == null) return null;
        
        if (id.Type == JTokenType.String)
            return id.ToString();
        if (id.Type == JTokenType.Integer)
            return id.ToObject<int>();
        if (id.Type == JTokenType.Float)
            return id.ToObject<double>();
            
        return id.ToString();
    }
    
    private HttpResponseMessage CreateJsonRpcSuccessResponse(object id, JObject result)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var jsonRpcResponse = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? JToken.FromObject(id) : null,
            ["result"] = result
        };
        
        response.Content = CreateJsonContent(jsonRpcResponse.ToString());
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
    
    private HttpResponseMessage CreateJsonRpcErrorResponse(object id, int code, string message, string data = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var errorObject = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        
        if (!string.IsNullOrEmpty(data))
        {
            errorObject["data"] = data;
        }
        
        var jsonRpcResponse = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? JToken.FromObject(id) : null,
            ["error"] = errorObject
        };
        
        response.Content = CreateJsonContent(jsonRpcResponse.ToString());
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
    
    private string GetStringProperty(JObject json, string propertyName, string defaultValue = "")
    {
        if (json == null) return defaultValue;
        return json[propertyName]?.ToString() ?? defaultValue;
    }
}

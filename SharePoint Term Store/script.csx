using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        this.Context.Logger.LogInformation($"SharePoint Term Store request. CorrelationId: {correlationId}");
        
        try
        {
            var requestPath = this.Context.Request.RequestUri.AbsolutePath;
            
            await LogToAppInsights("RequestReceived", new {
                CorrelationId = correlationId,
                Path = requestPath,
                Method = this.Context.Request.Method.Method,
                OperationId = this.Context.OperationId
            });
            
            // Route MCP requests to protocol handler
            if (requestPath.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleMCPProtocolAsync(correlationId).ConfigureAwait(false);
            }
            
            // Pass through all Graph API operations directly
            this.Context.Logger.LogInformation($"Forwarding to Graph API: {this.Context.OperationId}");
            var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            
            await LogToAppInsights("GraphAPIResponse", new {
                CorrelationId = correlationId,
                OperationId = this.Context.OperationId,
                StatusCode = (int)response.StatusCode
            });
            
            return response;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Unexpected error: {ex.Message}";
            this.Context.Logger.LogError($"CorrelationId: {correlationId}, Error: {errorMessage}");
            
            await LogToAppInsights("RequestError", new {
                CorrelationId = correlationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                OperationId = this.Context.OperationId
            });
            
            return CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"Request completed. CorrelationId: {correlationId}, Duration: {duration.TotalMilliseconds}ms");
            
            await LogToAppInsights("RequestCompleted", new {
                CorrelationId = correlationId,
                DurationMs = duration.TotalMilliseconds
            });
        }
    }

    // MCP Protocol version
    private const string PROTOCOL_VERSION = "2024-11-05";
    
    // Define available MCP tools for SharePoint Term Store management
    private static readonly JArray AVAILABLE_TOOLS = new JArray
    {
        new JObject
        {
            ["name"] = "get_term_store",
            ["description"] = "Get the default term store for a SharePoint site. Returns the term store ID, default language, and available languages.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    }
                },
                ["required"] = new JArray { "siteId" }
            }
        },
        new JObject
        {
            ["name"] = "list_term_groups",
            ["description"] = "List all term groups in a site's term store. Term groups organize term sets by business area or function.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    }
                },
                ["required"] = new JArray { "siteId" }
            }
        },
        new JObject
        {
            ["name"] = "create_term_group",
            ["description"] = "Create a new term group in the term store. Term groups contain related term sets.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["displayName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the term group"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of the term group"
                    }
                },
                ["required"] = new JArray { "siteId", "displayName" }
            }
        },
        new JObject
        {
            ["name"] = "list_term_sets",
            ["description"] = "List all term sets in a term group. Term sets are collections of related managed metadata terms.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["groupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term group ID"
                    }
                },
                ["required"] = new JArray { "siteId", "groupId" }
            }
        },
        new JObject
        {
            ["name"] = "create_term_set",
            ["description"] = "Create a new term set in a term group. Term sets contain hierarchical terms for categorization.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["groupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term group ID"
                    },
                    ["displayName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the term set"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of the term set"
                    },
                    ["languageTag"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Language code (e.g., 'en-US')"
                    }
                },
                ["required"] = new JArray { "siteId", "groupId", "displayName", "languageTag" }
            }
        },
        new JObject
        {
            ["name"] = "list_terms",
            ["description"] = "List all root-level terms in a term set. Use list_child_terms to get nested terms.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["setId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term set ID"
                    }
                },
                ["required"] = new JArray { "siteId", "setId" }
            }
        },
        new JObject
        {
            ["name"] = "create_term",
            ["description"] = "Create a new term in a term set. Terms are the actual metadata values users can apply to content.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["setId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term set ID"
                    },
                    ["label"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term label/name"
                    },
                    ["languageTag"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Language code (e.g., 'en-US')"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of the term"
                    }
                },
                ["required"] = new JArray { "siteId", "setId", "label", "languageTag" }
            }
        },
        new JObject
        {
            ["name"] = "list_child_terms",
            ["description"] = "List all child terms under a parent term. Useful for navigating term hierarchies.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["setId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term set ID"
                    },
                    ["termId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Parent term ID"
                    }
                },
                ["required"] = new JArray { "siteId", "setId", "termId" }
            }
        },
        new JObject
        {
            ["name"] = "create_child_term",
            ["description"] = "Create a child term under a parent term to build hierarchical taxonomy structures.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["setId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term set ID"
                    },
                    ["termId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Parent term ID"
                    },
                    ["label"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Child term label/name"
                    },
                    ["languageTag"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Language code (e.g., 'en-US')"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Description of the child term"
                    }
                },
                ["required"] = new JArray { "siteId", "setId", "termId", "label", "languageTag" }
            }
        },
        new JObject
        {
            ["name"] = "update_term",
            ["description"] = "Update an existing term's label or description. Use for renaming terms or adding multilingual labels.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["setId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term set ID"
                    },
                    ["termId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term ID to update"
                    },
                    ["labels"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of label objects with name, languageTag, and isDefault properties",
                        ["items"] = new JObject
                        {
                            ["type"] = "object"
                        }
                    },
                    ["descriptions"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of description objects with description and languageTag properties",
                        ["items"] = new JObject
                        {
                            ["type"] = "object"
                        }
                    }
                },
                ["required"] = new JArray { "siteId", "setId", "termId" }
            }
        },
        new JObject
        {
            ["name"] = "delete_term",
            ["description"] = "Delete a term from the term store. Warning: This will also delete all child terms.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["siteId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "SharePoint site ID (use 'root' for root site collection)"
                    },
                    ["setId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term set ID"
                    },
                    ["termId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Term ID to delete"
                    }
                },
                ["required"] = new JArray { "siteId", "setId", "termId" }
            }
        }
    };

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleMCPProtocolAsync(string correlationId)
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        await LogToAppInsights("MCPProtocolInvoked", new {
            CorrelationId = correlationId,
            BodyLength = requestBody?.Length ?? 0
        });
        
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return CreateMCPErrorResponse(-32600, "Invalid Request: Empty body", null);
        }

        JObject requestObj;
        try
        {
            requestObj = JObject.Parse(requestBody);
        }
        catch
        {
            return CreateMCPErrorResponse(-32700, "Parse error", null);
        }

        var method = requestObj["method"]?.ToString();
        var id = requestObj["id"];
        var paramsObj = requestObj["params"] as JObject;

        switch (method)
        {
            case "initialize":
                return HandleInitialize(id);
            case "initialized":
                return CreateMCPSuccessResponse(new JObject(), id);
            case "ping":
                return CreateMCPSuccessResponse(new JObject(), id);
            case "tools/list":
                return HandleToolsList(id);
            case "tools/call":
                return await HandleToolsCallAsync(paramsObj, id).ConfigureAwait(false);
            case "logging/setLevel":
                return CreateMCPSuccessResponse(new JObject(), id);
            case "notifications/cancelled":
                return CreateMCPSuccessResponse(new JObject(), id);
            default:
                return CreateMCPErrorResponse(-32601, $"Method not found: {method}", id);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken id)
    {
        var result = new JObject
        {
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "SharePoint Term Store",
                ["version"] = "1.0.0"
            }
        };
        return CreateMCPSuccessResponse(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        var result = new JObject { ["tools"] = AVAILABLE_TOOLS };
        return CreateMCPSuccessResponse(result, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject paramsObj, JToken id)
    {
        var toolName = paramsObj?["name"]?.ToString();
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();
        
        this.Context.Logger.LogInformation($"MCP tool invoked: {toolName}");
        var toolStartTime = DateTime.UtcNow();
        
        await LogToAppInsights("MCPToolInvoked", new {
            ToolName = toolName,
            ArgumentCount = arguments.Count
        });

        switch (toolName)
        {
            case "get_term_store":
                return await HandleGetTermStoreAsync(arguments, id).ConfigureAwait(false);
            case "list_term_groups":
                return await HandleListTermGroupsAsync(arguments, id).ConfigureAwait(false);
            case "create_term_group":
                return await HandleCreateTermGroupAsync(arguments, id).ConfigureAwait(false);
            case "list_term_sets":
                return await HandleListTermSetsAsync(arguments, id).ConfigureAwait(false);
            case "create_term_set":
                return await HandleCreateTermSetAsync(arguments, id).ConfigureAwait(false);
            case "list_terms":
                return await HandleListTermsAsync(arguments, id).ConfigureAwait(false);
            case "create_term":
                return await HandleCreateTermAsync(arguments, id).ConfigureAwait(false);
            case "list_child_terms":
                return await HandleListChildTermsAsync(arguments, id).ConfigureAwait(false);
            case "create_child_term":
                return await HandleCreateChildTermAsync(arguments, id).ConfigureAwait(false);
            case "update_term":
                return await HandleUpdateTermAsync(arguments, id).ConfigureAwait(false);
            case "delete_term":
                return await HandleDeleteTermAsync(arguments, id).ConfigureAwait(false);
            default:
                return CreateMCPErrorResponse(-32602, $"Unknown tool: {toolName}", id);
        }
    }

    #endregion

    #region MCP Tool Implementations

    private async Task<HttpResponseMessage> HandleGetTermStoreAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore";
        
        var response = await CallGraphApiAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        
        var text = $"Term Store ID: {data["id"]}\n" +
                   $"Default Language: {data["defaultLanguageTag"]}\n" +
                   $"Available Languages: {string.Join(", ", data["languageTags"] ?? new JArray())}";
        
        return CreateMCPTextResponse(text, id);
    }

    private async Task<HttpResponseMessage> HandleListTermGroupsAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/groups";
        
        var response = await CallGraphApiAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        var groups = data["value"] as JArray ?? new JArray();
        
        var text = new StringBuilder();
        text.AppendLine($"Found {groups.Count} term group(s):\n");
        
        foreach (var group in groups)
        {
            text.AppendLine($"• {group["displayName"]} (ID: {group["id"]})");
            text.AppendLine($"  Scope: {group["scope"]}");
            if (group["description"] != null && !string.IsNullOrWhiteSpace(group["description"].ToString()))
            {
                text.AppendLine($"  Description: {group["description"]}");
            }
            text.AppendLine();
        }
        
        return CreateMCPTextResponse(text.ToString(), id);
    }

    private async Task<HttpResponseMessage> HandleCreateTermGroupAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var displayName = arguments["displayName"]?.ToString();
        var description = arguments["description"]?.ToString();
        
        var body = new JObject
        {
            ["displayName"] = displayName
        };
        
        if (!string.IsNullOrWhiteSpace(description))
        {
            body["description"] = description;
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/groups";
        var response = await CallGraphApiAsync(url, HttpMethod.Post, body.ToString()).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode} - {error}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        
        var text = $"✓ Created term group '{displayName}'\nGroup ID: {data["id"]}";
        return CreateMCPTextResponse(text, id);
    }

    private async Task<HttpResponseMessage> HandleListTermSetsAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var groupId = arguments["groupId"]?.ToString();
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/groups/{groupId}/sets";
        
        var response = await CallGraphApiAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        var sets = data["value"] as JArray ?? new JArray();
        
        var text = new StringBuilder();
        text.AppendLine($"Found {sets.Count} term set(s):\n");
        
        foreach (var set in sets)
        {
            var localizedNames = set["localizedNames"] as JArray;
            var firstNamematched = localizedNames?.FirstOrDefault()?["name"]?.ToString();
            text.AppendLine($"• {firstNamematched} (ID: {set["id"]})");
            if (set["description"] != null && !string.IsNullOrWhiteSpace(set["description"].ToString()))
            {
                text.AppendLine($"  Description: {set["description"]}");
            }
            text.AppendLine();
        }
        
        return CreateMCPTextResponse(text.ToString(), id);
    }

    private async Task<HttpResponseMessage> HandleCreateTermSetAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var groupId = arguments["groupId"]?.ToString();
        var displayName = arguments["displayName"]?.ToString();
        var description = arguments["description"]?.ToString();
        var languageTag = arguments["languageTag"]?.ToString() ?? "en-US";
        
        var body = new JObject
        {
            ["localizedNames"] = new JArray
            {
                new JObject
                {
                    ["languageTag"] = languageTag,
                    ["name"] = displayName
                }
            }
        };
        
        if (!string.IsNullOrWhiteSpace(description))
        {
            body["description"] = description;
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/groups/{groupId}/sets";
        var response = await CallGraphApiAsync(url, HttpMethod.Post, body.ToString()).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode} - {error}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        
        var text = $"✓ Created term set '{displayName}'\nSet ID: {data["id"]}";
        return CreateMCPTextResponse(text, id);
    }

    private async Task<HttpResponseMessage> HandleListTermsAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var setId = arguments["setId"]?.ToString();
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/sets/{setId}/terms";
        
        var response = await CallGraphApiAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        var terms = data["value"] as JArray ?? new JArray();
        
        return CreateTermListResponse(terms, id);
    }

    private async Task<HttpResponseMessage> HandleCreateTermAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var setId = arguments["setId"]?.ToString();
        var label = arguments["label"]?.ToString();
        var languageTag = arguments["languageTag"]?.ToString() ?? "en-US";
        var description = arguments["description"]?.ToString();
        
        var body = new JObject
        {
            ["labels"] = new JArray
            {
                new JObject
                {
                    ["languageTag"] = languageTag,
                    ["name"] = label,
                    ["isDefault"] = true
                }
            }
        };
        
        if (!string.IsNullOrWhiteSpace(description))
        {
            body["descriptions"] = new JArray
            {
                new JObject
                {
                    ["languageTag"] = languageTag,
                    ["description"] = description
                }
            };
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/sets/{setId}/terms";
        var response = await CallGraphApiAsync(url, HttpMethod.Post, body.ToString()).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode} - {error}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        
        var text = $"✓ Created term '{label}'\nTerm ID: {data["id"]}";
        return CreateMCPTextResponse(text, id);
    }

    private async Task<HttpResponseMessage> HandleListChildTermsAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var setId = arguments["setId"]?.ToString();
        var termId = arguments["termId"]?.ToString();
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/sets/{setId}/terms/{termId}/children";
        
        var response = await CallGraphApiAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        var terms = data["value"] as JArray ?? new JArray();
        
        return CreateTermListResponse(terms, id);
    }

    private async Task<HttpResponseMessage> HandleCreateChildTermAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var setId = arguments["setId"]?.ToString();
        var termId = arguments["termId"]?.ToString();
        var label = arguments["label"]?.ToString();
        var languageTag = arguments["languageTag"]?.ToString() ?? "en-US";
        var description = arguments["description"]?.ToString();
        
        var body = new JObject
        {
            ["labels"] = new JArray
            {
                new JObject
                {
                    ["languageTag"] = languageTag,
                    ["name"] = label,
                    ["isDefault"] = true
                }
            }
        };
        
        if (!string.IsNullOrWhiteSpace(description))
        {
            body["descriptions"] = new JArray
            {
                new JObject
                {
                    ["languageTag"] = languageTag,
                    ["description"] = description
                }
            };
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/sets/{setId}/terms/{termId}/children";
        var response = await CallGraphApiAsync(url, HttpMethod.Post, body.ToString()).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode} - {error}", id);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        
        var text = $"✓ Created child term '{label}'\nTerm ID: {data["id"]}";
        return CreateMCPTextResponse(text, id);
    }

    private async Task<HttpResponseMessage> HandleUpdateTermAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var setId = arguments["setId"]?.ToString();
        var termId = arguments["termId"]?.ToString();
        
        var body = new JObject();
        
        if (arguments["labels"] != null)
        {
            body["labels"] = arguments["labels"];
        }
        
        if (arguments["descriptions"] != null)
        {
            body["descriptions"] = arguments["descriptions"];
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/sets/{setId}/terms/{termId}";
        var response = await CallGraphApiAsync(url, new HttpMethod("PATCH"), body.ToString()).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode} - {error}", id);
        }

        return CreateMCPTextResponse($"✓ Updated term {termId}", id);
    }

    private async Task<HttpResponseMessage> HandleDeleteTermAsync(JObject arguments, JToken id)
    {
        var siteId = arguments["siteId"]?.ToString() ?? "root";
        var setId = arguments["setId"]?.ToString();
        var termId = arguments["termId"]?.ToString();
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/termStore/sets/{setId}/terms/{termId}";
        var response = await CallGraphApiAsync(url, HttpMethod.Delete).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return CreateMCPErrorResponse(-32000, $"Graph API error: {response.StatusCode} - {error}", id);
        }

        return CreateMCPTextResponse($"✓ Deleted term {termId}", id);
    }

    #endregion

    #region Helper Methods

    private async Task<HttpResponseMessage> CallGraphApiAsync(string url, HttpMethod method = null, string body = null)
    {
        method = method ?? HttpMethod.Get;
        
        var request = new HttpRequestMessage(method, url);
        
        // Copy authorization header from original request
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        
        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private HttpResponseMessage CreateTermListResponse(JArray terms, JToken id)
    {
        var text = new StringBuilder();
        text.AppendLine($"Found {terms.Count} term(s):\n");
        
        foreach (var term in terms)
        {
            var labels = term["labels"] as JArray;
            var defaultLabel = labels?.FirstOrDefault(l => l["isDefault"]?.Value<bool>() == true)?["name"]?.ToString() 
                               ?? labels?.FirstOrDefault()?["name"]?.ToString();
            
            text.AppendLine($"• {defaultLabel} (ID: {term["id"]})");
            
            var descriptions = term["descriptions"] as JArray;
            var description = descriptions?.FirstOrDefault()?["description"]?.ToString();
            if (!string.IsNullOrWhiteSpace(description))
            {
                text.AppendLine($"  Description: {description}");
            }
            text.AppendLine();
        }
        
        return CreateMCPTextResponse(text.ToString(), id);
    }

    private HttpResponseMessage CreateMCPTextResponse(string text, JToken id)
    {
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = text
            }
        };
        
        return CreateMCPSuccessResponse(new JObject { ["content"] = content }, id);
    }

    private HttpResponseMessage CreateMCPSuccessResponse(JObject result, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return CreateJsonResponse(HttpStatusCode.OK, response.ToString());
    }

    private HttpResponseMessage CreateMCPErrorResponse(int code, string message, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return CreateJsonResponse(HttpStatusCode.OK, response.ToString());
    }

    private HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return response;
    }

    private HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string message)
    {
        return CreateJsonResponse(statusCode, $"{{\"error\": \"{EscapeJson(message)}\"}}");
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    #endregion

    #region Application Insights Telemetry

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return; // Telemetry disabled
            }

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
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

    #endregion
}

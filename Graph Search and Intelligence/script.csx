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
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly HashSet<string> SearchOperations = new HashSet<string>
    {
        "SearchMessages", "SearchChatMessages", "SearchEvents", "SearchFiles",
        "SearchSites", "SearchListItems", "SearchExternal", "SearchInterleaved"
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var opId = this.Context.OperationId;

        try
        {
            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = opId,
                Path = this.Context.Request.RequestUri.AbsolutePath,
                Method = this.Context.Request.Method.Method
            });

            HttpResponseMessage response;
            if (opId == "InvokeMCP")
                response = await HandleMcpAsync(correlationId);
            else if (SearchOperations.Contains(opId))
                response = await HandleSearchAsync(opId, correlationId);
            else
                response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = opId,
                StatusCode = (int)response.StatusCode,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = opId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
            throw;
        }
    }

    #region Search Dispatch

    private async Task<HttpResponseMessage> HandleSearchAsync(string opId, string correlationId)
    {
        var body = JObject.Parse(await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false));
        var queryString = body["queryString"]?.ToString() ?? "";
        var from = body["from"]?.Value<int?>() ?? 0;
        var size = body["size"]?.Value<int?>() ?? 25;

        string[] entityTypes;
        switch (opId)
        {
            case "SearchMessages": entityTypes = new[] { "message" }; break;
            case "SearchChatMessages": entityTypes = new[] { "chatMessage" }; break;
            case "SearchEvents": entityTypes = new[] { "event" }; break;
            case "SearchFiles": entityTypes = new[] { "driveItem" }; break;
            case "SearchSites": entityTypes = new[] { "site" }; break;
            case "SearchListItems": entityTypes = new[] { "listItem" }; break;
            case "SearchExternal": entityTypes = new[] { "externalItem" }; break;
            case "SearchInterleaved": entityTypes = new[] { "message", "chatMessage" }; break;
            default: throw new Exception($"Unknown search operation: {opId}");
        }

        var requestObj = new JObject
        {
            ["entityTypes"] = new JArray(entityTypes),
            ["query"] = new JObject { ["queryString"] = queryString },
            ["from"] = from,
            ["size"] = size
        };

        if (opId == "SearchMessages" && body["enableTopResults"] != null)
            requestObj["enableTopResults"] = body["enableTopResults"].Value<bool>();

        if (opId == "SearchFiles" || opId == "SearchListItems" || opId == "SearchExternal")
        {
            var fieldsStr = body["fields"]?.ToString();
            if (!string.IsNullOrEmpty(fieldsStr))
                requestObj["fields"] = new JArray(fieldsStr.Split(',').Select(f => f.Trim()));
        }

        if (opId == "SearchExternal")
        {
            var cs = body["contentSources"]?.ToString();
            if (!string.IsNullOrEmpty(cs))
                requestObj["contentSources"] = new JArray(cs.Split(',').Select(c => c.Trim()));
        }

        if (opId == "SearchFiles")
        {
            var sortBy = body["sortBy"]?.ToString();
            var sortOrder = body["sortOrder"]?.ToString() ?? "desc";
            if (!string.IsNullOrEmpty(sortBy))
            {
                requestObj["sortProperties"] = new JArray
                {
                    new JObject { ["name"] = sortBy, ["isDescending"] = sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase) }
                };
            }
        }

        var searchBody = new JObject
        {
            ["requests"] = new JArray { requestObj }
        };

        await LogToAppInsights("SearchDispatched", new
        {
            CorrelationId = correlationId,
            OperationId = opId,
            EntityTypes = string.Join(",", entityTypes)
        });

        var url = "https://graph.microsoft.com/v1.0/search/query";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        req.Content = new StringContent(searchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        return await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region MCP Handler

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        var handler = new McpRequestHandler(new McpServerInfo
        {
            Name = "graph-search-intelligence",
            Version = "1.0.0",
            ProtocolVersion = "2025-03-26"
        });

        RegisterSearchTools(handler);
        RegisterInsightTools(handler);
        RegisterPeopleTools(handler);
        RegisterDriveTools(handler);

        var parsed = JObject.Parse(body);
        var mcpMethod = parsed["method"]?.ToString() ?? "unknown";
        var toolName = parsed["params"]?["name"]?.ToString();

        await LogToAppInsights("McpRequestReceived", new
        {
            CorrelationId = correlationId,
            McpMethod = mcpMethod,
            ToolName = toolName ?? ""
        });

        var result = await handler.HandleAsync(body);

        var isError = result["error"] != null || (result["result"]?["isError"]?.Value<bool>() == true);
        await LogToAppInsights("McpRequestProcessed", new
        {
            CorrelationId = correlationId,
            McpMethod = mcpMethod,
            ToolName = toolName ?? "",
            IsError = isError
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(result.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return response;
    }

    #endregion

    #region Search Tools

    private void RegisterSearchTools(McpRequestHandler h)
    {
        // 1. search_messages
        h.AddTool("search_messages",
            "Search Outlook email messages using KQL. Supports field-scoped queries: subject:, from:, to:, cc:, hasAttachment:true, received>=2025-01-01.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"subject:quarterly AND from:john\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 500)" },
                    enableTopResults = new { type = "boolean", description = "Enable hybrid sort for most relevant messages first" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) =>
            {
                var searchBody = BuildSearchBody("message", args);
                if (args["enableTopResults"] != null)
                    ((JArray)searchBody["requests"])[0]["enableTopResults"] = args["enableTopResults"].Value<bool>();
                return await GraphPostAsync("/search/query", searchBody);
            },
            annotations: new { title = "Search Messages", readOnlyHint = true, openWorldHint = true });

        // 2. search_chat_messages
        h.AddTool("search_chat_messages",
            "Search Teams chat and channel messages using KQL. Supports mentions:{userId}, IsMentioned:true, from:user, hasAttachment:true.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"IsMentioned:true\" or \"from:john\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 500)" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) => await GraphPostAsync("/search/query", BuildSearchBody("chatMessage", args)),
            annotations: new { title = "Search Chat Messages", readOnlyHint = true, openWorldHint = true });

        // 3. search_events
        h.AddTool("search_events",
            "Search calendar events using KQL. Supports subject:, body:, organizer:, attendees: fields. Max page size is 25.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"subject:standup\" or \"organizer:cathy\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 25)" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) => await GraphPostAsync("/search/query", BuildSearchBody("event", args)),
            annotations: new { title = "Search Events", readOnlyHint = true, openWorldHint = true });

        // 4. search_files
        h.AddTool("search_files",
            "Search OneDrive and SharePoint files using KQL via the Graph Search API. Supports filename:, filetype:docx, author:, path:, content keywords. Can specify fields to return and sort order.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"filetype:docx budget\" or \"author:troy\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 500)" },
                    fields = new { type = "string", description = "Comma-separated fields to return (e.g. \"name,webUrl,lastModifiedDateTime\")" },
                    sortBy = new { type = "string", description = "Property to sort by (e.g. \"lastModifiedDateTime\")" },
                    sortOrder = new { type = "string", description = "Sort direction: asc or desc (default desc)" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) =>
            {
                var searchBody = BuildSearchBody("driveItem", args, includeFields: true);
                AddSortProperties(searchBody, args);
                return await GraphPostAsync("/search/query", searchBody);
            },
            annotations: new { title = "Search Files", readOnlyHint = true, openWorldHint = true });

        // 5. search_sites
        h.AddTool("search_sites",
            "Search SharePoint sites using KQL. Finds sites by name, description, or content.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"marketing\" or \"contoso\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 500)" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) => await GraphPostAsync("/search/query", BuildSearchBody("site", args)),
            annotations: new { title = "Search Sites", readOnlyHint = true, openWorldHint = true });

        // 6. search_list_items
        h.AddTool("search_list_items",
            "Search SharePoint list items using KQL. Can return custom columns by specifying fields.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"status:active\" or \"project plan\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 500)" },
                    fields = new { type = "string", description = "Comma-separated fields to return (e.g. \"Title,Status,DueDate\")" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) => await GraphPostAsync("/search/query", BuildSearchBody("listItem", args, includeFields: true)),
            annotations: new { title = "Search List Items", readOnlyHint = true, openWorldHint = true });

        // 7. search_external
        h.AddTool("search_external",
            "Search external content ingested via Microsoft Graph connectors. Requires contentSources to specify which connection(s) to search.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query (e.g. \"sales report\")" },
                    contentSources = new { type = "string", description = "Comma-separated connection IDs (e.g. \"/external/connections/myConnection\" or \"/external/connections/*\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25, max 500)" },
                    fields = new { type = "string", description = "Comma-separated fields to return from the external schema" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) =>
            {
                var searchBody = BuildSearchBody("externalItem", args, includeFields: true);
                var cs = args["contentSources"]?.ToString();
                if (!string.IsNullOrEmpty(cs))
                    ((JArray)searchBody["requests"])[0]["contentSources"] = new JArray(cs.Split(',').Select(c => c.Trim()));
                return await GraphPostAsync("/search/query", searchBody);
            },
            annotations: new { title = "Search External Items", readOnlyHint = true, openWorldHint = true });

        // 8. search_interleaved
        h.AddTool("search_interleaved",
            "Search across Outlook messages and Teams chat messages in a single interleaved call. Returns relevance-ranked results from both sources.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    queryString = new { type = "string", description = "KQL query to search both email and Teams (e.g. \"project update\" or \"from:cathy\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 25)" }
                },
                required = new[] { "queryString" }
            },
            handler: async (args) =>
            {
                var searchBody = BuildSearchBody(new[] { "message", "chatMessage" }, args);
                return await GraphPostAsync("/search/query", searchBody);
            },
            annotations: new { title = "Search Interleaved", readOnlyHint = true, openWorldHint = true });

        // 9. semantic_search_files
        h.AddTool("semantic_search_files",
            "Perform hybrid semantic and lexical search across OneDrive for work or school content using natural language. Requires Microsoft 365 Copilot license. Beta endpoint.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Natural language query (e.g. \"Find the board deck from last quarter\")" },
                    filter = new { type = "string", description = "KQL path filter (e.g. \"filetype:docx\")" },
                    from = new { type = "integer", description = "Pagination offset (default 0)" },
                    size = new { type = "integer", description = "Page size (default 10)" }
                },
                required = new[] { "query" }
            },
            handler: async (args) =>
            {
                var body = new JObject { ["query"] = args["query"]?.ToString() };
                if (args["filter"] != null) body["filter"] = args["filter"].ToString();
                if (args["from"] != null) body["from"] = args["from"].Value<int>();
                if (args["size"] != null) body["size"] = args["size"].Value<int>();
                return await GraphBetaPostAsync("/copilot/search", body);
            },
            annotations: new { title = "Semantic Search Files", readOnlyHint = true, openWorldHint = true });
    }

    #endregion

    #region Insight Tools

    private void RegisterInsightTools(McpRequestHandler h)
    {
        // 10. list_trending_documents
        h.AddTool("list_trending_documents",
            "Get documents trending around the signed-in user based on activity of their closest network. Uses ML-powered analytics across OneDrive and SharePoint.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    top = new { type = "integer", description = "Number of results (e.g. 10)" },
                    filter = new { type = "string", description = "OData filter (e.g. \"resourceVisualization/type eq 'PowerPoint'\")" },
                    orderby = new { type = "string", description = "Sort order (e.g. \"weight desc\")" }
                }
            },
            handler: async (args) =>
            {
                var query = BuildODataQuery(args);
                return await GraphGetAsync($"/me/insights/trending{query}");
            },
            annotations: new { title = "List Trending Documents", readOnlyHint = true, openWorldHint = true });

        // 11. list_shared_documents
        h.AddTool("list_shared_documents",
            "Get documents shared with or by the signed-in user, ordered by recency. Includes URLs, attachments, and reference attachments from OneDrive, SharePoint, Outlook, and Teams.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    top = new { type = "integer", description = "Number of results (e.g. 10)" },
                    filter = new { type = "string", description = "OData filter (e.g. \"lastShared/sharedBy/address eq 'cathy@contoso.com'\")" },
                    orderby = new { type = "string", description = "Sort order" }
                }
            },
            handler: async (args) =>
            {
                var query = BuildODataQuery(args);
                return await GraphGetAsync($"/me/insights/shared{query}");
            },
            annotations: new { title = "List Shared Documents", readOnlyHint = true, openWorldHint = true });

        // 12. list_used_documents
        h.AddTool("list_used_documents",
            "Get documents recently viewed or modified by the signed-in user, ranked by recency. Includes OneDrive and SharePoint documents.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    top = new { type = "integer", description = "Number of results (e.g. 10)" },
                    filter = new { type = "string", description = "OData filter (e.g. \"resourceVisualization/type eq 'Excel'\")" },
                    orderby = new { type = "string", description = "Sort order" }
                }
            },
            handler: async (args) =>
            {
                var query = BuildODataQuery(args);
                return await GraphGetAsync($"/me/insights/used{query}");
            },
            annotations: new { title = "List Used Documents", readOnlyHint = true, openWorldHint = true });
    }

    #endregion

    #region People Tools

    private void RegisterPeopleTools(McpRequestHandler h)
    {
        // 13. list_relevant_people
        h.AddTool("list_relevant_people",
            "Get people ranked by relevance to the signed-in user based on communication patterns, collaboration signals, and business relationships. Supports searching by name or email.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Search for people by name or email (e.g. \"cathy\")" },
                    top = new { type = "integer", description = "Number of people to return (default 10)" },
                    filter = new { type = "string", description = "OData filter (e.g. \"personType/class eq 'Person'\")" },
                    select = new { type = "string", description = "Fields to return (e.g. \"displayName,scoredEmailAddresses,jobTitle\")" },
                    orderby = new { type = "string", description = "Sort order (e.g. \"displayName\")" }
                }
            },
            handler: async (args) =>
            {
                var query = BuildODataQuery(args);
                return await GraphGetAsync($"/me/people{query}");
            },
            annotations: new { title = "List Relevant People", readOnlyHint = true, openWorldHint = true });
    }

    #endregion

    #region Drive Tools

    private void RegisterDriveTools(McpRequestHandler h)
    {
        // 14. list_recent_files
        h.AddTool("list_recent_files",
            "List files recently accessed by the signed-in user in OneDrive. NOTE: This API is deprecated and will stop returning data after November 2026. Use list_used_documents as an alternative.",
            schema: new
            {
                type = "object",
                properties = new { }
            },
            handler: async (args) => await GraphGetAsync("/me/drive/recent"),
            annotations: new { title = "List Recent Files (Deprecated)", readOnlyHint = true, openWorldHint = true });

        // 15. list_shared_with_me
        h.AddTool("list_shared_with_me",
            "Get files shared with the signed-in user in OneDrive. NOTE: This API is deprecated and will stop returning data after November 2026. Use list_shared_documents as an alternative.",
            schema: new
            {
                type = "object",
                properties = new { }
            },
            handler: async (args) => await GraphGetAsync("/me/drive/sharedWithMe"),
            annotations: new { title = "List Shared With Me (Deprecated)", readOnlyHint = true, openWorldHint = true });

        // 16. get_file_metadata
        h.AddTool("get_file_metadata",
            "Get metadata for a specific file or folder in the signed-in user's OneDrive, including name, size, last modified, author, and parent location.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    itemId = new { type = "string", description = "The unique ID of the drive item" },
                    select = new { type = "string", description = "Fields to return (e.g. \"name,size,webUrl,lastModifiedDateTime\")" }
                },
                required = new[] { "itemId" }
            },
            handler: async (args) =>
            {
                var id = args["itemId"].ToString();
                var sel = args["select"]?.ToString();
                var qs = !string.IsNullOrEmpty(sel) ? $"?$select={Uri.EscapeDataString(sel)}" : "";
                return await GraphGetAsync($"/me/drive/items/{id}{qs}");
            },
            annotations: new { title = "Get File Metadata", readOnlyHint = true, openWorldHint = true });

        // 17. get_file_content
        h.AddTool("get_file_content",
            "Get a pre-authenticated download URL for a file in the signed-in user's OneDrive. Returns the download URL from the @microsoft.graph.downloadUrl property.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    itemId = new { type = "string", description = "The unique ID of the file to download" }
                },
                required = new[] { "itemId" }
            },
            handler: async (args) =>
            {
                var id = args["itemId"].ToString();
                var metadata = await GraphGetAsync($"/me/drive/items/{id}?$select=name,@microsoft.graph.downloadUrl");
                return metadata;
            },
            annotations: new { title = "Get File Content", readOnlyHint = true, openWorldHint = true });

        // 18. list_folder_children
        h.AddTool("list_folder_children",
            "List the contents of a folder in the signed-in user's OneDrive. Returns files and subfolders with metadata.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    itemId = new { type = "string", description = "Folder ID (use 'root' for the root folder)" },
                    top = new { type = "integer", description = "Number of items to return (default 200)" },
                    select = new { type = "string", description = "Fields to return (e.g. \"name,size,webUrl\")" },
                    orderby = new { type = "string", description = "Sort order (e.g. \"name asc\" or \"lastModifiedDateTime desc\")" }
                },
                required = new[] { "itemId" }
            },
            handler: async (args) =>
            {
                var id = args["itemId"].ToString();
                var parts = new List<string>();
                var topVal = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(topVal)) parts.Add($"$top={Uri.EscapeDataString(topVal)}");
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var ob = args["orderby"]?.ToString();
                if (!string.IsNullOrEmpty(ob)) parts.Add($"$orderby={Uri.EscapeDataString(ob)}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/me/drive/items/{id}/children{qs}");
            },
            annotations: new { title = "List Folder Children", readOnlyHint = true, openWorldHint = true });

        // 19. search_my_drive
        h.AddTool("search_my_drive",
            "Search for files in the signed-in user's OneDrive by name or content. Lightweight drive-scoped search.",
            schema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search text to find in file names and content" },
                    top = new { type = "integer", description = "Number of results to return" },
                    select = new { type = "string", description = "Fields to return (e.g. \"name,size,webUrl\")" }
                },
                required = new[] { "query" }
            },
            handler: async (args) =>
            {
                var q = Uri.EscapeDataString(args["query"].ToString());
                var parts = new List<string>();
                var topVal = args["top"]?.ToString();
                if (!string.IsNullOrEmpty(topVal)) parts.Add($"$top={Uri.EscapeDataString(topVal)}");
                var sel = args["select"]?.ToString();
                if (!string.IsNullOrEmpty(sel)) parts.Add($"$select={Uri.EscapeDataString(sel)}");
                var qs = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
                return await GraphGetAsync($"/me/drive/root/search(q='{q}'){qs}");
            },
            annotations: new { title = "Search My Drive", readOnlyHint = true, openWorldHint = true });
    }

    #endregion

    #region Search Helpers

    private JObject BuildSearchBody(string entityType, JObject args, bool includeFields = false)
    {
        return BuildSearchBody(new[] { entityType }, args, includeFields);
    }

    private JObject BuildSearchBody(string[] entityTypes, JObject args, bool includeFields = false)
    {
        var request = new JObject
        {
            ["entityTypes"] = new JArray(entityTypes),
            ["query"] = new JObject { ["queryString"] = args["queryString"]?.ToString() ?? "" },
            ["from"] = args["from"]?.Value<int?>() ?? 0,
            ["size"] = args["size"]?.Value<int?>() ?? 25
        };

        if (includeFields)
        {
            var fieldsStr = args["fields"]?.ToString();
            if (!string.IsNullOrEmpty(fieldsStr))
                request["fields"] = new JArray(fieldsStr.Split(',').Select(f => f.Trim()));
        }

        return new JObject { ["requests"] = new JArray { request } };
    }

    private void AddSortProperties(JObject searchBody, JObject args)
    {
        var sortBy = args["sortBy"]?.ToString();
        if (string.IsNullOrEmpty(sortBy)) return;
        var sortOrder = args["sortOrder"]?.ToString() ?? "desc";
        ((JArray)searchBody["requests"])[0]["sortProperties"] = new JArray
        {
            new JObject
            {
                ["name"] = sortBy,
                ["isDescending"] = sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase)
            }
        };
    }

    #endregion

    #region Graph API Helpers

    private async Task<JToken> GraphGetAsync(string path, string consistencyLevel = null)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(consistencyLevel))
            req.Headers.Add("ConsistencyLevel", consistencyLevel);
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        if (string.IsNullOrWhiteSpace(content)) return new JObject { ["status"] = "success" };
        return JToken.Parse(content);
    }

    private async Task<JToken> GraphPostAsync(string path, JObject body)
    {
        var url = $"https://graph.microsoft.com/v1.0{path}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (body != null)
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        if (string.IsNullOrWhiteSpace(content)) return new JObject { ["status"] = "success" };
        return JToken.Parse(content);
    }

    private async Task<JToken> GraphBetaPostAsync(string path, JObject body)
    {
        var url = $"https://graph.microsoft.com/beta{path}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = this.Context.Request.Headers.Authorization;
        if (body != null)
            req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Graph API error {(int)resp.StatusCode}: {content}");
        if (string.IsNullOrWhiteSpace(content)) return new JObject { ["status"] = "success" };
        return JToken.Parse(content);
    }

    #endregion

    #region Utility Helpers

    private string BuildODataQuery(JObject args)
    {
        var parts = new List<string>();
        var map = new Dictionary<string, string>
        {
            { "search", "$search" }, { "filter", "$filter" },
            { "top", "$top" }, { "orderby", "$orderby" }, { "select", "$select" }
        };
        foreach (var kv in map)
        {
            var val = args[kv.Key]?.ToString();
            if (string.IsNullOrEmpty(val)) continue;
            if (kv.Key == "search")
            {
                var sv = val.StartsWith("\"") ? val : $"\"{val}\"";
                parts.Add($"$search={Uri.EscapeDataString(sv)}");
            }
            else
            {
                parts.Add($"{kv.Value}={Uri.EscapeDataString(val)}");
            }
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    #endregion

    #region Application Insights

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return;

            var propsDict = new Dictionary<string, string>();
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
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("InstrumentationKey=".Length);
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    #endregion
}

#region MCP Framework

public class McpServerInfo
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string ProtocolVersion { get; set; }
}

public class McpToolDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public Func<JObject, Task<object>> Handler { get; set; }
    public JObject Annotations { get; set; }
}

public class McpRequestHandler
{
    private readonly McpServerInfo _serverInfo;
    private readonly List<McpToolDefinition> _tools = new List<McpToolDefinition>();

    public McpRequestHandler(McpServerInfo serverInfo)
    {
        _serverInfo = serverInfo;
    }

    public McpRequestHandler AddTool(string name, string description,
        object schema, Func<JObject, Task<object>> handler, object annotations = null)
    {
        _tools.Add(new McpToolDefinition
        {
            Name = name,
            Description = description,
            InputSchema = JObject.FromObject(schema),
            Handler = handler,
            Annotations = annotations != null ? JObject.FromObject(annotations) : null
        });
        return this;
    }

    public async Task<JObject> HandleAsync(string requestBody)
    {
        var request = JObject.Parse(requestBody);
        var method = request["method"]?.ToString();
        var id = request["id"];

        if (id == null || id.Type == JTokenType.Null || method == "notifications/initialized")
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["result"] = new JObject()
            };
        }

        switch (method)
        {
            case "initialize":
                return CreateResponse(id, new JObject
                {
                    ["protocolVersion"] = _serverInfo.ProtocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = _serverInfo.Name,
                        ["version"] = _serverInfo.Version
                    }
                });

            case "tools/list":
                var toolArray = new JArray();
                foreach (var tool in _tools)
                {
                    var toolObj = new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["inputSchema"] = tool.InputSchema
                    };
                    if (tool.Annotations != null)
                        toolObj["annotations"] = tool.Annotations;
                    toolArray.Add(toolObj);
                }
                return CreateResponse(id, new JObject { ["tools"] = toolArray });

            case "tools/call":
                var toolName = request["params"]?["name"]?.ToString();
                var args = request["params"]?["arguments"] as JObject ?? new JObject();
                var matchedTool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (matchedTool == null)
                    return CreateError(id, -32602, $"Unknown tool: {toolName}");
                try
                {
                    var result = await matchedTool.Handler(args);
                    var text = result is string s ? s
                        : JToken.FromObject(result).ToString(Newtonsoft.Json.Formatting.None);
                    return CreateResponse(id, new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "text", ["text"] = text }
                        }
                    });
                }
                catch (Exception ex)
                {
                    return CreateResponse(id, new JObject
                    {
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" }
                        },
                        ["isError"] = true
                    });
                }

            case "ping":
                return CreateResponse(id, new JObject());

            default:
                return CreateError(id, -32601, $"Method not found: {method}");
        }
    }

    private JObject CreateResponse(JToken id, JObject result)
    {
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    }

    private JObject CreateError(JToken id, int code, string message)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JObject { ["code"] = code, ["message"] = message }
        };
    }
}

#endregion
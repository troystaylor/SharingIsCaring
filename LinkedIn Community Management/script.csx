using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // Application Insights configuration
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private const string LINKEDIN_VERSION = "202602";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                Path = Context.Request.RequestUri?.AbsolutePath ?? "unknown"
            });

            HttpResponseMessage response;

            switch (Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId);
                    break;
                case "CreatePost":
                    response = await HandleCreatePostAsync();
                    break;
                case "UpdatePost":
                    response = await HandleUpdatePostAsync();
                    break;
                case "ToggleComments":
                    response = await HandleToggleCommentsAsync();
                    break;
                case "DeleteReaction":
                    response = await HandleDeleteReactionAsync();
                    break;
                case "FinalizeVideoUpload":
                    response = await HandleFinalizeVideoUploadAsync();
                    break;
                default:
                    AddLinkedInHeaders(Context.Request);
                    EncodeUrnsInRequestUri();
                    response = await Context.SendAsync(Context.Request, CancellationToken);
                    break;
            }

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
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
                OperationId = Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });
            throw;
        }
    }

    #region LinkedIn Header & URL Helpers

    private void AddLinkedInHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Linkedin-Version", LINKEDIN_VERSION);
        request.Headers.TryAddWithoutValidation("X-Restli-Protocol-Version", "2.0.0");
    }

    private void EncodeUrnsInRequestUri()
    {
        var uri = Context.Request.RequestUri.ToString();
        var encoded = Regex.Replace(uri, @"urn:li:([^/&?\s#%]+):([^/&?\s#%]+)",
            m => Uri.EscapeDataString($"urn:li:{m.Groups[1].Value}:{m.Groups[2].Value}"));
        if (encoded != uri)
            Context.Request.RequestUri = new Uri(encoded);
    }

    private async Task<JObject> CallLinkedInApi(string method, string path, JObject body = null, Dictionary<string, string> queryParams = null)
    {
        var baseUri = "https://api.linkedin.com/rest";
        var url = $"{baseUri}{path}";

        if (queryParams?.Count > 0)
        {
            var pairs = queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}");
            url += "?" + string.Join("&", pairs);
        }

        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        AddLinkedInHeaders(request);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await Context.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            await LogToAppInsights("LinkedInAPIError", new
            {
                Method = method,
                Path = path,
                StatusCode = (int)response.StatusCode,
                ErrorBody = TruncateForLog(content, 500)
            });
            throw new HttpRequestException($"LinkedIn API returned {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrEmpty(content))
            return new JObject();

        if (content.TrimStart().StartsWith("["))
            return new JObject { ["items"] = JArray.Parse(content) };

        return JObject.Parse(content);
    }

    private async Task<HttpResponseMessage> CallLinkedInApiRaw(string method, string path, JObject body = null, Dictionary<string, string> queryParams = null)
    {
        var baseUri = "https://api.linkedin.com/rest";
        var url = $"{baseUri}{path}";

        if (queryParams?.Count > 0)
        {
            var pairs = queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}");
            url += "?" + string.Join("&", pairs);
        }

        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        AddLinkedInHeaders(request);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        return await Context.SendAsync(request, CancellationToken);
    }

    private string EncodeUrn(string urn)
    {
        if (string.IsNullOrEmpty(urn)) return urn;
        return Uri.EscapeDataString(urn);
    }

    private string TruncateForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value.Substring(0, maxLength) + "...";
    }

    #endregion

    #region REST Operation Handlers

    private async Task<HttpResponseMessage> HandleCreatePostAsync()
    {
        AddLinkedInHeaders(Context.Request);
        EncodeUrnsInRequestUri();
        var response = await Context.SendAsync(Context.Request, CancellationToken);

        // Extract post ID from x-restli-id response header
        if (response.StatusCode == HttpStatusCode.Created)
        {
            IEnumerable<string> restliIds;
            if (response.Headers.TryGetValues("x-restli-id", out restliIds))
            {
                var postId = restliIds.FirstOrDefault();
                var resultBody = new JObject { ["id"] = postId };
                response.Content = new StringContent(
                    resultBody.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8, "application/json");
            }
        }

        return response;
    }

    private async Task<HttpResponseMessage> HandleUpdatePostAsync()
    {
        // Read the user's body and wrap in patch/$set format
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var userBody = JObject.Parse(bodyContent);

        var patchBody = new JObject
        {
            ["patch"] = new JObject
            {
                ["$set"] = userBody
            }
        };

        Context.Request.Content = new StringContent(
            patchBody.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8, "application/json");

        AddLinkedInHeaders(Context.Request);
        EncodeUrnsInRequestUri();
        return await Context.SendAsync(Context.Request, CancellationToken);
    }

    private async Task<HttpResponseMessage> HandleToggleCommentsAsync()
    {
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var userBody = JObject.Parse(bodyContent);

        var patchBody = new JObject
        {
            ["patch"] = new JObject
            {
                ["$set"] = new JObject
                {
                    ["commentsSummary"] = new JObject
                    {
                        ["commentsState"] = userBody["commentsDisabled"]?.ToObject<bool>() == true ? "CLOSED" : "OPEN"
                    }
                }
            }
        };

        Context.Request.Content = new StringContent(
            patchBody.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8, "application/json");

        AddLinkedInHeaders(Context.Request);
        EncodeUrnsInRequestUri();
        return await Context.SendAsync(Context.Request, CancellationToken);
    }

    private async Task<HttpResponseMessage> HandleDeleteReactionAsync()
    {
        // Build compound key URL: /reactions/(actor:{actorUrn},entity:{entityUrn})
        var uri = Context.Request.RequestUri;
        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var actorUrn = queryParams["actorUrn"];
        var entityUrn = queryParams["entityUrn"];

        var compoundKey = $"(actor:{EncodeUrn(actorUrn)},entity:{EncodeUrn(entityUrn)})";
        var newUrl = $"https://api.linkedin.com/rest/reactions/{compoundKey}";

        var request = new HttpRequestMessage(HttpMethod.Delete, newUrl);
        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        AddLinkedInHeaders(request);

        return await Context.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> HandleFinalizeVideoUploadAsync()
    {
        // Rewrite URL from /videos/finalize to /videos?action=finalizeUpload
        var bodyContent = await Context.Request.Content.ReadAsStringAsync();
        var newUrl = "https://api.linkedin.com/rest/videos?action=finalizeUpload";

        var request = new HttpRequestMessage(HttpMethod.Post, newUrl);
        if (Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = Context.Request.Headers.Authorization;

        AddLinkedInHeaders(request);
        request.Content = new StringContent(bodyContent, Encoding.UTF8, "application/json");

        return await Context.SendAsync(request, CancellationToken);
    }

    #endregion

    #region MCP Handler

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var body = await Context.Request.Content.ReadAsStringAsync();
        var request = JObject.Parse(body);

        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        await LogToAppInsights("MCPRequest", new
        {
            CorrelationId = correlationId,
            Method = method,
            HasParams = @params.HasValues
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
                return await HandleToolsCall(@params, requestId, correlationId);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

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
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "linkedin-community-management-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Posts
            CreateTool("create_post", "Create a new LinkedIn post. Supports text, image, video, article, document, poll, and multi-image posts. Requires author URN, commentary text, visibility, and lifecycle state.",
                new JObject
                {
                    ["author"] = new JObject { ["type"] = "string", ["description"] = "Author URN (e.g., urn:li:organization:123 or urn:li:person:456)" },
                    ["commentary"] = new JObject { ["type"] = "string", ["description"] = "Post text content" },
                    ["visibility"] = new JObject { ["type"] = "string", ["description"] = "Visibility: PUBLIC, CONNECTIONS, or LOGGED_IN" },
                    ["lifecycleState"] = new JObject { ["type"] = "string", ["description"] = "State: PUBLISHED or DRAFT" },
                    ["mediaId"] = new JObject { ["type"] = "string", ["description"] = "Media URN for image/video/document attachment (optional)" },
                    ["mediaTitle"] = new JObject { ["type"] = "string", ["description"] = "Media title (optional)" },
                    ["articleUrl"] = new JObject { ["type"] = "string", ["description"] = "Article URL for article posts (optional)" },
                    ["articleTitle"] = new JObject { ["type"] = "string", ["description"] = "Article title (optional)" },
                    ["resharePostUrn"] = new JObject { ["type"] = "string", ["description"] = "Post URN to reshare (optional)" }
                },
                new[] { "author", "commentary", "visibility", "lifecycleState" }),

            CreateTool("get_post", "Retrieve a LinkedIn post by its URN.",
                new JObject
                {
                    ["post_urn"] = new JObject { ["type"] = "string", ["description"] = "Post URN (e.g., urn:li:ugcPost:12345 or urn:li:share:12345)" }
                },
                new[] { "post_urn" }),

            CreateTool("find_posts_by_author", "Find posts authored by a specific member or organization.",
                new JObject
                {
                    ["author_urn"] = new JObject { ["type"] = "string", ["description"] = "Author URN (e.g., urn:li:organization:123)" },
                    ["count"] = new JObject { ["type"] = "integer", ["description"] = "Number of posts to return (default 10, max 100)" },
                    ["start"] = new JObject { ["type"] = "integer", ["description"] = "Pagination start index (default 0)" },
                    ["sort_by"] = new JObject { ["type"] = "string", ["description"] = "Sort: LAST_MODIFIED or CREATED" }
                },
                new[] { "author_urn" }),

            CreateTool("update_post", "Update an existing LinkedIn post. Can change commentary, reshare settings, etc.",
                new JObject
                {
                    ["post_urn"] = new JObject { ["type"] = "string", ["description"] = "Post URN to update" },
                    ["commentary"] = new JObject { ["type"] = "string", ["description"] = "Updated post text (optional)" },
                    ["isReshareDisabledByAuthor"] = new JObject { ["type"] = "boolean", ["description"] = "Toggle resharing (optional)" }
                },
                new[] { "post_urn" }),

            CreateTool("delete_post", "Delete a LinkedIn post.",
                new JObject
                {
                    ["post_urn"] = new JObject { ["type"] = "string", ["description"] = "Post URN to delete" }
                },
                new[] { "post_urn" }),

            // Comments
            CreateTool("get_comments", "Get comments on a LinkedIn post or entity.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN to get comments for (e.g., urn:li:ugcPost:12345)" },
                    ["count"] = new JObject { ["type"] = "integer", ["description"] = "Number of comments to return" },
                    ["start"] = new JObject { ["type"] = "integer", ["description"] = "Pagination start index" }
                },
                new[] { "entity_urn" }),

            CreateTool("create_comment", "Create a comment on a LinkedIn post.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN to comment on" },
                    ["actor_urn"] = new JObject { ["type"] = "string", ["description"] = "Actor URN posting the comment (e.g., urn:li:organization:123)" },
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "Comment text" },
                    ["parent_comment"] = new JObject { ["type"] = "string", ["description"] = "Parent comment URN for threaded replies (optional)" }
                },
                new[] { "entity_urn", "actor_urn", "text" }),

            CreateTool("get_comment", "Get a specific comment by ID.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN the comment belongs to" },
                    ["comment_id"] = new JObject { ["type"] = "string", ["description"] = "Comment ID" }
                },
                new[] { "entity_urn", "comment_id" }),

            CreateTool("delete_comment", "Delete a comment from a LinkedIn post.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN the comment belongs to" },
                    ["comment_id"] = new JObject { ["type"] = "string", ["description"] = "Comment ID to delete" },
                    ["actor_urn"] = new JObject { ["type"] = "string", ["description"] = "Actor URN performing the delete" }
                },
                new[] { "entity_urn", "comment_id", "actor_urn" }),

            // Reactions
            CreateTool("get_reactions", "Get reactions on a LinkedIn post or entity.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN to get reactions for" },
                    ["count"] = new JObject { ["type"] = "integer", ["description"] = "Number of reactions to return" },
                    ["start"] = new JObject { ["type"] = "integer", ["description"] = "Pagination start index" }
                },
                new[] { "entity_urn" }),

            CreateTool("create_reaction", "React to a LinkedIn post or comment.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN to react to" },
                    ["actor_urn"] = new JObject { ["type"] = "string", ["description"] = "Actor URN creating the reaction" },
                    ["reaction_type"] = new JObject { ["type"] = "string", ["description"] = "Reaction type: LIKE, PRAISE, EMPATHY, INTEREST, APPRECIATION, ENTERTAINMENT, or MAYBE" }
                },
                new[] { "entity_urn", "actor_urn", "reaction_type" }),

            CreateTool("delete_reaction", "Remove a reaction from a LinkedIn post or comment.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN the reaction is on" },
                    ["actor_urn"] = new JObject { ["type"] = "string", ["description"] = "Actor URN who reacted" }
                },
                new[] { "entity_urn", "actor_urn" }),

            // Organizations
            CreateTool("get_organization", "Get LinkedIn organization details by numeric ID.",
                new JObject
                {
                    ["org_id"] = new JObject { ["type"] = "string", ["description"] = "Organization numeric ID" }
                },
                new[] { "org_id" }),

            CreateTool("find_org_by_vanity_name", "Look up a LinkedIn organization by its vanity name (URL slug).",
                new JObject
                {
                    ["vanity_name"] = new JObject { ["type"] = "string", ["description"] = "Organization vanity name (e.g., microsoft)" }
                },
                new[] { "vanity_name" }),

            CreateTool("get_follower_count", "Get the total follower count for a LinkedIn organization.",
                new JObject
                {
                    ["org_urn"] = new JObject { ["type"] = "string", ["description"] = "Organization URN (e.g., urn:li:organization:123)" }
                },
                new[] { "org_urn" }),

            CreateTool("find_member_org_access", "Find which organizations the authenticated member has access to, or find admins of a specific organization.",
                new JObject
                {
                    ["query_type"] = new JObject { ["type"] = "string", ["description"] = "Query type: roleAssignee (current member's orgs) or organization (org's admins)" },
                    ["org_urn"] = new JObject { ["type"] = "string", ["description"] = "Organization URN (required when query_type=organization)" },
                    ["role"] = new JObject { ["type"] = "string", ["description"] = "Filter by role: ADMINISTRATOR, DIRECT_SPONSORED_CONTENT_POSTER (optional)" },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "Filter by state: APPROVED or REQUESTED (optional)" }
                },
                new[] { "query_type" }),

            // Social Metadata
            CreateTool("get_social_metadata", "Get social engagement metadata (likes, comments, shares counts) for a post.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN (e.g., urn:li:ugcPost:12345)" }
                },
                new[] { "entity_urn" }),

            CreateTool("toggle_comments", "Enable or disable comments on a LinkedIn post.",
                new JObject
                {
                    ["entity_urn"] = new JObject { ["type"] = "string", ["description"] = "Entity URN (e.g., urn:li:ugcPost:12345)" },
                    ["actor_urn"] = new JObject { ["type"] = "string", ["description"] = "Actor URN performing the toggle" },
                    ["disable"] = new JObject { ["type"] = "boolean", ["description"] = "True to disable comments, false to enable" }
                },
                new[] { "entity_urn", "actor_urn", "disable" }),

            // Media
            CreateTool("initialize_image_upload", "Initialize an image upload and get an upload URL. After receiving the URL, PUT your binary image to it.",
                new JObject
                {
                    ["owner_urn"] = new JObject { ["type"] = "string", ["description"] = "Owner URN (e.g., urn:li:organization:123)" }
                },
                new[] { "owner_urn" }),

            CreateTool("get_image", "Get image details by URN.",
                new JObject
                {
                    ["image_urn"] = new JObject { ["type"] = "string", ["description"] = "Image URN" }
                },
                new[] { "image_urn" }),

            CreateTool("initialize_video_upload", "Initialize a video upload. Returns upload instructions with chunk URLs. Videos must be uploaded in 4MB chunks.",
                new JObject
                {
                    ["owner_urn"] = new JObject { ["type"] = "string", ["description"] = "Owner URN (e.g., urn:li:organization:123)" },
                    ["file_size_bytes"] = new JObject { ["type"] = "integer", ["description"] = "Video file size in bytes" }
                },
                new[] { "owner_urn", "file_size_bytes" }),

            CreateTool("finalize_video_upload", "Finalize a video upload after all chunks are uploaded. Requires the video URN, upload token, and ETags from each chunk.",
                new JObject
                {
                    ["video_urn"] = new JObject { ["type"] = "string", ["description"] = "Video URN from initialize response" },
                    ["upload_token"] = new JObject { ["type"] = "string", ["description"] = "Upload token from initialize response" },
                    ["uploaded_part_ids"] = new JObject { ["type"] = "array", ["description"] = "Array of ETag strings from each chunk upload", ["items"] = new JObject { ["type"] = "string" } }
                },
                new[] { "video_urn", "upload_token", "uploaded_part_ids" }),

            CreateTool("get_video", "Get video details and status by URN.",
                new JObject
                {
                    ["video_urn"] = new JObject { ["type"] = "string", ["description"] = "Video URN" }
                },
                new[] { "video_urn" }),

            CreateTool("initialize_document_upload", "Initialize a document upload. Supports PPT, PPTX, DOC, DOCX, and PDF up to 100MB.",
                new JObject
                {
                    ["owner_urn"] = new JObject { ["type"] = "string", ["description"] = "Owner URN (e.g., urn:li:organization:123)" }
                },
                new[] { "owner_urn" }),

            CreateTool("get_document", "Get document details by URN.",
                new JObject
                {
                    ["document_urn"] = new JObject { ["type"] = "string", ["description"] = "Document URN" }
                },
                new[] { "document_urn" }),

            // Statistics
            CreateTool("get_share_statistics", "Get share/post statistics for an organization. Includes impressions, clicks, engagement, and more. Data available for past 12 months.",
                new JObject
                {
                    ["org_urn"] = new JObject { ["type"] = "string", ["description"] = "Organization URN (e.g., urn:li:organization:123)" },
                    ["start_time"] = new JObject { ["type"] = "integer", ["description"] = "Start time in epoch milliseconds (optional, omit for lifetime stats)" },
                    ["end_time"] = new JObject { ["type"] = "integer", ["description"] = "End time in epoch milliseconds (optional)" },
                    ["granularity"] = new JObject { ["type"] = "string", ["description"] = "Time granularity: DAY or MONTH (optional, required with start/end time)" }
                },
                new[] { "org_urn" }),

            CreateTool("get_page_statistics", "Get page view statistics for an organization, segmented by geography, industry, function, seniority, and staff count.",
                new JObject
                {
                    ["org_urn"] = new JObject { ["type"] = "string", ["description"] = "Organization URN" },
                    ["start_time"] = new JObject { ["type"] = "integer", ["description"] = "Start time in epoch milliseconds (optional)" },
                    ["end_time"] = new JObject { ["type"] = "integer", ["description"] = "End time in epoch milliseconds (optional)" },
                    ["granularity"] = new JObject { ["type"] = "string", ["description"] = "Time granularity: DAY or MONTH (optional)" }
                },
                new[] { "org_urn" }),

            CreateTool("get_follower_statistics", "Get follower demographics and growth statistics for an organization. Lifetime stats include breakdowns by geo, function, industry, seniority.",
                new JObject
                {
                    ["org_urn"] = new JObject { ["type"] = "string", ["description"] = "Organization URN" },
                    ["start_time"] = new JObject { ["type"] = "integer", ["description"] = "Start time in epoch milliseconds (optional, omit for lifetime demographics)" },
                    ["end_time"] = new JObject { ["type"] = "integer", ["description"] = "End time in epoch milliseconds (optional)" },
                    ["granularity"] = new JObject { ["type"] = "string", ["description"] = "Time granularity: DAY, WEEK, or MONTH (optional)" }
                },
                new[] { "org_urn" })
        };

        var result = new JObject { ["tools"] = tools };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private JObject CreateTool(string name, string description, JObject properties, string[] required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(required)
            }
        };
    }

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("MCPToolCall", new
        {
            CorrelationId = correlationId,
            Tool = toolName,
            HasArguments = arguments.HasValues
        });

        try
        {
            var result = await ExecuteToolAsync(toolName, arguments);

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPToolError", new
            {
                CorrelationId = correlationId,
                Tool = toolName,
                ErrorMessage = ex.Message
            });

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

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            // Posts
            case "create_post":
            {
                var body = new JObject
                {
                    ["author"] = args["author"]?.ToString(),
                    ["commentary"] = args["commentary"]?.ToString(),
                    ["visibility"] = args["visibility"]?.ToString(),
                    ["distribution"] = new JObject
                    {
                        ["feedDistribution"] = "MAIN_FEED",
                        ["targetEntities"] = new JArray(),
                        ["thirdPartyDistributionChannels"] = new JArray()
                    },
                    ["lifecycleState"] = args["lifecycleState"]?.ToString()
                };

                // Add optional content
                var mediaId = args["mediaId"]?.ToString();
                var articleUrl = args["articleUrl"]?.ToString();
                var reshareUrn = args["resharePostUrn"]?.ToString();

                if (!string.IsNullOrEmpty(mediaId))
                {
                    var mediaContent = new JObject
                    {
                        ["media"] = new JObject { ["id"] = mediaId }
                    };
                    if (!string.IsNullOrEmpty(args["mediaTitle"]?.ToString()))
                        mediaContent["media"]["title"] = args["mediaTitle"].ToString();
                    body["content"] = mediaContent;
                }
                else if (!string.IsNullOrEmpty(articleUrl))
                {
                    var articleContent = new JObject
                    {
                        ["article"] = new JObject { ["source"] = articleUrl }
                    };
                    if (!string.IsNullOrEmpty(args["articleTitle"]?.ToString()))
                        articleContent["article"]["title"] = args["articleTitle"].ToString();
                    body["content"] = articleContent;
                }

                if (!string.IsNullOrEmpty(reshareUrn))
                {
                    body["reshareContext"] = new JObject { ["parent"] = reshareUrn };
                }

                var response = await CallLinkedInApiRaw("POST", "/posts", body);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Created)
                {
                    IEnumerable<string> restliIds;
                    string postId = null;
                    if (response.Headers.TryGetValues("x-restli-id", out restliIds))
                        postId = restliIds.FirstOrDefault();

                    return new JObject
                    {
                        ["success"] = true,
                        ["id"] = postId,
                        ["message"] = "Post created successfully"
                    };
                }

                throw new HttpRequestException($"LinkedIn API returned {(int)response.StatusCode}: {responseContent}");
            }

            case "get_post":
                return await CallLinkedInApi("GET", $"/posts/{EncodeUrn(args["post_urn"]?.ToString())}");

            case "find_posts_by_author":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = "author",
                    ["author"] = args["author_urn"]?.ToString()
                };
                if (args["count"] != null) qp["count"] = args["count"].ToString();
                if (args["start"] != null) qp["start"] = args["start"].ToString();
                if (!string.IsNullOrEmpty(args["sort_by"]?.ToString())) qp["sortBy"] = args["sort_by"].ToString();
                return await CallLinkedInApi("GET", "/posts", queryParams: qp);
            }

            case "update_post":
            {
                var setObj = new JObject();
                if (!string.IsNullOrEmpty(args["commentary"]?.ToString()))
                    setObj["commentary"] = args["commentary"].ToString();
                if (args["isReshareDisabledByAuthor"] != null)
                    setObj["isReshareDisabledByAuthor"] = args["isReshareDisabledByAuthor"].ToObject<bool>();

                var patchBody = new JObject
                {
                    ["patch"] = new JObject { ["$set"] = setObj }
                };

                await CallLinkedInApi("POST", $"/posts/{EncodeUrn(args["post_urn"]?.ToString())}", patchBody);
                return new JObject { ["success"] = true, ["message"] = "Post updated" };
            }

            case "delete_post":
                await CallLinkedInApi("DELETE", $"/posts/{EncodeUrn(args["post_urn"]?.ToString())}");
                return new JObject { ["success"] = true, ["message"] = "Post deleted" };

            // Comments
            case "get_comments":
            {
                var qp = new Dictionary<string, string>();
                if (args["count"] != null) qp["count"] = args["count"].ToString();
                if (args["start"] != null) qp["start"] = args["start"].ToString();
                return await CallLinkedInApi("GET", $"/socialActions/{EncodeUrn(args["entity_urn"]?.ToString())}/comments", queryParams: qp.Count > 0 ? qp : null);
            }

            case "create_comment":
            {
                var commentBody = new JObject
                {
                    ["actor"] = args["actor_urn"]?.ToString(),
                    ["message"] = new JObject { ["text"] = args["text"]?.ToString() }
                };
                if (!string.IsNullOrEmpty(args["parent_comment"]?.ToString()))
                    commentBody["parentComment"] = args["parent_comment"].ToString();

                var response = await CallLinkedInApiRaw("POST", $"/socialActions/{EncodeUrn(args["entity_urn"]?.ToString())}/comments", commentBody);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = string.IsNullOrEmpty(content) ? new JObject() : JObject.Parse(content);
                    result["success"] = true;
                    return result;
                }
                throw new HttpRequestException($"LinkedIn API returned {(int)response.StatusCode}: {content}");
            }

            case "get_comment":
                return await CallLinkedInApi("GET", $"/socialActions/{EncodeUrn(args["entity_urn"]?.ToString())}/comments/{args["comment_id"]}");

            case "delete_comment":
            {
                var qp = new Dictionary<string, string>
                {
                    ["actor"] = args["actor_urn"]?.ToString()
                };
                await CallLinkedInApi("DELETE", $"/socialActions/{EncodeUrn(args["entity_urn"]?.ToString())}/comments/{args["comment_id"]}", queryParams: qp);
                return new JObject { ["success"] = true, ["message"] = "Comment deleted" };
            }

            // Reactions
            case "get_reactions":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = "entity",
                    ["entity"] = args["entity_urn"]?.ToString()
                };
                if (args["count"] != null) qp["count"] = args["count"].ToString();
                if (args["start"] != null) qp["start"] = args["start"].ToString();
                return await CallLinkedInApi("GET", "/reactions", queryParams: qp);
            }

            case "create_reaction":
            {
                var reactionBody = new JObject
                {
                    ["root"] = args["entity_urn"]?.ToString(),
                    ["reactionType"] = args["reaction_type"]?.ToString()
                };
                var qp = new Dictionary<string, string>
                {
                    ["actor"] = args["actor_urn"]?.ToString()
                };
                var response = await CallLinkedInApiRaw("POST", "/reactions", reactionBody, qp);
                if (response.IsSuccessStatusCode)
                    return new JObject { ["success"] = true, ["message"] = "Reaction created" };

                var content = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"LinkedIn API returned {(int)response.StatusCode}: {content}");
            }

            case "delete_reaction":
            {
                var actorUrn = EncodeUrn(args["actor_urn"]?.ToString());
                var entityUrn = EncodeUrn(args["entity_urn"]?.ToString());
                var compoundKey = $"(actor:{actorUrn},entity:{entityUrn})";

                var response = await CallLinkedInApiRaw("DELETE", $"/reactions/{compoundKey}");
                if (response.IsSuccessStatusCode)
                    return new JObject { ["success"] = true, ["message"] = "Reaction deleted" };

                var content = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"LinkedIn API returned {(int)response.StatusCode}: {content}");
            }

            // Organizations
            case "get_organization":
                return await CallLinkedInApi("GET", $"/organizations/{args["org_id"]}");

            case "find_org_by_vanity_name":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = "vanityName",
                    ["vanityName"] = args["vanity_name"]?.ToString()
                };
                return await CallLinkedInApi("GET", "/organizations", queryParams: qp);
            }

            case "get_follower_count":
            {
                var qp = new Dictionary<string, string>
                {
                    ["edgeType"] = "COMPANY_FOLLOWED_BY_MEMBER"
                };
                return await CallLinkedInApi("GET", $"/networkSizes/{EncodeUrn(args["org_urn"]?.ToString())}", queryParams: qp);
            }

            case "find_member_org_access":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = args["query_type"]?.ToString()
                };
                if (!string.IsNullOrEmpty(args["org_urn"]?.ToString()))
                    qp["organization"] = args["org_urn"].ToString();
                if (!string.IsNullOrEmpty(args["role"]?.ToString()))
                    qp["role"] = args["role"].ToString();
                if (!string.IsNullOrEmpty(args["state"]?.ToString()))
                    qp["state"] = args["state"].ToString();
                return await CallLinkedInApi("GET", "/organizationAcls", queryParams: qp);
            }

            // Social Metadata
            case "get_social_metadata":
                return await CallLinkedInApi("GET", $"/socialMetadata/{EncodeUrn(args["entity_urn"]?.ToString())}");

            case "toggle_comments":
            {
                var disable = args["disable"]?.ToObject<bool>() ?? false;
                var patchBody = new JObject
                {
                    ["patch"] = new JObject
                    {
                        ["$set"] = new JObject
                        {
                            ["commentsSummary"] = new JObject
                            {
                                ["commentsState"] = disable ? "CLOSED" : "OPEN"
                            }
                        }
                    }
                };
                var qp = new Dictionary<string, string>
                {
                    ["actor"] = args["actor_urn"]?.ToString()
                };
                await CallLinkedInApi("POST", $"/socialMetadata/{EncodeUrn(args["entity_urn"]?.ToString())}", patchBody, qp);
                return new JObject { ["success"] = true, ["message"] = disable ? "Comments disabled" : "Comments enabled" };
            }

            // Media
            case "initialize_image_upload":
            {
                var body = new JObject
                {
                    ["initializeUploadRequest"] = new JObject
                    {
                        ["owner"] = args["owner_urn"]?.ToString()
                    }
                };
                var qp = new Dictionary<string, string> { ["action"] = "initializeUpload" };
                return await CallLinkedInApi("POST", "/images", body, qp);
            }

            case "get_image":
                return await CallLinkedInApi("GET", $"/images/{EncodeUrn(args["image_urn"]?.ToString())}");

            case "initialize_video_upload":
            {
                var body = new JObject
                {
                    ["initializeUploadRequest"] = new JObject
                    {
                        ["owner"] = args["owner_urn"]?.ToString(),
                        ["fileSizeBytes"] = args["file_size_bytes"]?.ToObject<long>() ?? 0
                    }
                };
                var qp = new Dictionary<string, string> { ["action"] = "initializeUpload" };
                return await CallLinkedInApi("POST", "/videos", body, qp);
            }

            case "finalize_video_upload":
            {
                var body = new JObject
                {
                    ["finalizeUploadRequest"] = new JObject
                    {
                        ["video"] = args["video_urn"]?.ToString(),
                        ["uploadToken"] = args["upload_token"]?.ToString(),
                        ["uploadedPartIds"] = args["uploaded_part_ids"] as JArray ?? new JArray()
                    }
                };
                var qp = new Dictionary<string, string> { ["action"] = "finalizeUpload" };
                return await CallLinkedInApi("POST", "/videos", body, qp);
            }

            case "get_video":
                return await CallLinkedInApi("GET", $"/videos/{EncodeUrn(args["video_urn"]?.ToString())}");

            case "initialize_document_upload":
            {
                var body = new JObject
                {
                    ["initializeUploadRequest"] = new JObject
                    {
                        ["owner"] = args["owner_urn"]?.ToString()
                    }
                };
                var qp = new Dictionary<string, string> { ["action"] = "initializeUpload" };
                return await CallLinkedInApi("POST", "/documents", body, qp);
            }

            case "get_document":
                return await CallLinkedInApi("GET", $"/documents/{EncodeUrn(args["document_urn"]?.ToString())}");

            // Statistics
            case "get_share_statistics":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = "organizationalEntity",
                    ["organizationalEntity"] = args["org_urn"]?.ToString()
                };
                var timeIntervals = BuildTimeIntervals(args);
                if (timeIntervals != null) qp["timeIntervals"] = timeIntervals;
                return await CallLinkedInApi("GET", "/organizationalEntityShareStatistics", queryParams: qp);
            }

            case "get_page_statistics":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = "organization",
                    ["organization"] = args["org_urn"]?.ToString()
                };
                var timeIntervals = BuildTimeIntervals(args);
                if (timeIntervals != null) qp["timeIntervals"] = timeIntervals;
                return await CallLinkedInApi("GET", "/organizationPageStatistics", queryParams: qp);
            }

            case "get_follower_statistics":
            {
                var qp = new Dictionary<string, string>
                {
                    ["q"] = "organizationalEntity",
                    ["organizationalEntity"] = args["org_urn"]?.ToString()
                };
                var timeIntervals = BuildTimeIntervals(args);
                if (timeIntervals != null) qp["timeIntervals"] = timeIntervals;
                return await CallLinkedInApi("GET", "/organizationalEntityFollowerStatistics", queryParams: qp);
            }

            default:
                throw new InvalidOperationException($"Unknown tool: {toolName}");
        }
    }

    private string BuildTimeIntervals(JObject args)
    {
        var startTime = args["start_time"];
        var endTime = args["end_time"];
        var granularity = args["granularity"]?.ToString();

        if (startTime == null || endTime == null) return null;

        var gran = !string.IsNullOrEmpty(granularity) ? granularity : "DAY";
        return $"(timeRange:(start:{startTime},end:{endTime}),timeGranularityType:{gran})";
    }

    #endregion

    #region JSON-RPC Helpers

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
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
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
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
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
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
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

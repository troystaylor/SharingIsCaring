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
    private const string ConnectorName = "Freshdesk";
    private const string ServerName = "FreshdeskMcpProxy";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-03-26";

    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private static bool _isInitialized = false;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var apiKey = GetHeaderValue("x-fd-apikey");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (this.Context.OperationId == "InvokeMCP")
            {
                return CreateJsonRpcErrorResponse(null, -32602, "Invalid params", "Missing API key");
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"Missing API key\"}", Encoding.UTF8, "application/json")
            };
        }

        if (this.Context.OperationId == "InvokeMCP")
        {
            return await HandleMcpRequestAsync(apiKey).ConfigureAwait(false);
        }

        return await ForwardToFreshdeskAsync(apiKey).ConfigureAwait(false);
    }

    // ── Typed operation forwarding ──────────────────────────────────────

    private async Task<HttpResponseMessage> ForwardToFreshdeskAsync(string apiKey)
    {
        var originalRequest = this.Context.Request;
        var forwardRequest = new HttpRequestMessage(originalRequest.Method, originalRequest.RequestUri);

        if (originalRequest.Content != null &&
            (originalRequest.Method == HttpMethod.Post || originalRequest.Method == HttpMethod.Put || originalRequest.Method.Method == "PATCH"))
        {
            var content = await originalRequest.Content.ReadAsStringAsync().ConfigureAwait(false);
            forwardRequest.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:X"));
        forwardRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        forwardRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(forwardRequest, this.CancellationToken).ConfigureAwait(false);

        LogToAppInsights("TypedOperation", new Dictionary<string, string>
        {
            { "connector", ConnectorName },
            { "operationId", this.Context.OperationId },
            { "method", originalRequest.Method.Method },
            { "statusCode", ((int)response.StatusCode).ToString() }
        });

        return response;
    }

    // ── MCP protocol handler ────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpRequestAsync(string apiKey)
    {
        string body;
        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
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
                    return HandleInitializedNotification();

                case "tools/list":
                    return HandleToolsList(requestId);

                case "tools/call":
                    return await HandleToolsCallAsync(apiKey, request, requestId).ConfigureAwait(false);

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            LogToAppInsights("McpError", new Dictionary<string, string>
            {
                { "connector", ConnectorName },
                { "method", method },
                { "error", ex.Message }
            });
            return CreateJsonRpcErrorResponse(requestId, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        _isInitialized = true;
        var result = new JObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = true }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleInitializedNotification()
    {
        _isInitialized = true;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"initialized\"}", Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        if (!_isInitialized)
        {
            return CreateJsonRpcErrorResponse(requestId, -32002, "Server not initialized", "Call initialize first");
        }

        var result = new JObject { ["tools"] = BuildToolsList() };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string apiKey, JObject request, JToken requestId)
    {
        if (!_isInitialized)
        {
            return CreateJsonRpcErrorResponse(requestId, -32002, "Server not initialized", "Call initialize first");
        }

        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "tool name is required");
        }

        var endpoint = ResolveToolEndpoint(toolName, arguments);
        if (endpoint == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", $"Unknown tool: {toolName}");
        }

        var freshdeskHost = this.Context.Request.RequestUri.Host;
        var targetUrl = $"https://{freshdeskHost}{endpoint.Path}";
        var httpRequest = new HttpRequestMessage(endpoint.Method, targetUrl);

        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:X"));
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (endpoint.Body != null && (endpoint.Method == HttpMethod.Post || endpoint.Method == HttpMethod.Put))
        {
            httpRequest.Content = new StringContent(endpoint.Body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(httpRequest, this.CancellationToken).ConfigureAwait(false);
        var respContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        LogToAppInsights("McpToolCall", new Dictionary<string, string>
        {
            { "connector", ConnectorName },
            { "tool", toolName },
            { "statusCode", ((int)response.StatusCode).ToString() }
        });

        var mcpResult = new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = respContent
                }
            },
            ["isError"] = !response.IsSuccessStatusCode
        };

        return CreateJsonRpcSuccessResponse(requestId, mcpResult);
    }

    // ── Tool definitions ────────────────────────────────────────────────

    private JArray BuildToolsList()
    {
        var tools = new List<JObject>
        {
            // Tickets
            Tool("list_tickets", "List all tickets with optional filters for status, priority, requester, or date range. Returns paginated results.",
                Props(
                    Prop("filter", "string", "Predefined filter: new_and_my_open, watching, spam, deleted"),
                    Prop("email", "string", "Filter by requester email"),
                    Prop("requester_id", "integer", "Filter by requester ID"),
                    Prop("company_id", "integer", "Filter by company ID"),
                    Prop("updated_since", "string", "ISO timestamp to filter recently updated tickets"),
                    Prop("page", "integer", "Page number"),
                    Prop("per_page", "integer", "Results per page (max 100)")
                )),
            Tool("view_ticket", "Get full details of a specific ticket including description, status, priority, and custom fields.",
                Props(Prop("id", "integer", "Ticket ID")),
                new JArray("id")),
            Tool("create_ticket", "Create a new support ticket. Requires at least one of: email, requester_id, phone, or twitter_id.",
                Props(
                    Prop("email", "string", "Requester email"),
                    Prop("subject", "string", "Ticket subject"),
                    Prop("description", "string", "HTML description"),
                    Prop("status", "integer", "2=Open, 3=Pending, 4=Resolved, 5=Closed"),
                    Prop("priority", "integer", "1=Low, 2=Medium, 3=High, 4=Urgent"),
                    Prop("type", "string", "Ticket type category"),
                    Prop("responder_id", "integer", "Agent ID to assign"),
                    Prop("group_id", "integer", "Group ID to assign"),
                    Prop("tags", "array", "Tags for the ticket")
                ),
                new JArray("status", "priority")),
            Tool("update_ticket", "Update ticket properties like status, priority, assignment, or custom fields.",
                Props(
                    Prop("id", "integer", "Ticket ID"),
                    Prop("status", "integer", "2=Open, 3=Pending, 4=Resolved, 5=Closed"),
                    Prop("priority", "integer", "1=Low, 2=Medium, 3=High, 4=Urgent"),
                    Prop("responder_id", "integer", "Agent ID"),
                    Prop("group_id", "integer", "Group ID"),
                    Prop("subject", "string", "Subject"),
                    Prop("type", "string", "Type category")
                ),
                new JArray("id")),
            Tool("delete_ticket", "Soft-delete a ticket (can be restored).",
                Props(Prop("id", "integer", "Ticket ID")),
                new JArray("id")),
            Tool("filter_tickets", "Search tickets using a query. Example: \"priority:3 AND status:2\".",
                Props(
                    Prop("query", "string", "Search query enclosed in double quotes"),
                    Prop("page", "integer", "Page number (1-10)")
                ),
                new JArray("query")),

            // Conversations
            Tool("create_reply", "Reply to a ticket.",
                Props(
                    Prop("ticket_id", "integer", "Ticket ID"),
                    Prop("body", "string", "Reply HTML content")
                ),
                new JArray("ticket_id", "body")),
            Tool("create_note", "Add a note to a ticket.",
                Props(
                    Prop("ticket_id", "integer", "Ticket ID"),
                    Prop("body", "string", "Note HTML content"),
                    Prop("private", "boolean", "True for private note (default true)")
                ),
                new JArray("ticket_id", "body")),
            Tool("list_conversations", "List all conversations (replies and notes) for a ticket.",
                Props(Prop("ticket_id", "integer", "Ticket ID")),
                new JArray("ticket_id")),

            // Contacts
            Tool("list_contacts", "List contacts with optional email, phone, or company filter.",
                Props(
                    Prop("email", "string", "Filter by email"),
                    Prop("company_id", "integer", "Filter by company"),
                    Prop("page", "integer", "Page number")
                )),
            Tool("view_contact", "Get contact details by ID.",
                Props(Prop("id", "integer", "Contact ID")),
                new JArray("id")),
            Tool("create_contact", "Create a new contact. Requires at least name plus one of: email, phone, mobile, or twitter_id.",
                Props(
                    Prop("name", "string", "Contact name"),
                    Prop("email", "string", "Email"),
                    Prop("phone", "string", "Phone"),
                    Prop("job_title", "string", "Job title"),
                    Prop("company_id", "integer", "Company ID")
                ),
                new JArray("name")),
            Tool("update_contact", "Update contact properties.",
                Props(
                    Prop("id", "integer", "Contact ID"),
                    Prop("name", "string", "Name"),
                    Prop("email", "string", "Email"),
                    Prop("phone", "string", "Phone"),
                    Prop("job_title", "string", "Job title"),
                    Prop("company_id", "integer", "Company ID")
                ),
                new JArray("id")),
            Tool("filter_contacts", "Search contacts using a query. Example: \"active:true AND company_id:2331\".",
                Props(Prop("query", "string", "Search query")),
                new JArray("query")),

            // Companies
            Tool("list_companies", "List all companies.",
                Props(Prop("page", "integer", "Page number"))),
            Tool("view_company", "Get company details by ID.",
                Props(Prop("id", "integer", "Company ID")),
                new JArray("id")),
            Tool("create_company", "Create a new company.",
                Props(
                    Prop("name", "string", "Company name"),
                    Prop("description", "string", "Description"),
                    Prop("domains", "array", "Email domains for auto-association"),
                    Prop("industry", "string", "Industry"),
                    Prop("health_score", "string", "Relationship strength"),
                    Prop("account_tier", "string", "Value classification")
                ),
                new JArray("name")),
            Tool("update_company", "Update company properties.",
                Props(
                    Prop("id", "integer", "Company ID"),
                    Prop("name", "string", "Name"),
                    Prop("description", "string", "Description"),
                    Prop("domains", "array", "Domains")
                ),
                new JArray("id")),

            // Agents
            Tool("list_agents", "List all agents.",
                Props(
                    Prop("email", "string", "Filter by email"),
                    Prop("state", "string", "Filter: fulltime or occasional")
                )),
            Tool("view_agent", "Get agent details by ID.",
                Props(Prop("id", "integer", "Agent ID")),
                new JArray("id")),
            Tool("get_current_agent", "Get the currently authenticated agent profile.", Props()),

            // Groups
            Tool("list_groups", "List all agent groups.", Props(Prop("page", "integer", "Page number"))),
            Tool("view_group", "Get group details by ID.",
                Props(Prop("id", "integer", "Group ID")),
                new JArray("id")),
            Tool("create_group", "Create a new agent group.",
                Props(
                    Prop("name", "string", "Group name"),
                    Prop("description", "string", "Description"),
                    Prop("agent_ids", "array", "Agent user IDs")
                ),
                new JArray("name")),

            // Solutions / Knowledge Base
            Tool("list_solution_categories", "List all knowledge base categories.", Props()),
            Tool("view_solution_category", "Get a solution category by ID.",
                Props(Prop("id", "integer", "Category ID")),
                new JArray("id")),
            Tool("create_solution_category", "Create a new KB category.",
                Props(
                    Prop("name", "string", "Category name"),
                    Prop("description", "string", "Description")
                ),
                new JArray("name")),
            Tool("update_solution_category", "Update a KB category.",
                Props(
                    Prop("id", "integer", "Category ID"),
                    Prop("name", "string", "Name"),
                    Prop("description", "string", "Description")
                ),
                new JArray("id")),
            Tool("delete_solution_category", "Delete a KB category.",
                Props(Prop("id", "integer", "Category ID")),
                new JArray("id")),
            Tool("list_solution_folders", "List folders in a KB category.",
                Props(Prop("category_id", "integer", "Category ID")),
                new JArray("category_id")),
            Tool("view_solution_folder", "Get a solution folder by ID.",
                Props(Prop("id", "integer", "Folder ID")),
                new JArray("id")),
            Tool("create_solution_folder", "Create a folder in a KB category.",
                Props(
                    Prop("category_id", "integer", "Category ID"),
                    Prop("name", "string", "Folder name"),
                    Prop("description", "string", "Description"),
                    Prop("visibility", "integer", "1=All, 2=Logged in, 3=Agents, 4=Companies, 5=Bots")
                ),
                new JArray("category_id", "name", "visibility")),
            Tool("update_solution_folder", "Update a KB folder.",
                Props(
                    Prop("id", "integer", "Folder ID"),
                    Prop("name", "string", "Name"),
                    Prop("description", "string", "Description"),
                    Prop("visibility", "integer", "Visibility level")
                ),
                new JArray("id")),
            Tool("delete_solution_folder", "Delete a KB folder.",
                Props(Prop("id", "integer", "Folder ID")),
                new JArray("id")),
            Tool("list_solution_articles", "List articles in a KB folder.",
                Props(Prop("folder_id", "integer", "Folder ID")),
                new JArray("folder_id")),
            Tool("view_solution_article", "Get a KB article by ID.",
                Props(Prop("id", "integer", "Article ID")),
                new JArray("id")),
            Tool("create_solution_article", "Create a KB article in a folder.",
                Props(
                    Prop("folder_id", "integer", "Folder ID"),
                    Prop("title", "string", "Article title"),
                    Prop("description", "string", "HTML content"),
                    Prop("status", "integer", "1=Draft, 2=Published")
                ),
                new JArray("folder_id", "title", "description", "status")),
            Tool("update_solution_article", "Update a KB article.",
                Props(
                    Prop("id", "integer", "Article ID"),
                    Prop("title", "string", "Title"),
                    Prop("description", "string", "HTML content"),
                    Prop("status", "integer", "1=Draft, 2=Published")
                ),
                new JArray("id")),
            Tool("delete_solution_article", "Delete a KB article.",
                Props(Prop("id", "integer", "Article ID")),
                new JArray("id")),
            Tool("search_solution_articles", "Search KB articles by keyword.",
                Props(Prop("term", "string", "Search keyword")),
                new JArray("term")),

            // Time Entries
            Tool("list_time_entries", "List all time entries with optional filters.",
                Props(
                    Prop("agent_id", "integer", "Filter by agent"),
                    Prop("billable", "boolean", "Filter billable"),
                    Prop("executed_after", "string", "After ISO timestamp"),
                    Prop("executed_before", "string", "Before ISO timestamp")
                )),
            Tool("create_time_entry", "Create a time entry for a ticket.",
                Props(
                    Prop("ticket_id", "integer", "Ticket ID"),
                    Prop("time_spent", "string", "Duration in hh:mm"),
                    Prop("agent_id", "integer", "Agent ID"),
                    Prop("billable", "boolean", "Billable (default true)"),
                    Prop("note", "string", "Description")
                ),
                new JArray("ticket_id")),
            Tool("update_time_entry", "Update a time entry.",
                Props(
                    Prop("id", "integer", "Time entry ID"),
                    Prop("time_spent", "string", "Duration hh:mm"),
                    Prop("note", "string", "Description"),
                    Prop("billable", "boolean", "Billable")
                ),
                new JArray("id")),
            Tool("delete_time_entry", "Delete a time entry.",
                Props(Prop("id", "integer", "Time entry ID")),
                new JArray("id")),
            Tool("toggle_timer", "Start or stop a time entry timer.",
                Props(Prop("id", "integer", "Time entry ID")),
                new JArray("id")),

            // SLA / Products / Business Hours
            Tool("list_sla_policies", "List all SLA policies.", Props()),
            Tool("list_products", "List all products.", Props()),
            Tool("view_product", "Get product details by ID.",
                Props(Prop("id", "integer", "Product ID")),
                new JArray("id")),
            Tool("list_business_hours", "List all business hour configurations.", Props()),
            Tool("view_business_hour", "Get business hour details by ID.",
                Props(Prop("id", "integer", "Business hour ID")),
                new JArray("id")),

            // Forums / Discussions
            Tool("list_forum_categories", "List all forum categories.", Props()),
            Tool("create_forum_category", "Create a forum category.",
                Props(Prop("name", "string", "Category name"), Prop("description", "string", "Description")),
                new JArray("name")),
            Tool("view_forum_category", "Get forum category by ID.",
                Props(Prop("id", "integer", "Category ID")),
                new JArray("id")),
            Tool("update_forum_category", "Update a forum category.",
                Props(Prop("id", "integer", "Category ID"), Prop("name", "string", "Name"), Prop("description", "string", "Description")),
                new JArray("id")),
            Tool("delete_forum_category", "Delete a forum category.",
                Props(Prop("id", "integer", "Category ID")),
                new JArray("id")),
            Tool("list_forums", "List forums in a category.",
                Props(Prop("category_id", "integer", "Category ID")),
                new JArray("category_id")),
            Tool("create_forum", "Create a forum in a category.",
                Props(
                    Prop("category_id", "integer", "Category ID"),
                    Prop("name", "string", "Forum name"),
                    Prop("forum_type", "integer", "1=How To, 2=Ideas, 3=Problems, 4=Announcements"),
                    Prop("forum_visibility", "integer", "1=Everyone, 2=Logged in, 3=Agents, 4=Companies")
                ),
                new JArray("category_id", "name", "forum_type", "forum_visibility")),
            Tool("view_forum", "Get forum by ID.",
                Props(Prop("id", "integer", "Forum ID")),
                new JArray("id")),
            Tool("update_forum", "Update a forum.",
                Props(Prop("id", "integer", "Forum ID"), Prop("name", "string", "Name"), Prop("description", "string", "Description")),
                new JArray("id")),
            Tool("delete_forum", "Delete a forum.",
                Props(Prop("id", "integer", "Forum ID")),
                new JArray("id")),
            Tool("list_topics", "List topics in a forum.",
                Props(Prop("forum_id", "integer", "Forum ID"), Prop("page", "integer", "Page number")),
                new JArray("forum_id")),
            Tool("create_topic", "Create a topic in a forum.",
                Props(
                    Prop("forum_id", "integer", "Forum ID"),
                    Prop("title", "string", "Title"),
                    Prop("message", "string", "Body content (first post)")
                ),
                new JArray("forum_id", "title", "message")),
            Tool("view_topic", "Get topic by ID.",
                Props(Prop("id", "integer", "Topic ID")),
                new JArray("id")),
            Tool("update_topic", "Update a topic.",
                Props(Prop("id", "integer", "Topic ID"), Prop("title", "string", "Title"), Prop("message", "string", "Body")),
                new JArray("id")),
            Tool("delete_topic", "Delete a topic.",
                Props(Prop("id", "integer", "Topic ID")),
                new JArray("id")),
            Tool("list_comments", "List comments in a topic.",
                Props(Prop("topic_id", "integer", "Topic ID")),
                new JArray("topic_id")),
            Tool("create_comment", "Add a comment to a topic.",
                Props(Prop("topic_id", "integer", "Topic ID"), Prop("body", "string", "Comment HTML content")),
                new JArray("topic_id", "body")),
            Tool("update_comment", "Update a forum comment.",
                Props(Prop("id", "integer", "Comment ID"), Prop("body", "string", "Updated content")),
                new JArray("id")),
            Tool("delete_comment", "Delete a forum comment.",
                Props(Prop("id", "integer", "Comment ID")),
                new JArray("id")),

            // Roles / Canned Responses / Automations / CSAT
            Tool("list_roles", "List all agent roles.", Props()),
            Tool("view_role", "Get role by ID.",
                Props(Prop("id", "integer", "Role ID")),
                new JArray("id")),
            Tool("list_canned_response_folders", "List all canned response folders.", Props()),
            Tool("list_canned_responses", "List canned responses in a folder.",
                Props(Prop("folder_id", "integer", "Folder ID")),
                new JArray("folder_id")),
            Tool("view_canned_response", "Get a canned response by ID.",
                Props(Prop("id", "integer", "Response ID")),
                new JArray("id")),
            Tool("create_canned_response", "Create a canned response.",
                Props(
                    Prop("title", "string", "Title"),
                    Prop("content_html", "string", "HTML content"),
                    Prop("folder_id", "integer", "Folder ID"),
                    Prop("visibility", "integer", "0=All, 1=Personal, 2=Groups")
                ),
                new JArray("title", "content_html", "folder_id", "visibility")),
            Tool("list_scenario_automations", "List all scenario automations.", Props()),
            Tool("list_surveys", "List customer satisfaction surveys.", Props()),
            Tool("list_satisfaction_ratings", "List all satisfaction ratings.",
                Props(
                    Prop("created_since", "string", "ISO timestamp filter"),
                    Prop("page", "integer", "Page number")
                )),

            // Additional high-value operations
            Tool("forward_ticket", "Forward a ticket to external email.",
                Props(
                    Prop("ticket_id", "integer", "Ticket ID"),
                    Prop("body", "string", "Forward content HTML"),
                    Prop("to_emails", "array", "Recipient emails")
                ),
                new JArray("ticket_id", "body", "to_emails")),
            Tool("merge_tickets", "Merge secondary tickets into a primary ticket.",
                Props(
                    Prop("primary_id", "integer", "Primary ticket ID"),
                    Prop("ticket_ids", "array", "All ticket IDs to merge")
                ),
                new JArray("primary_id", "ticket_ids")),
            Tool("list_watchers", "List watchers on a ticket.",
                Props(Prop("ticket_id", "integer", "Ticket ID")),
                new JArray("ticket_id")),
            Tool("add_watcher", "Add a watcher to a ticket.",
                Props(Prop("ticket_id", "integer", "Ticket ID"), Prop("user_id", "integer", "Agent user ID")),
                new JArray("ticket_id", "user_id")),
            Tool("remove_watcher", "Remove yourself as watcher.",
                Props(Prop("ticket_id", "integer", "Ticket ID")),
                new JArray("ticket_id")),
            Tool("restore_contact", "Restore a soft-deleted contact.",
                Props(Prop("id", "integer", "Contact ID")),
                new JArray("id")),
            Tool("hard_delete_contact", "Permanently delete a contact (GDPR).",
                Props(Prop("id", "integer", "Contact ID"), Prop("force", "boolean", "Force without soft-delete")),
                new JArray("id")),
            Tool("merge_contacts", "Merge contacts into a primary.",
                Props(
                    Prop("primary_contact_id", "integer", "Primary contact ID"),
                    Prop("secondary_contact_ids", "array", "Secondary contact IDs")
                ),
                new JArray("primary_contact_id", "secondary_contact_ids")),
            Tool("search_contacts", "Search contacts by name (autocomplete).",
                Props(Prop("term", "string", "Name search")),
                new JArray("term")),
            Tool("filter_companies", "Search companies with query syntax.",
                Props(Prop("query", "string", "Query string")),
                new JArray("query")),
            Tool("search_companies", "Search companies by name (autocomplete).",
                Props(Prop("name", "string", "Company name")),
                new JArray("name")),
            Tool("create_agent", "Create a new agent.",
                Props(
                    Prop("email", "string", "Email"),
                    Prop("ticket_scope", "integer", "1=Global, 2=Group, 3=Restricted"),
                    Prop("name", "string", "Name"),
                    Prop("occasional", "boolean", "Occasional agent")
                ),
                new JArray("email", "ticket_scope")),
            Tool("update_agent", "Update an agent.",
                Props(
                    Prop("id", "integer", "Agent ID"),
                    Prop("ticket_scope", "integer", "Permission level"),
                    Prop("group_ids", "array", "Group IDs"),
                    Prop("role_ids", "array", "Role IDs")
                ),
                new JArray("id")),
            Tool("delete_agent", "Delete an agent (downgrades to contact).",
                Props(Prop("id", "integer", "Agent ID")),
                new JArray("id")),
            Tool("search_agents", "Search agents by name or email.",
                Props(Prop("term", "string", "Search term")),
                new JArray("term")),
            Tool("update_group", "Update a group.",
                Props(
                    Prop("id", "integer", "Group ID"),
                    Prop("name", "string", "Name"),
                    Prop("description", "string", "Description"),
                    Prop("agent_ids", "array", "Agent IDs")
                ),
                new JArray("id")),
            Tool("delete_group", "Delete a group.",
                Props(Prop("id", "integer", "Group ID")),
                new JArray("id")),
            Tool("list_skills", "List all skills.", Props()),
            Tool("view_skill", "Get skill by ID.",
                Props(Prop("id", "integer", "Skill ID")),
                new JArray("id")),
            Tool("create_skill", "Create a skill.",
                Props(Prop("name", "string", "Skill name"), Prop("match_type", "string", "all or any")),
                new JArray("name")),
            Tool("update_skill", "Update a skill.",
                Props(Prop("id", "integer", "Skill ID"), Prop("name", "string", "Name")),
                new JArray("id")),
            Tool("delete_skill", "Delete a skill.",
                Props(Prop("id", "integer", "Skill ID")),
                new JArray("id")),
            Tool("update_canned_response", "Update a canned response.",
                Props(Prop("id", "integer", "Response ID"), Prop("title", "string", "Title"), Prop("content_html", "string", "HTML content")),
                new JArray("id")),

            // Account / Fields / Email / Ticket extras
            Tool("view_account", "Get Freshdesk account details (plan, timezone, agents).", Props()),
            Tool("list_contact_fields", "List all contact fields.", Props()),
            Tool("list_company_fields", "List all company fields.", Props()),
            Tool("list_email_mailboxes", "List all email mailboxes.", Props()),
            Tool("view_email_mailbox", "Get mailbox details by ID.",
                Props(Prop("id", "integer", "Mailbox ID")),
                new JArray("id")),
            Tool("view_ticket_summary", "Get the summary of a ticket.",
                Props(Prop("ticket_id", "integer", "Ticket ID")),
                new JArray("ticket_id")),
            Tool("update_ticket_summary", "Create or update a ticket summary.",
                Props(Prop("ticket_id", "integer", "Ticket ID"), Prop("body", "string", "Summary HTML")),
                new JArray("ticket_id", "body")),
            Tool("create_outbound_email", "Create an outbound email ticket.",
                Props(
                    Prop("email", "string", "Recipient email"),
                    Prop("subject", "string", "Subject"),
                    Prop("description", "string", "HTML body"),
                    Prop("email_config_id", "integer", "Email config ID")
                ),
                new JArray("email", "subject", "email_config_id")),
            Tool("view_archive_ticket", "Get an archived ticket by ID.",
                Props(Prop("id", "integer", "Ticket ID")),
                new JArray("id")),
            Tool("list_ticket_forms", "List all ticket forms.", Props()),
            Tool("list_email_configs", "List all email configurations.", Props()),

            // Custom Objects
            Tool("list_custom_object_schemas", "List all custom object schemas.", Props()),
            Tool("view_custom_object_schema", "Get a custom object schema with fields.",
                Props(Prop("schema_id", "integer", "Schema ID")),
                new JArray("schema_id")),
            Tool("list_custom_object_records", "List records for a custom object.",
                Props(Prop("schema_id", "integer", "Schema ID"), Prop("page_size", "integer", "Records per page (max 100)")),
                new JArray("schema_id")),
            Tool("create_custom_object_record", "Create a record in a custom object.",
                Props(Prop("schema_id", "integer", "Schema ID"), Prop("data", "object", "Field key-value pairs")),
                new JArray("schema_id", "data")),
            Tool("view_custom_object_record", "Get a custom object record by ID.",
                Props(Prop("schema_id", "integer", "Schema ID"), Prop("record_id", "string", "Record ID (e.g. BKG-1)")),
                new JArray("schema_id", "record_id")),
            Tool("update_custom_object_record", "Update a custom object record.",
                Props(Prop("schema_id", "integer", "Schema ID"), Prop("record_id", "string", "Record ID"), Prop("data", "object", "Updated field values")),
                new JArray("schema_id", "record_id", "data")),
            Tool("delete_custom_object_record", "Delete a custom object record.",
                Props(Prop("schema_id", "integer", "Schema ID"), Prop("record_id", "string", "Record ID")),
                new JArray("schema_id", "record_id")),

            // Bulk Operations
            Tool("bulk_update_tickets", "Update multiple tickets at once (returns job ID).",
                Props(
                    Prop("ids", "array", "Ticket IDs"),
                    Prop("status", "integer", "Status to set"),
                    Prop("priority", "integer", "Priority to set"),
                    Prop("group_id", "integer", "Group ID to assign"),
                    Prop("responder_id", "integer", "Agent ID to assign")
                ),
                new JArray("ids")),
            Tool("bulk_delete_tickets", "Delete multiple tickets at once (returns job ID).",
                Props(Prop("ids", "array", "Ticket IDs to delete")),
                new JArray("ids"))
        };

        return new JArray(tools);
    }

    private ToolEndpoint ResolveToolEndpoint(string toolName, JObject args)
    {
        switch (toolName)
        {
            // Tickets
            case "list_tickets":
                var ticketQuery = BuildQueryString(args, "filter", "email", "requester_id", "company_id", "updated_since", "page", "per_page");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/tickets{ticketQuery}");
            case "view_ticket":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/tickets/{args.Value<int>("id")}");
            case "create_ticket":
                var createTicketBody = FilterArgs(args);
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/tickets", createTicketBody);
            case "update_ticket":
                var updateId = args.Value<int>("id");
                var updateBody = FilterArgs(args, "id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/tickets/{updateId}", updateBody);
            case "delete_ticket":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/tickets/{args.Value<int>("id")}");
            case "filter_tickets":
                var ftQuery = Uri.EscapeDataString(args.Value<string>("query") ?? "");
                var ftPage = args.Value<int?>("page");
                var ftUrl = $"/api/v2/search/tickets?query=\"{ftQuery}\"";
                if (ftPage.HasValue) ftUrl += $"&page={ftPage.Value}";
                return new ToolEndpoint(HttpMethod.Get, ftUrl);

            // Conversations
            case "create_reply":
                var replyTicketId = args.Value<int>("ticket_id");
                var replyBody = FilterArgs(args, "ticket_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/tickets/{replyTicketId}/reply", replyBody);
            case "create_note":
                var noteTicketId = args.Value<int>("ticket_id");
                var noteBody = FilterArgs(args, "ticket_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/tickets/{noteTicketId}/notes", noteBody);
            case "list_conversations":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/tickets/{args.Value<int>("ticket_id")}/conversations");

            // Contacts
            case "list_contacts":
                var contactQuery = BuildQueryString(args, "email", "company_id", "page");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/contacts{contactQuery}");
            case "view_contact":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/contacts/{args.Value<int>("id")}");
            case "create_contact":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/contacts", FilterArgs(args));
            case "update_contact":
                var ucId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/contacts/{ucId}", FilterArgs(args, "id"));
            case "filter_contacts":
                var fcQuery = Uri.EscapeDataString(args.Value<string>("query") ?? "");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/search/contacts?query=\"{fcQuery}\"");

            // Companies
            case "list_companies":
                var companyQuery = BuildQueryString(args, "page");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/companies{companyQuery}");
            case "view_company":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/companies/{args.Value<int>("id")}");
            case "create_company":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/companies", FilterArgs(args));
            case "update_company":
                var ucoId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/companies/{ucoId}", FilterArgs(args, "id"));

            // Agents
            case "list_agents":
                var agentQuery = BuildQueryString(args, "email", "state");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/agents{agentQuery}");
            case "view_agent":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/agents/{args.Value<int>("id")}");
            case "get_current_agent":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/agents/me");

            // Groups
            case "list_groups":
                var groupQuery = BuildQueryString(args, "page");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/groups{groupQuery}");
            case "view_group":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/groups/{args.Value<int>("id")}");
            case "create_group":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/groups", FilterArgs(args));

            // Solutions / Knowledge Base
            case "list_solution_categories":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/solutions/categories");
            case "view_solution_category":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/solutions/categories/{args.Value<int>("id")}");
            case "create_solution_category":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/solutions/categories", FilterArgs(args));
            case "update_solution_category":
                var uscId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/solutions/categories/{uscId}", FilterArgs(args, "id"));
            case "delete_solution_category":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/solutions/categories/{args.Value<int>("id")}");
            case "list_solution_folders":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/solutions/categories/{args.Value<int>("category_id")}/folders");
            case "view_solution_folder":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/solutions/folders/{args.Value<int>("id")}");
            case "create_solution_folder":
                var sfCatId = args.Value<int>("category_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/solutions/categories/{sfCatId}/folders", FilterArgs(args, "category_id"));
            case "update_solution_folder":
                var usfId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/solutions/folders/{usfId}", FilterArgs(args, "id"));
            case "delete_solution_folder":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/solutions/folders/{args.Value<int>("id")}");
            case "list_solution_articles":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/solutions/folders/{args.Value<int>("folder_id")}/articles");
            case "view_solution_article":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/solutions/articles/{args.Value<int>("id")}");
            case "create_solution_article":
                var saFolderId = args.Value<int>("folder_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/solutions/folders/{saFolderId}/articles", FilterArgs(args, "folder_id"));
            case "update_solution_article":
                var usaId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/solutions/articles/{usaId}", FilterArgs(args, "id"));
            case "delete_solution_article":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/solutions/articles/{args.Value<int>("id")}");
            case "search_solution_articles":
                var searchTerm = Uri.EscapeDataString(args.Value<string>("term") ?? "");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/search/solutions?term={searchTerm}");

            // Time Entries
            case "list_time_entries":
                var teQuery = BuildQueryString(args, "agent_id", "billable", "executed_after", "executed_before");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/time_entries{teQuery}");
            case "create_time_entry":
                var teTicketId = args.Value<int>("ticket_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/tickets/{teTicketId}/time_entries", FilterArgs(args, "ticket_id"));
            case "update_time_entry":
                var ueId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/time_entries/{ueId}", FilterArgs(args, "id"));
            case "delete_time_entry":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/time_entries/{args.Value<int>("id")}");
            case "toggle_timer":
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/time_entries/{args.Value<int>("id")}/toggle_timer");

            // SLA / Products / Business Hours
            case "list_sla_policies":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/sla_policies");
            case "list_products":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/products");
            case "view_product":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/products/{args.Value<int>("id")}");
            case "list_business_hours":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/business_hours");
            case "view_business_hour":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/business_hours/{args.Value<int>("id")}");

            // Forums / Discussions
            case "list_forum_categories":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/discussions/categories");
            case "create_forum_category":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/discussions/categories", FilterArgs(args));
            case "view_forum_category":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/discussions/categories/{args.Value<int>("id")}");
            case "update_forum_category":
                var ufcId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/discussions/categories/{ufcId}", FilterArgs(args, "id"));
            case "delete_forum_category":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/discussions/categories/{args.Value<int>("id")}");
            case "list_forums":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/discussions/categories/{args.Value<int>("category_id")}/forums");
            case "create_forum":
                var cfCatId = args.Value<int>("category_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/discussions/categories/{cfCatId}/forums", FilterArgs(args, "category_id"));
            case "view_forum":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/discussions/forums/{args.Value<int>("id")}");
            case "update_forum":
                var ufId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/discussions/forums/{ufId}", FilterArgs(args, "id"));
            case "delete_forum":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/discussions/forums/{args.Value<int>("id")}");
            case "list_topics":
                var ltQuery = BuildQueryString(args, "page");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/discussions/forums/{args.Value<int>("forum_id")}/topics{ltQuery}");
            case "create_topic":
                var ctForumId = args.Value<int>("forum_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/discussions/forums/{ctForumId}/topics", FilterArgs(args, "forum_id"));
            case "view_topic":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/discussions/topics/{args.Value<int>("id")}");
            case "update_topic":
                var utId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/discussions/topics/{utId}", FilterArgs(args, "id"));
            case "delete_topic":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/discussions/topics/{args.Value<int>("id")}");
            case "list_comments":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/discussions/topics/{args.Value<int>("topic_id")}/comments");
            case "create_comment":
                var ccTopicId = args.Value<int>("topic_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/discussions/topics/{ccTopicId}/comments", FilterArgs(args, "topic_id"));
            case "update_comment":
                var ucmId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/discussions/comments/{ucmId}", FilterArgs(args, "id"));
            case "delete_comment":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/discussions/comments/{args.Value<int>("id")}");

            // Roles / Canned Responses / Automations / CSAT
            case "list_roles":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/roles");
            case "view_role":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/roles/{args.Value<int>("id")}");
            case "list_canned_response_folders":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/canned_response_folders");
            case "list_canned_responses":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/canned_response_folders/{args.Value<int>("folder_id")}/responses");
            case "view_canned_response":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/canned_responses/{args.Value<int>("id")}");
            case "create_canned_response":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/canned_responses", FilterArgs(args));
            case "list_scenario_automations":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/scenario_automations");
            case "list_surveys":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/surveys");
            case "list_satisfaction_ratings":
                var srQuery = BuildQueryString(args, "created_since", "page");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/surveys/satisfaction_ratings{srQuery}");

            // Additional high-value operations
            case "forward_ticket":
                var fwdId = args.Value<int>("ticket_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/tickets/{fwdId}/forward", FilterArgs(args, "ticket_id"));
            case "merge_tickets":
                return new ToolEndpoint(HttpMethod.Put, "/api/v2/tickets/merge", FilterArgs(args));
            case "list_watchers":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/tickets/{args.Value<int>("ticket_id")}/watchers");
            case "add_watcher":
                var awId = args.Value<int>("ticket_id");
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/tickets/{awId}/watch", FilterArgs(args, "ticket_id"));
            case "remove_watcher":
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/tickets/{args.Value<int>("ticket_id")}/unwatch");
            case "restore_contact":
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/contacts/{args.Value<int>("id")}/restore");
            case "hard_delete_contact":
                var hdForce = args.Value<bool?>("force") == true ? "?force=true" : "";
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/contacts/{args.Value<int>("id")}/hard_delete{hdForce}");
            case "merge_contacts":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/contacts/merge", FilterArgs(args));
            case "search_contacts":
                var scTerm = Uri.EscapeDataString(args.Value<string>("term") ?? "");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/contacts/autocomplete?term={scTerm}");
            case "filter_companies":
                var fcqQuery = Uri.EscapeDataString(args.Value<string>("query") ?? "");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/search/companies?query=\"{fcqQuery}\"");
            case "search_companies":
                var scName = Uri.EscapeDataString(args.Value<string>("name") ?? "");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/companies/autocomplete?name={scName}");
            case "create_agent":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/agents/", FilterArgs(args));
            case "update_agent":
                var uaId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/agents/{uaId}", FilterArgs(args, "id"));
            case "delete_agent":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/agents/{args.Value<int>("id")}");
            case "search_agents":
                var saTerm = Uri.EscapeDataString(args.Value<string>("term") ?? "");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/agents/autocomplete?term={saTerm}");
            case "update_group":
                var ugId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/groups/{ugId}", FilterArgs(args, "id"));
            case "delete_group":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/groups/{args.Value<int>("id")}");
            case "list_skills":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/admin/skills");
            case "view_skill":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/admin/skills/{args.Value<int>("id")}");
            case "create_skill":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/admin/skills", FilterArgs(args));
            case "update_skill":
                var uskId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/admin/skills/{uskId}", FilterArgs(args, "id"));
            case "delete_skill":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/admin/skills/{args.Value<int>("id")}");
            case "update_canned_response":
                var ucrId = args.Value<int>("id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/canned_responses/{ucrId}", FilterArgs(args, "id"));

            // Account / Fields / Email / Ticket extras
            case "view_account":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/account");
            case "list_contact_fields":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/contact_fields");
            case "list_company_fields":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/company_fields");
            case "list_email_mailboxes":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/email/mailboxes");
            case "view_email_mailbox":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/email/mailboxes/{args.Value<int>("id")}");
            case "view_ticket_summary":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/tickets/{args.Value<int>("ticket_id")}/summary");
            case "update_ticket_summary":
                var utsId = args.Value<int>("ticket_id");
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/tickets/{utsId}/summary", FilterArgs(args, "ticket_id"));
            case "create_outbound_email":
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/tickets/outbound_email", FilterArgs(args));
            case "view_archive_ticket":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/tickets/archived/{args.Value<int>("id")}");
            case "list_ticket_forms":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/ticket-forms");
            case "list_email_configs":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/email_configs");

            // Custom Objects
            case "list_custom_object_schemas":
                return new ToolEndpoint(HttpMethod.Get, "/api/v2/custom_objects/schemas");
            case "view_custom_object_schema":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/custom_objects/schemas/{args.Value<int>("schema_id")}");
            case "list_custom_object_records":
                var coQuery = BuildQueryString(args, "page_size");
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/custom_objects/schemas/{args.Value<int>("schema_id")}/records{coQuery}");
            case "create_custom_object_record":
                var coSchemaId = args.Value<int>("schema_id");
                var coData = new JObject { ["data"] = args["data"] };
                return new ToolEndpoint(HttpMethod.Post, $"/api/v2/custom_objects/schemas/{coSchemaId}/records", coData);
            case "view_custom_object_record":
                return new ToolEndpoint(HttpMethod.Get, $"/api/v2/custom_objects/schemas/{args.Value<int>("schema_id")}/records/{args.Value<string>("record_id")}");
            case "update_custom_object_record":
                var ucoSchemaId = args.Value<int>("schema_id");
                var ucoRecordId = args.Value<string>("record_id");
                var ucoBody = new JObject { ["display_id"] = ucoRecordId, ["data"] = args["data"] };
                return new ToolEndpoint(HttpMethod.Put, $"/api/v2/custom_objects/schemas/{ucoSchemaId}/records/{ucoRecordId}", ucoBody);
            case "delete_custom_object_record":
                return new ToolEndpoint(new HttpMethod("DELETE"), $"/api/v2/custom_objects/schemas/{args.Value<int>("schema_id")}/records/{args.Value<string>("record_id")}");

            // Bulk Operations
            case "bulk_update_tickets":
                var buProps = new JObject();
                if (args["status"] != null) buProps["status"] = args["status"];
                if (args["priority"] != null) buProps["priority"] = args["priority"];
                if (args["group_id"] != null) buProps["group_id"] = args["group_id"];
                if (args["responder_id"] != null) buProps["responder_id"] = args["responder_id"];
                var buBody = new JObject { ["bulk_action"] = new JObject { ["ids"] = args["ids"], ["properties"] = buProps } };
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/tickets/bulk_update", buBody);
            case "bulk_delete_tickets":
                var bdBody = new JObject { ["bulk_action"] = new JObject { ["ids"] = args["ids"] } };
                return new ToolEndpoint(HttpMethod.Post, "/api/v2/tickets/bulk_delete", bdBody);

            default:
                return null;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private class ToolEndpoint
    {
        public HttpMethod Method { get; }
        public string Path { get; }
        public JObject Body { get; }
        public ToolEndpoint(HttpMethod method, string path, JObject body = null)
        {
            Method = method;
            Path = path;
            Body = body;
        }
    }

    private JObject FilterArgs(JObject args, params string[] exclude)
    {
        var result = new JObject();
        var excludeSet = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
        foreach (var prop in args.Properties())
        {
            if (!excludeSet.Contains(prop.Name))
            {
                result[prop.Name] = prop.Value;
            }
        }
        return result;
    }

    private string BuildQueryString(JObject args, params string[] keys)
    {
        var parts = new List<string>();
        foreach (var key in keys)
        {
            var val = args[key];
            if (val != null && val.Type != JTokenType.Null)
            {
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(val.ToString())}");
            }
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }

    private JObject Tool(string name, string description, JObject properties, JArray required = null)
    {
        var inputSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required != null && required.Count > 0)
        {
            inputSchema["required"] = required;
        }

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private JObject Props(params JProperty[] props)
    {
        var obj = new JObject();
        foreach (var p in props)
        {
            obj.Add(p);
        }
        return obj;
    }

    private JProperty Prop(string name, string type, string description)
    {
        return new JProperty(name, new JObject
        {
            ["type"] = type,
            ["description"] = description
        });
    }

    private string GetHeaderValue(string name)
    {
        try
        {
            if (this.Context.Request.Headers.TryGetValues(name, out var values))
            {
                var raw = values.FirstOrDefault();
                return string.IsNullOrWhiteSpace(raw) ? null : raw;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

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
            Content = new StringContent(responseObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

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
                        ["properties"] = properties != null ? JObject.FromObject(properties) : new JObject()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT)
            {
                Content = new StringContent(
                    telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json"
                )
            };

            this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Zendesk MCP Server — Tools, Resources, and Prompts for Zendesk Support    ║
// ║  API v2 (Tickets, Users, Organizations, Groups, Search, Comments,          ║
// ║  Satisfaction Ratings, Views, Tags, and Ticket Metrics).                   ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "zendesk-mcp",
            Version = "1.0.0",
            Title = "Zendesk MCP Server",
            Description = "Power Platform custom connector implementing MCP for Zendesk Support API v2. Provides AI agents with ticketing, user, organization, search, comment, satisfaction rating, view, tag, and metric operations."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Logging = true,
            Completions = true
        },
        Instructions = "Use Zendesk search syntax for queries: type:ticket status:open priority:urgent assignee:me. Ticket statuses are: new, open, pending, hold, solved, closed. Priorities are: urgent, high, normal, low. Types are: problem, incident, question, task."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        var handler = new McpRequestHandler(Options);
        RegisterCapabilities(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        // ══════════════════════════════════════════════════════════════════
        //  TOOLS
        // ══════════════════════════════════════════════════════════════════

        // ── Search ───────────────────────────────────────────────────────

        handler.AddTool("search", "Search Zendesk for tickets, users, organizations, or other objects. Uses Zendesk search syntax (e.g., type:ticket status:open priority:urgent, type:user email:john@example.com).",
            schema: s => s
                .String("query", "Zendesk search query string (e.g., type:ticket status:open priority:urgent assignee:me)", required: true)
                .String("sort_by", "Sort results by: created_at, updated_at, priority, status, ticket_type")
                .String("sort_order", "Sort order: asc or desc"),
            handler: async (args, ct) =>
            {
                var query = RequireArgument(args, "query");
                var path = $"/search.json?query={Uri.EscapeDataString(query)}";
                var sortBy = GetArgument(args, "sort_by");
                var sortOrder = GetArgument(args, "sort_order");
                if (sortBy != null) path += $"&sort_by={Uri.EscapeDataString(sortBy)}";
                if (sortOrder != null) path += $"&sort_order={Uri.EscapeDataString(sortOrder)}";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Tickets ──────────────────────────────────────────────────────

        handler.AddTool("list_tickets", "List tickets in the account. Returns up to 100 tickets per page.",
            schema: s => s
                .String("sort_by", "Sort by: id, subject, created_at, updated_at, status, requester, assignee, group")
                .String("sort_order", "Sort order: asc or desc")
                .Integer("per_page", "Number of records per page (max 100)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var path = "/tickets.json";
                var queryParams = new List<string>();
                var sortBy = GetArgument(args, "sort_by");
                var sortOrder = GetArgument(args, "sort_order");
                var perPage = args.Value<int?>("per_page");
                if (sortBy != null) queryParams.Add($"sort_by={Uri.EscapeDataString(sortBy)}");
                if (sortOrder != null) queryParams.Add($"sort_order={Uri.EscapeDataString(sortOrder)}");
                if (perPage.HasValue) queryParams.Add($"per_page={perPage.Value}");
                if (queryParams.Count > 0) path += "?" + string.Join("&", queryParams);
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_ticket", "Get a specific ticket by ID. Returns ticket details but not the full comment thread — use get_ticket_comments for that.",
            schema: s => s
                .String("ticket_id", "The ticket ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                return await CallZendeskApi("GET", $"/tickets/{id}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_ticket", "Create a new ticket. At minimum requires a comment body. Subject, priority, type, requester, assignee, and tags are optional.",
            schema: s => s
                .String("subject", "Ticket subject line")
                .String("comment_body", "The ticket description / first comment body", required: true)
                .String("priority", "Ticket priority: urgent, high, normal, or low")
                .String("type", "Ticket type: problem, incident, question, or task")
                .String("status", "Ticket status: new, open, pending, hold, solved")
                .String("requester_email", "Email of the requester (creates user if not found)")
                .String("requester_name", "Name of the requester (used with requester_email)")
                .Integer("assignee_id", "Agent user ID to assign the ticket to")
                .Integer("group_id", "Group ID to assign the ticket to")
                .String("tags", "Comma-separated tags to apply")
                .String("external_id", "External ID to link to local records"),
            handler: async (args, ct) =>
            {
                var ticket = new JObject();
                var comment = new JObject { ["body"] = RequireArgument(args, "comment_body") };
                ticket["comment"] = comment;

                var subject = GetArgument(args, "subject");
                if (subject != null) ticket["subject"] = subject;
                var priority = GetArgument(args, "priority");
                if (priority != null) ticket["priority"] = priority;
                var type = GetArgument(args, "type");
                if (type != null) ticket["type"] = type;
                var status = GetArgument(args, "status");
                if (status != null) ticket["status"] = status;
                var assigneeId = args.Value<int?>("assignee_id");
                if (assigneeId.HasValue) ticket["assignee_id"] = assigneeId.Value;
                var groupId = args.Value<int?>("group_id");
                if (groupId.HasValue) ticket["group_id"] = groupId.Value;
                var externalId = GetArgument(args, "external_id");
                if (externalId != null) ticket["external_id"] = externalId;

                var requesterEmail = GetArgument(args, "requester_email");
                if (requesterEmail != null)
                {
                    var requester = new JObject { ["email"] = requesterEmail };
                    var requesterName = GetArgument(args, "requester_name");
                    if (requesterName != null) requester["name"] = requesterName;
                    ticket["requester"] = requester;
                }

                var tags = GetArgument(args, "tags");
                if (tags != null) ticket["tags"] = new JArray(tags.Split(',').Select(t => t.Trim()));

                return await CallZendeskApi("POST", "/tickets.json", new JObject { ["ticket"] = ticket });
            });

        handler.AddTool("update_ticket", "Update an existing ticket. Can change status, priority, assignee, add comments, tags, etc.",
            schema: s => s
                .String("ticket_id", "The ticket ID to update", required: true)
                .String("status", "New status: new, open, pending, hold, solved, closed")
                .String("priority", "New priority: urgent, high, normal, low")
                .String("type", "New type: problem, incident, question, task")
                .String("subject", "New subject")
                .Integer("assignee_id", "New assignee user ID")
                .Integer("group_id", "New group ID")
                .String("comment_body", "Add a comment to the ticket")
                .Boolean("comment_public", "Whether the comment is public (default true)")
                .String("tags", "Comma-separated tags (replaces existing tags)")
                .String("additional_tags", "Comma-separated tags to add without removing existing ones"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                var ticket = new JObject();

                var status = GetArgument(args, "status");
                if (status != null) ticket["status"] = status;
                var priority = GetArgument(args, "priority");
                if (priority != null) ticket["priority"] = priority;
                var type = GetArgument(args, "type");
                if (type != null) ticket["type"] = type;
                var subject = GetArgument(args, "subject");
                if (subject != null) ticket["subject"] = subject;
                var assigneeId = args.Value<int?>("assignee_id");
                if (assigneeId.HasValue) ticket["assignee_id"] = assigneeId.Value;
                var groupId = args.Value<int?>("group_id");
                if (groupId.HasValue) ticket["group_id"] = groupId.Value;

                var commentBody = GetArgument(args, "comment_body");
                if (commentBody != null)
                {
                    var comment = new JObject { ["body"] = commentBody };
                    var isPublic = args.Value<bool?>("comment_public");
                    if (isPublic.HasValue) comment["public"] = isPublic.Value;
                    ticket["comment"] = comment;
                }

                var tags = GetArgument(args, "tags");
                if (tags != null) ticket["tags"] = new JArray(tags.Split(',').Select(t => t.Trim()));

                var additionalTags = GetArgument(args, "additional_tags");
                if (additionalTags != null) ticket["additional_tags"] = new JArray(additionalTags.Split(',').Select(t => t.Trim()));

                return await CallZendeskApi("PUT", $"/tickets/{id}.json", new JObject { ["ticket"] = ticket });
            });

        handler.AddTool("delete_ticket", "Delete a ticket (soft delete). Requires admin or agent with delete permission.",
            schema: s => s
                .String("ticket_id", "The ticket ID to delete", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                await CallZendeskApi("DELETE", $"/tickets/{id}.json");
                return new JObject { ["success"] = true, ["deleted_ticket_id"] = id };
            });

        handler.AddTool("merge_tickets", "Merge one or more tickets into a target ticket. Source tickets are closed.",
            schema: s => s
                .String("target_ticket_id", "The target ticket ID to merge into", required: true)
                .String("source_ticket_ids", "Comma-separated list of source ticket IDs to merge", required: true)
                .String("target_comment", "Private comment to add to the target ticket")
                .String("source_comment", "Private comment to add to source tickets"),
            handler: async (args, ct) =>
            {
                var targetId = RequireArgument(args, "target_ticket_id");
                var sourceIds = RequireArgument(args, "source_ticket_ids")
                    .Split(',').Select(s => int.Parse(s.Trim())).ToArray();
                var body = new JObject { ["ids"] = new JArray(sourceIds) };
                var targetComment = GetArgument(args, "target_comment");
                if (targetComment != null) body["target_comment"] = targetComment;
                var sourceComment = GetArgument(args, "source_comment");
                if (sourceComment != null) body["source_comment"] = sourceComment;
                return await CallZendeskApi("POST", $"/tickets/{targetId}/merge.json", body);
            });

        // ── Ticket Comments ──────────────────────────────────────────────

        handler.AddTool("get_ticket_comments", "Get all comments on a ticket. Returns the full conversation thread including public and private comments.",
            schema: s => s
                .String("ticket_id", "The ticket ID", required: true)
                .String("sort_order", "Sort order: asc (oldest first) or desc (newest first)"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                var path = $"/tickets/{id}/comments.json";
                var sortOrder = GetArgument(args, "sort_order");
                if (sortOrder != null) path += $"?sort_order={sortOrder}";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Users ────────────────────────────────────────────────────────

        handler.AddTool("list_users", "List users in the account.",
            schema: s => s
                .String("role", "Filter by role: end-user, agent, admin")
                .Integer("per_page", "Number of records per page (max 100)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var path = "/users.json";
                var queryParams = new List<string>();
                var role = GetArgument(args, "role");
                if (role != null) queryParams.Add($"role={Uri.EscapeDataString(role)}");
                var perPage = args.Value<int?>("per_page");
                if (perPage.HasValue) queryParams.Add($"per_page={perPage.Value}");
                if (queryParams.Count > 0) path += "?" + string.Join("&", queryParams);
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_user", "Get a specific user by ID.",
            schema: s => s
                .String("user_id", "The user ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "user_id");
                return await CallZendeskApi("GET", $"/users/{id}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_user", "Create a new user (end-user, agent, or admin).",
            schema: s => s
                .String("name", "User's full name", required: true)
                .String("email", "User's email address", required: true)
                .String("role", "User role: end-user, agent, or admin (default: end-user)")
                .String("phone", "User's phone number")
                .Integer("organization_id", "Organization ID to associate with"),
            handler: async (args, ct) =>
            {
                var user = new JObject
                {
                    ["name"] = RequireArgument(args, "name"),
                    ["email"] = RequireArgument(args, "email")
                };
                var role = GetArgument(args, "role");
                if (role != null) user["role"] = role;
                var phone = GetArgument(args, "phone");
                if (phone != null) user["phone"] = phone;
                var orgId = args.Value<int?>("organization_id");
                if (orgId.HasValue) user["organization_id"] = orgId.Value;
                return await CallZendeskApi("POST", "/users.json", new JObject { ["user"] = user });
            });

        handler.AddTool("update_user", "Update an existing user's profile.",
            schema: s => s
                .String("user_id", "The user ID to update", required: true)
                .String("name", "Updated name")
                .String("email", "Updated email")
                .String("phone", "Updated phone")
                .String("role", "Updated role: end-user, agent, admin")
                .Integer("organization_id", "Updated organization ID"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "user_id");
                var user = new JObject();
                var name = GetArgument(args, "name");
                if (name != null) user["name"] = name;
                var email = GetArgument(args, "email");
                if (email != null) user["email"] = email;
                var phone = GetArgument(args, "phone");
                if (phone != null) user["phone"] = phone;
                var role = GetArgument(args, "role");
                if (role != null) user["role"] = role;
                var orgId = args.Value<int?>("organization_id");
                if (orgId.HasValue) user["organization_id"] = orgId.Value;
                return await CallZendeskApi("PUT", $"/users/{id}.json", new JObject { ["user"] = user });
            });

        handler.AddTool("get_user_tickets", "Get tickets requested by a specific user.",
            schema: s => s
                .String("user_id", "The user ID", required: true)
                .String("type", "Ticket relationship: requested (default), ccd, assigned"),
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "user_id");
                var ticketType = GetArgument(args, "type", "requested");
                return await CallZendeskApi("GET", $"/users/{userId}/tickets/{ticketType}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Organizations ────────────────────────────────────────────────

        handler.AddTool("list_organizations", "List organizations in the account.",
            schema: s => s
                .Integer("per_page", "Number of records per page (max 100)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var path = "/organizations.json";
                var perPage = args.Value<int?>("per_page");
                if (perPage.HasValue) path += $"?per_page={perPage.Value}";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_organization", "Get a specific organization by ID.",
            schema: s => s
                .String("organization_id", "The organization ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "organization_id");
                return await CallZendeskApi("GET", $"/organizations/{id}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_organization", "Create a new organization.",
            schema: s => s
                .String("name", "Organization name", required: true)
                .String("details", "Organization details")
                .String("notes", "Organization notes")
                .Integer("group_id", "Default group ID"),
            handler: async (args, ct) =>
            {
                var org = new JObject { ["name"] = RequireArgument(args, "name") };
                var details = GetArgument(args, "details");
                if (details != null) org["details"] = details;
                var notes = GetArgument(args, "notes");
                if (notes != null) org["notes"] = notes;
                var groupId = args.Value<int?>("group_id");
                if (groupId.HasValue) org["group_id"] = groupId.Value;
                return await CallZendeskApi("POST", "/organizations.json", new JObject { ["organization"] = org });
            });

        handler.AddTool("get_organization_tickets", "Get tickets for a specific organization.",
            schema: s => s
                .String("organization_id", "The organization ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "organization_id");
                return await CallZendeskApi("GET", $"/organizations/{id}/tickets.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Groups ───────────────────────────────────────────────────────

        handler.AddTool("list_groups", "List all agent groups.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/groups.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_group", "Get a specific group by ID.",
            schema: s => s
                .String("group_id", "The group ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "group_id");
                return await CallZendeskApi("GET", $"/groups/{id}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Views ────────────────────────────────────────────────────────

        handler.AddTool("list_views", "List all shared and personal views available to the current user.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/views.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_view_tickets", "Execute a view and return the tickets it contains.",
            schema: s => s
                .String("view_id", "The view ID to execute", required: true)
                .Integer("per_page", "Number of records per page (max 100)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "view_id");
                var path = $"/views/{id}/tickets.json";
                var perPage = args.Value<int?>("per_page");
                if (perPage.HasValue) path += $"?per_page={perPage.Value}";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_view_count", "Get the ticket count for a view.",
            schema: s => s
                .String("view_id", "The view ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "view_id");
                return await CallZendeskApi("GET", $"/views/{id}/count.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Satisfaction Ratings ──────────────────────────────────────────

        handler.AddTool("list_satisfaction_ratings", "List satisfaction ratings. Can filter by score (good, bad) or date range.",
            schema: s => s
                .String("score", "Filter by score: offered, unoffered, received, received_with_comment, received_without_comment, good, bad")
                .String("start_time", "Start time for filtering (ISO 8601)")
                .String("end_time", "End time for filtering (ISO 8601)"),
            handler: async (args, ct) =>
            {
                var path = "/satisfaction_ratings.json";
                var queryParams = new List<string>();
                var score = GetArgument(args, "score");
                if (score != null) queryParams.Add($"score={Uri.EscapeDataString(score)}");
                var startTime = GetArgument(args, "start_time");
                if (startTime != null) queryParams.Add($"start_time={Uri.EscapeDataString(startTime)}");
                var endTime = GetArgument(args, "end_time");
                if (endTime != null) queryParams.Add($"end_time={Uri.EscapeDataString(endTime)}");
                if (queryParams.Count > 0) path += "?" + string.Join("&", queryParams);
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Tags ─────────────────────────────────────────────────────────

        handler.AddTool("list_tags", "List the most popular recent tags in the account.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/tags.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Ticket Metrics ───────────────────────────────────────────────

        handler.AddTool("get_ticket_metrics", "Get a ticket's time metrics: first reply time, full resolution time, requester wait time, and agent wait time.",
            schema: s => s
                .String("ticket_id", "The ticket ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                return await CallZendeskApi("GET", $"/tickets/{id}/metrics.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Ticket Related Info ──────────────────────────────────────────

        handler.AddTool("get_ticket_related", "Get related information for a ticket (incidents count, Jira issues, followup sources).",
            schema: s => s
                .String("ticket_id", "The ticket ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                return await CallZendeskApi("GET", $"/tickets/{id}/related.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Ticket Audits ────────────────────────────────────────────────

        handler.AddTool("get_ticket_audits", "Get the full audit trail for a ticket, showing all changes and events.",
            schema: s => s
                .String("ticket_id", "The ticket ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                return await CallZendeskApi("GET", $"/tickets/{id}/audits.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Macros ───────────────────────────────────────────────────────

        handler.AddTool("list_macros", "List available macros (predefined ticket actions).",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/macros.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Ticket Fields ────────────────────────────────────────────────

        handler.AddTool("list_ticket_fields", "List all ticket fields (system and custom fields) and their configurations.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/ticket_fields.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Help Center / Knowledge Base ─────────────────────────────────

        handler.AddTool("list_articles", "List Help Center articles. Can filter by section, category, or label. Articles are sorted by position by default.",
            schema: s => s
                .Integer("section_id", "Filter articles by section ID")
                .Integer("category_id", "Filter articles by category ID")
                .String("label_names", "Comma-separated label names to filter by")
                .String("sort_by", "Sort by: position, title, created_at, updated_at")
                .String("sort_order", "Sort order: asc or desc")
                .Integer("per_page", "Number of records per page (max 100)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var sectionId = args.Value<int?>("section_id");
                var categoryId = args.Value<int?>("category_id");
                string path;
                if (sectionId.HasValue)
                    path = $"/help_center/sections/{sectionId.Value}/articles.json";
                else if (categoryId.HasValue)
                    path = $"/help_center/categories/{categoryId.Value}/articles.json";
                else
                    path = "/help_center/articles.json";
                var queryParams = new List<string>();
                var labelNames = GetArgument(args, "label_names");
                if (labelNames != null) queryParams.Add($"label_names={Uri.EscapeDataString(labelNames)}");
                var sortBy = GetArgument(args, "sort_by");
                if (sortBy != null) queryParams.Add($"sort_by={Uri.EscapeDataString(sortBy)}");
                var sortOrder = GetArgument(args, "sort_order");
                if (sortOrder != null) queryParams.Add($"sort_order={Uri.EscapeDataString(sortOrder)}");
                var perPage = args.Value<int?>("per_page");
                if (perPage.HasValue) queryParams.Add($"per_page={perPage.Value}");
                if (queryParams.Count > 0) path += "?" + string.Join("&", queryParams);
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_article", "Get a specific Help Center article by ID including its body, labels, and metadata.",
            schema: s => s
                .String("article_id", "The article ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "article_id");
                return await CallZendeskApi("GET", $"/help_center/articles/{id}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("search_articles", "Search Help Center articles by keyword. Returns articles matching the query with highlights.",
            schema: s => s
                .String("query", "Search query string", required: true)
                .Integer("category_id", "Filter results to a specific category")
                .Integer("section_id", "Filter results to a specific section")
                .String("label_names", "Comma-separated label names to filter by")
                .Integer("per_page", "Number of records per page (max 100)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var query = RequireArgument(args, "query");
                var path = $"/help_center/articles/search.json?query={Uri.EscapeDataString(query)}";
                var categoryId = args.Value<int?>("category_id");
                if (categoryId.HasValue) path += $"&category={categoryId.Value}";
                var sectionId = args.Value<int?>("section_id");
                if (sectionId.HasValue) path += $"&section={sectionId.Value}";
                var labelNames = GetArgument(args, "label_names");
                if (labelNames != null) path += $"&label_names={Uri.EscapeDataString(labelNames)}";
                var perPage = args.Value<int?>("per_page");
                if (perPage.HasValue) path += $"&per_page={perPage.Value}";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_article", "Create a new Help Center article in a specified section.",
            schema: s => s
                .Integer("section_id", "The section ID to create the article in", required: true)
                .String("title", "Article title", required: true)
                .String("body", "Article body (HTML)", required: true)
                .String("locale", "Article locale (e.g., en-us)", required: true)
                .Boolean("draft", "Whether the article is a draft (default true)")
                .String("label_names", "Comma-separated label names to apply"),
            handler: async (args, ct) =>
            {
                var sectionId = RequireArgument(args, "section_id");
                var article = new JObject
                {
                    ["title"] = RequireArgument(args, "title"),
                    ["body"] = RequireArgument(args, "body"),
                    ["locale"] = RequireArgument(args, "locale")
                };
                var draft = args.Value<bool?>("draft");
                if (draft.HasValue) article["draft"] = draft.Value;
                var labelNames = GetArgument(args, "label_names");
                if (labelNames != null) article["label_names"] = new JArray(labelNames.Split(',').Select(l => l.Trim()));
                return await CallZendeskApi("POST", $"/help_center/sections/{sectionId}/articles.json", new JObject { ["article"] = article });
            });

        handler.AddTool("list_sections", "List Help Center sections. Can filter by category.",
            schema: s => s
                .Integer("category_id", "Filter sections by category ID"),
            handler: async (args, ct) =>
            {
                var categoryId = args.Value<int?>("category_id");
                var path = categoryId.HasValue
                    ? $"/help_center/categories/{categoryId.Value}/sections.json"
                    : "/help_center/sections.json";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_categories", "List Help Center categories.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/help_center/categories.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── SLA Policies ─────────────────────────────────────────────────

        handler.AddTool("list_sla_policies", "List all SLA policies and their targets (first reply time, next reply time, resolution time).",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallZendeskApi("GET", "/slas/policies.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_sla_policy", "Get a specific SLA policy by ID including its filter conditions and metric targets.",
            schema: s => s
                .String("policy_id", "The SLA policy ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                return await CallZendeskApi("GET", $"/slas/policies/{id}.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Ticket Attachments ───────────────────────────────────────────

        handler.AddTool("list_ticket_attachments", "List all attachments on a ticket by retrieving its comments and extracting attachment metadata (filename, size, content URL).",
            schema: s => s
                .String("ticket_id", "The ticket ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "ticket_id");
                var comments = await CallZendeskApi("GET", $"/tickets/{id}/comments.json");
                var attachments = new JArray();
                var commentsArray = comments["comments"] as JArray;
                if (commentsArray != null)
                {
                    foreach (var comment in commentsArray)
                    {
                        var commentAttachments = comment["attachments"] as JArray;
                        if (commentAttachments != null && commentAttachments.Count > 0)
                        {
                            foreach (var att in commentAttachments)
                            {
                                attachments.Add(new JObject
                                {
                                    ["id"] = att["id"],
                                    ["file_name"] = att["file_name"],
                                    ["content_url"] = att["content_url"],
                                    ["content_type"] = att["content_type"],
                                    ["size"] = att["size"],
                                    ["comment_id"] = comment["id"],
                                    ["comment_created_at"] = comment["created_at"]
                                });
                            }
                        }
                    }
                }
                return new JObject
                {
                    ["ticket_id"] = id,
                    ["attachment_count"] = attachments.Count,
                    ["attachments"] = attachments
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── User Identities ──────────────────────────────────────────────

        handler.AddTool("list_user_identities", "List all identities (email addresses, phone numbers, X/Twitter handles, etc.) for a user.",
            schema: s => s
                .String("user_id", "The user ID", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "user_id");
                return await CallZendeskApi("GET", $"/users/{id}/identities.json");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Organization Memberships ─────────────────────────────────────

        handler.AddTool("list_organization_memberships", "List organization memberships. Can list all memberships, or filter by user or organization.",
            schema: s => s
                .String("user_id", "Filter memberships by user ID")
                .String("organization_id", "Filter memberships by organization ID"),
            handler: async (args, ct) =>
            {
                var userId = GetArgument(args, "user_id");
                var orgId = GetArgument(args, "organization_id");
                string path;
                if (userId != null)
                    path = $"/users/{userId}/organization_memberships.json";
                else if (orgId != null)
                    path = $"/organizations/{orgId}/organization_memberships.json";
                else
                    path = "/organization_memberships.json";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Group Memberships ────────────────────────────────────────────

        handler.AddTool("list_group_memberships", "List group memberships showing which agents belong to which groups. Can filter by user or group.",
            schema: s => s
                .String("user_id", "Filter memberships by user (agent) ID")
                .String("group_id", "Filter memberships by group ID"),
            handler: async (args, ct) =>
            {
                var userId = GetArgument(args, "user_id");
                var groupId = GetArgument(args, "group_id");
                string path;
                if (userId != null)
                    path = $"/users/{userId}/group_memberships.json";
                else if (groupId != null)
                    path = $"/groups/{groupId}/memberships.json";
                else
                    path = "/group_memberships.json";
                return await CallZendeskApi("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ══════════════════════════════════════════════════════════════════
        //  RESOURCES
        // ══════════════════════════════════════════════════════════════════

        handler.AddResource("zendesk://reference/search-syntax", "Zendesk Search Syntax Reference",
            "Comprehensive guide to Zendesk search query syntax including operators, field names, date ranges, and examples.",
            handler: async (ct) =>
            {
                return new JArray
                {
                    new JObject
                    {
                        ["uri"] = "zendesk://reference/search-syntax",
                        ["mimeType"] = "text/plain",
                        ["text"] = GetSearchSyntaxReference()
                    }
                };
            },
            mimeType: "text/plain");

        handler.AddResource("zendesk://reference/ticket-statuses", "Zendesk Ticket Statuses Reference",
            "Reference for ticket statuses, their transitions, and custom ticket status support.",
            handler: async (ct) =>
            {
                return new JArray
                {
                    new JObject
                    {
                        ["uri"] = "zendesk://reference/ticket-statuses",
                        ["mimeType"] = "text/plain",
                        ["text"] = GetTicketStatusesReference()
                    }
                };
            },
            mimeType: "text/plain");

        // ── Resource Templates ───────────────────────────────────────────

        handler.AddResourceTemplate("zendesk://tickets/{id}", "Ticket by ID",
            "Retrieve a specific ticket by its ID.",
            handler: async (uri, ct) =>
            {
                var parameters = McpRequestHandler.ExtractUriParameters("zendesk://tickets/{id}", uri);
                var id = parameters.ContainsKey("id") ? parameters["id"] : "unknown";
                var ticket = await CallZendeskApi("GET", $"/tickets/{id}.json");
                return new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "application/json",
                        ["text"] = ticket.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                };
            });

        handler.AddResourceTemplate("zendesk://users/{id}", "User by ID",
            "Retrieve a specific user by their ID.",
            handler: async (uri, ct) =>
            {
                var parameters = McpRequestHandler.ExtractUriParameters("zendesk://users/{id}", uri);
                var id = parameters.ContainsKey("id") ? parameters["id"] : "unknown";
                var user = await CallZendeskApi("GET", $"/users/{id}.json");
                return new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "application/json",
                        ["text"] = user.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                };
            });

        // ══════════════════════════════════════════════════════════════════
        //  PROMPTS
        // ══════════════════════════════════════════════════════════════════

        handler.AddPrompt("triage_ticket", "Analyze a ticket and suggest priority, type, group assignment, and next steps.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "ticket_id", Description = "The ticket ID to triage", Required = true }
            },
            handler: async (args, ct) =>
            {
                var ticketId = args.Value<string>("ticket_id") ?? "";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Retrieve ticket {ticketId} and its comments from Zendesk. Analyze the content and suggest: 1) Appropriate priority (urgent/high/normal/low), 2) Ticket type (problem/incident/question/task), 3) Which group to assign it to, 4) Recommended next steps for resolution. Provide a brief summary of the issue."
                        }
                    }
                };
            });

        handler.AddPrompt("summarize_ticket", "Summarize a ticket's conversation for handoff or escalation.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "ticket_id", Description = "The ticket ID to summarize", Required = true },
                new McpPromptArgument { Name = "style", Description = "Summary style: brief, detailed, or bullets", Required = false }
            },
            handler: async (args, ct) =>
            {
                var ticketId = args.Value<string>("ticket_id") ?? "";
                var style = args.Value<string>("style") ?? "brief";
                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] = $"Retrieve ticket {ticketId} and all its comments. Create a {style} summary that includes: the original issue, key interactions, current status, and any outstanding action items. This summary should be suitable for agent handoff or escalation."
                        }
                    }
                };
            });
    }

    // ── Resource Content ──────────────────────────────────────────────────

    private string GetSearchSyntaxReference()
    {
        return @"# Zendesk Search Syntax Reference

## Basic Search
Simply type keywords to search across all objects:
  printer fire                    — finds tickets, users, orgs containing these words

## Filtering by Object Type
  type:ticket                     — only tickets
  type:user                       — only users
  type:organization               — only organizations
  type:group                      — only groups
  type:topic                      — only community topics

## Ticket Field Filters
  status:open                     — by status (new, open, pending, hold, solved, closed)
  status<solved                   — less than solved (new, open, pending, hold)
  priority:urgent                 — by priority (low, normal, high, urgent)
  priority>normal                 — greater than normal (high, urgent)
  ticket_type:problem             — by type (question, incident, problem, task)
  assignee:john                   — assigned to agent named john
  assignee:me                     — assigned to current user
  requester:jane                  — requested by user named jane
  group:support                   — in group named ""support""
  tags:vip                        — has tag ""vip""
  subject:""printer fire""          — subject contains phrase
  description:""cannot login""      — description contains phrase
  via:email                       — created via email channel
  via:web                         — created via web form

## Date Filters
  created>2024-01-01              — created after date
  created<2024-06-01              — created before date
  created:2024-01-01..2024-06-01  — created in range
  updated>24hours                 — updated in last 24 hours
  updated>7days                   — updated in last 7 days
  solved>1month                   — solved more than 1 month ago

## User Filters
  role:agent                      — agents
  role:admin                      — admins
  role:end-user                   — end users
  email:john@example.com          — by email
  phone:555-1234                  — by phone
  organization:""Acme Inc""         — by organization name

## Organization Filters  
  name:""Acme Inc""                 — by organization name
  tags:enterprise                 — by organization tag

## Operators
  AND                             — both conditions (default)
  OR                              — either condition
  -                               — NOT (exclude): -status:closed
  ""exact phrase""                  — exact phrase match
  *                               — wildcard: print*  matches printer, printing

## Combining Filters
  type:ticket status:open priority:urgent assignee:me
  type:ticket created>7days -status:closed
  type:ticket (status:open OR status:pending) priority>normal
  type:user role:agent organization:""Acme Inc""

## Common Examples

### Find open urgent tickets assigned to me
  type:ticket status:open priority:urgent assignee:me

### Find all unresolved tickets for an organization
  type:ticket status<solved organization:""Acme Inc""

### Find tickets created in the last 7 days
  type:ticket created>7days

### Find tickets with specific tag
  type:ticket tags:billing -status:closed

### Find end-users by email domain
  type:user role:end-user email:@example.com

### Find tickets that mention ""password reset""
  type:ticket ""password reset"" -status:closed

### Find pending tickets older than 3 days
  type:ticket status:pending updated<3days
";
    }

    private string GetTicketStatusesReference()
    {
        return @"# Zendesk Ticket Statuses Reference

## Standard Statuses

| Status  | Description |
|---------|-------------|
| new     | Ticket has not been assigned to an agent yet |
| open    | Ticket is assigned and being worked on |
| pending | Agent is waiting for more info from the requester |
| hold    | Agent is waiting on a third party (not the requester) |
| solved  | Issue has been resolved; can be reopened by requester |
| closed  | Permanently closed; cannot be reopened (auto-closes after time) |

## Status Transitions
- new → open (when assigned to an agent)
- open → pending (waiting for customer reply)
- open → hold (waiting for third party)
- open → solved (issue resolved)
- pending → open (customer replied)
- hold → open (third party responded)
- solved → open (customer replies after solve)
- solved → closed (automated after retention period)
- closed → (no transitions; create a follow-up ticket instead)

## Priority Levels
| Priority | Description |
|----------|-------------|
| urgent   | Needs immediate attention |
| high     | Important but not immediately critical |
| normal   | Standard priority (default) |
| low      | Nice to address but not time-sensitive |

## Ticket Types
| Type     | Description |
|----------|-------------|
| question | General inquiry |
| incident | Issue affecting one requester (can link to a problem) |
| problem  | Issue affecting many users (link incidents to it) |
| task     | Work item with a due date |

## Custom Ticket Statuses
Accounts with custom statuses have:
- custom_status_id: The specific custom status
- status: The status category (new, open, pending, hold, solved, closed)
- Use sideloading to get custom status labels: GET /api/v2/tickets?include=custom_statuses
";
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<JObject> CallZendeskApi(string method, string path, JObject body = null)
    {
        var baseUri = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var fullUrl = $"{baseUri}/api/v2{path}";

        var request = new HttpRequestMessage(new HttpMethod(method), fullUrl);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == "POST" || method == "PUT" || method == "PATCH"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Zendesk API returned {(int)response.StatusCode}: {content}");

        if (string.IsNullOrWhiteSpace(content) || (int)response.StatusCode == 204)
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try
        {
            if (content.TrimStart().StartsWith("["))
                return new JObject { ["items"] = JArray.Parse(content) };
            return JObject.Parse(content);
        }
        catch
        {
            return new JObject { ["text"] = content };
        }
    }

    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
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
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
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
        catch { /* Suppress telemetry errors */ }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section   ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                            ║
// ║  Spec coverage: MCP 2025-11-25                                             ║
// ║  Handles: initialize, ping, tools/*, resources/*, prompts/*,               ║
// ║           completion/complete, logging/setLevel, all notifications          ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public McpRequestHandler AddTool(
        string name, string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);
        JObject ann = null;
        if (annotations != null) { ann = new JObject(); annotations(ann); }
        JObject outputSchema = null;
        if (outputSchemaConfig != null) { var ob = new McpSchemaBuilder(); outputSchemaConfig(ob); outputSchema = ob.Build(); }
        _tools[name] = new McpToolDefinition
        {
            Name = name, Title = title, Description = description,
            InputSchema = builder.Build(), OutputSchema = outputSchema, Annotations = ann,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };
        return this;
    }

    public McpRequestHandler AddResource(string uri, string name, string description,
        Func<CancellationToken, Task<JArray>> handler, string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject ann = null;
        if (annotationsConfig != null) { ann = new JObject(); annotationsConfig(ann); }
        _resources[uri] = new McpResourceDefinition
        {
            Uri = uri, Name = name, Description = description,
            MimeType = mimeType, Annotations = ann, Handler = handler
        };
        return this;
    }

    public McpRequestHandler AddResourceTemplate(string uriTemplate, string name, string description,
        Func<string, CancellationToken, Task<JArray>> handler, string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject ann = null;
        if (annotationsConfig != null) { ann = new JObject(); annotationsConfig(ann); }
        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate, Name = name, Description = description,
            MimeType = mimeType, Annotations = ann, Handler = handler
        });
        return this;
    }

    public McpRequestHandler AddPrompt(string name, string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        _prompts[name] = new McpPromptDefinition
        {
            Name = name, Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(), Handler = handler
        };
        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];
        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize": return HandleInitialize(id, request);
                case "initialized": case "notifications/initialized":
                case "notifications/cancelled": case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());
                case "ping": return SerializeSuccess(id, new JObject());
                case "tools/list": return HandleToolsList(id);
                case "tools/call": return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "resources/list": return HandleResourcesList(id);
                case "resources/templates/list": return HandleResourceTemplatesList(id);
                case "resources/read": return await HandleResourcesReadAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "resources/subscribe": case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());
                case "prompts/list": return HandlePromptsList(id);
                case "prompts/get": return await HandlePromptsGetAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });
                case "logging/setLevel": return SerializeSuccess(id, new JObject());
                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = method, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;
        var capabilities = new JObject();
        if (_options.Capabilities.Tools) capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources) capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts) capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging) capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions) capabilities["completions"] = new JObject();

        var serverInfo = new JObject { ["name"] = _options.ServerInfo.Name, ["version"] = _options.ServerInfo.Version };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title)) serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description)) serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject { ["protocolVersion"] = clientProtocolVersion, ["capabilities"] = capabilities, ["serverInfo"] = serverInfo };
        if (!string.IsNullOrWhiteSpace(_options.Instructions)) result["instructions"] = _options.Instructions;
        Log("McpInitialized", new { Server = _options.ServerInfo.Name, Version = _options.ServerInfo.Version, ProtocolVersion = clientProtocolVersion });
        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var obj = new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.InputSchema };
            if (!string.IsNullOrWhiteSpace(tool.Title)) obj["title"] = tool.Title;
            if (tool.OutputSchema != null) obj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0) obj["annotations"] = tool.Annotations;
            toolsArray.Add(obj);
        }
        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private string HandleResourcesList(JToken id)
    {
        var arr = new JArray();
        foreach (var res in _resources.Values)
        {
            var obj = new JObject { ["uri"] = res.Uri, ["name"] = res.Name };
            if (!string.IsNullOrWhiteSpace(res.Description)) obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType)) obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0) obj["annotations"] = res.Annotations;
            arr.Add(obj);
        }
        Log("McpResourcesListed", new { Count = _resources.Count });
        return SerializeSuccess(id, new JObject { ["resources"] = arr });
    }

    private string HandleResourceTemplatesList(JToken id)
    {
        var arr = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject { ["uriTemplate"] = tmpl.UriTemplate, ["name"] = tmpl.Name };
            if (!string.IsNullOrWhiteSpace(tmpl.Description)) obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType)) obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0) obj["annotations"] = tmpl.Annotations;
            arr.Add(obj);
        }
        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(id, new JObject { ["resourceTemplates"] = arr });
    }

    private async Task<string> HandleResourcesReadAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");
        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(id, McpErrorCode.InvalidParams, "Resource URI is required");

        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(id, new JObject { ["contents"] = contents });
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(id, McpErrorCode.InternalError, ex.Message);
            }
        }

        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(id, new JObject { ["contents"] = contents });
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }
        return SerializeError(id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    private static bool MatchesUriTemplate(string template, string uri)
    {
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');
        if (templateParts.Length != uriParts.Length) return false;
        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue;
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');
        if (templateParts.Length != uriParts.Length) return result;
        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
                result[seg.Substring(1, seg.Length - 2)] = uriParts[i];
        }
        return result;
    }

    private string HandlePromptsList(JToken id)
    {
        var arr = new JArray();
        foreach (var prompt in _prompts.Values)
        {
            var obj = new JObject { ["name"] = prompt.Name };
            if (!string.IsNullOrWhiteSpace(prompt.Description)) obj["description"] = prompt.Description;
            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description)) argObj["description"] = arg.Description;
                    if (arg.Required) argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }
            arr.Add(obj);
        }
        Log("McpPromptsListed", new { Count = _prompts.Count });
        return SerializeSuccess(id, new JObject { ["prompts"] = arr });
    }

    private async Task<string> HandlePromptsGetAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Prompt name is required");
        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");
        Log("McpPromptGetStarted", new { Prompt = promptName });
        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });
            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description)) result["description"] = prompt.Description;
            return SerializeSuccess(id, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");
        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");
        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);
            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject { ["content"] = contentArray, ["isError"] = jobj.Value<bool?>("isError") ?? false };
                if (jobj["structuredContent"] is JObject structured) callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject plainObj) text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s) text = s;
                else if (result == null) text = "{}";
                else text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }
            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } },
                ["isError"] = true
            });
        }
    }

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }
            .ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }
            .ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) { OnLog?.Invoke(eventName, data); }
}

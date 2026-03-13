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
// ║  Planner MCP — all Graph Planner API beta endpoints as MCP tools,          ║
// ║  including business scenarios (preview).                                   ║
// ║  Tool registration uses the fluent AddTool API.                            ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string GraphBaseUrl = "https://graph.microsoft.com";

    // ── Server Configuration ─────────────────────────────────────────────

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "planner-mcp",
            Version = "1.0.0",
            Title = "Planner MCP",
            Description = "Manage Microsoft Planner plans, tasks, buckets, rosters, and business scenarios (preview) via MCP."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = false,
            Prompts = false,
            Logging = true,
            Completions = false
        },
        Instructions = "Use this server to manage Microsoft Planner. You can create, read, update, and delete plans, tasks, buckets, and rosters. Business scenario tools (preview) let you configure scenario-controlled Planner tasks with custom policies. Note: Planner uses ETags for concurrency control — you must read an item first to get its @odata.etag before updating or deleting it."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (this.Context.OperationId != "InvokeMCP")
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        var handler = new McpRequestHandler(Options);
        RegisterTools(handler);

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


    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        // ── Plans ────────────────────────────────────────────────────────

        handler.AddTool("list_group_plans", "Get all Planner plans for a Microsoft 365 group.",
            schema: s => s.String("groupId", "The ID of the Microsoft 365 group", required: true),
            handler: async (args, ct) =>
            {
                var groupId = RequireArgument(args, "groupId");
                return await SendGraphRequestAsync("GET", $"/v1.0/groups/{groupId}/planner/plans");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_plan", "Get a specific Planner plan by ID. Returns @odata.etag needed for update/delete.",
            schema: s => s.String("planId", "The ID of the plan", required: true),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/plans/{planId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_plan", "Create a new Planner plan in a group or roster container.",
            schema: s => s
                .String("title", "The title of the plan", required: true)
                .String("containerId", "The ID of the container (group ID or roster ID)", required: true)
                .String("containerType", "Container type: group or roster", required: true, enumValues: new[] { "group", "roster" }),
            handler: async (args, ct) =>
            {
                var title = RequireArgument(args, "title");
                var containerId = RequireArgument(args, "containerId");
                var containerType = RequireArgument(args, "containerType");
                var body = new JObject
                {
                    ["title"] = title,
                    ["container"] = new JObject
                    {
                        ["url"] = containerType == "group"
                            ? $"https://graph.microsoft.com/beta/groups/{containerId}"
                            : $"https://graph.microsoft.com/beta/planner/rosters/{containerId}",
                        ["type"] = containerType
                    }
                };
                return await SendGraphRequestAsync("POST", "/v1.0/planner/plans", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_plan", "Update a Planner plan title. Requires @odata.etag from get_plan.",
            schema: s => s
                .String("planId", "The ID of the plan", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true)
                .String("title", "New title for the plan", required: true),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                var etag = RequireArgument(args, "etag");
                var title = RequireArgument(args, "title");
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/plans/{planId}",
                    new JObject { ["title"] = title }, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_plan", "Delete a Planner plan. Requires @odata.etag from get_plan.",
            schema: s => s
                .String("planId", "The ID of the plan", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                var etag = RequireArgument(args, "etag");
                return await SendGraphRequestAsync("DELETE", $"/v1.0/planner/plans/{planId}", null, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("get_plan_details", "Get plan details including category labels and shared-with info.",
            schema: s => s.String("planId", "The ID of the plan", required: true),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/plans/{planId}/details");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_plan_details", "Update plan details such as category labels. Requires @odata.etag from get_plan_details.",
            schema: s => s
                .String("planId", "The ID of the plan", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true)
                .String("category1", "Label for category 1")
                .String("category2", "Label for category 2")
                .String("category3", "Label for category 3")
                .String("category4", "Label for category 4")
                .String("category5", "Label for category 5")
                .String("category6", "Label for category 6"),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                var etag = RequireArgument(args, "etag");
                var cats = new JObject();
                for (int i = 1; i <= 6; i++)
                {
                    var val = GetArgument(args, $"category{i}");
                    if (val != null) cats[$"category{i}"] = val;
                }
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/plans/{planId}/details",
                    new JObject { ["categoryDescriptions"] = cats }, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("list_plan_buckets", "Get all buckets in a Planner plan.",
            schema: s => s.String("planId", "The ID of the plan", required: true),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/plans/{planId}/buckets");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_plan_tasks", "Get all tasks in a Planner plan.",
            schema: s => s.String("planId", "The ID of the plan", required: true),
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/plans/{planId}/tasks");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Tasks ────────────────────────────────────────────────────────

        handler.AddTool("get_task", "Get a specific Planner task by ID. Returns @odata.etag needed for update/delete.",
            schema: s => s.String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/tasks/{taskId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_task", "Create a new Planner task in a plan.",
            schema: s => s
                .String("planId", "The ID of the plan", required: true)
                .String("title", "Title of the task", required: true)
                .String("bucketId", "The ID of the bucket")
                .Integer("percentComplete", "Percentage complete (0-100)")
                .Integer("priority", "Priority (0=urgent, 1=urgent, 3=important, 5=medium, 9=low)")
                .String("dueDateTime", "Due date in ISO 8601 format (e.g., 2024-12-31T23:59:00Z)")
                .String("startDateTime", "Start date in ISO 8601 format")
                .String("assigneeUserId", "User ID to assign the task to"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["planId"] = RequireArgument(args, "planId"),
                    ["title"] = RequireArgument(args, "title")
                };
                if (args["bucketId"] != null) body["bucketId"] = args["bucketId"].ToString();
                if (args["percentComplete"] != null) body["percentComplete"] = (int)args["percentComplete"];
                if (args["priority"] != null) body["priority"] = (int)args["priority"];
                if (args["dueDateTime"] != null) body["dueDateTime"] = args["dueDateTime"].ToString();
                if (args["startDateTime"] != null) body["startDateTime"] = args["startDateTime"].ToString();
                if (args["assigneeUserId"] != null)
                {
                    body["assignments"] = new JObject
                    {
                        [args["assigneeUserId"].ToString()] = new JObject
                        {
                            ["@odata.type"] = "#microsoft.graph.plannerAssignment",
                            ["orderHint"] = " !"
                        }
                    };
                }
                return await SendGraphRequestAsync("POST", "/v1.0/planner/tasks", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_task", "Update a Planner task. Requires @odata.etag from get_task.",
            schema: s => s
                .String("taskId", "The ID of the task", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true)
                .String("title", "New title")
                .String("bucketId", "New bucket ID")
                .Integer("percentComplete", "Percentage complete (0-100). Set to 100 to complete.")
                .Integer("priority", "Priority (0=urgent, 1=urgent, 3=important, 5=medium, 9=low)")
                .String("dueDateTime", "Due date in ISO 8601 format")
                .String("startDateTime", "Start date in ISO 8601 format"),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                if (args["title"] != null) body["title"] = args["title"].ToString();
                if (args["bucketId"] != null) body["bucketId"] = args["bucketId"].ToString();
                if (args["percentComplete"] != null) body["percentComplete"] = (int)args["percentComplete"];
                if (args["priority"] != null) body["priority"] = (int)args["priority"];
                if (args["dueDateTime"] != null) body["dueDateTime"] = args["dueDateTime"].ToString();
                if (args["startDateTime"] != null) body["startDateTime"] = args["startDateTime"].ToString();
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/tasks/{taskId}", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("complete_task", "Mark a Planner task as completed (sets percentComplete to 100). Requires @odata.etag.",
            schema: s => s
                .String("taskId", "The ID of the task", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/tasks/{taskId}",
                    new JObject { ["percentComplete"] = 100 }, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_task", "Delete a Planner task. Requires @odata.etag from get_task.",
            schema: s => s
                .String("taskId", "The ID of the task", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                return await SendGraphRequestAsync("DELETE", $"/v1.0/planner/tasks/{taskId}", null, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("get_task_details", "Get task details including description, checklist, and references.",
            schema: s => s.String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/tasks/{taskId}/details");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_task_details", "Update task description and preview type. Requires @odata.etag from get_task_details.",
            schema: s => s
                .String("taskId", "The ID of the task", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true)
                .String("description", "Description of the task")
                .String("previewType", "Type of preview: automatic, noPreview, checklist, description, reference",
                    enumValues: new[] { "automatic", "noPreview", "checklist", "description", "reference" }),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                if (args["description"] != null) body["description"] = args["description"].ToString();
                if (args["previewType"] != null) body["previewType"] = args["previewType"].ToString();
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/tasks/{taskId}/details", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("list_my_tasks", "Get all Planner tasks assigned to the current user.",
            schema: s => s
                .Integer("top", "Number of tasks to return")
                .String("filter", "OData filter expression"),
            handler: async (args, ct) =>
            {
                var query = BuildQueryString(args, "filter", "top");
                return await SendGraphRequestAsync("GET", $"/v1.0/me/planner/tasks{query}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Buckets ──────────────────────────────────────────────────────

        handler.AddTool("get_bucket", "Get a specific Planner bucket by ID. Returns @odata.etag needed for update/delete.",
            schema: s => s.String("bucketId", "The ID of the bucket", required: true),
            handler: async (args, ct) =>
            {
                var bucketId = RequireArgument(args, "bucketId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/buckets/{bucketId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_bucket", "Create a new bucket in a Planner plan.",
            schema: s => s
                .String("planId", "The ID of the plan", required: true)
                .String("name", "Name of the bucket", required: true)
                .String("orderHint", "Hint for ordering the bucket"),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["planId"] = RequireArgument(args, "planId"),
                    ["name"] = RequireArgument(args, "name")
                };
                if (args["orderHint"] != null) body["orderHint"] = args["orderHint"].ToString();
                return await SendGraphRequestAsync("POST", "/v1.0/planner/buckets", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_bucket", "Update a Planner bucket name or order. Requires @odata.etag from get_bucket.",
            schema: s => s
                .String("bucketId", "The ID of the bucket", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true)
                .String("name", "New name for the bucket")
                .String("orderHint", "New order hint"),
            handler: async (args, ct) =>
            {
                var bucketId = RequireArgument(args, "bucketId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                if (args["name"] != null) body["name"] = args["name"].ToString();
                if (args["orderHint"] != null) body["orderHint"] = args["orderHint"].ToString();
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/buckets/{bucketId}", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_bucket", "Delete a Planner bucket. Requires @odata.etag from get_bucket.",
            schema: s => s
                .String("bucketId", "The ID of the bucket", required: true)
                .String("etag", "The @odata.etag value for concurrency control", required: true),
            handler: async (args, ct) =>
            {
                var bucketId = RequireArgument(args, "bucketId");
                var etag = RequireArgument(args, "etag");
                return await SendGraphRequestAsync("DELETE", $"/v1.0/planner/buckets/{bucketId}", null, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("list_bucket_tasks", "Get all tasks in a Planner bucket.",
            schema: s => s.String("bucketId", "The ID of the bucket", required: true),
            handler: async (args, ct) =>
            {
                var bucketId = RequireArgument(args, "bucketId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/buckets/{bucketId}/tasks");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Rosters ──────────────────────────────────────────────────────

        handler.AddTool("create_roster", "Create a new Planner roster (a non-group container for plans).",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await SendGraphRequestAsync("POST", "/beta/planner/rosters", new JObject());
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_roster", "Get a specific Planner roster by ID.",
            schema: s => s.String("rosterId", "The ID of the roster", required: true),
            handler: async (args, ct) =>
            {
                var rosterId = RequireArgument(args, "rosterId");
                return await SendGraphRequestAsync("GET", $"/beta/planner/rosters/{rosterId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("delete_roster", "Delete a Planner roster.",
            schema: s => s.String("rosterId", "The ID of the roster", required: true),
            handler: async (args, ct) =>
            {
                var rosterId = RequireArgument(args, "rosterId");
                return await SendGraphRequestAsync("DELETE", $"/beta/planner/rosters/{rosterId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("list_roster_members", "Get all members of a Planner roster.",
            schema: s => s.String("rosterId", "The ID of the roster", required: true),
            handler: async (args, ct) =>
            {
                var rosterId = RequireArgument(args, "rosterId");
                return await SendGraphRequestAsync("GET", $"/beta/planner/rosters/{rosterId}/members");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("add_roster_member", "Add a member to a Planner roster.",
            schema: s => s
                .String("rosterId", "The ID of the roster", required: true)
                .String("userId", "The ID of the user to add", required: true)
                .String("tenantId", "The tenant ID of the user (optional)"),
            handler: async (args, ct) =>
            {
                var rosterId = RequireArgument(args, "rosterId");
                var body = new JObject { ["userId"] = RequireArgument(args, "userId") };
                if (args["tenantId"] != null) body["tenantId"] = args["tenantId"].ToString();
                return await SendGraphRequestAsync("POST", $"/beta/planner/rosters/{rosterId}/members", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("remove_roster_member", "Remove a member from a Planner roster.",
            schema: s => s
                .String("rosterId", "The ID of the roster", required: true)
                .String("memberId", "The ID of the roster member", required: true),
            handler: async (args, ct) =>
            {
                var rosterId = RequireArgument(args, "rosterId");
                var memberId = RequireArgument(args, "memberId");
                return await SendGraphRequestAsync("DELETE", $"/beta/planner/rosters/{rosterId}/members/{memberId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("list_roster_plans", "Get all plans in a Planner roster.",
            schema: s => s.String("rosterId", "The ID of the roster", required: true),
            handler: async (args, ct) =>
            {
                var rosterId = RequireArgument(args, "rosterId");
                return await SendGraphRequestAsync("GET", $"/beta/planner/rosters/{rosterId}/plans");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Business Scenarios (Preview) ─────────────────────────────────

        handler.AddTool("list_business_scenarios", "Get all business scenarios in the tenant (preview).",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await SendGraphRequestAsync("GET", "/beta/solutions/businessScenarios");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_business_scenario", "Get a specific business scenario by ID (preview).",
            schema: s => s.String("scenarioId", "The ID of the business scenario", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                return await SendGraphRequestAsync("GET", $"/beta/solutions/businessScenarios/{scenarioId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_business_scenario", "Create a new business scenario for Planner integration (preview).",
            schema: s => s
                .String("displayName", "Display name of the business scenario", required: true)
                .String("uniqueName", "Unique name in reverse DNS format (e.g., com.contoso.apps.myScenario)", required: true)
                .Array("ownerAppIds", "App IDs that own this scenario", new JObject { ["type"] = "string" }),
            handler: async (args, ct) =>
            {
                var body = new JObject
                {
                    ["displayName"] = RequireArgument(args, "displayName"),
                    ["uniqueName"] = RequireArgument(args, "uniqueName")
                };
                if (args["ownerAppIds"] != null) body["ownerAppIds"] = args["ownerAppIds"];
                return await SendGraphRequestAsync("POST", "/beta/solutions/businessScenarios", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_business_scenario", "Update a business scenario (preview).",
            schema: s => s
                .String("scenarioId", "The ID of the business scenario", required: true)
                .String("displayName", "New display name")
                .Array("ownerAppIds", "Updated owner app IDs", new JObject { ["type"] = "string" }),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                var body = new JObject();
                if (args["displayName"] != null) body["displayName"] = args["displayName"].ToString();
                if (args["ownerAppIds"] != null) body["ownerAppIds"] = args["ownerAppIds"];
                return await SendGraphRequestAsync("PATCH", $"/beta/solutions/businessScenarios/{scenarioId}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_business_scenario", "Delete a business scenario and all its associated data (preview).",
            schema: s => s.String("scenarioId", "The ID of the business scenario", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                return await SendGraphRequestAsync("DELETE", $"/beta/solutions/businessScenarios/{scenarioId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        handler.AddTool("get_scenario_planner", "Get the Planner configuration for a business scenario (preview).",
            schema: s => s.String("scenarioId", "The ID of the business scenario", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                return await SendGraphRequestAsync("GET", $"/beta/solutions/businessScenarios/{scenarioId}/planner");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_scenario_plan", "Get the plan reference for a business scenario targeting a specific group (preview).",
            schema: s => s
                .String("scenarioId", "The ID of the business scenario", required: true)
                .String("groupId", "The ID of the target group", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                var groupId = RequireArgument(args, "groupId");
                var body = new JObject
                {
                    ["target"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.businessScenarioGroupTarget",
                        ["taskTargetKind"] = "group",
                        ["groupId"] = groupId
                    }
                };
                return await SendGraphRequestAsync("POST", $"/beta/solutions/businessScenarios/{scenarioId}/planner/getPlan", body);
            },
            annotations: a => { a["readOnlyHint"] = true; });

        handler.AddTool("list_scenario_tasks", "Get all tasks for a business scenario (preview).",
            schema: s => s.String("scenarioId", "The ID of the business scenario", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                return await SendGraphRequestAsync("GET", $"/beta/solutions/businessScenarios/{scenarioId}/planner/tasks");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_scenario_task", "Create a new task for a business scenario (preview).",
            schema: s => s
                .String("scenarioId", "The ID of the business scenario", required: true)
                .String("title", "Title of the task", required: true)
                .String("groupId", "The ID of the target group", required: true)
                .String("externalObjectId", "Unique external ID for the task within the tenant", required: true)
                .String("externalContextId", "Context ID to group tasks")
                .String("externalBucketId", "External bucket ID for plan configuration buckets")
                .Integer("percentComplete", "Percentage complete (0-100)")
                .Integer("priority", "Priority (0-10, lower is higher)")
                .String("dueDateTime", "Due date in ISO 8601 format"),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                var body = new JObject
                {
                    ["title"] = RequireArgument(args, "title"),
                    ["target"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.businessScenarioGroupTarget",
                        ["taskTargetKind"] = "group",
                        ["groupId"] = RequireArgument(args, "groupId")
                    },
                    ["businessScenarioProperties"] = new JObject
                    {
                        ["externalObjectId"] = RequireArgument(args, "externalObjectId")
                    }
                };
                var scenarioProps = (JObject)body["businessScenarioProperties"];
                if (args["externalContextId"] != null) scenarioProps["externalContextId"] = args["externalContextId"].ToString();
                if (args["externalBucketId"] != null) scenarioProps["externalBucketId"] = args["externalBucketId"].ToString();
                if (args["percentComplete"] != null) body["percentComplete"] = (int)args["percentComplete"];
                if (args["priority"] != null) body["priority"] = (int)args["priority"];
                if (args["dueDateTime"] != null) body["dueDateTime"] = args["dueDateTime"].ToString();
                return await SendGraphRequestAsync("POST", $"/beta/solutions/businessScenarios/{scenarioId}/planner/tasks", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_plan_configuration", "Get the plan configuration for a business scenario (preview).",
            schema: s => s.String("scenarioId", "The ID of the business scenario", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                return await SendGraphRequestAsync("GET", $"/beta/solutions/businessScenarios/{scenarioId}/planner/planConfiguration");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_task_configuration", "Get the task configuration for a business scenario (preview).",
            schema: s => s.String("scenarioId", "The ID of the business scenario", required: true),
            handler: async (args, ct) =>
            {
                var scenarioId = RequireArgument(args, "scenarioId");
                return await SendGraphRequestAsync("GET", $"/beta/solutions/businessScenarios/{scenarioId}/planner/taskConfiguration");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Plan Archive / Unarchive ────────────────────────────────────────

        handler.AddTool("archive_plan", "Archive a Planner plan. Archived plans become read-only.",
            schema: s =>
            {
                s.String("planId", "The ID of the plan to archive", required: true);
                s.String("etag", "The @odata.etag value from the plan", required: true);
                s.String("justification", "Reason for archiving the plan");
            },
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                var justification = GetArgument(args, "justification");
                if (justification != null) body["justification"] = justification;
                return await SendGraphRequestAsync("POST", $"/beta/planner/plans/{planId}/archive", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("unarchive_plan", "Unarchive a previously archived Planner plan.",
            schema: s =>
            {
                s.String("planId", "The ID of the plan to unarchive", required: true);
                s.String("etag", "The @odata.etag value from the plan", required: true);
                s.String("justification", "Reason for unarchiving the plan");
            },
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                var justification = GetArgument(args, "justification");
                if (justification != null) body["justification"] = justification;
                return await SendGraphRequestAsync("POST", $"/beta/planner/plans/{planId}/unarchive", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("move_plan_to_container", "Move a Planner plan from one container to another (e.g., user to group).",
            schema: s =>
            {
                s.String("planId", "The ID of the plan to move", required: true);
                s.String("containerId", "The ID of the target container (e.g., group ID)", required: true);
                s.String("containerType", "The type of the target container (e.g., group, roster, user)", required: true);
            },
            handler: async (args, ct) =>
            {
                var planId = RequireArgument(args, "planId");
                var containerId = RequireArgument(args, "containerId");
                var containerType = RequireArgument(args, "containerType");
                var body = new JObject
                {
                    ["container"] = new JObject
                    {
                        ["containerId"] = containerId,
                        ["type"] = containerType
                    }
                };
                return await SendGraphRequestAsync("POST", $"/beta/planner/plans/{planId}/moveToContainer", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── User Planner Views ──────────────────────────────────────────────

        handler.AddTool("list_my_plans", "Get all Planner plans for the current user.",
            schema: s =>
            {
                s.Number("top", "Number of plans to return");
                s.String("filter", "OData filter expression");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "$top:top", "$filter:filter");
                return await SendGraphRequestAsync("GET", $"/v1.0/me/planner/plans{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_favorite_plans", "Get all Planner plans marked as favorites by the current user.",
            schema: s =>
            {
                s.Number("top", "Number of plans to return");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "$top:top");
                return await SendGraphRequestAsync("GET", $"/beta/me/planner/favoritePlans{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_recent_plans", "Get all Planner plans recently viewed by the current user.",
            schema: s =>
            {
                s.Number("top", "Number of plans to return");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "$top:top");
                return await SendGraphRequestAsync("GET", $"/beta/me/planner/recentPlans{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_my_day_tasks", "Get all Planner tasks in the current user's My Day view.",
            schema: s =>
            {
                s.Number("top", "Number of tasks to return");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "$top:top");
                return await SendGraphRequestAsync("GET", $"/beta/me/planner/myDayTasks{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Task Board Formats ──────────────────────────────────────────────

        handler.AddTool("get_assigned_to_board_format", "Get the Assigned To task board format for a task.",
            schema: s => s.String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/tasks/{taskId}/assignedToTaskBoardFormat");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_assigned_to_board_format", "Update the Assigned To task board format for a task.",
            schema: s =>
            {
                s.String("taskId", "The ID of the task", required: true);
                s.String("etag", "The @odata.etag value", required: true);
                s.String("unassignedOrderHint", "Order hint for the unassigned column");
                s.String("orderHintsByAssignee", "JSON object of order hints keyed by assignee user ID");
            },
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                if (args["unassignedOrderHint"] != null) body["unassignedOrderHint"] = args["unassignedOrderHint"].ToString();
                if (args["orderHintsByAssignee"] != null) body["orderHintsByAssignee"] = JObject.Parse(args["orderHintsByAssignee"].ToString());
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/tasks/{taskId}/assignedToTaskBoardFormat", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_progress_board_format", "Get the Progress task board format for a task.",
            schema: s => s.String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/tasks/{taskId}/progressTaskBoardFormat");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_progress_board_format", "Update the Progress task board format for a task.",
            schema: s =>
            {
                s.String("taskId", "The ID of the task", required: true);
                s.String("etag", "The @odata.etag value", required: true);
                s.String("orderHint", "Order hint for the Progress view");
            },
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                if (args["orderHint"] != null) body["orderHint"] = args["orderHint"].ToString();
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/tasks/{taskId}/progressTaskBoardFormat", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("get_bucket_board_format", "Get the Bucket task board format for a task.",
            schema: s => s.String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                return await SendGraphRequestAsync("GET", $"/v1.0/planner/tasks/{taskId}/bucketTaskBoardFormat");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_bucket_board_format", "Update the Bucket task board format for a task.",
            schema: s =>
            {
                s.String("taskId", "The ID of the task", required: true);
                s.String("etag", "The @odata.etag value", required: true);
                s.String("orderHint", "Order hint for the Bucket view");
            },
            handler: async (args, ct) =>
            {
                var taskId = RequireArgument(args, "taskId");
                var etag = RequireArgument(args, "etag");
                var body = new JObject();
                if (args["orderHint"] != null) body["orderHint"] = args["orderHint"].ToString();
                return await SendGraphRequestAsync("PATCH", $"/v1.0/planner/tasks/{taskId}/bucketTaskBoardFormat", body, etag);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── Delta Query ─────────────────────────────────────────────────────

        handler.AddTool("get_task_delta", "Get newly created, updated, or deleted tasks using delta query. Use the deltaLink from the response for subsequent calls.",
            schema: s =>
            {
                s.String("deltaToken", "Delta token from a previous response for incremental changes");
            },
            handler: async (args, ct) =>
            {
                var deltaToken = GetArgument(args, "deltaToken");
                var path = "/beta/planner/tasks/delta";
                if (deltaToken != null) path += $"?$deltatoken={Uri.EscapeDataString(deltaToken)}";
                return await SendGraphRequestAsync("GET", path);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }


    // ── Graph API Helper ─────────────────────────────────────────────────

    private async Task<JObject> SendGraphRequestAsync(string method, string path, JObject body = null, string etag = null)
    {
        var url = GraphBaseUrl + path;
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        // Forward the auth header from the connector
        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (etag != null)
            request.Headers.TryAddWithoutValidation("If-Match", etag);

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var errorMsg = $"Graph API returned {statusCode}";
            try
            {
                var errorObj = JObject.Parse(content);
                var graphError = errorObj["error"]?["message"]?.ToString();
                if (!string.IsNullOrEmpty(graphError))
                    errorMsg = $"Graph API {statusCode}: {graphError}";
            }
            catch { }
            throw new McpException(McpErrorCode.InternalError, errorMsg);
        }

        if (string.IsNullOrWhiteSpace(content) || response.StatusCode == HttpStatusCode.NoContent)
            return new JObject { ["status"] = "success" };

        return JObject.Parse(content);
    }


    // ── Utility Helpers ──────────────────────────────────────────────────

    private static string RequireArgument(JObject args, string name)
    {
        var val = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val))
            throw new ArgumentException($"'{name}' is required");
        return val;
    }

    private static string GetArgument(JObject args, string name)
    {
        return args[name]?.ToString();
    }

    private static string BuildQueryString(JObject args, params string[] paramNames)
    {
        var parts = new List<string>();
        foreach (var p in paramNames)
        {
            var val = args[p]?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
            {
                var key = p == "filter" ? "$filter" : p == "top" ? "$top" : p;
                parts.Add($"{key}={Uri.EscapeDataString(val)}");
            }
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }


    // ── Application Insights Logging ─────────────────────────────────────

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

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
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
// ║  SECTION 2: MCP FRAMEWORK                                                    ║
// ║                                                                              ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power         ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section    ║
// ║  becomes a using statement instead of inline code.                           ║
// ║                                                                              ║
// ║  Spec coverage: MCP 2025-11-25                                               ║
// ║  Handles: initialize, ping, tools/*, resources/*, prompts/*,                 ║
// ║           completion/complete, logging/setLevel, all notifications           ║
// ║                                                                              ║
// ║  Stateless limitations (Power Platform cannot send async notifications):     ║
// ║   - Tasks (experimental, requires persistent state between requests)         ║
// ║   - Server→client requests (sampling, elicitation, roots/list)               ║
// ║   - Server→client notifications (progress, logging/message, list_changed)    ║
// ║                                                                              ║
// ║  Do not modify unless extending the framework itself.                        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported in initialize response.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

/// <summary>Capabilities declared during initialization.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>Standard JSON-RPC 2.0 error codes used by MCP.</summary>
public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>Fluent builder for JSON Schema objects used in tool inputSchema.</summary>
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

// ── Internal Tool Registration ───────────────────────────────────────────────

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

// ── McpRequestHandler ────────────────────────────────────────────────────────

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    // ── Tool Registration ────────────────────────────────────────────────

    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchema = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObj = null;
        if (annotations != null)
        {
            annotationsObj = new JObject();
            annotations(annotationsObj);
        }

        JObject outputSchemaObj = null;
        if (outputSchema != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchema(outBuilder);
            outputSchemaObj = outBuilder.Build();
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchemaObj,
            Annotations = annotationsObj,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping":
                    return SerializeSuccess(id, new JObject());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "resources/read":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "prompts/get":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                case "logging/setLevel":
                    return SerializeSuccess(id, new JObject());

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

    // ── Protocol Handlers ────────────────────────────────────────────────

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString()
            ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources)
            capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = serverInfo
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = clientProtocolVersion
        });

        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
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
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };
                if (jobj["structuredContent"] is JObject structured)
                    callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

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
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    // ── Content Helpers ──────────────────────────────────────────────────

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

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data))
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

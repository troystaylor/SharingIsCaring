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
// ║  Microsoft To Do MCP — all Graph To Do API endpoints as MCP tools.         ║
// ║  Tool registration uses the fluent AddTool API.                            ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    // ── Server Configuration ─────────────────────────────────────────────

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "microsoft-todo-mcp",
            Version = "1.0.0",
            Title = "Microsoft To Do MCP",
            Description = "Manage Microsoft To Do task lists, tasks, checklist items, linked resources, attachments, delta sync, and categories via MCP."
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
        Instructions = "Use this server to manage Microsoft To Do tasks. You can create, read, update, and delete task lists, tasks, subtasks (checklist items), linked resources, and file attachments. Delta queries let you sync incremental changes."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Pass through non-MCP operations to the connector host
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
        // ── Task Lists ───────────────────────────────────────────────────

        handler.AddTool("list_task_lists", "Get all To Do task lists for the current user.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallGraphAsync("GET", "/me/todo/lists");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_task_list", "Get a specific task list by ID.",
            schema: s => s.String("listId", "The ID of the task list", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_task_list", "Create a new task list.",
            schema: s => s.String("displayName", "The name of the task list", required: true),
            handler: async (args, ct) =>
            {
                var displayName = RequireArgument(args, "displayName");
                return await CallGraphAsync("POST", "/me/todo/lists",
                    new JObject { ["displayName"] = displayName });
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_task_list", "Update the name of a task list.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("displayName", "The new name of the task list", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var displayName = RequireArgument(args, "displayName");
                return await CallGraphAsync("PATCH", $"/me/todo/lists/{listId}",
                    new JObject { ["displayName"] = displayName });
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_task_list", "Delete a task list.",
            schema: s => s.String("listId", "The ID of the task list", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                return await CallGraphAsync("DELETE", $"/me/todo/lists/{listId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ── Tasks ────────────────────────────────────────────────────────

        handler.AddTool("list_tasks", "Get all tasks in a specific task list.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("filter", "OData filter expression (e.g., status eq 'notStarted')")
                .Integer("top", "Number of tasks to return"),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var query = BuildQueryString(args, "filter", "top");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks{query}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_task", "Get a specific task by ID.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_task", "Create a new task in a task list.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("title", "The title of the task", required: true)
                .String("body", "The body/notes content of the task")
                .String("importance", "The importance: low, normal, or high", enumValues: new[] { "low", "normal", "high" })
                .String("status", "The status: notStarted, inProgress, completed, waitingOnOthers, or deferred")
                .String("dueDateTime", "Due date in ISO 8601 format (e.g., 2024-12-31T23:59:00)")
                .String("dueDateTimeZone", "Time zone for due date (e.g., UTC, Pacific Standard Time). Defaults to UTC.")
                .String("startDateTime", "Start date in ISO 8601 format")
                .String("startDateTimeZone", "Time zone for start date. Defaults to UTC.")
                .Boolean("isReminderOn", "Whether to set a reminder")
                .String("reminderDateTime", "Reminder date in ISO 8601 format")
                .String("reminderDateTimeZone", "Time zone for reminder date. Defaults to UTC.")
                .Array("categories", "Categories for the task", new JObject { ["type"] = "string" }),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                return await CallGraphAsync("POST", $"/me/todo/lists/{listId}/tasks", BuildTaskBody(args));
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_task", "Update an existing task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("title", "The title of the task")
                .String("body", "The body/notes content of the task")
                .String("importance", "The importance: low, normal, or high")
                .String("status", "The status: notStarted, inProgress, completed, waitingOnOthers, or deferred")
                .String("dueDateTime", "Due date in ISO 8601 format")
                .String("dueDateTimeZone", "Time zone for due date. Defaults to UTC.")
                .Boolean("isReminderOn", "Whether to set a reminder")
                .Array("categories", "Categories for the task", new JObject { ["type"] = "string" }),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("PATCH", $"/me/todo/lists/{listId}/tasks/{taskId}", BuildTaskBody(args));
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("complete_task", "Mark a task as completed.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("PATCH", $"/me/todo/lists/{listId}/tasks/{taskId}",
                    new JObject
                    {
                        ["status"] = "completed",
                        ["completedDateTime"] = new JObject
                        {
                            ["dateTime"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.0000000"),
                            ["timeZone"] = "UTC"
                        }
                    });
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_task", "Delete a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("DELETE", $"/me/todo/lists/{listId}/tasks/{taskId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ── Checklist Items ──────────────────────────────────────────────

        handler.AddTool("list_checklist_items", "Get all checklist items (subtasks) for a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}/checklistItems");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_checklist_item", "Get a specific checklist item by ID.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("checklistItemId", "The ID of the checklist item", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var checklistItemId = RequireArgument(args, "checklistItemId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}/checklistItems/{checklistItemId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_checklist_item", "Create a new checklist item (subtask) for a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("displayName", "The title of the checklist item", required: true)
                .Boolean("isChecked", "Whether the item is checked off"),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var body = new JObject { ["displayName"] = args["displayName"] };
                if (args["isChecked"] != null) body["isChecked"] = args["isChecked"];
                return await CallGraphAsync("POST", $"/me/todo/lists/{listId}/tasks/{taskId}/checklistItems", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_checklist_item", "Update a checklist item.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("checklistItemId", "The ID of the checklist item", required: true)
                .String("displayName", "The title of the checklist item")
                .Boolean("isChecked", "Whether the item is checked off"),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var checklistItemId = RequireArgument(args, "checklistItemId");
                var body = new JObject();
                if (args["displayName"] != null) body["displayName"] = args["displayName"];
                if (args["isChecked"] != null) body["isChecked"] = args["isChecked"];
                return await CallGraphAsync("PATCH", $"/me/todo/lists/{listId}/tasks/{taskId}/checklistItems/{checklistItemId}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_checklist_item", "Delete a checklist item.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("checklistItemId", "The ID of the checklist item", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var checklistItemId = RequireArgument(args, "checklistItemId");
                return await CallGraphAsync("DELETE", $"/me/todo/lists/{listId}/tasks/{taskId}/checklistItems/{checklistItemId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ── Linked Resources ─────────────────────────────────────────────

        handler.AddTool("list_linked_resources", "Get all linked resources for a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}/linkedResources");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_linked_resource", "Get a specific linked resource by ID.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("linkedResourceId", "The ID of the linked resource", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var linkedResourceId = RequireArgument(args, "linkedResourceId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}/linkedResources/{linkedResourceId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_linked_resource", "Create a linked resource for a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("applicationName", "The app name of the source", required: true)
                .String("displayName", "The title of the linked resource", required: true)
                .String("externalId", "ID of the object on the third-party system")
                .String("webUrl", "Deep link to the linked resource"),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var body = new JObject
                {
                    ["applicationName"] = args["applicationName"],
                    ["displayName"] = args["displayName"]
                };
                if (args["externalId"] != null) body["externalId"] = args["externalId"];
                if (args["webUrl"] != null) body["webUrl"] = args["webUrl"];
                return await CallGraphAsync("POST", $"/me/todo/lists/{listId}/tasks/{taskId}/linkedResources", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("update_linked_resource", "Update a linked resource.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("linkedResourceId", "The ID of the linked resource", required: true)
                .String("applicationName", "The app name of the source")
                .String("displayName", "The title of the linked resource")
                .String("externalId", "ID of the object on the third-party system")
                .String("webUrl", "Deep link to the linked resource"),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var linkedResourceId = RequireArgument(args, "linkedResourceId");
                var body = new JObject();
                if (args["applicationName"] != null) body["applicationName"] = args["applicationName"];
                if (args["displayName"] != null) body["displayName"] = args["displayName"];
                if (args["externalId"] != null) body["externalId"] = args["externalId"];
                if (args["webUrl"] != null) body["webUrl"] = args["webUrl"];
                return await CallGraphAsync("PATCH", $"/me/todo/lists/{listId}/tasks/{taskId}/linkedResources/{linkedResourceId}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        handler.AddTool("delete_linked_resource", "Delete a linked resource.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("linkedResourceId", "The ID of the linked resource", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var linkedResourceId = RequireArgument(args, "linkedResourceId");
                return await CallGraphAsync("DELETE", $"/me/todo/lists/{listId}/tasks/{taskId}/linkedResources/{linkedResourceId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ── Attachments ──────────────────────────────────────────────────

        handler.AddTool("list_task_attachments", "Get all file attachments for a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}/attachments");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_task_attachment", "Get a specific file attachment and its content.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("attachmentId", "The ID of the attachment", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var attachmentId = RequireArgument(args, "attachmentId");
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/{taskId}/attachments/{attachmentId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_task_attachment", "Attach a small file (up to 3 MB) to a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("name", "The file name (e.g., report.pdf)", required: true)
                .String("contentType", "The MIME type (e.g., application/pdf)")
                .String("contentBytes", "The base64-encoded file contents", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var body = new JObject
                {
                    ["@odata.type"] = "#microsoft.graph.taskFileAttachment",
                    ["name"] = args["name"],
                    ["contentBytes"] = args["contentBytes"]
                };
                if (args["contentType"] != null) body["contentType"] = args["contentType"];
                return await CallGraphAsync("POST", $"/me/todo/lists/{listId}/tasks/{taskId}/attachments", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("delete_task_attachment", "Delete a file attachment from a task.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("taskId", "The ID of the task", required: true)
                .String("attachmentId", "The ID of the attachment", required: true),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var taskId = RequireArgument(args, "taskId");
                var attachmentId = RequireArgument(args, "attachmentId");
                return await CallGraphAsync("DELETE", $"/me/todo/lists/{listId}/tasks/{taskId}/attachments/{attachmentId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; });

        // ── Delta Sync ───────────────────────────────────────────────────

        handler.AddTool("get_tasks_delta", "Get tasks that have been added, deleted, or updated since the last sync.",
            schema: s => s
                .String("listId", "The ID of the task list", required: true)
                .String("deltaToken", "State token from previous delta call for incremental changes")
                .String("skipToken", "State token for pagination within a delta result set")
                .Integer("top", "Number of tasks to return per page"),
            handler: async (args, ct) =>
            {
                var listId = RequireArgument(args, "listId");
                var query = BuildDeltaQueryString(args);
                return await CallGraphAsync("GET", $"/me/todo/lists/{listId}/tasks/delta{query}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_task_lists_delta", "Get task lists that have been added, deleted, or updated since the last sync.",
            schema: s => s
                .String("deltaToken", "State token from previous delta call for incremental changes")
                .String("skipToken", "State token for pagination within a delta result set"),
            handler: async (args, ct) =>
            {
                var query = BuildDeltaQueryString(args);
                return await CallGraphAsync("GET", $"/me/todo/lists/delta{query}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Categories ───────────────────────────────────────────────────

        handler.AddTool("list_outlook_categories", "Get all the categories defined for the user. Categories can be used to tag tasks.",
            schema: s => { },
            handler: async (args, ct) =>
            {
                return await CallGraphAsync("GET", "/me/outlook/masterCategories");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── Graph API Helpers ────────────────────────────────────────────────

    private async Task<JObject> CallGraphAsync(string method, string path, JObject body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri($"{GraphBaseUrl}{path}"));

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Graph API error ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["message"] = method == "DELETE" ? "Successfully deleted." : "Success." };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private JObject BuildTaskBody(JObject args)
    {
        var task = new JObject();

        if (args["title"] != null) task["title"] = args["title"];
        if (args["importance"] != null) task["importance"] = args["importance"];
        if (args["status"] != null) task["status"] = args["status"];
        if (args["isReminderOn"] != null) task["isReminderOn"] = args["isReminderOn"];
        if (args["categories"] != null) task["categories"] = args["categories"];

        if (args["body"] != null)
        {
            task["body"] = new JObject
            {
                ["content"] = args["body"].ToString(),
                ["contentType"] = "text"
            };
        }

        if (args["dueDateTime"] != null)
        {
            task["dueDateTime"] = new JObject
            {
                ["dateTime"] = args["dueDateTime"].ToString(),
                ["timeZone"] = args["dueDateTimeZone"]?.ToString() ?? "UTC"
            };
        }

        if (args["startDateTime"] != null)
        {
            task["startDateTime"] = new JObject
            {
                ["dateTime"] = args["startDateTime"].ToString(),
                ["timeZone"] = args["startDateTimeZone"]?.ToString() ?? "UTC"
            };
        }

        if (args["reminderDateTime"] != null)
        {
            task["reminderDateTime"] = new JObject
            {
                ["dateTime"] = args["reminderDateTime"].ToString(),
                ["timeZone"] = args["reminderDateTimeZone"]?.ToString() ?? "UTC"
            };
        }

        return task;
    }

    private string BuildQueryString(JObject args, params string[] queryParams)
    {
        var parts = new List<string>();
        foreach (var param in queryParams)
        {
            var value = args[param]?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                var key = param == "filter" ? "$filter" : param == "top" ? "$top" : param;
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private string BuildDeltaQueryString(JObject args)
    {
        var parts = new List<string>();
        if (args["deltaToken"] != null)
            parts.Add($"$deltatoken={Uri.EscapeDataString(args["deltaToken"].ToString())}");
        if (args["skipToken"] != null)
            parts.Add($"$skiptoken={Uri.EscapeDataString(args["skipToken"].ToString())}");
        if (args["top"] != null)
            parts.Add($"$top={Uri.EscapeDataString(args["top"].ToString())}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    // ── Argument Helpers ─────────────────────────────────────────────────

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

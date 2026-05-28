using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class ListMyPrivateTasksTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_my_private_tasks",
        Description = "Retrieve private Planner-style tasks by reading Microsoft To Do lists/tasks (Exchange-backed private tasks).",
        Annotations = new ToolAnnotations(
            Title: "List my private tasks",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["includeCompleted"] = Prop("boolean", "Include completed tasks. Default false."),
                ["maxPerList"] = Prop("integer", "Maximum tasks per list to return. Default 200, max 500."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var includeCompleted = args["includeCompleted"]?.GetValue<bool?>() ?? false;
            var maxPerList = args["maxPerList"]?.GetValue<int?>() ?? 200;
            if (maxPerList < 1) maxPerList = 1;
            if (maxPerList > 500) maxPerList = 500;

            var listsResp = await graph.ListMyTodoListsAsync(ct);
            var lists = listsResp["value"] as JsonArray ?? new JsonArray();

            var warnings = new JsonArray();
            var byList = new JsonArray();
            var allTasks = new JsonArray();

            foreach (var listNode in lists)
            {
                var list = listNode as JsonObject;
                if (list is null) continue;

                var listId = list["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(listId)) continue;

                try
                {
                    var tasksResp = await graph.ListMyTodoTasksAsync(listId, ct);
                    var items = tasksResp["value"] as JsonArray ?? new JsonArray();

                    var filtered = new JsonArray();
                    foreach (var taskNode in items)
                    {
                        var task = taskNode as JsonObject;
                        if (task is null) continue;

                        var status = task["status"]?.GetValue<string>() ?? string.Empty;
                        var isCompleted = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
                        if (!includeCompleted && isCompleted) continue;

                        if (filtered.Count >= maxPerList) break;

                        task["listMeta"] = new JsonObject
                        {
                            ["id"] = listId,
                            ["displayName"] = list["displayName"]?.GetValue<string>() ?? string.Empty,
                            ["wellknownListName"] = list["wellknownListName"]?.GetValue<string>() ?? string.Empty,
                            ["isShared"] = list["isShared"]?.GetValue<bool?>() ?? false,
                        };

                        filtered.Add(task.DeepClone());
                        allTasks.Add(task.DeepClone());
                    }

                    byList.Add(new JsonObject
                    {
                        ["id"] = listId,
                        ["displayName"] = list["displayName"]?.GetValue<string>() ?? string.Empty,
                        ["wellknownListName"] = list["wellknownListName"]?.GetValue<string>() ?? string.Empty,
                        ["isShared"] = list["isShared"]?.GetValue<bool?>() ?? false,
                        ["taskCount"] = filtered.Count,
                        ["tasks"] = filtered,
                    });
                }
                catch (PlannerApiException ex)
                {
                    warnings.Add(new JsonObject
                    {
                        ["listId"] = listId,
                        ["statusCode"] = ex.StatusCode,
                        ["message"] = "Failed to read tasks for this To Do list.",
                    });
                }
            }

            var result = new JsonObject
            {
                ["source"] = "me/todo/lists + me/todo/lists/{list-id}/tasks",
                ["includeCompleted"] = includeCompleted,
                ["maxPerList"] = maxPerList,
                ["listCount"] = lists.Count,
                ["taskCount"] = allTasks.Count,
                ["lists"] = byList,
                ["tasks"] = allTasks,
                ["warnings"] = warnings,
                ["note"] = "This tool reads Exchange-backed To Do tasks that power private tasks in Planner/Teams.",
            };

            return ContentResult(result);
        },
    };
}

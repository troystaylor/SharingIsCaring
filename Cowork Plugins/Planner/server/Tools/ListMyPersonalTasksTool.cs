using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class ListMyPersonalTasksTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_my_personal_tasks",
        Description = "List tasks from the signed-in user's non-group Planner plans (best effort for personal/private plans).",
        Annotations = new ToolAnnotations(
            Title: "List my personal/private tasks",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(new JsonObject()),
        Invoke = async (sp, _, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();

            var plansResp = await graph.ListMyPlansAsync(ct);
            var planItems = plansResp["value"] as JsonArray ?? new JsonArray();

            var personalPlans = new JsonArray();
            foreach (var node in planItems)
            {
                var plan = node as JsonObject;
                if (plan is null) continue;

                var containerType = plan["container"]?["containerType"]?.GetValue<string>();
                if (!string.Equals(containerType, "group", StringComparison.OrdinalIgnoreCase))
                {
                    personalPlans.Add(plan.DeepClone());
                }
            }

            var tasks = new JsonArray();
            var warnings = new JsonArray();

            foreach (var node in personalPlans)
            {
                var plan = node as JsonObject;
                if (plan is null) continue;

                var planId = plan["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(planId)) continue;

                try
                {
                    var tasksResp = await graph.ListPlanTasksAsync(planId, ct);
                    var taskItems = tasksResp["value"] as JsonArray ?? new JsonArray();

                    foreach (var taskNode in taskItems)
                    {
                        var task = taskNode as JsonObject;
                        if (task is null) continue;

                        task["planMeta"] = new JsonObject
                        {
                            ["id"] = planId,
                            ["title"] = plan["title"]?.GetValue<string>() ?? string.Empty,
                            ["containerType"] = plan["container"]?["containerType"]?.GetValue<string>() ?? "unknown",
                        };

                        tasks.Add(task.DeepClone());
                    }
                }
                catch (PlannerApiException ex)
                {
                    warnings.Add(new JsonObject
                    {
                        ["planId"] = planId,
                        ["statusCode"] = ex.StatusCode,
                        ["message"] = "Failed to read tasks for this plan.",
                    });
                }
            }

            var result = new JsonObject
            {
                ["source"] = "me/planner/plans + planner/plans/{plan-id}/tasks",
                ["planCount"] = planItems.Count,
                ["personalPlanCount"] = personalPlans.Count,
                ["taskCount"] = tasks.Count,
                ["plans"] = personalPlans,
                ["tasks"] = tasks,
                ["warnings"] = warnings,
                ["note"] = "Graph v1.0 Planner APIs may not expose all premium or unsupported personal plan/task types.",
            };

            return ContentResult(result);
        },
    };
}

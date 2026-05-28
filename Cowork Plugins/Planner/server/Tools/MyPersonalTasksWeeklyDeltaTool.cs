using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class MyPersonalTasksWeeklyDeltaTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "my_personal_tasks_weekly_delta",
        Description = "Summarize recent personal/private Planner task changes across the caller's non-group plans.",
        Annotations = new ToolAnnotations(
            Title: "My personal/private weekly task delta",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["daysBack"] = Prop("integer", "How many days to look back. Default is 7."),
                ["includeTaskSample"] = Prop("boolean", "Include a sample list of changed tasks. Default true."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();

            var daysBack = args["daysBack"]?.GetValue<int?>() ?? 7;
            if (daysBack <= 0) daysBack = 7;
            if (daysBack > 90) daysBack = 90;

            var includeTaskSample = args["includeTaskSample"]?.GetValue<bool?>() ?? true;

            var now = DateTimeOffset.UtcNow;
            var since = now.AddDays(-daysBack);
            var soon = now.AddDays(7);

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

            var warnings = new JsonArray();
            var perPlan = new JsonArray();
            var changedSample = new JsonArray();

            var totalTasks = 0;
            var changedTasks = 0;
            var newTasks = 0;
            var completedTasks = 0;
            var overdueOpen = 0;
            var dueSoonOpen = 0;

            static DateTimeOffset? ParseDate(JsonNode? node)
            {
                var text = node?.GetValue<string>();
                return DateTimeOffset.TryParse(text, out var dt) ? dt : null;
            }

            foreach (var planNode in personalPlans)
            {
                var plan = planNode as JsonObject;
                if (plan is null) continue;

                var planId = plan["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(planId)) continue;

                var planTitle = plan["title"]?.GetValue<string>() ?? string.Empty;
                var planTotal = 0;
                var planChanged = 0;
                var planNew = 0;
                var planCompleted = 0;
                var planOverdueOpen = 0;
                var planDueSoonOpen = 0;

                try
                {
                    var tasksResp = await graph.ListPlanTasksAsync(planId, ct);
                    var taskItems = tasksResp["value"] as JsonArray ?? new JsonArray();

                    foreach (var taskNode in taskItems)
                    {
                        var task = taskNode as JsonObject;
                        if (task is null) continue;

                        planTotal++;
                        totalTasks++;

                        var created = ParseDate(task["createdDateTime"]);
                        var modified = ParseDate(task["lastModifiedDateTime"]);
                        var completed = ParseDate(task["completedDateTime"]);
                        var due = ParseDate(task["dueDateTime"]);
                        var percentComplete = task["percentComplete"]?.GetValue<int?>() ?? 0;
                        var isDone = percentComplete >= 100 || completed is not null;

                        var isChanged = modified is not null && modified.Value >= since;
                        var isNew = created is not null && created.Value >= since;
                        var isCompletedRecently = completed is not null && completed.Value >= since;

                        if (isChanged)
                        {
                            planChanged++;
                            changedTasks++;

                            if (includeTaskSample && changedSample.Count < 30)
                            {
                                changedSample.Add(new JsonObject
                                {
                                    ["id"] = task["id"]?.DeepClone(),
                                    ["title"] = task["title"]?.DeepClone(),
                                    ["percentComplete"] = percentComplete,
                                    ["dueDateTime"] = task["dueDateTime"]?.DeepClone(),
                                    ["lastModifiedDateTime"] = task["lastModifiedDateTime"]?.DeepClone(),
                                    ["plan"] = new JsonObject
                                    {
                                        ["id"] = planId,
                                        ["title"] = planTitle,
                                    },
                                });
                            }
                        }

                        if (isNew)
                        {
                            planNew++;
                            newTasks++;
                        }

                        if (isCompletedRecently)
                        {
                            planCompleted++;
                            completedTasks++;
                        }

                        if (!isDone && due is not null)
                        {
                            if (due.Value < now)
                            {
                                planOverdueOpen++;
                                overdueOpen++;
                            }
                            else if (due.Value <= soon)
                            {
                                planDueSoonOpen++;
                                dueSoonOpen++;
                            }
                        }
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

                perPlan.Add(new JsonObject
                {
                    ["planId"] = planId,
                    ["planTitle"] = planTitle,
                    ["tasks"] = planTotal,
                    ["changedInWindow"] = planChanged,
                    ["newInWindow"] = planNew,
                    ["completedInWindow"] = planCompleted,
                    ["overdueOpen"] = planOverdueOpen,
                    ["dueSoon7DaysOpen"] = planDueSoonOpen,
                });
            }

            var result = new JsonObject
            {
                ["window"] = new JsonObject
                {
                    ["daysBack"] = daysBack,
                    ["sinceUtc"] = since.ToString("O"),
                    ["untilUtc"] = now.ToString("O"),
                },
                ["source"] = "me/planner/plans + planner/plans/{plan-id}/tasks",
                ["planCount"] = planItems.Count,
                ["personalPlanCount"] = personalPlans.Count,
                ["summary"] = new JsonObject
                {
                    ["tasks"] = totalTasks,
                    ["changedInWindow"] = changedTasks,
                    ["newInWindow"] = newTasks,
                    ["completedInWindow"] = completedTasks,
                    ["overdueOpen"] = overdueOpen,
                    ["dueSoon7DaysOpen"] = dueSoonOpen,
                },
                ["byPlan"] = perPlan,
                ["changedTaskSample"] = includeTaskSample ? changedSample : new JsonArray(),
                ["warnings"] = warnings,
                ["note"] = "Best effort: Graph v1.0 Planner APIs may not expose all premium or unsupported personal plan/task types.",
            };

            return ContentResult(result);
        },
    };
}
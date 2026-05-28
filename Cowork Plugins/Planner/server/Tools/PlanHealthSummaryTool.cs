using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class PlanHealthSummaryTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "plan_health_summary",
        Description = "Build a compact plan health summary from tasks and buckets for a plan id.",
        Annotations = new ToolAnnotations(
            Title: "Planner plan health summary",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["planId"] = Prop("string", "Planner plan id."),
            },
            required: "planId"),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var planId = RequireString(args, "planId");

            var tasks = await graph.ListPlanTasksAsync(planId, ct);
            var buckets = await graph.ListPlanBucketsAsync(planId, ct);

            var taskItems = tasks["value"] as JsonArray ?? new JsonArray();
            var total = taskItems.Count;
            var completed = 0;
            var overdue = 0;
            var dueSoon = 0;

            var now = DateTimeOffset.UtcNow;
            var soon = now.AddDays(7);

            foreach (var node in taskItems)
            {
                var t = node as JsonObject;
                if (t is null) continue;

                var percentComplete = t["percentComplete"]?.GetValue<int?>() ?? 0;
                if (percentComplete >= 100) completed++;

                var dueText = t["dueDateTime"]?.GetValue<string>();
                if (DateTimeOffset.TryParse(dueText, out var due))
                {
                    if (percentComplete < 100 && due < now) overdue++;
                    if (percentComplete < 100 && due >= now && due <= soon) dueSoon++;
                }
            }

            var summary = new JsonObject
            {
                ["planId"] = planId,
                ["totals"] = new JsonObject
                {
                    ["tasks"] = total,
                    ["completed"] = completed,
                    ["open"] = Math.Max(0, total - completed),
                    ["overdue"] = overdue,
                    ["dueSoon7Days"] = dueSoon,
                    ["completionPercent"] = total == 0 ? 0 : Math.Round((double)completed / total * 100, 1),
                },
                ["buckets"] = buckets["value"]?.DeepClone() ?? new JsonArray(),
                ["taskSample"] = new JsonArray(taskItems.Take(10).Select(x => x?.DeepClone()).ToArray()),
            };

            return ContentResult(summary);
        },
    };
}

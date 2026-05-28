using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class ListPlanBucketsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_plan_buckets",
        Description = "List buckets in a Planner plan.",
        Annotations = new ToolAnnotations(
            Title: "List plan buckets",
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
            var resp = await graph.ListPlanBucketsAsync(planId, ct);
            return ContentResult(resp);
        },
    };
}

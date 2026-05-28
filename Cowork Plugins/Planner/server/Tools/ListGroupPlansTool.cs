using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class ListGroupPlansTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_group_plans",
        Description = "List Microsoft Planner plans for a Microsoft 365 group.",
        Annotations = new ToolAnnotations(
            Title: "List group plans",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["groupId"] = Prop("string", "Microsoft 365 group id."),
            },
            required: "groupId"),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var groupId = RequireString(args, "groupId");
            var resp = await graph.ListGroupPlansAsync(groupId, ct);
            return ContentResult(resp);
        },
    };
}

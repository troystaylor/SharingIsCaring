using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class ListMyTasksTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_my_tasks",
        Description = "List tasks assigned to the signed-in user from Planner plans only (not Exchange-backed private To Do tasks).",
        Annotations = new ToolAnnotations(
            Title: "List my tasks",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(new JsonObject()),
        Invoke = async (sp, _, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var resp = await graph.ListMyTasksAsync(ct);
            return ContentResult(resp);
        },
    };
}

using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class GetTaskDetailsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_task_details",
        Description = "Get Planner task details for a task id.",
        Annotations = new ToolAnnotations(
            Title: "Get Planner task details",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["taskId"] = Prop("string", "Planner task id."),
            },
            required: "taskId"),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var taskId = RequireString(args, "taskId");
            var resp = await graph.GetTaskDetailsAsync(taskId, ct);
            return ContentResult(resp);
        },
    };
}

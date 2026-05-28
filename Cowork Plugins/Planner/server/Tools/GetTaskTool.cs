using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class GetTaskTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_task",
        Description = "Get a Planner task by id.",
        Annotations = new ToolAnnotations(
            Title: "Get Planner task",
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
            var resp = await graph.GetTaskAsync(taskId, ct);
            return ContentResult(resp);
        },
    };
}

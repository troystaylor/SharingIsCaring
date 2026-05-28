using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class UpdateTaskDetailsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "update_task_details",
        Description = "Update Planner task details fields. Requires latest etag for optimistic concurrency.",
        Annotations = new ToolAnnotations(
            Title: "Update Planner task details",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["taskId"] = Prop("string", "Planner task id."),
                ["etag"] = Prop("string", "Latest task details etag from Graph, used as If-Match."),
                ["changes"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Task details properties to patch.",
                },
            },
            "taskId", "etag", "changes"),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var taskId = RequireString(args, "taskId");
            var etag = RequireString(args, "etag");
            var changes = OptObject(args, "changes") ?? throw new ArgumentException("missing required parameter: changes");

            if (changes.Count == 0)
            {
                throw new ArgumentException("changes must include at least one field");
            }

            var resp = await graph.PatchTaskDetailsAsync(taskId, etag, changes, ct);
            return ContentResult(resp);
        },
    };
}

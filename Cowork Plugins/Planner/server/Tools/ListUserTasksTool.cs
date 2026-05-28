using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class ListUserTasksTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_user_tasks",
        Description = "List tasks assigned to a specified user.",
        Annotations = new ToolAnnotations(
            Title: "List user tasks",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["userId"] = Prop("string", "User id to query tasks for."),
            },
            required: "userId"),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var userId = RequireString(args, "userId");
            var resp = await graph.ListUserTasksAsync(userId, ct);
            return ContentResult(resp);
        },
    };
}

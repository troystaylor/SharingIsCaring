using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PlannerCoworkMcp.Planner;
using static PlannerCoworkMcp.Tools.ToolHelpers;

namespace PlannerCoworkMcp.Tools;

internal static class CreateTaskTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "create_task",
        Description = "Create a new Planner task in a plan.",
        Annotations = new ToolAnnotations(
            Title: "Create Planner task",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["planId"] = Prop("string", "Planner plan id."),
                ["title"] = Prop("string", "Task title."),
                ["bucketId"] = Prop("string", "Optional Planner bucket id."),
                ["dueDateTime"] = Prop("string", "Optional due date in ISO 8601 format."),
                ["assignments"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Optional assignments object keyed by user id.",
                },
            },
            "planId", "title"),
        Invoke = async (sp, args, ct) =>
        {
            var graph = sp.GetRequiredService<IPlannerGraphClient>();
            var body = new JsonObject
            {
                ["planId"] = RequireString(args, "planId"),
                ["title"] = RequireString(args, "title"),
            };

            var bucketId = OptString(args, "bucketId");
            if (!string.IsNullOrWhiteSpace(bucketId)) body["bucketId"] = bucketId;

            var dueDateTime = OptString(args, "dueDateTime");
            if (!string.IsNullOrWhiteSpace(dueDateTime)) body["dueDateTime"] = dueDateTime;

            var assignments = OptObject(args, "assignments");
            if (assignments is not null) body["assignments"] = assignments.DeepClone();

            var resp = await graph.CreateTaskAsync(body, ct);
            return ContentResult(resp);
        },
    };
}

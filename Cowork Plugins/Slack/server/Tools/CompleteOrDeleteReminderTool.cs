using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class CompleteOrDeleteReminderTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "complete_or_delete_reminder",
        Description = "Complete or delete a Slack reminder by id using reminders.complete or reminders.delete.",
        Annotations = new ToolAnnotations(
            Title: "Complete or delete Slack reminder",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["reminder"] = Prop("string", "Reminder id (e.g. Rm12345678)."),
                ["action"] = Prop("string", "Operation to perform.",
                    new JsonObject
                    {
                        ["enum"] = new JsonArray { "complete", "delete" },
                    }),
            },
            required: new[] { "reminder", "action" }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var action = RequireString(args, "action");
            var endpoint = action switch
            {
                "complete" => "reminders.complete",
                "delete" => "reminders.delete",
                _ => throw new ArgumentException("action must be either 'complete' or 'delete'"),
            };

            var body = new JsonObject
            {
                ["reminder"] = RequireString(args, "reminder"),
            };

            var resp = await slack.PostJsonAsync(endpoint, body, ct);
            return ContentResult(resp);
        },
    };
}
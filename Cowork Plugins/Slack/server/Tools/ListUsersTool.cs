using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class ListUsersTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_users",
        Description = "List members of the Slack workspace.",
        Annotations = new ToolAnnotations(
            Title: "List Slack users",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["limit"] = Prop("integer", "Max users per page (1-1000, default 200)."),
                ["cursor"] = Prop("string", "Pagination cursor from a previous response."),
                ["include_locale"] = Prop("boolean", "Include locale data for each user (default false)."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var q = new Dictionary<string, string?>
            {
                ["limit"] = (OptInt(args, "limit") ?? 200).ToString(),
                ["cursor"] = OptString(args, "cursor"),
                ["include_locale"] = (OptBool(args, "include_locale") ?? false) ? "true" : "false",
            };
            var resp = await slack.GetAsync("users.list", q, ct);
            return ContentResult(resp);
        },
    };
}

using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class ListChannelsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_channels",
        Description = "List Slack conversations visible to the calling user. Defaults to public and private channels, excluding archived.",
        Annotations = new ToolAnnotations(
            Title: "List Slack channels",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["types"] = Prop("string", "CSV of channel types: public_channel, private_channel, mpim, im. Default 'public_channel,private_channel'."),
                ["exclude_archived"] = Prop("boolean", "Exclude archived channels (default true)."),
                ["limit"] = Prop("integer", "Max channels per page (1-1000, default 200)."),
                ["cursor"] = Prop("string", "Pagination cursor from a previous response."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var q = new Dictionary<string, string?>
            {
                ["types"] = OptString(args, "types") ?? "public_channel,private_channel",
                ["exclude_archived"] = (OptBool(args, "exclude_archived") ?? true) ? "true" : "false",
                ["limit"] = (OptInt(args, "limit") ?? 200).ToString(),
                ["cursor"] = OptString(args, "cursor"),
            };
            var resp = await slack.GetAsync("conversations.list", q, ct);
            return ContentResult(resp);
        },
    };
}

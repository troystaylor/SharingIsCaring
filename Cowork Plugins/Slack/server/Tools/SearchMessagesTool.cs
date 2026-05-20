using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class SearchMessagesTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "search_messages",
        Description = "Search Slack messages using Slack search syntax (e.g. \"in:#general from:@alice yesterday\"). Returns matching message permalinks, channels, authors, and text.",
        Annotations = new ToolAnnotations(
            Title: "Search Slack messages",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["query"] = Prop("string", "Slack search syntax string."),
                ["count"] = Prop("integer", "Max results per page (1-100, default 20)."),
                ["sort"] = Prop("string", "Sort by 'score' (default) or 'timestamp'."),
                ["sort_dir"] = Prop("string", "'asc' or 'desc' (default 'desc')."),
            },
            required: "query"),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var q = new Dictionary<string, string?>
            {
                ["query"] = RequireString(args, "query"),
                ["count"] = OptInt(args, "count")?.ToString(),
                ["sort"] = OptString(args, "sort"),
                ["sort_dir"] = OptString(args, "sort_dir"),
            };
            var resp = await slack.GetAsync("search.messages", q, ct);
            return ContentResult(resp);
        },
    };
}

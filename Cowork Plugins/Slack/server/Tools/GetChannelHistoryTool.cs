using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class GetChannelHistoryTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_channel_history",
        Description = "Fetch the message history for a channel. Pages through conversations.history until 'limit' messages are accumulated or no more pages exist.",
        Annotations = new ToolAnnotations(
            Title: "Get Slack channel history",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["channel"] = Prop("string", "Channel ID (Cxxxx, Dxxxx, or Gxxxx)."),
                ["oldest"] = Prop("string", "Start of time range, inclusive (Slack ts string)."),
                ["latest"] = Prop("string", "End of time range, inclusive (Slack ts string)."),
                ["limit"] = Prop("integer", "Total max messages across pages (1-1000, default 200)."),
                ["cursor"] = Prop("string", "Starting cursor; if provided, only that page is fetched."),
            },
            required: "channel"),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var channel = RequireString(args, "channel");
            var oldest = OptString(args, "oldest");
            var latest = OptString(args, "latest");
            var limit = OptInt(args, "limit") ?? 200;
            var cursor = OptString(args, "cursor");

            // If caller supplied an explicit cursor, surface a single page.
            // Otherwise page through until limit reached or cursor exhausted.
            var allMessages = new JsonArray();
            string? nextCursor = cursor;
            JsonObject? lastResp = null;
            bool singlePage = cursor is not null;

            do
            {
                var pageLimit = Math.Min(200, limit - allMessages.Count);
                if (pageLimit <= 0) break;

                var q = new Dictionary<string, string?>
                {
                    ["channel"] = channel,
                    ["oldest"] = oldest,
                    ["latest"] = latest,
                    ["limit"] = pageLimit.ToString(),
                    ["cursor"] = nextCursor,
                };
                lastResp = await slack.GetAsync("conversations.history", q, ct);
                if (lastResp["messages"] is JsonArray msgs)
                {
                    foreach (var m in msgs)
                    {
                        if (m is null) continue;
                        allMessages.Add(m.DeepClone());
                        if (allMessages.Count >= limit) break;
                    }
                }
                nextCursor = lastResp["response_metadata"]?["next_cursor"]?.GetValue<string>();
                if (singlePage) break;
            } while (!string.IsNullOrEmpty(nextCursor) && allMessages.Count < limit);

            var result = new JsonObject
            {
                ["ok"] = true,
                ["channel"] = channel,
                ["messages"] = allMessages,
                ["next_cursor"] = nextCursor,
                ["has_more"] = lastResp?["has_more"]?.DeepClone(),
            };
            return ContentResult(result);
        },
    };
}

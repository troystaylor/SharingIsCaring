using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class ScheduleMessageTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "schedule_message",
        Description = "Schedule a Slack message to post at a future Unix timestamp using chat.scheduleMessage.",
        Annotations = new ToolAnnotations(
            Title: "Schedule Slack message",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["channel"] = Prop("string", "Channel ID, DM ID, or channel name (with leading '#')."),
                ["text"] = Prop("string", "Message text. Required fallback text when 'blocks' is provided."),
                ["post_at"] = Prop("integer", "Future Unix timestamp (seconds) when the message should be posted."),
                ["thread_ts"] = Prop("string", "Parent message timestamp to schedule a thread reply."),
                ["unfurl_links"] = Prop("boolean", "Whether to unfurl primarily text-based content (default true)."),
                ["reply_broadcast"] = Prop("boolean", "When posting in a thread, broadcast reply to the channel."),
                ["blocks"] = Prop("array", "Block Kit array of layout blocks.",
                    new JsonObject
                    {
                        ["items"] = new JsonObject { ["type"] = "object" },
                    }),
            },
            required: new[] { "channel", "text", "post_at" }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var postAt = OptInt(args, "post_at")
                ?? throw new ArgumentException("missing required parameter: post_at");

            var body = new JsonObject
            {
                ["channel"] = RequireString(args, "channel"),
                ["text"] = RequireString(args, "text"),
                ["post_at"] = postAt,
            };

            if (OptString(args, "thread_ts") is { } threadTs) body["thread_ts"] = threadTs;
            body["unfurl_links"] = OptBool(args, "unfurl_links") ?? true;
            if (OptBool(args, "reply_broadcast") is { } replyBroadcast) body["reply_broadcast"] = replyBroadcast;
            if (args["blocks"] is JsonArray blocks) body["blocks"] = blocks.DeepClone();

            var resp = await slack.PostJsonAsync("chat.scheduleMessage", body, ct);
            return ContentResult(resp);
        },
    };
}
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class SendMessageTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "send_message",
        Description = "Post a message to a Slack channel, DM, or thread as the signed-in user. Call this tool directly when the user's request specifies both a destination (channel, DM target, or thread) and the message content.",
        Annotations = new ToolAnnotations(
            Title: "Send Slack message",
            ReadOnlyHint: true,
            DestructiveHint: false,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["channel"] = Prop("string", "Channel ID, DM ID, or channel name (with leading '#')."),
                ["text"] = Prop("string", "Plain-text fallback. Required even when 'blocks' is provided."),
                ["thread_ts"] = Prop("string", "Parent message timestamp to reply in-thread."),
                ["unfurl_links"] = Prop("boolean", "Whether to unfurl primarily text-based content (default true)."),
                ["blocks"] = Prop("array", "Block Kit array of layout blocks (advanced; overrides 'text' rendering).",
                    new JsonObject
                    {
                        ["items"] = new JsonObject { ["type"] = "object" },
                    }),
            },
            required: new[] { "channel", "text" }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var body = new JsonObject
            {
                ["channel"] = RequireString(args, "channel"),
                ["text"] = RequireString(args, "text"),
            };
            if (OptString(args, "thread_ts") is { } tts) body["thread_ts"] = tts;
            body["unfurl_links"] = OptBool(args, "unfurl_links") ?? true;
            if (args["blocks"] is JsonArray blocks) body["blocks"] = blocks.DeepClone();

            var resp = await slack.PostJsonAsync("chat.postMessage", body, ct);
            return ContentResult(resp);
        },
    };
}

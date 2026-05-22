using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class PinMessageTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "pin_message",
        Description = "Pin a Slack message in a channel using pins.add.",
        Annotations = new ToolAnnotations(
            Title: "Pin Slack message",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["channel"] = Prop("string", "Channel ID containing the message to pin."),
                ["timestamp"] = Prop("string", "Slack message ts value to pin."),
            },
            required: new[] { "channel", "timestamp" }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var body = new JsonObject
            {
                ["channel"] = RequireString(args, "channel"),
                ["timestamp"] = RequireString(args, "timestamp"),
            };

            var resp = await slack.PostJsonAsync("pins.add", body, ct);
            return ContentResult(resp);
        },
    };
}
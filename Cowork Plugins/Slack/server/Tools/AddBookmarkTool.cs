using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class AddBookmarkTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "add_bookmark",
        Description = "Add a link bookmark to a Slack channel using bookmarks.add.",
        Annotations = new ToolAnnotations(
            Title: "Add Slack bookmark",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["channel"] = Prop("string", "Channel ID to add the bookmark to."),
                ["title"] = Prop("string", "Bookmark title."),
                ["link"] = Prop("string", "Bookmark URL (http:// or https://)."),
                ["emoji"] = Prop("string", "Optional emoji name to tag the bookmark."),
                ["parent_id"] = Prop("string", "Optional parent bookmark id."),
            },
            required: new[] { "channel", "title", "link" }),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var body = new JsonObject
            {
                ["channel_id"] = RequireString(args, "channel"),
                ["title"] = RequireString(args, "title"),
                ["type"] = "link",
                ["link"] = RequireString(args, "link"),
            };

            if (OptString(args, "emoji") is { } emoji) body["emoji"] = emoji;
            if (OptString(args, "parent_id") is { } parentId) body["parent_id"] = parentId;

            var resp = await slack.PostJsonAsync("bookmarks.add", body, ct);
            return ContentResult(resp);
        },
    };
}
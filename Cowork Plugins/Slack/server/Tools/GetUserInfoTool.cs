using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class GetUserInfoTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_user_info",
        Description = "Look up a Slack user by ID (Uxxxxx) or handle (e.g. 'alice'). If a handle is supplied, the tool resolves it through users.list first.",
        Annotations = new ToolAnnotations(
            Title: "Get Slack user info",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["user"] = Prop("string", "User ID (starts with 'U' or 'W') or display/name handle."),
            },
            required: "user"),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var user = RequireString(args, "user");

            // Treat anything that doesn't look like an ID as a handle to resolve.
            if (!(user.StartsWith('U') || user.StartsWith('W'))
                || user.Any(c => !char.IsLetterOrDigit(c)))
            {
                var handle = user.TrimStart('@').ToLowerInvariant();
                string? cursor = null;
                string? resolvedId = null;
                for (var page = 0; page < 25 && resolvedId is null; page++)
                {
                    var q = new Dictionary<string, string?>
                    {
                        ["limit"] = "200",
                        ["cursor"] = cursor,
                    };
                    var listResp = await slack.GetAsync("users.list", q, ct);
                    if (listResp["members"] is JsonArray members)
                    {
                        foreach (var m in members)
                        {
                            if (m is not JsonObject mo) continue;
                            var name = mo["name"]?.GetValue<string>();
                            var dn = mo["profile"]?["display_name_normalized"]?.GetValue<string>();
                            var rn = mo["profile"]?["real_name_normalized"]?.GetValue<string>();
                            if (string.Equals(name, handle, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(dn, handle, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(rn, handle, StringComparison.OrdinalIgnoreCase))
                            {
                                resolvedId = mo["id"]?.GetValue<string>();
                                break;
                            }
                        }
                    }
                    cursor = listResp["response_metadata"]?["next_cursor"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(cursor)) break;
                }

                if (resolvedId is null)
                    throw new SlackApiException("users.list", "user_not_found",
                        new JsonObject { ["queried_handle"] = handle });
                user = resolvedId;
            }

            var resp = await slack.GetAsync("users.info", new Dictionary<string, string?>
            {
                ["user"] = user,
            }, ct);
            return ContentResult(resp);
        },
    };
}

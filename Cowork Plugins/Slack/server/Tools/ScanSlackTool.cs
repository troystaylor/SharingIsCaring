using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class ScanSlackTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "scan_slack",
        Description = "Search the built-in Slack Web API capability index for methods matching a natural-language intent. Returns a ranked list (method, domain, description, required_scopes) suitable for handing to launch_slack.",
        Annotations = new ToolAnnotations(
            Title: "Scan Slack capability index",
            ReadOnlyHint: true,
            OpenWorldHint: false),
        InputSchema = Schema(
            new JsonObject
            {
                ["query"] = Prop("string", "Natural-language description of what you want to do, e.g. 'set my status', 'snooze notifications', 'pin a message'."),
                ["top"] = Prop("integer", "Max results to return (default 10, max 50)."),
            },
            required: "query"),
        Invoke = async (sp, args, ct) =>
        {
            var idx = sp.GetRequiredService<SlackCapabilityIndex>();
            var top = Math.Clamp(OptInt(args, "top") ?? 10, 1, 50);
            var results = idx.Search(RequireString(args, "query"), top);

            var arr = new JsonArray();
            foreach (var (cap, score) in results)
            {
                arr.Add(new JsonObject
                {
                    ["method"] = cap.Method,
                    ["domain"] = cap.Domain,
                    ["description"] = cap.Description,
                    ["required_scopes"] = new JsonArray(cap.RequiredScopes.Select(s => (JsonNode)s!).ToArray()),
                    ["score"] = Math.Round(score, 3),
                });
            }
            await Task.CompletedTask;
            return ContentResult(new JsonObject
            {
                ["ok"] = true,
                ["results"] = arr,
            });
        },
    };
}

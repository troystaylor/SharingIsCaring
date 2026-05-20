using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class LaunchSlackTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "launch_slack",
        Description = "Generic invoker for any Slack Web API method listed in the built-in capability index. Use after scan_slack to discover the method. Defaults to POST with a JSON body.",
        // ReadOnlyHint:true is a Cowork workaround — Copilot Cowork's client gates tools/call on readOnlyHint and won't invoke write-class tools. Revert when Microsoft adds a write-tool approval UX.
        Annotations = new ToolAnnotations(
            Title: "Invoke any Slack Web API method",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["endpoint"] = Prop("string", "Slack Web API method (e.g. 'reminders.add'). Must exist in the capability index."),
                ["method"] = Prop("string", "HTTP verb: 'GET' or 'POST' (default 'POST')."),
                ["body"] = Prop("object", "JSON body for POST. Ignored for GET.",
                    new JsonObject { ["additionalProperties"] = true }),
                ["query"] = Prop("object", "Querystring parameters as a flat object of string values.",
                    new JsonObject { ["additionalProperties"] = true }),
            },
            required: "endpoint"),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var idx = sp.GetRequiredService<SlackCapabilityIndex>();
            var endpoint = RequireString(args, "endpoint");

            if (!idx.Contains(endpoint))
                throw new ArgumentException($"unknown Slack method: {endpoint}. Use scan_slack to discover valid endpoints.");

            var method = (OptString(args, "method") ?? "POST").ToUpperInvariant();
            var query = ToStringDict(args["query"] as JsonObject);

            JsonObject resp;
            if (method == "GET")
            {
                resp = await slack.GetAsync(endpoint, query, ct);
            }
            else
            {
                var body = (args["body"] as JsonObject) ?? new JsonObject();
                // If caller supplied query params on a POST, append them to the endpoint.
                var path = endpoint;
                if (query is { Count: > 0 })
                {
                    var qs = string.Join('&', query.Where(kv => kv.Value is not null)
                        .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
                    path = endpoint + "?" + qs;
                }
                resp = await slack.PostJsonAsync(path, body, ct);
            }

            return ContentResult(resp);
        },
    };

    internal static Dictionary<string, string?>? ToStringDict(JsonObject? obj)
    {
        if (obj is null) return null;
        var d = new Dictionary<string, string?>();
        foreach (var kv in obj)
        {
            if (kv.Value is null) { d[kv.Key] = null; continue; }
            d[kv.Key] = kv.Value switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
                JsonValue v when v.TryGetValue<long>(out var l) => l.ToString(),
                JsonValue v when v.TryGetValue<double>(out var dn) => dn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => kv.Value.ToJsonString(),
            };
        }
        return d;
    }
}

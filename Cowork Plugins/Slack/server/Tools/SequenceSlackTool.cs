using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SlackCoworkMcp.Slack;
using static SlackCoworkMcp.Tools.ToolHelpers;

namespace SlackCoworkMcp.Tools;

internal static class SequenceSlackTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "sequence_slack",
        Description = "Execute a sequence of Slack Web API calls in order. Stops on the first error unless continue_on_error is true. Each request follows the same shape as launch_slack.",
        Annotations = new ToolAnnotations(
            Title: "Run a sequence of Slack calls",
            ReadOnlyHint: true,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["requests"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Ordered list of Slack calls to execute.",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["endpoint"] = Prop("string", "Slack Web API method (must exist in capability index)."),
                            ["method"] = Prop("string", "HTTP verb: GET or POST (default POST)."),
                            ["body"] = Prop("object", "JSON body for POST.", new JsonObject { ["additionalProperties"] = true }),
                            ["query"] = Prop("object", "Querystring parameters.", new JsonObject { ["additionalProperties"] = true }),
                        },
                        ["required"] = new JsonArray { "endpoint" },
                    },
                },
                ["continue_on_error"] = Prop("boolean", "When true, errors are recorded per-request and execution continues. Default false."),
            },
            required: "requests"),
        Invoke = async (sp, args, ct) =>
        {
            var slack = sp.GetRequiredService<ISlackClient>();
            var idx = sp.GetRequiredService<SlackCapabilityIndex>();

            if (args["requests"] is not JsonArray requests)
                throw new ArgumentException("'requests' must be an array");

            var continueOnError = OptBool(args, "continue_on_error") ?? false;

            var results = new JsonArray();
            for (var i = 0; i < requests.Count; i++)
            {
                var entry = requests[i] as JsonObject
                    ?? throw new ArgumentException($"requests[{i}] is not an object");

                var endpoint = entry["endpoint"]?.GetValue<string>()
                    ?? throw new ArgumentException($"requests[{i}].endpoint is required");
                var method = (entry["method"]?.GetValue<string>() ?? "POST").ToUpperInvariant();
                var query = LaunchSlackTool.ToStringDict(entry["query"] as JsonObject);
                var body = entry["body"] as JsonObject ?? new JsonObject();

                if (!idx.Contains(endpoint))
                {
                    var err = new JsonObject
                    {
                        ["index"] = i,
                        ["endpoint"] = endpoint,
                        ["ok"] = false,
                        ["error"] = "unknown_method",
                    };
                    results.Add(err);
                    if (!continueOnError)
                        return ContentResult(new JsonObject { ["ok"] = false, ["results"] = results }, isError: true);
                    continue;
                }

                try
                {
                    JsonObject resp;
                    if (method == "GET")
                    {
                        resp = await slack.GetAsync(endpoint, query, ct);
                    }
                    else
                    {
                        var path = endpoint;
                        if (query is { Count: > 0 })
                        {
                            var qs = string.Join('&', query.Where(kv => kv.Value is not null)
                                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
                            path = endpoint + "?" + qs;
                        }
                        resp = await slack.PostJsonAsync(path, body, ct);
                    }
                    results.Add(new JsonObject
                    {
                        ["index"] = i,
                        ["endpoint"] = endpoint,
                        ["ok"] = true,
                        ["response"] = resp.DeepClone(),
                    });
                }
                catch (SlackApiException ex)
                {
                    results.Add(new JsonObject
                    {
                        ["index"] = i,
                        ["endpoint"] = endpoint,
                        ["ok"] = false,
                        ["error"] = ex.SlackError,
                        ["response"] = ex.Response?.DeepClone(),
                    });
                    if (!continueOnError)
                        return ContentResult(new JsonObject { ["ok"] = false, ["results"] = results }, isError: true);
                }
            }

            return ContentResult(new JsonObject
            {
                ["ok"] = true,
                ["results"] = results,
            });
        },
    };
}

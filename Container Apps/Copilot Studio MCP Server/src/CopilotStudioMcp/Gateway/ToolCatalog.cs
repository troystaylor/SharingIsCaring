using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CopilotStudioMcp.Configuration;
using Microsoft.Extensions.Options;

namespace CopilotStudioMcp.Gateway;

/// <summary>
/// The gateway's tool list: <c>list_environments</c> plus every Copilot Studio tool, each with a required
/// <c>environmentId</c> added. Read from Copilot Studio on demand, so new tools and schema changes appear
/// without a redeploy.
/// </summary>
public sealed class ToolCatalog(UpstreamClient upstream, IOptions<GatewayOptions> options, TimeProvider clock)
{
    public const string ListEnvironmentsName = "list_environments";
    public const string EnvironmentIdName = "environmentId";

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, JsonArray Tools)> _cache = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);

    public static JsonObject ListEnvironmentsTool() => new()
    {
        ["name"] = ListEnvironmentsName,
        ["title"] = "List Power Platform environments",
        ["description"] =
            "List the Power Platform environments the signed-in user can access. Call this first and use an `id` from the result as `environmentId` for every other tool. If the user did not name an environment, show the list and ask which one to use.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["additionalProperties"] = false
        },
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = true,
            ["destructiveHint"] = false,
            ["idempotentHint"] = true,
            ["openWorldHint"] = true
        }
    };

    /// <summary>Returns a copy of the tool whose input schema requires <c>environmentId</c>.</summary>
    public static JsonObject WithEnvironmentId(JsonObject tool)
    {
        var copy = (JsonObject)tool.DeepClone();
        var schema = copy["inputSchema"] as JsonObject ?? new JsonObject();
        schema["type"] = "object";

        var properties = new JsonObject
        {
            [EnvironmentIdName] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "ID of the Power Platform environment to run this in, exactly as returned by list_environments. Required. Never guess it."
            }
        };
        if (schema["properties"] is JsonObject existing)
        {
            foreach (var (key, value) in existing.ToArray())
            {
                if (key != EnvironmentIdName)
                {
                    existing.Remove(key);
                    properties[key] = value;
                }
            }
        }

        schema["properties"] = properties;

        var required = new JsonArray(EnvironmentIdName);
        if (schema["required"] is JsonArray existingRequired)
        {
            foreach (var name in existingRequired.Select(n => n?.GetValue<string>()).Where(n => n is not null && n != EnvironmentIdName))
            {
                required.Add(name);
            }
        }

        schema["required"] = required;
        copy["inputSchema"] = schema;
        return copy;
    }

    public async Task<JsonArray> GetAsync(string cacheKey, string token, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(options.Value.ToolsCacheMinutes);
        if (_cache.TryGetValue(cacheKey, out var hit) && clock.GetUtcNow() - hit.At < ttl)
        {
            return (JsonArray)hit.Tools.DeepClone();
        }

        await _refresh.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out hit) && clock.GetUtcNow() - hit.At < ttl)
            {
                return (JsonArray)hit.Tools.DeepClone();
            }

            var environments = await upstream.ListEnvironmentsAsync(token, ct);
            if (environments.Count == 0)
            {
                throw new GatewayException(404, "NO_ENVIRONMENTS", "This account has no accessible Power Platform environments.");
            }

            // Tool schemas are not specific to one environment, so the first one that answers is enough.
            GatewayException? last = null;
            foreach (var environment in environments.Take(5))
            {
                try
                {
                    var message = await upstream.CallAsync(environment.Id, token, "tools/list", new JsonObject(), ct);
                    if (message["error"] is not null || message["result"]?["tools"] is not JsonArray upstreamTools)
                    {
                        last = new GatewayException(502, "UPSTREAM_ERROR", "Copilot Studio could not list its tools.");
                        continue;
                    }

                    var tools = new JsonArray(ListEnvironmentsTool());
                    foreach (var tool in upstreamTools.OfType<JsonObject>())
                    {
                        tools.Add(WithEnvironmentId(tool));
                    }

                    _cache[cacheKey] = (clock.GetUtcNow(), tools);
                    return (JsonArray)tools.DeepClone();
                }
                catch (GatewayException ex) when (ex.Status != 401)
                {
                    last = ex;
                }
            }

            throw last ?? new GatewayException(502, "UPSTREAM_ERROR", "Copilot Studio could not list its tools.");
        }
        finally
        {
            _refresh.Release();
        }
    }
}

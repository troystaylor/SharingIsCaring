using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlannerCoworkMcp.Tools;

internal static class ToolHelpers
{
    public static JsonObject ContentResult(JsonNode? payload, bool isError = false)
    {
        var text = payload?.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
            ?? "null";
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                },
            },
            ["isError"] = isError,
        };
    }

    public static string RequireString(JsonObject args, string name)
    {
        var v = args[name]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(v))
            throw new ArgumentException($"missing required parameter: {name}");
        return v;
    }

    public static string? OptString(JsonObject args, string name)
        => args[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static JsonObject? OptObject(JsonObject args, string name)
        => args[name] as JsonObject;

    public static JsonObject Schema(JsonObject properties, params string[] required)
    {
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        var obj = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (required.Length > 0) obj["required"] = req;
        return obj;
    }

    public static JsonObject Prop(string type, string description, JsonObject? extra = null)
    {
        var o = new JsonObject
        {
            ["type"] = type,
            ["description"] = description,
        };
        if (extra is not null)
            foreach (var kv in extra) o[kv.Key] = kv.Value?.DeepClone();
        return o;
    }
}

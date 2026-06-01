using System.Text.Json.Nodes;

namespace NetSuiteCoworkMcp.Tools;

internal static class ToolHelpers
{
    public static JsonObject ContentResult(JsonNode? payload, bool isError = false)
    {
        var text = payload?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false })
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
        if (string.IsNullOrEmpty(v))
            throw new ArgumentException($"missing required parameter: {name}");
        return v;
    }

    public static string? OptString(JsonObject args, string name)
        => args[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? OptInt(JsonObject args, string name)
    {
        if (args[name] is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return (int)l;
            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        }
        return null;
    }

    public static bool OptBool(JsonObject args, string name)
    {
        if (args[name] is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var p)) return p;
        }
        return false;
    }

    public static JsonObject RequireObject(JsonObject args, string name)
    {
        if (args[name] is JsonObject o) return o;
        throw new ArgumentException($"missing required object parameter: {name}");
    }

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

    public static JsonObject SchemaOpenObject(JsonObject properties, params string[] required)
    {
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        var obj = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = true,
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

    public static string EscapeSuiteQLLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}

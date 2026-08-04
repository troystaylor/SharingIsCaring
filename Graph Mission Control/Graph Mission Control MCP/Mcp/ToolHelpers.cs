using System.Text.Json;
using System.Text.Json.Nodes;

namespace GraphMissionControl.Mcp.Mcp;

/// <summary>Schema and result builders shared by the tool pack.</summary>
public static class ToolHelpers
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    /// <summary>
    /// Wraps a payload as MCP text content. <c>structuredContent</c> is deliberately omitted —
    /// several hosts mishandle it.
    /// </summary>
    public static JsonObject ContentResult(JsonNode? payload, bool isError = false) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = payload?.ToJsonString(Compact) ?? "null",
            },
        },
        ["isError"] = isError,
    };

    public static JsonObject TextResult(string text, bool isError = false) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = text },
        },
        ["isError"] = isError,
    };

    public static string RequireString(JsonObject args, string name)
    {
        var v = args[name]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(v))
            throw new ArgumentException($"missing required parameter: {name}");
        return v;
    }

    public static string? OptString(JsonObject args, string name)
        => args[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? OptInt(JsonObject args, string name)
    {
        if (args[name] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        return null;
    }

    public static string[] OptStringArray(JsonObject args, string name)
    {
        if (args[name] is not JsonArray arr) return [];
        return [.. arr.OfType<JsonValue>()
                      .Select(v => v.TryGetValue<string>(out var s) ? s : null)
                      .Where(s => !string.IsNullOrWhiteSpace(s))
                      .Select(s => s!)];
    }
}

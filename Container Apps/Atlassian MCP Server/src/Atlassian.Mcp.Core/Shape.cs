using System.Text.Json.Nodes;

namespace Atlassian.Mcp.Core;

/// <summary>Small helpers for reading provider JSON defensively and building model-friendly results.</summary>
public static class Shape
{
    /// <summary>Walks object properties, returning null as soon as a step is missing or not an object.</summary>
    public static JsonNode? At(JsonNode? node, params string[] path)
    {
        foreach (var key in path)
        {
            if (node is JsonObject obj && obj.TryGetPropertyValue(key, out var next))
            {
                node = next;
            }
            else
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>The string (or scalar rendered as a string) at <paramref name="path"/>, or null.</summary>
    public static string? Str(JsonNode? node, params string[] path)
    {
        if (At(node, path) is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString().Trim('"');
    }

    public static int? Int(JsonNode? node, params string[] path) =>
        At(node, path) is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    public static bool? Bool(JsonNode? node, params string[] path) =>
        At(node, path) is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    /// <summary>The array at <paramref name="path"/>, or an empty sequence.</summary>
    public static IEnumerable<JsonNode> Items(JsonNode? node, params string[] path) =>
        At(node, path) is JsonArray array ? array.Where(i => i is not null).Select(i => i!) : [];

    /// <summary>
    /// Builds an object from key/value pairs, leaving out nulls so results carry only what exists. Values may be
    /// strings, numbers, booleans, or JSON nodes.
    /// </summary>
    public static JsonObject Obj(params (string Key, object? Value)[] items)
    {
        var result = new JsonObject();
        foreach (var (key, value) in items)
        {
            switch (value)
            {
                case null:
                    break;
                case string s:
                    result[key] = JsonValue.Create(s);
                    break;
                case bool b:
                    result[key] = JsonValue.Create(b);
                    break;
                case int i:
                    result[key] = JsonValue.Create(i);
                    break;
                case long l:
                    result[key] = JsonValue.Create(l);
                    break;
                case JsonNode node:
                    result[key] = node.Parent is null ? node : node.DeepClone();
                    break;
                default:
                    throw new ArgumentException($"Unsupported value type {value.GetType().Name} for '{key}'.");
            }
        }

        return result;
    }

    public static JsonArray Array(IEnumerable<string?> values) =>
        new(values.Where(v => !string.IsNullOrEmpty(v)).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// <summary>
    /// Keeps items until either cap is hit. The first item is always kept, so a single large item is returned
    /// rather than nothing.
    /// </summary>
    public static (JsonArray Kept, bool Truncated) Limit(IEnumerable<JsonNode?> items, int maxItems, int maxChars)
    {
        var kept = new JsonArray();
        var chars = 0;

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var length = item.ToJsonString().Length;
            if (kept.Count >= maxItems || (kept.Count > 0 && chars + length > maxChars))
            {
                return (kept, true);
            }

            chars += length;
            kept.Add(item);
        }

        return (kept, false);
    }

    public static string Cap(string? text, int max, out bool truncated)
    {
        text ??= string.Empty;
        truncated = text.Length > max;
        return truncated ? text[..max] : text;
    }

    public static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    /// <summary>Reads the <c>cursor</c> query value out of a provider's <c>next</c> link.</summary>
    public static string? CursorFrom(string? nextLink)
    {
        if (string.IsNullOrEmpty(nextLink))
        {
            return null;
        }

        var query = nextLink.Contains('?') ? nextLink[(nextLink.IndexOf('?') + 1)..] : nextLink;
        foreach (var part in query.Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "cursor")
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }
}

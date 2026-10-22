using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SalesforceMcp.Salesforce;

/// <summary>
/// Reduces an object description to one short signature per field, so two descriptions can be compared
/// cheaply and a caller can hold a baseline without keeping the whole description.
/// </summary>
/// <remarks>
/// A signature covers what changes how a field can be queried or written: type and size, whether it is
/// required, writable, calculated, unique, an external ID, what it points to, which field controls it, and
/// a hash of its active picklist values. Labels and help text are left out on purpose, because a relabel
/// does not break an integration and would drown out the changes that do.
/// </remarks>
public static class SchemaDiff
{
    public const int MaxListed = 200;

    public static SortedDictionary<string, string> Signatures(JsonObject describe)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in (describe["fields"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if ((string?)field["name"] is { Length: > 0 } name)
            {
                result[name] = Signature(field);
            }
        }

        return result;
    }

    public static string Signature(JsonObject field)
    {
        var type = (string?)field["type"] ?? "unknown";
        var precision = (int?)field["precision"] ?? 0;
        var scale = (int?)field["scale"] ?? 0;
        var length = (int?)field["length"] ?? 0;

        var parts = new List<string>
        {
            precision > 0 ? $"{type}({precision},{scale})" : length > 0 ? $"{type}({length})" : type
        };

        if ((bool?)field["nillable"] == false && (bool?)field["defaultedOnCreate"] != true)
        {
            parts.Add("required");
        }

        if ((bool?)field["createable"] == false)
        {
            parts.Add("!createable");
        }

        if ((bool?)field["updateable"] == false)
        {
            parts.Add("!updateable");
        }

        if ((bool?)field["calculated"] == true)
        {
            parts.Add("calculated");
        }

        if ((bool?)field["unique"] == true)
        {
            parts.Add("unique");
        }

        if ((bool?)field["externalId"] == true)
        {
            parts.Add("externalId");
        }

        if (field["referenceTo"] is JsonArray { Count: > 0 } references)
        {
            parts.Add("ref=" + string.Join('/', references.Select(r => (string?)r).OrderBy(r => r, StringComparer.Ordinal)));
        }

        if ((string?)field["controllerName"] is { Length: > 0 } controller)
        {
            parts.Add($"controlledBy={controller}");
        }

        if (field["picklistValues"] is JsonArray picklist)
        {
            var active = picklist.OfType<JsonObject>()
                .Where(p => (bool?)p["active"] == true)
                .Select(p => (string?)p["value"] ?? string.Empty)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (active.Count > 0)
            {
                parts.Add($"values={active.Count}:{Hash(string.Join('\n', active), 4)}");
            }
        }

        return string.Join('|', parts);
    }

    /// <summary>A short hash of every field signature. Equal fingerprints mean no field changed.</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, string> signatures)
    {
        var text = string.Join('\n', signatures.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}={s.Value}"));
        return Hash(text, 8);
    }

    public static JsonObject Compare(
        IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var added = after.Where(a => !before.ContainsKey(a.Key)).OrderBy(a => a.Key, StringComparer.Ordinal).ToList();
        var removed = before.Where(b => !after.ContainsKey(b.Key)).OrderBy(b => b.Key, StringComparer.Ordinal).ToList();
        var changed = before
            .Where(b => after.TryGetValue(b.Key, out var current) && current != b.Value)
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        var result = new JsonObject
        {
            ["identical"] = added.Count + removed.Count + changed.Count == 0,
            ["added"] = new JsonArray(added.Take(MaxListed)
                .Select(a => (JsonNode?)new JsonObject { ["field"] = a.Key, ["signature"] = a.Value }).ToArray()),
            ["removed"] = new JsonArray(removed.Take(MaxListed)
                .Select(r => (JsonNode?)new JsonObject { ["field"] = r.Key, ["signature"] = r.Value }).ToArray()),
            ["changed"] = new JsonArray(changed.Take(MaxListed)
                .Select(c => (JsonNode?)new JsonObject
                {
                    ["field"] = c.Key,
                    ["before"] = c.Value,
                    ["after"] = after[c.Key]
                }).ToArray()),
            ["counts"] = new JsonObject
            {
                ["added"] = added.Count,
                ["removed"] = removed.Count,
                ["changed"] = changed.Count,
                ["unchanged"] = before.Count - removed.Count - changed.Count
            }
        };

        if (added.Count > MaxListed || removed.Count > MaxListed || changed.Count > MaxListed)
        {
            result["note"] = $"Each list shows at most {MaxListed} fields. The counts are complete.";
        }

        return result;
    }

    private static string Hash(string text, int bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))[..bytes]);
}

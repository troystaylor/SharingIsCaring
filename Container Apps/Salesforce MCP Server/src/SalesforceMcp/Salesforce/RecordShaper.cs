using System.Text.Json.Nodes;

namespace SalesforceMcp.Salesforce;

/// <summary>
/// Reshapes Salesforce responses for a model: strips REST plumbing, adds a link back to each record, and
/// caps how much data comes back so one call cannot flood the model's context window.
/// </summary>
public static class RecordShaper
{
    /// <summary>
    /// Replaces each record's <c>attributes</c> block (type plus an API URL) with <c>sobjectType</c> and, when
    /// the record has an Id, a <c>recordUrl</c> that opens it in Salesforce. Recurses into related records and
    /// subquery results.
    /// </summary>
    public static JsonNode? Clean(JsonNode? node, string instanceUrl)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    Clean(item, instanceUrl);
                }

                break;

            case JsonObject obj:
                foreach (var child in obj.Select(p => p.Value).ToList())
                {
                    Clean(child, instanceUrl);
                }

                if (obj["attributes"] is JsonObject attributes)
                {
                    var type = (string?)attributes["type"];
                    obj.Remove("attributes");

                    if (type is not null)
                    {
                        obj.Insert(0, "sobjectType", type);
                    }

                    if (obj["Id"] is JsonValue id && id.TryGetValue<string>(out var recordId) && !string.IsNullOrEmpty(recordId))
                    {
                        obj["recordUrl"] = $"{instanceUrl.TrimEnd('/')}/{recordId}";
                    }
                }

                break;
        }

        return node;
    }

    /// <summary>
    /// Cleans <paramref name="records"/> and keeps as many as fit in both <paramref name="maxRecords"/> and
    /// <paramref name="maxChars"/>, always keeping at least one. Reports whether anything was dropped.
    /// </summary>
    public static (JsonArray Kept, bool Truncated) Limit(
        JsonArray? records, string instanceUrl, int maxRecords, int maxChars)
    {
        var kept = new JsonArray();
        if (records is null)
        {
            return (kept, false);
        }

        var used = 0;
        foreach (var record in records)
        {
            var clone = Clean(record?.DeepClone(), instanceUrl);
            var size = clone?.ToJsonString().Length ?? 4;

            if (kept.Count > 0 && (kept.Count >= maxRecords || used + size > maxChars))
            {
                return (kept, true);
            }

            kept.Add(clone);
            used += size;
        }

        return (kept, false);
    }

    /// <summary>
    /// Shapes a SOQL result. A continuation URL is only offered when every record in the batch was returned,
    /// because following it after a partial batch would silently skip the records that were dropped.
    /// </summary>
    public static JsonObject ShapeQuery(JsonNode? response, string instanceUrl, int maxRecords, int maxChars)
    {
        var (kept, truncated) = Limit(response?["records"] as JsonArray, instanceUrl, maxRecords, maxChars);
        var nextRecordsUrl = (string?)response?["nextRecordsUrl"];

        var result = new JsonObject
        {
            ["totalSize"] = response?["totalSize"]?.DeepClone() ?? kept.Count,
            ["returned"] = kept.Count,
            ["done"] = (bool?)response?["done"] ?? true,
            ["records"] = kept
        };

        if (truncated)
        {
            result["truncated"] = true;
            result["note"] = "Some records in this batch were left out to keep the response small. Narrow the WHERE clause, select fewer fields, or add a LIMIT.";
        }
        else if (nextRecordsUrl is not null)
        {
            result["nextRecordsUrl"] = nextRecordsUrl;
        }

        return result;
    }
}

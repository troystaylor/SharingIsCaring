using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tools;

/// <summary>
/// Read-only tools over the Salesforce REST API. Each call runs as the person who is signed in to Salesforce,
/// so Salesforce's own sharing rules and field-level security decide what comes back for them.
/// </summary>
/// <remarks>
/// The [Description] text is the model's only basis for choosing a tool and filling its arguments.
/// An empty result is a success and is returned as one: reporting "found nothing" as an error teaches
/// an agent to invent an answer.
/// </remarks>
[McpServerToolType]
public sealed class SalesforceReadTools(
    SalesforceClient salesforce,
    SalesforceVersions versions,
    SchemaService schema)
{
    private const int MaxListedObjects = 200;
    private const int MaxDescribedFields = 300;
    private const int MaxPicklistValues = 25;
    private const int MaxChildRelationships = 100;

    [McpServerTool(Name = "list_api_versions", Title = "List API Versions", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the Salesforce REST API versions this org supports, the server's default version, and the latest version. Use it before passing an apiVersion to another tool, or to check that the org is on the release you expect.")]
    public async Task<JsonNode> ListApiVersions(CancellationToken cancellationToken = default)
    {
        var supported = await versions.ListAsync(cancellationToken);
        var latest = supported[^1];
        var fallback = salesforce.Settings.ApiVersion;

        return new JsonObject
        {
            ["default"] = fallback,
            ["defaultSupported"] = supported.Any(v => v.Version == fallback),
            ["latest"] = latest.Version,
            ["latestRelease"] = latest.Label,
            ["versions"] = new JsonArray(supported.Select(v => (JsonNode?)JsonValue.Create(v.Version)).ToArray())
        };
    }

    [McpServerTool(Name = "soql_query", Title = "Run SOQL Query", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Runs a read-only SOQL query against Salesforce and returns the matching records, each with a recordUrl that opens it in Salesforce. Use it to find and read records, for example SELECT Id, Name, StageName, Amount FROM Opportunity WHERE IsClosed = false ORDER BY Amount DESC LIMIT 20. Always select specific fields and add a LIMIT. If you do not know the object or field names, call list_objects and describe_object first. If done is false and nextRecordsUrl is present, pass it to query_more for the next batch.")]
    public async Task<JsonNode> SoqlQuery(
        [Description("A SOQL statement that starts with SELECT. Use API names for objects and fields, such as Account or Region__c.")] string query,
        [Description("Most records to return in this response, from 1 to 200. Defaults to 50.")] int maxRecords = 50,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var soql = SalesforceNames.Soql(query);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var response = await salesforce.GetAsync(
            salesforce.DataPath($"query?q={Uri.EscapeDataString(soql)}", version), cancellationToken);

        return ShapeQuery(response, maxRecords);
    }

    [McpServerTool(Name = "query_more", Title = "Get Next Page of Query Results", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Fetches the next batch of a SOQL query that returned done: false. Pass the nextRecordsUrl exactly as the previous soql_query or query_more call returned it.")]
    public async Task<JsonNode> QueryMore(
        [Description("The nextRecordsUrl value from the previous result, such as /services/data/v66.0/query/01gxx0000000001AAA-2000.")] string nextRecordsUrl,
        [Description("Most records to return in this response, from 1 to 200. Defaults to 50.")] int maxRecords = 50,
        CancellationToken cancellationToken = default)
    {
        var path = SalesforceNames.NextRecordsUrl(nextRecordsUrl);
        return ShapeQuery(await salesforce.GetAsync(path, cancellationToken), maxRecords);
    }

    [McpServerTool(Name = "sosl_search", Title = "Search Across Objects", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Runs a SOSL full-text search that looks across several objects at once, for example FIND {Acme} IN NAME FIELDS RETURNING Account(Id, Name), Contact(Id, Name, Email) LIMIT 10. Use it when you have a name or keyword but do not know which object holds it. Use soql_query when you know the object and the filter.")]
    public async Task<JsonNode> SoslSearch(
        [Description("A SOSL statement that starts with FIND. Wrap the search text in braces.")] string search,
        [Description("Most records to return in this response, from 1 to 200. Defaults to 50.")] int maxRecords = 50,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var sosl = SalesforceNames.Sosl(search);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var response = await salesforce.GetAsync(
            salesforce.DataPath($"search?q={Uri.EscapeDataString(sosl)}", version), cancellationToken);

        var (kept, truncated) = RecordShaper.Limit(
            response?["searchRecords"] as JsonArray,
            salesforce.Settings.InstanceUrl,
            Cap(maxRecords),
            salesforce.Settings.MaxResponseChars);

        var result = new JsonObject { ["returned"] = kept.Count, ["records"] = kept };
        if (truncated)
        {
            result["truncated"] = true;
            result["note"] = "Some results were left out to keep the response small. Add a LIMIT or narrow the search.";
        }

        return result;
    }

    [McpServerTool(Name = "get_record", Title = "Get Record", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Reads one Salesforce record by its ID. Pass fields to limit which fields come back. Without fields, every field the signed-in user can see is returned, which can be large.")]
    public async Task<JsonNode> GetRecord(
        [Description("The sObject API name, such as Account, Contact, Opportunity, or Invoice__c.")] string sobject,
        [Description("The 15 or 18 character record ID.")] string id,
        [Description("Optional field API names to return, such as [\"Name\",\"Industry\"].")] string[]? fields = null,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var recordId = SalesforceNames.RecordId(id);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var path = salesforce.DataPath($"sobjects/{type}/{recordId}", version);
        if (fields is { Length: > 0 })
        {
            if (fields.Length > SalesforceNames.MaxFieldCount)
            {
                throw new McpException($"Request at most {SalesforceNames.MaxFieldCount} fields.");
            }

            path += "?fields=" + Uri.EscapeDataString(string.Join(',', fields.Select(SalesforceNames.Field)));
        }

        var record = await salesforce.GetAsync(path, cancellationToken);
        return RecordShaper.Clean(record, salesforce.Settings.InstanceUrl) ?? new JsonObject();
    }

    [McpServerTool(Name = "list_objects", Title = "List Salesforce Objects", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the Salesforce objects (tables) the signed-in user can see, with API name, label, and whether each is custom. Use nameContains to find an object, for example 'invoice'. Call this before querying an object you are not sure exists.")]
    public async Task<JsonNode> ListObjects(
        [Description("Optional text to match against object API names and labels, case-insensitive.")] string? nameContains = null,
        [Description("When true (the default), only objects that can be queried with SOQL are returned.")] bool queryableOnly = true,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var global = await schema.GlobalDescribeAsync(version, cancellationToken);

        var matches = (global.Body["sobjects"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(o => !queryableOnly || (bool?)o["queryable"] == true)
            .Where(o => Matches(o, nameContains))
            .ToList();

        var objects = new JsonArray();
        foreach (var o in matches.Take(MaxListedObjects))
        {
            objects.Add(new JsonObject
            {
                ["name"] = (string?)o["name"],
                ["label"] = (string?)o["label"],
                ["custom"] = (bool?)o["custom"] ?? false,
                ["queryable"] = (bool?)o["queryable"] ?? false,
                ["keyPrefix"] = (string?)o["keyPrefix"]
            });
        }

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["schema"] = global.Meta(),
            ["totalMatches"] = matches.Count,
            ["returned"] = objects.Count,
            ["objects"] = objects
        };

        if (matches.Count > MaxListedObjects)
        {
            result["note"] = $"Only the first {MaxListedObjects} matches are shown. Use nameContains to narrow the list.";
        }

        return result;
    }

    [McpServerTool(Name = "describe_object", Title = "Describe Object", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Returns the fields of one Salesforce object: API name, label, type, length, picklist values, and which object a lookup points to. Use it to learn the exact field names before writing a SOQL query. Use fieldNameContains to look for a specific field on objects with many fields.")]
    public async Task<JsonNode> DescribeObject(
        [Description("The sObject API name, such as Account or Invoice__c.")] string sobject,
        [Description("Optional text to match against field API names and labels, case-insensitive.")] string? fieldNameContains = null,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var described = await schema.DescribeAsync(type, version, cancellationToken);
        var describe = described.Body;

        var allFields = (describe["fields"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(f => Matches(f, fieldNameContains))
            .ToList();

        var fields = new JsonArray();
        foreach (var f in allFields.Take(MaxDescribedFields))
        {
            fields.Add(DescribeField(f));
        }

        var children = new JsonArray();
        foreach (var c in (describe["childRelationships"] as JsonArray ?? []).OfType<JsonObject>()
                     .Where(c => !string.IsNullOrEmpty((string?)c["relationshipName"]))
                     .Take(MaxChildRelationships))
        {
            children.Add(new JsonObject
            {
                ["relationshipName"] = (string?)c["relationshipName"],
                ["childObject"] = (string?)c["childSObject"],
                ["field"] = (string?)c["field"]
            });
        }

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["schema"] = described.Meta(),
            ["name"] = (string?)describe["name"],
            ["label"] = (string?)describe["label"],
            ["custom"] = (bool?)describe["custom"] ?? false,
            ["queryable"] = (bool?)describe["queryable"] ?? false,
            ["createable"] = (bool?)describe["createable"] ?? false,
            ["updateable"] = (bool?)describe["updateable"] ?? false,
            ["deletable"] = (bool?)describe["deletable"] ?? false,
            ["totalFields"] = (describe["fields"] as JsonArray)?.Count ?? 0,
            ["matchingFields"] = allFields.Count,
            ["fields"] = fields,
            ["childRelationships"] = children
        };

        if (allFields.Count > MaxDescribedFields)
        {
            result["note"] = $"Only the first {MaxDescribedFields} matching fields are shown. Use fieldNameContains to narrow the list.";
        }

        return result;
    }

    [McpServerTool(Name = "get_limits", Title = "Get API Limits", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Returns the Salesforce org's API limits and how much of each remains, such as DailyApiRequests. Use it when calls fail with a limit error, or before a large batch of queries.")]
    public async Task<JsonNode> GetLimits(
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var response = await salesforce.GetAsync(salesforce.DataPath("limits", version), cancellationToken);

        var limits = new JsonArray();
        foreach (var (name, value) in (response as JsonObject) ?? [])
        {
            var max = (int?)value?["Max"] ?? 0;
            var remaining = (int?)value?["Remaining"] ?? 0;

            if (max > 0)
            {
                limits.Add(new JsonObject
                {
                    ["name"] = name,
                    ["max"] = max,
                    ["remaining"] = remaining,
                    ["used"] = max - remaining
                });
            }
        }

        return new JsonObject { ["limits"] = limits };
    }

    [McpServerTool(Name = "get_connection_info", Title = "Get Connection Info", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Shows which Salesforce org and which signed-in user this request runs as, and whether the org is a sandbox. Every other tool runs with that user's permissions, so use it to explain why a record or field is not visible to them.")]
    public async Task<JsonNode> GetConnectionInfo(CancellationToken cancellationToken = default)
    {
        var org = await salesforce.GetAsync(
            salesforce.DataPath("query?q=" + Uri.EscapeDataString(
                "SELECT Id, Name, OrganizationType, IsSandbox, InstanceName FROM Organization LIMIT 1")),
            cancellationToken);

        var current = salesforce.User;
        JsonNode? profile = null;

        if (current.UserId is { } userId)
        {
            var response = await salesforce.GetAsync(
                salesforce.DataPath("query?q=" + Uri.EscapeDataString(
                    $"SELECT Id, Name, Username, Profile.Name FROM User WHERE Id = '{SalesforceNames.RecordId(userId)}'")),
                cancellationToken);
            profile = RecordShaper.ShapeQuery(response, salesforce.Settings.InstanceUrl, 1, 10_000)["records"]?[0]?.DeepClone();
        }

        return new JsonObject
        {
            ["instanceUrl"] = salesforce.Settings.InstanceUrl,
            ["apiVersion"] = salesforce.Settings.ApiVersion,
            ["writeToolsEnabled"] = salesforce.Settings.EnableWriteTools,
            ["organization"] = RecordShaper.ShapeQuery(org, salesforce.Settings.InstanceUrl, 1, 10_000)["records"]?[0]?.DeepClone(),
            ["signedInUser"] = profile
        };
    }

    private JsonNode ShapeQuery(JsonNode? response, int maxRecords) =>
        RecordShaper.ShapeQuery(
            response, salesforce.Settings.InstanceUrl, Cap(maxRecords), salesforce.Settings.MaxResponseChars);

    private int Cap(int requested) => Math.Clamp(requested, 1, salesforce.Settings.MaxRecords);

    private static bool Matches(JsonObject o, string? text) =>
        string.IsNullOrWhiteSpace(text)
        || ((string?)o["name"])?.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) == true
        || ((string?)o["label"])?.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) == true;

    private static JsonObject DescribeField(JsonObject f)
    {
        var field = new JsonObject
        {
            ["name"] = (string?)f["name"],
            ["label"] = (string?)f["label"],
            ["type"] = (string?)f["type"]
        };

        if (((int?)f["length"] ?? 0) > 0)
        {
            field["length"] = (int?)f["length"];
        }

        field["required"] = (bool?)f["nillable"] == false && (bool?)f["defaultedOnCreate"] != true;
        field["custom"] = (bool?)f["custom"] ?? false;
        field["createable"] = (bool?)f["createable"] ?? false;
        field["updateable"] = (bool?)f["updateable"] ?? false;

        if (f["referenceTo"] is JsonArray { Count: > 0 } references)
        {
            field["referenceTo"] = references.DeepClone();
            field["relationshipName"] = (string?)f["relationshipName"];
        }

        if (f["picklistValues"] is JsonArray picklist)
        {
            var active = picklist.OfType<JsonObject>().Where(p => (bool?)p["active"] == true).ToList();
            if (active.Count > 0)
            {
                field["picklistValues"] = new JsonArray(
                    active.Take(MaxPicklistValues).Select(p => (JsonNode?)JsonValue.Create((string?)p["value"])).ToArray());

                if (active.Count > MaxPicklistValues)
                {
                    field["picklistValueCount"] = active.Count;
                }
            }
        }

        return field;
    }
}

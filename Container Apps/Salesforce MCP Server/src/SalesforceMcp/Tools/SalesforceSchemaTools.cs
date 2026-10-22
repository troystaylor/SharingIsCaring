using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tools;

/// <summary>
/// Read-only tools for understanding the shape of an object: record types, layouts, picklists, and how a
/// schema differs from an earlier snapshot or from another API version.
/// </summary>
/// <remarks>
/// Object descriptions come from a cache that revalidates with If-Modified-Since, so asking about the same
/// object repeatedly is cheap. Snapshots are held by the caller, not the server: replicas share no storage,
/// and a baseline kept in one replica's memory would vanish on restart or scale-in.
/// </remarks>
[McpServerToolType]
public sealed class SalesforceSchemaTools(
    SalesforceClient salesforce,
    SalesforceVersions versions,
    SchemaService schema)
{
    private const int MaxPicklistValues = 200;
    private const int MaxLayoutSections = 25;
    private const int MaxRelatedLists = 40;
    private const int MaxBaselineFields = 2000;
    private const int MaxRecentItems = 25;

    [McpServerTool(Name = "get_object_info", Title = "Get Object Info", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Returns an object's basic properties and the records the signed-in user viewed most recently, from the Salesforce /sobjects/{Object} resource. Use describe_object for fields. Recent items only reflect what that user opened, so a user who has not opened any records has none.")]
    public async Task<JsonNode> GetObjectInfo(
        [Description("The sObject API name, such as Account or Demo__c.")] string sobject,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var response = await salesforce.GetAsync(salesforce.DataPath($"sobjects/{type}", version), cancellationToken);

        var info = response?["objectDescribe"] as JsonObject ?? [];
        var (recent, truncated) = RecordShaper.Limit(
            response?["recentItems"] as JsonArray,
            salesforce.Settings.InstanceUrl,
            MaxRecentItems,
            salesforce.Settings.MaxResponseChars);

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["name"] = (string?)info["name"],
            ["label"] = (string?)info["label"],
            ["labelPlural"] = (string?)info["labelPlural"],
            ["keyPrefix"] = (string?)info["keyPrefix"],
            ["custom"] = (bool?)info["custom"] ?? false,
            ["queryable"] = (bool?)info["queryable"] ?? false,
            ["searchable"] = (bool?)info["searchable"] ?? false,
            ["createable"] = (bool?)info["createable"] ?? false,
            ["updateable"] = (bool?)info["updateable"] ?? false,
            ["deletable"] = (bool?)info["deletable"] ?? false,
            ["recentItems"] = recent
        };

        if (truncated)
        {
            result["truncated"] = true;
        }

        return result;
    }

    [McpServerTool(Name = "get_record_types", Title = "Get Record Types", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists an object's record types with their IDs and whether each is active, available to the signed-in user, and the default. Record types decide which picklist values and page layout apply, so call this before get_picklist_values or get_page_layout, and before creating a record on an object that has them.")]
    public async Task<JsonNode> GetRecordTypes(
        [Description("The sObject API name, such as Case or Demo__c.")] string sobject,
        [Description("When true, inactive record types are included. Defaults to false.")] bool includeInactive = false,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var described = await schema.DescribeAsync(type, version, cancellationToken);

        var all = (described.Body["recordTypeInfos"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var custom = all.Where(r => (bool?)r["master"] != true).ToList();

        var listed = new JsonArray();
        foreach (var r in custom.Where(r => includeInactive || (bool?)r["active"] != false))
        {
            listed.Add(new JsonObject
            {
                ["name"] = (string?)r["name"],
                ["developerName"] = (string?)r["developerName"],
                ["recordTypeId"] = (string?)r["recordTypeId"],
                ["active"] = (bool?)r["active"] ?? false,
                ["available"] = (bool?)r["available"] ?? false,
                ["default"] = (bool?)r["defaultRecordTypeMapping"] ?? false
            });
        }

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["schema"] = described.Meta(),
            ["sobject"] = (string?)described.Body["name"] ?? type,
            ["hasRecordTypes"] = custom.Count > 0,
            ["recordTypes"] = listed
        };

        if (custom.Count == 0)
        {
            result["note"] = "This object has no record types. One layout and one set of picklist values apply to every record.";
        }

        return result;
    }

    [McpServerTool(Name = "get_page_layout", Title = "Get Page Layout", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Shows the page layout for an object: the sections and fields a user sees when editing a record, which fields are required on the layout, which are read-only, and the related lists. Pass recordTypeId (from get_record_types) to get the layout for one record type. Without it, an object with several layouts returns the record-type-to-layout mapping instead.")]
    public async Task<JsonNode> GetPageLayout(
        [Description("The sObject API name, such as Opportunity or Demo__c.")] string sobject,
        [Description("Optional record type ID, from get_record_types.")] string? recordTypeId = null,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var rt = string.IsNullOrWhiteSpace(recordTypeId) ? null : SalesforceNames.RecordId(recordTypeId);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var resource = rt is null ? $"sobjects/{type}/describe/layouts" : $"sobjects/{type}/describe/layouts/{rt}";
        var response = await salesforce.GetAsync(salesforce.DataPath(resource, version), cancellationToken);

        var layouts = (response?["layouts"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var mappings = (response?["recordTypeMappings"] as JsonArray ?? []).OfType<JsonObject>().ToList();

        var result = new JsonObject { ["apiVersion"] = version, ["sobject"] = type };

        if (layouts.Count == 1 || (rt is not null && layouts.Count > 0))
        {
            result["layout"] = SummarizeLayout(layouts[0]);
        }
        else
        {
            result["layoutCount"] = layouts.Count;
            result["note"] = layouts.Count == 0
                ? "Salesforce returned no layout for this object."
                : "This object has several layouts. Pass recordTypeId to see the fields on one of them.";
        }

        result["recordTypeMappings"] = new JsonArray(mappings.Select(m => (JsonNode?)new JsonObject
        {
            ["name"] = (string?)m["name"],
            ["recordTypeId"] = (string?)m["recordTypeId"],
            ["layoutId"] = (string?)m["layoutId"],
            ["active"] = (bool?)m["active"] ?? false,
            ["available"] = (bool?)m["available"] ?? false,
            ["default"] = (bool?)m["defaultRecordTypeMapping"] ?? false
        }).ToArray());

        return result;
    }

    [McpServerTool(Name = "get_picklist_values", Title = "Get Picklist Values", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the allowed values of one picklist field, with each value's label, which is the default, and for a dependent picklist which values of the controlling field make it valid. Pass recordTypeId (from get_record_types) to get only the values that record type allows. Use it before creating or updating a record, because a value outside the list is rejected.")]
    public async Task<JsonNode> GetPicklistValues(
        [Description("The sObject API name, such as Case or Demo__c.")] string sobject,
        [Description("The picklist field API name, such as Status or Region__c.")] string field,
        [Description("Optional record type ID, from get_record_types.")] string? recordTypeId = null,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var fieldName = SalesforceNames.Field(field);
        var rt = string.IsNullOrWhiteSpace(recordTypeId) ? null : SalesforceNames.RecordId(recordTypeId);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var described = await schema.DescribeAsync(type, version, cancellationToken);
        var fields = (described.Body["fields"] as JsonArray ?? []).OfType<JsonObject>().ToList();

        var target = fields.FirstOrDefault(f =>
                string.Equals((string?)f["name"], fieldName, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException(
                $"{type} has no field named '{fieldName}'. Call describe_object with fieldNameContains to find it.");

        var kind = (string?)target["type"];
        if (kind is not ("picklist" or "multipicklist"))
        {
            throw new McpException($"{type}.{fieldName} is a {kind} field, not a picklist.");
        }

        HashSet<string>? allowed = null;
        string? restriction = null;

        if (rt is not null)
        {
            var layouts = await salesforce.GetAsync(
                salesforce.DataPath($"sobjects/{type}/describe/layouts/{rt}", version), cancellationToken);
            allowed = AllowedForRecordType(layouts, rt, (string?)target["name"] ?? fieldName, out restriction);
        }

        var controllerValues = ControllerValues(fields, (string?)target["controllerName"]);

        var values = new JsonArray();
        var total = 0;
        foreach (var v in (target["picklistValues"] as JsonArray ?? []).OfType<JsonObject>()
                     .Where(v => (bool?)v["active"] == true))
        {
            var value = (string?)v["value"] ?? string.Empty;
            if (allowed is not null && !allowed.Contains(value))
            {
                continue;
            }

            total++;
            if (values.Count >= MaxPicklistValues)
            {
                continue;
            }

            var entry = new JsonObject
            {
                ["value"] = value,
                ["label"] = (string?)v["label"],
                ["default"] = (bool?)v["defaultValue"] ?? false
            };

            if (controllerValues is not null)
            {
                entry["validWhen"] = new JsonArray(
                    DecodeValidFor((string?)v["validFor"], controllerValues)
                        .Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
            }

            values.Add(entry);
        }

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["schema"] = described.Meta(),
            ["sobject"] = type,
            ["field"] = (string?)target["name"],
            ["multiSelect"] = kind == "multipicklist",
            ["recordTypeId"] = rt,
            ["controllingField"] = (string?)target["controllerName"],
            ["totalValues"] = total,
            ["values"] = values
        };

        if (restriction is not null)
        {
            result["recordTypeNote"] = restriction;
        }

        if (total > MaxPicklistValues)
        {
            result["note"] = $"Only the first {MaxPicklistValues} values are shown.";
        }

        return result;
    }

    [McpServerTool(Name = "get_schema_snapshot", Title = "Get Schema Snapshot", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Captures a compact snapshot of an object's fields: one short signature per field (type, size, required, writable, lookups, picklist hash) and a fingerprint of the whole set. Keep the result. Later, pass its fields and fingerprint to compare_schema to learn what changed. The server does not store snapshots.")]
    public async Task<JsonNode> GetSchemaSnapshot(
        [Description("The sObject API name, such as Account or Demo__c.")] string sobject,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var described = await schema.DescribeAsync(type, version, cancellationToken);

        var signatures = SchemaDiff.Signatures(described.Body);
        return new JsonObject
        {
            ["sobject"] = (string?)described.Body["name"] ?? type,
            ["apiVersion"] = version,
            ["capturedAt"] = described.RetrievedAt.UtcDateTime.ToString("O"),
            ["fieldCount"] = signatures.Count,
            ["fingerprint"] = SchemaDiff.Fingerprint(signatures),
            ["fields"] = ToObject(signatures)
        };
    }

    [McpServerTool(Name = "compare_schema", Title = "Compare Schema to a Snapshot", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Compares an object's current fields with a snapshot you took earlier with get_schema_snapshot, and reports fields that were added, removed, or changed. Pass the snapshot's fields. Passing its fingerprint as well lets an unchanged object be confirmed immediately.")]
    public async Task<JsonNode> CompareSchema(
        [Description("The sObject API name, such as Account or Demo__c.")] string sobject,
        [Description("The fields object from an earlier get_schema_snapshot result: field API name to signature.")] Dictionary<string, string> baselineFields,
        [Description("Optional fingerprint from the same snapshot.")] string? baselineFingerprint = null,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        if (baselineFields is null || baselineFields.Count == 0)
        {
            throw new McpException("baselineFields must be the fields object from a get_schema_snapshot result.");
        }

        if (baselineFields.Count > MaxBaselineFields)
        {
            throw new McpException($"baselineFields can hold at most {MaxBaselineFields} fields.");
        }

        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var described = await schema.DescribeAsync(type, version, cancellationToken);
        var current = SchemaDiff.Signatures(described.Body);
        var fingerprint = SchemaDiff.Fingerprint(current);

        var result = SchemaDiff.Compare(baselineFields, current);
        result["sobject"] = (string?)described.Body["name"] ?? type;
        result["apiVersion"] = version;
        result["schema"] = described.Meta();
        result["fingerprint"] = fingerprint;
        result["fingerprintMatches"] = baselineFingerprint is null
            ? null
            : string.Equals(baselineFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);

        return result;
    }

    [McpServerTool(Name = "compare_api_versions", Title = "Compare Object Across API Versions", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Compares one object's fields as two API versions describe them, and reports fields that appear, disappear, or change between them. Use it to see what moving an integration from an older API version to a newer one would expose, or why a field is missing under an older version.")]
    public async Task<JsonNode> CompareApiVersions(
        [Description("The sObject API name, such as Account or Demo__c.")] string sobject,
        [Description("The version to compare from, such as v52.0.")] string fromApiVersion,
        [Description("The version to compare to. Leave it empty to use the server default.")] string? toApiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        if (string.IsNullOrWhiteSpace(fromApiVersion))
        {
            throw new McpException("fromApiVersion is required, for example v52.0.");
        }

        var from = await versions.ResolveAsync(fromApiVersion, cancellationToken);
        var to = await versions.ResolveAsync(toApiVersion, cancellationToken);
        if (from == to)
        {
            throw new McpException($"Both versions resolve to {from}. Pick two different versions.");
        }

        var before = await schema.DescribeAsync(type, from, cancellationToken);
        var after = await schema.DescribeAsync(type, to, cancellationToken);
        var beforeSignatures = SchemaDiff.Signatures(before.Body);
        var afterSignatures = SchemaDiff.Signatures(after.Body);

        var result = SchemaDiff.Compare(beforeSignatures, afterSignatures);
        result["sobject"] = (string?)after.Body["name"] ?? type;
        result["from"] = new JsonObject
        {
            ["apiVersion"] = from,
            ["fieldCount"] = beforeSignatures.Count,
            ["fingerprint"] = SchemaDiff.Fingerprint(beforeSignatures)
        };
        result["to"] = new JsonObject
        {
            ["apiVersion"] = to,
            ["fieldCount"] = afterSignatures.Count,
            ["fingerprint"] = SchemaDiff.Fingerprint(afterSignatures)
        };

        return result;
    }

    private static JsonObject ToObject(IEnumerable<KeyValuePair<string, string>> signatures)
    {
        var obj = new JsonObject();
        foreach (var (name, signature) in signatures)
        {
            obj[name] = signature;
        }

        return obj;
    }

    private static JsonObject SummarizeLayout(JsonObject layout)
    {
        var sections = (layout["editLayoutSections"] as JsonArray ?? layout["detailLayoutSections"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Take(MaxLayoutSections)
            .Select(section => (JsonNode?)new JsonObject
            {
                ["heading"] = (string?)section["heading"],
                ["fields"] = new JsonArray(LayoutFields(section).Select(f => (JsonNode?)f).ToArray())
            })
            .ToArray();

        var related = (layout["relatedLists"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Take(MaxRelatedLists)
            .Select(r => (JsonNode?)new JsonObject
            {
                ["label"] = (string?)r["label"],
                ["sobject"] = (string?)r["sobject"]
            })
            .ToArray();

        return new JsonObject
        {
            ["id"] = (string?)layout["id"],
            ["sections"] = new JsonArray(sections),
            ["relatedLists"] = new JsonArray(related)
        };
    }

    private static IEnumerable<JsonObject> LayoutFields(JsonObject section)
    {
        foreach (var row in (section["layoutRows"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var item in (row["layoutItems"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var component = (item["layoutComponents"] as JsonArray ?? []).OfType<JsonObject>()
                    .FirstOrDefault(c => (string?)c["type"] == "Field");

                if ((string?)component?["value"] is not { Length: > 0 } fieldName)
                {
                    continue;
                }

                yield return new JsonObject
                {
                    ["field"] = fieldName,
                    ["label"] = (string?)item["label"],
                    ["required"] = (bool?)item["required"] ?? false,
                    ["readOnly"] = (bool?)item["editableForNew"] != true && (bool?)item["editableForUpdate"] != true
                };
            }
        }
    }

    /// <summary>
    /// The values a record type allows for one picklist, from the layout resource. Returns null, with a note,
    /// when Salesforce lists no restriction for that field, which means every active value applies.
    /// </summary>
    private static HashSet<string>? AllowedForRecordType(
        JsonNode? layouts, string recordTypeId, string fieldName, out string? note)
    {
        var mapping = (layouts?["recordTypeMappings"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(m => SameId((string?)m["recordTypeId"], recordTypeId));

        if (mapping is null)
        {
            throw new McpException(
                $"Record type {recordTypeId} was not found on this object. Call get_record_types for valid IDs.");
        }

        var picklist = (mapping["picklistsForRecordType"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(p => string.Equals((string?)p["picklistName"], fieldName, StringComparison.OrdinalIgnoreCase));

        if (picklist is null)
        {
            note = "Salesforce lists no restriction for this field on this record type, so every active value is shown.";
            return null;
        }

        note = null;
        return (picklist["picklistValues"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(v => (string?)v["value"] ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Record type IDs appear as 15 or 18 characters. The first 15 are the identity; the rest is a checksum.
    private static bool SameId(string? a, string? b) =>
        a is { Length: >= 15 } && b is { Length: >= 15 }
        && string.Equals(a[..15], b[..15], StringComparison.Ordinal);

    private static List<string>? ControllerValues(List<JsonObject> fields, string? controllerName)
    {
        if (string.IsNullOrEmpty(controllerName))
        {
            return null;
        }

        var controller = fields.FirstOrDefault(f =>
            string.Equals((string?)f["name"], controllerName, StringComparison.OrdinalIgnoreCase));

        if (controller is null)
        {
            return null;
        }

        return (string?)controller["type"] == "boolean"
            ? ["Unchecked", "Checked"]
            : (controller["picklistValues"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(v => (string?)v["value"] ?? string.Empty)
                .ToList();
    }

    /// <summary>
    /// <c>validFor</c> is a base64 bit set. Bit N, counting from the high bit of the first byte, is set when
    /// the value is valid for the Nth value of the controlling field.
    /// </summary>
    public static IEnumerable<string> DecodeValidFor(string? validFor, IReadOnlyList<string> controllerValues)
    {
        if (string.IsNullOrEmpty(validFor))
        {
            yield break;
        }

        byte[] bits;
        try
        {
            bits = Convert.FromBase64String(validFor);
        }
        catch (FormatException)
        {
            yield break;
        }

        for (var i = 0; i < controllerValues.Count && i / 8 < bits.Length; i++)
        {
            if ((bits[i / 8] & (0x80 >> (i % 8))) != 0)
            {
                yield return controllerValues[i];
            }
        }
    }
}

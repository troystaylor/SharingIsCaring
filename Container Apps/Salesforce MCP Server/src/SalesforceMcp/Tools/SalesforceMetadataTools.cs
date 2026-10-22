using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tools;

/// <summary>
/// Read-only tools for Salesforce setup metadata: descriptions, help text, formulas, and validation rules.
/// They use the Tooling API, which is separate from the data API and is gated by its own permissions.
/// </summary>
/// <remarks>
/// Every query here is fixed. Only validated object names, field names, and IDs are placed into them, and
/// there is deliberately no tool that accepts a free-form Tooling API query: that API also exposes Apex
/// source and other setup that these tools have no reason to reach.
/// The signed-in user usually needs "View Setup and Configuration" for these to return anything. A failure on a
/// secondary lookup is reported as a note on the result instead of failing the whole call.
/// </remarks>
[McpServerToolType]
public sealed partial class SalesforceMetadataTools(
    SalesforceClient salesforce,
    SalesforceVersions versions,
    SchemaService schema)
{
    private const int MaxFields = 200;
    private const int MaxRules = 200;
    private const int MaxFormulaLookups = 25;

    [McpServerTool(Name = "get_object_metadata", Title = "Get Object Metadata", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Returns an object's setup metadata: its label, whether it is custom, its description from Setup, and field and record type counts. Use it to learn what an object is for. Descriptions exist only for custom objects, and only if an admin wrote one.")]
    public async Task<JsonNode> GetObjectMetadata(
        [Description("The sObject API name, such as Demo__c or Account.")] string sobject,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);
        var described = await schema.DescribeAsync(type, version, cancellationToken);
        var body = described.Body;

        var fields = (body["fields"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["schema"] = described.Meta(),
            ["sobject"] = (string?)body["name"] ?? type,
            ["label"] = (string?)body["label"],
            ["labelPlural"] = (string?)body["labelPlural"],
            ["keyPrefix"] = (string?)body["keyPrefix"],
            ["custom"] = (bool?)body["custom"] ?? false,
            ["fieldCount"] = fields.Count,
            ["customFieldCount"] = fields.Count(f => (bool?)f["custom"] == true),
            ["recordTypeCount"] = (body["recordTypeInfos"] as JsonArray ?? [])
                .OfType<JsonObject>().Count(r => (bool?)r["master"] != true)
        };

        if (!type.EndsWith("__c", StringComparison.Ordinal))
        {
            result["setupNote"] = "Setup descriptions are available for custom objects (API names ending in __c) only.";
            return result;
        }

        var (ns, developerName) = SplitCustomObjectName(type);
        var soql = "SELECT Id, DeveloperName, NamespacePrefix, Description, ManageableState FROM CustomObject "
            + $"WHERE DeveloperName = '{developerName}' AND NamespacePrefix "
            + (ns is null ? "= null" : $"= '{ns}'");

        try
        {
            var rows = await QueryAsync(soql, version, cancellationToken);
            if (rows.FirstOrDefault() is { } row)
            {
                result["description"] = (string?)row["Description"];
                result["namespacePrefix"] = (string?)row["NamespacePrefix"];
                result["manageableState"] = (string?)row["ManageableState"];
            }
            else
            {
                result["setupNote"] = "Setup has no custom object record for this name, or the signed-in user cannot see it.";
            }
        }
        catch (McpException ex)
        {
            result["setupNote"] = ex.Message;
        }

        return result;
    }

    [McpServerTool(Name = "list_custom_fields", Title = "List Field Definitions", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists an object's fields with the description and inline help text an admin wrote for each. Use it to learn what a field means, not just its type. Custom fields only by default. Use nameContains to find a field by name or label.")]
    public async Task<JsonNode> ListCustomFields(
        [Description("The sObject API name, such as Demo__c or Account.")] string sobject,
        [Description("Optional text to match against field API names and labels, case-insensitive.")] string? nameContains = null,
        [Description("When true, standard fields are included as well. Defaults to false.")] bool includeStandard = false,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var rows = await QueryAsync(
            "SELECT QualifiedApiName, Label, DataType, Description, InlineHelpText, IsCalculated, NamespacePrefix "
            + $"FROM FieldDefinition WHERE EntityDefinition.QualifiedApiName = '{type}'",
            version, cancellationToken);

        var matches = rows
            .Where(r => includeStandard || ((string?)r["QualifiedApiName"])?.EndsWith("__c", StringComparison.Ordinal) == true)
            .Where(r => string.IsNullOrWhiteSpace(nameContains)
                || ((string?)r["QualifiedApiName"])?.Contains(nameContains.Trim(), StringComparison.OrdinalIgnoreCase) == true
                || ((string?)r["Label"])?.Contains(nameContains.Trim(), StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(r => (string?)r["QualifiedApiName"], StringComparer.Ordinal)
            .ToList();

        var fields = new JsonArray(matches.Take(MaxFields).Select(r => (JsonNode?)new JsonObject
        {
            ["name"] = (string?)r["QualifiedApiName"],
            ["label"] = (string?)r["Label"],
            ["type"] = (string?)r["DataType"],
            ["description"] = (string?)r["Description"],
            ["helpText"] = (string?)r["InlineHelpText"],
            ["calculated"] = (bool?)r["IsCalculated"] ?? false,
            ["namespacePrefix"] = (string?)r["NamespacePrefix"]
        }).ToArray());

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["sobject"] = type,
            ["totalMatches"] = matches.Count,
            ["returned"] = fields.Count,
            ["fields"] = fields
        };

        if (matches.Count > MaxFields)
        {
            result["note"] = $"Only the first {MaxFields} fields are shown. Use nameContains to narrow the list.";
        }

        return result;
    }

    [McpServerTool(Name = "get_field_metadata", Title = "Get Field Metadata", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Returns one field's setup metadata: label, type, description, inline help text, and for a custom field its formula, default value, and whether it is required, unique, or an external ID. Use it to explain how a formula field is calculated or what a field is meant to hold.")]
    public async Task<JsonNode> GetFieldMetadata(
        [Description("The sObject API name, such as Demo__c or Account.")] string sobject,
        [Description("The field API name, such as Region__c or Industry.")] string field,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var fieldName = SalesforceNames.Field(field);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var rows = await QueryAsync(
            "SELECT QualifiedApiName, Label, DataType, Description, InlineHelpText, IsCalculated, NamespacePrefix, DurableId "
            + $"FROM FieldDefinition WHERE EntityDefinition.QualifiedApiName = '{type}' AND QualifiedApiName = '{fieldName}'",
            version, cancellationToken);

        var row = rows.FirstOrDefault()
            ?? throw new McpException(
                $"{type} has no field named '{fieldName}', or the signed-in user cannot see it. Call describe_object with fieldNameContains to find it.");

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["sobject"] = type,
            ["name"] = (string?)row["QualifiedApiName"],
            ["label"] = (string?)row["Label"],
            ["type"] = (string?)row["DataType"],
            ["description"] = (string?)row["Description"],
            ["helpText"] = (string?)row["InlineHelpText"],
            ["calculated"] = (bool?)row["IsCalculated"] ?? false,
            ["namespacePrefix"] = (string?)row["NamespacePrefix"]
        };

        // The durable ID is "{objectId}.{fieldId}". Only custom fields have a field ID in the 00N range,
        // and only those have a definition (formula, default value) to read.
        var fieldId = (string?)row["DurableId"] is { } durable ? durable[(durable.LastIndexOf('.') + 1)..] : null;
        if (fieldId is null || !CustomFieldId().IsMatch(fieldId))
        {
            result["definitionNote"] = "Standard fields have no formula or default definition in setup.";
            return result;
        }

        try
        {
            var definitions = await QueryAsync(
                $"SELECT Id, FullName, Metadata FROM CustomField WHERE Id = '{SalesforceNames.RecordId(fieldId)}'",
                version, cancellationToken);

            if (definitions.FirstOrDefault()?["Metadata"] is JsonObject metadata)
            {
                result["definition"] = new JsonObject
                {
                    ["formula"] = (string?)metadata["formula"],
                    ["formulaTreatBlanksAs"] = (string?)metadata["formulaTreatBlanksAs"],
                    ["defaultValue"] = (string?)metadata["defaultValue"],
                    ["required"] = (bool?)metadata["required"],
                    ["unique"] = (bool?)metadata["unique"],
                    ["externalId"] = (bool?)metadata["externalId"],
                    ["trackHistory"] = (bool?)metadata["trackHistory"],
                    ["length"] = (int?)metadata["length"],
                    ["precision"] = (int?)metadata["precision"],
                    ["scale"] = (int?)metadata["scale"],
                    ["summaryOperation"] = (string?)metadata["summaryOperation"],
                    ["summarizedField"] = (string?)metadata["summarizedField"],
                    ["summaryForeignKey"] = (string?)metadata["summaryForeignKey"]
                };
            }
            else
            {
                result["definitionNote"] = "Setup returned no definition for this field.";
            }
        }
        catch (McpException ex)
        {
            result["definitionNote"] = ex.Message;
        }

        return result;
    }

    [McpServerTool(Name = "list_validation_rules", Title = "List Validation Rules", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists an object's validation rules: name, whether it is active, its description, the error message users see, and the field the error shows on. Use it to explain why a create or update was rejected. Set includeFormulas to true to also get each rule's formula, which costs one extra API call per rule, up to 25.")]
    public async Task<JsonNode> ListValidationRules(
        [Description("The sObject API name, such as Opportunity or Demo__c.")] string sobject,
        [Description("When true, each rule's formula is included. Defaults to false.")] bool includeFormulas = false,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var rows = await QueryAsync(
            "SELECT Id, ValidationName, Active, Description, ErrorDisplayField, ErrorMessage, NamespacePrefix "
            + $"FROM ValidationRule WHERE EntityDefinition.QualifiedApiName = '{type}'",
            version, cancellationToken);

        var rules = new JsonArray();
        var formulaLookups = 0;

        foreach (var row in rows.OrderBy(r => (string?)r["ValidationName"], StringComparer.Ordinal).Take(MaxRules))
        {
            var rule = new JsonObject
            {
                ["name"] = (string?)row["ValidationName"],
                ["active"] = (bool?)row["Active"] ?? false,
                ["description"] = (string?)row["Description"],
                ["errorMessage"] = (string?)row["ErrorMessage"],
                ["errorDisplayField"] = (string?)row["ErrorDisplayField"],
                ["namespacePrefix"] = (string?)row["NamespacePrefix"]
            };

            if (includeFormulas && formulaLookups < MaxFormulaLookups && (string?)row["Id"] is { } id)
            {
                formulaLookups++;
                try
                {
                    var detail = await QueryAsync(
                        $"SELECT Id, Metadata FROM ValidationRule WHERE Id = '{SalesforceNames.RecordId(id)}'",
                        version, cancellationToken);
                    rule["formula"] = (string?)detail.FirstOrDefault()?["Metadata"]?["errorConditionFormula"];
                }
                catch (McpException ex)
                {
                    rule["formulaNote"] = ex.Message;
                }
            }

            rules.Add(rule);
        }

        var result = new JsonObject
        {
            ["apiVersion"] = version,
            ["sobject"] = type,
            ["totalRules"] = rows.Count,
            ["returned"] = rules.Count,
            ["rules"] = rules
        };

        if (includeFormulas && rows.Count > MaxFormulaLookups)
        {
            result["note"] = $"Formulas were fetched for the first {MaxFormulaLookups} rules only.";
        }

        return result;
    }

    private async Task<List<JsonObject>> QueryAsync(string soql, string version, CancellationToken cancellationToken)
    {
        var response = await salesforce.GetAsync(
            salesforce.DataPath($"tooling/query?q={Uri.EscapeDataString(soql)}", version), cancellationToken);

        return (response?["records"] as JsonArray ?? []).OfType<JsonObject>().ToList();
    }

    /// <summary><c>Demo__c</c> is (null, Demo). <c>acme__Demo__c</c> is (acme, Demo).</summary>
    public static (string? Namespace, string DeveloperName) SplitCustomObjectName(string apiName)
    {
        var core = apiName[..^"__c".Length];
        var separator = core.IndexOf("__", StringComparison.Ordinal);
        return separator > 0 ? (core[..separator], core[(separator + 2)..]) : (null, core);
    }

    [GeneratedRegex(@"^00N[A-Za-z0-9]{12}([A-Za-z0-9]{3})?$")]
    private static partial Regex CustomFieldId();
}

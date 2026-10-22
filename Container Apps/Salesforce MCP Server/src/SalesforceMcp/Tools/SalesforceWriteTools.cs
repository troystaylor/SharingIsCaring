using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tools;

/// <summary>
/// Tools that change Salesforce data. They are only registered when <c>Salesforce:EnableWriteTools</c> is
/// true, so a default deployment cannot modify anything. Salesforce permissions on the signed-in user still
/// apply: a write the user is not allowed to make fails at Salesforce regardless of this setting.
/// </summary>
[McpServerToolType]
public sealed class SalesforceWriteTools(SalesforceClient salesforce, SalesforceVersions versions)
{
    [McpServerTool(Name = "create_record", Title = "Create Record", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Creates one Salesforce record and returns its new ID and a recordUrl. Call describe_object first to get the exact field API names and which fields are required. Do not call this twice for the same record: it creates a duplicate each time.")]
    public async Task<JsonNode> CreateRecord(
        [Description("The sObject API name, such as Account, Contact, or Invoice__c.")] string sobject,
        [Description("Field values keyed by field API name, such as {\"Name\":\"Acme\",\"Industry\":\"Retail\"}.")] Dictionary<string, JsonElement> fields,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var body = ToBody(fields);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        var response = await salesforce.SendAsync(
            HttpMethod.Post, salesforce.DataPath($"sobjects/{type}", version), body, cancellationToken);

        var id = (string?)response?["id"];
        return new JsonObject
        {
            ["id"] = id,
            ["success"] = (bool?)response?["success"] ?? id is not null,
            ["recordUrl"] = id is null ? null : $"{salesforce.Settings.InstanceUrl.TrimEnd('/')}/{id}"
        };
    }

    [McpServerTool(Name = "update_record", Title = "Update Record", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Changes fields on one existing Salesforce record. Only the fields you pass are changed, and each one overwrites its current value. Read the record with get_record first if you need the old value.")]
    public async Task<JsonNode> UpdateRecord(
        [Description("The sObject API name, such as Account or Opportunity.")] string sobject,
        [Description("The 15 or 18 character record ID.")] string id,
        [Description("New field values keyed by field API name, such as {\"StageName\":\"Closed Won\"}.")] Dictionary<string, JsonElement> fields,
        [Description(ToolText.ApiVersion)] string? apiVersion = null,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var recordId = SalesforceNames.RecordId(id);
        var body = ToBody(fields);
        var version = await versions.ResolveAsync(apiVersion, cancellationToken);

        await salesforce.SendAsync(
            HttpMethod.Patch, salesforce.DataPath($"sobjects/{type}/{recordId}", version), body, cancellationToken);

        return new JsonObject
        {
            ["id"] = recordId,
            ["updated"] = true,
            ["fieldsChanged"] = new JsonArray(body.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()),
            ["recordUrl"] = $"{salesforce.Settings.InstanceUrl.TrimEnd('/')}/{recordId}"
        };
    }

    [McpServerTool(Name = "delete_record", Title = "Delete Record", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Deletes one Salesforce record. Salesforce moves most records to the Recycle Bin, where an admin can restore them for 15 days, but related records can be deleted with it. Confirm with the user before calling this.")]
    public async Task<JsonNode> DeleteRecord(
        [Description("The sObject API name, such as Contact or Case.")] string sobject,
        [Description("The 15 or 18 character record ID.")] string id,
        CancellationToken cancellationToken = default)
    {
        var type = SalesforceNames.SObject(sobject);
        var recordId = SalesforceNames.RecordId(id);

        await salesforce.SendAsync(
            HttpMethod.Delete, salesforce.DataPath($"sobjects/{type}/{recordId}"), null, cancellationToken);

        return new JsonObject { ["id"] = recordId, ["deleted"] = true };
    }

    private static JsonObject ToBody(Dictionary<string, JsonElement>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            throw new McpException("fields must contain at least one field.");
        }

        if (fields.Count > SalesforceNames.MaxFieldCount)
        {
            throw new McpException($"Send at most {SalesforceNames.MaxFieldCount} fields.");
        }

        var body = new JsonObject();
        foreach (var (name, value) in fields)
        {
            if (string.Equals(name, "attributes", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException("'attributes' is not a field. Send only field values keyed by field API name.");
            }

            body[SalesforceNames.Field(name)] = JsonNode.Parse(value.GetRawText());
        }

        return body;
    }
}

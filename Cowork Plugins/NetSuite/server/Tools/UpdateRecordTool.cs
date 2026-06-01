using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class UpdateRecordTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "update_record",
        Description = "Partially update a NetSuite record. Only fields supplied are changed (PATCH semantics).",
        Annotations = new ToolAnnotations(
            Title: "Update NetSuite record",
            ReadOnlyHint: false,
            DestructiveHint: true,
            IdempotentHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id (e.g., customer, salesorder, invoice)."),
                ["recordId"] = Prop("string", "Internal id of the record to update."),
                ["fields"] = SchemaOpenObject(new JsonObject(), required: Array.Empty<string>()),
            },
            required: new[] { "recordType", "recordId", "fields" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var fields = RequireObject(args, "fields");
            var resp = await ns.UpdateRecordAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                fields,
                ct);
            return ContentResult(resp);
        },
    };
}

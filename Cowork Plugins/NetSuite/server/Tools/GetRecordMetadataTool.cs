using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class GetRecordMetadataTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_record_metadata",
        Description = "Get the field schema for a NetSuite record type. Useful before create/update to understand required and allowed fields.",
        Annotations = new ToolAnnotations(
            Title: "Get NetSuite record metadata",
            ReadOnlyHint: true,
            IdempotentHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id (e.g., customer, salesorder, invoice)."),
            },
            required: "recordType"),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.GetRecordMetadataAsync(RequireString(args, "recordType"), ct);
            return ContentResult(resp);
        },
    };
}

using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class CreateRecordTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "create_record",
        Description = "Create a new NetSuite record. Field shape varies by record type — call get_record_metadata first if unsure. Returns the created record location and id.",
        Annotations = new ToolAnnotations(
            Title: "Create NetSuite record",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id (e.g., customer, salesorder, invoice)."),
                ["fields"] = SchemaOpenObject(new JsonObject(), required: Array.Empty<string>()),
            },
            required: new[] { "recordType", "fields" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var fields = RequireObject(args, "fields");
            var resp = await ns.CreateRecordAsync(RequireString(args, "recordType"), fields, ct);
            return ContentResult(resp);
        },
    };
}

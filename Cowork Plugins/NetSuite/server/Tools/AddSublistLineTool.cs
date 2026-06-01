using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class AddSublistLineTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "add_sublist_line",
        Description = "Add a line to a sublist on a NetSuite record (e.g., add an item line to a sales order).",
        Annotations = new ToolAnnotations(
            Title: "Add NetSuite sublist line",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id of the parent record."),
                ["recordId"] = Prop("string", "Internal id of the parent record."),
                ["sublistId"] = Prop("string", "Sublist id (e.g., item, addressBook)."),
                ["fields"] = SchemaOpenObject(new JsonObject(), required: Array.Empty<string>()),
            },
            required: new[] { "recordType", "recordId", "sublistId", "fields" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var fields = RequireObject(args, "fields");
            var resp = await ns.AddSublistLineAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                RequireString(args, "sublistId"),
                fields,
                ct);
            return ContentResult(resp);
        },
    };
}

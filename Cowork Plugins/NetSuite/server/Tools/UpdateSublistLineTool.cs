using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class UpdateSublistLineTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "update_sublist_line",
        Description = "Update a line in a sublist on a NetSuite record (PATCH semantics).",
        Annotations = new ToolAnnotations(
            Title: "Update NetSuite sublist line",
            ReadOnlyHint: false,
            DestructiveHint: true,
            IdempotentHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id of the parent record."),
                ["recordId"] = Prop("string", "Internal id of the parent record."),
                ["sublistId"] = Prop("string", "Sublist id (e.g., item, addressBook)."),
                ["lineId"] = Prop("string", "Line id within the sublist."),
                ["fields"] = SchemaOpenObject(new JsonObject(), required: Array.Empty<string>()),
            },
            required: new[] { "recordType", "recordId", "sublistId", "lineId", "fields" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var fields = RequireObject(args, "fields");
            var resp = await ns.UpdateSublistLineAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                RequireString(args, "sublistId"),
                RequireString(args, "lineId"),
                fields,
                ct);
            return ContentResult(resp);
        },
    };
}

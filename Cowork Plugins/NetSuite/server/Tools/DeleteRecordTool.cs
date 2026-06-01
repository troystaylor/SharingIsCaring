using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class DeleteRecordTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "delete_record",
        Description = "Permanently delete a NetSuite record. Irreversible. Confirm intent before invoking.",
        Annotations = new ToolAnnotations(
            Title: "Delete NetSuite record",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id."),
                ["recordId"] = Prop("string", "Internal id of the record to delete."),
            },
            required: new[] { "recordType", "recordId" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.DeleteRecordAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                ct);
            return ContentResult(resp);
        },
    };
}

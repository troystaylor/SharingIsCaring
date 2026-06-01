using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class DeleteSublistLineTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "delete_sublist_line",
        Description = "Remove a line from a sublist on a NetSuite record.",
        Annotations = new ToolAnnotations(
            Title: "Delete NetSuite sublist line",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id of the parent record."),
                ["recordId"] = Prop("string", "Internal id of the parent record."),
                ["sublistId"] = Prop("string", "Sublist id."),
                ["lineId"] = Prop("string", "Line id within the sublist."),
            },
            required: new[] { "recordType", "recordId", "sublistId", "lineId" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.DeleteSublistLineAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                RequireString(args, "sublistId"),
                RequireString(args, "lineId"),
                ct);
            return ContentResult(resp);
        },
    };
}

using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class GetSublistTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_sublist",
        Description = "Get a sublist (line items) on a NetSuite record. Common sublist ids: item (transaction lines), addressBook (entity addresses), contactRoles (customer contacts).",
        Annotations = new ToolAnnotations(
            Title: "Get NetSuite sublist",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id (e.g., salesorder, invoice, customer)."),
                ["recordId"] = Prop("string", "Internal id of the parent record."),
                ["sublistId"] = Prop("string", "Sublist id (e.g., item, addressBook, contactRoles)."),
            },
            required: new[] { "recordType", "recordId", "sublistId" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.GetSublistAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                RequireString(args, "sublistId"),
                ct);
            return ContentResult(resp);
        },
    };
}

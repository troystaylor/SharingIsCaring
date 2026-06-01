using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class GetRecordTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_record",
        Description = "Get a single NetSuite record by id.",
        Annotations = new ToolAnnotations(
            Title: "Get NetSuite record",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id (e.g., customer, salesorder, invoice)."),
                ["recordId"] = Prop("string", "Internal id of the record."),
                ["fields"] = Prop("string", "Optional comma-separated list of fields to return."),
                ["expandSubResources"] = Prop("boolean", "Expand subresources inline (default false)."),
            },
            required: new[] { "recordType", "recordId" }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.GetRecordAsync(
                RequireString(args, "recordType"),
                RequireString(args, "recordId"),
                OptString(args, "fields"),
                OptBool(args, "expandSubResources"),
                ct);
            return ContentResult(resp);
        },
    };
}

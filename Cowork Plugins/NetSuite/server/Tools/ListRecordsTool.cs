using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class ListRecordsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_records",
        Description = "List instances of a NetSuite record type with optional collection filtering. Returns HATEOAS-style id/link entries unless 'fields' is supplied. Filter syntax: field OPERATOR value (e.g., email START_WITH \"barbara\"). Operators: CONTAIN, IS, START_WITH, END_WITH, EQUAL, GREATER, LESS, BETWEEN, ANY_OF, AFTER, BEFORE, ON. Join with AND/OR. For rich queries prefer run_suiteql.",
        Annotations = new ToolAnnotations(
            Title: "List NetSuite records",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["recordType"] = Prop("string", "Record type id (e.g., customer, salesorder, invoice, vendor, item)."),
                ["filter"] = Prop("string", "Optional collection filter expression."),
                ["fields"] = Prop("string", "Optional comma-separated list of fields to return."),
                ["limit"] = Prop("integer", "Max rows per page (1-1000, default 100)."),
                ["offset"] = Prop("integer", "Pagination offset (default 0)."),
                ["expandSubResources"] = Prop("boolean", "Expand subresources inline (default false)."),
            },
            required: "recordType"),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var resp = await ns.ListRecordsAsync(
                RequireString(args, "recordType"),
                OptString(args, "filter"),
                OptString(args, "fields"),
                OptInt(args, "limit"),
                OptInt(args, "offset"),
                OptBool(args, "expandSubResources"),
                ct);
            return ContentResult(resp);
        },
    };
}

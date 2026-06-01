using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class RunSuiteQLTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "run_suiteql",
        Description = "Execute a SuiteQL query against NetSuite. SuiteQL is SQL-like and supports SELECT, JOIN, WHERE, GROUP BY, ORDER BY, aggregates, and subqueries. Common tables: transaction, customer, vendor, employee, item, contact, salesorder, purchaseorder, invoice, account, department, location, subsidiary.",
        Annotations = new ToolAnnotations(
            Title: "Run SuiteQL",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["q"] = Prop("string", "SuiteQL query string (e.g., SELECT id, companyname, email FROM customer WHERE isinactive = 'F' ORDER BY companyname)."),
                ["limit"] = Prop("integer", "Max rows per page (1-1000, default 100)."),
                ["offset"] = Prop("integer", "Pagination offset (default 0)."),
            },
            required: "q"),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var q = RequireString(args, "q");
            var limit = OptInt(args, "limit");
            var offset = OptInt(args, "offset");
            var resp = await ns.SuiteQLAsync(q, limit, offset, ct);
            return ContentResult(resp);
        },
    };
}

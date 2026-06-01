using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class GetOpenSalesOrdersTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_open_sales_orders",
        Description = "Return open sales orders, optionally filtered by customer id and minimum total. Wraps a SuiteQL query against the transaction table.",
        Annotations = new ToolAnnotations(
            Title: "Get open sales orders",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["customerId"] = Prop("string", "Optional NetSuite customer internal id to filter by."),
                ["minTotal"] = Prop("number", "Optional minimum order total to include."),
                ["limit"] = Prop("integer", "Max rows (1-200, default 25)."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var limit = Math.Clamp(OptInt(args, "limit") ?? 25, 1, 200);
            var customerId = OptString(args, "customerId");

            var clauses = new List<string>
            {
                "type = 'SalesOrd'",
                "status NOT IN ('SalesOrd:H', 'SalesOrd:G', 'SalesOrd:C')",
            };
            if (!string.IsNullOrWhiteSpace(customerId))
            {
                if (!int.TryParse(customerId, out var custId))
                    throw new ArgumentException("customerId must be a numeric internal id");
                clauses.Add($"entity = {custId}");
            }
            if (args["minTotal"] is JsonValue mtVal && mtVal.TryGetValue<double>(out var minTotal))
            {
                clauses.Add($"foreigntotal >= {minTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            var soql = $@"SELECT id, tranid, trandate, entity, status, foreigntotal, currency, lastmodifieddate
                          FROM transaction
                          WHERE {string.Join(" AND ", clauses)}
                          ORDER BY trandate DESC";

            var resp = await ns.SuiteQLAsync(soql, limit, 0, ct);
            return ContentResult(resp);
        },
    };
}

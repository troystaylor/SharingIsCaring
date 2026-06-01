using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class GetOpenInvoicesTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_open_invoices",
        Description = "Return open (unpaid) customer invoices with amount-remaining and aging info, optionally filtered by customer id or aging bucket. Wraps a SuiteQL query against the transaction table.",
        Annotations = new ToolAnnotations(
            Title: "Get open invoices",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["customerId"] = Prop("string", "Optional NetSuite customer internal id to filter by."),
                ["minDaysOverdue"] = Prop("integer", "Optional minimum days past due date (e.g., 30 for 30+ day aging)."),
                ["limit"] = Prop("integer", "Max rows (1-200, default 50)."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var limit = Math.Clamp(OptInt(args, "limit") ?? 50, 1, 200);
            var customerId = OptString(args, "customerId");
            var minDays = OptInt(args, "minDaysOverdue");

            var clauses = new List<string>
            {
                "type = 'CustInvc'",
                "status NOT IN ('CustInvc:B', 'CustInvc:C')",
            };
            if (!string.IsNullOrWhiteSpace(customerId))
            {
                if (!int.TryParse(customerId, out var custId))
                    throw new ArgumentException("customerId must be a numeric internal id");
                clauses.Add($"entity = {custId}");
            }
            if (minDays.HasValue)
            {
                clauses.Add($"duedate <= SYSDATE - {minDays.Value}");
            }

            var soql = $@"SELECT id, tranid, trandate, duedate, entity, status, foreigntotal, foreignamountunpaid, currency,
                                 CASE WHEN duedate < SYSDATE THEN TRUNC(SYSDATE - duedate) ELSE 0 END AS daysoverdue
                          FROM transaction
                          WHERE {string.Join(" AND ", clauses)}
                          ORDER BY duedate ASC";

            var resp = await ns.SuiteQLAsync(soql, limit, 0, ct);
            return ContentResult(resp);
        },
    };
}

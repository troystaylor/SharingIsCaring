using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NetSuiteCoworkMcp.NetSuite;
using static NetSuiteCoworkMcp.Tools.ToolHelpers;

namespace NetSuiteCoworkMcp.Tools;

internal static class SearchVendorsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "search_vendors",
        Description = "Search NetSuite vendors by company name or email. Returns id, company name, email, phone, and inactive flag. Wraps a SuiteQL query against the vendor table.",
        Annotations = new ToolAnnotations(
            Title: "Search NetSuite vendors",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["query"] = Prop("string", "Search term matched against companyname or email (case-insensitive contains)."),
                ["includeInactive"] = Prop("boolean", "Include inactive vendors (default false)."),
                ["limit"] = Prop("integer", "Max rows (1-200, default 25)."),
            },
            required: "query"),
        Invoke = async (sp, args, ct) =>
        {
            var ns = sp.GetRequiredService<INetSuiteClient>();
            var q = EscapeSuiteQLLiteral(RequireString(args, "query"));
            var limit = Math.Clamp(OptInt(args, "limit") ?? 25, 1, 200);
            var includeInactive = OptBool(args, "includeInactive");
            var inactiveClause = includeInactive ? string.Empty : "isinactive = 'F' AND ";

            var soql = $@"SELECT id, entityid, companyname, email, phone, isinactive, lastmodifieddate
                          FROM vendor
                          WHERE {inactiveClause}(LOWER(companyname) LIKE '%{q.ToLowerInvariant()}%' OR LOWER(email) LIKE '%{q.ToLowerInvariant()}%')
                          ORDER BY lastmodifieddate DESC";

            var resp = await ns.SuiteQLAsync(soql, limit, 0, ct);
            return ContentResult(resp);
        },
    };
}

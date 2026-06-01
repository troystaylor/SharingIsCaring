using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class SearchAccountsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "search_accounts",
        Description = "Search Salesforce accounts by name and return key account details.",
        Annotations = new ToolAnnotations(
            Title: "Search Salesforce accounts",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["query"] = Prop("string", "Account name search term."),
                ["limit"] = Prop("integer", "Max rows to return (1-200, default 20)."),
            },
            required: "query"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var query = EscapeSoqlLiteral(RequireString(args, "query"));
            var limit = Math.Clamp(OptInt(args, "limit") ?? 20, 1, 200);
            var soql = $"SELECT Id, Name, Type, Industry, Owner.Name, LastModifiedDate FROM Account WHERE Name LIKE '%{query}%' ORDER BY LastModifiedDate DESC LIMIT {limit}";
            var resp = await sf.QueryAsync(soql, ct);
            return ContentResult(resp);
        },
    };
}

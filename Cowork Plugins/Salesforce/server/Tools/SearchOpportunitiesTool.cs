using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class SearchOpportunitiesTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "search_opportunities",
        Description = "Search Salesforce opportunities by name and optional account/stage filters.",
        Annotations = new ToolAnnotations(
            Title: "Search Salesforce opportunities",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["query"] = Prop("string", "Opportunity name search term."),
                ["accountId"] = Prop("string", "Optional Account Id filter."),
                ["stageName"] = Prop("string", "Optional StageName filter."),
                ["limit"] = Prop("integer", "Max rows to return (1-200, default 20)."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var limit = Math.Clamp(OptInt(args, "limit") ?? 20, 1, 200);
            var where = new List<string>();

            var query = OptString(args, "query");
            if (!string.IsNullOrWhiteSpace(query))
            {
                where.Add($"Name LIKE '%{EscapeSoqlLiteral(query)}%'");
            }

            var accountId = OptString(args, "accountId");
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                where.Add($"AccountId = '{EscapeSoqlLiteral(accountId)}'");
            }

            var stageName = OptString(args, "stageName");
            if (!string.IsNullOrWhiteSpace(stageName))
            {
                where.Add($"StageName = '{EscapeSoqlLiteral(stageName)}'");
            }

            var whereClause = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
            var soql = "SELECT Id, Name, StageName, Amount, CloseDate, Probability, AccountId, OwnerId, LastModifiedDate FROM Opportunity"
                + whereClause
                + $" ORDER BY LastModifiedDate DESC LIMIT {limit}";

            var resp = await sf.QueryAsync(soql, ct);
            return ContentResult(resp);
        },
    };
}

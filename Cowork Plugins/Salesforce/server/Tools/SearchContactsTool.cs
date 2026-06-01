using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class SearchContactsTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "search_contacts",
        Description = "Search Salesforce contacts by name, email, or related account.",
        Annotations = new ToolAnnotations(
            Title: "Search Salesforce contacts",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["query"] = Prop("string", "Search term matched against contact name or email."),
                ["accountId"] = Prop("string", "Optional Account Id filter."),
                ["limit"] = Prop("integer", "Max rows to return (1-200, default 20)."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var limit = Math.Clamp(OptInt(args, "limit") ?? 20, 1, 200);
            var query = OptString(args, "query");
            var accountId = OptString(args, "accountId");

            var clauses = new List<string>();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var escaped = EscapeSoqlLiteral(query);
                clauses.Add($"(Name LIKE '%{escaped}%' OR Email LIKE '%{escaped}%')");
            }
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                var escapedId = EscapeSoqlLiteral(accountId);
                clauses.Add($"AccountId = '{escapedId}'");
            }
            if (clauses.Count == 0)
            {
                throw new ArgumentException("at least one of 'query' or 'accountId' is required");
            }

            var where = string.Join(" AND ", clauses);
            var soql = $"SELECT Id, FirstName, LastName, Title, Email, Phone, AccountId, Account.Name, Owner.Name, LastModifiedDate FROM Contact WHERE {where} ORDER BY LastModifiedDate DESC LIMIT {limit}";
            var resp = await sf.QueryAsync(soql, ct);
            return ContentResult(resp);
        },
    };
}

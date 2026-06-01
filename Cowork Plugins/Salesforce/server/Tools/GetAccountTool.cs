using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class GetAccountTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_account",
        Description = "Get a Salesforce account by ID with common briefing fields.",
        Annotations = new ToolAnnotations(
            Title: "Get Salesforce account",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["accountId"] = Prop("string", "Salesforce Account Id."),
                ["fields"] = Prop("string", "Optional CSV field list override."),
            },
            required: "accountId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var accountId = RequireString(args, "accountId");
            var fields = OptString(args, "fields")
                ?? "Id,Name,Type,Industry,AnnualRevenue,BillingCity,BillingCountry,OwnerId,LastModifiedDate";
            var resp = await sf.GetSObjectAsync("Account", accountId, fields, ct);
            return ContentResult(resp);
        },
    };
}

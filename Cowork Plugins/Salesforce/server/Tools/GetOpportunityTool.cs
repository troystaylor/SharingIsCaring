using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class GetOpportunityTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_opportunity",
        Description = "Get a Salesforce opportunity by ID with health and forecast fields.",
        Annotations = new ToolAnnotations(
            Title: "Get Salesforce opportunity",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["opportunityId"] = Prop("string", "Salesforce Opportunity Id."),
                ["fields"] = Prop("string", "Optional CSV field list override."),
            },
            required: "opportunityId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var oppId = RequireString(args, "opportunityId");
            var fields = OptString(args, "fields")
                ?? "Id,Name,AccountId,StageName,Amount,CloseDate,Probability,NextStep,ForecastCategoryName,OwnerId,LastModifiedDate";
            var resp = await sf.GetSObjectAsync("Opportunity", oppId, fields, ct);
            return ContentResult(resp);
        },
    };
}

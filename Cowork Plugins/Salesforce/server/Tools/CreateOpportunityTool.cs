using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class CreateOpportunityTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "create_opportunity",
        Description = "Create a new Salesforce opportunity linked to an account.",
        Annotations = new ToolAnnotations(
            Title: "Create Salesforce opportunity",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["name"] = Prop("string", "Opportunity name."),
                ["accountId"] = Prop("string", "Related Account Id."),
                ["stageName"] = Prop("string", "Stage name (e.g., Prospecting, Qualification, Proposal/Price Quote)."),
                ["closeDate"] = Prop("string", "Expected close date in YYYY-MM-DD format."),
                ["amount"] = Prop("number", "Expected amount."),
                ["probability"] = Prop("number", "Win probability from 0 to 100."),
                ["type"] = Prop("string", "Optional type (e.g., New Business, Existing Customer - Upgrade)."),
                ["leadSource"] = Prop("string", "Optional lead source."),
                ["nextStep"] = Prop("string", "Optional next step guidance."),
                ["description"] = Prop("string", "Optional opportunity description."),
                ["ownerId"] = Prop("string", "Optional owner user Id."),
            },
            "name", "stageName", "closeDate"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();

            var opp = new JsonObject
            {
                ["Name"] = RequireString(args, "name"),
                ["StageName"] = RequireString(args, "stageName"),
                ["CloseDate"] = RequireString(args, "closeDate"),
            };

            if (!string.IsNullOrWhiteSpace(OptString(args, "accountId"))) opp["AccountId"] = OptString(args, "accountId");
            if (OptDecimal(args, "amount") is { } amount) opp["Amount"] = amount;
            if (OptDecimal(args, "probability") is { } probability) opp["Probability"] = probability;
            if (!string.IsNullOrWhiteSpace(OptString(args, "type"))) opp["Type"] = OptString(args, "type");
            if (!string.IsNullOrWhiteSpace(OptString(args, "leadSource"))) opp["LeadSource"] = OptString(args, "leadSource");
            if (!string.IsNullOrWhiteSpace(OptString(args, "nextStep"))) opp["NextStep"] = OptString(args, "nextStep");
            if (!string.IsNullOrWhiteSpace(OptString(args, "description"))) opp["Description"] = OptString(args, "description");
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) opp["OwnerId"] = OptString(args, "ownerId");

            var resp = await sf.CreateSObjectAsync("Opportunity", opp, ct);
            return ContentResult(resp);
        },
    };
}

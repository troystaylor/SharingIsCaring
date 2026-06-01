using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class UpdateOpportunityTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "update_opportunity",
        Description = "Update key Salesforce opportunity fields such as stage, amount, close date, next step, probability, and owner.",
        Annotations = new ToolAnnotations(
            Title: "Update Salesforce opportunity",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["opportunityId"] = Prop("string", "Salesforce Opportunity Id."),
                ["stageName"] = Prop("string", "New StageName value."),
                ["amount"] = Prop("number", "New Amount value."),
                ["closeDate"] = Prop("string", "New close date in YYYY-MM-DD format."),
                ["nextStep"] = Prop("string", "Next step guidance text."),
                ["probability"] = Prop("number", "Probability value from 0 to 100."),
                ["ownerId"] = Prop("string", "New opportunity owner user Id."),
            },
            required: "opportunityId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var oppId = RequireString(args, "opportunityId");

            var patch = new JsonObject();
            if (!string.IsNullOrWhiteSpace(OptString(args, "stageName"))) patch["StageName"] = OptString(args, "stageName");
            if (OptDecimal(args, "amount") is { } amount) patch["Amount"] = amount;
            if (!string.IsNullOrWhiteSpace(OptString(args, "closeDate"))) patch["CloseDate"] = OptString(args, "closeDate");
            if (!string.IsNullOrWhiteSpace(OptString(args, "nextStep"))) patch["NextStep"] = OptString(args, "nextStep");
            if (OptDecimal(args, "probability") is { } probability) patch["Probability"] = probability;
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) patch["OwnerId"] = OptString(args, "ownerId");

            if (patch.Count == 0)
            {
                throw new ArgumentException("at least one updatable field is required");
            }

            var resp = await sf.PatchSObjectAsync("Opportunity", oppId, patch, ct);
            return ContentResult(resp);
        },
    };
}

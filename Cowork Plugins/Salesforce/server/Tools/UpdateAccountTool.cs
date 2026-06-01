using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class UpdateAccountTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "update_account",
        Description = "Update key Salesforce account fields such as type, industry, revenue, website, phone, billing address, and owner.",
        Annotations = new ToolAnnotations(
            Title: "Update Salesforce account",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["accountId"] = Prop("string", "Salesforce Account Id."),
                ["name"] = Prop("string", "New account name."),
                ["type"] = Prop("string", "Account type (e.g., Prospect, Customer - Direct)."),
                ["industry"] = Prop("string", "Industry."),
                ["annualRevenue"] = Prop("number", "Annual revenue."),
                ["numberOfEmployees"] = Prop("integer", "Number of employees."),
                ["website"] = Prop("string", "Website URL."),
                ["phone"] = Prop("string", "Main phone."),
                ["billingCity"] = Prop("string", "Billing city."),
                ["billingState"] = Prop("string", "Billing state."),
                ["billingCountry"] = Prop("string", "Billing country."),
                ["description"] = Prop("string", "Description."),
                ["ownerId"] = Prop("string", "New owner user Id."),
            },
            required: "accountId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var accountId = RequireString(args, "accountId");

            var patch = new JsonObject();
            if (!string.IsNullOrWhiteSpace(OptString(args, "name"))) patch["Name"] = OptString(args, "name");
            if (!string.IsNullOrWhiteSpace(OptString(args, "type"))) patch["Type"] = OptString(args, "type");
            if (!string.IsNullOrWhiteSpace(OptString(args, "industry"))) patch["Industry"] = OptString(args, "industry");
            if (OptDecimal(args, "annualRevenue") is { } revenue) patch["AnnualRevenue"] = revenue;
            if (OptInt(args, "numberOfEmployees") is { } emp) patch["NumberOfEmployees"] = emp;
            if (!string.IsNullOrWhiteSpace(OptString(args, "website"))) patch["Website"] = OptString(args, "website");
            if (!string.IsNullOrWhiteSpace(OptString(args, "phone"))) patch["Phone"] = OptString(args, "phone");
            if (!string.IsNullOrWhiteSpace(OptString(args, "billingCity"))) patch["BillingCity"] = OptString(args, "billingCity");
            if (!string.IsNullOrWhiteSpace(OptString(args, "billingState"))) patch["BillingState"] = OptString(args, "billingState");
            if (!string.IsNullOrWhiteSpace(OptString(args, "billingCountry"))) patch["BillingCountry"] = OptString(args, "billingCountry");
            if (!string.IsNullOrWhiteSpace(OptString(args, "description"))) patch["Description"] = OptString(args, "description");
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) patch["OwnerId"] = OptString(args, "ownerId");

            if (patch.Count == 0)
            {
                throw new ArgumentException("at least one updatable field is required");
            }

            var resp = await sf.PatchSObjectAsync("Account", accountId, patch, ct);
            return ContentResult(resp);
        },
    };
}

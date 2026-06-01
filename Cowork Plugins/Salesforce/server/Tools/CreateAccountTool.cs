using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class CreateAccountTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "create_account",
        Description = "Create a new Salesforce account.",
        Annotations = new ToolAnnotations(
            Title: "Create Salesforce account",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["name"] = Prop("string", "Account name."),
                ["type"] = Prop("string", "Optional account type (e.g., Prospect, Customer - Direct)."),
                ["industry"] = Prop("string", "Optional industry."),
                ["annualRevenue"] = Prop("number", "Optional annual revenue."),
                ["numberOfEmployees"] = Prop("integer", "Optional number of employees."),
                ["website"] = Prop("string", "Optional website URL."),
                ["phone"] = Prop("string", "Optional main phone."),
                ["billingCity"] = Prop("string", "Optional billing city."),
                ["billingState"] = Prop("string", "Optional billing state."),
                ["billingCountry"] = Prop("string", "Optional billing country."),
                ["description"] = Prop("string", "Optional description."),
                ["ownerId"] = Prop("string", "Optional owner user Id."),
            },
            "name"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();

            var account = new JsonObject
            {
                ["Name"] = RequireString(args, "name"),
            };

            if (!string.IsNullOrWhiteSpace(OptString(args, "type"))) account["Type"] = OptString(args, "type");
            if (!string.IsNullOrWhiteSpace(OptString(args, "industry"))) account["Industry"] = OptString(args, "industry");
            if (OptDecimal(args, "annualRevenue") is { } revenue) account["AnnualRevenue"] = revenue;
            if (OptInt(args, "numberOfEmployees") is { } emp) account["NumberOfEmployees"] = emp;
            if (!string.IsNullOrWhiteSpace(OptString(args, "website"))) account["Website"] = OptString(args, "website");
            if (!string.IsNullOrWhiteSpace(OptString(args, "phone"))) account["Phone"] = OptString(args, "phone");
            if (!string.IsNullOrWhiteSpace(OptString(args, "billingCity"))) account["BillingCity"] = OptString(args, "billingCity");
            if (!string.IsNullOrWhiteSpace(OptString(args, "billingState"))) account["BillingState"] = OptString(args, "billingState");
            if (!string.IsNullOrWhiteSpace(OptString(args, "billingCountry"))) account["BillingCountry"] = OptString(args, "billingCountry");
            if (!string.IsNullOrWhiteSpace(OptString(args, "description"))) account["Description"] = OptString(args, "description");
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) account["OwnerId"] = OptString(args, "ownerId");

            var resp = await sf.CreateSObjectAsync("Account", account, ct);
            return ContentResult(resp);
        },
    };
}

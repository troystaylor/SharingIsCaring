using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class UpdateContactTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "update_contact",
        Description = "Update key Salesforce contact fields such as title, email, phone, account, department, and owner.",
        Annotations = new ToolAnnotations(
            Title: "Update Salesforce contact",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["contactId"] = Prop("string", "Salesforce Contact Id."),
                ["firstName"] = Prop("string", "First name."),
                ["lastName"] = Prop("string", "Last name."),
                ["title"] = Prop("string", "Job title."),
                ["email"] = Prop("string", "Email."),
                ["phone"] = Prop("string", "Phone."),
                ["mobilePhone"] = Prop("string", "Mobile phone."),
                ["department"] = Prop("string", "Department."),
                ["accountId"] = Prop("string", "Related Account Id."),
                ["description"] = Prop("string", "Description."),
                ["ownerId"] = Prop("string", "New owner user Id."),
            },
            required: "contactId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var contactId = RequireString(args, "contactId");

            var patch = new JsonObject();
            if (!string.IsNullOrWhiteSpace(OptString(args, "firstName"))) patch["FirstName"] = OptString(args, "firstName");
            if (!string.IsNullOrWhiteSpace(OptString(args, "lastName"))) patch["LastName"] = OptString(args, "lastName");
            if (!string.IsNullOrWhiteSpace(OptString(args, "title"))) patch["Title"] = OptString(args, "title");
            if (!string.IsNullOrWhiteSpace(OptString(args, "email"))) patch["Email"] = OptString(args, "email");
            if (!string.IsNullOrWhiteSpace(OptString(args, "phone"))) patch["Phone"] = OptString(args, "phone");
            if (!string.IsNullOrWhiteSpace(OptString(args, "mobilePhone"))) patch["MobilePhone"] = OptString(args, "mobilePhone");
            if (!string.IsNullOrWhiteSpace(OptString(args, "department"))) patch["Department"] = OptString(args, "department");
            if (!string.IsNullOrWhiteSpace(OptString(args, "accountId"))) patch["AccountId"] = OptString(args, "accountId");
            if (!string.IsNullOrWhiteSpace(OptString(args, "description"))) patch["Description"] = OptString(args, "description");
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) patch["OwnerId"] = OptString(args, "ownerId");

            if (patch.Count == 0)
            {
                throw new ArgumentException("at least one updatable field is required");
            }

            var resp = await sf.PatchSObjectAsync("Contact", contactId, patch, ct);
            return ContentResult(resp);
        },
    };
}

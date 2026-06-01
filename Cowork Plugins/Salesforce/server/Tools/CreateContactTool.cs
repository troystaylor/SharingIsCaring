using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class CreateContactTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "create_contact",
        Description = "Create a new Salesforce contact, optionally linked to an account.",
        Annotations = new ToolAnnotations(
            Title: "Create Salesforce contact",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["lastName"] = Prop("string", "Contact last name (required by Salesforce)."),
                ["firstName"] = Prop("string", "Optional first name."),
                ["accountId"] = Prop("string", "Optional related Account Id."),
                ["title"] = Prop("string", "Optional job title."),
                ["email"] = Prop("string", "Optional email."),
                ["phone"] = Prop("string", "Optional phone."),
                ["mobilePhone"] = Prop("string", "Optional mobile phone."),
                ["department"] = Prop("string", "Optional department."),
                ["ownerId"] = Prop("string", "Optional owner user Id."),
                ["description"] = Prop("string", "Optional description."),
            },
            "lastName"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();

            var contact = new JsonObject
            {
                ["LastName"] = RequireString(args, "lastName"),
            };

            if (!string.IsNullOrWhiteSpace(OptString(args, "firstName"))) contact["FirstName"] = OptString(args, "firstName");
            if (!string.IsNullOrWhiteSpace(OptString(args, "accountId"))) contact["AccountId"] = OptString(args, "accountId");
            if (!string.IsNullOrWhiteSpace(OptString(args, "title"))) contact["Title"] = OptString(args, "title");
            if (!string.IsNullOrWhiteSpace(OptString(args, "email"))) contact["Email"] = OptString(args, "email");
            if (!string.IsNullOrWhiteSpace(OptString(args, "phone"))) contact["Phone"] = OptString(args, "phone");
            if (!string.IsNullOrWhiteSpace(OptString(args, "mobilePhone"))) contact["MobilePhone"] = OptString(args, "mobilePhone");
            if (!string.IsNullOrWhiteSpace(OptString(args, "department"))) contact["Department"] = OptString(args, "department");
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) contact["OwnerId"] = OptString(args, "ownerId");
            if (!string.IsNullOrWhiteSpace(OptString(args, "description"))) contact["Description"] = OptString(args, "description");

            var resp = await sf.CreateSObjectAsync("Contact", contact, ct);
            return ContentResult(resp);
        },
    };
}

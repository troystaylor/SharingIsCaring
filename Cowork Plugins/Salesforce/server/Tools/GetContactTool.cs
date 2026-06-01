using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class GetContactTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_contact",
        Description = "Get a Salesforce contact by ID with common briefing fields.",
        Annotations = new ToolAnnotations(
            Title: "Get Salesforce contact",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["contactId"] = Prop("string", "Salesforce Contact Id."),
                ["fields"] = Prop("string", "Optional CSV field list override."),
            },
            required: "contactId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var contactId = RequireString(args, "contactId");
            var fields = OptString(args, "fields")
                ?? "Id,FirstName,LastName,Title,Email,Phone,MobilePhone,Department,AccountId,OwnerId,LastModifiedDate";
            var resp = await sf.GetSObjectAsync("Contact", contactId, fields, ct);
            return ContentResult(resp);
        },
    };
}

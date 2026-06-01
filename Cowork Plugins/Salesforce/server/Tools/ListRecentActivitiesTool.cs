using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class ListRecentActivitiesTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_recent_activities",
        Description = "List recent Salesforce task and event activities related to an account or opportunity.",
        Annotations = new ToolAnnotations(
            Title: "List recent Salesforce activities",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["parentId"] = Prop("string", "Salesforce Id for Account/Opportunity (WhatId)."),
                ["limit"] = Prop("integer", "Max rows to return (1-200, default 25)."),
            },
            required: "parentId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var parentId = EscapeSoqlLiteral(RequireString(args, "parentId"));
            var limit = Math.Clamp(OptInt(args, "limit") ?? 25, 1, 200);

            var soql = $"SELECT Id, Subject, ActivityDate, Status, Priority, OwnerId, WhatId, LastModifiedDate FROM Task WHERE WhatId = '{parentId}' ORDER BY ActivityDate DESC, LastModifiedDate DESC LIMIT {limit}";
            var resp = await sf.QueryAsync(soql, ct);
            return ContentResult(resp);
        },
    };
}

using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class ListTasksTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "list_tasks",
        Description = "List Salesforce tasks, optionally filtered by related record, owner, status, or open-only.",
        Annotations = new ToolAnnotations(
            Title: "List Salesforce tasks",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["whatId"] = Prop("string", "Optional related Account or Opportunity Id."),
                ["whoId"] = Prop("string", "Optional related Contact or Lead Id."),
                ["ownerId"] = Prop("string", "Optional owner user Id."),
                ["status"] = Prop("string", "Optional status filter (e.g., Not Started, In Progress, Completed)."),
                ["openOnly"] = Prop("boolean", "If true, exclude Completed tasks. Default false."),
                ["limit"] = Prop("integer", "Max rows to return (1-200, default 25)."),
            }),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var limit = Math.Clamp(OptInt(args, "limit") ?? 25, 1, 200);

            var clauses = new List<string>();
            if (OptString(args, "whatId") is { Length: > 0 } whatId) clauses.Add($"WhatId = '{EscapeSoqlLiteral(whatId)}'");
            if (OptString(args, "whoId") is { Length: > 0 } whoId) clauses.Add($"WhoId = '{EscapeSoqlLiteral(whoId)}'");
            if (OptString(args, "ownerId") is { Length: > 0 } ownerId) clauses.Add($"OwnerId = '{EscapeSoqlLiteral(ownerId)}'");
            if (OptString(args, "status") is { Length: > 0 } status) clauses.Add($"Status = '{EscapeSoqlLiteral(status)}'");

            var openOnly = args["openOnly"] is JsonValue ov && ov.TryGetValue<bool>(out var b) && b;
            if (openOnly) clauses.Add("Status != 'Completed'");

            var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
            var soql = $"SELECT Id, Subject, Status, Priority, ActivityDate, WhatId, WhoId, OwnerId, Owner.Name, CreatedDate, LastModifiedDate FROM Task {where} ORDER BY LastModifiedDate DESC LIMIT {limit}";
            var resp = await sf.QueryAsync(soql, ct);
            return ContentResult(resp);
        },
    };
}

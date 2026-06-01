using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class GetTaskTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "get_task",
        Description = "Get a Salesforce task by ID with full detail.",
        Annotations = new ToolAnnotations(
            Title: "Get Salesforce task",
            ReadOnlyHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["taskId"] = Prop("string", "Salesforce Task Id."),
                ["fields"] = Prop("string", "Optional CSV field list override."),
            },
            required: "taskId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();
            var taskId = RequireString(args, "taskId");
            var fields = OptString(args, "fields")
                ?? "Id,Subject,Description,Status,Priority,ActivityDate,WhatId,WhoId,OwnerId,CreatedDate,LastModifiedDate";
            var resp = await sf.GetSObjectAsync("Task", taskId, fields, ct);
            return ContentResult(resp);
        },
    };
}

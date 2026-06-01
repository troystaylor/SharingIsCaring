using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCoworkMcp.Salesforce;
using static SalesforceCoworkMcp.Tools.ToolHelpers;

namespace SalesforceCoworkMcp.Tools;

internal static class CreateTaskTool
{
    public static ToolDescriptor Build() => new()
    {
        Name = "create_task",
        Description = "Create a Salesforce task linked to an account or opportunity.",
        Annotations = new ToolAnnotations(
            Title: "Create Salesforce task",
            ReadOnlyHint: false,
            DestructiveHint: true,
            OpenWorldHint: true),
        InputSchema = Schema(
            new JsonObject
            {
                ["subject"] = Prop("string", "Task subject/title."),
                ["whatId"] = Prop("string", "Related record Id (Account or Opportunity)."),
                ["description"] = Prop("string", "Optional task description."),
                ["activityDate"] = Prop("string", "Optional due date in YYYY-MM-DD format."),
                ["status"] = Prop("string", "Optional status (default Not Started)."),
                ["priority"] = Prop("string", "Optional priority (default Normal)."),
                ["ownerId"] = Prop("string", "Optional owner user Id."),
            },
            "subject", "whatId"),
        Invoke = async (sp, args, ct) =>
        {
            var sf = sp.GetRequiredService<ISalesforceClient>();

            var task = new JsonObject
            {
                ["Subject"] = RequireString(args, "subject"),
                ["WhatId"] = RequireString(args, "whatId"),
                ["Status"] = OptString(args, "status") ?? "Not Started",
                ["Priority"] = OptString(args, "priority") ?? "Normal",
            };

            if (!string.IsNullOrWhiteSpace(OptString(args, "description"))) task["Description"] = OptString(args, "description");
            if (!string.IsNullOrWhiteSpace(OptString(args, "activityDate"))) task["ActivityDate"] = OptString(args, "activityDate");
            if (!string.IsNullOrWhiteSpace(OptString(args, "ownerId"))) task["OwnerId"] = OptString(args, "ownerId");

            var resp = await sf.CreateSObjectAsync("Task", task, ct);
            return ContentResult(resp);
        },
    };
}

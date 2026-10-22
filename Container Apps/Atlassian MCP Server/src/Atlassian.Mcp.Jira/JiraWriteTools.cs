using System.ComponentModel;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Atlassian.Mcp.Jira;

/// <summary>
/// Tools that create or change Jira data. They are registered only when <c>Jira:EnableWriteTools</c> is true.
/// Each call runs as the signed-in person, so Jira attributes the change to them and enforces their permissions.
/// </summary>
/// <remarks>
/// Failed writes are never retried automatically, so a transient error cannot create a duplicate. Descriptions
/// tell the model to search before trying again for the same reason. Text arguments are plain text with light
/// Markdown, converted to Atlassian Document Format here.
/// </remarks>
[McpServerToolType]
public sealed class JiraWriteTools(JiraClient jira, IOptions<LimitOptions> limits)
{
    private const int MaxExtraFieldChars = 20_000;
    private const int MaxLabels = 20;

    [McpServerTool(Name = "jira_create_issue", Title = "Create Jira Issue", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Creates a Jira issue as the signed-in user and returns its key and url. Call jira_get_create_metadata first to learn the project's issue types and which fields are required, and use jira_search_users to find an assignee's account ID. This is not idempotent: if the call fails or you are unsure it worked, search with jira_search_issues before trying again, or you will create a duplicate. Confirm the summary and project with the user before creating.")]
    public async Task<JsonNode> CreateIssue(
        [Description("The project key, such as PROJ.")] string project,
        [Description("The issue type name, such as Bug, Task, Story, or Epic. Must exist in the project.")] string issueType,
        [Description("The issue title, one line of up to 255 characters.")] string summary,
        [Description("Optional description. Plain text; blank lines separate paragraphs, '- ' makes bullets, '1. ' makes numbered items, '#' makes headings, and ``` fences code.")] string? description = null,
        [Description("Optional account ID of the assignee, from jira_search_users.")] string? assigneeAccountId = null,
        [Description("Optional priority name, such as High. Must exist in the project.")] string? priority = null,
        [Description("Optional labels. A label cannot contain spaces.")] string[]? labels = null,
        [Description("Optional key of the parent issue or epic, such as PROJ-10. Required for sub-tasks.")] string? parentKey = null,
        [Description("Optional other fields by ID, for required or custom fields, such as {\"customfield_10010\":{\"value\":\"Gold\"}}. Values use Jira's field formats; jira_get_create_metadata lists each field's ID and allowed values.")] JsonObject? additionalFields = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new JsonObject
        {
            ["project"] = new JsonObject { ["key"] = Names.ProjectKey(project) },
            ["issuetype"] = new JsonObject { ["name"] = Names.Line(issueType, "issue type", 60) },
            ["summary"] = Names.Line(summary, "summary")
        };

        Apply(fields, description, assigneeAccountId, priority, labels, parentKey, additionalFields);

        var response = await jira.SendAsync(HttpMethod.Post, "rest/api/3/issue", new JsonObject { ["fields"] = fields }, cancellationToken);
        var key = Shape.Str(response, "key") ?? throw new McpException("Jira did not return the key of the new issue.");

        return Shape.Obj(("created", true), ("key", key), ("id", Shape.Str(response, "id")), ("url", jira.IssueUrl(key)));
    }

    [McpServerTool(Name = "jira_update_issue", Title = "Update Jira Issue", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Changes fields on an existing Jira issue as the signed-in user. Only the fields you pass are changed. Labels replace the whole list, and a description replaces the existing one, so read the issue with jira_get_issue first if you need to keep any of it. To change status use jira_transition_issue, to change the assignee use jira_assign_issue, and to comment use jira_add_comment.")]
    public async Task<JsonNode> UpdateIssue(
        [Description("The issue key, such as PROJ-123.")] string key,
        [Description("Optional new summary, one line of up to 255 characters.")] string? summary = null,
        [Description("Optional new description. Replaces the current one. Plain text with light Markdown.")] string? description = null,
        [Description("Optional priority name, such as High.")] string? priority = null,
        [Description("Optional labels. Replaces all current labels; pass an empty list to clear them.")] string[]? labels = null,
        [Description("Optional other fields by ID, such as {\"customfield_10010\":{\"value\":\"Gold\"}}.")] JsonObject? additionalFields = null,
        CancellationToken cancellationToken = default)
    {
        var issueKey = Names.IssueKey(key);
        var fields = new JsonObject();

        if (summary is not null)
        {
            fields["summary"] = Names.Line(summary, "summary");
        }

        Apply(fields, description, null, priority, labels, null, additionalFields);

        if (fields.Count == 0)
        {
            throw new McpException("Pass at least one field to change.");
        }

        await jira.SendAsync(HttpMethod.Put, $"rest/api/3/issue/{issueKey}", new JsonObject { ["fields"] = fields }, cancellationToken);

        return Shape.Obj(
            ("updated", true),
            ("key", issueKey),
            ("url", jira.IssueUrl(issueKey)),
            ("fields", Shape.Array(fields.Select(f => f.Key))));
    }

    [McpServerTool(Name = "jira_transition_issue", Title = "Transition Jira Issue", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Moves an issue through its workflow, for example to In Progress or Done, as the signed-in user. Pass the transition's name or ID, or the name of the status it leads to; jira_get_issue_transitions lists what is available right now. Some transitions require fields Jira will report if missing. Optionally adds a comment in the same step.")]
    public async Task<JsonNode> TransitionIssue(
        [Description("The issue key, such as PROJ-123.")] string key,
        [Description("The transition name or ID, or the target status name, such as Done.")] string transition,
        [Description("Optional comment to add with the transition. Plain text with light Markdown.")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var issueKey = Names.IssueKey(key);
        var wanted = Names.Line(transition, "transition", 100);

        var available = await jira.GetAsync($"rest/api/3/issue/{issueKey}/transitions", cancellationToken);
        var options = Shape.Items(available, "transitions").ToList();

        var match =
            options.FirstOrDefault(t => Shape.Str(t, "id") == wanted)
            ?? options.FirstOrDefault(t => string.Equals(Shape.Str(t, "name"), wanted, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault(t => string.Equals(Shape.Str(t, "to", "name"), wanted, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var names = options.Count == 0
                ? "none"
                : string.Join(", ", options.Select(t => $"{Shape.Str(t, "name")} (to {Shape.Str(t, "to", "name")})"));
            throw new McpException($"{issueKey} has no transition '{wanted}' available right now. Available: {names}.");
        }

        var body = new JsonObject { ["transition"] = new JsonObject { ["id"] = Shape.Str(match, "id") } };
        if (!string.IsNullOrWhiteSpace(comment))
        {
            body["update"] = new JsonObject
            {
                ["comment"] = new JsonArray(new JsonObject
                {
                    ["add"] = new JsonObject { ["body"] = Adf.FromText(Names.Body(comment, "comment", limits.Value.MaxTextChars)) }
                })
            };
        }

        await jira.SendAsync(HttpMethod.Post, $"rest/api/3/issue/{issueKey}/transitions", body, cancellationToken);

        return Shape.Obj(
            ("transitioned", true),
            ("key", issueKey),
            ("url", jira.IssueUrl(issueKey)),
            ("transition", Shape.Str(match, "name")),
            ("toStatus", Shape.Str(match, "to", "name")));
    }

    [McpServerTool(Name = "jira_add_comment", Title = "Add Jira Comment", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Adds a comment to an issue as the signed-in user. This is not idempotent: a repeated call adds a second comment, so read the issue with jira_get_issue before retrying after a failure.")]
    public async Task<JsonNode> AddComment(
        [Description("The issue key, such as PROJ-123.")] string key,
        [Description("The comment. Plain text with light Markdown.")] string comment,
        CancellationToken cancellationToken = default)
    {
        var issueKey = Names.IssueKey(key);
        var body = new JsonObject { ["body"] = Adf.FromText(Names.Body(comment, "comment", limits.Value.MaxTextChars)) };

        var response = await jira.SendAsync(HttpMethod.Post, $"rest/api/3/issue/{issueKey}/comment", body, cancellationToken);
        var id = Shape.Str(response, "id");

        return Shape.Obj(
            ("added", true),
            ("key", issueKey),
            ("commentId", id),
            ("url", id is null ? jira.IssueUrl(issueKey) : $"{jira.IssueUrl(issueKey)}?focusedCommentId={id}"));
    }

    [McpServerTool(Name = "jira_assign_issue", Title = "Assign Jira Issue", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Sets the assignee of an issue as the signed-in user. Find the account ID with jira_search_users. Pass the word unassigned to remove the assignee.")]
    public async Task<JsonNode> AssignIssue(
        [Description("The issue key, such as PROJ-123.")] string key,
        [Description("The assignee's account ID, or the word unassigned to clear the assignee.")] string accountId,
        CancellationToken cancellationToken = default)
    {
        var issueKey = Names.IssueKey(key);
        var clear = string.Equals((accountId ?? string.Empty).Trim(), "unassigned", StringComparison.OrdinalIgnoreCase);

        var body = new JsonObject { ["accountId"] = clear ? null : Names.AccountId(accountId) };
        await jira.SendAsync(HttpMethod.Put, $"rest/api/3/issue/{issueKey}/assignee", body, cancellationToken);

        return Shape.Obj(
            ("assigned", !clear),
            ("unassigned", clear ? true : null),
            ("key", issueKey),
            ("url", jira.IssueUrl(issueKey)));
    }

    /// <summary>Adds the optional fields shared by create and update, refusing any that a dedicated argument already set.</summary>
    private void Apply(
        JsonObject fields, string? description, string? assigneeAccountId, string? priority,
        string[]? labels, string? parentKey, JsonObject? additionalFields)
    {
        if (description is not null)
        {
            fields["description"] = Adf.FromText(Names.Body(description, "description", limits.Value.MaxTextChars));
        }

        if (!string.IsNullOrWhiteSpace(assigneeAccountId))
        {
            fields["assignee"] = new JsonObject { ["accountId"] = Names.AccountId(assigneeAccountId) };
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            fields["priority"] = new JsonObject { ["name"] = Names.Line(priority, "priority", 60) };
        }

        if (labels is not null)
        {
            if (labels.Length > MaxLabels)
            {
                throw new McpException($"Use at most {MaxLabels} labels.");
            }

            fields["labels"] = new JsonArray(labels.Select(l => (JsonNode?)JsonValue.Create(Names.Label(l))).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(parentKey))
        {
            fields["parent"] = new JsonObject { ["key"] = Names.IssueKey(parentKey) };
        }

        if (additionalFields is not { Count: > 0 })
        {
            return;
        }

        if (additionalFields.ToJsonString().Length > MaxExtraFieldChars)
        {
            throw new McpException($"additionalFields is longer than {MaxExtraFieldChars} characters.");
        }

        foreach (var (name, value) in additionalFields)
        {
            var field = Names.Field(name);
            if (fields.ContainsKey(field))
            {
                throw new McpException($"'{field}' is set by a dedicated argument. Use that argument instead of additionalFields.");
            }

            fields[field] = value?.DeepClone();
        }
    }
}

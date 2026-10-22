using System.ComponentModel;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Atlassian.Mcp.Jira;

/// <summary>
/// Read-only tools over the Jira Cloud REST API. Each call runs as the person who is signed in to Atlassian,
/// so Jira's own project and issue permissions decide what comes back for them.
/// </summary>
/// <remarks>
/// The [Description] text is the model's only basis for choosing a tool and filling its arguments. An empty
/// result is a success and is returned as one: reporting "found nothing" as an error teaches an agent to
/// invent an answer.
/// </remarks>
[McpServerToolType]
public sealed class JiraReadTools(JiraClient jira, IOptions<LimitOptions> limits)
{
    private const int MaxComments = 20;

    private static readonly string[] DefaultSearchFields =
        ["summary", "status", "issuetype", "priority", "assignee", "reporter", "project", "updated", "created", "labels"];

    private static readonly string[] DefaultIssueFields =
    [
        "summary", "status", "issuetype", "priority", "assignee", "reporter", "project", "labels", "components",
        "fixVersions", "created", "updated", "duedate", "resolution", "parent", "description", "comment", "attachment"
    ];

    [McpServerTool(Name = "jira_get_current_user", Title = "Get Jira Connection Info", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Shows who the Jira requests run as, which Atlassian site they go to, and the scopes the sign-in holds on it. Use it first when a call is refused, or to confirm the connection.")]
    public async Task<JsonNode> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        var me = await jira.GetAsync("rest/api/3/myself", cancellationToken);
        return Shape.Obj(
            ("site", jira.Settings.Site),
            ("accountId", Shape.Str(me, "accountId")),
            ("displayName", Shape.Str(me, "displayName")),
            ("email", Shape.Str(me, "emailAddress")),
            ("timeZone", Shape.Str(me, "timeZone")),
            ("active", Shape.Bool(me, "active")),
            ("scopes", jira.Who.Scopes));
    }

    [McpServerTool(Name = "jira_search_issues", Title = "Search Jira Issues", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Searches Jira issues with JQL and returns summaries, each with a url that opens the issue. Use it to find work, for example: project = PROJ AND status != Done AND assignee = currentUser() ORDER BY updated DESC. Always include a restriction such as project, assignee, or a date, because Jira refuses unbounded queries. If isLast is false and nextPageToken is present, pass it back to get the next page. To read one issue in full, call jira_get_issue.")]
    public async Task<JsonNode> SearchIssues(
        [Description("A JQL query, such as: project = PROJ AND created >= -7d ORDER BY created DESC")] string jql,
        [Description("Most issues to return, from 1 to 100. Defaults to 25.")] int maxResults = 25,
        [Description("Optional field names to return instead of the defaults, such as [\"summary\",\"status\",\"customfield_10010\"].")] string[]? fields = null,
        [Description("The nextPageToken from the previous result, unchanged. Leave out for the first page.")] string? nextPageToken = null,
        CancellationToken cancellationToken = default)
    {
        var query = Names.Query(jql, "JQL");
        var wanted = Names.Fields(fields);
        var count = Shape.Clamp(maxResults, 1, Math.Min(100, limits.Value.MaxItems));

        var path = UpstreamClient.WithQuery(
            "rest/api/3/search/jql",
            ("jql", query),
            ("maxResults", count.ToString()),
            ("fields", string.Join(',', wanted.Count > 0 ? wanted : DefaultSearchFields)),
            ("nextPageToken", nextPageToken is null ? null : Names.Cursor(nextPageToken)));

        var response = await jira.GetAsync(path, cancellationToken);
        var (issues, truncated) = Shape.Limit(
            Shape.Items(response, "issues").Select(i => (JsonNode?)Summary(i, wanted.Count > 0)),
            count,
            limits.Value.MaxResponseChars);

        var result = Shape.Obj(("returned", issues.Count), ("issues", issues));
        result["isLast"] = Shape.Bool(response, "isLast") ?? !truncated;
        if (Shape.Str(response, "nextPageToken") is { } next)
        {
            result["nextPageToken"] = next;
        }

        if (truncated)
        {
            result["note"] = "Some issues were left out to keep the response small. Narrow the JQL or request fewer fields.";
        }

        return result;
    }

    [McpServerTool(Name = "jira_get_issue", Title = "Get Jira Issue", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Reads one Jira issue by key: its status, people, dates, labels, description, attachments, and the most recent comments, all as plain text, with a url that opens it. Use it after jira_search_issues, or when the user names an issue such as PROJ-123.")]
    public async Task<JsonNode> GetIssue(
        [Description("The issue key, such as PROJ-123.")] string key,
        [Description("Optional field names to return instead of the defaults. Description and comments come back as text.")] string[]? fields = null,
        CancellationToken cancellationToken = default)
    {
        var issueKey = Names.IssueKey(key);
        var wanted = Names.Fields(fields);

        var path = UpstreamClient.WithQuery(
            $"rest/api/3/issue/{issueKey}",
            ("fields", string.Join(',', wanted.Count > 0 ? wanted : DefaultIssueFields)));

        var issue = await jira.GetAsync(path, cancellationToken);
        if (issue is null)
        {
            throw new McpException($"Jira returned no issue for {issueKey}.");
        }

        var result = Summary(issue, custom: wanted.Count > 0);
        var f = Shape.At(issue, "fields");

        if (Shape.At(f, "description") is { } description)
        {
            result["description"] = Adf.ToText(description, limits.Value.MaxTextChars);
        }

        result["labels"] = Shape.Array(Shape.Items(f, "labels").Select(l => l.ToString()));
        result["components"] = Shape.Array(Shape.Items(f, "components").Select(c => Shape.Str(c, "name")));
        result["fixVersions"] = Shape.Array(Shape.Items(f, "fixVersions").Select(v => Shape.Str(v, "name")));
        result["dueDate"] = Shape.Str(f, "duedate");
        result["resolution"] = Shape.Str(f, "resolution", "name");
        result["parent"] = Shape.Str(f, "parent", "key");
        result["attachments"] = Shape.Array(Shape.Items(f, "attachment").Select(a => Shape.Str(a, "filename")));

        var comments = Shape.Items(f, "comment", "comments").ToList();
        var recent = comments.Skip(Math.Max(0, comments.Count - MaxComments)).Select(c =>
            (JsonNode?)Shape.Obj(
                ("id", Shape.Str(c, "id")),
                ("author", Shape.Str(c, "author", "displayName")),
                ("created", Shape.Str(c, "created")),
                ("body", Adf.ToText(Shape.At(c, "body"), limits.Value.MaxTextChars))));

        result["comments"] = new JsonArray(recent.ToArray());
        result["commentCount"] = comments.Count;
        return result;
    }

    [McpServerTool(Name = "jira_get_issue_transitions", Title = "Get Jira Issue Transitions", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the workflow transitions available for an issue right now, with the status each one leads to. Use it to answer what can happen next to an issue.")]
    public async Task<JsonNode> GetTransitions(
        [Description("The issue key, such as PROJ-123.")] string key,
        CancellationToken cancellationToken = default)
    {
        var issueKey = Names.IssueKey(key);
        var response = await jira.GetAsync($"rest/api/3/issue/{issueKey}/transitions", cancellationToken);

        var transitions = new JsonArray(Shape.Items(response, "transitions").Select(t =>
            (JsonNode?)Shape.Obj(
                ("id", Shape.Str(t, "id")),
                ("name", Shape.Str(t, "name")),
                ("toStatus", Shape.Str(t, "to", "name")))).ToArray());

        return Shape.Obj(("key", issueKey), ("url", jira.IssueUrl(issueKey)), ("transitions", transitions));
    }

    [McpServerTool(Name = "jira_list_projects", Title = "List Jira Projects", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Lists the Jira projects the signed-in user can see, optionally filtered by name or key. Use it to find a project key before writing JQL.")]
    public async Task<JsonNode> ListProjects(
        [Description("Optional text to match in the project name or key.")] string? nameContains = null,
        [Description("Most projects to return, from 1 to 100. Defaults to 50.")] int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        var count = Shape.Clamp(maxResults, 1, Math.Min(100, limits.Value.MaxItems));
        var path = UpstreamClient.WithQuery(
            "rest/api/3/project/search",
            ("query", string.IsNullOrWhiteSpace(nameContains) ? null : Names.SafeText(nameContains, "name filter")),
            ("maxResults", count.ToString()));

        var response = await jira.GetAsync(path, cancellationToken);
        var (projects, truncated) = Shape.Limit(
            Shape.Items(response, "values").Select(p => (JsonNode?)Shape.Obj(
                ("key", Shape.Str(p, "key")),
                ("name", Shape.Str(p, "name")),
                ("id", Shape.Str(p, "id")),
                ("type", Shape.Str(p, "projectTypeKey")),
                ("lead", Shape.Str(p, "lead", "displayName")),
                ("url", Shape.Str(p, "key") is { } k ? jira.IssueUrl(k) : null))),
            count,
            limits.Value.MaxResponseChars);

        var result = Shape.Obj(("returned", projects.Count), ("projects", projects));
        result["isLast"] = Shape.Bool(response, "isLast") ?? true;
        if (truncated)
        {
            result["note"] = "Some projects were left out to keep the response small. Add a name filter.";
        }

        return result;
    }

    [McpServerTool(Name = "jira_search_users", Title = "Search Jira Users", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Finds Jira users by name or email, so you can name an assignee or reporter. Emails may be hidden by each person's privacy settings.")]
    public async Task<JsonNode> SearchUsers(
        [Description("Part of a name or email address.")] string query,
        [Description("Most users to return, from 1 to 50. Defaults to 10.")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length is 0 or > 100 || text.Any(char.IsControl))
        {
            throw new McpException("The query must be 1 to 100 characters of a name or email address.");
        }

        var count = Shape.Clamp(maxResults, 1, 50);
        var response = await jira.GetAsync(
            UpstreamClient.WithQuery("rest/api/3/user/search", ("query", text), ("maxResults", count.ToString())),
            cancellationToken);

        var users = new JsonArray((response as JsonArray ?? []).Where(u => u is not null).Select(u =>
            (JsonNode?)Shape.Obj(
                ("accountId", Shape.Str(u, "accountId")),
                ("displayName", Shape.Str(u, "displayName")),
                ("email", Shape.Str(u, "emailAddress")),
                ("active", Shape.Bool(u, "active")))).ToArray());

        return Shape.Obj(("returned", users.Count), ("users", users));
    }

    [McpServerTool(Name = "jira_get_create_metadata", Title = "Get Jira Create Metadata", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Shows how issues are created in a project. With only a project it lists the issue types. With an issue type (name or ID) it lists every field for that type: its ID, whether it is required, its type, and its allowed values. Use it before jira_create_issue to learn the required fields and valid values, such as priorities and custom fields.")]
    public async Task<JsonNode> GetCreateMetadata(
        [Description("The project key, such as PROJ.")] string project,
        [Description("Optional issue type name or ID, such as Bug. Omit to list the project's issue types.")] string? issueType = null,
        CancellationToken cancellationToken = default)
    {
        var key = Names.ProjectKey(project);

        var response = await jira.GetAsync($"rest/api/3/issue/createmeta/{key}/issuetypes?maxResults=100", cancellationToken);
        var types = Shape.Items(response, "issueTypes").Concat(Shape.Items(response, "values")).ToList();

        var summary = new JsonArray(types.Select(t => (JsonNode?)Shape.Obj(
            ("id", Shape.Str(t, "id")),
            ("name", Shape.Str(t, "name")),
            ("subtask", Shape.Bool(t, "subtask")),
            ("description", string.IsNullOrEmpty(Shape.Str(t, "description")) ? null : Shape.Cap(Shape.Str(t, "description"), 200, out _)))).ToArray());

        if (string.IsNullOrWhiteSpace(issueType))
        {
            return Shape.Obj(("project", key), ("issueTypes", summary));
        }

        var wanted = Names.Line(issueType, "issue type", 60);
        var type = types.FirstOrDefault(t =>
                Shape.Str(t, "id") == wanted || string.Equals(Shape.Str(t, "name"), wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException(
                $"{key} has no issue type '{wanted}'. Available: {string.Join(", ", types.Select(t => Shape.Str(t, "name")))}.");

        var typeId = Names.Number(Shape.Str(type, "id"), "issue type ID");
        var meta = await jira.GetAsync($"rest/api/3/issue/createmeta/{key}/issuetypes/{typeId}?maxResults=200", cancellationToken);

        var fields = Shape.Items(meta, "fields").Concat(Shape.Items(meta, "results"))
            .OrderByDescending(f => Shape.Bool(f, "required") == true)
            .Take(100)
            .Select(f => (JsonNode?)Shape.Obj(
                ("id", Shape.Str(f, "fieldId") ?? Shape.Str(f, "key")),
                ("name", Shape.Str(f, "name")),
                ("required", Shape.Bool(f, "required")),
                ("type", Shape.Str(f, "schema", "type")),
                ("itemsType", Shape.Str(f, "schema", "items")),
                ("hasDefault", Shape.Bool(f, "hasDefaultValue") is true ? true : null),
                ("allowedValues", Shape.Items(f, "allowedValues").Any()
                    ? Shape.Array(Shape.Items(f, "allowedValues").Take(25)
                        .Select(v => Shape.Str(v, "name") ?? Shape.Str(v, "value") ?? Shape.Str(v, "id")))
                    : null)))
            .ToArray();

        return Shape.Obj(
            ("project", key),
            ("issueType", Shape.Str(type, "name")),
            ("issueTypeId", typeId),
            ("fields", new JsonArray(fields)));
    }

    private JsonObject Summary(JsonNode issue, bool custom)
    {
        var f = Shape.At(issue, "fields");
        var key = Shape.Str(issue, "key");

        var summary = Shape.Obj(
            ("key", key),
            ("id", Shape.Str(issue, "id")),
            ("url", key is null ? null : jira.IssueUrl(key)),
            ("summary", Shape.Str(f, "summary")),
            ("status", Shape.Str(f, "status", "name")),
            ("statusCategory", Shape.Str(f, "status", "statusCategory", "name")),
            ("issueType", Shape.Str(f, "issuetype", "name")),
            ("priority", Shape.Str(f, "priority", "name")),
            ("assignee", Shape.Str(f, "assignee", "displayName")),
            ("reporter", Shape.Str(f, "reporter", "displayName")),
            ("project", Shape.Str(f, "project", "key")),
            ("created", Shape.Str(f, "created")),
            ("updated", Shape.Str(f, "updated")));

        // Fields the caller asked for by name, such as custom fields, are passed through as the provider sent them.
        if (custom && f is JsonObject all)
        {
            var extra = new JsonObject();
            foreach (var (name, value) in all)
            {
                if (!DefaultIssueFields.Contains(name) && value is not null)
                {
                    extra[name] = value.DeepClone();
                }
            }

            if (extra.Count > 0)
            {
                summary["fields"] = extra;
            }
        }

        return summary;
    }
}

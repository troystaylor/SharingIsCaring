using System.Net;
using System.Text.Json.Nodes;

namespace Atlassian.Mcp.Tests;

public class JiraToolTests
{
    private const string Host = FakeUpstream.AtlassianHost;
    private static readonly string Base = $"/ex/jira/{FakeUpstream.CloudId}/rest/api/3";

    private const string SearchResponse = """
        {"issues":[{"id":"10001","key":"PROJ-1","fields":{
          "summary":"Fix login","status":{"name":"In Progress","statusCategory":{"name":"In Progress"}},
          "issuetype":{"name":"Bug"},"priority":{"name":"High"},"assignee":{"displayName":"Alice"},
          "reporter":{"displayName":"Bob"},"project":{"key":"PROJ"},
          "created":"2026-10-01T10:00:00.000+0000","updated":"2026-10-02T10:00:00.000+0000"}}],
         "isLast":false,"nextPageToken":"abc123"}
        """;

    [Fact]
    public async Task Search_returns_shaped_issues_with_links_and_a_page_token()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/search/jql", SearchResponse);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues",
            new { jql = "project = PROJ AND status != Done" });

        Assert.Equal(1, (int)result["returned"]!);
        var issue = result["issues"]![0]!;
        Assert.Equal("PROJ-1", (string?)issue["key"]);
        Assert.Equal("https://acme.atlassian.net/browse/PROJ-1", (string?)issue["url"]);
        Assert.Equal("Fix login", (string?)issue["summary"]);
        Assert.Equal("Alice", (string?)issue["assignee"]);
        Assert.Equal("In Progress", (string?)issue["statusCategory"]);
        Assert.Equal("abc123", (string?)result["nextPageToken"]);
        Assert.False((bool)result["isLast"]!);
    }

    [Fact]
    public async Task Search_runs_as_the_caller_against_the_configured_site_and_encodes_the_query()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/search/jql", SearchResponse);

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues",
            new { jql = "project = \"A&B\" ORDER BY created", maxResults = 5, nextPageToken = "tok-1" });

        var seen = app.Upstream.Last(Host, $"{Base}/search/jql");
        Assert.Equal("alice-token", seen.Bearer);
        Assert.Contains("jql=project%20%3D%20%22A%26B%22%20ORDER%20BY%20created", seen.Query);
        Assert.Contains("maxResults=5", seen.Query);
        Assert.Contains("nextPageToken=tok-1", seen.Query);
        Assert.Contains("fields=summary", seen.Query);
    }

    [Fact]
    public async Task An_empty_search_is_a_success()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/search/jql", """{"issues":[],"isLast":true}""");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues", new { jql = "project = NONE" });

        Assert.Equal(0, (int)result["returned"]!);
        Assert.Empty(result["issues"]!.AsArray());
    }

    [Fact]
    public async Task Max_results_is_clamped_to_the_server_limit()
    {
        await using var app = new TestApp().With("Limits:MaxItems", "10");
        app.Upstream.Json(Host, $"{Base}/search/jql", SearchResponse);

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues", new { jql = "project = PROJ", maxResults = 100 });

        Assert.Contains("maxResults=10", app.Upstream.Last(Host, $"{Base}/search/jql").Query);
    }

    [Fact]
    public async Task Get_issue_turns_rich_text_into_readable_text_and_keeps_recent_comments()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-1", """
            {"id":"10001","key":"PROJ-1","fields":{
              "summary":"Fix login","status":{"name":"Done"},"project":{"key":"PROJ"},
              "labels":["auth","urgent"],"components":[{"name":"Web"}],"duedate":"2026-11-01",
              "attachment":[{"filename":"trace.log"}],
              "description":{"type":"doc","version":1,"content":[
                {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Steps"}]},
                {"type":"paragraph","content":[{"type":"text","text":"Open the "},
                  {"type":"text","text":"portal","marks":[{"type":"link","attrs":{"href":"https://example.com"}}]}]},
                {"type":"bulletList","content":[
                  {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Sign in"}]}]},
                  {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Observe"}]}]}]}]},
              "comment":{"comments":[
                {"id":"1","author":{"displayName":"Bob"},"created":"2026-10-02T10:00:00.000+0000",
                 "body":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"Reproduced."}]}]}}]}}}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_get_issue", new { key = "proj-1" });

        var description = (string)result["description"]!;
        Assert.Contains("## Steps", description);
        Assert.Contains("Open the portal (https://example.com)", description);
        Assert.Contains("- Sign in", description);
        Assert.Contains("- Observe", description);
        Assert.Equal("Reproduced.", (string?)result["comments"]![0]!["body"]);
        Assert.Equal("Bob", (string?)result["comments"]![0]!["author"]);
        Assert.Equal(1, (int)result["commentCount"]!);
        Assert.Equal("urgent", (string?)result["labels"]![1]);
        Assert.Equal("Web", (string?)result["components"]![0]);
        Assert.Equal("trace.log", (string?)result["attachments"]![0]);
        Assert.Equal("https://acme.atlassian.net/browse/PROJ-1", (string?)result["url"]);
    }

    [Fact]
    public async Task Only_the_most_recent_comments_are_returned()
    {
        await using var app = new TestApp();
        var comments = string.Join(',', Enumerable.Range(1, 30).Select(i =>
            $$$"""{"id":"{{{i}}}","author":{"displayName":"A"},"created":"x","body":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"c{{{i}}}"}]}]}}"""));
        app.Upstream.Json(
            Host, $"{Base}/issue/PROJ-2",
            """{"key":"PROJ-2","fields":{"summary":"s","comment":{"comments":[""" + comments + """]}}}""");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_get_issue", new { key = "PROJ-2" });

        Assert.Equal(30, (int)result["commentCount"]!);
        Assert.Equal(20, result["comments"]!.AsArray().Count);
        Assert.Equal("c30", (string?)result["comments"]![19]!["body"]);
    }

    [Theory]
    [InlineData("bad key!")]
    [InlineData("PROJ-1/../../x")]
    [InlineData("../../rest/api/3/myself")]
    [InlineData("")]
    public async Task A_malformed_issue_key_is_refused_before_any_call(string key)
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(app.ClientWith("alice-token"), Mcp.Jira, "jira_get_issue", new { key });

        Assert.True(isError);
        Assert.Contains("issue key", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("/issue/"));
    }

    [Fact]
    public async Task An_oversized_or_empty_jql_is_refused()
    {
        await using var app = new TestApp();
        var client = app.ClientWith("alice-token");

        var (emptyError, _) = await Mcp.CallToolAsync(client, Mcp.Jira, "jira_search_issues", new { jql = "  " });
        var (longError, longText) = await Mcp.CallToolAsync(client, Mcp.Jira, "jira_search_issues", new { jql = new string('a', 2001) });

        Assert.True(emptyError);
        Assert.True(longError);
        Assert.Contains("longer than", longText);
    }

    [Fact]
    public async Task A_field_name_that_could_break_out_of_the_list_is_refused()
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues",
            new { jql = "project = PROJ", fields = new[] { "summary,description" } });

        Assert.True(isError);
        Assert.Contains("field name", text);
    }

    [Fact]
    public async Task Custom_fields_that_were_asked_for_are_passed_through()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/search/jql", """
            {"issues":[{"key":"PROJ-1","fields":{"summary":"s","customfield_10010":{"value":"Gold"}}}],"isLast":true}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues",
            new { jql = "project = PROJ", fields = new[] { "summary", "customfield_10010" } });

        Assert.Equal("Gold", (string?)result["issues"]![0]!["fields"]!["customfield_10010"]!["value"]);
    }

    [Fact]
    public async Task Jira_errors_are_turned_into_actionable_messages()
    {
        await using var app = new TestApp();
        app.Upstream.Json(
            Host, $"{Base}/search/jql",
            """{"errorMessages":["Error in the JQL Query: The operator '=' is not valid."],"errors":{}}""",
            HttpStatusCode.BadRequest);

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues", new { jql = "project == X" });

        Assert.True(isError);
        Assert.Contains("HTTP 400", text);
        Assert.Contains("Error in the JQL Query", text);
    }

    [Fact]
    public async Task A_missing_issue_says_it_may_be_hidden_from_the_user()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-9", """{"errorMessages":["Issue does not exist or you do not have permission to see it."]}""", HttpStatusCode.NotFound);

        var (isError, text) = await Mcp.CallToolAsync(app.ClientWith("alice-token"), Mcp.Jira, "jira_get_issue", new { key = "PROJ-9" });

        Assert.True(isError);
        Assert.Contains("cannot see it", text);
    }

    [Fact]
    public async Task A_rate_limit_response_says_how_long_to_wait()
    {
        await using var app = new TestApp();
        app.Upstream.Route(Host, $"{Base}/search/jql", _ =>
        {
            var response = FakeUpstream.Respond(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(7));
            return response;
        });

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_search_issues", new { jql = "project = PROJ" });

        Assert.True(isError);
        Assert.Contains("rate limit", text);
        Assert.Contains("7 seconds", text);
    }

    [Fact]
    public async Task A_token_with_no_access_to_the_site_gets_a_clear_message_and_no_upstream_call()
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("nosite-token"), Mcp.Jira, "jira_search_issues", new { jql = "project = PROJ" });

        Assert.True(isError);
        Assert.Contains("does not have access to https://acme.atlassian.net", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("/rest/api/3/"));
    }

    [Fact]
    public async Task Each_person_runs_as_themselves()
    {
        await using var app = new TestApp();
        app.Upstream.Route(Host, $"{Base}/myself", request =>
        {
            var who = request.Headers.Authorization!.Parameter == "alice-token" ? "Alice" : "Bob";
            return FakeUpstream.Respond(HttpStatusCode.OK, $$"""{"accountId":"id-{{who}}","displayName":"{{who}}","active":true}""");
        });

        var alice = await Mcp.CallOkAsync(app.ClientWith("alice-token"), Mcp.Jira, "jira_get_current_user");
        var bob = await Mcp.CallOkAsync(app.ClientWith("bob-token"), Mcp.Jira, "jira_get_current_user");

        Assert.Equal("Alice", (string?)alice["displayName"]);
        Assert.Equal("Bob", (string?)bob["displayName"]);
        Assert.Equal("https://acme.atlassian.net", (string?)alice["site"]);
        Assert.Contains("read:jira-work", (string?)alice["scopes"]);

        var calls = app.Upstream.Requests.Where(r => r.Path == $"{Base}/myself").Select(r => r.Bearer).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal("alice-token", calls[0]);
        Assert.Equal("bob-token", calls[1]);
    }

    [Fact]
    public async Task Transitions_and_projects_and_users_are_shaped()
    {
        await using var app = new TestApp();
        app.Upstream
            .Json(Host, $"{Base}/issue/PROJ-1/transitions", """{"transitions":[{"id":"31","name":"Done","to":{"name":"Done"}}]}""")
            .Json(Host, $"{Base}/project/search", """{"values":[{"id":"1","key":"PROJ","name":"Project","projectTypeKey":"software","lead":{"displayName":"Alice"}}],"isLast":true}""")
            .Json(Host, $"{Base}/user/search", """[{"accountId":"a1","displayName":"Alice","emailAddress":"alice@example.com","active":true}]""");
        var client = app.ClientWith("alice-token");

        var transitions = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_get_issue_transitions", new { key = "PROJ-1" });
        var projects = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_list_projects", new { nameContains = "Proj" });
        var users = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_search_users", new { query = "ali" });

        Assert.Equal("31", (string?)transitions["transitions"]![0]!["id"]);
        Assert.Equal("Done", (string?)transitions["transitions"]![0]!["toStatus"]);
        Assert.Equal("https://acme.atlassian.net/browse/PROJ", (string?)projects["projects"]![0]!["url"]);
        Assert.Contains("query=Proj", app.Upstream.Last(Host, $"{Base}/project/search").Query);
        Assert.Equal("a1", (string?)users["users"]![0]!["accountId"]);
    }

    [Fact]
    public async Task A_cloud_id_setting_lets_dev_mode_skip_the_site_lookup()
    {
        await using var app = new TestApp().With("Auth:Enabled", "false").With("Atlassian:CloudId", "dev-cloud");
        app.Upstream.Json(Host, "/ex/jira/dev-cloud/rest/api/3/myself", """{"accountId":"a","displayName":"Dev"}""");

        var result = await Mcp.CallOkAsync(app.ClientWith("alice-token"), Mcp.Jira, "jira_get_current_user");

        Assert.Equal("Dev", (string?)result["displayName"]);
        Assert.Equal(0, app.Upstream.CountOf(Host, "/oauth/token/accessible-resources"));
    }
}

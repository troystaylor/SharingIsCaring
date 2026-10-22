using System.Net;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Confluence;
using Atlassian.Mcp.Core;
using Atlassian.Mcp.Jira;
using ModelContextProtocol;

namespace Atlassian.Mcp.Tests;

public class JiraWriteToolTests
{
    private const string Host = FakeUpstream.AtlassianHost;
    private static readonly string Base = $"/ex/jira/{FakeUpstream.CloudId}/rest/api/3";

    private static TestApp App() => new TestApp().With("Jira:EnableWriteTools", "true");

    private static JsonNode Sent(TestApp app, HttpMethod method, string path) =>
        JsonNode.Parse(app.Upstream.LastBy(method, Host, path).Body!)!;

    [Fact]
    public async Task Create_issue_sends_the_fields_as_the_caller_and_returns_a_link()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue", """{"id":"10002","key":"PROJ-2","self":"x"}""", HttpStatusCode.Created, "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue",
            new
            {
                project = "proj",
                issueType = "Bug",
                summary = "Login fails",
                description = "Steps:\n\n- open the page\n- sign in",
                assigneeAccountId = "acct-1",
                priority = "High",
                labels = new[] { "auth", "urgent" },
                parentKey = "proj-1",
                additionalFields = new { customfield_10010 = new { value = "Gold" } }
            });

        Assert.True((bool)result["created"]!);
        Assert.Equal("PROJ-2", (string?)result["key"]);
        Assert.Equal("https://acme.atlassian.net/browse/PROJ-2", (string?)result["url"]);

        var seen = app.Upstream.LastBy(HttpMethod.Post, Host, $"{Base}/issue");
        Assert.Equal("alice-token", seen.Bearer);

        var fields = JsonNode.Parse(seen.Body!)!["fields"]!;
        Assert.Equal("PROJ", (string?)fields["project"]!["key"]);
        Assert.Equal("Bug", (string?)fields["issuetype"]!["name"]);
        Assert.Equal("Login fails", (string?)fields["summary"]);
        Assert.Equal("acct-1", (string?)fields["assignee"]!["accountId"]);
        Assert.Equal("High", (string?)fields["priority"]!["name"]);
        Assert.Equal("urgent", (string?)fields["labels"]![1]);
        Assert.Equal("PROJ-1", (string?)fields["parent"]!["key"]);
        Assert.Equal("Gold", (string?)fields["customfield_10010"]!["value"]);
        Assert.Equal("doc", (string?)fields["description"]!["type"]);
        Assert.Equal("bulletList", (string?)fields["description"]!["content"]![1]!["type"]);
    }

    [Fact]
    public async Task Create_issue_with_only_the_required_fields_sends_nothing_else()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue", """{"id":"1","key":"PROJ-3"}""", HttpStatusCode.Created, "POST");

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Task", summary = "Do it" });

        var fields = ((JsonObject)Sent(app, HttpMethod.Post, $"{Base}/issue")["fields"]!).Select(f => f.Key).Order().ToArray();
        Assert.Equal(["issuetype", "project", "summary"], fields.Select(f => f).ToArray());
    }

    [Fact]
    public async Task An_additional_field_cannot_override_a_dedicated_argument()
    {
        await using var app = App();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Bug", summary = "x", additionalFields = new { summary = "other" } });

        Assert.True(isError);
        Assert.Contains("dedicated argument", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("bad key", "Bug", "s")]
    [InlineData("PROJ", "", "s")]
    [InlineData("PROJ", "Bug", "")]
    [InlineData("PROJ", "Bug", "two\nlines")]
    public async Task Create_issue_refuses_bad_input_before_any_call(string project, string issueType, string summary)
    {
        await using var app = App();

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue", new { project, issueType, summary });

        Assert.True(isError);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_label_with_a_space_and_a_field_name_with_a_comma_are_refused()
    {
        await using var app = App();
        var client = app.ClientWith("alice-token");

        var (label, labelText) = await Mcp.CallToolAsync(client, Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Bug", summary = "x", labels = new[] { "two words" } });
        var (field, _) = await Mcp.CallToolAsync(client, Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Bug", summary = "x", additionalFields = new Dictionary<string, string> { ["a,b"] = "x" } });

        Assert.True(label);
        Assert.Contains("no spaces", labelText);
        Assert.True(field);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Jira_validation_errors_come_back_readable()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue", """{"errorMessages":[],"errors":{"summary":"Summary is required.","customfield_10010":"Field is required."}}""",
            HttpStatusCode.BadRequest, "POST");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Bug", summary = "x" });

        Assert.True(isError);
        Assert.Contains("summary: Summary is required.", text);
        Assert.Contains("customfield_10010: Field is required.", text);
    }

    [Fact]
    public async Task A_missing_scope_is_explained_instead_of_blamed_on_the_sign_in()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue", """{"code":401,"message":"Unauthorized; scope does not match"}""",
            HttpStatusCode.Unauthorized, "POST");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Bug", summary = "x" });

        Assert.True(isError);
        Assert.Contains("scope does not match", text);
        Assert.Contains("Add the scope to your OAuth app", text);
        Assert.DoesNotContain("expired or been revoked", text);
    }

    [Fact]
    public async Task A_failed_create_is_not_retried()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue", """{"message":"boom"}""", HttpStatusCode.InternalServerError, "POST");

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_create_issue",
            new { project = "PROJ", issueType = "Bug", summary = "x" });

        Assert.True(isError);
        Assert.Equal(1, app.Upstream.Requests.Count(r => r.Method == HttpMethod.Post && r.Path == $"{Base}/issue"));
    }

    [Fact]
    public async Task Update_issue_changes_only_the_fields_passed()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-1", "", HttpStatusCode.NoContent, "PUT");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_update_issue",
            new { key = "proj-1", summary = "New title", description = "Now clearer.", labels = new[] { "a" }, priority = "Low" });

        Assert.True((bool)result["updated"]!);
        Assert.Equal("https://acme.atlassian.net/browse/PROJ-1", (string?)result["url"]);

        var fields = Sent(app, HttpMethod.Put, $"{Base}/issue/PROJ-1")["fields"]!;
        Assert.Equal("New title", (string?)fields["summary"]);
        Assert.Equal("Low", (string?)fields["priority"]!["name"]);
        Assert.Equal("Now clearer.", (string?)fields["description"]!["content"]![0]!["content"]![0]!["text"]);
        Assert.Null(fields["assignee"]);
        Assert.Null(fields["issuetype"]);
    }

    [Fact]
    public async Task An_empty_label_list_clears_labels_and_no_fields_is_refused()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-1", "", HttpStatusCode.NoContent, "PUT");
        var client = app.ClientWith("alice-token");

        await Mcp.CallOkAsync(client, Mcp.Jira, "jira_update_issue", new { key = "PROJ-1", labels = Array.Empty<string>() });
        var cleared = Sent(app, HttpMethod.Put, $"{Base}/issue/PROJ-1")["fields"]!["labels"]!.AsArray();
        var puts = app.Upstream.Requests.Count(r => r.Method == HttpMethod.Put);

        var (isError, text) = await Mcp.CallToolAsync(client, Mcp.Jira, "jira_update_issue", new { key = "PROJ-1" });

        Assert.Empty(cleared);
        Assert.True(isError);
        Assert.Contains("at least one field", text);
        Assert.Equal(puts, app.Upstream.Requests.Count(r => r.Method == HttpMethod.Put));
    }

    [Fact]
    public async Task Transition_matches_by_target_status_name_id_or_transition_name_and_can_comment()
    {
        await using var app = App();
        app.Upstream
            .Json(Host, $"{Base}/issue/PROJ-1/transitions",
                """{"transitions":[{"id":"31","name":"Complete","to":{"name":"Done"}},{"id":"21","name":"Start","to":{"name":"In Progress"}}]}""",
                method: "GET")
            .Json(Host, $"{Base}/issue/PROJ-1/transitions", "", HttpStatusCode.NoContent, "POST");
        var client = app.ClientWith("alice-token");

        var byStatus = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_transition_issue",
            new { key = "PROJ-1", transition = "done", comment = "Shipped in 2.4." });
        var first = Sent(app, HttpMethod.Post, $"{Base}/issue/PROJ-1/transitions");

        await Mcp.CallOkAsync(client, Mcp.Jira, "jira_transition_issue", new { key = "PROJ-1", transition = "21" });
        var second = Sent(app, HttpMethod.Post, $"{Base}/issue/PROJ-1/transitions");

        await Mcp.CallOkAsync(client, Mcp.Jira, "jira_transition_issue", new { key = "PROJ-1", transition = "Start" });

        Assert.Equal("Complete", (string?)byStatus["transition"]);
        Assert.Equal("Done", (string?)byStatus["toStatus"]);
        Assert.Equal("31", (string?)first["transition"]!["id"]);
        Assert.Equal("Shipped in 2.4.", (string?)first["update"]!["comment"]![0]!["add"]!["body"]!["content"]![0]!["content"]![0]!["text"]);
        Assert.Equal("21", (string?)second["transition"]!["id"]);
        Assert.Null(second["update"]);
    }

    [Fact]
    public async Task An_unavailable_transition_lists_what_is_available_and_changes_nothing()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-1/transitions",
            """{"transitions":[{"id":"31","name":"Complete","to":{"name":"Done"}}]}""", method: "GET");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_transition_issue", new { key = "PROJ-1", transition = "Reopen" });

        Assert.True(isError);
        Assert.Contains("no transition 'Reopen'", text);
        Assert.Contains("Complete (to Done)", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Add_comment_posts_rich_text_and_links_to_the_comment()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-1/comment", """{"id":"20001"}""", HttpStatusCode.Created, "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_add_comment",
            new { key = "PROJ-1", comment = "Looks good.\nShip it." });

        Assert.Equal("20001", (string?)result["commentId"]);
        Assert.Equal("https://acme.atlassian.net/browse/PROJ-1?focusedCommentId=20001", (string?)result["url"]);

        var body = Sent(app, HttpMethod.Post, $"{Base}/issue/PROJ-1/comment")["body"]!;
        Assert.Equal("doc", (string?)body["type"]);
        Assert.Equal("hardBreak", (string?)body["content"]![0]!["content"]![1]!["type"]);
    }

    [Fact]
    public async Task Assign_sets_or_clears_the_assignee()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/issue/PROJ-1/assignee", "", HttpStatusCode.NoContent, "PUT");
        var client = app.ClientWith("alice-token");

        var set = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_assign_issue", new { key = "PROJ-1", accountId = "712020:abc-123" });
        Assert.Equal("712020:abc-123", (string?)Sent(app, HttpMethod.Put, $"{Base}/issue/PROJ-1/assignee")["accountId"]);

        var cleared = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_assign_issue", new { key = "PROJ-1", accountId = "Unassigned" });
        var body = Sent(app, HttpMethod.Put, $"{Base}/issue/PROJ-1/assignee");

        Assert.True((bool)set["assigned"]!);
        Assert.False((bool)cleared["assigned"]!);
        Assert.True((bool)cleared["unassigned"]!);
        Assert.True(body is JsonObject o && o.ContainsKey("accountId") && o["accountId"] is null);
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("../x")]
    [InlineData("")]
    public async Task Assign_refuses_an_account_id_that_is_not_one(string accountId)
    {
        await using var app = App();

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_assign_issue", new { key = "PROJ-1", accountId });

        Assert.True(isError);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Create_metadata_lists_issue_types_then_the_required_fields_first()
    {
        await using var app = new TestApp();
        app.Upstream
            .Json(Host, $"{Base}/issue/createmeta/PROJ/issuetypes",
                """{"issueTypes":[{"id":"10001","name":"Bug","subtask":false,"description":"A defect"},{"id":"10002","name":"Task","subtask":false}]}""")
            .Json(Host, $"{Base}/issue/createmeta/PROJ/issuetypes/10001", """
                {"fields":[
                  {"fieldId":"labels","name":"Labels","required":false,"schema":{"type":"array","items":"string"}},
                  {"fieldId":"summary","name":"Summary","required":true,"schema":{"type":"string"}},
                  {"fieldId":"priority","name":"Priority","required":false,"hasDefaultValue":true,"schema":{"type":"priority"},
                   "allowedValues":[{"id":"1","name":"High"},{"id":"2","name":"Low"}]}]}
                """);
        var client = app.ClientWith("alice-token");

        var types = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_get_create_metadata", new { project = "proj" });
        var fields = await Mcp.CallOkAsync(client, Mcp.Jira, "jira_get_create_metadata", new { project = "PROJ", issueType = "bug" });

        Assert.Equal(2, types["issueTypes"]!.AsArray().Count);
        Assert.Equal("Bug", (string?)types["issueTypes"]![0]!["name"]);
        Assert.Equal("10001", (string?)fields["issueTypeId"]);
        Assert.Equal("summary", (string?)fields["fields"]![0]!["id"]);
        Assert.True((bool)fields["fields"]![0]!["required"]!);
        Assert.Equal("array", (string?)fields["fields"]![1]!["type"] ?? (string?)fields["fields"]![2]!["type"]);
        var priority = fields["fields"]!.AsArray().First(f => (string?)f!["id"] == "priority")!;
        Assert.Equal("High", (string?)priority["allowedValues"]![0]);
        Assert.True((bool)priority["hasDefault"]!);
    }

    [Fact]
    public async Task Create_metadata_for_an_unknown_type_lists_the_known_ones()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/issue/createmeta/PROJ/issuetypes", """{"issueTypes":[{"id":"10001","name":"Bug"}]}""");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "jira_get_create_metadata", new { project = "PROJ", issueType = "Epic" });

        Assert.True(isError);
        Assert.Contains("no issue type 'Epic'", text);
        Assert.Contains("Bug", text);
    }
}

public class ConfluenceWriteToolTests
{
    private const string Host = FakeUpstream.AtlassianHost;
    private static readonly string Base = $"/ex/confluence/{FakeUpstream.CloudId}/wiki";

    private static TestApp App() => new TestApp().With("Confluence:EnableWriteTools", "true");

    private static JsonNode Sent(TestApp app, HttpMethod method, string path) =>
        JsonNode.Parse(app.Upstream.LastBy(method, Host, path).Body!)!;

    private static void ExistingPage(TestApp app, string storage = "<p>Old</p>") =>
        app.Upstream
            .Json(Host, $"{Base}/api/v2/pages/123",
                """{"id":"123","status":"current","title":"Runbook","version":{"number":4},"body":{"storage":{"value":"""
                + "\"" + storage + "\"" + "}}}",
                method: "GET")
            .Json(Host, $"{Base}/api/v2/pages/123", """
                {"id":"123","title":"Runbook","spaceId":"98","version":{"number":5},"_links":{"webui":"/spaces/ENG/pages/123/Runbook"}}
                """, method: "PUT");

    [Fact]
    public async Task Create_page_sends_escaped_storage_format_and_returns_a_link()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/api/v2/pages", """
            {"id":"900","title":"Runbook","spaceId":"98","version":{"number":1},"_links":{"webui":"/spaces/ENG/pages/900/Runbook"}}
            """, method: "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_create_page",
            new { spaceId = "98", title = "Runbook", body = "Hello & welcome", parentId = "100" });

        Assert.True((bool)result["created"]!);
        Assert.Equal("900", (string?)result["id"]);
        Assert.Equal("https://acme.atlassian.net/wiki/spaces/ENG/pages/900/Runbook", (string?)result["url"]);

        var seen = app.Upstream.LastBy(HttpMethod.Post, Host, $"{Base}/api/v2/pages");
        Assert.Equal("alice-token", seen.Bearer);
        var sent = JsonNode.Parse(seen.Body!)!;
        Assert.Equal("98", (string?)sent["spaceId"]);
        Assert.Equal("current", (string?)sent["status"]);
        Assert.Equal("100", (string?)sent["parentId"]);
        Assert.Equal("storage", (string?)sent["body"]!["representation"]);
        Assert.Equal("<p>Hello &amp; welcome</p>", (string?)sent["body"]!["value"]);
    }

    [Fact]
    public async Task Markup_in_the_body_is_escaped_and_code_stays_inside_a_code_macro()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/api/v2/pages", """{"id":"1","title":"t"}""", method: "POST");

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_create_page",
            new { spaceId = "98", title = "t", body = "<script>alert(1)</script>\n\n```cs\nvar a = \"]]>\";\n```" });

        var value = (string)Sent(app, HttpMethod.Post, $"{Base}/api/v2/pages")["body"]!["value"]!;
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", value);
        Assert.DoesNotContain("<script>", value);
        Assert.Contains("<ac:structured-macro ac:name=\"code\"><ac:parameter ac:name=\"language\">cs</ac:parameter>", value);
        Assert.Contains("]]]]><![CDATA[>", value);
    }

    [Theory]
    [InlineData("98x", "t", "b")]
    [InlineData("98", "", "b")]
    [InlineData("98", "two\nlines", "b")]
    [InlineData("98", "t", "")]
    public async Task Create_page_refuses_bad_input_before_any_call(string spaceId, string title, string body)
    {
        await using var app = App();

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_create_page", new { spaceId, title, body });

        Assert.True(isError);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Update_page_appends_by_default_and_keeps_what_is_there()
    {
        await using var app = App();
        ExistingPage(app);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_update_page",
            new { pageId = "123", body = "New text" });

        var sent = Sent(app, HttpMethod.Put, $"{Base}/api/v2/pages/123");
        Assert.Equal(5, (int)sent["version"]!["number"]!);
        Assert.Equal("Runbook", (string?)sent["title"]);
        Assert.Equal("<p>Old</p><p>New text</p>", (string?)sent["body"]!["value"]);
        Assert.Equal("123", (string?)sent["id"]);
        Assert.True((bool)result["updated"]!);
        Assert.Equal(5, (int)result["version"]!);
    }

    [Fact]
    public async Task Replace_mode_overwrites_the_body()
    {
        await using var app = App();
        ExistingPage(app, "<p>Old</p><ac:structured-macro ac:name='toc' />");

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_update_page",
            new { pageId = "123", body = "New text", mode = "replace" });

        Assert.Equal("<p>New text</p>", (string?)Sent(app, HttpMethod.Put, $"{Base}/api/v2/pages/123")["body"]!["value"]);
    }

    [Fact]
    public async Task A_title_only_update_keeps_the_body_untouched()
    {
        await using var app = App();
        ExistingPage(app);

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_update_page", new { pageId = "123", title = "New title" });

        var sent = Sent(app, HttpMethod.Put, $"{Base}/api/v2/pages/123");
        Assert.Equal("New title", (string?)sent["title"]);
        Assert.Equal("<p>Old</p>", (string?)sent["body"]!["value"]);
    }

    [Fact]
    public async Task Update_page_refuses_nothing_to_change_and_unknown_modes_without_calling()
    {
        await using var app = App();
        var client = app.ClientWith("alice-token");

        var (none, noneText) = await Mcp.CallToolAsync(client, Mcp.Confluence, "confluence_update_page", new { pageId = "123" });
        var (mode, modeText) = await Mcp.CallToolAsync(client, Mcp.Confluence, "confluence_update_page", new { pageId = "123", body = "x", mode = "merge" });

        Assert.True(none);
        Assert.Contains("body, a title, or both", noneText);
        Assert.True(mode);
        Assert.Contains("append or replace", modeText);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("/api/v2/pages/"));
    }

    [Fact]
    public async Task A_version_conflict_tells_the_model_to_read_the_page_again()
    {
        await using var app = App();
        ExistingPage(app);
        app.Upstream.Json(Host, $"{Base}/api/v2/pages/123",
            """{"errors":[{"status":409,"title":"Version conflict","detail":"The page was updated by someone else."}]}""",
            HttpStatusCode.Conflict, "PUT");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_update_page", new { pageId = "123", body = "x" });

        Assert.True(isError);
        Assert.Contains("updated by someone else", text);
        Assert.Contains("Someone else changed it first", text);
    }

    [Fact]
    public async Task Add_comment_posts_a_footer_comment()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Base}/api/v2/footer-comments",
            """{"id":"c1","_links":{"webui":"/spaces/ENG/pages/123/Runbook?focusedCommentId=c1"}}""", method: "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_add_comment",
            new { pageId = "123", comment = "Reviewed." });

        var sent = Sent(app, HttpMethod.Post, $"{Base}/api/v2/footer-comments");
        Assert.Equal("123", (string?)sent["pageId"]);
        Assert.Equal("<p>Reviewed.</p>", (string?)sent["body"]!["value"]);
        Assert.Equal("c1", (string?)result["commentId"]);
        Assert.Contains("focusedCommentId=c1", (string?)result["url"]);
    }
}

public class BitbucketWriteToolTests
{
    private const string Host = FakeUpstream.BitbucketHost;
    private const string Repo = "/2.0/repositories/myteam/api";

    private static TestApp App() => new TestApp().With("Bitbucket:EnableWriteTools", "true");

    private static JsonNode Sent(TestApp app, string path) =>
        JsonNode.Parse(app.Upstream.LastBy(HttpMethod.Post, Host, path).Body!)!;

    [Fact]
    public async Task A_general_comment_has_no_inline_position()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Repo}/pullrequests/42/comments",
            """{"id":7,"user":{"display_name":"Alice"},"links":{"html":{"href":"https://bitbucket.org/myteam/api/pull-requests/42#comment-7"}}}""",
            HttpStatusCode.Created, "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_create_pull_request_comment",
            new { workspace = "myteam", repository = "api", pullRequestId = "42", comment = "Nice work." });

        var sent = Sent(app, $"{Repo}/pullrequests/42/comments");
        Assert.Equal("Nice work.", (string?)sent["content"]!["raw"]);
        Assert.Null(sent["inline"]);
        Assert.Null(sent["parent"]);
        Assert.Equal(7, (int)result["id"]!);
        Assert.Equal("Alice", (string?)result["author"]);
        Assert.Equal("bb-alice", app.Upstream.LastBy(HttpMethod.Post, Host, $"{Repo}/pullrequests/42/comments").Bearer);
    }

    [Fact]
    public async Task An_inline_reply_carries_the_file_line_and_parent()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Repo}/pullrequests/42/comments", """{"id":8}""", HttpStatusCode.Created, "POST");

        await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_create_pull_request_comment",
            new { workspace = "myteam", repository = "api", pullRequestId = "42", comment = "Rename this.", path = "src/a.cs", line = 12, replyTo = 3 });

        var sent = Sent(app, $"{Repo}/pullrequests/42/comments");
        Assert.Equal("src/a.cs", (string?)sent["inline"]!["path"]);
        Assert.Equal(12, (int)sent["inline"]!["to"]!);
        Assert.Equal(3, (int)sent["parent"]!["id"]!);
    }

    [Theory]
    [InlineData("src/a.cs", null)]
    [InlineData(null, 5)]
    [InlineData("../secrets", 5)]
    [InlineData("src/a.cs", 0)]
    public async Task Inline_comments_need_a_real_path_and_line_together(string? path, int? line)
    {
        await using var app = App();

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_create_pull_request_comment",
            new { workspace = "myteam", repository = "api", pullRequestId = "42", comment = "x", path, line });

        Assert.True(isError);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/comments"));
    }

    [Fact]
    public async Task Create_pull_request_sends_branches_reviewers_and_options()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Repo}/pullrequests", """
            {"id":43,"title":"Add caching","state":"OPEN","source":{"branch":{"name":"feature/cache"}},
             "destination":{"branch":{"name":"main"}},"links":{"html":{"href":"https://bitbucket.org/myteam/api/pull-requests/43"}}}
            """, HttpStatusCode.Created, "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_create_pull_request",
            new
            {
                workspace = "myteam",
                repository = "api",
                title = "Add caching",
                sourceBranch = "feature/cache",
                destinationBranch = "main",
                description = "Speeds things up.",
                closeSourceBranch = true,
                reviewerUuids = new[] { "{1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f}" }
            });

        var sent = Sent(app, $"{Repo}/pullrequests");
        Assert.Equal("Add caching", (string?)sent["title"]);
        Assert.Equal("feature/cache", (string?)sent["source"]!["branch"]!["name"]);
        Assert.Equal("main", (string?)sent["destination"]!["branch"]!["name"]);
        Assert.Equal("Speeds things up.", (string?)sent["description"]);
        Assert.True((bool)sent["close_source_branch"]!);
        Assert.Equal("{1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f}", (string?)sent["reviewers"]![0]!["uuid"]);
        Assert.Equal(43, (int)result["id"]!);
        Assert.Equal("https://bitbucket.org/myteam/api/pull-requests/43", (string?)result["url"]);
    }

    [Fact]
    public async Task The_destination_defaults_to_the_main_branch_by_being_left_out()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Repo}/pullrequests", """{"id":44}""", HttpStatusCode.Created, "POST");

        await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_create_pull_request",
            new { workspace = "myteam", repository = "api", title = "t", sourceBranch = "feature/x" });

        var sent = Sent(app, $"{Repo}/pullrequests");
        Assert.Null(sent["destination"]);
        Assert.Null(sent["reviewers"]);
        Assert.False((bool)sent["close_source_branch"]!);
    }

    [Theory]
    [InlineData("a..b", "{1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f}")]
    [InlineData("feature/x", "not-a-uuid")]
    [InlineData("feature/x", "1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f")]
    public async Task Create_pull_request_refuses_a_bad_branch_or_reviewer_before_any_call(string branch, string reviewer)
    {
        await using var app = App();

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_create_pull_request",
            new { workspace = "myteam", repository = "api", title = "t", sourceBranch = branch, reviewerUuids = new[] { reviewer } });

        Assert.True(isError);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/pullrequests"));
    }

    [Fact]
    public async Task Approve_posts_without_a_body_and_reports_who_approved()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Repo}/pullrequests/42/approve",
            """{"approved":true,"role":"PARTICIPANT","user":{"display_name":"Alice"}}""", method: "POST");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_approve_pull_request",
            new { workspace = "myteam", repository = "api", pullRequestId = "42" });

        Assert.True((bool)result["approved"]!);
        Assert.Equal("Alice", (string?)result["by"]);
        Assert.Null(app.Upstream.LastBy(HttpMethod.Post, Host, $"{Repo}/pullrequests/42/approve").Body);
    }

    [Fact]
    public async Task A_refused_write_names_the_scope_the_sign_in_lacks()
    {
        await using var app = App();
        app.Upstream.Json(Host, $"{Repo}/pullrequests/42/approve",
            """{"type":"error","error":{"message":"Your credentials lack one or more required privilege scopes.","detail":{"granted":["pullrequest"],"required":["pullrequest:write"]}}}""",
            HttpStatusCode.Forbidden, "POST");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_approve_pull_request",
            new { workspace = "myteam", repository = "api", pullRequestId = "42" });

        Assert.True(isError);
        Assert.Contains("Required scopes: pullrequest:write.", text);
    }

    [Fact]
    public async Task A_pinned_workspace_applies_to_writes_too()
    {
        await using var app = App().With("Bitbucket:Workspace", "myteam");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_approve_pull_request",
            new { workspace = "other", repository = "api", pullRequestId = "1" });

        Assert.True(isError);
        Assert.Contains("limited to the workspace 'myteam'", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }
}

public class WriteSwitchTests
{
    [Fact]
    public async Task Write_tools_do_not_exist_until_they_are_switched_on()
    {
        await using var app = new TestApp();

        Assert.Equal(HostTests.JiraTools, await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Jira));
        Assert.Equal(HostTests.ConfluenceTools, await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Confluence));
        Assert.Equal(HostTests.BitbucketTools, await Mcp.ListToolNamesAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket));
    }

    [Fact]
    public async Task A_write_tool_that_is_switched_off_cannot_be_called()
    {
        await using var app = new TestApp();

        var (response, result) = await Mcp.CallAsync(
            app.ClientWith("alice-token"), Mcp.Jira, "tools/call",
            new { name = "jira_create_issue", arguments = new { project = "PROJ", issueType = "Bug", summary = "x" } });

        Assert.True(response.IsSuccessStatusCode);
        Assert.True(result!.Value.TryGetProperty("error", out _)
            || result.Value.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Each_product_is_switched_on_on_its_own()
    {
        await using var app = new TestApp().With("Confluence:EnableWriteTools", "true");

        Assert.Equal(HostTests.JiraTools, await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Jira));
        Assert.Equal(
            HostTests.ConfluenceTools.Concat(HostTests.ConfluenceWriteTools).Order().ToArray(),
            await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Confluence));
        Assert.Equal(HostTests.BitbucketTools, await Mcp.ListToolNamesAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket));
    }

    [Fact]
    public async Task All_write_tools_appear_on_their_own_endpoint_only()
    {
        await using var app = new TestApp()
            .With("Jira:EnableWriteTools", "true")
            .With("Confluence:EnableWriteTools", "true")
            .With("Bitbucket:EnableWriteTools", "true");

        Assert.Equal(
            HostTests.JiraTools.Concat(HostTests.JiraWriteTools).Order().ToArray(),
            await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Jira));
        Assert.Equal(
            HostTests.ConfluenceTools.Concat(HostTests.ConfluenceWriteTools).Order().ToArray(),
            await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Confluence));
        Assert.Equal(
            HostTests.BitbucketTools.Concat(HostTests.BitbucketWriteTools).Order().ToArray(),
            await Mcp.ListToolNamesAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket));
    }

    [Fact]
    public async Task Write_tools_are_marked_as_changing_data()
    {
        await using var app = new TestApp().With("Jira:EnableWriteTools", "true");

        var (_, result) = await Mcp.CallAsync(app.ClientWith("alice-token"), Mcp.Jira, "tools/list");

        foreach (var tool in result!.Value.GetProperty("result").GetProperty("tools").EnumerateArray())
        {
            var name = tool.GetProperty("name").GetString()!;
            var readOnly = tool.GetProperty("annotations").TryGetProperty("readOnlyHint", out var hint) && hint.GetBoolean();
            Assert.Equal(!HostTests.JiraWriteTools.Contains(name), readOnly);
        }
    }

    [Theory]
    [InlineData(Mcp.Jira, "Jira", "write:jira-work")]
    [InlineData(Mcp.Confluence, "Confluence", "write:page:confluence")]
    [InlineData(Mcp.Bitbucket, "Bitbucket", "pullrequest:write")]
    public async Task Write_scopes_are_advertised_only_when_write_tools_are_on(string endpoint, string product, string scope)
    {
        await using var off = new TestApp();
        await using var on = new TestApp().With($"{product}:EnableWriteTools", "true");

        Assert.DoesNotContain(scope, await Scopes(off, endpoint));
        Assert.Contains(scope, await Scopes(on, endpoint));
    }

    private static async Task<string[]> Scopes(TestApp app, string endpoint)
    {
        var response = await app.ClientWith().GetAsync("/.well-known/oauth-protected-resource" + endpoint);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()!).ToArray();
    }
}

public class AuthorizeEndpointTests
{
    private const string Redirect = "https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect";

    private static string Url(string extra = "") =>
        "/atlassian/oauth/authorize?client_id=abc123&response_type=code&redirect_uri=" + Uri.EscapeDataString(Redirect)
        + "&scope=" + Uri.EscapeDataString("read:jira-work write:jira-work offline_access")
        + "&state=xyz&code_challenge=cc&code_challenge_method=S256" + extra;

    [Fact]
    public async Task It_redirects_to_atlassian_with_the_audience_added()
    {
        await using var app = new TestApp();

        var response = await app.NoRedirectClient().GetAsync(Url());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("auth.atlassian.com", location.Host);
        Assert.Equal("/authorize", location.AbsolutePath);

        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("api.atlassian.com", query["audience"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("abc123", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.Equal("read:jira-work write:jira-work offline_access", query["scope"]);
        Assert.Equal("xyz", query["state"]);
        Assert.Equal("cc", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    [Fact]
    public async Task A_caller_cannot_choose_the_audience_the_prompt_or_add_parameters()
    {
        await using var app = new TestApp();

        var response = await app.NoRedirectClient().GetAsync(Url("&audience=evil.example&prompt=none&foo=bar&response_mode=fragment"));

        var query = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal(["api.atlassian.com"], query.GetValues("audience")!);
        Assert.Equal(["consent"], query.GetValues("prompt")!);
        Assert.Null(query["foo"]);
        Assert.Null(query["response_mode"]);
    }

    [Fact]
    public async Task It_works_before_anyone_has_signed_in()
    {
        await using var app = new TestApp().With("Auth:Enabled", "true");

        var response = await app.NoRedirectClient().GetAsync(Url());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("/atlassian/oauth/authorize?redirect_uri=https%3A%2F%2Fx&response_type=code", "client_id")]
    [InlineData("/atlassian/oauth/authorize?client_id=a&response_type=code", "redirect_uri")]
    [InlineData("/atlassian/oauth/authorize?client_id=a&redirect_uri=https%3A%2F%2Fx", "response_type")]
    public async Task A_request_missing_a_required_parameter_is_refused(string url, string missing)
    {
        await using var app = new TestApp();

        var response = await app.NoRedirectClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(missing, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_the_authorization_code_flow_is_supported()
    {
        await using var app = new TestApp();

        var response = await app.NoRedirectClient().GetAsync("/atlassian/oauth/authorize?client_id=a&redirect_uri=https%3A%2F%2Fx&response_type=token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_request_is_refused()
    {
        await using var app = new TestApp();

        var response = await app.NoRedirectClient().GetAsync(Url("&state=" + new string('a', 5000)));

        Assert.Equal(HttpStatusCode.RequestUriTooLong, response.StatusCode);
    }

    [Fact]
    public async Task It_does_not_exist_when_only_bitbucket_is_served()
    {
        await using var app = new TestApp()
            .With("Jira:Enabled", "false")
            .With("Confluence:Enabled", "false")
            .With("Atlassian:SiteUrl", "");

        var response = await app.NoRedirectClient().GetAsync(Url());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class TextConversionTests
{
    private const string Sample =
        "First line\nsecond line\n\n# Heading\n\n- one\n- two\n\n1. alpha\n2. beta\n\n```csharp\nvar x = 1;\n```";

    [Fact]
    public void Text_is_split_into_blocks()
    {
        var blocks = TextBlocks.Parse(Sample);

        Assert.Equal(5, blocks.Count);
        Assert.Equal(["First line", "second line"], Assert.IsType<ParagraphBlock>(blocks[0]).Lines);
        Assert.Equal(1, Assert.IsType<HeadingBlock>(blocks[1]).Level);
        Assert.False(Assert.IsType<ListBlock>(blocks[2]).Ordered);
        Assert.True(Assert.IsType<ListBlock>(blocks[3]).Ordered);
        Assert.Equal("csharp", Assert.IsType<CodeBlock>(blocks[4]).Language);
        Assert.Equal("var x = 1;", ((CodeBlock)blocks[4]).Code);
    }

    [Fact]
    public void Switching_list_type_starts_a_new_list_and_an_unclosed_fence_runs_to_the_end()
    {
        var blocks = TextBlocks.Parse("- a\n1. b\n\n```\ncode\nmore");

        Assert.Equal(3, blocks.Count);
        Assert.False(Assert.IsType<ListBlock>(blocks[0]).Ordered);
        Assert.True(Assert.IsType<ListBlock>(blocks[1]).Ordered);
        Assert.Equal("code\nmore", Assert.IsType<CodeBlock>(blocks[2]).Code);
    }

    [Theory]
    [InlineData("#hashtag")]
    [InlineData("---")]
    [InlineData("-not a list")]
    public void Lookalikes_stay_paragraphs(string text) =>
        Assert.IsType<ParagraphBlock>(Assert.Single(TextBlocks.Parse(text)));

    [Fact]
    public void Adf_is_built_with_the_structure_jira_expects()
    {
        var doc = Atlassian.Mcp.Jira.Adf.FromText(Sample);

        Assert.Equal(1, (int)doc["version"]!);
        var content = doc["content"]!.AsArray();
        Assert.Equal(["paragraph", "heading", "bulletList", "orderedList", "codeBlock"], content.Select(c => (string)c!["type"]!).ToArray());
        Assert.Equal("hardBreak", (string?)content[0]!["content"]![1]!["type"]);
        Assert.Equal(1, (int)content[1]!["attrs"]!["level"]!);
        Assert.Equal("two", (string?)content[2]!["content"]![1]!["content"]![0]!["content"]![0]!["text"]);
        Assert.Equal("csharp", (string?)content[4]!["attrs"]!["language"]);
        Assert.Equal("var x = 1;", (string?)content[4]!["content"]![0]!["text"]);
    }

    [Fact]
    public void An_empty_code_block_has_no_empty_text_node()
    {
        var doc = Atlassian.Mcp.Jira.Adf.FromText("```\n```");

        Assert.Null(doc["content"]![0]!["content"]);
    }

    [Fact]
    public void Text_survives_a_trip_through_adf_and_back()
    {
        var text = Atlassian.Mcp.Jira.Adf.ToText(Atlassian.Mcp.Jira.Adf.FromText(Sample));

        Assert.Contains("First line", text);
        Assert.Contains("# Heading", text);
        Assert.Contains("- two", text);
        Assert.Contains("2. beta", text);
        Assert.Contains("var x = 1;", text);
    }

    [Fact]
    public void Storage_format_is_built_and_escaped()
    {
        var html = StorageFormat.FromText("A & B\nline <two>\n\n# Title\n\n- x\n\n```\nif (a < b) {}\n```");

        Assert.Contains("<p>A &amp; B<br />line &lt;two&gt;</p>", html);
        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<ul><li>x</li></ul>", html);
        Assert.Contains("<![CDATA[if (a < b) {}]]>", html);
        Assert.DoesNotContain("<two>", html);
    }

    [Fact]
    public void Text_survives_a_trip_through_storage_format_and_back()
    {
        var text = StorageFormat.ToText(StorageFormat.FromText(Sample));

        Assert.Contains("First line", text);
        Assert.Contains("# Heading", text);
        Assert.Contains("- two", text);
        Assert.Contains("var x = 1;", text);
    }

    [Fact]
    public void New_validators_accept_real_shapes_and_refuse_the_rest()
    {
        Assert.Equal("712020:0f1e-2d3c", Names.AccountId("712020:0f1e-2d3c"));
        Assert.Equal("{1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f}", Names.Uuid("{1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f}"));
        Assert.Equal("urgent", Names.Label("urgent"));
        Assert.Equal("a b", Names.Line(" a b ", "title"));
        Assert.Equal("l1\nl2", Names.Body("l1\nl2", "body", 100));
        Assert.Equal("src/a b.cs", Names.RepoPath("/src/a b.cs/"));

        Assert.Throws<McpException>(() => Names.AccountId("a b"));
        Assert.Throws<McpException>(() => Names.AccountId("x\ny"));
        Assert.Equal("x", Names.AccountId("x\n"));
        Assert.Throws<McpException>(() => Names.Uuid("1a2b3c4d-1a2b-1a2b-1a2b-1a2b3c4d5e6f"));
        Assert.Throws<McpException>(() => Names.Label("a b"));
        Assert.Throws<McpException>(() => Names.Line("a\nb", "title"));
        Assert.Throws<McpException>(() => Names.Line(new string('a', 300), "title"));
        Assert.Throws<McpException>(() => Names.Body("a\0b", "body", 100));
        Assert.Throws<McpException>(() => Names.Body("   ", "body", 100));
        Assert.Throws<McpException>(() => Names.Body(new string('a', 101), "body", 100));
        Assert.Throws<McpException>(() => Names.RepoPath("a/../b"));
    }
}

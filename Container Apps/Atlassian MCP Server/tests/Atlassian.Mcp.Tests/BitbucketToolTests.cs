using System.Net;

namespace Atlassian.Mcp.Tests;

public class BitbucketToolTests
{
    private const string Host = FakeUpstream.BitbucketHost;
    private const string Base = "/2.0";

    [Fact]
    public async Task Listing_repositories_pages_by_number_and_filters_by_name()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam", """
            {"values":[{"full_name":"myteam/api","name":"api","slug":"api","is_private":true,"language":"csharp",
              "mainbranch":{"name":"main"},"project":{"name":"Core"},"updated_on":"2026-10-01T10:00:00+00:00",
              "links":{"html":{"href":"https://bitbucket.org/myteam/api"}}}],
             "page":1,"pagelen":25,"next":"https://api.bitbucket.org/2.0/repositories/myteam?page=2"}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_repositories",
            new { workspace = "myteam", nameContains = "api" });

        var repo = result["repositories"]![0]!;
        Assert.Equal("myteam/api", (string?)repo["fullName"]);
        Assert.Equal("main", (string?)repo["mainBranch"]);
        Assert.Equal("https://bitbucket.org/myteam/api", (string?)repo["url"]);
        Assert.True((bool)result["hasMore"]!);
        Assert.Equal(2, (int)result["nextPage"]!);

        var seen = app.Upstream.Last(Host, $"{Base}/repositories/myteam");
        Assert.Equal("bb-alice", seen.Bearer);
        Assert.Contains("q=name%20~%20%22api%22", seen.Query);
        Assert.Contains("page=1", seen.Query);
        Assert.Contains("pagelen=25", seen.Query);
    }

    [Fact]
    public async Task The_last_page_says_there_is_no_more()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/workspaces", """{"values":[{"slug":"myteam","name":"My Team","uuid":"{w}"}],"page":1,"pagelen":25}""");

        var result = await Mcp.CallOkAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_workspaces");

        Assert.False((bool)result["hasMore"]!);
        Assert.Null(result["nextPage"]);
        Assert.Equal("myteam", (string?)result["workspaces"]![0]!["slug"]);
    }

    [Fact]
    public async Task An_empty_list_is_a_success()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam", """{"values":[],"page":1,"pagelen":25}""");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_repositories", new { workspace = "myteam" });

        Assert.Equal(0, (int)result["returned"]!);
    }

    [Fact]
    public async Task A_pinned_workspace_refuses_any_other()
    {
        await using var app = new TestApp().With("Bitbucket:Workspace", "myteam");
        app.Upstream.Json(Host, $"{Base}/repositories/myteam", """{"values":[]}""");
        var client = app.ClientWith("bb-alice");

        var (refused, text) = await Mcp.CallToolAsync(client, Mcp.Bitbucket, "bitbucket_list_repositories", new { workspace = "someone-else" });
        await Mcp.CallOkAsync(client, Mcp.Bitbucket, "bitbucket_list_repositories", new { workspace = "MyTeam" });

        Assert.True(refused);
        Assert.Contains("limited to the workspace 'myteam'", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("someone-else"));
    }

    [Theory]
    [InlineData("my team")]
    [InlineData("../admin")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task A_workspace_that_is_not_a_slug_is_refused_before_any_call(string workspace)
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_repositories", new { workspace });

        Assert.True(isError);
        Assert.Contains("workspace", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.StartsWith("/2.0/repositories"));
    }

    [Fact]
    public async Task A_name_filter_cannot_break_out_of_its_quotes()
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_repositories",
            new { workspace = "myteam", nameContains = "x\" OR is_private = true OR name ~ \"" });

        Assert.True(isError);
        Assert.Contains("name filter", text);
    }

    [Fact]
    public async Task Pull_request_details_include_reviewers_and_a_link()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam/api/pullrequests/42", """
            {"id":42,"title":"Add caching","state":"OPEN","description":"Speeds things up.",
             "author":{"display_name":"Alice"},"source":{"branch":{"name":"feature/cache"}},
             "destination":{"branch":{"name":"main"}},"comment_count":3,"close_source_branch":true,
             "participants":[{"role":"REVIEWER","approved":true,"state":"approved","user":{"display_name":"Bob"}},
                             {"role":"PARTICIPANT","approved":false,"user":{"display_name":"Carol"}}],
             "links":{"html":{"href":"https://bitbucket.org/myteam/api/pull-requests/42"}}}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_pull_request",
            new { workspace = "myteam", repository = "api", pullRequestId = "42" });

        Assert.Equal("Add caching", (string?)result["title"]);
        Assert.Equal("feature/cache", (string?)result["sourceBranch"]);
        Assert.Equal("Speeds things up.", (string?)result["description"]);
        Assert.Single(result["reviewers"]!.AsArray());
        Assert.Equal("Bob", (string?)result["reviewers"]![0]!["name"]);
        Assert.True((bool)result["reviewers"]![0]!["approved"]!);
        Assert.Equal("https://bitbucket.org/myteam/api/pull-requests/42", (string?)result["url"]);
    }

    [Fact]
    public async Task Listing_pull_requests_filters_by_state_and_refuses_unknown_states()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam/api/pullrequests", """{"values":[{"id":1,"title":"t","state":"MERGED"}]}""");
        var client = app.ClientWith("bb-alice");

        await Mcp.CallOkAsync(client, Mcp.Bitbucket, "bitbucket_list_pull_requests",
            new { workspace = "myteam", repository = "api", state = "merged" });
        var (bad, text) = await Mcp.CallToolAsync(client, Mcp.Bitbucket, "bitbucket_list_pull_requests",
            new { workspace = "myteam", repository = "api", state = "OPEN OR 1=1" });

        Assert.Contains("state=MERGED", app.Upstream.Last(Host, $"{Base}/repositories/myteam/api/pullrequests").Query);
        Assert.True(bad);
        Assert.Contains("must be OPEN", text);
    }

    [Fact]
    public async Task Diffstat_lists_changed_files_and_renames()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam/api/pullrequests/42/diffstat", """
            {"values":[{"status":"modified","old":{"path":"a.cs"},"new":{"path":"a.cs"},"lines_added":10,"lines_removed":2},
                       {"status":"renamed","old":{"path":"b.cs"},"new":{"path":"c.cs"},"lines_added":0,"lines_removed":0}]}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_pull_request_diffstat",
            new { workspace = "myteam", repository = "api", pullRequestId = "42" });

        var files = result["files"]!.AsArray();
        Assert.Equal("a.cs", (string?)files[0]!["path"]);
        Assert.Null(files[0]!["oldPath"]);
        Assert.Equal("b.cs", (string?)files[1]!["oldPath"]);
        Assert.Equal(10, (int)files[0]!["linesAdded"]!);
    }

    [Fact]
    public async Task A_large_diff_is_cut_and_says_so()
    {
        await using var app = new TestApp().With("Limits:MaxTextChars", "1000");
        app.Upstream.Text(Host, $"{Base}/repositories/myteam/api/pullrequests/42/diff", "diff --git a/x b/x\n" + new string('+', 5000));

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_pull_request_diff",
            new { workspace = "myteam", repository = "api", pullRequestId = "42" });

        Assert.Equal(1000, ((string)result["diff"]!).Length);
        Assert.True((bool)result["truncated"]!);
        Assert.True((int)result["characters"]! > 1000);
    }

    [Fact]
    public async Task Comments_show_inline_position_and_replies()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam/api/pullrequests/42/comments", """
            {"values":[{"id":7,"user":{"display_name":"Bob"},"created_on":"2026-10-02T10:00:00+00:00",
              "content":{"raw":"Please rename this."},"inline":{"path":"src/a.cs","to":12},"parent":{"id":3},
              "links":{"html":{"href":"https://bitbucket.org/myteam/api/pull-requests/42#comment-7"}}}]}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_pull_request_comments",
            new { workspace = "myteam", repository = "api", pullRequestId = "42" });

        var comment = result["comments"]![0]!;
        Assert.Equal("Please rename this.", (string?)comment["text"]);
        Assert.Equal("src/a.cs", (string?)comment["path"]);
        Assert.Equal(12, (int)comment["line"]!);
        Assert.Equal(3, (int)comment["replyTo"]!);
    }

    [Fact]
    public async Task Commits_can_be_listed_for_a_branch_with_a_slash_in_its_name()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam/api/commits/feature%2Fcache", """
            {"values":[{"hash":"abc123","date":"2026-10-01T10:00:00+00:00","message":"Add cache\n\nDetails","author":{"raw":"A <a@x>","user":{"display_name":"Alice"}},
              "links":{"html":{"href":"https://bitbucket.org/myteam/api/commits/abc123"}}}]}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_list_commits",
            new { workspace = "myteam", repository = "api", branch = "feature/cache" });

        Assert.Equal("abc123", (string?)result["commits"]![0]!["hash"]);
        Assert.Equal("Alice", (string?)result["commits"]![0]!["author"]);
    }

    [Fact]
    public async Task A_file_is_read_as_text_with_each_path_segment_escaped()
    {
        await using var app = new TestApp();
        app.Upstream.Text(Host, $"{Base}/repositories/myteam/api/src/main/docs/read%20me.md", "# Hello");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_file",
            new { workspace = "myteam", repository = "api", @ref = "main", path = "docs/read me.md" });

        Assert.Equal("# Hello", (string?)result["content"]);
        Assert.Equal("docs/read me.md", (string?)result["path"]);
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("docs/../../x")]
    [InlineData("docs//x")]
    [InlineData("")]
    public async Task A_file_path_that_climbs_out_is_refused_before_any_call(string path)
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_file",
            new { workspace = "myteam", repository = "api", @ref = "main", path });

        Assert.True(isError);
        Assert.Contains("path", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("/src/"));
    }

    [Fact]
    public async Task A_ref_that_climbs_out_is_refused()
    {
        await using var app = new TestApp();

        var (isError, _) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_file",
            new { workspace = "myteam", repository = "api", @ref = "../x", path = "a.txt" });

        Assert.True(isError);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("/src/"));
    }

    [Fact]
    public async Task A_binary_file_is_refused()
    {
        await using var app = new TestApp();
        app.Upstream.Text(Host, $"{Base}/repositories/myteam/api/src/main/logo.png", "PNG\0\0data");

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_file",
            new { workspace = "myteam", repository = "api", @ref = "main", path = "logo.png" });

        Assert.True(isError);
        Assert.Contains("binary", text);
    }

    [Fact]
    public async Task Bitbucket_errors_are_turned_into_actionable_messages()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/repositories/myteam/nope", """{"type":"error","error":{"message":"Repository myteam/nope not found"}}""", HttpStatusCode.NotFound);

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_repository",
            new { workspace = "myteam", repository = "nope" });

        Assert.True(isError);
        Assert.Contains("Repository myteam/nope not found", text);
        Assert.Contains("cannot see it", text);
    }

    [Fact]
    public async Task Current_user_is_reported_and_each_person_runs_as_themselves()
    {
        await using var app = new TestApp().With("Auth:ValidationCacheSeconds", "0");
        app.Upstream.Route(Host, $"{Base}/user", request =>
        {
            var token = request.Headers.Authorization!.Parameter!;
            var name = token == "bb-alice" ? "Alice" : "Bob";
            return FakeUpstream.Respond(HttpStatusCode.OK, $$"""{"display_name":"{{name}}","uuid":"{{token}}","account_id":"a-{{name}}"}""");
        });

        var alice = await Mcp.CallOkAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket, "bitbucket_get_current_user");
        var bob = await Mcp.CallOkAsync(app.ClientWith("bb-bob"), Mcp.Bitbucket, "bitbucket_get_current_user");

        Assert.Equal("Alice", (string?)alice["displayName"]);
        Assert.Equal("Bob", (string?)bob["displayName"]);
    }
}

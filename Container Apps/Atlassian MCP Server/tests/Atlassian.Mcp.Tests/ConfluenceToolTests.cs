using System.Net;

namespace Atlassian.Mcp.Tests;

public class ConfluenceToolTests
{
    private const string Host = FakeUpstream.AtlassianHost;
    private static readonly string Base = $"/ex/confluence/{FakeUpstream.CloudId}/wiki";

    [Fact]
    public async Task Get_page_returns_the_body_as_readable_text_with_a_link()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/api/v2/pages/123", """
            {"id":"123","status":"current","title":"Release process","spaceId":"98","parentId":"100","authorId":"u1",
             "createdAt":"2026-09-01T10:00:00.000Z",
             "version":{"number":4,"createdAt":"2026-10-01T10:00:00.000Z","message":"Tidy"},
             "body":{"storage":{"value":"<h2>Steps</h2><p>Merge &amp; tag.</p><ul><li>Build</li><li>Ship</li></ul>"}},
             "_links":{"webui":"/spaces/ENG/pages/123/Release+process"}}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_get_page", new { pageId = "123" });

        var body = (string)result["body"]!;
        Assert.Contains("## Steps", body);
        Assert.Contains("Merge & tag.", body);
        Assert.Contains("- Build", body);
        Assert.Contains("- Ship", body);
        Assert.Equal("Release process", (string?)result["title"]);
        Assert.Equal(4, (int)result["version"]!);
        Assert.Equal("https://acme.atlassian.net/wiki/spaces/ENG/pages/123/Release+process", (string?)result["url"]);
        Assert.Contains("body-format=storage", app.Upstream.Last(Host, $"{Base}/api/v2/pages/123").Query);
    }

    [Fact]
    public async Task A_long_page_is_cut_and_says_so()
    {
        await using var app = new TestApp().With("Limits:MaxTextChars", "1000");
        app.Upstream.Json(Host, $"{Base}/api/v2/pages/5", $$$"""
            {"id":"5","title":"Big","body":{"storage":{"value":"<p>{{{new string('x', 5000)}}}</p>"}},"_links":{"webui":"/spaces/A/pages/5"}}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_get_page", new { pageId = "5" });

        Assert.Equal(1000, ((string)result["body"]!).Length);
        Assert.True((bool)result["truncated"]!);
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("../../x")]
    [InlineData("1/children")]
    public async Task A_page_id_that_is_not_a_number_is_refused_before_any_call(string pageId)
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_get_page", new { pageId });

        Assert.True(isError);
        Assert.Contains("page ID", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Path.Contains("/api/v2/"));
    }

    [Fact]
    public async Task Search_strips_highlight_marks_and_offers_the_next_offset()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/rest/api/search", """
            {"results":[{"content":{"id":"123","type":"page"},"title":"Release process",
              "excerpt":"how to @@@hl@@@release@@@endhl@@@ safely","lastModified":"2026-10-01T10:00:00.000Z",
              "resultGlobalContainer":{"title":"Engineering"},"url":"/spaces/ENG/pages/123/Release+process"}],
             "_links":{"next":"/rest/api/search?cql=x&start=25"}}
            """);

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_search_content",
            new { cql = "type = page AND text ~ \"release\"" });

        var hit = result["results"]![0]!;
        Assert.Equal("123", (string?)hit["id"]);
        Assert.Equal("how to release safely", (string?)hit["excerpt"]);
        Assert.Equal("Engineering", (string?)hit["space"]);
        Assert.Equal("https://acme.atlassian.net/wiki/spaces/ENG/pages/123/Release+process", (string?)hit["url"]);
        Assert.Equal(25, (int)result["nextStart"]!);

        var seen = app.Upstream.Last(Host, $"{Base}/rest/api/search");
        Assert.Equal("alice-token", seen.Bearer);
        Assert.Contains("cql=type%20%3D%20page%20AND%20text%20~%20%22release%22", seen.Query);
    }

    [Fact]
    public async Task An_empty_search_is_a_success()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/rest/api/search", """{"results":[]}""");

        var result = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_search_content", new { cql = "title = \"none\"" });

        Assert.Equal(0, (int)result["returned"]!);
        Assert.Null(result["nextStart"]);
    }

    [Fact]
    public async Task Listing_pages_follows_the_cursor_without_exposing_a_url()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/api/v2/pages", """
            {"results":[{"id":"1","title":"One","status":"current","spaceId":"98","version":{"number":2},"_links":{"webui":"/spaces/A/pages/1"}}],
             "_links":{"next":"/wiki/api/v2/pages?cursor=NEXT%3D%3D&limit=1"}}
            """);

        var first = await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_list_pages",
            new { spaceId = "98", title = "One", limit = 1 });

        Assert.Equal("NEXT==", (string?)first["nextCursor"]);
        var seen = app.Upstream.Last(Host, $"{Base}/api/v2/pages");
        Assert.Contains("space-id=98", seen.Query);
        Assert.Contains("title=One", seen.Query);

        await Mcp.CallOkAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_list_pages", new { cursor = "NEXT==" });
        Assert.Contains("cursor=NEXT%3D%3D", app.Upstream.Last(Host, $"{Base}/api/v2/pages").Query);
    }

    [Fact]
    public async Task A_cursor_that_is_a_url_is_refused()
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_list_pages",
            new { cursor = "https://evil.example/x?y=1" });

        Assert.True(isError);
        Assert.Contains("cursor", text);
        Assert.DoesNotContain(app.Upstream.Requests, r => r.Host == "evil.example");
    }

    [Fact]
    public async Task Spaces_and_children_are_shaped_and_space_keys_are_validated()
    {
        await using var app = new TestApp();
        app.Upstream
            .Json(Host, $"{Base}/api/v2/spaces", """{"results":[{"id":"98","key":"ENG","name":"Engineering","type":"global","status":"current"}]}""")
            .Json(Host, $"{Base}/api/v2/pages/123/children", """{"results":[{"id":"124","title":"Child","status":"current","spaceId":"98"}]}""");
        var client = app.ClientWith("alice-token");

        var spaces = await Mcp.CallOkAsync(client, Mcp.Confluence, "confluence_list_spaces", new { keys = new[] { "ENG", "~jdoe" } });
        var children = await Mcp.CallOkAsync(client, Mcp.Confluence, "confluence_get_page_children", new { pageId = "123" });
        var (bad, _) = await Mcp.CallToolAsync(client, Mcp.Confluence, "confluence_list_spaces", new { keys = new[] { "E,NG" } });

        Assert.Equal("https://acme.atlassian.net/wiki/spaces/ENG", (string?)spaces["spaces"]![0]!["url"]);
        Assert.Contains("keys=ENG%2C~jdoe", app.Upstream.Last(Host, $"{Base}/api/v2/spaces").Query);
        Assert.Equal("Child", (string?)children["children"]![0]!["title"]);
        Assert.True(bad);
    }

    [Fact]
    public async Task Current_user_reports_the_site_and_scopes()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/rest/api/user/current", """{"accountId":"a1","displayName":"Alice","email":"alice@example.com"}""");

        var result = await Mcp.CallOkAsync(app.ClientWith("alice-token"), Mcp.Confluence, "confluence_get_current_user");

        Assert.Equal("Alice", (string?)result["displayName"]);
        Assert.Equal("https://acme.atlassian.net", (string?)result["site"]);
        Assert.Contains("read:page:confluence", (string?)result["scopes"]);
    }

    [Fact]
    public async Task A_forbidden_page_explains_the_scopes_may_be_missing()
    {
        await using var app = new TestApp();
        app.Upstream.Json(Host, $"{Base}/api/v2/pages/7", """{"errors":[{"status":403,"title":"Forbidden","detail":"Scope check failed."}]}""", HttpStatusCode.Forbidden);

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("alice-token"), Mcp.Confluence, "confluence_get_page", new { pageId = "7" });

        Assert.True(isError);
        Assert.Contains("Scope check failed.", text);
        Assert.Contains("scopes", text);
    }

    [Fact]
    public async Task A_token_with_no_access_to_the_site_gets_a_clear_message()
    {
        await using var app = new TestApp();

        var (isError, text) = await Mcp.CallToolAsync(
            app.ClientWith("nosite-token"), Mcp.Confluence, "confluence_get_page", new { pageId = "1" });

        Assert.True(isError);
        Assert.Contains("does not have access to https://acme.atlassian.net", text);
    }
}

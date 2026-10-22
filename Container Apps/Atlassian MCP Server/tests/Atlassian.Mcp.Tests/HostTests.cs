using System.Net;
using System.Text.Json;

namespace Atlassian.Mcp.Tests;

public class HostTests
{
    internal static readonly string[] JiraTools = Sorted(
        "jira_get_create_metadata", "jira_get_current_user", "jira_get_issue", "jira_get_issue_transitions",
        "jira_list_projects", "jira_search_issues", "jira_search_users");

    internal static readonly string[] JiraWriteTools = Sorted(
        "jira_add_comment", "jira_assign_issue", "jira_create_issue", "jira_transition_issue", "jira_update_issue");

    internal static readonly string[] ConfluenceTools = Sorted(
        "confluence_get_current_user", "confluence_get_page", "confluence_get_page_children",
        "confluence_list_pages", "confluence_list_spaces", "confluence_search_content");

    internal static readonly string[] ConfluenceWriteTools = Sorted(
        "confluence_add_comment", "confluence_create_page", "confluence_update_page");

    internal static readonly string[] BitbucketTools = Sorted(
        "bitbucket_get_current_user", "bitbucket_get_file", "bitbucket_get_pull_request",
        "bitbucket_get_pull_request_diff", "bitbucket_get_pull_request_diffstat", "bitbucket_get_repository",
        "bitbucket_list_commits", "bitbucket_list_pull_request_comments", "bitbucket_list_pull_requests",
        "bitbucket_list_repositories", "bitbucket_list_workspaces");

    internal static readonly string[] BitbucketWriteTools = Sorted(
        "bitbucket_approve_pull_request", "bitbucket_create_pull_request", "bitbucket_create_pull_request_comment");

    private static string[] Sorted(params string[] names) => names.Order().ToArray();

    [Fact]
    public async Task Liveness_does_not_depend_on_any_provider()
    {
        await using var app = new TestApp();
        app.Upstream.Down = true;

        var response = await app.ClientWith().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_healthy_when_providers_answer()
    {
        await using var app = new TestApp();
        // An unauthenticated probe is answered 401 by the provider, which still proves it is up.

        var response = await app.ClientWith().GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_reports_degraded_not_failed_when_a_provider_is_down()
    {
        await using var app = new TestApp();
        app.Upstream.Down = true;

        var response = await app.ClientWith().GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(Mcp.Jira)]
    [InlineData(Mcp.Confluence)]
    [InlineData(Mcp.Bitbucket)]
    public async Task Each_endpoint_challenges_with_its_own_resource_metadata(string endpoint)
    {
        await using var app = new TestApp();

        var (response, _) = await Mcp.CallAsync(app.ClientWith(), endpoint, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(' ', response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata=", challenge);
        Assert.Contains("/.well-known/oauth-protected-resource" + endpoint, challenge);
    }

    [Theory]
    [InlineData(Mcp.Jira, "https://auth.atlassian.com", "read:jira-work")]
    [InlineData(Mcp.Confluence, "https://auth.atlassian.com", "read:page:confluence")]
    [InlineData(Mcp.Bitbucket, "https://bitbucket.org/site/oauth2", "repository")]
    public async Task Resource_metadata_names_the_right_sign_in_for_each_endpoint(
        string endpoint, string authorizationServer, string scope)
    {
        await using var app = new TestApp();

        var response = await app.ClientWith().GetAsync("/.well-known/oauth-protected-resource" + endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.EndsWith(endpoint, root.GetProperty("resource").GetString());
        Assert.Contains(authorizationServer, root.GetProperty("authorization_servers").EnumerateArray().Select(a => a.GetString()));
        Assert.Contains(scope, root.GetProperty("scopes_supported").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task Each_endpoint_serves_only_its_own_tools()
    {
        await using var app = new TestApp();

        Assert.Equal(JiraTools, await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Jira));
        Assert.Equal(ConfluenceTools, await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Confluence));
        Assert.Equal(BitbucketTools, await Mcp.ListToolNamesAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket));
    }

    [Fact]
    public async Task A_disabled_product_has_no_endpoint_and_no_tools()
    {
        await using var app = new TestApp().With("Jira:Enabled", "false");

        var (jira, _) = await Mcp.CallAsync(app.ClientWith("alice-token"), Mcp.Jira, "tools/list");
        var confluence = await Mcp.ListToolNamesAsync(app.ClientWith("alice-token"), Mcp.Confluence);

        Assert.Equal(HttpStatusCode.NotFound, jira.StatusCode);
        Assert.Equal(ConfluenceTools, confluence);
    }

    [Fact]
    public async Task Bitbucket_alone_needs_no_atlassian_site()
    {
        await using var app = new TestApp()
            .With("Jira:Enabled", "false")
            .With("Confluence:Enabled", "false")
            .With("Atlassian:SiteUrl", "");

        var tools = await Mcp.ListToolNamesAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket);

        Assert.Equal(BitbucketTools, tools);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://acme.atlassian.net")]
    [InlineData("https://acme.atlassian.net/wiki")]
    [InlineData("acme.atlassian.net")]
    public async Task Startup_refuses_a_missing_or_malformed_site_url(string siteUrl)
    {
        await using var app = new TestApp().With("Atlassian:SiteUrl", siteUrl);

        var failure = Record.Exception(() => app.ClientWith());

        Assert.NotNull(failure);
        Assert.Contains("Atlassian:SiteUrl", failure.ToString());
    }

    [Fact]
    public async Task Startup_refuses_when_every_product_is_off()
    {
        await using var app = new TestApp()
            .With("Jira:Enabled", "false")
            .With("Confluence:Enabled", "false")
            .With("Bitbucket:Enabled", "false");

        var failure = Record.Exception(() => app.ClientWith());

        Assert.NotNull(failure);
        Assert.Contains("at least one", failure.ToString());
    }

    [Fact]
    public async Task A_good_atlassian_token_is_accepted_and_checked_once_within_the_cache_window()
    {
        await using var app = new TestApp();
        var client = app.ClientWith("alice-token");

        await Mcp.ListToolNamesAsync(client, Mcp.Jira);
        await Mcp.ListToolNamesAsync(client, Mcp.Confluence);

        Assert.Equal(1, app.Upstream.CountOf(FakeUpstream.AtlassianHost, "/oauth/token/accessible-resources"));
    }

    [Fact]
    public async Task With_caching_off_every_call_checks_the_token()
    {
        await using var app = new TestApp().With("Auth:ValidationCacheSeconds", "0");
        var client = app.ClientWith("alice-token");

        await Mcp.ListToolNamesAsync(client, Mcp.Jira);
        await Mcp.ListToolNamesAsync(client, Mcp.Jira);

        Assert.Equal(2, app.Upstream.CountOf(FakeUpstream.AtlassianHost, "/oauth/token/accessible-resources"));
    }

    [Theory]
    [InlineData(Mcp.Jira, "bb-alice")]
    [InlineData(Mcp.Confluence, "bb-alice")]
    [InlineData(Mcp.Bitbucket, "alice-token")]
    [InlineData(Mcp.Jira, "not-a-token")]
    public async Task A_token_from_the_wrong_provider_or_an_unknown_token_is_refused(string endpoint, string token)
    {
        await using var app = new TestApp();

        var (response, _) = await Mcp.CallAsync(app.ClientWith(token), endpoint, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_provider_outage_during_the_check_refuses_the_request_instead_of_letting_it_through()
    {
        await using var app = new TestApp();
        app.Upstream.Down = true;

        var (response, _) = await Mcp.CallAsync(app.ClientWith("alice-token"), Mcp.Jira, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_bitbucket_token_without_the_account_scope_is_still_accepted()
    {
        await using var app = new TestApp();
        app.Upstream.Json(FakeUpstream.BitbucketHost, "/2.0/user", """{"error":{"message":"Forbidden"}}""", HttpStatusCode.Forbidden);

        var tools = await Mcp.ListToolNamesAsync(app.ClientWith("bb-alice"), Mcp.Bitbucket);

        Assert.Equal(BitbucketTools.Length, tools.Length);
    }

    [Fact]
    public async Task With_auth_off_tools_can_be_listed_without_a_token()
    {
        await using var app = new TestApp().With("Auth:Enabled", "false");

        var tools = await Mcp.ListToolNamesAsync(app.ClientWith(), Mcp.Jira);

        Assert.Equal(JiraTools, tools);
    }

    [Fact]
    public async Task Requests_are_rate_limited_per_person()
    {
        await using var app = new TestApp().With("RateLimit:PermitsPerMinute", "2");

        var alice = app.ClientWith("alice-token");
        await Mcp.ListToolNamesAsync(alice, Mcp.Jira);
        await Mcp.ListToolNamesAsync(alice, Mcp.Jira);
        var (limited, _) = await Mcp.CallAsync(alice, Mcp.Jira, "tools/list");
        var (bobOk, _) = await Mcp.CallAsync(app.ClientWith("bob-token"), Mcp.Jira, "tools/list");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobOk.StatusCode);
    }
}

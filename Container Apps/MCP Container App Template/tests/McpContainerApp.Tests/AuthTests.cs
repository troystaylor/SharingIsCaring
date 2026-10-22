using System.Net;

namespace McpContainerApp.Tests;

public sealed class AuthTests
{
    [Fact]
    public async Task Rejects_requests_without_a_token_and_points_at_resource_metadata()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        var (response, _) = await Mcp.CallAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", string.Join(' ', response.Headers.WwwAuthenticate.Select(h => h.ToString())));
    }

    [Fact]
    public async Task Publishes_protected_resource_metadata_naming_the_tenant_authority()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"login.microsoftonline.com/{TestApp.TenantId}/v2.0", body);
        Assert.Contains($"api://{TestApp.ClientId}/access_as_user", body);
    }

    [Fact]
    public async Task Accepts_a_token_with_the_required_scope()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token());

        var tools = await Mcp.ListToolNamesAsync(client);

        Assert.Contains("get_server_info", tools);
    }

    [Fact]
    public async Task Rejects_a_token_issued_for_another_audience()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token(audience: "api://someone-else"));

        var (response, _) = await Mcp.CallAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_valid_token_that_lacks_the_scope()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token(scope: "Files.Read"));

        var (response, _) = await Mcp.CallAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Accepts_an_app_only_token_that_carries_the_configured_role()
    {
        using var app = new TestApp().With("Auth:AppRole", "Mcp.Invoke");
        using var client = app.ClientWith(TestApp.Token(scope: null, role: "Mcp.Invoke"));

        var tools = await Mcp.ListToolNamesAsync(client);

        Assert.NotEmpty(tools);
    }

    [Fact]
    public async Task Refuses_to_start_with_auth_enabled_and_no_tenant()
    {
        using var app = new TestApp().With("Auth:TenantId", "");

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("Auth:TenantId", ex.ToString());
    }

    [Fact]
    public async Task Rate_limits_a_caller_who_exceeds_the_per_minute_budget()
    {
        using var app = new TestApp().With("RateLimit:PermitsPerMinute", "2");
        using var client = app.ClientWith(TestApp.Token());

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            codes.Add((await Mcp.CallAsync(client, "tools/list")).Response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codes);
    }
}

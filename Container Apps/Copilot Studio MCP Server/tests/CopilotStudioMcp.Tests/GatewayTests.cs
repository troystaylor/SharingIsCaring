using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CopilotStudioMcp.Gateway;

namespace CopilotStudioMcp.Tests;

public sealed class AuthTests
{
    [Fact]
    public async Task Missing_token_is_challenged()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        var response = await Rpc.SendAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public async Task Token_for_another_audience_is_refused()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token(audience: "api://someone-else"));

        var response = await Rpc.SendAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public async Task Token_without_the_required_scope_is_forbidden()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token(scope: "Something.Else"));

        var response = await Rpc.SendAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public async Task Token_from_another_tenant_is_refused()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token(issuer: "https://login.microsoftonline.com/99999999-9999-9999-9999-999999999999/v2.0"));

        var response = await Rpc.SendAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Probes_and_get_do_not_need_a_token()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/mcp")).StatusCode);
    }

    [Fact]
    public async Task Server_refuses_to_start_with_auth_on_and_no_tenant_or_client()
    {
        using var noTenant = new TestApp().With("Auth:TenantId", string.Empty);
        Assert.Throws<InvalidOperationException>(() => noTenant.CreateClient());

        using var noClient = new TestApp().With("Auth:ClientId", string.Empty);
        Assert.Throws<InvalidOperationException>(() => noClient.CreateClient());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_token_issued_for_power_platform_is_not_accepted()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token(audience: "https://api.powerplatform.com"));

        var response = await Rpc.SendAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Broker.Exchanged);
    }
}

public sealed class ToolTests
{
    private static HttpClient SignedIn(TestApp app) => app.ClientWith(TestApp.Token());

    [Fact]
    public async Task A_failed_exchange_is_a_tool_error_and_nothing_is_forwarded()
    {
        using var app = new TestApp();
        app.Broker.Failure = new GatewayException(403, "CONSENT_REQUIRED", "The plugin's Entra app does not yet have consent.");
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/call", new { name = "list_environments", arguments = new { } });

        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("consent", reply["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public async Task Tool_list_adds_list_environments_and_requires_environmentId_everywhere()
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/list");
        var tools = reply["result"]!["tools"]!.AsArray();

        Assert.Equal(["list_environments", "manage_agents", "get_copilot_studio_skills"], tools.Select(t => t!["name"]!.GetValue<string>()));
        foreach (var tool in tools.Skip(1))
        {
            var schema = tool!["inputSchema"]!;
            Assert.Equal("environmentId", schema["required"]![0]!.GetValue<string>());
            Assert.NotNull(schema["properties"]!["environmentId"]);
        }

        // Existing properties and requirements survive.
        var manage = tools[1]!["inputSchema"]!;
        Assert.NotNull(manage["properties"]!["action"]);
        Assert.Equal(["environmentId", "action"], manage["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task Tool_list_is_read_once_and_cached()
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        await Rpc.CallAsync(client, "tools/list");
        await Rpc.CallAsync(client, "tools/list");

        Assert.Equal(1, app.Upstream.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/mcp", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task List_environments_returns_only_usable_environments()
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/call", new { name = "list_environments", arguments = new { } });
        var text = reply["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.Contains(FakeUpstream.EnvironmentA, text);
        Assert.Contains(FakeUpstream.EnvironmentB, text);
        Assert.DoesNotContain(FakeUpstream.DisabledEnvironment, text);
        Assert.DoesNotContain("not-a-guid", text);
        Assert.False(reply["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Call_goes_to_the_chosen_environment_with_the_exchanged_token_and_without_environmentId()
    {
        using var app = new TestApp();
        var token = TestApp.Token();
        using var client = app.ClientWith(token);

        var reply = await Rpc.CallAsync(
            client, "tools/call", new { name = "manage_agents", arguments = new { environmentId = FakeUpstream.EnvironmentA, action = "list" } }, id: 42);

        Assert.Equal(42, reply["id"]!.GetValue<int>());
        Assert.Equal("ok:aaaaaaaa1111422283334444444444.44.environment.api.powerplatform.com", reply["result"]!["content"]![0]!["text"]!.GetValue<string>());

        var sent = Assert.Single(app.Upstream.Requests);
        Assert.Equal($"Bearer {FakeBroker.PowerPlatformTokenFor(token)}", sent.Authorization);
        Assert.DoesNotContain(token, sent.Authorization);
        var forwarded = JsonNode.Parse(sent.Body)!;
        Assert.Equal("tools/call", forwarded["method"]!.GetValue<string>());
        Assert.Equal("manage_agents", forwarded["params"]!["name"]!.GetValue<string>());
        Assert.Equal("list", forwarded["params"]!["arguments"]!["action"]!.GetValue<string>());
        Assert.Null(forwarded["params"]!["arguments"]!["environmentId"]);
    }

    [Fact]
    public async Task Default_environment_ids_map_to_their_own_host()
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(
            client, "tools/call", new { name = "manage_agents", arguments = new { environmentId = FakeUpstream.EnvironmentB, action = "list" } });

        var text = reply["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.EndsWith(".environment.api.powerplatform.com", text);
        Assert.StartsWith("ok:defaultbbbbbbbb1111", text);
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("aaaaaaaa-1111-4222-8333-444444444444.evil.com")]
    [InlineData("../../x")]
    [InlineData("")]
    public async Task Invalid_environment_ids_never_reach_the_network(string environmentId)
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/call", new { name = "manage_agents", arguments = new { environmentId, action = "list" } });

        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public async Task Missing_environment_id_is_a_tool_error()
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/call", new { name = "manage_agents", arguments = new { action = "list" } });

        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("list_environments", reply["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public async Task Upstream_errors_become_tool_errors_with_a_plain_message()
    {
        using var app = new TestApp();
        app.Upstream.McpResponder = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/call", new { name = "manage_agents", arguments = new { environmentId = FakeUpstream.EnvironmentA, action = "list" } });

        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("denied access", reply["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Tool_calls_are_never_retried()
    {
        using var app = new TestApp();
        app.Upstream.McpResponder = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        using var client = SignedIn(app);

        var reply = await Rpc.CallAsync(client, "tools/call", new { name = "manage_agents", arguments = new { environmentId = FakeUpstream.EnvironmentA, action = "create" } });

        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Single(app.Upstream.Requests);
    }

    [Fact]
    public async Task Initialize_negotiates_and_notifications_get_no_reply()
    {
        using var app = new TestApp();
        using var client = SignedIn(app);

        var init = await Rpc.CallAsync(client, "initialize", new { protocolVersion = "2025-03-26" });
        Assert.Equal("2025-03-26", init["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Contains("list_environments", init["result"]!["instructions"]!.GetValue<string>());

        var note = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", method = "notifications/initialized" });
        Assert.Equal(HttpStatusCode.Accepted, note.StatusCode);
        Assert.Empty(app.Upstream.Requests);
    }

    [Fact]
    public void Environment_host_is_derived_from_the_id()
    {
        Assert.Equal(
            "https://aaaaaaaa1111422283334444444444.44.environment.api.powerplatform.com/copilotflows/agenticbuilder/mcp?api-version=1",
            Environments.McpUrl("AAAAAAAA-1111-4222-8333-444444444444").ToString());
    }

    [Fact]
    public void Responses_are_read_from_json_or_event_streams()
    {
        var json = UpstreamClient.ParseRpcResponse("application/json", """{"jsonrpc":"2.0","id":1,"result":{"a":1}}""");
        Assert.Equal(1, json["result"]!["a"]!.GetValue<int>());

        var sse = UpstreamClient.ParseRpcResponse(
            "text/event-stream",
            "event: message\r\ndata: {\"method\":\"notifications/progress\"}\r\n\r\nevent: message\r\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"a\":2}}\r\n\r\n");
        Assert.Equal(2, sse["result"]!["a"]!.GetValue<int>());

        Assert.Throws<GatewayException>(() => UpstreamClient.ParseRpcResponse("text/event-stream", "data: nope\n\n"));
    }
}

internal static class Rpc
{
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, object? parameters = null, int id = 1) =>
        client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } });

    public static async Task<JsonNode> CallAsync(HttpClient client, string method, object? parameters = null, int id = 1)
    {
        var response = await SendAsync(client, method, parameters, id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}

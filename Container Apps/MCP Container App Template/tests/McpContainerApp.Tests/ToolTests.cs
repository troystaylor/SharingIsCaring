using System.Net;
using System.Text.Json;
using McpContainerApp.Configuration;
using McpContainerApp.Tools;
using Microsoft.Extensions.Options;

namespace McpContainerApp.Tests;

public sealed class ToolTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Health_probes_are_anonymous()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).StatusCode);
    }

    [Fact]
    public async Task Lists_the_example_tools_with_read_only_hints()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token());

        var (_, result) = await Mcp.CallAsync(client, "tools/list");
        var tools = result!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();

        Assert.Equal(["get_server_info", "search_items"], tools.Select(t => t.GetProperty("name").GetString()).Order());
        Assert.All(tools, t => Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
    }

    [Fact]
    public async Task Search_passes_the_query_to_the_upstream_and_returns_its_json()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"items":[{"id":"1"}]}""")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://upstream.test/") };
        var client = new ExampleUpstreamClient(http, Options.Create(new UpstreamOptions
        {
            ClientSecret = "s3cret"
        }));

        var result = await new ExampleTools(client).SearchItems("a b&c", top: 500);

        Assert.Equal("1", result.GetProperty("items")[0].GetProperty("id").GetString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://upstream.test/items?q=a%20b%26c&limit=50", request.RequestUri!.AbsoluteUri);
        Assert.Equal("s3cret", request.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task An_upstream_failure_surfaces_the_status_and_not_the_body()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("internal stack trace")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://upstream.test/") };
        var tools = new ExampleTools(new ExampleUpstreamClient(http, Options.Create(new UpstreamOptions())));

        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => tools.SearchItems("x"));

        Assert.Contains("502", ex.Message);
        Assert.DoesNotContain("stack trace", ex.Message);
    }

    [Fact]
    public async Task An_unconfigured_upstream_is_reported_as_a_tool_error()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(TestApp.Token());

        var (_, result) = await Mcp.CallAsync(client, "tools/call",
            new { name = "search_items", arguments = new { query = "x" } });

        var call = result!.Value.GetProperty("result");
        Assert.True(call.GetProperty("isError").GetBoolean());
        Assert.Contains("Upstream:BaseUrl", call.GetProperty("content")[0].GetProperty("text").GetString());
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Atlassian.Mcp.Tests;

/// <summary>A request the fake upstream received, for asserting what the server sent.</summary>
public sealed record Seen(HttpMethod Method, string Host, string Path, string Query, string? Bearer, string? Body = null);

/// <summary>
/// Stands in for Atlassian and Bitbucket. Sign-in checks answer from the token tables; tests add routes for the
/// API calls they expect. An unrouted call returns 404, so a missing route fails loudly.
/// </summary>
public sealed class FakeUpstream : HttpMessageHandler
{
    public const string AtlassianHost = "api.atlassian.com";
    public const string BitbucketHost = "api.bitbucket.org";
    public const string Site = "https://acme.atlassian.net";
    public const string CloudId = "cloud-acme";

    public List<Seen> Requests { get; } = [];

    /// <summary>Valid Atlassian tokens and who they belong to.</summary>
    public Dictionary<string, (string AccountId, string Name)> AtlassianUsers { get; } = new()
    {
        ["alice-token"] = ("acct-alice", "Alice"),
        ["bob-token"] = ("acct-bob", "Bob"),
        ["nosite-token"] = ("acct-nosite", "Nosite")
    };

    /// <summary>Valid Bitbucket tokens and who they belong to.</summary>
    public Dictionary<string, (string Uuid, string Name)> BitbucketUsers { get; } = new()
    {
        ["bb-alice"] = ("{uuid-alice}", "Alice"),
        ["bb-bob"] = ("{uuid-bob}", "Bob")
    };

    /// <summary>When set, every request fails as if the network were down.</summary>
    public bool Down { get; set; }

    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public int CountOf(string host, string path) =>
        Requests.Count(r => r.Host == host && r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    public Seen Last(string host, string path) =>
        Requests.Last(r => r.Host == host && r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    public Seen LastBy(HttpMethod method, string host, string path) =>
        Requests.Last(r => r.Method == method && r.Host == host && r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Routes a call. A null <paramref name="method"/> matches any method; a method-specific route wins.</summary>
    public FakeUpstream Route(
        string host, string path, Func<HttpRequestMessage, HttpResponseMessage> handler, string? method = null)
    {
        _routes[(method ?? "*") + " " + host + path] = handler;
        return this;
    }

    public FakeUpstream Json(
        string host, string path, string json, HttpStatusCode status = HttpStatusCode.OK, string? method = null) =>
        Route(host, path, _ => Respond(status, json), method);

    public FakeUpstream Text(string host, string path, string text) =>
        Route(host, path, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        });

    public static HttpResponseMessage Respond(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new HttpRequestException("The upstream is down.");
        }

        var uri = request.RequestUri!;
        var bearer = request.Headers.Authorization?.Parameter;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Seen(request.Method, uri.Host, uri.AbsolutePath, uri.Query, bearer, body));

        var key = uri.Host + uri.AbsolutePath;
        if (_routes.TryGetValue(request.Method.Method + " " + key, out var route) || _routes.TryGetValue("* " + key, out route))
        {
            return route(request);
        }

        return key switch
        {
            AtlassianHost + "/oauth/token/accessible-resources" => AccessibleResources(bearer),
            AtlassianHost + "/me" => Me(bearer),
            BitbucketHost + "/2.0/user" => BitbucketUser(bearer),
            _ => Respond(HttpStatusCode.NotFound, """{"message":"No fake route."}""")
        };
    }

    private HttpResponseMessage AccessibleResources(string? bearer)
    {
        if (bearer is null || !AtlassianUsers.ContainsKey(bearer))
        {
            return Respond(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}""");
        }

        var url = bearer == "nosite-token" ? "https://other.atlassian.net" : Site;
        var id = bearer == "nosite-token" ? "cloud-other" : CloudId;
        return Respond(HttpStatusCode.OK, $$"""
            [{"id":"{{id}}","url":"{{url}}","name":"Site","scopes":["read:jira-work","read:page:confluence"]}]
            """);
    }

    private HttpResponseMessage Me(string? bearer) =>
        bearer is not null && AtlassianUsers.TryGetValue(bearer, out var user)
            ? Respond(HttpStatusCode.OK, $$"""{"account_id":"{{user.AccountId}}","name":"{{user.Name}}","email":"{{user.Name.ToLowerInvariant()}}@example.com"}""")
            : Respond(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}""");

    private HttpResponseMessage BitbucketUser(string? bearer) =>
        bearer is not null && BitbucketUsers.TryGetValue(bearer, out var user)
            ? Respond(HttpStatusCode.OK, $$"""{"uuid":"{{user.Uuid}}","account_id":"acct-{{user.Name.ToLowerInvariant()}}","display_name":"{{user.Name}}"}""")
            : Respond(HttpStatusCode.Unauthorized, """{"error":{"message":"Bad credentials"}}""");
}

/// <summary>Hosts the real app in-process against a fake upstream.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Auth:Enabled"] = "true",
        ["Atlassian:SiteUrl"] = FakeUpstream.Site,
        ["RateLimit:PermitsPerMinute"] = "0"
    };

    public FakeUpstream Upstream { get; } = new();

    public TestApp With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            foreach (var name in new[] { HttpNames.Atlassian, HttpNames.Bitbucket })
            {
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => Upstream);

                // One quick retry, ignoring Retry-After, so failure-path tests do not wait out real backoff.
                services.Configure<HttpStandardResilienceOptions>($"{name}-standard", o =>
                {
                    o.Retry.MaxRetryAttempts = 1;
                    o.Retry.Delay = TimeSpan.FromMilliseconds(1);
                    o.Retry.UseJitter = false;
                    o.Retry.ShouldRetryAfterHeader = false;
                });
            }
        });
    }

    /// <summary>A client that presents <paramref name="bearer"/> as its access token, if given.</summary>
    public HttpClient ClientWith(string? bearer = null)
    {
        var client = CreateClient();
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }

    /// <summary>A client that shows redirects instead of following them.</summary>
    public HttpClient NoRedirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

public static class Mcp
{
    public const string Jira = "/jira/mcp";
    public const string Confluence = "/confluence/mcp";
    public const string Bitbucket = "/bitbucket/mcp";

    /// <summary>Sends one JSON-RPC request. Stateless mode answers each POST on its own, as SSE or JSON.</summary>
    public static async Task<(HttpResponseMessage Response, JsonElement? Result)> CallAsync(
        HttpClient client, string endpoint, string method, object? parameters = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } })
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        var text = await response.Content.ReadAsStringAsync();
        var json = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
            .Select(l => l["data:".Length..].Trim())
            .LastOrDefault() ?? text;

        using var doc = JsonDocument.Parse(json);
        return (response, doc.RootElement.Clone());
    }

    /// <summary>Calls a tool and returns its text result and whether it was an error.</summary>
    public static async Task<(bool IsError, string Text)> CallToolAsync(
        HttpClient client, string endpoint, string name, object? arguments = null)
    {
        var (response, result) = await CallAsync(client, endpoint, "tools/call", new { name, arguments = arguments ?? new { } });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.Headers.WwwAuthenticate}");

        var call = result!.Value.GetProperty("result");
        var isError = call.TryGetProperty("isError", out var flag) && flag.GetBoolean();
        return (isError, call.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    /// <summary>Calls a tool that is expected to succeed and returns its JSON result.</summary>
    public static async Task<JsonNode> CallOkAsync(
        HttpClient client, string endpoint, string name, object? arguments = null)
    {
        var (isError, text) = await CallToolAsync(client, endpoint, name, arguments);
        Assert.False(isError, text);
        return JsonNode.Parse(text)!;
    }

    public static async Task<string[]> ListToolNamesAsync(HttpClient client, string endpoint)
    {
        var (response, result) = await CallAsync(client, endpoint, "tools/list");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.Headers.WwwAuthenticate}");
        return result!.Value.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).Order().ToArray();
    }
}

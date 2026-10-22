using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tests;

/// <summary>Hosts the real app in-process against a fake Salesforce.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Auth:Enabled"] = "true",
        ["Salesforce:InstanceUrl"] = "https://acme.my.salesforce.com",
    };

    public FakeSalesforce Salesforce { get; } = new();

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
            services.AddHttpClient(SalesforceHttp.PublicClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Salesforce);
            services.AddHttpClient(SalesforceClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Salesforce);
        });
    }

    /// <summary>A client that presents <paramref name="bearer"/> as its Salesforce access token, if given.</summary>
    public HttpClient ClientWith(string? bearer = null)
    {
        var client = CreateClient();
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }
}

public static class Mcp
{
    /// <summary>Sends one JSON-RPC request. Stateless mode answers each POST on its own, as SSE or JSON.</summary>
    public static async Task<(HttpResponseMessage Response, JsonElement? Result)> CallAsync(
        HttpClient client, string method, object? parameters = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
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
    public static async Task<(bool IsError, string Text)> CallToolAsync(HttpClient client, string name, object arguments)
    {
        var (response, result) = await CallAsync(client, "tools/call", new { name, arguments });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.Headers.WwwAuthenticate}");

        var call = result!.Value.GetProperty("result");
        var isError = call.TryGetProperty("isError", out var flag) && flag.GetBoolean();
        return (isError, call.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    public static async Task<List<JsonElement>> ListToolsAsync(HttpClient client)
    {
        var (response, result) = await CallAsync(client, "tools/list");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.Headers.WwwAuthenticate}");
        return result!.Value.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
    }

    public static async Task<string[]> ListToolNamesAsync(HttpClient client) =>
        (await ListToolsAsync(client)).Select(t => t.GetProperty("name").GetString()!).Order().ToArray();
}

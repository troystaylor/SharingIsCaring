using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace McpContainerApp.Tests;

/// <summary>Hosts the real app in-process, with settings and test-only signing keys swapped in.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ClientId = "22222222-2222-2222-2222-222222222222";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes(new string('k', 48)));

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Auth:Enabled"] = "true",
        ["Auth:TenantId"] = TenantId,
        ["Auth:ClientId"] = ClientId,
    };

    private Action<IServiceCollection>? _services;

    public TestApp With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    public TestApp WithServices(Action<IServiceCollection> configure)
    {
        _services = configure;
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
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0",
                    SigningKeys = { SigningKey }
                };

                // The framework's own post-configure has already built a metadata-fetching manager
                // from Authority by now, so it has to be replaced rather than pre-empted.
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                o.TokenValidationParameters.ValidIssuer = configuration.Issuer;
            });

            _services?.Invoke(services);
        });
    }

    public static string Token(string? scope = "access_as_user", string? audience = ClientId, string? role = null)
    {
        var claims = new Dictionary<string, object> { ["oid"] = Guid.NewGuid().ToString() };
        if (scope is not null)
        {
            claims["scp"] = scope;
        }

        if (role is not null)
        {
            claims["roles"] = new[] { role };
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0",
            Audience = audience,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)
        });
    }

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

    public static async Task<string[]> ListToolNamesAsync(HttpClient client)
    {
        var (response, result) = await CallAsync(client, "tools/list");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.Headers.WwwAuthenticate}");
        return result!.Value.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToArray();
    }
}

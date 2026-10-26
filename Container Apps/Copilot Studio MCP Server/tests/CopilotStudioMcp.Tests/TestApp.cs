using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using CopilotStudioMcp.Configuration;
using CopilotStudioMcp.Gateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CopilotStudioMcp.Tests;

/// <summary>Stands in for Copilot Studio and environment discovery. Records every request it receives.</summary>
public sealed class FakeUpstream : HttpMessageHandler
{
    public const string EnvironmentA = "aaaaaaaa-1111-4222-8333-444444444444";
    public const string EnvironmentB = "Default-bbbbbbbb-1111-4222-8333-444444444444";
    public const string DisabledEnvironment = "cccccccc-1111-4222-8333-444444444444";

    public List<(HttpMethod Method, Uri Uri, string? Authorization, string Body)> Requests { get; } = [];

    /// <summary>Overrides the response for Copilot Studio calls. Return null to use the default.</summary>
    public Func<JsonObject, HttpResponseMessage?>? McpResponder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        }

        if (request.RequestUri!.Host == "api.powerplatform.com")
        {
            return Json(new JsonObject
            {
                ["value"] = new JsonArray(
                    Env(EnvironmentA, "Power Platform Demo", "Production", "Enabled"),
                    Env(EnvironmentB, "Contoso (default)", "Default", "Enabled"),
                    Env(DisabledEnvironment, "CA Test", "Sandbox", "Disabled"),
                    Env("not-a-guid", "Broken", "Sandbox", "Enabled"))
            });
        }

        var rpc = (JsonObject)JsonNode.Parse(body)!;
        if (McpResponder?.Invoke(rpc) is { } custom)
        {
            return custom;
        }

        var result = rpc["method"]!.GetValue<string>() switch
        {
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "manage_agents",
                        ["description"] = "Manage agents.",
                        ["inputSchema"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject { ["action"] = new JsonObject { ["type"] = "string" } },
                            ["required"] = new JsonArray("action")
                        }
                    },
                    new JsonObject { ["name"] = "get_copilot_studio_skills", ["description"] = "Skills." })
            },
            _ => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"ok:{request.RequestUri.Host}" }),
                ["isError"] = false
            }
        };

        // Copilot Studio answers with server-sent events.
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = rpc["id"]!.DeepClone(), ["result"] = result };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"event: message\ndata: {message.ToJsonString()}\n\n", Encoding.UTF8, "text/event-stream")
        };
    }

    public static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    private static JsonObject Env(string id, string name, string type, string state) =>
        new() { ["id"] = id, ["displayName"] = name, ["type"] = type, ["state"] = state, ["azureRegion"] = "westus" };
}

/// <summary>Stands in for the Entra on-behalf-of exchange.</summary>
public sealed class FakeBroker : ITokenBroker
{
    public List<string> Exchanged { get; } = [];

    /// <summary>When set, every exchange throws it.</summary>
    public GatewayException? Failure { get; set; }

    public static string PowerPlatformTokenFor(string userToken) => "pp-" + userToken[^12..];

    public Task<string> ExchangeAsync(string userToken, CancellationToken ct)
    {
        lock (Exchanged)
        {
            Exchanged.Add(userToken);
        }

        return Failure is null ? Task.FromResult(PowerPlatformTokenFor(userToken)) : Task.FromException<string>(Failure);
    }
}

/// <summary>Hosts the real app in-process, with settings, test signing keys, and a fake upstream swapped in.</summary>
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

    public FakeUpstream Upstream { get; } = new();

    public FakeBroker Broker { get; } = new();

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
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0",
                    SigningKeys = { SigningKey }
                };

                // The framework has already built a metadata-fetching manager from Authority by now.
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.AddHttpClient<UpstreamClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
            services.AddSingleton<ITokenBroker>(Broker);
            services.AddSingleton<ITokenBroker>(Broker);
        });
    }

    public static string Token(
        string? scope = "access_as_user",
        string audience = ClientId,
        string issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0")
    {
        var claims = new Dictionary<string, object> { ["oid"] = Guid.NewGuid().ToString(), ["tid"] = TenantId };
        if (scope is not null)
        {
            claims["scp"] = scope;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
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

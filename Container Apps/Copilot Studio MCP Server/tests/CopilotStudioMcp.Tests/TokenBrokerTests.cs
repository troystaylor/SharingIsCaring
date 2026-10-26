using System.Net;
using System.Text;
using CopilotStudioMcp.Configuration;
using CopilotStudioMcp.Gateway;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CopilotStudioMcp.Tests;

/// <summary>Stands in for the Entra token endpoint and the managed identity endpoint.</summary>
internal sealed class FakeEntra : HttpMessageHandler
{
    public List<(Uri Uri, string Body, string? IdentityHeader)> Requests { get; } = [];

    public Func<HttpRequestMessage, string, HttpResponseMessage>? Responder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues("X-IDENTITY-HEADER", out var header);
        Requests.Add((request.RequestUri!, body, header?.FirstOrDefault()));
        return Responder!(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class TokenBrokerTests
{
    private static readonly AuthOptions Auth = new() { TenantId = TestApp.TenantId, ClientId = TestApp.ClientId };

    private static OnBehalfOfBroker Broker(FakeEntra entra, ExchangeOptions exchange) =>
        new(new HttpClient(entra), Auth, Options.Create(exchange), new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

    [Fact]
    public async Task Exchange_sends_the_users_token_as_the_assertion_and_caches_the_result()
    {
        var entra = new FakeEntra { Responder = (_, _) => FakeEntra.Json(HttpStatusCode.OK, """{"access_token":"pp-token","expires_in":3600}""") };
        var broker = Broker(entra, new ExchangeOptions { ClientSecret = "local-secret" });

        Assert.Equal("pp-token", await broker.ExchangeAsync("user-token", default));
        Assert.Equal("pp-token", await broker.ExchangeAsync("user-token", default));

        var request = Assert.Single(entra.Requests);
        Assert.Equal($"https://login.microsoftonline.com/{TestApp.TenantId}/oauth2/v2.0/token", request.Uri.ToString());
        var form = request.Body.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", form["grant_type"]);
        Assert.Equal(TestApp.ClientId, form["client_id"]);
        Assert.Equal("user-token", form["assertion"]);
        Assert.Equal("on_behalf_of", form["requested_token_use"]);
        Assert.Equal("https://api.powerplatform.com/.default", form["scope"]);
        Assert.Equal("local-secret", form["client_secret"]);
    }

    [Fact]
    public async Task Different_users_never_share_an_exchanged_token()
    {
        var count = 0;
        var entra = new FakeEntra { Responder = (_, _) => FakeEntra.Json(HttpStatusCode.OK, $$"""{"access_token":"pp-{{++count}}","expires_in":3600}""") };
        var broker = Broker(entra, new ExchangeOptions { ClientSecret = "s" });

        Assert.Equal("pp-1", await broker.ExchangeAsync("alice-token", default));
        Assert.Equal("pp-2", await broker.ExchangeAsync("bob-token", default));
        Assert.Equal("pp-1", await broker.ExchangeAsync("alice-token", default));
    }

    [Fact]
    public async Task Short_lived_tokens_are_not_cached()
    {
        var entra = new FakeEntra { Responder = (_, _) => FakeEntra.Json(HttpStatusCode.OK, """{"access_token":"pp","expires_in":120}""") };
        var broker = Broker(entra, new ExchangeOptions { ClientSecret = "s" });

        await broker.ExchangeAsync("t", default);
        await broker.ExchangeAsync("t", default);

        Assert.Equal(2, entra.Requests.Count);
    }

    [Fact]
    public async Task Managed_identity_token_is_used_as_the_client_assertion_and_reused()
    {
        var entra = new FakeEntra
        {
            Responder = (request, _) => request.RequestUri!.Host == "identity.local"
                ? FakeEntra.Json(HttpStatusCode.OK, $$"""{"access_token":"mi-assertion","expires_on":"{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}"}""")
                : FakeEntra.Json(HttpStatusCode.OK, """{"access_token":"pp-token","expires_in":3600}""")
        };

        var previous = (Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"), Environment.GetEnvironmentVariable("IDENTITY_HEADER"));
        Environment.SetEnvironmentVariable("IDENTITY_ENDPOINT", "http://identity.local/msi/token");
        Environment.SetEnvironmentVariable("IDENTITY_HEADER", "secret-header");
        try
        {
            var broker = Broker(entra, new ExchangeOptions { ManagedIdentityClientId = "33333333-3333-3333-3333-333333333333" });

            await broker.ExchangeAsync("user-one", default);
            await broker.ExchangeAsync("user-two", default);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IDENTITY_ENDPOINT", previous.Item1);
            Environment.SetEnvironmentVariable("IDENTITY_HEADER", previous.Item2);
        }

        var identityCalls = entra.Requests.Where(r => r.Uri.Host == "identity.local").ToList();
        var single = Assert.Single(identityCalls);
        Assert.Equal("secret-header", single.IdentityHeader);
        Assert.Contains("resource=api%3A%2F%2FAzureADTokenExchange", single.Uri.Query);
        Assert.Contains("client_id=33333333-3333-3333-3333-333333333333", single.Uri.Query);

        var exchanges = entra.Requests.Where(r => r.Uri.Host == "login.microsoftonline.com").ToList();
        Assert.Equal(2, exchanges.Count);
        Assert.All(exchanges, e =>
        {
            Assert.Contains("client_assertion=mi-assertion", e.Body);
            Assert.Contains("client_assertion_type=urn%3Aietf%3Aparams%3Aoauth%3Aclient-assertion-type%3Ajwt-bearer", e.Body);
            Assert.DoesNotContain("client_secret", e.Body);
        });
    }

    [Fact]
    public async Task Without_a_secret_or_managed_identity_the_exchange_reports_it_is_not_configured()
    {
        var broker = Broker(new FakeEntra(), new ExchangeOptions());

        var error = await Assert.ThrowsAsync<GatewayException>(() => broker.ExchangeAsync("t", default));

        Assert.Equal("EXCHANGE_NOT_CONFIGURED", error.Code);
    }

    [Theory]
    [InlineData("""{"error":"invalid_grant","error_description":"AADSTS65001: The user or administrator has not consented"}""", "CONSENT_REQUIRED", 403)]
    [InlineData("""{"error":"interaction_required","error_description":"AADSTS50076: MFA"}""", "CONSENT_REQUIRED", 403)]
    [InlineData("""{"error":"invalid_grant","error_description":"AADSTS700024: expired assertion"}""", "UNAUTHORIZED", 401)]
    [InlineData("""{"error":"invalid_client","error_description":"AADSTS7000215: bad secret"}""", "EXCHANGE_FAILED", 502)]
    public async Task Entra_errors_become_plain_messages_without_leaking_details(string response, string code, int status)
    {
        var entra = new FakeEntra { Responder = (_, _) => FakeEntra.Json(HttpStatusCode.BadRequest, response) };
        var broker = Broker(entra, new ExchangeOptions { ClientSecret = "s" });

        var error = await Assert.ThrowsAsync<GatewayException>(() => broker.ExchangeAsync("t", default));

        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.Status);
        Assert.DoesNotContain("AADSTS", error.Message);
    }
}

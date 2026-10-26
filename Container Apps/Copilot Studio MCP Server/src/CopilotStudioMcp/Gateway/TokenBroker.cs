using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CopilotStudioMcp.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CopilotStudioMcp.Gateway;

/// <summary>Turns the token the plugin signed in with into a token Power Platform accepts, for the same user.</summary>
public interface ITokenBroker
{
    Task<string> ExchangeAsync(string userToken, CancellationToken ct);
}

/// <summary>
/// On-behalf-of exchange against Microsoft Entra. The server proves it is the Entra app with a token from its
/// managed identity (a federated credential on the app trusts it), so no client secret exists to store or leak.
/// </summary>
public sealed class OnBehalfOfBroker(
    HttpClient http,
    AuthOptions auth,
    IOptions<ExchangeOptions> options,
    IMemoryCache cache,
    TimeProvider clock) : ITokenBroker
{
    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    private const string TokenExchangeAudience = "api://AzureADTokenExchange";
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    private readonly ExchangeOptions _options = options.Value;
    private readonly SemaphoreSlim _assertionLock = new(1, 1);
    private (string Token, DateTimeOffset Expires)? _assertion;

    public async Task<string> ExchangeAsync(string userToken, CancellationToken ct)
    {
        // The cache key is a hash, so the user's token is never held as a key. Entries expire with the token they hold.
        var key = "obo:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userToken)));
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["client_id"] = auth.ClientId,
            ["assertion"] = userToken,
            ["scope"] = _options.Scope,
            ["requested_token_use"] = "on_behalf_of"
        };

        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            form["client_secret"] = _options.ClientSecret;
        }
        else
        {
            form["client_assertion_type"] = AssertionType;
            form["client_assertion"] = await GetManagedIdentityAssertionAsync(ct);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{auth.TenantId}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using var response = await SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode || !root.TryGetProperty("access_token", out var tokenElement))
        {
            throw ExchangeError(root);
        }

        var token = tokenElement.GetString()!;
        var lifetime = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(10);
        if (lifetime > Skew)
        {
            cache.Set(key, token, lifetime - Skew);
        }

        return token;
    }

    private async Task<string> GetManagedIdentityAssertionAsync(CancellationToken ct)
    {
        if (_assertion is { } held && held.Expires - clock.GetUtcNow() > Skew)
        {
            return held.Token;
        }

        await _assertionLock.WaitAsync(ct);
        try
        {
            if (_assertion is { } again && again.Expires - clock.GetUtcNow() > Skew)
            {
                return again.Token;
            }

            var endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            var header = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(header) || string.IsNullOrEmpty(_options.ManagedIdentityClientId))
            {
                throw new GatewayException(500, "EXCHANGE_NOT_CONFIGURED", "The gateway has no managed identity configured to exchange tokens.");
            }

            var url = $"{endpoint}?resource={Uri.EscapeDataString(TokenExchangeAudience)}&api-version=2019-08-01&client_id={Uri.EscapeDataString(_options.ManagedIdentityClientId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-IDENTITY-HEADER", header);

            using var response = await SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new GatewayException(502, "EXCHANGE_IDENTITY_FAILED", "The gateway could not get its managed identity token.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresOn = doc.RootElement.TryGetProperty("expires_on", out var e) && long.TryParse(e.GetString(), out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : clock.GetUtcNow().AddMinutes(30);
            _assertion = (token, expiresOn);
            return token;
        }
        finally
        {
            _assertionLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw new GatewayException(502, "EXCHANGE_UNREACHABLE", "Microsoft Entra could not be reached to exchange the sign-in token.");
        }
    }

    /// <summary>Maps an Entra error to a message the user can act on, without echoing tokens or details.</summary>
    private static GatewayException ExchangeError(JsonElement root)
    {
        var description = root.TryGetProperty("error_description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
        var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;

        if (error == "interaction_required" || description.Contains("AADSTS65001", StringComparison.Ordinal) || description.Contains("AADSTS50076", StringComparison.Ordinal) || description.Contains("AADSTS50079", StringComparison.Ordinal))
        {
            return new GatewayException(
                403,
                "CONSENT_REQUIRED",
                "The plugin's Entra app does not yet have consent to call Power Platform for you, or your account needs multi-factor authentication. Ask an admin to grant consent, or sign in again.");
        }

        if (error == "invalid_grant")
        {
            return new GatewayException(401, "UNAUTHORIZED", "Your sign-in could not be exchanged. Sign in again.");
        }

        return new GatewayException(502, "EXCHANGE_FAILED", "Microsoft Entra refused to exchange the sign-in token for Power Platform.");
    }
}

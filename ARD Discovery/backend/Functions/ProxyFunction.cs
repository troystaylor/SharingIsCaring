#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// POST /proxy — Proxy MCP calls to discovered endpoints.
/// 
/// Three-tier auth resolution:
///   Tier 1: OBO exchange (same Entra tenant — automatic, no user action)
///   Tier 2: Org-level pre-connected token (admin connects domain once)
///   Tier 3: Per-user token or elicitation fallback (if enabled)
/// 
/// For unauthenticated targets, proxies directly with no auth.
/// </summary>
public class ProxyFunction
{
    private readonly RegistryClient _registry;
    private readonly TokenStore _tokenStore;
    private readonly OAuthConfigStore _oauthConfig;
    private readonly OboTokenService _obo;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<ProxyFunction> _logger;

    private static readonly bool ElicitationEnabled =
        string.Equals(Environment.GetEnvironmentVariable("EnableElicitation"), "true",
            StringComparison.OrdinalIgnoreCase);

    public ProxyFunction(RegistryClient registry, TokenStore tokenStore,
        OAuthConfigStore oauthConfig, OboTokenService obo, RateLimiter rateLimiter,
        ILogger<ProxyFunction> logger)
    {
        _registry = registry;
        _tokenStore = tokenStore;
        _oauthConfig = oauthConfig;
        _obo = obo;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    [Function("Proxy")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "proxy")] HttpRequestData req)
    {
        if (!AuthHelper.ValidateApiKey(req))
            return AuthHelper.Unauthorized(req);

        var userId = AuthHelper.GetUserId(req);

        // Rate limiting: 60 requests per minute per user
        if (!_rateLimiter.TryAcquire(userId))
        {
            var retryAfter = _rateLimiter.GetRetryAfterSeconds(userId);
            var tooMany = req.CreateResponse(HttpStatusCode.TooManyRequests);
            tooMany.Headers.Add("Retry-After", retryAfter.ToString());
            await tooMany.WriteAsJsonAsync(new { error = "Rate limit exceeded. Try again later.", retryAfterSeconds = retryAfter });
            return tooMany;
        }

        var bodyString = await req.ReadAsStringAsync();

        if (string.IsNullOrEmpty(bodyString))
            return CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required.");

        var body = JsonNode.Parse(bodyString);
        var targetUrl = body?["targetUrl"]?.GetValue<string>();
        var mcpRequest = body?["mcpRequest"];

        if (string.IsNullOrEmpty(targetUrl))
            return CreateErrorResponse(req, HttpStatusCode.BadRequest, "targetUrl is required.");

        if (mcpRequest == null)
            return CreateErrorResponse(req, HttpStatusCode.BadRequest, "mcpRequest is required.");

        // Validate HTTPS
        if (!targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return CreateErrorResponse(req, HttpStatusCode.BadRequest, "targetUrl must use HTTPS.");

        var targetDomain = OAuthConfigStore.ExtractDomain(targetUrl);
        var config = _oauthConfig.GetConfig(targetDomain);

        // ================================================================
        // No auth configured for this domain — proxy directly
        // ================================================================
        if (config == null)
        {
            return await ProxyDirectAsync(req, targetUrl, targetDomain, mcpRequest);
        }

        // ================================================================
        // Tier 1: OBO exchange (same Entra tenant, automatic)
        // ================================================================
        if (!string.IsNullOrEmpty(config.OboScope) && _obo.IsConfigured)
        {
            var userToken = ExtractUserToken(req);
            if (!string.IsNullOrEmpty(userToken))
            {
                var oboToken = await _obo.TryExchangeAsync(userToken, config.OboScope);
                if (!string.IsNullOrEmpty(oboToken))
                {
                    _logger.LogInformation("Tier 1 (OBO) succeeded for {Domain}", targetDomain);
                    var result = await _registry.ProxyMcpCallAsync(targetUrl, mcpRequest, oboToken);
                    if (!string.IsNullOrEmpty(result))
                        return await CreateJsonResponse(req, result);

                    // OBO token was rejected — fall through to Tier 2
                    _logger.LogWarning("Tier 1 (OBO) token rejected by {Domain}, trying Tier 2", targetDomain);
                }
            }
        }

        // ================================================================
        // Tier 2: Org-level pre-connected token
        // ================================================================
        var orgToken = await _tokenStore.GetOrgTokenAsync(targetDomain);
        if (orgToken != null && orgToken.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("Tier 2 (org token) for {Domain}", targetDomain);
            var result = await _registry.ProxyMcpCallAsync(targetUrl, mcpRequest, orgToken.AccessToken);
            if (!string.IsNullOrEmpty(result))
                return await CreateJsonResponse(req, result);

            // Org token rejected — try refresh
            if (!string.IsNullOrEmpty(orgToken.RefreshToken))
            {
                var refreshed = await RefreshTokenAsync("_org", targetDomain, orgToken.RefreshToken, config);
                if (refreshed)
                {
                    orgToken = await _tokenStore.GetOrgTokenAsync(targetDomain);
                    if (orgToken != null)
                    {
                        result = await _registry.ProxyMcpCallAsync(targetUrl, mcpRequest, orgToken.AccessToken);
                        if (!string.IsNullOrEmpty(result))
                            return await CreateJsonResponse(req, result);
                    }
                }
            }

            _logger.LogWarning("Tier 2 (org token) failed for {Domain}, trying Tier 3", targetDomain);
        }

        // ================================================================
        // Tier 3: Per-user token (check store) or elicitation/error
        // ================================================================
        var userStoredToken = await _tokenStore.GetTokenAsync(userId, targetDomain);
        if (userStoredToken != null)
        {
            // Try refresh if near expiry
            if (userStoredToken.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5)
                && !string.IsNullOrEmpty(userStoredToken.RefreshToken))
            {
                var refreshed = await RefreshTokenAsync(userId, targetDomain, userStoredToken.RefreshToken, config);
                if (refreshed)
                    userStoredToken = await _tokenStore.GetTokenAsync(userId, targetDomain);
                else
                {
                    await _tokenStore.DeleteTokenAsync(userId, targetDomain);
                    userStoredToken = null;
                }
            }

            if (userStoredToken != null)
            {
                var result = await _registry.ProxyMcpCallAsync(targetUrl, mcpRequest, userStoredToken.AccessToken);
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogInformation("Tier 3 (user token) succeeded for {Domain}", targetDomain);
                    return await CreateJsonResponse(req, result);
                }

                // Token rejected — delete it
                await _tokenStore.DeleteTokenAsync(userId, targetDomain);
            }
        }

        // No valid token from any tier — elicitation or error
        if (ElicitationEnabled)
        {
            _logger.LogInformation("Tier 3: returning elicitation for {Domain}", targetDomain);
            return await CreateElicitationResponse(req, userId, targetDomain, targetUrl);
        }

        // Elicitation disabled — return actionable error
        var backendBaseUrl = Environment.GetEnvironmentVariable("BackendBaseUrl")
            ?? "https://ard-discovery.azurewebsites.net";

        return CreateErrorResponse(req, HttpStatusCode.Forbidden,
            $"The service at {targetDomain} requires authentication but no valid credentials are available. " +
            $"Options: (1) Ask your admin to pre-connect this domain at {backendBaseUrl}/api/connect?target={Uri.EscapeDataString(targetDomain)} " +
            $"(2) If this is a same-tenant Entra service, configure OboScope in the OAuthConfigs setting.");
    }

    // ====================================================================
    // Direct proxy (no auth)
    // ====================================================================

    private async Task<HttpResponseData> ProxyDirectAsync(
        HttpRequestData req, string targetUrl, string targetDomain, JsonNode mcpRequest)
    {
        var result = await _registry.ProxyMcpCallAsync(targetUrl, mcpRequest);

        if (string.IsNullOrEmpty(result))
        {
            return CreateErrorResponse(req, HttpStatusCode.Forbidden,
                $"Target at {targetDomain} returned 401/403 but has no auth configured. " +
                "Ask an admin to register OAuth credentials for this domain in the OAuthConfigs setting.");
        }

        return await CreateJsonResponse(req, result);
    }

    // ====================================================================
    // Elicitation response (behind feature flag)
    // ====================================================================

    private async Task<HttpResponseData> CreateElicitationResponse(
        HttpRequestData req, string userId, string targetDomain, string targetUrl)
    {
        var backendBaseUrl = Environment.GetEnvironmentVariable("BackendBaseUrl")
            ?? "https://ard-discovery.azurewebsites.net";

        var state = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { userId, targetDomain, targetUrl })
            )
        );

        var connectUrl = $"{backendBaseUrl}/api/connect" +
            $"?target={Uri.EscapeDataString(targetDomain)}" +
            $"&state={Uri.EscapeDataString(state)}";

        var responseBody = new JsonObject
        {
            ["elicitation"] = new JsonObject
            {
                ["url"] = connectUrl,
                ["message"] = $"The service at {targetDomain} requires authentication. Please sign in to continue.",
                ["requestState"] = state
            }
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(responseBody.ToJsonString());
        return response;
    }

    // ====================================================================
    // Token refresh
    // ====================================================================

    private async Task<bool> RefreshTokenAsync(string userId, string targetDomain,
        string refreshToken, OAuthConfig config)
    {
        try
        {
            using var httpClient = new HttpClient();
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("client_id", config.ClientId),
                new KeyValuePair<string, string>("client_secret", config.ClientSecret),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
            });

            var tokenResponse = await httpClient.PostAsync(config.TokenUrl, tokenRequest);
            if (!tokenResponse.IsSuccessStatusCode) return false;

            var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
            var tokenJson = JsonNode.Parse(tokenBody);

            var accessToken = tokenJson?["access_token"]?.GetValue<string>();
            var newRefreshToken = tokenJson?["refresh_token"]?.GetValue<string>() ?? refreshToken;
            var expiresIn = tokenJson?["expires_in"]?.GetValue<int>() ?? 3600;

            if (string.IsNullOrEmpty(accessToken)) return false;

            await _tokenStore.UpsertTokenAsync(userId, targetDomain, accessToken, newRefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn), config.Scopes);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token refresh failed for {Domain}", targetDomain);
            return false;
        }
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    /// <summary>
    /// Extract the user's Entra ID access token from App Service auth headers.
    /// </summary>
    private static string? ExtractUserToken(HttpRequestData req)
    {
        // App Service injects this header when EasyAuth is configured
        if (req.Headers.TryGetValues("x-ms-token-aad-access-token", out var values))
        {
            foreach (var val in values)
            {
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }

        // Fallback: check Authorization header
        if (req.Headers.TryGetValues("Authorization", out var authValues))
        {
            foreach (var val in authValues)
            {
                if (val.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return val.Substring(7);
            }
        }

        return null;
    }

    private static async Task<HttpResponseData> CreateJsonResponse(HttpRequestData req, string json)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(json);
        return response;
    }

    private static HttpResponseData CreateErrorResponse(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        response.WriteString(JsonSerializer.Serialize(new { error = message }));
        return response;
    }
}

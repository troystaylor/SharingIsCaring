using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// GET /callback — OAuth redirect handler.
/// 
/// Flow:
/// 1. Target's auth server redirects here with ?code=...&state=...
/// 2. Decode state to get userId + targetDomain
/// 3. Exchange authorization code for tokens
/// 4. Store tokens in Table Storage bound to userId + targetDomain
/// 5. Show success page — user can close the tab
/// </summary>
public class CallbackFunction
{
    private readonly TokenStore _tokenStore;
    private readonly OAuthConfigStore _oauthConfig;

    public CallbackFunction(TokenStore tokenStore, OAuthConfigStore oauthConfig)
    {
        _tokenStore = tokenStore;
        _oauthConfig = oauthConfig;
    }

    [Function("Callback")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "callback")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var code = query["code"];
        var state = query["state"];
        var error = query["error"];

        // Handle OAuth errors
        if (!string.IsNullOrEmpty(error))
        {
            var errorDesc = query["error_description"] ?? "Unknown error";
            return CreateHtmlResponse(req, HttpStatusCode.BadRequest,
                "Authorization Failed",
                $"<p>The authorization server returned an error: <strong>{WebUtility.HtmlEncode(error)}</strong></p>" +
                $"<p>{WebUtility.HtmlEncode(errorDesc)}</p>" +
                "<p>You can close this tab and try again.</p>");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return CreateHtmlResponse(req, HttpStatusCode.BadRequest,
                "Missing Parameters",
                "<p>Authorization code and state are required.</p>");
        }

        // Decode state
        string userId, targetDomain;
        try
        {
            var stateJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));
            var stateObj = JsonDocument.Parse(stateJson);
            userId = stateObj.RootElement.GetProperty("userId").GetString() ?? string.Empty;
            targetDomain = stateObj.RootElement.GetProperty("targetDomain").GetString() ?? string.Empty;
        }
        catch
        {
            return CreateHtmlResponse(req, HttpStatusCode.BadRequest,
                "Invalid State",
                "<p>The state parameter is malformed. This may indicate tampering.</p>");
        }

        // Look up OAuth config
        var config = _oauthConfig.GetConfig(targetDomain);
        if (config == null)
        {
            return CreateHtmlResponse(req, HttpStatusCode.NotFound,
                "Configuration Not Found",
                $"<p>No OAuth configuration found for <strong>{WebUtility.HtmlEncode(targetDomain)}</strong>.</p>");
        }

        // Exchange code for tokens
        var backendBaseUrl = Environment.GetEnvironmentVariable("BackendBaseUrl")
            ?? "https://ard-discovery.azurewebsites.net";
        var redirectUri = $"{backendBaseUrl}/api/callback";

        try
        {
            using var httpClient = new HttpClient();
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("client_id", config.ClientId),
                new KeyValuePair<string, string>("client_secret", config.ClientSecret),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
            });

            var tokenResponse = await httpClient.PostAsync(config.TokenUrl, tokenRequest);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorBody = await tokenResponse.Content.ReadAsStringAsync();
                return CreateHtmlResponse(req, HttpStatusCode.BadGateway,
                    "Token Exchange Failed",
                    $"<p>Failed to exchange authorization code for tokens.</p>" +
                    $"<p>Status: {tokenResponse.StatusCode}</p>");
            }

            var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
            var tokenJson = JsonNode.Parse(tokenBody);

            var accessToken = tokenJson?["access_token"]?.GetValue<string>();
            var refreshToken = tokenJson?["refresh_token"]?.GetValue<string>() ?? string.Empty;
            var expiresIn = tokenJson?["expires_in"]?.GetValue<int>() ?? 3600;

            if (string.IsNullOrEmpty(accessToken))
            {
                return CreateHtmlResponse(req, HttpStatusCode.BadGateway,
                    "Invalid Token Response",
                    "<p>The authorization server did not return an access token.</p>");
            }

            // Store token bound to user + domain
            await _tokenStore.UpsertTokenAsync(userId, targetDomain, accessToken, refreshToken,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn), config.Scopes);

            return CreateHtmlResponse(req, HttpStatusCode.OK,
                "Connected Successfully",
                $"<p>You are now connected to <strong>{WebUtility.HtmlEncode(targetDomain)}</strong>.</p>" +
                "<p>You can close this tab and return to your conversation. " +
                "The agent will automatically retry your request with your credentials.</p>" +
                "<script>setTimeout(function() { window.close(); }, 3000);</script>");
        }
        catch (Exception ex)
        {
            return CreateHtmlResponse(req, HttpStatusCode.InternalServerError,
                "Unexpected Error",
                $"<p>An error occurred during token exchange: {WebUtility.HtmlEncode(ex.Message)}</p>");
        }
    }

    private static HttpResponseData CreateHtmlResponse(HttpRequestData req, HttpStatusCode status,
        string title, string bodyHtml)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        response.WriteString(
            "<!DOCTYPE html>" +
            "<html><head>" +
            $"<title>{WebUtility.HtmlEncode(title)} — ARD Discovery</title>" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            "<style>" +
            "body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; " +
            "max-width: 480px; margin: 80px auto; padding: 0 20px; color: #333; }" +
            "h2 { color: #1a73e8; }" +
            "</style>" +
            "</head><body>" +
            $"<h2>{WebUtility.HtmlEncode(title)}</h2>" +
            bodyHtml +
            "</body></html>");
        return response;
    }
}

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ArdDiscovery.Services;

namespace ArdDiscovery.Functions;

/// <summary>
/// GET /connect — Start the OAuth authorization flow for a target domain.
/// 
/// This is the URL that MCP elicitation opens in the user's browser.
/// Per the elicitation security spec, this page MUST verify that the user
/// opening it is the same user who triggered the elicitation request.
/// 
/// Flow:
/// 1. User opens /connect?target=api.acme.com&state=...
/// 2. We validate the state parameter (contains userId binding)
/// 3. If the user is authenticated (App Service auth), verify identity matches
/// 4. Redirect to the target's OAuth authorize endpoint
/// </summary>
public class ConnectFunction
{
    private readonly OAuthConfigStore _oauthConfig;

    public ConnectFunction(OAuthConfigStore oauthConfig)
    {
        _oauthConfig = oauthConfig;
    }

    [Function("Connect")]
    public Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "connect")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var targetDomain = query["target"];
        var state = query["state"];

        if (string.IsNullOrEmpty(targetDomain) || string.IsNullOrEmpty(state))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            badRequest.Headers.Add("Content-Type", "text/html");
            badRequest.WriteString("<html><body><h2>Missing parameters</h2><p>target and state are required.</p></body></html>");
            return Task.FromResult(badRequest);
        }

        // Validate state — decode and verify user binding
        string requestUserId;
        try
        {
            var stateJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));
            var stateObj = JsonDocument.Parse(stateJson);
            requestUserId = stateObj.RootElement.GetProperty("userId").GetString() ?? string.Empty;
        }
        catch
        {
            var badState = req.CreateResponse(HttpStatusCode.BadRequest);
            badState.Headers.Add("Content-Type", "text/html");
            badState.WriteString("<html><body><h2>Invalid state</h2><p>The state parameter is malformed.</p></body></html>");
            return Task.FromResult(badState);
        }

        // Verify identity: if App Service auth is enabled, the browsing user must match
        var browsingUserId = AuthHelper.GetUserId(req);
        if (browsingUserId != "anonymous" && requestUserId != "anonymous"
            && !string.Equals(browsingUserId, requestUserId, StringComparison.OrdinalIgnoreCase))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            forbidden.Headers.Add("Content-Type", "text/html");
            forbidden.WriteString(
                "<html><body><h2>Identity mismatch</h2>" +
                "<p>The user who initiated this request is different from the user opening this page. " +
                "This may indicate a phishing attempt.</p></body></html>");
            return Task.FromResult(forbidden);
        }

        // Look up OAuth config for this domain
        var config = _oauthConfig.GetConfig(targetDomain);
        if (config == null)
        {
            var notConfigured = req.CreateResponse(HttpStatusCode.NotFound);
            notConfigured.Headers.Add("Content-Type", "text/html");
            notConfigured.WriteString(
                $"<html><body><h2>No OAuth configuration</h2>" +
                $"<p>No OAuth credentials are registered for <strong>{System.Net.WebUtility.HtmlEncode(targetDomain)}</strong>. " +
                $"Ask an administrator to register this service.</p></body></html>");
            return Task.FromResult(notConfigured);
        }

        // Build the OAuth authorize URL
        var backendBaseUrl = Environment.GetEnvironmentVariable("BackendBaseUrl")
            ?? "https://ard-discovery.azurewebsites.net";
        var redirectUri = $"{backendBaseUrl}/api/callback";

        var authorizeUrl =
            $"{config.AuthorizeUrl}" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(config.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(config.Scopes)}" +
            $"&state={Uri.EscapeDataString(state)}";

        // Redirect to the target's authorization server
        var redirect = req.CreateResponse(HttpStatusCode.Redirect);
        redirect.Headers.Add("Location", authorizeUrl);
        return Task.FromResult(redirect);
    }
}

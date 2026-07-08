// ============================================================================
// MCP OAuth 2.1 WWW-Authenticate Handler
// ============================================================================
// Handles MCP authorization discovery per the 2025-11-25 spec:
//   - Parses WWW-Authenticate headers from 401/403 responses (RFC 6750)
//   - Fetches Protected Resource Metadata (RFC 9728)
//   - Discovers Authorization Server Metadata (RFC 8414 / OIDC Discovery)
//   - Detects Azure AD/Entra ID servers (api:// scopes, login.microsoftonline.com)
//   - Attempts token refresh and request retry
//   - Handles insufficient_scope step-up authorization (403)
//
// Copilot Studio integration:
//   When using the MCP onboarding wizard with "Dynamic discovery" OAuth,
//   Copilot Studio handles DCR + metadata discovery natively. This code is
//   for the custom connector path (Option 2) where you need script.csx to
//   bridge Power Platform's static OAuth with MCP's dynamic discovery.
//
// References:
//   - MCP Auth Spec: https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization
//   - ISE Blog: https://devblogs.microsoft.com/ise/aca-secure-mcp-server-oauth21-azure-ad/
//   - Copilot Studio MCP: https://learn.microsoft.com/en-us/microsoft-copilot-studio/mcp-add-existing-server-to-agent
//
// Usage: Call SendWithMcpAuthAsync() instead of this.Context.SendAsync()
//        in your MCP connector's ExecuteAsync method.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const int MaxAuthRetries = 2;
    // Minimum remaining token lifetime (seconds) before considering it expired.
    // ISE blog recommends 5 minutes to prevent race conditions during OBO exchange.
    private const int TokenExpiryBufferSeconds = 300;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokeMCP":
                // MCP endpoint — apply auth discovery and retry on 401/403
                return await SendWithMcpAuthAsync(this.Context.Request);

            case "GetResourceMetadata":
                // RFC 9728 metadata endpoint — public, no auth needed
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken);

            default:
                // Other typed operations — apply auth handling
                return await SendWithMcpAuthAsync(this.Context.Request);
        }
    }

    // ========================================================================
    // MAIN ENTRY POINT: Send request with MCP OAuth 2.1 error handling
    // ========================================================================
    /// <summary>
    /// Sends the request and handles MCP 401/403 responses by parsing
    /// WWW-Authenticate, discovering the authorization server, and retrying.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithMcpAuthAsync(
        HttpRequestMessage originalRequest,
        int retryCount = 0)
    {
        var response = await this.Context.SendAsync(originalRequest, this.CancellationToken);
        var statusCode = (int)response.StatusCode;

        // ── 401 Unauthorized: Token missing, expired, or wrong audience ──
        if (statusCode == 401 && retryCount < MaxAuthRetries)
        {
            var challenge = ParseWwwAuthenticate(response);

            if (challenge != null)
            {
                // Discover the authorization server from resource metadata
                var resourceMeta = await FetchProtectedResourceMetadataAsync(
                    challenge.ResourceMetadataUrl,
                    originalRequest.RequestUri);

                var authServerMeta = resourceMeta != null
                    ? await FetchAuthServerMetadataAsync(resourceMeta)
                    : null;

                // Build diagnostic info for the response
                var diagnostics = BuildAuthDiagnostics(challenge, resourceMeta, authServerMeta);

                // If we discovered a token endpoint, attempt token refresh
                if (authServerMeta != null)
                {
                    var tokenEndpoint = authServerMeta.Value<string>("token_endpoint");
                    var requiredScopes = challenge.Scope
                        ?? resourceMeta?.Value<string>("scopes_supported")
                        ?? "";

                    if (!string.IsNullOrEmpty(tokenEndpoint))
                    {
                        // Attempt to get a new token using client_credentials
                        // or refresh_token if available
                        var newToken = await AttemptTokenRefreshAsync(
                            tokenEndpoint, requiredScopes, originalRequest.RequestUri);

                        if (!string.IsNullOrEmpty(newToken))
                        {
                            // Clone the original request with the new token and retry
                            var retryRequest = await CloneRequestAsync(originalRequest);
                            retryRequest.Headers.Authorization =
                                new AuthenticationHeaderValue("Bearer", newToken);

                            return await SendWithMcpAuthAsync(retryRequest, retryCount + 1);
                        }
                    }
                }

                // Can't auto-recover — return 401 with discovery diagnostics
                return CreateAuthErrorResponse(response, diagnostics);
            }
        }

        // ── 403 Forbidden: Insufficient scope (step-up auth needed) ──
        if (statusCode == 403)
        {
            var challenge = ParseWwwAuthenticate(response);

            if (challenge != null && challenge.Error == "insufficient_scope")
            {
                // Detect Azure AD scopes (api://{client-id}/{scope-name} format)
                var isAzureAd = IsAzureAdScope(challenge.Scope);

                var diagnostics = new JObject
                {
                    ["error"] = "insufficient_scope",
                    ["required_scopes"] = challenge.Scope ?? "(not specified)",
                    ["error_description"] = challenge.ErrorDescription
                        ?? "The access token does not have sufficient scopes for this operation.",
                    ["resource_metadata_url"] = challenge.ResourceMetadataUrl ?? "",
                    ["is_azure_ad"] = isAzureAd,
                    ["action_required"] = isAzureAd
                        ? "Update your connector's OAuth scopes in apiProperties.json. " +
                          "For Azure AD/Entra ID, scopes use the format api://{client-id}/{scope-name}. " +
                          "Also verify the App Registration's 'Expose an API' section includes these scopes, " +
                          "and that your client app is pre-authorized if needed."
                        : "Update your connector's OAuth scopes in apiProperties.json " +
                          "to include the required scopes, then re-authenticate the connection."
                };

                return CreateAuthErrorResponse(response, diagnostics);
            }
        }

        return response;
    }

    // ========================================================================
    // WWW-Authenticate HEADER PARSER (RFC 6750 + RFC 9728)
    // ========================================================================

    /// <summary>
    /// Parsed components from a WWW-Authenticate Bearer challenge.
    /// </summary>
    private class BearerChallenge
    {
        public string Realm { get; set; }
        public string Scope { get; set; }
        public string Error { get; set; }
        public string ErrorDescription { get; set; }
        public string ResourceMetadataUrl { get; set; }
        public Dictionary<string, string> AllParameters { get; set; }
    }

    /// <summary>
    /// Parses the WWW-Authenticate header from a 401/403 response.
    /// Handles RFC 6750 Bearer scheme with RFC 9728 resource_metadata extension.
    /// 
    /// Example header:
    ///   WWW-Authenticate: Bearer resource_metadata="https://mcp.example.com/.well-known/oauth-protected-resource",
    ///                            scope="files:read files:write"
    /// </summary>
    private BearerChallenge ParseWwwAuthenticate(HttpResponseMessage response)
    {
        IEnumerable<string> wwwAuthValues;
        if (!response.Headers.TryGetValues("WWW-Authenticate", out wwwAuthValues))
            return null;

        // Find the Bearer challenge (there could be multiple schemes)
        var bearerChallenge = wwwAuthValues
            .FirstOrDefault(v => v.TrimStart().StartsWith("Bearer", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(bearerChallenge))
            return null;

        // Strip "Bearer " prefix
        var paramString = bearerChallenge.Substring("Bearer".Length).Trim();

        // Parse key="value" pairs (RFC 6750 auth-param format)
        var parameters = ParseAuthParams(paramString);

        return new BearerChallenge
        {
            Realm = GetParam(parameters, "realm"),
            Scope = GetParam(parameters, "scope"),
            Error = GetParam(parameters, "error"),
            ErrorDescription = GetParam(parameters, "error_description"),
            ResourceMetadataUrl = GetParam(parameters, "resource_metadata"),
            AllParameters = parameters
        };
    }

    /// <summary>
    /// Parses RFC 6750 / RFC 7235 auth-param pairs from a challenge string.
    /// Handles quoted values, unquoted values, and comma/space separators.
    /// 
    /// Examples:
    ///   realm="example", scope="read write"
    ///   error="insufficient_scope", scope="files:read files:write", resource_metadata="https://..."
    /// </summary>
    private Dictionary<string, string> ParseAuthParams(string paramString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Regex: key = "quoted value" or key = unquoted-value
        var pattern = @"(\w+)\s*=\s*(?:""([^""]*)""|([^\s,]+))";
        var matches = Regex.Matches(paramString, pattern);

        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            result[key] = value;
        }

        return result;
    }

    private string GetParam(Dictionary<string, string> parameters, string key)
    {
        string value;
        return parameters.TryGetValue(key, out value) ? value : null;
    }

    // ========================================================================
    // PROTECTED RESOURCE METADATA (RFC 9728)
    // ========================================================================

    /// <summary>
    /// Fetches the OAuth 2.0 Protected Resource Metadata document.
    /// Tries the resource_metadata URL from WWW-Authenticate first,
    /// then falls back to well-known URI discovery per RFC 9728.
    /// 
    /// The metadata document contains authorization_servers[] which tells
    /// the client where to get tokens.
    /// </summary>
    private async Task<JObject> FetchProtectedResourceMetadataAsync(
        string resourceMetadataUrl,
        Uri serverUri)
    {
        // Priority 1: URL from WWW-Authenticate resource_metadata parameter
        if (!string.IsNullOrEmpty(resourceMetadataUrl))
        {
            var meta = await FetchJsonAsync(resourceMetadataUrl);
            if (meta != null) return meta;
        }

        // Priority 2: Well-known URI with path component
        // e.g., https://example.com/.well-known/oauth-protected-resource/mcp
        if (serverUri != null)
        {
            var pathComponent = serverUri.AbsolutePath.TrimEnd('/');
            if (!string.IsNullOrEmpty(pathComponent) && pathComponent != "/")
            {
                var wellKnownWithPath = $"{serverUri.Scheme}://{serverUri.Authority}" +
                    $"/.well-known/oauth-protected-resource{pathComponent}";
                var meta = await FetchJsonAsync(wellKnownWithPath);
                if (meta != null) return meta;
            }

            // Priority 3: Well-known URI at root
            // e.g., https://example.com/.well-known/oauth-protected-resource
            var wellKnownRoot = $"{serverUri.Scheme}://{serverUri.Authority}" +
                "/.well-known/oauth-protected-resource";
            var rootMeta = await FetchJsonAsync(wellKnownRoot);
            if (rootMeta != null) return rootMeta;
        }

        return null;
    }

    // ========================================================================
    // AUTHORIZATION SERVER METADATA DISCOVERY (RFC 8414 + OIDC Discovery)
    // ========================================================================

    /// <summary>
    /// Discovers the Authorization Server Metadata from the Protected Resource
    /// Metadata's authorization_servers list. Tries RFC 8414 and OIDC Discovery
    /// endpoints in the order specified by the MCP spec.
    /// </summary>
    private async Task<JObject> FetchAuthServerMetadataAsync(JObject resourceMetadata)
    {
        var authServers = resourceMetadata["authorization_servers"] as JArray;
        if (authServers == null || authServers.Count == 0)
            return null;

        // Use the first authorization server
        var issuer = authServers[0]?.ToString();
        if (string.IsNullOrEmpty(issuer))
            return null;

        var issuerUri = new Uri(issuer);
        var hasPath = !string.IsNullOrEmpty(issuerUri.AbsolutePath.Trim('/'));

        if (hasPath)
        {
            var pathComponent = issuerUri.AbsolutePath.TrimEnd('/');
            var baseUrl = $"{issuerUri.Scheme}://{issuerUri.Authority}";

            // 1. RFC 8414 with path insertion
            var rfcUrl = $"{baseUrl}/.well-known/oauth-authorization-server{pathComponent}";
            var meta = await FetchJsonAsync(rfcUrl);
            if (meta != null) return meta;

            // 2. OIDC Discovery with path insertion
            var oidcInsertUrl = $"{baseUrl}/.well-known/openid-configuration{pathComponent}";
            meta = await FetchJsonAsync(oidcInsertUrl);
            if (meta != null) return meta;

            // 3. OIDC Discovery path-appending
            var oidcAppendUrl = $"{issuer.TrimEnd('/')}/.well-known/openid-configuration";
            meta = await FetchJsonAsync(oidcAppendUrl);
            if (meta != null) return meta;
        }
        else
        {
            var baseUrl = issuer.TrimEnd('/');

            // 1. RFC 8414
            var rfcUrl = $"{baseUrl}/.well-known/oauth-authorization-server";
            var meta = await FetchJsonAsync(rfcUrl);
            if (meta != null) return meta;

            // 2. OIDC Discovery
            var oidcUrl = $"{baseUrl}/.well-known/openid-configuration";
            meta = await FetchJsonAsync(oidcUrl);
            if (meta != null) return meta;
        }

        return null;
    }

    // ========================================================================
    // TOKEN REFRESH / ACQUISITION
    // ========================================================================

    /// <summary>
    /// Attempts to acquire a new access token from the discovered token endpoint.
    /// 
    /// Strategy:
    /// 1. If the connector has client credentials available (via connection
    ///    parameters or hardcoded), use client_credentials grant
    /// 2. Include the RFC 8707 resource parameter to bind the token to the
    ///    MCP server's audience
    /// 3. For Azure AD: supports On-Behalf-Of (OBO) flow to exchange the
    ///    Power Platform token for one scoped to the MCP server
    /// 
    /// NOTE: Power Platform manages the primary OAuth flow. This is a fallback
    /// for cases where the platform token doesn't satisfy the MCP server's
    /// requirements (wrong audience, missing scopes, etc.).
    /// </summary>
    private async Task<string> AttemptTokenRefreshAsync(
        string tokenEndpoint,
        string requiredScopes,
        Uri mcpServerUri)
    {
        try
        {
            // Build the canonical resource URI per RFC 8707
            var resourceUri = $"{mcpServerUri.Scheme}://{mcpServerUri.Authority}";

            // Check if the existing token is about to expire (5-min buffer per ISE blog)
            var existingToken = this.Context.Request.Headers.Authorization?.Parameter;
            if (!string.IsNullOrEmpty(existingToken) && IsTokenExpiringSoon(existingToken))
            {
                // Token is about to expire — don't attempt OBO or exchange,
                // as it will likely fail mid-flow. Return null to surface diagnostics.
                return null;
            }

            // ── Option 1: Client credentials grant ──
            // Uncomment and configure if your MCP server supports machine-to-machine auth.
            // Replace CLIENT_ID and CLIENT_SECRET with your values or read from
            // connection parameters.
            //
            // var clientId = "YOUR_CLIENT_ID";
            // var clientSecret = "YOUR_CLIENT_SECRET";
            //
            // var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
            // var formData = new Dictionary<string, string>
            // {
            //     { "grant_type", "client_credentials" },
            //     { "client_id", clientId },
            //     { "client_secret", clientSecret },
            //     { "scope", requiredScopes },
            //     { "resource", resourceUri }  // RFC 8707
            // };
            // tokenRequest.Content = new FormUrlEncodedContent(formData);
            //
            // var tokenResponse = await this.Context.SendAsync(tokenRequest, this.CancellationToken);
            // if (tokenResponse.IsSuccessStatusCode)
            // {
            //     var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
            //     var tokenJson = JObject.Parse(tokenBody);
            //     return tokenJson.Value<string>("access_token");
            // }

            // ── Option 2: Azure AD On-Behalf-Of (OBO) flow ──
            // Exchanges the Power Platform user's token for one scoped to the MCP server.
            // Requires the MCP server's App Registration to expose API scopes
            // (format: api://{client-id}/{scope-name}) and your connector's
            // App Registration to have API permissions for those scopes.
            // See: https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow
            //
            // var clientId = "YOUR_CONNECTOR_CLIENT_ID";
            // var clientSecret = "YOUR_CONNECTOR_CLIENT_SECRET";
            // var tenantId = "YOUR_TENANT_ID";
            // var oboEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            //
            // var oboRequest = new HttpRequestMessage(HttpMethod.Post, oboEndpoint);
            // var oboForm = new Dictionary<string, string>
            // {
            //     { "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer" },
            //     { "client_id", clientId },
            //     { "client_secret", clientSecret },
            //     { "assertion", existingToken },
            //     { "scope", requiredScopes },
            //     { "requested_token_use", "on_behalf_of" }
            // };
            // oboRequest.Content = new FormUrlEncodedContent(oboForm);
            //
            // var oboResponse = await this.Context.SendAsync(oboRequest, this.CancellationToken);
            // if (oboResponse.IsSuccessStatusCode)
            // {
            //     var oboBody = await oboResponse.Content.ReadAsStringAsync();
            //     var oboJson = JObject.Parse(oboBody);
            //     return oboJson.Value<string>("access_token");
            // }

            // ── Option 3: Forward the existing Power Platform token ──
            // If the token is valid but missing the resource parameter,
            // some auth servers support token exchange (RFC 8693).
            // This is server-specific and uncommon.

            return null; // No auto-recovery available
        }
        catch
        {
            return null; // Silent fail — will return diagnostics instead
        }
    }

    /// <summary>
    /// Checks if a JWT token is expiring within the buffer window.
    /// Does NOT validate the signature — only reads the exp claim.
    /// Per ISE blog recommendation: use a 5-minute buffer to prevent
    /// race conditions where the token expires mid-OBO-exchange.
    /// </summary>
    private bool IsTokenExpiringSoon(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return true; // Not a valid JWT

            // Decode the payload (base64url)
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var payloadBytes = Convert.FromBase64String(payload);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            var claims = JObject.Parse(payloadJson);

            var exp = claims.Value<long?>("exp");
            if (!exp.HasValue) return false; // No exp claim — can't check

            var expTime = DateTimeOffset.FromUnixTimeSeconds(exp.Value);
            var remaining = expTime - DateTimeOffset.UtcNow;
            return remaining.TotalSeconds < TokenExpiryBufferSeconds;
        }
        catch
        {
            return false; // If we can't parse, don't block the flow
        }
    }

    /// <summary>
    /// Detects whether a scope string is in Azure AD/Entra ID format.
    /// Azure AD scopes use the pattern: api://{client-id}/{scope-name}
    /// </summary>
    private bool IsAzureAdScope(string scope)
    {
        if (string.IsNullOrEmpty(scope)) return false;
        return scope.Contains("api://") ||
               scope.Contains("login.microsoftonline.com") ||
               scope.EndsWith("/.default");
    }

    /// <summary>
    /// Detects whether the authorization server is Azure AD/Entra ID
    /// based on the issuer URL pattern.
    /// </summary>
    private bool IsAzureAdIssuer(string issuer)
    {
        if (string.IsNullOrEmpty(issuer)) return false;
        return issuer.Contains("login.microsoftonline.com") ||
               issuer.Contains("sts.windows.net") ||
               issuer.Contains("login.microsoft.com");
    }

    // ========================================================================
    // DIAGNOSTICS AND ERROR RESPONSE
    // ========================================================================

    /// <summary>
    /// Builds a diagnostic JSON object with all discovered authorization info.
    /// This helps developers configure their connector's OAuth correctly.
    /// </summary>
    private JObject BuildAuthDiagnostics(
        BearerChallenge challenge,
        JObject resourceMetadata,
        JObject authServerMetadata)
    {
        var diagnostics = new JObject
        {
            ["error"] = "mcp_authorization_required",
            ["message"] = "The MCP server requires OAuth 2.1 authorization. " +
                "Use the discovered endpoints below to configure your connector's " +
                "apiProperties.json OAuth settings."
        };

        // WWW-Authenticate details
        var challengeInfo = new JObject
        {
            ["scope"] = challenge.Scope ?? "(not specified)",
            ["realm"] = challenge.Realm ?? "(not specified)",
            ["error"] = challenge.Error ?? "(none)",
            ["error_description"] = challenge.ErrorDescription ?? "(none)",
            ["resource_metadata_url"] = challenge.ResourceMetadataUrl ?? "(not specified)"
        };
        diagnostics["www_authenticate"] = challengeInfo;

        // Protected Resource Metadata (RFC 9728)
        if (resourceMetadata != null)
        {
            var resourceInfo = new JObject
            {
                ["resource"] = resourceMetadata.Value<string>("resource") ?? "",
                ["authorization_servers"] = resourceMetadata["authorization_servers"],
                ["scopes_supported"] = resourceMetadata["scopes_supported"],
                ["bearer_methods_supported"] = resourceMetadata["bearer_methods_supported"]
            };
            diagnostics["protected_resource_metadata"] = resourceInfo;
        }

        // Authorization Server Metadata (RFC 8414 / OIDC)
        if (authServerMetadata != null)
        {
            var issuer = authServerMetadata.Value<string>("issuer") ?? "";
            var isAzureAd = IsAzureAdIssuer(issuer);

            var authInfo = new JObject
            {
                ["issuer"] = issuer,
                ["is_azure_ad"] = isAzureAd,
                ["authorization_endpoint"] = authServerMetadata.Value<string>("authorization_endpoint") ?? "",
                ["token_endpoint"] = authServerMetadata.Value<string>("token_endpoint") ?? "",
                ["scopes_supported"] = authServerMetadata["scopes_supported"],
                ["grant_types_supported"] = authServerMetadata["grant_types_supported"],
                ["code_challenge_methods_supported"] =
                    authServerMetadata["code_challenge_methods_supported"],
                ["client_id_metadata_document_supported"] =
                    authServerMetadata.Value<bool?>("client_id_metadata_document_supported"),
                ["registration_endpoint"] =
                    authServerMetadata.Value<string>("registration_endpoint") ?? ""
            };
            diagnostics["authorization_server_metadata"] = authInfo;

            // Determine the correct identity provider for apiProperties.json
            var identityProvider = isAzureAd ? "aad" : "oauth2generic";

            // Provide actionable configuration guidance
            var suggestedConfig = new JObject
            {
                ["identityProvider"] = identityProvider,
                ["authorizationUrlTemplate"] =
                    authServerMetadata.Value<string>("authorization_endpoint") ?? "",
                ["tokenUrlTemplate"] =
                    authServerMetadata.Value<string>("token_endpoint") ?? "",
                ["refreshUrlTemplate"] =
                    authServerMetadata.Value<string>("token_endpoint") ?? "",
                ["scopes"] = challenge.Scope
                    ?? string.Join(" ", (authServerMetadata["scopes_supported"] ?? new JArray())
                        .Select(s => s.ToString()))
            };

            // Azure AD-specific: extract tenant, resource URI, and app ID for apiProperties
            if (isAzureAd)
            {
                // Extract tenant ID from issuer URL
                // e.g., https://login.microsoftonline.com/{tenant-id}/v2.0
                var tenantMatch = Regex.Match(issuer,
                    @"login\.microsoftonline\.com/([^/]+)");
                if (tenantMatch.Success)
                {
                    suggestedConfig["tenantId"] = tenantMatch.Groups[1].Value;
                }

                // Extract resource URI from scopes (api://{client-id}/...)
                var scopes = challenge.Scope ?? "";
                var apiMatch = Regex.Match(scopes, @"api://([^/\s]+)");
                if (apiMatch.Success)
                {
                    suggestedConfig["AzureActiveDirectoryResourceId"] =
                        $"api://{apiMatch.Groups[1].Value}";
                    suggestedConfig["resourceUri"] =
                        $"api://{apiMatch.Groups[1].Value}";
                }

                suggestedConfig["note"] =
                    "For Azure AD: use identityProvider 'aad'. Verify the MCP server's " +
                    "App Registration has 'Expose an API' scopes configured. " +
                    "Pre-authorize your connector's client ID under 'Authorized client applications'. " +
                    "For OBO flows, grant API permissions in your connector's App Registration.";
            }

            // Copilot Studio-specific guidance
            suggestedConfig["copilot_studio_options"] = new JObject
            {
                ["dynamic_discovery"] = authServerMetadata["registration_endpoint"] != null
                    ? "Supported — use 'Dynamic discovery' OAuth type in the MCP onboarding wizard"
                    : "Not supported — use 'Manual' OAuth type and provide endpoints below",
                ["manual_config"] = new JObject
                {
                    ["authorization_url"] = authServerMetadata.Value<string>("authorization_endpoint") ?? "",
                    ["token_url"] = authServerMetadata.Value<string>("token_endpoint") ?? "",
                    ["refresh_url"] = authServerMetadata.Value<string>("token_endpoint") ?? "",
                    ["scopes"] = challenge.Scope ?? ""
                }
            };

            diagnostics["suggested_apiProperties_config"] = suggestedConfig;
        }

        return diagnostics;
    }

    /// <summary>
    /// Creates an error response with authorization diagnostics in the body.
    /// Preserves the original status code (401/403).
    /// </summary>
    private HttpResponseMessage CreateAuthErrorResponse(
        HttpResponseMessage originalResponse,
        JObject diagnostics)
    {
        var errorResponse = new HttpResponseMessage(originalResponse.StatusCode);
        errorResponse.Content = new StringContent(
            diagnostics.ToString(Newtonsoft.Json.Formatting.Indented),
            Encoding.UTF8,
            "application/json");

        // Copy relevant headers from original response
        foreach (var header in originalResponse.Headers)
        {
            if (header.Key.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase))
            {
                errorResponse.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return errorResponse;
    }

    // ========================================================================
    // HTTP HELPERS
    // ========================================================================

    /// <summary>
    /// Fetches a JSON document from a URL. Used for metadata discovery.
    /// Does NOT include authorization headers — metadata endpoints are public.
    /// </summary>
    private async Task<JObject> FetchJsonAsync(string url)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await this.Context.SendAsync(request, this.CancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            return JObject.Parse(body);
        }
        catch
        {
            return null; // Discovery endpoint not available
        }
    }

    /// <summary>
    /// Clones an HttpRequestMessage for retry. The original request's content
    /// stream may have been consumed, so we read and recreate it.
    /// </summary>
    private async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        // Copy headers
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content
        if (original.Content != null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // Copy content headers
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

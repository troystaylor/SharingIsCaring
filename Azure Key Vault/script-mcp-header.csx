using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// Key Vault header injection script for MCP connectors.
///
/// Problem: Copilot Studio MCP connectors require API key auth via custom header,
/// but enterprise customers can't put plaintext API keys in connections. Key Vault
/// environment variable references don't resolve in the MCP runtime path.
///
/// Solution: This script fetches the API key from Key Vault at runtime using a
/// service principal, injects it as the required header, and proxies the request
/// to the MCP server. The connector itself can use "No Authentication."
///
/// Flow:
///   Copilot Studio → Connector (no auth) → script.csx
///     → [fetches API key from Key Vault]
///     → [injects header]
///     → MCP server (with API key header)
///
/// Security: The service principal credentials are in the script source, visible
/// to environment admins and solution exporters. Scope the service principal to
/// only Key Vault Secrets User on the specific vault. For high-sensitivity keys,
/// use an Azure Function or APIM proxy with managed identity instead.
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // CONFIGURATION — Update these values
    // ========================================

    /// <summary>
    /// Key Vault URL (e.g., https://myvault.vault.azure.net).
    /// </summary>
    private const string VAULT_URL = "";

    /// <summary>
    /// Name of the secret in Key Vault that contains the API key.
    /// </summary>
    private const string SECRET_NAME = "";

    /// <summary>
    /// Header name the MCP server expects the API key in (e.g., "x-api-key", "Authorization").
    /// If the server expects "Authorization: Bearer {key}", set HEADER_PREFIX to "Bearer ".
    /// </summary>
    private const string HEADER_NAME = "x-api-key";

    /// <summary>
    /// Optional prefix prepended to the secret value in the header (e.g., "Bearer ", "Basic ").
    /// Leave empty if the API key is sent as-is.
    /// </summary>
    private const string HEADER_PREFIX = "";

    /// <summary>
    /// The MCP server base URL to proxy requests to.
    /// </summary>
    private const string MCP_SERVER_URL = "";

    /// <summary>Entra ID tenant ID for the service principal.</summary>
    private const string TENANT_ID = "";

    /// <summary>Service principal client ID.</summary>
    private const string CLIENT_ID = "";

    /// <summary>Service principal client secret.</summary>
    private const string CLIENT_SECRET = "";

    /// <summary>Key Vault REST API version.</summary>
    private const string KV_API_VERSION = "2025-07-01";

    // Cached values
    private string _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private string _cachedApiKey;
    private DateTime _apiKeyExpiry = DateTime.MinValue;

    // ========================================
    // ENTRY POINT
    // ========================================

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(VAULT_URL) || string.IsNullOrWhiteSpace(SECRET_NAME) ||
                string.IsNullOrWhiteSpace(MCP_SERVER_URL) || string.IsNullOrWhiteSpace(TENANT_ID) ||
                string.IsNullOrWhiteSpace(CLIENT_ID) || string.IsNullOrWhiteSpace(CLIENT_SECRET))
            {
                return CreateErrorResponse(
                    "Key Vault header injection not configured. Set all constants in script.csx.", 500);
            }

            // Step 1: Get the API key from Key Vault (cached)
            var apiKey = await GetApiKeyFromVault().ConfigureAwait(false);

            // Step 2: Read the original request body
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Step 3: Forward to MCP server with the API key header injected
            var mcpUrl = MCP_SERVER_URL.TrimEnd('/');
            var proxyRequest = new HttpRequestMessage(HttpMethod.Post, mcpUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            // Inject the API key header
            var headerValue = string.IsNullOrEmpty(HEADER_PREFIX) ? apiKey : HEADER_PREFIX + apiKey;
            proxyRequest.Headers.TryAddWithoutValidation(HEADER_NAME, headerValue);

            // Forward Accept header for MCP Streamable HTTP
            proxyRequest.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            proxyRequest.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            // Step 4: Send and return the response as-is
            var response = await this.Context.SendAsync(proxyRequest, this.CancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Key Vault header injection failed: {ex.Message}");
            return CreateErrorResponse(ex.Message, 500);
        }
    }

    // ========================================
    // KEY VAULT — Secret Retrieval
    // ========================================

    /// <summary>
    /// Fetch the API key from Key Vault. Cached for 5 minutes.
    /// </summary>
    private async Task<string> GetApiKeyFromVault()
    {
        if (_cachedApiKey != null && DateTime.UtcNow < _apiKeyExpiry)
            return _cachedApiKey;

        var vaultUrl = VAULT_URL.TrimEnd('/');
        var secretUrl = $"{vaultUrl}/secrets/{Uri.EscapeDataString(SECRET_NAME)}?api-version={KV_API_VERSION}";

        var token = await GetVaultTokenAsync().ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Get, secretUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to retrieve secret '{SECRET_NAME}' ({(int)response.StatusCode}): {content}");

        var secretData = JObject.Parse(content);
        _cachedApiKey = secretData.Value<string>("value");
        _apiKeyExpiry = DateTime.UtcNow.AddMinutes(5);

        if (string.IsNullOrWhiteSpace(_cachedApiKey))
            throw new Exception($"Secret '{SECRET_NAME}' exists but has an empty value.");

        return _cachedApiKey;
    }

    // ========================================
    // KEY VAULT — Token Acquisition
    // ========================================

    /// <summary>
    /// Acquire a bearer token for Key Vault using client credentials flow.
    /// Cached and refreshed 5 minutes before expiry.
    /// </summary>
    private async Task<string> GetVaultTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        var tokenUrl = $"https://login.microsoftonline.com/{TENANT_ID}/oauth2/v2.0/token";
        var tokenBody = new StringContent(
            $"grant_type=client_credentials&client_id={Uri.EscapeDataString(CLIENT_ID)}&client_secret={Uri.EscapeDataString(CLIENT_SECRET)}&scope={Uri.EscapeDataString("https://vault.azure.net/.default")}",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = tokenBody
        };

        var response = await this.Context.SendAsync(tokenRequest, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to acquire Key Vault token ({(int)response.StatusCode}): {content}");

        var tokenResponse = JObject.Parse(content);
        _cachedToken = tokenResponse.Value<string>("access_token");

        var expiresIn = tokenResponse.Value<int?>("expires_in") ?? 3600;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 300);

        return _cachedToken;
    }

    // ========================================
    // HELPERS
    // ========================================

    private HttpResponseMessage CreateErrorResponse(string message, int statusCode)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = message,
                ["statusCode"] = statusCode
            }
        };
        return new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(error.ToString(), Encoding.UTF8, "application/json")
        };
    }
}

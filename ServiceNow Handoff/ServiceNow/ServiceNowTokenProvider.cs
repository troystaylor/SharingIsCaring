using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceNowHandoff.ServiceNow;

    /// <summary>
    /// Manages OAuth 2.0 client_credentials tokens for ServiceNow API access.
    /// Caches tokens and refreshes 60 seconds before expiry to avoid failed requests.
    ///
    /// TODO: SERVICENOW OAUTH SETUP
    /// 1. In ServiceNow: System OAuth > Application Registry > Create an OAuth API endpoint
    ///    for external clients
    /// 2. Set the Client ID and Client Secret in appsettings.json:ServiceNow
    /// 3. Grant the OAuth app's user the following roles:
    ///    - itil (for interaction table access)
    ///    - sn_csm_ws.csm_ws_integration (if using CSM Chat APIs)
    ///    - rest_api_explorer (for testing)
    /// 4. Verify token endpoint works: POST https://YOUR-INSTANCE.service-now.com/oauth_token.do
    ///    with grant_type=client_credentials&amp;client_id=X&amp;client_secret=Y
    /// </summary>
public class ServiceNowTokenProvider
{
    private readonly IServiceNowConnectionSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ServiceNowTokenProvider> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public ServiceNowTokenProvider(
        IServiceNowConnectionSettings settings,
        IHttpClientFactory httpClientFactory,
        ILogger<ServiceNowTokenProvider> logger)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Return cached token if still valid (with 60s buffer)
        if (_cachedToken != null && DateTime.UtcNow.AddSeconds(60) < _tokenExpiry)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken != null && DateTime.UtcNow.AddSeconds(60) < _tokenExpiry)
            {
                return _cachedToken;
            }

            _logger.LogInformation("Requesting new OAuth token from ServiceNow");

            var client = _httpClientFactory.CreateClient("ServiceNow");
            var tokenUrl = $"{_settings.InstanceUrl.TrimEnd('/')}/oauth_token.do";

            var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var formData = new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret
            };

            // Use client_credentials for production; fall back to password grant for dev instances
            if (!string.IsNullOrEmpty(_settings.Username) && !string.IsNullOrEmpty(_settings.Password))
            {
                formData["grant_type"] = "password";
                formData["username"] = _settings.Username;
                formData["password"] = _settings.Password;
                _logger.LogInformation("Using password grant for ServiceNow OAuth");
            }
            else
            {
                formData["grant_type"] = "client_credentials";
                _logger.LogInformation("Using client_credentials grant for ServiceNow OAuth");
            }

            request.Content = new FormUrlEncodedContent(formData);

            var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("ServiceNow OAuth token response was empty");
            }

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _logger.LogInformation("ServiceNow OAuth token acquired, expires in {ExpiresIn}s", tokenResponse.ExpiresIn);

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Mongo.Copilot.Indexer.Graph;

public sealed class GraphOptions
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Leave empty to use managed identity, which is the deployed configuration.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// Checks the settings the credential chain needs before it is constructed. Azure.Identity
    /// otherwise throws "Invalid tenant id provided" during host startup, which does not point
    /// at the setting that is actually wrong.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (IsUnset(TenantId))
            errors.Add("Graph:TenantId is not configured (it still holds its placeholder value).");
        else if (!Guid.TryParse(TenantId, out _) && !TenantId.Contains('.', StringComparison.Ordinal))
            errors.Add($"Graph:TenantId '{TenantId}' is neither a GUID nor a domain name.");

        if (!IsUnset(ClientSecret) && IsUnset(ClientId))
            errors.Add("Graph:ClientSecret is set but Graph:ClientId is not.");

        return errors;
    }

    private static bool IsUnset(string value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith('{');
}

/// <summary>
/// Thin Microsoft Graph client over <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Deliberately not the Graph SDK: the external connectors surface used here is small, and
/// hand-rolling it keeps full control over retry-after handling and the exact item payload,
/// which matters when publishing tens of thousands of items.
/// </remarks>
public sealed class GraphClient
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly ILogger<GraphClient> _logger;
    private AccessToken _token;

    public GraphClient(HttpClient http, IOptions<GraphOptions> options, ILogger<GraphClient> logger)
    {
        var config = options.Value;
        _http = http;
        _http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        _logger = logger;

        _credential = string.IsNullOrWhiteSpace(config.ClientSecret)
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(config.ClientId) ? null : config.ClientId,
                TenantId = string.IsNullOrWhiteSpace(config.TenantId) ? null : config.TenantId
            })
            : new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        // Graph throttles aggressively during a bulk crawl; Retry-After is authoritative
        // and must be honoured rather than replaced with a fixed backoff.
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));

            if (body is not null)
                request.Content = JsonContent.Create(body, options: GraphJson.Options);

            var response = await _http.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return response;

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return response;

            var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout;

            if (!transient || attempt >= 5)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                throw new GraphException(
                    $"{method} {path} failed with {(int)response.StatusCode}: {Trim(detail)}");
            }

            var delay = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));

            _logger.LogWarning(
                "Graph returned {Status} for {Method} {Path}; retrying in {Delay}s (attempt {Attempt}).",
                (int)response.StatusCode, method, path, delay.TotalSeconds, attempt + 1);

            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, GraphJson.Options, cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        // Refresh a few minutes early so a long crawl never fails mid-batch on expiry.
        if (_token.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
            _token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);

        return _token.Token;
    }

    private static string Trim(string value) =>
        value.Length > 800 ? value[..800] + "…" : value;
}

public sealed class GraphException(string message) : Exception(message);

public static class GraphJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

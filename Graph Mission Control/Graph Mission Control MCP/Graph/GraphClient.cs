using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Identity.Web;

namespace GraphMissionControl.Mcp.Graph;

/// <summary>Microsoft Graph returned a non-success status.</summary>
public sealed class GraphRequestException(int status, string code, string message)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>
/// Calls Microsoft Graph as the signed-in user.
///
/// The inbound token is issued for this server, so it cannot be forwarded to Graph.
/// It is exchanged on-behalf-of instead, which keeps every call bounded by the calling
/// user's own permissions.
/// </summary>
public sealed class GraphClient(
    ITokenAcquisition tokenAcquisition,
    IHttpClientFactory httpClientFactory,
    ILogger<GraphClient> logger)
{
    private const string BaseUrl = "https://graph.microsoft.com/v1.0";
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    private const int MaxAttempts = 3;

    // Graph can ask for a wait far longer than the caller's own budget. Sleeping that long would
    // hold the request open only to time out anyway, so a longer Retry-After is surfaced as an
    // error the agent can act on rather than waited out.
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(8);

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
        => await SendAsync(HttpMethod.Get, path, body: null, ct);

    /// <summary>
    /// POST is used only for Graph operations that read — /search/query and the
    /// availability functions. Nothing here mutates.
    /// </summary>
    public async Task<JsonNode?> PostReadAsync(string path, JsonNode body, CancellationToken ct)
        => await SendAsync(HttpMethod.Post, path, body, ct);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var token = await tokenAcquisition.GetAccessTokenForUserAsync(Scopes);
        var url = $"{BaseUrl}/{path.TrimStart('/')}";
        var http = httpClientFactory.CreateClient();

        for (var attempt = 1; ; attempt++)
        {
            // A request message cannot be resent, so each attempt builds its own.
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Graph rejects $search and $count on directory objects without this.
            if (url.Contains("$search", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("$count", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
            }

            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);

            var status = (int)response.StatusCode;
            var delay = RetryDelayFor(response, status, attempt);

            if (delay is null || attempt >= MaxAttempts)
            {
                var (code, message) = ParseGraphError(payload, status);
                logger.LogWarning("graph {Method} {Path} failed status={Status} code={Code} attempts={Attempts}",
                    method.Method, path, status, code, attempt);
                throw new GraphRequestException(status, code, message);
            }

            logger.LogInformation("graph {Method} {Path} status={Status}, retrying in {Seconds}s ({Attempt}/{Max})",
                method.Method, path, status, delay.Value.TotalSeconds, attempt, MaxAttempts);
            await Task.Delay(delay.Value, ct);
        }
    }

    /// <summary>Null when the response should not be retried at all.</summary>
    private static TimeSpan? RetryDelayFor(HttpResponseMessage response, int status, int attempt)
    {
        if (status is not (429 or 503 or 504)) return null;

        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        // Graph's own figure wins when it is short enough to be worth waiting for.
        if (requested is { } wait)
            return wait < TimeSpan.Zero || wait > MaxRetryDelay ? null : wait;

        return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
    }

    private static (string Code, string Message) ParseGraphError(string payload, int status)
    {
        try
        {
            var err = JsonNode.Parse(payload)?["error"];
            var code = err?["code"]?.GetValue<string>();
            var message = err?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message))
                return (code ?? $"HTTP{status}", message);
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through to the raw payload.
        }

        return ($"HTTP{status}", string.IsNullOrWhiteSpace(payload) ? $"Graph returned {status}." : payload);
    }
}

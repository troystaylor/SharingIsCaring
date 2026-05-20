using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SlackCoworkMcp.Auth;

namespace SlackCoworkMcp.Slack;

public interface ISlackClient
{
    Task<JsonObject> GetAsync(string method, IDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<JsonObject> PostJsonAsync(string method, JsonObject body, CancellationToken ct = default);
    Task<JsonObject> PostFormAsync(string method, IDictionary<string, string?> form, CancellationToken ct = default);
    Task<HttpResponseMessage> PutRawAsync(string absoluteUrl, byte[] body, string? contentType, CancellationToken ct = default);
}

/// <summary>
/// Typed HttpClient for the Slack Web API. Handles bearer-token forwarding,
/// 429 retry with <c>Retry-After</c>, and Slack's <c>ok=false</c> error envelope.
/// </summary>
public sealed class SlackClient : ISlackClient
{
    private readonly HttpClient _http;
    private readonly IBearerTokenAccessor _token;
    private readonly ILogger<SlackClient> _log;

    private const int MaxRateLimitRetries = 3;

    public SlackClient(HttpClient http, IBearerTokenAccessor token, ILogger<SlackClient> log)
    {
        _http = http;
        _token = token;
        _log = log;
    }

    public async Task<JsonObject> GetAsync(string method, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var url = BuildUrl(method, query);
        return await SendAsync(HttpMethod.Get, url, content: null, isJson: false, ct);
    }

    public async Task<JsonObject> PostJsonAsync(string method, JsonObject body, CancellationToken ct = default)
    {
        var json = body.ToJsonString();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, method, content, isJson: true, ct);
    }

    public async Task<JsonObject> PostFormAsync(string method, IDictionary<string, string?> form, CancellationToken ct = default)
    {
        var pairs = form
            .Where(kv => kv.Value is not null)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!));
        using var content = new FormUrlEncodedContent(pairs);
        return await SendAsync(HttpMethod.Post, method, content, isJson: false, ct);
    }

    public async Task<HttpResponseMessage> PutRawAsync(string absoluteUrl, byte[] body, string? contentType, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, absoluteUrl);
        var bc = new ByteArrayContent(body);
        if (!string.IsNullOrEmpty(contentType))
            bc.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Content = bc;
        // No bearer for the externally-issued upload URL
        return await _http.SendAsync(req, ct);
    }

    private async Task<JsonObject> SendAsync(HttpMethod verb, string urlOrMethod, HttpContent? content, bool isJson, CancellationToken ct)
    {
        var bearer = _token.Require();

        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(verb, urlOrMethod);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (content is not null)
            {
                req.Content = content;
                // For json calls Slack requires Content-Type with charset
            }

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "slack transport error calling {Url}", urlOrMethod);
                throw;
            }

            if ((int)resp.StatusCode == 429 && attempt < MaxRateLimitRetries)
            {
                var retryAfter = resp.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromSeconds(resp.Headers.RetryAfter?.Date is { } d
                        ? Math.Max(1, (d - DateTimeOffset.UtcNow).TotalSeconds)
                        : 1.0 * Math.Pow(2, attempt));
                _log.LogInformation("slack 429 from {Url}, sleeping {Sec}s (attempt {N})",
                    urlOrMethod, retryAfter.TotalSeconds, attempt + 1);
                resp.Dispose();
                content = await CloneContentAsync(content);
                await Task.Delay(retryAfter, ct);
                continue;
            }

            using (resp)
            {
                var bodyStr = await resp.Content.ReadAsStringAsync(ct);

                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new UnauthorizedSlackException($"slack rejected token at {urlOrMethod}: {bodyStr}");
                }

                if (!resp.IsSuccessStatusCode && string.IsNullOrEmpty(bodyStr))
                {
                    throw new SlackApiException(urlOrMethod, $"http_{(int)resp.StatusCode}", null);
                }

                JsonNode? parsed;
                try { parsed = JsonNode.Parse(bodyStr); }
                catch (JsonException)
                {
                    throw new SlackApiException(urlOrMethod, "invalid_json_response",
                        new JsonObject { ["raw"] = bodyStr });
                }

                if (parsed is not JsonObject obj)
                {
                    throw new SlackApiException(urlOrMethod, "unexpected_response_shape", parsed);
                }

                var ok = obj["ok"]?.GetValue<bool>() ?? false;
                if (!ok)
                {
                    var err = obj["error"]?.GetValue<string>() ?? "unknown_error";
                    if (err == "ratelimited" && attempt < MaxRateLimitRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
                        content = await CloneContentAsync(content);
                        continue;
                    }
                    throw new SlackApiException(urlOrMethod, err, obj);
                }

                return obj;
            }
        }
    }

    private static async Task<HttpContent?> CloneContentAsync(HttpContent? c)
    {
        if (c is null) return null;
        var bytes = await c.ReadAsByteArrayAsync();
        var mediaType = c.Headers.ContentType?.MediaType ?? "application/x-www-form-urlencoded";
        var charset = c.Headers.ContentType?.CharSet;
        var clone = new ByteArrayContent(bytes);
        clone.Headers.ContentType = new MediaTypeHeaderValue(mediaType)
        {
            CharSet = charset,
        };
        return clone;
    }

    private static string BuildUrl(string method, IDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0) return method;
        var sb = new StringBuilder(method);
        sb.Append('?');
        var first = true;
        foreach (var kv in query)
        {
            if (kv.Value is null) continue;
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value));
            first = false;
        }
        return sb.ToString();
    }
}

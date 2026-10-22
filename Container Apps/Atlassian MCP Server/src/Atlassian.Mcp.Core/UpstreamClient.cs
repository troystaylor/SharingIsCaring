using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace Atlassian.Mcp.Core;

/// <summary>
/// The only kind of class that sends requests to a provider. Tools build a path; this adds the caller's own
/// access token and turns provider errors into messages a model can act on.
/// </summary>
/// <remarks>
/// Paths are relative to the HTTP client's base address. Nothing a model supplies ever becomes a host: tools
/// validate identifiers, then place them into a fixed path template.
/// </remarks>
public abstract class UpstreamClient(HttpClient http, ICaller caller, string product)
{
    private const int MaxErrorChars = 600;

    protected string Product => product;

    protected ICaller Caller => caller;

    protected async Task<JsonNode?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        var text = await SendAsync(HttpMethod.Get, path, null, "application/json", cancellationToken);
        return Parse(text);
    }

    protected async Task<JsonNode?> SendJsonAsync(
        HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var text = await SendAsync(method, path, body, "application/json", cancellationToken);
        return Parse(text);
    }

    protected async Task<string> GetTextAsync(string path, string accept, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Get, path, null, accept, cancellationToken) ?? string.Empty;

    /// <summary>Builds <c>path?a=1&amp;b=2</c>, leaving out null or empty values and escaping the rest.</summary>
    public static string WithQuery(string path, params (string Key, string? Value)[] query)
    {
        var parts = query
            .Where(q => !string.IsNullOrEmpty(q.Value))
            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value!)}")
            .ToArray();

        return parts.Length == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    private JsonNode? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new McpException($"{product} returned a response that was not valid JSON.");
        }
    }

    private async Task<string?> SendAsync(
        HttpMethod method, string path, JsonNode? body, string accept, CancellationToken cancellationToken)
    {
        var token = caller.AccessToken
            ?? throw new McpException(
                $"This request carried no {product} sign-in. Sign in to {product} for this connection and try again.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd(accept);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new McpException($"Could not reach {product} ({ex.GetType().Name}). Try again shortly.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Atlassian answers a missing scope with 401, not 403. Telling the two apart saves a pointless
                // re-sign-in: signing in again cannot add a scope the OAuth app does not request.
                var rejected = await response.Content.ReadAsStringAsync(cancellationToken);
                if (rejected.Contains("scope", StringComparison.OrdinalIgnoreCase))
                {
                    throw new McpException(
                        Describe(product, response.StatusCode, rejected)
                        + " The sign-in does not include a scope this action needs. Add the scope to your OAuth app, then sign in again.");
                }

                throw new McpException(
                    $"{product} rejected this sign-in. It has expired or been revoked. Sign in to {product} again and retry.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                throw new McpException(
                    $"{product} rate limit reached." + (wait is null ? string.Empty : $" Retry after {wait:0} seconds.")
                    + " Narrow the request or wait before trying again.");
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new McpException(Describe(product, response.StatusCode, text));
            }

            return text;
        }
    }

    /// <summary>
    /// Pulls the useful sentence out of the error shapes Atlassian and Bitbucket use: Jira's
    /// <c>errorMessages</c> and <c>errors</c>, Confluence's <c>errors[].title</c>, a plain <c>message</c>, and
    /// Bitbucket's <c>error.message</c>. The message usually says what was wrong with the query or field.
    /// </summary>
    internal static string Describe(string product, HttpStatusCode status, string body)
    {
        var messages = new List<string>();

        try
        {
            var node = JsonNode.Parse(body);
            foreach (var m in Shape.Items(node, "errorMessages"))
            {
                Add(messages, m.ToString());
            }

            if (Shape.At(node, "errors") is JsonObject fields)
            {
                foreach (var (name, value) in fields)
                {
                    Add(messages, $"{name}: {value}");
                }
            }
            else
            {
                foreach (var e in Shape.Items(node, "errors"))
                {
                    Add(messages, Shape.Str(e, "detail") ?? Shape.Str(e, "title") ?? e.ToString());
                }
            }

            Add(messages, Shape.Str(node, "message"));
            Add(messages, Shape.Str(node, "error", "message"));
            Add(messages, Shape.Str(node, "error", "detail"));

            // Bitbucket names the scopes a refused call needed.
            var required = Shape.Items(node, "error", "detail", "required").Select(r => r.ToString()).ToArray();
            if (required.Length > 0)
            {
                Add(messages, "Required scopes: " + string.Join(", ", required) + ".");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON. Fall through to the status-only message.
        }

        var detail = messages.Count == 0
            ? $"HTTP {(int)status}"
            : $"HTTP {(int)status}: {string.Join(' ', messages.Distinct())}";

        if (detail.Length > MaxErrorChars)
        {
            detail = detail[..MaxErrorChars] + "...";
        }

        var hint = status switch
        {
            HttpStatusCode.NotFound => " It does not exist, or the signed-in user cannot see it.",
            HttpStatusCode.Forbidden => " The signed-in user or the granted scopes do not allow this.",
            HttpStatusCode.Gone => " The endpoint has been retired; the server needs updating.",
            HttpStatusCode.Conflict => " Someone else changed it first. Read it again and retry with the latest version.",
            _ => string.Empty
        };

        return $"{product} returned {detail}.{hint}";
    }

    private static void Add(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message.Trim());
        }
    }
}

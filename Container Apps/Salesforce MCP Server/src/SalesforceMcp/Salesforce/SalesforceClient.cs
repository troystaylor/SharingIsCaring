using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using SalesforceMcp.Configuration;

namespace SalesforceMcp.Salesforce;

/// <summary>The outcome of a conditional GET. <see cref="Text"/> is null when <see cref="NotModified"/> is true.</summary>
public sealed record ConditionalResponse(string? Text, bool NotModified, DateTimeOffset? LastModified);

/// <summary>
/// The only class that sends requests to the Salesforce REST API. Tools build a path; this adds the caller's
/// own access token and turns Salesforce errors into messages a model can act on.
/// </summary>
public sealed class SalesforceClient(
    HttpClient http,
    ISalesforceUser user,
    IOptions<SalesforceOptions> options)
{
    public const string HttpClientName = "salesforce-api";

    private const int MaxErrorChars = 600;

    public SalesforceOptions Settings => options.Value;

    /// <summary>The person this request is running as.</summary>
    public ISalesforceUser User => user;

    /// <summary>
    /// Builds <c>/services/data/{version}/{resource}</c>. <paramref name="version"/> is a value already
    /// resolved by <see cref="SalesforceVersions"/>; null means the server default.
    /// </summary>
    public string DataPath(string resource, string? version = null) =>
        $"/services/data/{version ?? Settings.ApiVersion}/{resource}";

    public Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, cancellationToken);

    public async Task<JsonNode?> SendAsync(
        HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var raw = await SendCoreAsync(method, path, body, null, cancellationToken);
        return ParseJson(raw.Text);
    }

    /// <summary>
    /// GET that sends <c>If-Modified-Since</c> when given a date. A <c>304</c> comes back as
    /// <see cref="ConditionalResponse.NotModified"/> instead of an error, and the body is returned as text so
    /// callers can cache it without sharing a mutable tree.
    /// </summary>
    public async Task<ConditionalResponse> GetConditionalAsync(
        string path, DateTimeOffset? ifModifiedSince, CancellationToken cancellationToken)
    {
        var raw = await SendCoreAsync(HttpMethod.Get, path, null, ifModifiedSince, cancellationToken);
        return new ConditionalResponse(raw.Text, raw.NotModified, raw.LastModified);
    }

    private static JsonNode? ParseJson(string? text)
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
            throw new McpException("Salesforce returned a response that was not valid JSON.");
        }
    }

    private sealed record RawResponse(string? Text, bool NotModified, DateTimeOffset? LastModified);

    private async Task<RawResponse> SendCoreAsync(
        HttpMethod method, string path, JsonNode? body, DateTimeOffset? ifModifiedSince,
        CancellationToken cancellationToken)
    {
        var token = user.AccessToken
            ?? throw new McpException(
                "This request carried no Salesforce sign-in. Sign in to Salesforce for this connection and try again.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.IfModifiedSince = ifModifiedSince;
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
            throw new McpException($"Could not reach Salesforce ({ex.GetType().Name}). Try again shortly.");
        }

        using (response)
        {
            // The token is the caller's own, so there is nothing to swap in. It has expired or been revoked.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new McpException(
                    "Salesforce rejected this sign-in. It has expired or been revoked. Sign in to Salesforce again and retry.");
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new RawResponse(null, true, null);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new McpException(Describe(response.StatusCode, text));
            }

            return new RawResponse(text, false, response.Content.Headers.LastModified);
        }
    }

    /// <summary>
    /// Salesforce reports errors as <c>[{"message":"...","errorCode":"..."}]</c>. The message usually says
    /// what was wrong with the query or field, which is exactly what the model needs to correct itself.
    /// </summary>
    internal static string Describe(HttpStatusCode status, string body)
    {
        string? code = null;
        string? message = null;

        try
        {
            var node = JsonNode.Parse(body);
            var first = node is JsonArray { Count: > 0 } array ? array[0] : node;
            code = (string?)first?["errorCode"];
            message = (string?)first?["message"];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON. Fall through to the status-only message.
        }

        var detail = (code, message) switch
        {
            (null, null) => $"HTTP {(int)status}",
            (_, null) => $"HTTP {(int)status} {code}",
            (null, _) => $"HTTP {(int)status}: {message}",
            _ => $"HTTP {(int)status} {code}: {message}"
        };

        if (detail.Length > MaxErrorChars)
        {
            detail = detail[..MaxErrorChars] + "...";
        }

        var hint = status switch
        {
            HttpStatusCode.NotFound => " The record or resource does not exist, or the signed-in user cannot see it.",
            HttpStatusCode.Forbidden => " The signed-in user lacks permission, or an API limit was reached.",
            _ => string.Empty
        };

        return $"Salesforce returned {detail}.{hint}";
    }
}

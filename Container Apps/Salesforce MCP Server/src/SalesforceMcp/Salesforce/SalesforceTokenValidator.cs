using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SalesforceMcp.Configuration;

namespace SalesforceMcp.Salesforce;

public sealed record SalesforceUserInfo(
    string UserId, string OrganizationId, string? Username, string? DisplayName, string? Email);

/// <summary>Salesforce could not be reached, so the token could be neither accepted nor rejected.</summary>
public sealed class SalesforceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Asks Salesforce whether an access token is good, and who it belongs to, using its <c>userinfo</c>
/// endpoint. Results are remembered briefly so a burst of tool calls costs one check.
/// </summary>
/// <remarks>
/// A singleton. The cache holds a hash of each token, never the token. A rejection is remembered for only a
/// few seconds, long enough to blunt a flood of bad tokens without locking out a client that has just signed
/// in again. The server never stores a token it will use later: tool calls read it from the request.
/// </remarks>
public sealed class SalesforceTokenValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    private const int MaxEntries = 5000;
    private static readonly TimeSpan RejectedFor = TimeSpan.FromSeconds(10);

    private sealed record Entry(SalesforceUserInfo? Info, DateTimeOffset Expires);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public int CachedCount => _entries.Count;

    /// <summary>Returns the token's owner, or null if Salesforce rejects the token.</summary>
    /// <exception cref="SalesforceUnavailableException">Salesforce could not be reached.</exception>
    public async Task<SalesforceUserInfo?> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        var now = time.GetUtcNow();

        if (_entries.TryGetValue(key, out var hit) && hit.Expires > now)
        {
            return hit.Info;
        }

        var info = await FetchAsync(accessToken, cancellationToken);
        var lifetime = info is null ? RejectedFor : TimeSpan.FromSeconds(options.Value.ValidationCacheSeconds);

        if (lifetime > TimeSpan.Zero)
        {
            Remember(key, new Entry(info, now + lifetime), now);
        }

        return info;
    }

    private void Remember(string key, Entry entry, DateTimeOffset now)
    {
        if (_entries.Count >= MaxEntries)
        {
            foreach (var stale in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList())
            {
                _entries.TryRemove(stale, out _);
            }

            // Still full of live entries. Starting over is safe: the next call just asks Salesforce again.
            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }
        }

        _entries[key] = entry;
    }

    private async Task<SalesforceUserInfo?> FetchAsync(string accessToken, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(SalesforceHttp.PublicClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, "services/oauth2/userinfo");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SalesforceUnavailableException("Salesforce could not be reached to check the sign-in.", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                throw new SalesforceUnavailableException(
                    $"Salesforce returned HTTP {(int)response.StatusCode} while checking the sign-in.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;

                var userId = Text(root, "user_id");
                var organizationId = Text(root, "organization_id");

                return userId is null || organizationId is null
                    ? null
                    : new SalesforceUserInfo(
                        userId, organizationId, Text(root, "preferred_username"), Text(root, "name"), Text(root, "email"));
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlassian.Mcp.Core;

/// <summary>Who a checked token belongs to. Every field except the token's validity is optional.</summary>
public sealed record CallerInfo(
    string? AccountId, string? DisplayName, string? Email, string? CloudId = null, string? Scopes = null);

/// <summary>The provider could not be reached, so the token could be neither accepted nor rejected.</summary>
public sealed class TokenCheckUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public static class TokenSchemes
{
    /// <summary>Selects the Atlassian or Bitbucket scheme from the endpoint being called.</summary>
    public const string ByEndpoint = "TokenByEndpoint";
    public const string Atlassian = "AtlassianToken";
    public const string Bitbucket = "BitbucketToken";
}

/// <summary>
/// Asks a provider whether an access token is good, and who it belongs to. Results are remembered briefly so
/// a burst of tool calls costs one check.
/// </summary>
/// <remarks>
/// A singleton. The cache holds a hash of each token, never the token. A rejection is remembered for only a
/// few seconds, long enough to blunt a flood of bad tokens without locking out a client that has just signed
/// in again. The server never stores a token it will use later: tool calls read it from the request.
/// </remarks>
public abstract class CachingTokenValidator(IOptions<AuthOptions> options, TimeProvider time)
{
    private const int MaxEntries = 5000;
    private static readonly TimeSpan RejectedFor = TimeSpan.FromSeconds(10);

    private sealed record Entry(CallerInfo? Info, DateTimeOffset Expires);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public int CachedCount => _entries.Count;

    /// <summary>Returns the token's owner, or null if the provider rejects the token.</summary>
    /// <exception cref="TokenCheckUnavailableException">The provider could not be reached.</exception>
    public async Task<CallerInfo?> ValidateAsync(string accessToken, CancellationToken cancellationToken)
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

    protected abstract Task<CallerInfo?> FetchAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// GETs <paramref name="path"/> with the token. A 5xx or a network failure means the provider is down, and
    /// is reported as unavailable rather than as a rejected token.
    /// </summary>
    protected static async Task<(HttpStatusCode Status, JsonNode? Body)> GetAsync(
        HttpClient http, string path, string accessToken, string provider, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TokenCheckUnavailableException($"{provider} could not be reached to check the sign-in.", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                throw new TokenCheckUnavailableException(
                    $"{provider} returned HTTP {(int)response.StatusCode} while checking the sign-in.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (response.StatusCode, null);
            }

            try
            {
                return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)));
            }
            catch (JsonException)
            {
                return (response.StatusCode, null);
            }
        }
    }

    private void Remember(string key, Entry entry, DateTimeOffset now)
    {
        if (_entries.Count >= MaxEntries)
        {
            foreach (var stale in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList())
            {
                _entries.TryRemove(stale, out _);
            }

            // Still full of live entries. Starting over is safe: the next call just asks the provider again.
            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }
        }

        _entries[key] = entry;
    }
}

/// <summary>
/// Checks an Atlassian 3LO token. <c>accessible-resources</c> needs no scope beyond the token being valid, so it
/// is the check, and it also says which sites the token reaches. <c>me</c> adds the account identity when the
/// token holds <c>read:me</c>, and is skipped quietly when it does not.
/// </summary>
public sealed class AtlassianTokenValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<AtlassianOptions> atlassian,
    IOptions<AuthOptions> auth,
    TimeProvider time)
    : CachingTokenValidator(auth, time)
{
    protected override async Task<CallerInfo?> FetchAsync(string accessToken, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpNames.Atlassian);

        var (status, sites) = await GetAsync(
            http, "oauth/token/accessible-resources", accessToken, "Atlassian", cancellationToken);

        if (status != HttpStatusCode.OK || sites is not JsonArray list)
        {
            return null;
        }

        string? cloudId = null;
        string? scopes = null;
        foreach (var site in list)
        {
            if (Shape.Str(site, "url") is { } url
                && string.Equals(url.TrimEnd('/'), atlassian.Value.Site, StringComparison.OrdinalIgnoreCase))
            {
                cloudId = Shape.Str(site, "id");
                scopes = site?["scopes"] is JsonArray granted
                    ? string.Join(' ', granted.Select(s => s?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)))
                    : null;
                break;
            }
        }

        var (meStatus, me) = await GetAsync(http, "me", accessToken, "Atlassian", cancellationToken);
        return meStatus == HttpStatusCode.OK
            ? new CallerInfo(Shape.Str(me, "account_id"), Shape.Str(me, "name"), Shape.Str(me, "email"), cloudId, scopes)
            : new CallerInfo(null, null, null, cloudId, scopes);
    }
}

/// <summary>Checks a Bitbucket Cloud OAuth token with the signed-in user's profile.</summary>
public sealed class BitbucketTokenValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<AuthOptions> auth,
    TimeProvider time)
    : CachingTokenValidator(auth, time)
{
    protected override async Task<CallerInfo?> FetchAsync(string accessToken, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpNames.Bitbucket);
        var (status, user) = await GetAsync(http, "user", accessToken, "Bitbucket", cancellationToken);

        // A token without the account scope is still a valid token; it just cannot say who it is.
        return status switch
        {
            HttpStatusCode.OK => new CallerInfo(
                Shape.Str(user, "account_id") ?? Shape.Str(user, "uuid"), Shape.Str(user, "display_name"), null),
            HttpStatusCode.Forbidden => new CallerInfo(null, null, null),
            _ => null
        };
    }
}

/// <summary>
/// Authenticates a request by checking its bearer token with the provider. A request with no token yields no
/// result, so the challenge answers it with a 401 that points the client at how to sign in.
/// </summary>
public abstract class TokenHandlerBase(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CachingTokenValidator validator,
    string provider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Bearer.Parse(Request.Headers.Authorization.ToString());
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        CallerInfo? info;
        try
        {
            info = await validator.ValidateAsync(token, Context.RequestAborted);
        }
        catch (TokenCheckUnavailableException ex)
        {
            Logger.LogWarning("{Reason}", ex.Message);
            return AuthenticateResult.Fail(ex.Message);
        }

        if (info is null)
        {
            return AuthenticateResult.Fail($"The {provider} access token was not accepted.");
        }

        var claims = new List<Claim>();
        AddIfPresent(claims, CallerClaims.AccountId, info.AccountId);
        AddIfPresent(claims, CallerClaims.Name, info.DisplayName);
        AddIfPresent(claims, CallerClaims.Email, info.Email);
        AddIfPresent(claims, CallerClaims.CloudId, info.CloudId);
        AddIfPresent(claims, CallerClaims.Scopes, info.Scopes);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, CallerClaims.Name, ClaimTypes.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(type, value));
        }
    }
}

public sealed class AtlassianTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AtlassianTokenValidator validator)
    : TokenHandlerBase(options, logger, encoder, validator, "Atlassian");

public sealed class BitbucketTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BitbucketTokenValidator validator)
    : TokenHandlerBase(options, logger, encoder, validator, "Bitbucket");

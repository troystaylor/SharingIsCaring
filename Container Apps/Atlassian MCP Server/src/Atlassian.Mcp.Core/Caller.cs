using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Atlassian.Mcp.Core;

/// <summary>Names of the HTTP clients used to reach each provider.</summary>
public static class HttpNames
{
    public const string Atlassian = "atlassian";
    public const string Bitbucket = "bitbucket";
}

/// <summary>Claim types the token handlers write and the callers read.</summary>
public static class CallerClaims
{
    public const string AccountId = "account_id";
    public const string Name = "name";
    public const string Email = "email";
    public const string CloudId = "cloud_id";
    public const string Scopes = "scopes";
}

/// <summary>The person making the current request, and the token they signed in with.</summary>
public interface ICaller
{
    /// <summary>The caller's access token, or null when the request carried none.</summary>
    string? AccessToken { get; }

    /// <summary>The provider's account ID, known once the token has been checked.</summary>
    string? AccountId { get; }

    string? DisplayName { get; }

    /// <summary>The cloud ID of the configured Atlassian site, when the token has access to it.</summary>
    string? CloudId { get; }

    /// <summary>The scopes the token holds on the configured site, space separated.</summary>
    string? Scopes { get; }

    /// <summary>A key unique to this caller, for anything that must never be shared between people.</summary>
    string CacheKey { get; }
}

/// <summary>Reads the signed-in person from the current HTTP request.</summary>
public sealed class HttpContextCaller(IHttpContextAccessor accessor) : ICaller
{
    public string? AccessToken => Bearer.Parse(accessor.HttpContext?.Request.Headers.Authorization.ToString());

    public string? AccountId => Claim(CallerClaims.AccountId);

    public string? DisplayName => Claim(CallerClaims.Name);

    public string? CloudId => Claim(CallerClaims.CloudId);

    public string? Scopes => Claim(CallerClaims.Scopes);

    public string CacheKey => Bearer.CacheKeyFor(AccountId, AccessToken);

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirstValue(type);
}

public static class Bearer
{
    /// <summary>Returns the token from an <c>Authorization: Bearer</c> header, or null.</summary>
    public static string? Parse(string? header)
    {
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// The account ID once the token is checked. Without one (sign-in checking off, or an identity the provider
    /// would not reveal) the key is a hash of the token, which still keeps one caller away from another.
    /// </summary>
    public static string CacheKeyFor(string? accountId, string? accessToken)
    {
        if (!string.IsNullOrEmpty(accountId))
        {
            return accountId;
        }

        return accessToken is null
            ? "anonymous"
            : "token-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)))[..16];
    }
}

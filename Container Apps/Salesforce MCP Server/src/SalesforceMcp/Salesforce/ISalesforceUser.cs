using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SalesforceMcp.Salesforce;

/// <summary>Names of the HTTP clients used to reach Salesforce.</summary>
public static class SalesforceHttp
{
    /// <summary>Calls that carry no caller token: the sign-in check and the health probe.</summary>
    public const string PublicClientName = "salesforce-public";
}

/// <summary>The person making the current request, and the Salesforce token they signed in with.</summary>
public interface ISalesforceUser
{
    /// <summary>The caller's Salesforce access token, or null when the request carried none.</summary>
    string? AccessToken { get; }

    /// <summary>The caller's Salesforce user ID, known once the token has been checked.</summary>
    string? UserId { get; }

    string? OrganizationId { get; }

    string? Username { get; }

    string? DisplayName { get; }

    /// <summary>
    /// A key that is unique to this caller, for anything that must never be shared between people. Object
    /// descriptions are the example: field-level security means two users can see different fields.
    /// </summary>
    string CacheKey { get; }
}

/// <summary>Reads the signed-in user from the current HTTP request.</summary>
public sealed class HttpContextSalesforceUser(IHttpContextAccessor accessor) : ISalesforceUser
{
    public string? AccessToken => BearerToken(accessor.HttpContext?.Request.Headers.Authorization.ToString());

    public string? UserId => Claim("user_id");

    public string? OrganizationId => Claim("organization_id");

    public string? Username => Claim("preferred_username");

    public string? DisplayName => Claim("name");

    public string CacheKey => CacheKeyFor(UserId, AccessToken);

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirstValue(type);

    /// <summary>Returns the token from an <c>Authorization: Bearer ...</c> header, or null.</summary>
    public static string? BearerToken(string? header)
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
    /// The user ID when the token has been checked. With sign-in checking off there is none, so the key is a
    /// hash of the token instead, which still keeps one caller's data away from another's.
    /// </summary>
    public static string CacheKeyFor(string? userId, string? accessToken)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        return accessToken is null
            ? "anonymous"
            : "token-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)))[..16];
    }
}

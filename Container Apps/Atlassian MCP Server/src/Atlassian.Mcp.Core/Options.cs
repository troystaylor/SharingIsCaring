namespace Atlassian.Mcp.Core;

/// <summary>
/// Inbound authentication. Callers sign in to Atlassian (Jira, Confluence) or Bitbucket themselves and send
/// their own access token. The server checks the token with the provider, then uses that same token for every
/// upstream call, so each request runs with the signed-in person's own permissions.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string PolicyName = "McpAccess";

    /// <summary>
    /// Leave true everywhere except local development. When false, tokens are not checked at the door.
    /// The provider still checks every token on every call, so this only affects who can list the tools.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a token the provider accepted is trusted without asking again. Revoking a token takes effect
    /// for the next call after this long. Rejected tokens are remembered for a few seconds only.
    /// </summary>
    public int ValidationCacheSeconds { get; set; } = 60;

    public void Validate()
    {
        if (ValidationCacheSeconds is < 0 or > 600)
        {
            throw new InvalidOperationException("Auth:ValidationCacheSeconds must be between 0 and 600.");
        }
    }
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public const string PolicyName = "mcp";

    /// <summary>Requests per caller per minute. 0 disables rate limiting.</summary>
    public int PermitsPerMinute { get; set; } = 120;
}

/// <summary>Caps that keep a response small enough for a model to use.</summary>
public sealed class LimitOptions
{
    public const string SectionName = "Limits";

    /// <summary>Most items one list call returns.</summary>
    public int MaxItems { get; set; } = 50;

    /// <summary>Most characters of items one list call returns.</summary>
    public int MaxResponseChars { get; set; } = 100_000;

    /// <summary>Most characters of one page, issue body, comment, or file returned as text.</summary>
    public int MaxTextChars { get; set; } = 30_000;

    public void Validate()
    {
        if (MaxItems is < 1 or > 200)
        {
            throw new InvalidOperationException("Limits:MaxItems must be between 1 and 200.");
        }

        if (MaxResponseChars < 1_000 || MaxTextChars < 1_000)
        {
            throw new InvalidOperationException("Limits:MaxResponseChars and Limits:MaxTextChars must be at least 1000.");
        }
    }
}

/// <summary>The Atlassian Cloud site that Jira and Confluence calls go to.</summary>
public sealed class AtlassianOptions
{
    public const string SectionName = "Atlassian";

    /// <summary>
    /// Your site, for example <c>https://acme.atlassian.net</c>. Tokens are used against this site only. An
    /// Atlassian token can cover several sites, so the server picks this one and refuses a token that has no
    /// access to it.
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional cloud ID of <see cref="SiteUrl"/>, used when the sign-in check has not supplied one, which is
    /// the case in local development with <c>Auth:Enabled</c> off.
    /// </summary>
    public string? CloudId { get; set; }

    /// <summary>The Atlassian API gateway. Only tests change this.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.atlassian.com/";

    public string Site => SiteUrl.TrimEnd('/');

    public void Validate()
    {
        if (!Uri.TryCreate(SiteUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Atlassian:SiteUrl must be an https site URL with no path, for example https://acme.atlassian.net.");
        }
    }
}

public abstract class ProductOptions
{
    /// <summary>Serves this product's MCP endpoint.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Registers this product's tools that create or change data. Off by default, so a client cannot see or
    /// call them until you turn them on. The signed-in person's own permissions still decide what succeeds.
    /// </summary>
    public bool EnableWriteTools { get; set; }
}

public sealed class JiraOptions : ProductOptions
{
    public const string SectionName = "Jira";
}

public sealed class ConfluenceOptions : ProductOptions
{
    public const string SectionName = "Confluence";
}

public sealed class BitbucketOptions : ProductOptions
{
    public const string SectionName = "Bitbucket";

    /// <summary>The Bitbucket Cloud REST API. Only tests change this.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.bitbucket.org/2.0/";

    /// <summary>Optional. When set, every tool is limited to this workspace slug.</summary>
    public string? Workspace { get; set; }
}

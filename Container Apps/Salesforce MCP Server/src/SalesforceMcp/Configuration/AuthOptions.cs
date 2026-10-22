namespace SalesforceMcp.Configuration;

/// <summary>
/// Inbound authentication. Callers sign in to Salesforce themselves and present their Salesforce access
/// token. The server checks the token with Salesforce, then uses that same token to call Salesforce, so
/// every request runs with the signed-in person's own permissions.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string PolicyName = "McpAccess";

    /// <summary>
    /// Leave true everywhere except local development. When false, tokens are not checked at the door.
    /// Salesforce still checks every token on every call, so this only affects who can list the tools.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Overrides the resource identifier published in protected resource metadata. Normally inferred.</summary>
    public string? ResourceUri { get; set; }

    /// <summary>
    /// How long a token that Salesforce accepted is trusted without asking again. Revoking a token in
    /// Salesforce takes effect for the next call after this long. Rejected tokens are remembered for a few
    /// seconds only, so a client that signs in again is not turned away.
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

namespace SlackCoworkMcp.Auth;

/// <summary>
/// Configuration for the OAuth shim that translates between the Cowork Plugin Vault's
/// standard OAuth 2.0 authorization-code flow and Slack v2 OAuth (which splits bot
/// vs. user token scopes across <c>scope</c> and <c>user_scope</c> parameters).
/// </summary>
public sealed class OAuthShimOptions
{
    public const string SectionName = "OAuthShim";

    /// <summary>Slack app client id (public).</summary>
    public string SlackClientId { get; set; } = "";

    /// <summary>Slack app client secret. Bound at runtime from Key Vault via Container App secretRef.</summary>
    public string SlackClientSecret { get; set; } = "";

    /// <summary>Comma-separated Slack bot scopes. Empty string is valid.</summary>
    public string SlackBotScopes { get; set; } = "";

    /// <summary>Comma-separated Slack user scopes. These produce the xoxp-* token the MCP needs.</summary>
    public string SlackUserScopes { get; set; } = "";

    public string SlackAuthorizeUrl { get; set; } = "https://slack.com/oauth/v2/authorize";
    public string SlackTokenUrl { get; set; } = "https://slack.com/api/oauth.v2.access";

    /// <summary>Client id the Cowork Plugin Vault sends. Compared against inbound requests.</summary>
    public string CoworkClientId { get; set; } = "";

    /// <summary>Client secret the Cowork Plugin Vault sends. Compared at the token endpoint.</summary>
    public string CoworkClientSecret { get; set; } = "";

    /// <summary>
    /// Public base URL of the shim (e.g. <c>https://ca-xxx.region.azurecontainerapps.io</c>).
    /// Used to build the redirect_uri sent to Slack. When empty, derived from the inbound request.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>TTL for one-time shim codes returned to Cowork.</summary>
    public int AuthCodeTtlSeconds { get; set; } = 300;

    /// <summary>TTL for in-flight authorize state (between /authorize and /callback).</summary>
    public int StateTtlSeconds { get; set; } = 600;
}

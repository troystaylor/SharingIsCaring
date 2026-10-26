using System.Security.Claims;

namespace CopilotStudioMcp.Configuration;

/// <summary>
/// Inbound authentication. The plugin signs the user in to this server's own Entra app (access_as_user), so the
/// gateway validates tokens issued for itself. It then exchanges each one for a Power Platform token on the
/// user's behalf.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string PolicyName = "McpAccess";

    /// <summary>Leave true everywhere except local development.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Entra tenant whose tokens are accepted. Tokens from any other tenant are refused.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID of the Entra app that is both the plugin's sign-in client and this server's API.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Delegated scope callers must hold.</summary>
    public string ScopeName { get; set; } = "access_as_user";

    public string Authority => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public string FullScope => $"api://{ClientId}/{ScopeName}";

    public IReadOnlyList<string> ValidAudiences => [ClientId, $"api://{ClientId}"];

    /// <summary>Throws at startup, so a misconfigured deployment fails loudly instead of running open.</summary>
    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (!Guid.TryParse(TenantId, out _))
        {
            throw new InvalidOperationException("Auth:TenantId must be a GUID when Auth:Enabled is true.");
        }

        if (!Guid.TryParse(ClientId, out _))
        {
            throw new InvalidOperationException("Auth:ClientId must be a GUID when Auth:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(ScopeName))
        {
            throw new InvalidOperationException("Auth:ScopeName is required when Auth:Enabled is true.");
        }
    }

    public bool Allows(ClaimsPrincipal user) =>
        user.FindAll("scp")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(ScopeName, StringComparer.Ordinal);
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public const string PolicyName = "mcp";

    /// <summary>Requests per caller per minute. 0 disables rate limiting.</summary>
    public int PermitsPerMinute { get; set; } = 120;
}

/// <summary>How the gateway obtains a Power Platform token for the signed-in user (the on-behalf-of exchange).</summary>
public sealed class ExchangeOptions
{
    public const string SectionName = "Exchange";

    /// <summary>Scope requested from Entra. <c>.default</c> returns every Power Platform permission the app has consent for.</summary>
    public string Scope { get; set; } = "https://api.powerplatform.com/.default";

    /// <summary>
    /// Client ID of the user-assigned managed identity whose token proves this server is the Entra app (a federated
    /// credential on the app trusts it). Leave empty to use a client secret instead.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Only for local development. In Azure the managed identity is used and no secret exists.</summary>
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>How long the mirrored tool list is reused before it is read again from Copilot Studio.</summary>
    public int ToolsCacheMinutes { get; set; } = 10;

    /// <summary>Longest a single call to Copilot Studio may take.</summary>
    public int UpstreamTimeoutSeconds { get; set; } = 120;

    /// <summary>Largest response relayed back to the caller.</summary>
    public int MaxResponseBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Largest request body accepted.</summary>
    public int MaxRequestBytes { get; set; } = 1024 * 1024;
}

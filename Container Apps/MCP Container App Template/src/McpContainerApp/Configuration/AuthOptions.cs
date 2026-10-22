using System.Security.Claims;

namespace McpContainerApp.Configuration;

/// <summary>
/// Inbound authentication: validates Microsoft Entra access tokens issued for this MCP server.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string PolicyName = "McpAccess";

    /// <summary>Leave true everywhere except local development.</summary>
    public bool Enabled { get; set; } = true;

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID of the Entra app registration that represents this server.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Delegated scope callers must hold, for example <c>access_as_user</c>.</summary>
    public string ScopeName { get; set; } = "access_as_user";

    /// <summary>Optional app role. Lets app-only callers (no user) in without the delegated scope.</summary>
    public string? AppRole { get; set; }

    /// <summary>Extra accepted <c>aud</c> values, beyond the client ID and <c>api://{ClientId}</c>.</summary>
    public string[] AdditionalAudiences { get; set; } = [];

    /// <summary>Overrides the resource identifier published in protected resource metadata. Normally inferred.</summary>
    public string? ResourceUri { get; set; }

    public string Authority => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public string FullScope => $"api://{ClientId}/{ScopeName}";

    public IReadOnlyList<string> ValidAudiences =>
        [ClientId, $"api://{ClientId}", .. AdditionalAudiences.Where(a => !string.IsNullOrWhiteSpace(a))];

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

    /// <summary>True when the caller holds the delegated scope, or the configured app role.</summary>
    public bool Allows(ClaimsPrincipal user)
    {
        var scopes = user.FindAll("scp")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (scopes.Contains(ScopeName, StringComparer.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(AppRole)
            && user.FindAll("roles").Any(c => string.Equals(c.Value, AppRole, StringComparison.Ordinal));
    }
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public const string PolicyName = "mcp";

    /// <summary>Requests per caller per minute. 0 disables rate limiting.</summary>
    public int PermitsPerMinute { get; set; } = 120;
}

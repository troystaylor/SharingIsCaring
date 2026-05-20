using Microsoft.AspNetCore.Http;

namespace SlackCoworkMcp.Auth;

/// <summary>
/// Per-request accessor for the inbound Slack user token. Cowork forwards
/// <c>Authorization: Bearer xoxp-...</c> from the M365 Enterprise Token Store
/// binding <c>slack-oauth</c>; the MCP server forwards the same value on
/// outbound calls to <c>slack.com/api/*</c>.
/// </summary>
public interface IBearerTokenAccessor
{
    /// <summary>Returns the inbound bearer token or null when missing.</summary>
    string? Get();

    /// <summary>Returns the token or throws <see cref="UnauthorizedSlackException"/>.</summary>
    string Require();
}

public sealed class BearerTokenAccessor : IBearerTokenAccessor
{
    private readonly IHttpContextAccessor _ctx;
    /// <summary>Local-dev override: set <c>SLACK_DEV_USER_TOKEN</c> when running outside Cowork.</summary>
    private readonly string? _devToken;

    public BearerTokenAccessor(IHttpContextAccessor ctx)
    {
        _ctx = ctx;
        _devToken = Environment.GetEnvironmentVariable("SLACK_DEV_USER_TOKEN");
    }

    public string? Get()
    {
        var http = _ctx.HttpContext;
        if (http is not null)
        {
            var hdr = http.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(hdr) && hdr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return hdr["Bearer ".Length..].Trim();
            }
        }
        return _devToken;
    }

    public string Require()
        => Get() ?? throw new UnauthorizedSlackException("unauthorized: missing user token");
}

/// <summary>
/// Thrown when the inbound request carries no bearer token. Surfaces as
/// JSON-RPC error code <c>-32001</c>.
/// </summary>
public sealed class UnauthorizedSlackException(string message) : Exception(message);

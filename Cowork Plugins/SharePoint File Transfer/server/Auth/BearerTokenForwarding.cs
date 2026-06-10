using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Client;

namespace SharePointTransferMcp.Auth;

public interface IBearerTokenAccessor
{
    string? Get();
    string Require();
    Task<string> RequireGraphTokenAsync();
    TokenDiagnostic Diagnose();
}

public sealed record TokenDiagnostic(
    bool Present,
    string Source,
    int Length,
    string? Shape,
    long? ExpiresAtUnix,
    long? SecondsUntilExpiry);

public sealed class BearerTokenAccessor : IBearerTokenAccessor
{
    private readonly IHttpContextAccessor _ctx;
    private readonly string? _devToken;
    private readonly IConfidentialClientApplication? _cca;
    private readonly string[] _graphScopes;
    private readonly ILogger<BearerTokenAccessor> _logger;

    public BearerTokenAccessor(IHttpContextAccessor ctx, IConfiguration config, ILogger<BearerTokenAccessor> logger)
    {
        _ctx = ctx;
        _logger = logger;
        _devToken = Environment.GetEnvironmentVariable("GRAPH_DEV_ACCESS_TOKEN");

        var clientId = config["OBO_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("OBO_CLIENT_ID");
        var clientSecret = config["OBO_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("OBO_CLIENT_SECRET");
        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            _cca = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority("https://login.microsoftonline.com/common")
                .Build();
            _logger.LogInformation("OBO configured for client {ClientId} (multi-tenant)", clientId);
        }
        else
        {
            _logger.LogInformation("OBO not configured — using direct bearer forwarding");
        }

        _graphScopes = new[] { "https://graph.microsoft.com/.default" };
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
        => Get() ?? throw new UnauthorizedGraphException(
            "missing_token",
            "no bearer token was forwarded from Cowork; the user likely needs to authorize the SharePoint File Transfer connector");

    public async Task<string> RequireGraphTokenAsync()
    {
        var incomingToken = Require();

        // If OBO is configured and the token targets our own API (SSO path),
        // exchange it for a Graph token via On-Behalf-Of.
        if (_cca is not null && IsAppAudienceToken(incomingToken))
        {
            try
            {
                var result = await _cca.AcquireTokenOnBehalfOf(_graphScopes, new UserAssertion(incomingToken))
                    .ExecuteAsync();
                _logger.LogInformation("OBO exchange succeeded, Graph token expires {Expiry}", result.ExpiresOn);
                return result.AccessToken;
            }
            catch (MsalException ex)
            {
                _logger.LogError(ex, "OBO exchange failed: {Error}", ex.Message);
                throw new UnauthorizedGraphException("obo_failed",
                    $"On-behalf-of token exchange failed: {ex.ErrorCode} — {ex.Message}");
            }
        }

        // Otherwise, token is already a Graph token (direct forwarding path)
        return incomingToken;
    }

    /// <summary>
    /// Returns true if the JWT's aud claim targets our own app (SSO token),
    /// not Microsoft Graph.
    /// </summary>
    private static bool IsAppAudienceToken(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return false;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            if (JsonNode.Parse(json) is JsonObject obj && obj.TryGetPropertyValue("aud", out var audNode))
            {
                var aud = audNode?.GetValue<string>() ?? "";
                // Graph tokens have aud = 00000003-... or https://graph.microsoft.com
                // SSO tokens have aud = api://... or the app client id
                return !aud.Contains("00000003-0000-0000-c000-000000000000")
                    && !aud.Contains("graph.microsoft.com");
            }
        }
        catch { }
        return false; // assume Graph token if we can't parse
    }

    public TokenDiagnostic Diagnose()
    {
        var http = _ctx.HttpContext;
        var headerVal = http?.Request.Headers.Authorization.ToString();
        var hasHeader = !string.IsNullOrEmpty(headerVal) && headerVal!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var token = hasHeader ? headerVal!["Bearer ".Length..].Trim() : _devToken;
        var source = hasHeader ? "request_header" : (token is not null ? "env" : "none");

        if (string.IsNullOrEmpty(token))
        {
            return new TokenDiagnostic(false, source, 0, null, null, null);
        }

        var shape = ClassifyShape(token);
        long? exp = shape == "jwt" ? TryGetJwtExpiry(token) : null;
        long? remaining = exp.HasValue
            ? exp.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : null;

        return new TokenDiagnostic(true, source, token.Length, shape, exp, remaining);
    }

    private static string ClassifyShape(string token)
    {
        var parts = token.Split('.');
        if (parts.Length == 3 && parts[0].Length > 0 && parts[1].Length > 0)
        {
            return "jwt";
        }
        return "opaque";
    }

    private static long? TryGetJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var bytes = Convert.FromBase64String(payload);
            var json = Encoding.UTF8.GetString(bytes);
            if (JsonNode.Parse(json) is JsonObject node
                && node.TryGetPropertyValue("exp", out var expNode)
                && expNode is not null)
            {
                return expNode.GetValue<long>();
            }
        }
        catch
        {
            // diagnostics only
        }
        return null;
    }
}

public sealed class UnauthorizedGraphException : Exception
{
    public string ErrorCode { get; }

    public UnauthorizedGraphException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

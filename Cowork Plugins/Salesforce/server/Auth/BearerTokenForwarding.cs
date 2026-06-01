using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace SalesforceCoworkMcp.Auth;

public interface IBearerTokenAccessor
{
    string? Get();
    string Require();
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

    public BearerTokenAccessor(IHttpContextAccessor ctx)
    {
        _ctx = ctx;
        _devToken = Environment.GetEnvironmentVariable("SALESFORCE_DEV_ACCESS_TOKEN");
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
        => Get() ?? throw new UnauthorizedSalesforceException(
            "missing_token",
            "no bearer token was forwarded from Cowork; the user likely needs to authorize the Salesforce connector");

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
        if (token.StartsWith("00D", StringComparison.Ordinal) && token.Contains('!'))
        {
            return "salesforce_session";
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

public sealed class UnauthorizedSalesforceException : Exception
{
    public string ErrorCode { get; }

    public UnauthorizedSalesforceException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

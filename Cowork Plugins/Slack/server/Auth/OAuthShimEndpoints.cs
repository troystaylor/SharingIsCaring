using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SlackCoworkMcp.Auth;

/// <summary>
/// OAuth shim endpoints that translate between the Cowork Plugin Vault's standard
/// OAuth 2.0 authorization-code flow and Slack v2 OAuth.
///
/// Slack splits bot vs. user token scopes across two parameters
/// (<c>scope</c> and <c>user_scope</c>) and returns the user token at
/// <c>authed_user.access_token</c> in the token response. The Cowork Plugin Vault
/// only models the standard single-<c>scope</c> flow with a top-level
/// <c>access_token</c>. This shim reshapes both sides.
/// </summary>
public static class OAuthShimEndpoints
{
    public static IEndpointRouteBuilder MapOAuthShim(this IEndpointRouteBuilder app)
    {
        app.MapGet("/oauth/authorize", AuthorizeAsync);
        app.MapGet("/oauth/callback", CallbackAsync);
        app.MapPost("/oauth/token", TokenAsync);
        return app;
    }

    private static Task<IResult> AuthorizeAsync(
        HttpContext ctx,
        IOptions<OAuthShimOptions> opts,
        IOAuthShimStore store,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("OAuthShim.Authorize");
        var o = opts.Value;
        var q = ctx.Request.Query;

        var responseType = q["response_type"].ToString();
        var clientId = q["client_id"].ToString();
        var redirectUri = q["redirect_uri"].ToString();
        var coworkState = q["state"].ToString();
        var codeChallenge = q["code_challenge"].ToString();
        var codeChallengeMethod = q["code_challenge_method"].ToString();

        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
        {
            return Task.FromResult(Results.BadRequest(new { error = "unsupported_response_type" }));
        }
        if (string.IsNullOrWhiteSpace(o.CoworkClientId) || !string.Equals(clientId, o.CoworkClientId, StringComparison.Ordinal))
        {
            log.LogWarning("Unknown client_id at /authorize");
            return Task.FromResult(Results.BadRequest(new { error = "unauthorized_client" }));
        }
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return Task.FromResult(Results.BadRequest(new { error = "invalid_request", error_description = "redirect_uri required" }));
        }
        if (string.IsNullOrWhiteSpace(o.SlackClientId))
        {
            log.LogError("OAuthShim.SlackClientId not configured");
            return Task.FromResult(Results.Problem("OAuth shim not configured: SlackClientId missing", statusCode: 500));
        }

        var shimState = NewRandomToken(32);
        store.StoreAuth(shimState, new CoworkAuthRequest(
            CoworkRedirectUri: redirectUri,
            CoworkState: coworkState,
            CodeChallenge: string.IsNullOrEmpty(codeChallenge) ? null : codeChallenge,
            CodeChallengeMethod: string.IsNullOrEmpty(codeChallengeMethod) ? null : codeChallengeMethod,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(o.StateTtlSeconds)));

        var slackRedirect = BuildShimRedirectUri(ctx, o);

        var sb = new StringBuilder();
        sb.Append(o.SlackAuthorizeUrl.TrimEnd('?'));
        sb.Append('?');
        sb.Append("client_id=").Append(HttpUtility.UrlEncode(o.SlackClientId));
        sb.Append("&scope=").Append(HttpUtility.UrlEncode(o.SlackBotScopes ?? ""));
        sb.Append("&user_scope=").Append(HttpUtility.UrlEncode(o.SlackUserScopes ?? ""));
        sb.Append("&redirect_uri=").Append(HttpUtility.UrlEncode(slackRedirect));
        sb.Append("&state=").Append(HttpUtility.UrlEncode(shimState));

        log.LogInformation("Redirecting to Slack OAuth (bot scopes len={Bot}, user scopes len={User})",
            (o.SlackBotScopes ?? "").Length, (o.SlackUserScopes ?? "").Length);

        return Task.FromResult(Results.Redirect(sb.ToString()));
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext ctx,
        IOptions<OAuthShimOptions> opts,
        IOAuthShimStore store,
        IHttpClientFactory http,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("OAuthShim.Callback");
        var o = opts.Value;
        var q = ctx.Request.Query;

        var slackError = q["error"].ToString();
        if (!string.IsNullOrEmpty(slackError))
        {
            log.LogWarning("Slack returned error: {Error}", slackError);
            return Results.BadRequest(new { error = slackError });
        }

        var code = q["code"].ToString();
        var shimState = q["state"].ToString();
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(shimState))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "code/state missing" });
        }

        var pending = store.TakeAuth(shimState);
        if (pending is null)
        {
            log.LogWarning("Unknown or expired state at /callback");
            return Results.BadRequest(new { error = "invalid_state" });
        }

        var slackRedirect = BuildShimRedirectUri(ctx, o);

        // Server-side exchange with Slack
        using var client = http.CreateClient("slack-oauth");
        using var req = new HttpRequestMessage(HttpMethod.Post, o.SlackTokenUrl);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = o.SlackClientId,
            ["client_secret"] = o.SlackClientSecret,
            ["code"] = code,
            ["redirect_uri"] = slackRedirect,
        });

        string slackJson;
        try
        {
            using var resp = await client.SendAsync(req, ctx.RequestAborted);
            slackJson = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Slack token exchange failed");
            return Results.Problem("Slack token exchange failed", statusCode: 502);
        }

        using var doc = JsonDocument.Parse(slackJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            var err = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : "unknown_error";
            log.LogWarning("Slack token endpoint returned not-ok: {Error}", err);
            return Results.BadRequest(new { error = "slack_oauth_failed", slack_error = err });
        }

        // Pull the user token from authed_user.access_token; that's the xoxp-* the MCP needs.
        string? userToken = null;
        string? userScope = null;
        if (root.TryGetProperty("authed_user", out var au) && au.ValueKind == JsonValueKind.Object)
        {
            if (au.TryGetProperty("access_token", out var atEl)) userToken = atEl.GetString();
            if (au.TryGetProperty("scope", out var scEl)) userScope = scEl.GetString();
        }

        if (string.IsNullOrEmpty(userToken))
        {
            log.LogWarning("Slack response missing authed_user.access_token; falling back to top-level access_token");
            if (root.TryGetProperty("access_token", out var topAt)) userToken = topAt.GetString();
        }

        if (string.IsNullOrEmpty(userToken))
        {
            return Results.Problem("Slack returned no user access_token; verify user_scope is configured", statusCode: 502);
        }

        // Mint a shim code, store the token, redirect back to Cowork.
        var shimCode = NewRandomToken(32);
        store.StoreCode(shimCode, new IssuedToken(
            AccessToken: userToken!,
            Scope: userScope,
            CodeChallenge: pending.CodeChallenge,
            CodeChallengeMethod: pending.CodeChallengeMethod,
            CoworkRedirectUri: pending.CoworkRedirectUri,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(o.AuthCodeTtlSeconds)));

        var sep = pending.CoworkRedirectUri.Contains('?') ? '&' : '?';
        var redirect = $"{pending.CoworkRedirectUri}{sep}code={HttpUtility.UrlEncode(shimCode)}&state={HttpUtility.UrlEncode(pending.CoworkState ?? "")}";
        log.LogInformation("Slack user token issued; redirecting to Cowork");
        return Results.Redirect(redirect);
    }

    private static async Task<IResult> TokenAsync(
        HttpContext ctx,
        IOptions<OAuthShimOptions> opts,
        IOAuthShimStore store,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("OAuthShim.Token");
        var o = opts.Value;

        if (!ctx.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "form body required" });
        }

        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var grantType = form["grant_type"].ToString();
        var code = form["code"].ToString();
        var codeVerifier = form["code_verifier"].ToString();
        var clientId = form["client_id"].ToString();
        var clientSecret = form["client_secret"].ToString();

        // Some clients send credentials via HTTP Basic auth.
        if (string.IsNullOrEmpty(clientId) && ctx.Request.Headers.TryGetValue("Authorization", out var authHdr))
        {
            var v = authHdr.ToString();
            if (v.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(v["Basic ".Length..].Trim()));
                    var ix = decoded.IndexOf(':');
                    if (ix > 0)
                    {
                        clientId = decoded[..ix];
                        clientSecret = decoded[(ix + 1)..];
                    }
                }
                catch { /* ignore malformed basic */ }
            }
        }

        if (!string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }
        if (string.IsNullOrEmpty(code))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "code required" });
        }

        if (!string.Equals(clientId, o.CoworkClientId, StringComparison.Ordinal))
        {
            log.LogWarning("Token request had wrong client_id");
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);
        }
        // Allow secret to be optional when PKCE is in play (RFC 7636), required otherwise.
        var pkceUsed = !string.IsNullOrEmpty(codeVerifier);
        if (!pkceUsed && !string.Equals(clientSecret, o.CoworkClientSecret, StringComparison.Ordinal))
        {
            log.LogWarning("Token request had wrong client_secret (and no PKCE)");
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);
        }
        if (pkceUsed && !string.IsNullOrEmpty(o.CoworkClientSecret) && !string.IsNullOrEmpty(clientSecret)
            && !string.Equals(clientSecret, o.CoworkClientSecret, StringComparison.Ordinal))
        {
            log.LogWarning("Token request had wrong client_secret (PKCE present)");
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);
        }

        var issued = store.TakeCode(code);
        if (issued is null)
        {
            return Results.BadRequest(new { error = "invalid_grant", error_description = "code unknown or expired" });
        }

        // PKCE verification if a challenge was registered.
        if (!string.IsNullOrEmpty(issued.CodeChallenge))
        {
            if (string.IsNullOrEmpty(codeVerifier))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "code_verifier required" });
            }
            var method = string.IsNullOrEmpty(issued.CodeChallengeMethod) ? "plain" : issued.CodeChallengeMethod!;
            var expected = method.Equals("S256", StringComparison.OrdinalIgnoreCase)
                ? Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
                : codeVerifier;
            if (!FixedTimeEquals(expected, issued.CodeChallenge))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "code_verifier mismatch" });
            }
        }

        // Slack user tokens are long-lived unless token rotation is enabled on the app.
        // Omit expires_in so Cowork treats it as non-expiring; add refresh later if rotation is turned on.
        return Results.Json(new
        {
            access_token = issued.AccessToken,
            token_type = "Bearer",
            scope = issued.Scope,
        });
    }

    private static string BuildShimRedirectUri(HttpContext ctx, OAuthShimOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.PublicBaseUrl))
        {
            return o.PublicBaseUrl.TrimEnd('/') + "/oauth/callback";
        }
        // Honor X-Forwarded-Proto so Container Apps ingress (TLS-terminated) yields https://.
        var scheme = ctx.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && !string.IsNullOrEmpty(proto)
            ? proto.ToString().Split(',')[0].Trim()
            : ctx.Request.Scheme;
        return $"{scheme}://{ctx.Request.Host}/oauth/callback";
    }

    private static string NewRandomToken(int byteLen)
        => Base64Url(RandomNumberGenerator.GetBytes(byteLen));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}

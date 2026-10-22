using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SalesforceMcp.Salesforce;

public static class SalesforceTokenDefaults
{
    public const string AuthenticationScheme = "SalesforceToken";
}

/// <summary>
/// Authenticates a request by checking its bearer token with Salesforce. A request with no token yields no
/// result, so the challenge answers it with a 401 that points the client at how to sign in.
/// </summary>
public sealed class SalesforceTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SalesforceTokenValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = HttpContextSalesforceUser.BearerToken(Request.Headers.Authorization.ToString());
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        SalesforceUserInfo? info;
        try
        {
            info = await validator.ValidateAsync(token, Context.RequestAborted);
        }
        catch (SalesforceUnavailableException ex)
        {
            Logger.LogWarning("{Reason}", ex.Message);
            return AuthenticateResult.Fail(ex.Message);
        }

        if (info is null)
        {
            return AuthenticateResult.Fail("The Salesforce access token was not accepted.");
        }

        var claims = new List<Claim>
        {
            new("user_id", info.UserId),
            new("organization_id", info.OrganizationId)
        };

        AddIfPresent(claims, "preferred_username", info.Username);
        AddIfPresent(claims, "name", info.DisplayName);
        AddIfPresent(claims, "email", info.Email);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, "name", ClaimTypes.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(type, value));
        }
    }
}

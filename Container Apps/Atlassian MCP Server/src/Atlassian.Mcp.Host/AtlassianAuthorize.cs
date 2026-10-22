using System.Text;

namespace Atlassian.Mcp.Host;

/// <summary>
/// Sends a person to Atlassian's sign-in page with the <c>audience</c> parameter Atlassian requires.
/// </summary>
/// <remarks>
/// Atlassian will not issue a token the Jira and Confluence APIs accept unless the authorization request carries
/// <c>audience=api.atlassian.com</c>. Neither the Teams OAuth client registration nor the Power Platform OAuth
/// template can add a parameter of their own. Point their authorization URL here and this adds it, then redirects.
/// The token and refresh requests go straight to Atlassian, so no code, token, or secret ever passes through
/// this server.
/// <para>
/// The destination is fixed. Only the standard authorization parameters are passed on and <c>audience</c> is
/// always set here, so this cannot be used to redirect anywhere else or to ask Atlassian for something other
/// than what the client registered.
/// </para>
/// </remarks>
public static class AtlassianAuthorize
{
    public const string Path = "/atlassian/oauth/authorize";

    public const string Target = "https://auth.atlassian.com/authorize";
    public const string Audience = "api.atlassian.com";

    private const int MaxQueryChars = 4096;

    private static readonly string[] Passed =
        ["client_id", "redirect_uri", "response_type", "scope", "state", "code_challenge", "code_challenge_method"];

    public static IResult Handle(HttpRequest request)
    {
        if (request.QueryString.Value is { Length: > MaxQueryChars })
        {
            return Results.Text("The authorization request is too long.", statusCode: StatusCodes.Status414UriTooLong);
        }

        var query = new Dictionary<string, string>();
        foreach (var name in Passed)
        {
            if (request.Query.TryGetValue(name, out var value) && value.Count > 0 && !string.IsNullOrEmpty(value[0]))
            {
                query[name] = value[0]!;
            }
        }

        foreach (var required in new[] { "client_id", "redirect_uri", "response_type" })
        {
            if (!query.ContainsKey(required))
            {
                return Results.Text($"The authorization request is missing {required}.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        if (query["response_type"] != "code")
        {
            return Results.Text("Only response_type=code is supported.", statusCode: StatusCodes.Status400BadRequest);
        }

        var url = new StringBuilder(Target).Append("?audience=").Append(Uri.EscapeDataString(Audience));
        foreach (var (name, value) in query)
        {
            url.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        // Atlassian only shows its consent screen when asked to, and a client cannot be relied on to ask.
        url.Append("&prompt=consent");

        return Results.Redirect(url.ToString());
    }
}

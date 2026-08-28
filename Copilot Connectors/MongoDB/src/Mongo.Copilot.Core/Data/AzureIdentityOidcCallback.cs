using Azure.Core;
using Azure.Identity;
using MongoDB.Driver.Authentication.Oidc;

namespace Mongo.Copilot.Core.Data;

/// <summary>
/// Supplies Entra access tokens to the MongoDB driver for <c>MONGODB-OIDC</c> authentication.
/// </summary>
/// <remarks>
/// Both viable Azure targets accept an Entra token as a database credential — Azure
/// DocumentDB through Microsoft Entra authentication, and MongoDB Atlas through workload
/// identity federation. Using this removes the connection-string password from the
/// deployment entirely: no secret to store, rotate, or leak.
/// <para>
/// <see cref="DefaultAzureCredential"/> caches and refreshes tokens internally, so the
/// driver calling this per authentication does not cause a token request each time.
/// </para>
/// </remarks>
public sealed class AzureIdentityOidcCallback : IOidcCallback
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;

    public AzureIdentityOidcCallback(string tokenResource, string? managedIdentityClientId = null)
    {
        if (string.IsNullOrWhiteSpace(tokenResource))
            throw new ArgumentException("A token resource is required for managed identity auth.", nameof(tokenResource));

        // Accept either a bare resource or one already suffixed with /.default.
        _scopes =
        [
            tokenResource.EndsWith("/.default", StringComparison.Ordinal)
                ? tokenResource
                : tokenResource.TrimEnd('/') + "/.default"
        ];

        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? null
                : managedIdentityClientId
        });
    }

    public OidcAccessToken GetOidcAccessToken(OidcCallbackParameters parameters, CancellationToken cancellationToken)
    {
        var token = _credential.GetToken(new TokenRequestContext(_scopes), cancellationToken);
        return ToAccessToken(token);
    }

    public async Task<OidcAccessToken> GetOidcAccessTokenAsync(
        OidcCallbackParameters parameters,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);
        return ToAccessToken(token);
    }

    private static OidcAccessToken ToAccessToken(AccessToken token)
    {
        var lifetime = token.ExpiresOn - DateTimeOffset.UtcNow;

        return new OidcAccessToken(
            token.Token,
            lifetime > TimeSpan.Zero ? lifetime : null);
    }
}

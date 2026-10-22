using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Atlassian.Mcp.Confluence;

/// <summary>
/// Sends Confluence Cloud REST requests as the signed-in person, through the Atlassian API gateway and the
/// cloud ID of the configured site.
/// </summary>
public sealed class ConfluenceClient(
    IHttpClientFactory httpClientFactory,
    ICaller caller,
    IOptions<AtlassianOptions> options)
    : UpstreamClient(httpClientFactory.CreateClient(HttpNames.Atlassian), caller, "Confluence")
{
    public AtlassianOptions Settings => options.Value;

    public ICaller Who => Caller;

    /// <summary>A browser link built from a relative <c>webui</c> link such as <c>/spaces/ENG/pages/123/Title</c>.</summary>
    public string WebUrl(string? webui) =>
        string.IsNullOrEmpty(webui) ? $"{Settings.Site}/wiki" : $"{Settings.Site}/wiki{webui}";

    public Task<JsonNode?> GetAsync(string resource, CancellationToken cancellationToken) =>
        GetJsonAsync($"ex/confluence/{CloudId()}/wiki/{resource.TrimStart('/')}", cancellationToken);

    public Task<JsonNode?> SendAsync(HttpMethod method, string resource, JsonNode? body, CancellationToken cancellationToken) =>
        SendJsonAsync(method, $"ex/confluence/{CloudId()}/wiki/{resource.TrimStart('/')}", body, cancellationToken);

    private string CloudId() =>
        Caller.CloudId
        ?? Settings.CloudId
        ?? throw new McpException(
            $"Your Atlassian sign-in does not have access to {Settings.Site}. "
            + "Sign in with an account that can open that site, and check that the sign-in granted access to it.");
}

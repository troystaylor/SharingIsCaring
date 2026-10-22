using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Atlassian.Mcp.Jira;

/// <summary>
/// Sends Jira Cloud REST requests as the signed-in person. Calls go through the Atlassian API gateway, which
/// is where OAuth tokens are accepted, using the cloud ID of the configured site.
/// </summary>
public sealed class JiraClient(
    IHttpClientFactory httpClientFactory,
    ICaller caller,
    IOptions<AtlassianOptions> options)
    : UpstreamClient(httpClientFactory.CreateClient(HttpNames.Atlassian), caller, "Jira")
{
    public AtlassianOptions Settings => options.Value;

    public ICaller Who => Caller;

    /// <summary>A browser link for an issue.</summary>
    public string IssueUrl(string key) => $"{Settings.Site}/browse/{key}";

    public Task<JsonNode?> GetAsync(string resource, CancellationToken cancellationToken) =>
        GetJsonAsync($"ex/jira/{CloudId()}/{resource.TrimStart('/')}", cancellationToken);

    public Task<JsonNode?> SendAsync(HttpMethod method, string resource, JsonNode? body, CancellationToken cancellationToken) =>
        SendJsonAsync(method, $"ex/jira/{CloudId()}/{resource.TrimStart('/')}", body, cancellationToken);

    private string CloudId() =>
        Caller.CloudId
        ?? Settings.CloudId
        ?? throw new McpException(
            $"Your Atlassian sign-in does not have access to {Settings.Site}. "
            + "Sign in with an account that can open that site, and check that the sign-in granted access to it.");
}

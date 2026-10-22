using System.Text.Json.Nodes;
using Atlassian.Mcp.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Atlassian.Mcp.Bitbucket;

/// <summary>
/// Sends Bitbucket Cloud REST requests as the signed-in person. Bitbucket uses its own OAuth consumer and its own
/// API host, separate from Jira and Confluence.
/// </summary>
public sealed class BitbucketClient(
    IHttpClientFactory httpClientFactory,
    ICaller caller,
    IOptions<BitbucketOptions> options)
    : UpstreamClient(httpClientFactory.CreateClient(HttpNames.Bitbucket), caller, "Bitbucket")
{
    public BitbucketOptions Settings => options.Value;

    /// <summary>
    /// Validates a workspace slug, and when the server is pinned to one workspace, refuses any other. The pin is
    /// a convenience for tidy results. Bitbucket's own permissions remain the real limit.
    /// </summary>
    public string Workspace(string? workspace)
    {
        var slug = Names.Slug(workspace, "workspace");
        if (!string.IsNullOrEmpty(Settings.Workspace)
            && !string.Equals(slug, Settings.Workspace, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"This server is limited to the workspace '{Settings.Workspace}'.");
        }

        return slug;
    }

    public Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken) =>
        GetJsonAsync(path, cancellationToken);

    public Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken) =>
        SendJsonAsync(method, path, body, cancellationToken);

    public Task<string> TextAsync(string path, CancellationToken cancellationToken) =>
        GetTextAsync(path, "text/plain, */*", cancellationToken);
}

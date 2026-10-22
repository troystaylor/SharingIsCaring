using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using McpContainerApp.Configuration;

namespace McpContainerApp.Tools;

/// <summary>
/// Typed client for the upstream API. This is the only class that talks to it, so credentials,
/// error mapping, and result shaping live in one place rather than in every tool.
/// </summary>
public sealed class ExampleUpstreamClient(HttpClient http, IOptions<UpstreamOptions> options)
{
    public async Task<JsonElement> SearchAsync(string query, int top, CancellationToken cancellationToken)
    {
        if (http.BaseAddress is null)
        {
            throw new McpException("The upstream API is not configured. Set Upstream:BaseUrl.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"items?q={Uri.EscapeDataString(query)}&limit={top}");

        if (!string.IsNullOrEmpty(options.Value.ClientSecret))
        {
            request.Headers.Add("X-Api-Key", options.Value.ClientSecret);
        }

        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Status only: upstream bodies can carry internals that should not reach the model.
            throw new McpException($"The upstream API returned HTTP {(int)response.StatusCode}.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }
}

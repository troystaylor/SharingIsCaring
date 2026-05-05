using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace FederatedMcpTemplate.Tools;

/// <summary>
/// Example MCP tools for the federated connector template.
/// Replace this class with tools specific to your data source.
///
/// Federated connector rules:
///   - All tools MUST be read-only (no create/update/delete)
///   - Include clear descriptions for Copilot's tool selection
///   - Return structured JSON for Copilot to reason over
///   - Include source citations where possible (URLs to source records)
/// </summary>
[McpServerToolType]
public class ExampleTools(IHttpClientFactory httpClientFactory, IHttpContextAccessor contextAccessor)
{
    [McpServerTool(Title = "Search Records")]
    [Description("Search for records matching a query. Returns matching items with ID, name, and summary.")]
    public async Task<string> SearchRecords(
        [Description("Search query text")] string query,
        [Description("Maximum number of results to return (1-50)")] int top = 10,
        CancellationToken cancellationToken = default)
    {
        var client = CreateAuthenticatedClient();

        // TODO: Replace with actual upstream API call
        // var response = await client.GetAsync(
        //     $"/api/search?q={Uri.EscapeDataString(query)}&limit={top}",
        //     cancellationToken);
        // response.EnsureSuccessStatusCode();
        // return await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            query,
            results = new[]
            {
                new
                {
                    id = "REC-001",
                    name = "Sample Record",
                    summary = "This is a placeholder result from the template.",
                    url = "https://example.com/records/REC-001"
                }
            },
            total = 1,
            hasMore = false
        });
    }

    [McpServerTool(Title = "Get Record by ID")]
    [Description("Retrieve a single record by its unique identifier. Returns full record details.")]
    public async Task<string> GetRecord(
        [Description("The unique identifier of the record")] string id,
        CancellationToken cancellationToken = default)
    {
        var client = CreateAuthenticatedClient();

        // TODO: Replace with actual upstream API call
        // var response = await client.GetAsync(
        //     $"/api/records/{Uri.EscapeDataString(id)}",
        //     cancellationToken);
        // response.EnsureSuccessStatusCode();
        // return await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            id,
            name = "Sample Record",
            status = "active",
            description = "Full record details from the data source.",
            url = $"https://example.com/records/{id}",
            retrievedAt = DateTime.UtcNow.ToString("o")
        });
    }

    [McpServerTool(Title = "List Recent Records")]
    [Description("List the most recently updated records. Useful for checking current status and recent activity.")]
    public async Task<string> ListRecentRecords(
        [Description("Number of records to return (1-100)")] int count = 10,
        [Description("Filter by status: active, closed, or all")] string status = "all",
        CancellationToken cancellationToken = default)
    {
        var client = CreateAuthenticatedClient();

        // TODO: Replace with actual upstream API call
        // var url = $"/api/records?sort=updated_desc&limit={count}";
        // if (status != "all") url += $"&status={Uri.EscapeDataString(status)}";
        // var response = await client.GetAsync(url, cancellationToken);
        // response.EnsureSuccessStatusCode();
        // return await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            records = new[]
            {
                new
                {
                    id = "REC-001",
                    name = "Sample Record",
                    status = "active",
                    updatedAt = DateTime.UtcNow.AddHours(-1).ToString("o"),
                    url = "https://example.com/records/REC-001"
                }
            },
            total = 1,
            statusFilter = status
        });
    }

    /// <summary>
    /// Creates an HTTP client with the caller's bearer token forwarded
    /// to the upstream API. This enables user-scoped access — the upstream
    /// API sees the same identity that authenticated to the MCP server.
    ///
    /// For Entra SSO: The token is a Microsoft Entra ID JWT that can be
    /// used directly with Microsoft APIs (Graph, Azure, etc.) or exchanged
    /// via On-Behalf-Of (OBO) flow for a downstream API token.
    ///
    /// For OAuth 2.0: The token is from the third-party IdP and should be
    /// forwarded directly to the third-party API.
    /// </summary>
    private HttpClient CreateAuthenticatedClient()
    {
        var client = httpClientFactory.CreateClient("upstream");

        var token = contextAccessor.HttpContext?.Request.Headers.Authorization
            .ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}

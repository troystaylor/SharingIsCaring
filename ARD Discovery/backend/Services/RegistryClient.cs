#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ArdDiscovery.Services;

/// <summary>
/// HTTP client wrapper for querying ARD registries.
/// Handles search, explore, and list operations per the ARD API spec (§7).
/// </summary>
public class RegistryClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public RegistryClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// POST /search — semantic search for capabilities.
    /// </summary>
    public async Task<JsonNode?> SearchAsync(string registryUrl, JsonNode body)
    {
        var client = _httpClientFactory.CreateClient("ard-registry");
        var url = $"{registryUrl.TrimEnd('/')}/search";
        var response = await client.PostAsync(url,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(content);
    }

    /// <summary>
    /// POST /explore — facet aggregation.
    /// </summary>
    public async Task<JsonNode?> ExploreAsync(string registryUrl, JsonNode body)
    {
        var client = _httpClientFactory.CreateClient("ard-registry");
        var url = $"{registryUrl.TrimEnd('/')}/explore";
        var response = await client.PostAsync(url,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(content);
    }

    /// <summary>
    /// GET /agents — deterministic listing with optional filters.
    /// </summary>
    public async Task<JsonNode?> ListAsync(string registryUrl, string? filter = null,
        string? orderBy = null, int? pageSize = null, string? pageToken = null)
    {
        var client = _httpClientFactory.CreateClient("ard-registry");
        var url = $"{registryUrl.TrimEnd('/')}/agents";

        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(filter)) queryParams.Add($"filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrEmpty(orderBy)) queryParams.Add($"orderBy={Uri.EscapeDataString(orderBy)}");
        if (pageSize.HasValue) queryParams.Add($"pageSize={pageSize.Value}");
        if (!string.IsNullOrEmpty(pageToken)) queryParams.Add($"pageToken={Uri.EscapeDataString(pageToken)}");

        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(content);
    }

    /// <summary>
    /// Forward an MCP JSON-RPC request to a discovered endpoint.
    /// Injects bearer token if available.
    /// </summary>
    public async Task<string> ProxyMcpCallAsync(string targetUrl, JsonNode mcpRequest, string? bearerToken = null)
    {
        var client = _httpClientFactory.CreateClient("mcp-proxy");
        var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
        request.Content = new StringContent(mcpRequest.ToJsonString(), Encoding.UTF8, "application/json");

        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        }

        var response = await client.SendAsync(request);

        // If 401/403, caller should trigger elicitation
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return string.Empty; // Signal auth needed
        }

        // Return error details for non-success responses instead of throwing
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            var errorJson = new System.Text.Json.Nodes.JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["code"] = (int)response.StatusCode,
                    ["message"] = $"Target returned {(int)response.StatusCode} {response.StatusCode}",
                    ["data"] = errorBody.Length > 500 ? errorBody[..500] : errorBody
                },
                ["id"] = 1
            };
            return errorJson.ToJsonString();
        }

        return await response.Content.ReadAsStringAsync();
    }
}

#nullable enable
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArdDiscovery.Functions;

/// <summary>
/// GET /.well-known/ai-catalog.json — Publish this backend's capabilities
/// so ARD crawlers and other registries can discover them.
/// 
/// Per the ARD spec (§6.1), publishers advertise via well-known URI.
/// This endpoint advertises:
///   1. The ARD Discovery MCP server itself (so agents can find it)
///   2. The search registry endpoint (so other registries can federate)
/// 
/// Additional entries can be added via the "CatalogEntries" app setting
/// (JSON array of catalog entry objects).
/// </summary>
public class CatalogFunction
{
    [Function("AiCatalog")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = ".well-known/ai-catalog.json")] HttpRequestData req)
    {
        var backendBaseUrl = Environment.GetEnvironmentVariable("BackendBaseUrl")
            ?? "https://ard-discovery.azurewebsites.net";

        var publisherDomain = Environment.GetEnvironmentVariable("PublisherDomain")
            ?? "ard-discovery.azurewebsites.net";

        // Core entries: the MCP server and the registry search endpoint
        var catalog = new
        {
            specVersion = "1.0",
            host = new
            {
                displayName = "ARD Discovery",
                identifier = $"did:web:{publisherDomain}",
                documentationUrl = "https://agenticresourcediscovery.org/spec/"
            },
            entries = BuildEntries(backendBaseUrl, publisherDomain)
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");

        var json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteStringAsync(json);
        return response;
    }

    private static object[] BuildEntries(string baseUrl, string domain)
    {
        var entries = new System.Collections.Generic.List<object>
        {
            // Entry 1: The ARD Discovery MCP server
            new
            {
                identifier = $"urn:air:{domain}:mcp:ard-discovery",
                displayName = "ARD Discovery MCP Server",
                type = "application/mcp-server-card+json",
                url = $"{baseUrl}/mcp",
                capabilities = new[] { "search_capabilities", "explore_registry", "invoke_capability" },
                description = "MCP server that searches ARD registries to discover and invoke agentic resources. " +
                    "Supports federated search across multiple registries with tiered authentication " +
                    "(OBO, org tokens, per-user tokens).",
                representativeQueries = new[]
                {
                    "find me an MCP server for weather data",
                    "search for a flight booking agent",
                    "what A2A agents are available for finance",
                    "discover tools for document processing",
                    "invoke the weather tool I found"
                },
                tags = new[] { "ard", "discovery", "search", "proxy", "federation" },
                version = "1.0.0"
            },

            // Entry 2: The registry search endpoint (for federation)
            new
            {
                identifier = $"urn:air:{domain}:registry:search",
                displayName = "ARD Discovery Registry",
                type = "application/ai-registry+json",
                url = $"{baseUrl}/api/",
                description = "ARD-compliant search registry. Indexes capabilities from crawled " +
                    "ai-catalog.json manifests and exposes POST /search, POST /explore, and GET /agents endpoints.",
                tags = new[] { "registry", "search", "federation" }
            }
        };

        // Load additional entries from app settings (admin-managed)
        var extraJson = Environment.GetEnvironmentVariable("CatalogEntries");
        if (!string.IsNullOrEmpty(extraJson))
        {
            try
            {
                var extra = JsonSerializer.Deserialize<JsonElement[]>(extraJson);
                if (extra != null)
                {
                    foreach (var entry in extra)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch
            {
                // Invalid extra entries — skip silently
            }
        }

        return entries.ToArray();
    }
}

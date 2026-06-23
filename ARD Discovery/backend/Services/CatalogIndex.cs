#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ArdDiscovery.Services;

/// <summary>
/// Represents an indexed catalog entry ready for local search.
/// </summary>
public class IndexedEntry
{
    public string Identifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Description { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] RepresentativeQueries { get; set; } = Array.Empty<string>();
    public string? Version { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset IndexedAt { get; set; }
    public int TrustScore { get; set; }

    /// <summary>
    /// Compute a simple text relevance score against a query (0-100).
    /// In production, replace with vector embeddings via Azure AI Search.
    /// </summary>
    public int ScoreAgainst(string query)
    {
        if (string.IsNullOrEmpty(query)) return 50;

        var queryLower = query.ToLowerInvariant();
        var queryTerms = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var score = 0;

        // Check display name
        if (DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 40;

        // Check description
        foreach (var term in queryTerms)
        {
            if (Description.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 10;
        }

        // Check representative queries (best signal per ARD spec)
        foreach (var rq in RepresentativeQueries)
        {
            foreach (var term in queryTerms)
            {
                if (rq.Contains(term, StringComparison.OrdinalIgnoreCase))
                    score += 15;
            }
        }

        // Check capabilities
        foreach (var cap in Capabilities)
        {
            if (cap.Contains(query, StringComparison.OrdinalIgnoreCase))
                score += 20;
        }

        // Check tags
        foreach (var tag in Tags)
        {
            foreach (var term in queryTerms)
            {
                if (tag.Equals(term, StringComparison.OrdinalIgnoreCase))
                    score += 10;
            }
        }

        return Math.Min(score, 100);
    }
}

/// <summary>
/// Catalog crawler and search facade. Crawls ai-catalog.json from registered domains
/// and delegates storage/search to the configured ISearchIndex implementation.
/// 
/// Per ARD spec §6.2, web ingestion (crawling ai-catalog.json) is required.
/// </summary>
public class CatalogIndex
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CatalogIndex> _logger;
    private readonly ISearchIndex _index;
    private readonly TrustVerifier _trust;

    public CatalogIndex(IHttpClientFactory httpClientFactory, ILogger<CatalogIndex> logger,
        ISearchIndex index, TrustVerifier trust)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _index = index;
        _trust = trust;
    }

    /// <summary>
    /// Number of indexed entries.
    /// </summary>
    public int Count => _index.GetCountAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Crawl a domain's /.well-known/ai-catalog.json and index all entries.
    /// </summary>
    /// <summary>
    /// Crawl a domain's /.well-known/ai-catalog.json and index all entries.
    /// Trust score is computed per-domain and applied to all entries from that domain.
    /// </summary>
    public async Task CrawlAsync(string domain, int? overrideTrustScore = null)
    {
        var url = $"https://{domain}/.well-known/ai-catalog.json";
        _logger.LogInformation("Crawling {Url}", url);

        // Compute trust score for the domain (or use override from caller)
        var trustScore = overrideTrustScore ?? _trust.QuickScore($"https://{domain}");

        try
        {
            var client = _httpClientFactory.CreateClient("ard-registry");
            var response = await client.GetAsync(url);

            // Fall back to /api/.well-known/ path (Azure Functions route prefix)
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var fallbackUrl = $"https://{domain}/api/.well-known/ai-catalog.json";
                _logger.LogInformation("Retrying with fallback {Url}", fallbackUrl);
                response = await client.GetAsync(fallbackUrl);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to crawl {Url}: {Status}", url, response.StatusCode);
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var catalog = JsonNode.Parse(content);
            var entries = catalog?["entries"]?.AsArray();

            if (entries == null)
            {
                _logger.LogWarning("No entries found in {Url}", url);
                return;
            }

            var count = 0;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var indexed = ParseEntry(entry, domain);
                if (indexed != null)
                {
                    indexed.TrustScore = trustScore;
                    await _index.IndexEntryAsync(indexed);
                    count++;

                    // Recurse into nested catalogs (§4.1 — type: application/ai-catalog+json)
                    if (indexed.Type == "application/ai-catalog+json" && !string.IsNullOrEmpty(indexed.Url))
                    {
                        await CrawlCatalogUrlAsync(indexed.Url, domain, trustScore);
                    }
                }
            }

            _logger.LogInformation("Indexed {Count} entries from {Domain}", count, domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crawling {Url}", url);
        }
    }

    /// <summary>
    /// Crawl a specific catalog URL (for nested catalogs and non-well-known paths).
    /// </summary>
    public async Task CrawlCatalogUrlAsync(string catalogUrl, string sourceDomain, int trustScore = 0)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ard-registry");
            var response = await client.GetAsync(catalogUrl);
            if (!response.IsSuccessStatusCode) return;

            var content = await response.Content.ReadAsStringAsync();
            var catalog = JsonNode.Parse(content);
            var entries = catalog?["entries"]?.AsArray();
            if (entries == null) return;

            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var indexed = ParseEntry(entry, sourceDomain);
                if (indexed != null)
                {
                    indexed.TrustScore = trustScore;
                    await _index.IndexEntryAsync(indexed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error crawling nested catalog {Url}", catalogUrl);
        }
    }

    /// <summary>
    /// Search the index. Returns entries ranked by relevance.
    /// </summary>
    public async Task<List<IndexedEntry>> SearchAsync(string text, Dictionary<string, string[]>? filters = null, int pageSize = 10)
    {
        var results = await _index.SearchAsync(text, filters, pageSize);
        return results.Select(r => r.Entry).ToList();
    }

    /// <summary>
    /// Synchronous search wrapper for backward compatibility.
    /// </summary>
    public List<IndexedEntry> Search(string text, Dictionary<string, string[]>? filters = null, int pageSize = 10)
    {
        return SearchAsync(text, filters, pageSize).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Explore facets across the index.
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, int>>> ExploreAsync(
        string[] facetFields, string? text = null, int limit = 20)
    {
        return await _index.ExploreAsync(facetFields, text, limit);
    }

    /// <summary>
    /// Synchronous explore wrapper for backward compatibility.
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> Explore(
        string[] facetFields, string? text = null, int limit = 20)
    {
        return ExploreAsync(facetFields, text, limit).GetAwaiter().GetResult();
    }

    /// <summary>
    /// List all entries with optional filtering (deterministic order by identifier).
    /// </summary>
    public async Task<List<IndexedEntry>> ListAsync(string? filter = null, int pageSize = 20, int offset = 0)
    {
        return await _index.ListAsync(filter, pageSize, offset);
    }

    /// <summary>
    /// Synchronous list wrapper for backward compatibility.
    /// </summary>
    public List<IndexedEntry> List(string? filter = null, int pageSize = 20, int offset = 0)
    {
        return ListAsync(filter, pageSize, offset).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Parse a JSON catalog entry into an IndexedEntry.
    /// Rejects entries where the URN publisher doesn't match the crawled domain (security).
    /// </summary>
    private IndexedEntry? ParseEntry(JsonNode entry, string sourceDomain)
    {
        var identifier = entry["identifier"]?.GetValue<string>();
        if (string.IsNullOrEmpty(identifier)) return null;

        // Extract publisher from URN: urn:air:<publisher>:<namespace>:<name>
        var publisher = sourceDomain;
        var urnParts = identifier.Split(':');
        if (urnParts.Length >= 3 && urnParts[0] == "urn" && (urnParts[1] == "air" || urnParts[1] == "ai"))
        {
            publisher = urnParts[2];

            // Security: reject entries where URN publisher doesn't match the source domain.
            // Allows exact match or subdomain (e.g., "api.contoso.com" matches source "contoso.com").
            if (!publisher.Equals(sourceDomain, StringComparison.OrdinalIgnoreCase)
                && !publisher.EndsWith($".{sourceDomain}", StringComparison.OrdinalIgnoreCase)
                && !sourceDomain.EndsWith($".{publisher}", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Rejecting entry {Identifier}: URN publisher '{Publisher}' doesn't match crawl source '{Domain}'",
                    identifier, publisher, sourceDomain);
                return null;
            }
        }

        return new IndexedEntry
        {
            Identifier = identifier,
            DisplayName = entry["displayName"]?.GetValue<string>() ?? identifier,
            Type = entry["type"]?.GetValue<string>() ?? "unknown",
            Url = entry["url"]?.GetValue<string>(),
            Description = entry["description"]?.GetValue<string>() ?? string.Empty,
            Capabilities = ParseStringArray(entry["capabilities"]),
            Tags = ParseStringArray(entry["tags"]),
            RepresentativeQueries = ParseStringArray(entry["representativeQueries"]),
            Version = entry["version"]?.GetValue<string>(),
            Publisher = publisher,
            Source = $"https://{sourceDomain}/.well-known/ai-catalog.json",
            IndexedAt = DateTimeOffset.UtcNow
        };
    }

    private static string[] ParseStringArray(JsonNode? node)
    {
        if (node is not JsonArray arr) return Array.Empty<string>();
        return arr
            .Select(n => n?.GetValue<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray()!;
    }
}

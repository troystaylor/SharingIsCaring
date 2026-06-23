#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;

namespace ArdDiscovery.Services;

/// <summary>
/// Azure AI Search-backed index with hybrid search (vector + keyword).
/// Vector embeddings generated from description + representativeQueries
/// via Azure OpenAI text-embedding-3-small.
/// </summary>
public class AiSearchIndex : ISearchIndex
{
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiSearchIndex> _logger;
    private readonly string _indexName;
    private readonly string? _embeddingEndpoint;
    private readonly string? _embeddingKey;
    private readonly string _embeddingModel;
    private const int VectorDimensions = 1536; // text-embedding-3-small

    public AiSearchIndex(IHttpClientFactory httpClientFactory, ILogger<AiSearchIndex> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var endpoint = Environment.GetEnvironmentVariable("AiSearchEndpoint")!;
        var apiKey = Environment.GetEnvironmentVariable("AiSearchApiKey")!;
        _indexName = Environment.GetEnvironmentVariable("AiSearchIndexName") ?? "ard-catalog-entries";
        _embeddingEndpoint = Environment.GetEnvironmentVariable("AiSearchEmbeddingEndpoint");
        _embeddingKey = Environment.GetEnvironmentVariable("AiSearchEmbeddingKey");
        _embeddingModel = Environment.GetEnvironmentVariable("AiSearchEmbeddingModel") ?? "text-embedding-3-small";

        var credential = new AzureKeyCredential(apiKey);
        _indexClient = new SearchIndexClient(new Uri(endpoint), credential);
        _searchClient = new SearchClient(new Uri(endpoint), _indexName, credential);

        EnsureIndexExistsAsync().GetAwaiter().GetResult();
    }

    public async Task IndexEntryAsync(IndexedEntry entry)
    {
        var doc = await ToSearchDocumentAsync(entry);
        await _searchClient.MergeOrUploadDocumentsAsync(new[] { doc });
    }

    public async Task IndexBatchAsync(IEnumerable<IndexedEntry> entries)
    {
        var docs = new List<SearchDocument>();
        foreach (var entry in entries)
        {
            docs.Add(await ToSearchDocumentAsync(entry));

            if (docs.Count >= 100)
            {
                await _searchClient.MergeOrUploadDocumentsAsync(docs);
                docs.Clear();
            }
        }
        if (docs.Count > 0)
            await _searchClient.MergeOrUploadDocumentsAsync(docs);
    }

    public async Task RemoveEntryAsync(string identifier)
    {
        try
        {
            await _searchClient.DeleteDocumentsAsync("identifier", new[] { identifier });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove {Id} from AI Search", identifier);
        }
    }

    public async Task<List<ScoredEntry>> SearchAsync(string text, Dictionary<string, string[]>? filters = null, int pageSize = 10)
    {
        var options = new SearchOptions
        {
            Size = pageSize,
            Select = { "identifier", "displayName", "type", "url", "description",
                       "capabilities", "tags", "representativeQueries", "version",
                       "publisher", "source", "indexedAt", "trustScore" },
            IncludeTotalCount = true
        };

        // Build filter string
        var filterParts = new List<string>();
        if (filters != null)
        {
            if (filters.TryGetValue("type", out var types) && types.Length > 0)
                filterParts.Add($"({string.Join(" or ", types.Select(t => $"type eq '{Escape(t)}'"))})");
            if (filters.TryGetValue("publisher", out var pubs) && pubs.Length > 0)
                filterParts.Add($"({string.Join(" or ", pubs.Select(p => $"publisher eq '{Escape(p)}'"))})");
            if (filters.TryGetValue("tags", out var tags) && tags.Length > 0)
                filterParts.Add($"({string.Join(" or ", tags.Select(t => $"tags/any(tag: tag eq '{Escape(t)}')"))})");
        }
        if (filterParts.Count > 0)
            options.Filter = string.Join(" and ", filterParts);

        // Hybrid search: vector + keyword
        float[]? vector = null;
        if (!string.IsNullOrEmpty(_embeddingEndpoint) && !string.IsNullOrEmpty(text))
        {
            vector = await GetEmbeddingAsync(text);
        }

        if (vector != null)
        {
            options.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        KNearestNeighborsCount = pageSize,
                        Fields = { "descriptionVector" }
                    }
                }
            };
        }

        var response = await _searchClient.SearchAsync<SearchDocument>(text, options);
        var results = new List<ScoredEntry>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var entry = FromSearchDocument(result.Document);
            results.Add(new ScoredEntry
            {
                Entry = entry,
                Score = result.Score ?? 0
            });
        }

        return results;
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> ExploreAsync(
        string[] facetFields, string? text = null, int limit = 20)
    {
        var options = new SearchOptions
        {
            Size = 0,
            IncludeTotalCount = true
        };

        foreach (var field in facetFields)
        {
            var facetField = field.ToLowerInvariant() switch
            {
                "type" => "type",
                "publisher" => "publisher",
                "tags" => "tags",
                _ => field
            };
            options.Facets.Add($"{facetField},count:{limit}");
        }

        var response = await _searchClient.SearchAsync<SearchDocument>(text ?? "*", options);
        var result = new Dictionary<string, Dictionary<string, int>>();

        foreach (var facet in response.Value.Facets)
        {
            var buckets = new Dictionary<string, int>();
            foreach (var value in facet.Value)
            {
                buckets[value.Value?.ToString() ?? ""] = (int)(value.Count ?? 0);
            }
            result[facet.Key] = buckets;
        }

        return result;
    }

    public async Task<List<IndexedEntry>> ListAsync(string? filter = null, int pageSize = 20, int offset = 0)
    {
        var options = new SearchOptions
        {
            Size = pageSize,
            Skip = offset,
            OrderBy = { "identifier asc" },
            Select = { "identifier", "displayName", "type", "url", "description",
                       "capabilities", "tags", "representativeQueries", "version",
                       "publisher", "source", "indexedAt", "trustScore" }
        };

        if (!string.IsNullOrEmpty(filter))
        {
            var parts = filter.Split('=', 2);
            if (parts.Length == 2)
                options.Filter = $"{parts[0].Trim()} eq '{Escape(parts[1].Trim())}'";
        }

        var response = await _searchClient.SearchAsync<SearchDocument>("*", options);
        var results = new List<IndexedEntry>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            results.Add(FromSearchDocument(result.Document));
        }

        return results;
    }

    public async Task<int> GetCountAsync()
    {
        var options = new SearchOptions { Size = 0, IncludeTotalCount = true };
        var response = await _searchClient.SearchAsync<SearchDocument>("*", options);
        return (int)(response.Value.TotalCount ?? 0);
    }

    // ====================================================================
    // Index Schema
    // ====================================================================

    private async Task EnsureIndexExistsAsync()
    {
        try
        {
            var index = new SearchIndex(_indexName)
            {
                Fields =
                {
                    new SimpleField("identifier", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
                    new SearchableField("displayName") { IsFilterable = true, IsSortable = true },
                    new SimpleField("type", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("url", SearchFieldDataType.String) { IsFilterable = false },
                    new SearchableField("description"),
                    new SimpleField("capabilities", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                    new SimpleField("tags", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true },
                    new SimpleField("representativeQueries", SearchFieldDataType.Collection(SearchFieldDataType.String)),
                    new SimpleField("version", SearchFieldDataType.String),
                    new SimpleField("publisher", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true, IsSortable = true },
                    new SimpleField("source", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("indexedAt", SearchFieldDataType.DateTimeOffset) { IsSortable = true },
                    new SimpleField("trustScore", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                    new SearchField("descriptionVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = VectorDimensions,
                        VectorSearchProfileName = "vector-profile"
                    }
                },
                VectorSearch = new VectorSearch
                {
                    Profiles = { new VectorSearchProfile("vector-profile", "hnsw-config") },
                    Algorithms = { new HnswAlgorithmConfiguration("hnsw-config") }
                }
            };

            await _indexClient.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("AI Search index '{Index}' ensured", _indexName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/update AI Search index '{Index}'", _indexName);
        }
    }

    // ====================================================================
    // Embedding
    // ====================================================================

    private async Task<float[]?> GetEmbeddingAsync(string text)
    {
        if (string.IsNullOrEmpty(_embeddingEndpoint) || string.IsNullOrEmpty(_embeddingKey))
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient("embedding");
            var request = new HttpRequestMessage(HttpMethod.Post, _embeddingEndpoint);
            request.Headers.Add("api-key", _embeddingKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { input = text, model = _embeddingModel }),
                Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body);
            var embedding = json.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get embedding for text");
            return null;
        }
    }

    // ====================================================================
    // Document Conversion
    // ====================================================================

    private async Task<SearchDocument> ToSearchDocumentAsync(IndexedEntry entry)
    {
        // Build embedding from description + representative queries
        var embeddingText = $"{entry.DisplayName}. {entry.Description}. " +
            string.Join(". ", entry.RepresentativeQueries);
        var vector = await GetEmbeddingAsync(embeddingText);

        var doc = new SearchDocument
        {
            ["identifier"] = SanitizeKey(entry.Identifier),
            ["displayName"] = entry.DisplayName,
            ["type"] = entry.Type,
            ["url"] = entry.Url ?? "",
            ["description"] = entry.Description,
            ["capabilities"] = entry.Capabilities,
            ["tags"] = entry.Tags,
            ["representativeQueries"] = entry.RepresentativeQueries,
            ["version"] = entry.Version ?? "",
            ["publisher"] = entry.Publisher,
            ["source"] = entry.Source,
            ["indexedAt"] = entry.IndexedAt,
            ["trustScore"] = 0
        };

        if (vector != null)
            doc["descriptionVector"] = vector;

        return doc;
    }

    private static IndexedEntry FromSearchDocument(SearchDocument doc)
    {
        return new IndexedEntry
        {
            Identifier = doc.GetString("identifier") ?? "",
            DisplayName = doc.GetString("displayName") ?? "",
            Type = doc.GetString("type") ?? "",
            Url = doc.GetString("url"),
            Description = doc.GetString("description") ?? "",
            Capabilities = GetStringArray(doc, "capabilities"),
            Tags = GetStringArray(doc, "tags"),
            RepresentativeQueries = GetStringArray(doc, "representativeQueries"),
            Version = doc.GetString("version"),
            Publisher = doc.GetString("publisher") ?? "",
            Source = doc.GetString("source") ?? "",
            IndexedAt = doc.GetDateTimeOffset("indexedAt") ?? DateTimeOffset.MinValue
        };
    }

    private static string[] GetStringArray(SearchDocument doc, string key)
    {
        if (doc.TryGetValue(key, out var val) && val is IEnumerable<object> arr)
            return arr.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();
        return Array.Empty<string>();
    }

    private static string SanitizeKey(string key)
    {
        // AI Search keys: letters, digits, dashes, underscores, equals
        return string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
    }

    private static string Escape(string value) => value.Replace("'", "''");
}

#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ArdDiscovery.Services;

/// <summary>
/// Scored search result wrapping an IndexedEntry with its relevance score.
/// </summary>
public class ScoredEntry
{
    public IndexedEntry Entry { get; set; } = new();
    public double Score { get; set; }
}

/// <summary>
/// Abstraction over the catalog search index. Implementations:
/// - TableStorageIndex: persists to Azure Table Storage, queries in-memory (small catalogs)
/// - AiSearchIndex: persists to Azure AI Search with vector embeddings (production scale)
/// </summary>
public interface ISearchIndex
{
    /// <summary>Index a single catalog entry.</summary>
    Task IndexEntryAsync(IndexedEntry entry);

    /// <summary>Index a batch of catalog entries.</summary>
    Task IndexBatchAsync(IEnumerable<IndexedEntry> entries);

    /// <summary>Remove an entry by identifier.</summary>
    Task RemoveEntryAsync(string identifier);

    /// <summary>Semantic + keyword search with optional filters.</summary>
    Task<List<ScoredEntry>> SearchAsync(string text, Dictionary<string, string[]>? filters = null, int pageSize = 10);

    /// <summary>Facet aggregation across the index.</summary>
    Task<Dictionary<string, Dictionary<string, int>>> ExploreAsync(
        string[] facetFields, string? text = null, int limit = 20);

    /// <summary>Deterministic listing with filter and pagination.</summary>
    Task<List<IndexedEntry>> ListAsync(string? filter = null, int pageSize = 20, int offset = 0);

    /// <summary>Total entry count in the index.</summary>
    Task<int> GetCountAsync();
}

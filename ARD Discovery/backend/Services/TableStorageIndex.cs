#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace ArdDiscovery.Services;

/// <summary>
/// Table Storage entity for persisted catalog entries.
/// </summary>
public class CatalogEntryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // publisher domain
    public string RowKey { get; set; } = string.Empty;       // sanitized identifier
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Identifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "[]";
    public string TagsJson { get; set; } = "[]";
    public string RepresentativeQueriesJson { get; set; } = "[]";
    public string? Version { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset IndexedAt { get; set; }
    public int TrustScore { get; set; }
}

/// <summary>
/// Table Storage-backed search index. Loads all entries into memory on startup
/// for in-process search. Suitable for catalogs with &lt;100K entries.
/// Falls back gracefully if AI Search is unavailable.
/// </summary>
public class TableStorageIndex : ISearchIndex
{
    private const string TableName = "ArdCatalogEntries";
    private readonly TableClient _table;
    private readonly ILogger<TableStorageIndex> _logger;
    private readonly ConcurrentDictionary<string, IndexedEntry> _cache = new();
    private bool _loaded;
    private bool _tableEnsured;

    public TableStorageIndex(ILogger<TableStorageIndex> logger)
    {
        _logger = logger;
        var tableUri = Environment.GetEnvironmentVariable("TokenStoreTableUri");
        TableServiceClient serviceClient;
        if (!string.IsNullOrEmpty(tableUri))
        {
            serviceClient = new TableServiceClient(new Uri(tableUri), new Azure.Identity.DefaultAzureCredential());
        }
        else
        {
            var connectionString = Environment.GetEnvironmentVariable("TokenStoreConnection")
                ?? "UseDevelopmentStorage=true";
            serviceClient = new TableServiceClient(connectionString);
        }
        _table = serviceClient.GetTableClient(TableName);
    }

    private async Task EnsureTableAsync()
    {
        if (_tableEnsured) return;
        await _table.CreateIfNotExistsAsync();
        _tableEnsured = true;
    }

    /// <summary>Ensure the in-memory cache is hydrated from Table Storage.</summary>
    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        try
        {
            await EnsureTableAsync();
            var count = 0;
            await foreach (var entity in _table.QueryAsync<CatalogEntryEntity>())
            {
                var entry = ToIndexedEntry(entity);
                _cache[entry.Identifier] = entry;
                count++;
            }
            _logger.LogInformation("TableStorageIndex loaded {Count} entries from Table Storage", count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load entries from Table Storage, starting empty");
        }

        _loaded = true;
    }

    public async Task IndexEntryAsync(IndexedEntry entry)
    {
        await EnsureLoadedAsync();
        _cache[entry.Identifier] = entry;

        var entity = ToEntity(entry);
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    public async Task IndexBatchAsync(IEnumerable<IndexedEntry> entries)
    {
        await EnsureLoadedAsync();

        var batch = new List<TableTransactionAction>();
        foreach (var entry in entries)
        {
            _cache[entry.Identifier] = entry;
            batch.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, ToEntity(entry)));

            // Table Storage batches max 100 per partition — flush when needed
            if (batch.Count >= 100)
            {
                try { await _table.SubmitTransactionAsync(batch); }
                catch { /* Fall back to individual upserts if partition mismatch */ await UpsertIndividuallyAsync(batch); }
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            try { await _table.SubmitTransactionAsync(batch); }
            catch { await UpsertIndividuallyAsync(batch); }
        }
    }

    public async Task RemoveEntryAsync(string identifier)
    {
        _cache.TryRemove(identifier, out _);

        if (_cache.Values.FirstOrDefault(e => e.Identifier == identifier) is not null)
            return;

        // Find and delete from Table Storage
        await EnsureTableAsync();
        var sanitized = SanitizeKey(identifier);
        await foreach (var entity in _table.QueryAsync<CatalogEntryEntity>(e => e.RowKey == sanitized))
        {
            await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
            break;
        }
    }

    public async Task<List<ScoredEntry>> SearchAsync(string text, Dictionary<string, string[]>? filters = null, int pageSize = 10)
    {
        await EnsureLoadedAsync();

        var results = _cache.Values.AsEnumerable();

        // Apply filters
        if (filters != null)
        {
            if (filters.TryGetValue("type", out var types) && types.Length > 0)
                results = results.Where(e => types.Contains(e.Type, StringComparer.OrdinalIgnoreCase));
            if (filters.TryGetValue("tags", out var tags) && tags.Length > 0)
                results = results.Where(e => e.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
            if (filters.TryGetValue("publisher", out var pubs) && pubs.Length > 0)
                results = results.Where(e => pubs.Contains(e.Publisher, StringComparer.OrdinalIgnoreCase));
        }

        return results
            .Select(e => new ScoredEntry { Entry = e, Score = e.ScoreAgainst(text) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(pageSize)
            .ToList();
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> ExploreAsync(
        string[] facetFields, string? text = null, int limit = 20)
    {
        await EnsureLoadedAsync();

        var entries = string.IsNullOrEmpty(text)
            ? _cache.Values
            : _cache.Values.Where(e => e.ScoreAgainst(text) > 0);

        var result = new Dictionary<string, Dictionary<string, int>>();
        foreach (var field in facetFields)
        {
            var buckets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var values = field.ToLowerInvariant() switch
                {
                    "type" => new[] { entry.Type },
                    "publisher" => new[] { entry.Publisher },
                    "tags" => entry.Tags,
                    _ => Array.Empty<string>()
                };
                foreach (var val in values)
                {
                    if (!string.IsNullOrEmpty(val))
                        buckets[val] = buckets.GetValueOrDefault(val) + 1;
                }
            }
            result[field] = buckets.OrderByDescending(kv => kv.Value).Take(limit)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        return result;
    }

    public async Task<List<IndexedEntry>> ListAsync(string? filter = null, int pageSize = 20, int offset = 0)
    {
        await EnsureLoadedAsync();

        var results = _cache.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
        {
            var parts = filter.Split('=', 2);
            if (parts.Length == 2)
            {
                var field = parts[0].Trim().ToLowerInvariant();
                var value = parts[1].Trim();
                results = field switch
                {
                    "type" => results.Where(e => e.Type.Equals(value, StringComparison.OrdinalIgnoreCase)),
                    "publisher" => results.Where(e => e.Publisher.Equals(value, StringComparison.OrdinalIgnoreCase)),
                    _ => results
                };
            }
        }

        return results.OrderBy(e => e.Identifier).Skip(offset).Take(pageSize).ToList();
    }

    public async Task<int> GetCountAsync()
    {
        await EnsureLoadedAsync();
        return _cache.Count;
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private async Task UpsertIndividuallyAsync(List<TableTransactionAction> batch)
    {
        foreach (var action in batch)
        {
            try { await _table.UpsertEntityAsync(action.Entity, TableUpdateMode.Replace); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to upsert entry"); }
        }
    }

    private static CatalogEntryEntity ToEntity(IndexedEntry entry)
    {
        return new CatalogEntryEntity
        {
            PartitionKey = SanitizeKey(entry.Publisher),
            RowKey = SanitizeKey(entry.Identifier),
            Identifier = entry.Identifier,
            DisplayName = entry.DisplayName,
            Type = entry.Type,
            Url = entry.Url,
            Description = entry.Description,
            CapabilitiesJson = JsonSerializer.Serialize(entry.Capabilities),
            TagsJson = JsonSerializer.Serialize(entry.Tags),
            RepresentativeQueriesJson = JsonSerializer.Serialize(entry.RepresentativeQueries),
            Version = entry.Version,
            Publisher = entry.Publisher,
            Source = entry.Source,
            IndexedAt = entry.IndexedAt,
            TrustScore = entry.TrustScore
        };
    }

    private static IndexedEntry ToIndexedEntry(CatalogEntryEntity entity)
    {
        return new IndexedEntry
        {
            Identifier = entity.Identifier,
            DisplayName = entity.DisplayName,
            Type = entity.Type,
            Url = entity.Url,
            Description = entity.Description,
            Capabilities = JsonSerializer.Deserialize<string[]>(entity.CapabilitiesJson) ?? Array.Empty<string>(),
            Tags = JsonSerializer.Deserialize<string[]>(entity.TagsJson) ?? Array.Empty<string>(),
            RepresentativeQueries = JsonSerializer.Deserialize<string[]>(entity.RepresentativeQueriesJson) ?? Array.Empty<string>(),
            Version = entity.Version,
            Publisher = entity.Publisher,
            Source = entity.Source,
            IndexedAt = entity.IndexedAt,
            TrustScore = entity.TrustScore
        };
    }

    private static string SanitizeKey(string key)
    {
        return key.Replace("/", "_").Replace("\\", "_").Replace("#", "_").Replace("?", "_");
    }
}

#nullable enable
using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace ArdDiscovery.Services;

/// <summary>
/// Represents crawl state for a single domain.
/// </summary>
public class CrawlStateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "crawl";
    public string RowKey { get; set; } = string.Empty; // domain
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTimeOffset LastCrawledAt { get; set; }
    public int EntryCount { get; set; }
    public string Status { get; set; } = "unknown"; // success, error, skipped
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Persists crawl state per domain to Table Storage.
/// Used by CrawlFunction to skip recently crawled domains and by health endpoint for status.
/// </summary>
public class CrawlStateStore
{
    private const string TableName = "ArdCrawlState";
    private readonly TableClient _table;

    private bool _tableEnsured;

    public CrawlStateStore()
    {
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

    /// <summary>
    /// Get the crawl state for a domain. Returns null if never crawled.
    /// </summary>
    public async Task<CrawlStateEntity?> GetStateAsync(string domain)
    {
        try
        {
            await EnsureTableAsync();
            var response = await _table.GetEntityAsync<CrawlStateEntity>("crawl", domain);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Record a successful crawl.
    /// </summary>
    public async Task RecordSuccessAsync(string domain, int entryCount)
    {
        await EnsureTableAsync();
        var entity = new CrawlStateEntity
        {
            RowKey = domain,
            LastCrawledAt = DateTimeOffset.UtcNow,
            EntryCount = entryCount,
            Status = "success",
            ErrorMessage = null
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    /// <summary>
    /// Record a failed crawl.
    /// </summary>
    public async Task RecordErrorAsync(string domain, string errorMessage)
    {
        await EnsureTableAsync();
        var entity = new CrawlStateEntity
        {
            RowKey = domain,
            LastCrawledAt = DateTimeOffset.UtcNow,
            EntryCount = 0,
            Status = "error",
            ErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    /// <summary>
    /// Check if a domain was crawled recently (within the specified window).
    /// </summary>
    public async Task<bool> WasCrawledRecentlyAsync(string domain, TimeSpan window)
    {
        var state = await GetStateAsync(domain);
        if (state == null) return false;
        return state.Status == "success" && (DateTimeOffset.UtcNow - state.LastCrawledAt) < window;
    }
}

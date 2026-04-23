using System.Text.Json;
using Microsoft.Agents.Storage;

namespace ServiceNowHandoff.ServiceNow;

/// <summary>
/// Bidirectional mapping between ServiceNow conversation IDs and MCS conversation IDs.
/// Persisted to IStorage for cross-request access (webhook handler needs this).
///
/// Uses a registry key ("active_sn_conversations") to track all active ServiceNow
/// conversation IDs so the polling service can enumerate them. The registry is
/// updated on every add/remove operation.
/// </summary>
public class ConversationMappingStore
{
    private readonly IStorage _storage;
    private readonly ILogger<ConversationMappingStore> _logger;
    private readonly SemaphoreSlim _registryLock = new(1, 1);

    private const string SnToMcsKeyPrefix = "sn_to_mcs_";
    private const string McsToSnKeyPrefix = "mcs_to_sn_";
    private const string RegistryKey = "active_sn_conversations";

    public ConversationMappingStore(IStorage storage, ILogger<ConversationMappingStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task AddMappingAsync(string snConversationId, string mcsConversationId)
    {
        // Store bidirectional mapping
        var data = new Dictionary<string, object>
        {
            [$"{SnToMcsKeyPrefix}{snConversationId}"] = mcsConversationId,
            [$"{McsToSnKeyPrefix}{mcsConversationId}"] = snConversationId
        };
        await _storage.WriteAsync(data);

        // Add to active conversations registry
        await UpdateRegistryAsync(snConversationId, add: true);

        _logger.LogInformation(
            "Stored conversation mapping: SN {SnId} <-> MCS {McsId}",
            snConversationId, mcsConversationId);
    }

    public async Task<string?> GetMcsConversationIdAsync(string snConversationId)
    {
        var key = $"{SnToMcsKeyPrefix}{snConversationId}";
        var data = await _storage.ReadAsync([key]);
        return data.TryGetValue(key, out var val) ? val as string : null;
    }

    public async Task<string?> GetSnConversationIdAsync(string mcsConversationId)
    {
        var key = $"{McsToSnKeyPrefix}{mcsConversationId}";
        var data = await _storage.ReadAsync([key]);
        return data.TryGetValue(key, out var val) ? val as string : null;
    }

    public async Task RemoveMappingByServiceNowIdAsync(string snConversationId)
    {
        var mcsId = await GetMcsConversationIdAsync(snConversationId);
        var keysToDelete = new List<string> { $"{SnToMcsKeyPrefix}{snConversationId}" };

        if (mcsId != null)
        {
            keysToDelete.Add($"{McsToSnKeyPrefix}{mcsId}");
        }

        await _storage.DeleteAsync(keysToDelete.ToArray());

        // Remove from active conversations registry
        await UpdateRegistryAsync(snConversationId, add: false);

        _logger.LogInformation("Removed conversation mapping for SN {SnId}", snConversationId);
    }

    public async Task RemoveMappingByMcsIdAsync(string mcsConversationId)
    {
        var snId = await GetSnConversationIdAsync(mcsConversationId);
        var keysToDelete = new List<string> { $"{McsToSnKeyPrefix}{mcsConversationId}" };

        if (snId != null)
        {
            keysToDelete.Add($"{SnToMcsKeyPrefix}{snId}");

            // Remove from active conversations registry
            await UpdateRegistryAsync(snId, add: false);
        }

        await _storage.DeleteAsync(keysToDelete.ToArray());
        _logger.LogInformation("Removed conversation mapping for MCS {McsId}", mcsConversationId);
    }

    /// <summary>
    /// Get all active ServiceNow conversation IDs (for polling service).
    /// </summary>
    public async Task<IReadOnlyList<(string SnId, string McsId)>> GetAllActiveMappingsAsync()
    {
        var activeIds = await ReadRegistryAsync();

        var result = new List<(string, string)>();
        foreach (var snId in activeIds)
        {
            var mcsId = await GetMcsConversationIdAsync(snId);
            if (mcsId != null)
            {
                result.Add((snId, mcsId));
            }
        }
        return result;
    }

    /// <summary>
    /// Add or remove a ServiceNow conversation ID from the active conversations registry.
    /// Thread-safe via semaphore since multiple handoffs could happen concurrently.
    /// </summary>
    private async Task UpdateRegistryAsync(string snConversationId, bool add)
    {
        await _registryLock.WaitAsync();
        try
        {
            var activeIds = await ReadRegistryAsync();

            if (add)
            {
                if (!activeIds.Contains(snConversationId))
                    activeIds.Add(snConversationId);
            }
            else
            {
                activeIds.Remove(snConversationId);
            }

            // Serialize as JSON string for IStorage compatibility
            await _storage.WriteAsync(new Dictionary<string, object>
            {
                [RegistryKey] = JsonSerializer.Serialize(activeIds)
            });
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private async Task<List<string>> ReadRegistryAsync()
    {
        var data = await _storage.ReadAsync([RegistryKey]);

        if (data.TryGetValue(RegistryKey, out var val))
        {
            if (val is string json)
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            if (val is List<string> list)
                return list;
        }

        return new List<string>();
    }
}

using System.Text.Json;
using Microsoft.Agents.Storage;

namespace ServiceNowHandoff.ServiceNow;

/// <summary>
/// Bidirectional mapping between ServiceNow conversation IDs and MCS conversation IDs.
/// Persisted to IStorage (Azure Blob Storage) for durability across restarts.
/// Uses a registry key to track active conversations for the polling service.
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
        var data = new Dictionary<string, object>
        {
            [$"{SnToMcsKeyPrefix}{snConversationId}"] = new MappingEntry { Value = mcsConversationId },
            [$"{McsToSnKeyPrefix}{mcsConversationId}"] = new MappingEntry { Value = snConversationId }
        };
        await _storage.WriteAsync(data);
        await UpdateRegistryAsync(snConversationId, add: true);

        _logger.LogInformation("Stored conversation mapping: SN {SnId} <-> MCS {McsId}",
            snConversationId, mcsConversationId);
    }

    public async Task<string?> GetMcsConversationIdAsync(string snConversationId)
    {
        var key = $"{SnToMcsKeyPrefix}{snConversationId}";
        var data = await _storage.ReadAsync([key]);
        return ExtractValue(data, key);
    }

    public async Task<string?> GetSnConversationIdAsync(string mcsConversationId)
    {
        var key = $"{McsToSnKeyPrefix}{mcsConversationId}";
        var data = await _storage.ReadAsync([key]);
        return ExtractValue(data, key);
    }

    public async Task RemoveMappingByServiceNowIdAsync(string snConversationId)
    {
        var mcsId = await GetMcsConversationIdAsync(snConversationId);
        var keysToDelete = new List<string> { $"{SnToMcsKeyPrefix}{snConversationId}" };
        if (mcsId != null) keysToDelete.Add($"{McsToSnKeyPrefix}{mcsId}");
        await _storage.DeleteAsync(keysToDelete.ToArray());
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
            await UpdateRegistryAsync(snId, add: false);
        }
        await _storage.DeleteAsync(keysToDelete.ToArray());
        _logger.LogInformation("Removed conversation mapping for MCS {McsId}", mcsConversationId);
    }

    public async Task<IReadOnlyList<(string SnId, string McsId)>> GetAllActiveMappingsAsync()
    {
        var activeIds = await ReadRegistryAsync();
        var result = new List<(string, string)>();
        foreach (var snId in activeIds)
        {
            var mcsId = await GetMcsConversationIdAsync(snId);
            if (mcsId != null) result.Add((snId, mcsId));
        }
        return result;
    }

    private async Task UpdateRegistryAsync(string snConversationId, bool add)
    {
        await _registryLock.WaitAsync();
        try
        {
            var activeIds = await ReadRegistryAsync();
            if (add) { if (!activeIds.Contains(snConversationId)) activeIds.Add(snConversationId); }
            else { activeIds.Remove(snConversationId); }
            await _storage.WriteAsync(new Dictionary<string, object>
            {
                [RegistryKey] = new MappingEntry { Value = JsonSerializer.Serialize(activeIds) }
            });
        }
        finally { _registryLock.Release(); }
    }

    private async Task<List<string>> ReadRegistryAsync()
    {
        var data = await _storage.ReadAsync([RegistryKey]);
        var val = ExtractValue(data, RegistryKey);
        if (!string.IsNullOrEmpty(val))
            return JsonSerializer.Deserialize<List<string>>(val) ?? new List<string>();
        return new List<string>();
    }

    private static string? ExtractValue(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var val)) return null;
        if (val is MappingEntry entry) return entry.Value;
        if (val is JsonElement je && je.TryGetProperty("Value", out var vp)) return vp.GetString();
        if (val is IDictionary<string, object> dict && dict.TryGetValue("Value", out var v)) return v?.ToString();
        return val?.ToString();
    }
}

/// <summary>Simple wrapper object for BlobsStorage compatibility.</summary>
public class MappingEntry
{
    public string Value { get; set; } = string.Empty;
}

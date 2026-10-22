using System.Collections.Concurrent;

namespace SalesforceMcp.Salesforce;

public sealed record ApiVersionInfo(string Version, string Label);

/// <summary>
/// Holds what is worth keeping between requests: the org's supported API versions and object descriptions.
/// </summary>
/// <remarks>
/// A singleton, because the services that fill it are created per request. Descriptions are stored as raw
/// UTF-8 JSON and parsed on each read. That keeps a shared, mutable <c>JsonNode</c> tree out of the cache,
/// which could not safely be attached to two parents or read from two threads.
/// Each replica has its own cache; nothing is shared between replicas.
/// </remarks>
public sealed class MetadataCache
{
    public sealed record VersionList(IReadOnlyList<ApiVersionInfo> Items, DateTimeOffset FetchedAt);

    public sealed record SchemaEntry(byte[] Json, DateTimeOffset? LastModified, DateTimeOffset FetchedAt);

    private readonly ConcurrentDictionary<string, SchemaEntry> _entries = new();
    private readonly Lock _gate = new();
    private VersionList? _versions;

    public VersionList? Versions
    {
        get => Volatile.Read(ref _versions);
        set => Volatile.Write(ref _versions, value);
    }

    public int Count => _entries.Count;

    public bool TryGet(string key, out SchemaEntry entry) => _entries.TryGetValue(key, out entry!);

    public void Put(string key, SchemaEntry entry, int maxEntries)
    {
        lock (_gate)
        {
            if (!_entries.ContainsKey(key))
            {
                while (_entries.Count >= maxEntries)
                {
                    var oldest = _entries.MinBy(e => e.Value.FetchedAt).Key;
                    _entries.TryRemove(oldest, out _);
                }
            }

            _entries[key] = entry;
        }
    }
}

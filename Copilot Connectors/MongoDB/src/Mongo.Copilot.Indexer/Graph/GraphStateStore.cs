using System.Text.Json;
using Mongo.Copilot.Core.Access;

namespace Mongo.Copilot.Indexer.Graph;

/// <summary>Per-collection crawl state.</summary>
public sealed class CollectionState
{
    /// <summary>Base64 change-stream resume token, or null before the first tail.</summary>
    public string? ResumeToken { get; set; }

    /// <summary>Set once the initial full crawl has completed for this collection.</summary>
    public bool InitialCrawlComplete { get; set; }

    public DateTimeOffset? LastCrawlCompletedUtc { get; set; }

    /// <summary>
    /// External item IDs published by the last completed crawl, used to find orphans on the
    /// next one. Microsoft Graph has no operation to list the items in a connection, so the
    /// indexer has to remember what it published.
    /// </summary>
    public List<string> Manifest { get; set; } = [];

    /// <summary>
    /// True when the manifest was dropped because the collection exceeded
    /// <see cref="IndexerOptions.MaxManifestItems"/>. Orphan reconciliation is skipped for
    /// such collections rather than run against partial data.
    /// </summary>
    public bool ManifestTruncated { get; set; }
}

/// <summary>State for one Graph connection.</summary>
public sealed class ConnectionState
{
    /// <summary>
    /// Fingerprint of the resolved citation URL configuration. Item URLs are written at
    /// crawl time, so a change here means published items carry stale links.
    /// </summary>
    public string? UrlFingerprint { get; set; }

    public Dictionary<string, CollectionState> Collections { get; set; } = [];

    public CollectionState For(string collection)
    {
        if (!Collections.TryGetValue(collection, out var state))
            Collections[collection] = state = new CollectionState();

        return state;
    }
}

/// <summary>
/// Persists indexer state as a reserved <c>externalItem</c> inside the connection it
/// describes.
/// </summary>
/// <remarks>
/// MongoDB is reached with a read-only role, so state cannot be written back there, and
/// <c>externalConnection</c> has no free-form metadata bag — its writable properties are
/// id, name, description, configuration, state, activitySettings, searchSettings and
/// contentCategory, none of which can hold a resume token. Storing state as an item keeps
/// the deployment free of an extra storage account. The item is ACL'd deny-everyone, so it
/// can never appear in anyone's search results.
/// </remarks>
public sealed class GraphStateStore(GraphClient graph, ILogger<GraphStateStore> logger)
{
    public const string StateItemId = "mongocopilotstate";

    public async Task<ConnectionState> LoadAsync(string connectionId, CancellationToken cancellationToken)
    {
        var item = await graph.GetAsync<StateItemResponse>(
            $"external/connections/{connectionId}/items/{StateItemId}", cancellationToken);

        var json = item?.Properties?.StateJson;

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogInformation("No prior state for {ConnectionId}; treating as first run.", connectionId);
            return new ConnectionState();
        }

        try
        {
            return JsonSerializer.Deserialize<ConnectionState>(json) ?? new ConnectionState();
        }
        catch (JsonException ex)
        {
            // Unreadable state is recoverable: a full re-crawl is correct and safe.
            logger.LogWarning(ex, "State for {ConnectionId} could not be parsed; starting fresh.", connectionId);
            return new ConnectionState();
        }
    }

    public async Task SaveAsync(string connectionId, ConnectionState state, CancellationToken cancellationToken)
    {
        var payload = new
        {
            acl = new[] { Acl(AclEntry.DenyEveryone()) },
            properties = new Dictionary<string, object?>
            {
                [ConnectionManager.StateProperty] = JsonSerializer.Serialize(state),
                ["title"] = "Indexer state",
                ["collection"] = "_state"
            }
        };

        await graph.SendAsync(
            HttpMethod.Put,
            $"external/connections/{connectionId}/items/{StateItemId}",
            payload,
            cancellationToken);
    }

    internal static object Acl(AclEntry entry) => new
    {
        type = entry.Type,
        value = entry.Value,
        accessType = entry.AccessType
    };

    private sealed class StateItemResponse
    {
        public StateItemProperties? Properties { get; set; }
    }

    private sealed class StateItemProperties
    {
        public string? StateJson { get; set; }
    }
}

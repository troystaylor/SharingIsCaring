using MongoDB.Bson;
using Mongo.Copilot.Core.Access;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Indexer.Graph;

/// <summary>Outcome of attempting to publish one document.</summary>
public enum PublishResult
{
    Published,

    /// <summary>ACL could not be resolved, so the document was deliberately not published.</summary>
    SkippedUnresolvableAcl,

    /// <summary>Access kind cannot be expressed as an index-time ACL.</summary>
    SkippedNonIndexableAccess
}

/// <summary>
/// Publishes projected MongoDB documents as Graph external items.
/// </summary>
public sealed class ItemPublisher(
    GraphClient graph,
    DocumentProjector projector,
    PrincipalResolver principals,
    ILogger<ItemPublisher> logger)
{
    /// <summary>
    /// Graph rejects items above 4 MB. Content is truncated rather than dropped so a long
    /// document still grounds and cites, just without its tail.
    /// </summary>
    private const int MaxContentBytes = 3 * 1024 * 1024;

    public async Task<PublishResult> PublishAsync(
        string connectionId,
        CollectionProfile profile,
        BsonDocument document,
        CancellationToken cancellationToken)
    {
        if (!AccessEvaluator.IsIndexable(profile.Access!))
            return PublishResult.SkippedNonIndexableAccess;

        var acl = AccessEvaluator.BuildAcl(profile, document);
        if (acl is null)
        {
            // Fail closed: an unresolvable audience means the item is not published at all.
            // A wrong grant here would be visible tenant-wide until a re-crawl corrected it.
            return PublishResult.SkippedUnresolvableAcl;
        }

        // Documents usually carry an email or a team name rather than an object ID, so the
        // raw values are resolved against the directory before they become an ACL.
        acl = await principals.ResolveAsync(acl, cancellationToken);
        if (acl is null)
            return PublishResult.SkippedUnresolvableAcl;

        var projected = projector.Project(profile, document);

        var properties = new Dictionary<string, object?>(projected.Properties)
        {
            ["collection"] = profile.Name
        };

        if (!string.IsNullOrWhiteSpace(projected.Title))
            properties["title"] = projected.Title;

        if (!string.IsNullOrWhiteSpace(projected.Url))
            properties["url"] = projected.Url;

        var payload = new
        {
            acl = acl.Select(GraphStateStore.Acl).ToArray(),
            properties,
            content = new
            {
                value = Truncate(projected.Content) ?? projected.Title ?? string.Empty,
                type = "text"
            }
        };

        await graph.SendAsync(
            HttpMethod.Put,
            $"external/connections/{connectionId}/items/{ItemId(profile, projected.Id)}",
            payload,
            cancellationToken);

        return PublishResult.Published;
    }

    public async Task DeleteAsync(
        string connectionId,
        CollectionProfile profile,
        string documentId,
        CancellationToken cancellationToken)
    {
        using var response = await graph.SendAsync(
            HttpMethod.Delete,
            $"external/connections/{connectionId}/items/{ItemId(profile, documentId)}",
            null,
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            logger.LogDebug("Delete for {Collection}/{Id} found no item; already removed.", profile.Name, documentId);
    }

    /// <summary>
    /// Item IDs must be unique within a connection and at most 128 characters. Grouped
    /// collections share a connection, so the collection name is part of the ID.
    /// </summary>
    public static string ItemId(CollectionProfile profile, string documentId)
    {
        var raw = $"{profile.Name}_{documentId}";
        var cleaned = new string([.. raw.Select(c => char.IsLetterOrDigit(c) ? c : '_')]);

        if (cleaned.Length <= 128)
            return cleaned;

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..16];

        return cleaned[..111] + "_" + hash;
    }

    /// <summary>Deletes an item by its already-computed external item ID.</summary>
    public async Task DeleteByItemIdAsync(
        string connectionId,
        string itemId,
        CancellationToken cancellationToken)
    {
        using var response = await graph.SendAsync(
            HttpMethod.Delete,
            $"external/connections/{connectionId}/items/{itemId}",
            null,
            cancellationToken,
            allowNotFound: true);
    }

    private static string? Truncate(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        if (System.Text.Encoding.UTF8.GetByteCount(content) <= MaxContentBytes)
            return content;

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var slice = System.Text.Encoding.UTF8.GetString(bytes, 0, MaxContentBytes);

        return slice + "\n\n[Content truncated for indexing.]";
    }
}

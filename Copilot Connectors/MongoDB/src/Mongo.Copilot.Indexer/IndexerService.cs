using MongoDB.Bson;
using MongoDB.Driver;
using Mongo.Copilot.Core.Citations;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;
using Mongo.Copilot.Indexer.Graph;

namespace Mongo.Copilot.Indexer;

public sealed class IndexerOptions
{
    /// <summary>Documents read per batch during the full crawl.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Save state at most this often while tailing, to limit Graph writes.</summary>
    public int SaveStateEverySeconds { get; set; } = 30;

    /// <summary>Prefix for generated Graph connection display names.</summary>
    public string ConnectionNamePrefix { get; set; } = "MongoDB";

    /// <summary>
    /// Largest manifest kept per collection. The manifest lives inside the reserved state
    /// item, which Graph caps at 4 MB, so very large collections forgo orphan reconciliation
    /// rather than risk a state write that cannot succeed.
    /// </summary>
    public int MaxManifestItems { get; set; } = 20_000;

    /// <summary>
    /// Refuse to delete more than this share of the previous manifest in one pass. A
    /// connectivity failure part-way through a crawl looks exactly like a mass deletion, and
    /// wrongly emptying the index is far more damaging than leaving stale items in it.
    /// </summary>
    public double MaxOrphanDeleteRatio { get; set; } = 0.25;
}

/// <summary>
/// Crawls each indexed collection, then tails its change stream. Runs as a single
/// always-on service because the tail holds an open cursor rather than polling.
/// </summary>
public sealed class IndexerService(
    ProfileStore profiles,
    MongoConnectionFactory mongo,
    CitationUrlResolver citations,
    ConnectionManager connections,
    GraphStateStore stateStore,
    ItemPublisher publisher,
    IndexerHeartbeat heartbeat,
    Microsoft.Extensions.Options.IOptions<IndexerOptions> options,
    ILogger<IndexerService> logger) : BackgroundService
{
    private readonly IndexerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var groups = profiles.IndexedByConnection().ToList();

        if (groups.Count == 0)
        {
            logger.LogWarning("No collections are configured with Mode Indexed or Both; nothing to do.");
            return;
        }

        logger.LogInformation(
            "Indexing {Collections} collection(s) across {Connections} Graph connection(s). " +
            "Detected provider: {Provider}.",
            profiles.Indexed.Count(), groups.Count, profiles.Provider);

        if (profiles.Provider == MongoProvider.AzureDocumentDb)
        {
            // Documented DocumentDB constraints that change how the tail behaves. Surfacing
            // them once at startup saves diagnosing a re-crawl as a bug later.
            logger.LogInformation(
                "Azure DocumentDB: change events are retained in a 400 MB active log, so a resume " +
                "token can expire under sustained write load and force a re-crawl. Multi-shard " +
                "clusters require change streams to be enabled via support request, and changing " +
                "cluster topology invalidates resumability.");
        }

        // Each connection is independent, so a failure in one does not stall the others.
        var tasks = groups.Select(group => RunConnectionAsync(
            ConnectionManager.SanitizeConnectionId(group.Key),
            group.Key,
            [.. group],
            stoppingToken));

        await Task.WhenAll(tasks);
    }

    private async Task RunConnectionAsync(
        string connectionId,
        string groupKey,
        IReadOnlyList<CollectionProfile> group,
        CancellationToken cancellationToken)
    {
        try
        {
            await connections.EnsureConnectionAsync(
                connectionId,
                $"{_options.ConnectionNamePrefix} — {groupKey}",
                group,
                cancellationToken);

            var state = await stateStore.LoadAsync(connectionId, cancellationToken);

            // Item URLs are written at crawl time, so a change to the citation configuration
            // leaves published items pointing at the old location. Re-crawl rather than
            // silently serving stale links.
            var fingerprint = citations.Fingerprint(group);
            if (state.UrlFingerprint is not null && state.UrlFingerprint != fingerprint)
            {
                logger.LogWarning(
                    "Citation URL configuration changed for {ConnectionId}; scheduling a full re-crawl.",
                    connectionId);

                foreach (var collection in state.Collections.Values)
                {
                    collection.InitialCrawlComplete = false;
                    collection.ResumeToken = null;
                }
            }

            state.UrlFingerprint = fingerprint;
            await stateStore.SaveAsync(connectionId, state, cancellationToken);

            foreach (var profile in group)
            {
                if (!state.For(profile.Name).InitialCrawlComplete)
                    await CrawlAsync(connectionId, profile, state, cancellationToken);
            }

            await Task.WhenAll(group.Select(p => TailAsync(connectionId, p, state, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Indexer for {ConnectionId} stopping.", connectionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Indexer for {ConnectionId} failed.", connectionId);
            throw;
        }
    }

    private async Task CrawlAsync(
        string connectionId,
        CollectionProfile profile,
        ConnectionState state,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting full crawl of {Collection}.", profile.Name);

        // Capture a resume point *before* reading, so changes made during the crawl are
        // replayed by the tail rather than lost between the two phases.
        var collection = mongo.Collection(profile);
        var resumeToken = await CaptureResumeTokenAsync(collection, cancellationToken);
        var published = 0;
        var skippedAcl = 0;
        var skippedAccess = 0;

        // Item IDs seen by this crawl, diffed against the previous manifest afterwards to
        // find items whose source documents have since disappeared.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var manifestOverflowed = false;

        using var cursor = await collection
            .Find(Builders<BsonDocument>.Filter.Empty, new FindOptions { BatchSize = _options.BatchSize })
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await publisher.PublishAsync(connectionId, profile, document, cancellationToken);

                switch (result)
                {
                    case PublishResult.Published:
                        published++;

                        if (!manifestOverflowed)
                        {
                            if (seen.Count >= _options.MaxManifestItems)
                                manifestOverflowed = true;
                            else if (DocumentIdOf(profile, document) is { } id)
                                seen.Add(ItemPublisher.ItemId(profile, id));
                        }
                        break;

                    case PublishResult.SkippedUnresolvableAcl:
                        skippedAcl++;

                        // A document that was published by an earlier crawl and has since lost
                        // its audience must be withdrawn, not merely skipped. Leaving it would
                        // keep it visible in the index under its previous ACL. The delete
                        // tolerates a missing item, so a first crawl costs nothing here.
                        await WithdrawAsync(connectionId, profile, document, cancellationToken);
                        break;

                    case PublishResult.SkippedNonIndexableAccess:
                        skippedAccess++;
                        break;
                }
            }

            logger.LogInformation("{Collection}: {Published} items published so far.", profile.Name, published);
            heartbeat.Beat($"crawling {profile.Name}");
        }

        if (skippedAcl > 0)
        {
            logger.LogWarning(
                "{Collection}: {Skipped} documents were not published because their access field " +
                "could not be resolved to an Entra identity; any previously indexed copies were withdrawn.",
                profile.Name, skippedAcl);
        }

        if (skippedAccess > 0)
        {
            logger.LogWarning(
                "{Collection}: {Skipped} documents were skipped because access kind " +
                "'{Kind}' cannot be expressed as an index-time ACL. Use Mode 'Live' for this collection.",
                profile.Name, skippedAccess, profile.Access!.Kind);
        }

        var collectionState = state.For(profile.Name);

        await ReconcileOrphansAsync(connectionId, profile, collectionState, seen, manifestOverflowed, cancellationToken);

        collectionState.InitialCrawlComplete = true;
        collectionState.LastCrawlCompletedUtc = DateTimeOffset.UtcNow;
        collectionState.ResumeToken ??= resumeToken;

        await stateStore.SaveAsync(connectionId, state, cancellationToken);

        logger.LogInformation(
            "Full crawl of {Collection} complete: {Published} published, {Skipped} skipped.",
            profile.Name, published, skippedAcl + skippedAccess);
    }

    /// <summary>
    /// Removes items published by a previous crawl whose source documents are gone.
    /// </summary>
    /// <remarks>
    /// Deletes that happen while the tail is running are handled by the change stream. This
    /// covers the gap where a document was removed while the indexer was down and the resume
    /// token expired before it came back, so the event was never seen.
    /// </remarks>
    private async Task ReconcileOrphansAsync(
        string connectionId,
        CollectionProfile profile,
        CollectionState collectionState,
        HashSet<string> seen,
        bool overflowed,
        CancellationToken cancellationToken)
    {
        if (overflowed)
        {
            logger.LogWarning(
                "{Collection} exceeded the {Max}-item manifest limit, so orphaned items cannot be " +
                "detected. Deletes seen by the change stream are still applied. Raise " +
                "Indexer:MaxManifestItems if the state item can carry it.",
                profile.Name, _options.MaxManifestItems);

            collectionState.Manifest = [];
            collectionState.ManifestTruncated = true;
            return;
        }

        var previous = collectionState.Manifest;
        var plan = OrphanReconciler.Plan(previous, seen, _options.MaxOrphanDeleteRatio);

        switch (plan.Action)
        {
            case OrphanAction.RecordBaseline:
                logger.LogInformation(
                    "{Collection}: recorded a baseline manifest of {Count} items.", profile.Name, seen.Count);
                break;

            case OrphanAction.RefuseAboveThreshold:
                logger.LogError(
                    "{Collection}: {Orphans} of {Previous} items ({Percent:P0}) would be deleted, above " +
                    "the {Limit:P0} safety threshold. Nothing was deleted and the manifest was left " +
                    "unchanged. Re-run once the source is known good, or raise " +
                    "Indexer:MaxOrphanDeleteRatio if this shrinkage is expected.",
                    profile.Name, plan.Orphans.Count, previous.Count, plan.Ratio, _options.MaxOrphanDeleteRatio);
                return;

            case OrphanAction.Delete:
            {
                logger.LogInformation(
                    "{Collection}: removing {Count} orphaned item(s) from the index.",
                    profile.Name, plan.Orphans.Count);

                var removed = 0;
                foreach (var itemId in plan.Orphans)
                {
                    try
                    {
                        await publisher.DeleteByItemIdAsync(connectionId, itemId, cancellationToken);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        // Keep a failed deletion in the manifest so the next crawl retries it.
                        logger.LogWarning(ex, "Failed to delete orphaned item {ItemId}.", itemId);
                        seen.Add(itemId);
                    }
                }

                logger.LogInformation("{Collection}: removed {Removed} orphan(s).", profile.Name, removed);
                break;
            }
        }

        collectionState.Manifest = [.. seen];
        collectionState.ManifestTruncated = false;
    }

    /// <summary>Reads the raw id field from a document, or null when it is absent.</summary>
    private static string? DocumentIdOf(CollectionProfile profile, BsonDocument document)
    {
        var value = BsonValues.ReadPath(document, profile.IdField);
        return value is null || value.IsBsonNull ? null : BsonValues.ToPlainString(value);
    }

    /// <summary>
    /// Opens a change stream briefly to capture the current resume point before a full crawl
    /// begins. Without this, a document changed during the crawl could be missed: the crawl
    /// might read it in its old form while the tail starts after the change was emitted.
    /// </summary>
    private async Task<string?> CaptureResumeTokenAsync(
        IMongoCollection<BsonDocument> collection,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new ChangeStreamOptions { MaxAwaitTime = TimeSpan.FromSeconds(2) };
            using var cursor = await collection.WatchAsync(options, cancellationToken);

            // One iteration returns an empty batch after MaxAwaitTime and populates the
            // post-batch resume token.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await cursor.MoveNextAsync(timeout.Token);

            return cursor.GetResumeToken()?.ToJson();
        }
        catch (Exception ex)
        {
            // Not fatal: the tail will start from "now" instead, at the cost of possibly
            // missing changes made during the crawl until the next full re-crawl.
            logger.LogWarning(ex,
                "Could not capture a resume token before crawling {Collection}; " +
                "the change stream will start from the current time.", collection.CollectionNamespace.CollectionName);

            return null;
        }
    }

    /// <summary>
    /// Removes an item whose source document is no longer publishable, identified from the
    /// document itself rather than a change event.
    /// </summary>
    private async Task WithdrawAsync(
        string connectionId,
        CollectionProfile profile,
        BsonDocument document,
        CancellationToken cancellationToken)
    {
        var id = BsonValues.ReadPath(document, profile.IdField);
        if (id is null || id.IsBsonNull)
            return;

        await publisher.DeleteAsync(connectionId, profile, BsonValues.ToPlainString(id), cancellationToken);
    }

    private async Task TailAsync(
        string connectionId,
        CollectionProfile profile,
        ConnectionState state,
        CancellationToken cancellationToken)
    {
        var collection = mongo.Collection(profile);
        var lastSave = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var collectionState = state.For(profile.Name);

            var options = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
                BatchSize = _options.BatchSize
            };

            if (collectionState.ResumeToken is { Length: > 0 } token)
                options.ResumeAfter = BsonDocument.Parse(token);

            try
            {
                using var cursor = await collection.WatchAsync(options, cancellationToken);

                logger.LogInformation("Tailing change stream for {Collection}.", profile.Name);

                while (await cursor.MoveNextAsync(cancellationToken))
                {
                    // Beat on every batch, including the empty ones a quiet collection returns,
                    // so silence is distinguishable from a stall.
                    heartbeat.Beat($"tailing {profile.Name}");

                    foreach (var change in cursor.Current)
                    {
                        await ApplyChangeAsync(connectionId, profile, change, collectionState, cancellationToken);
                        collectionState.ResumeToken = change.ResumeToken?.ToJson();
                    }

                    // Batching state writes keeps a busy collection from generating a Graph
                    // write per event, while still bounding replay after a restart.
                    if (DateTimeOffset.UtcNow - lastSave > TimeSpan.FromSeconds(_options.SaveStateEverySeconds))
                    {
                        await stateStore.SaveAsync(connectionId, state, cancellationToken);
                        lastSave = DateTimeOffset.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await stateStore.SaveAsync(connectionId, state, CancellationToken.None);
                return;
            }
            catch (MongoCommandException ex) when (IsInvalidResumeToken(ex))
            {
                // On MongoDB the oplog rolled past the token; on Azure DocumentDB the 400 MB
                // active change log rotated past it. Either way the delta is unrecoverable
                // and a full re-crawl is the only correct response.
                logger.LogWarning(
                    "Resume token for {Collection} is no longer valid (code {Code}); re-crawling.",
                    profile.Name, ex.Code);

                collectionState.ResumeToken = null;
                collectionState.InitialCrawlComplete = false;

                await CrawlAsync(connectionId, profile, state, cancellationToken);
            }
            catch (MongoCommandException ex) when (ex.Code == NamespaceNotFound)
            {
                // Azure DocumentDB reports a dropped collection as NamespaceNotFound rather
                // than emitting a drop event, so this is the only signal available there.
                logger.LogWarning(
                    "{Collection} no longer exists upstream; its indexed items are now orphaned. " +
                    "Retrying in 5 minutes in case this is a transient rename.", profile.Name);

                await stateStore.SaveAsync(connectionId, state, cancellationToken);
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
            catch (Exception ex) when (IsRecoverableCursorFailure(ex))
            {
                // Azure DocumentDB requires the cursor to be reinitialized after a failover,
                // while the resume token stays valid. This is routine, not an error.
                logger.LogInformation(
                    "Change stream cursor for {Collection} was interrupted ({Reason}); reopening from the stored token.",
                    profile.Name, ex.GetType().Name);

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Change stream for {Collection} failed; retrying in 30s.", profile.Name);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task ApplyChangeAsync(
        string connectionId,
        CollectionProfile profile,
        ChangeStreamDocument<BsonDocument> change,
        CollectionState collectionState,
        CancellationToken cancellationToken)
    {
        switch (change.OperationType)
        {
            case ChangeStreamOperationType.Insert:
            case ChangeStreamOperationType.Update:
            case ChangeStreamOperationType.Replace:
                if (change.FullDocument is { } document)
                {
                    var result = await publisher.PublishAsync(connectionId, profile, document, cancellationToken);

                    if (result == PublishResult.SkippedUnresolvableAcl)
                    {
                        // A document whose ACL became unresolvable must be withdrawn, not left
                        // behind at its previous audience.
                        await DeleteByKeyAsync(connectionId, profile, change, collectionState, cancellationToken);
                    }
                    else if (result == PublishResult.Published
                        && collectionState.Manifest.Count > 0
                        && DocumentIdOf(profile, document) is { } newId)
                    {
                        // Track newly published items so a crawl interrupted before its next
                        // pass still has an accurate picture of what is in the index.
                        var itemId = ItemPublisher.ItemId(profile, newId);
                        if (!collectionState.Manifest.Contains(itemId))
                            collectionState.Manifest.Add(itemId);
                    }
                }
                break;

            case ChangeStreamOperationType.Delete:
                await DeleteByKeyAsync(connectionId, profile, change, collectionState, cancellationToken);
                break;

            case ChangeStreamOperationType.Drop:
            case ChangeStreamOperationType.DropDatabase:
                logger.LogWarning(
                    "{Collection} was dropped upstream; indexed items are now orphaned.", profile.Name);
                break;
        }
    }

    /// <summary>
    /// Deletes use only <c>documentKey</c>, which is all that a delete event carries unless
    /// pre-images are enabled. That is sufficient here, so pre-images are not required.
    /// </summary>
    private async Task DeleteByKeyAsync(
        string connectionId,
        CollectionProfile profile,
        ChangeStreamDocument<BsonDocument> change,
        CollectionState collectionState,
        CancellationToken cancellationToken)
    {
        var key = change.DocumentKey;
        if (key is null || !key.TryGetValue("_id", out var id))
            return;

        var documentId = BsonValues.ToPlainString(id);
        await publisher.DeleteAsync(connectionId, profile, documentId, cancellationToken);

        // Drop it from the manifest as well. Otherwise the next full crawl would see every
        // legitimately deleted document as an orphan, and enough of them would trip the
        // safety threshold and block reconciliation entirely.
        if (collectionState.Manifest.Count > 0)
            collectionState.Manifest.Remove(ItemPublisher.ItemId(profile, documentId));
    }

    private const int NamespaceNotFound = 26;

    /// <summary>
    /// Detects a resume token the server can no longer honour.
    /// </summary>
    /// <remarks>
    /// 286 ChangeStreamHistoryLost, 280 ChangeStreamFatalError, 136 CappedPositionLost.
    /// Azure DocumentDB rotates a 400 MB active change log rather than an oplog and does not
    /// document its error codes, so the message is also inspected as a fallback.
    /// </remarks>
    private static bool IsInvalidResumeToken(MongoCommandException ex)
    {
        if (ex.Code is 286 or 280 or 136)
            return true;

        var message = ex.Message;

        return message.Contains("resume token", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("no longer", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Transient cursor failures where the stored resume token remains usable, so the stream
    /// should be reopened rather than re-crawled. Azure DocumentDB failovers land here.
    /// </summary>
    private static bool IsRecoverableCursorFailure(Exception ex) => ex switch
    {
        MongoConnectionException => true,
        MongoNotPrimaryException => true,
        MongoNodeIsRecoveringException => true,
        MongoCursorNotFoundException => true,
        MongoExecutionTimeoutException => true,
        MongoCommandException command => command.Code is 6 or 89 or 91 or 189 or 262 or 9001 or 10107 or 11602 or 13435 or 13436,
        _ => false
    };
}



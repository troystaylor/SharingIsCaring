namespace Mongo.Copilot.Indexer;

/// <summary>What a crawl should do about items missing from the current pass.</summary>
public enum OrphanAction
{
    /// <summary>No previous manifest; record a baseline and delete nothing.</summary>
    RecordBaseline,

    /// <summary>Nothing is missing.</summary>
    NothingToDo,

    /// <summary>Delete the listed items.</summary>
    Delete,

    /// <summary>Too much would be deleted; leave the index and manifest untouched.</summary>
    RefuseAboveThreshold
}

public readonly record struct OrphanPlan(OrphanAction Action, IReadOnlyList<string> Orphans, double Ratio);

/// <summary>
/// Decides whether items absent from a crawl should be deleted from the index.
/// </summary>
/// <remarks>
/// Kept free of I/O so the safety threshold — the part that protects against wiping an index
/// after a partial crawl — can be tested directly.
/// </remarks>
public static class OrphanReconciler
{
    public static OrphanPlan Plan(
        IReadOnlyCollection<string> previousManifest,
        ISet<string> seenThisCrawl,
        double maxDeleteRatio)
    {
        if (previousManifest.Count == 0)
            return new OrphanPlan(OrphanAction.RecordBaseline, [], 0);

        var orphans = previousManifest.Where(id => !seenThisCrawl.Contains(id)).ToList();

        if (orphans.Count == 0)
            return new OrphanPlan(OrphanAction.NothingToDo, [], 0);

        var ratio = (double)orphans.Count / previousManifest.Count;

        // A crawl cut short by a connectivity failure is indistinguishable from a mass
        // deletion. Leaving stale items is recoverable; emptying the index is not.
        return ratio > maxDeleteRatio
            ? new OrphanPlan(OrphanAction.RefuseAboveThreshold, orphans, ratio)
            : new OrphanPlan(OrphanAction.Delete, orphans, ratio);
    }
}

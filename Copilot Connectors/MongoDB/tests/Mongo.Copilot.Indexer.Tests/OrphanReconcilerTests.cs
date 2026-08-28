using Mongo.Copilot.Core.Access;
using Mongo.Copilot.Core.Profiles;
using Mongo.Copilot.Indexer;
using Mongo.Copilot.Indexer.Graph;

namespace Mongo.Copilot.Indexer.Tests;

public class OrphanReconcilerTests
{
    private static HashSet<string> Seen(params string[] ids) => new(ids, StringComparer.Ordinal);

    [Fact]
    public void A_first_crawl_records_a_baseline_and_deletes_nothing()
    {
        var plan = OrphanReconciler.Plan([], Seen("a", "b"), 0.25);

        Assert.Equal(OrphanAction.RecordBaseline, plan.Action);
        Assert.Empty(plan.Orphans);
    }

    [Fact]
    public void An_unchanged_collection_has_nothing_to_do()
    {
        var plan = OrphanReconciler.Plan(["a", "b"], Seen("a", "b"), 0.25);

        Assert.Equal(OrphanAction.NothingToDo, plan.Action);
    }

    [Fact]
    public void A_small_number_of_missing_items_is_deleted()
    {
        // One of five is 20%, under the 25% threshold.
        var plan = OrphanReconciler.Plan(["a", "b", "c", "d", "e"], Seen("a", "b", "c", "d"), 0.25);

        Assert.Equal(OrphanAction.Delete, plan.Action);
        Assert.Equal(["e"], plan.Orphans);
    }

    [Fact]
    public void A_mass_disappearance_is_refused()
    {
        // Three of four is 75% — far more likely a failed crawl than a real deletion.
        var plan = OrphanReconciler.Plan(["a", "b", "c", "d"], Seen("a"), 0.25);

        Assert.Equal(OrphanAction.RefuseAboveThreshold, plan.Action);
        Assert.Equal(0.75, plan.Ratio, 3);
    }

    [Fact]
    public void An_empty_crawl_of_a_populated_collection_is_refused()
    {
        // The catastrophic case: a connectivity failure returns no documents at all.
        var plan = OrphanReconciler.Plan(["a", "b", "c"], Seen(), 0.25);

        Assert.Equal(OrphanAction.RefuseAboveThreshold, plan.Action);
    }

    [Fact]
    public void The_threshold_is_exclusive_so_a_ratio_exactly_at_the_limit_proceeds()
    {
        var plan = OrphanReconciler.Plan(["a", "b", "c", "d"], Seen("a", "b", "c"), 0.25);

        Assert.Equal(OrphanAction.Delete, plan.Action);
    }

    [Fact]
    public void Raising_the_limit_permits_a_large_deletion()
    {
        var plan = OrphanReconciler.Plan(["a", "b", "c", "d"], Seen("a"), 1.0);

        Assert.Equal(OrphanAction.Delete, plan.Action);
        Assert.Equal(3, plan.Orphans.Count);
    }
}

public class ItemIdTests
{
    private static CollectionProfile Profile(string name) => new()
    {
        Name = name,
        Description = "x",
        Access = new AccessMapping { Kind = AccessKind.Everyone }
    };

    [Fact]
    public void Ids_are_namespaced_by_collection_so_a_shared_connection_cannot_collide()
    {
        Assert.NotEqual(
            ItemPublisher.ItemId(Profile("tickets"), "1"),
            ItemPublisher.ItemId(Profile("orders"), "1"));
    }

    [Fact]
    public void Ids_contain_only_characters_graph_accepts()
    {
        var id = ItemPublisher.ItemId(Profile("support_tickets"), "507f1f77bcf86cd799439011");

        Assert.Matches("^[A-Za-z0-9_]+$", id);
    }

    [Fact]
    public void Long_ids_are_hashed_to_stay_within_the_graph_limit()
    {
        var id = ItemPublisher.ItemId(Profile("c"), new string('x', 400));

        Assert.True(id.Length <= 128);
    }

    [Fact]
    public void Long_ids_remain_distinct_after_truncation()
    {
        var a = ItemPublisher.ItemId(Profile("c"), new string('x', 200) + "a");
        var b = ItemPublisher.ItemId(Profile("c"), new string('x', 200) + "b");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Ids_are_stable_across_calls()
    {
        Assert.Equal(
            ItemPublisher.ItemId(Profile("tickets"), "abc"),
            ItemPublisher.ItemId(Profile("tickets"), "abc"));
    }
}

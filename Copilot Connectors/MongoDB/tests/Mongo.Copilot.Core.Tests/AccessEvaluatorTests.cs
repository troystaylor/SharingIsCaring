using System.Security.Claims;
using MongoDB.Bson;
using Mongo.Copilot.Core.Access;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Tests;

public class AccessEvaluatorTests
{
    private static CollectionProfile Profile(AccessMapping access) => new()
    {
        Name = "tickets",
        Description = "Tickets",
        Access = access
    };

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    // ── Query-time filters ──────────────────────────────────────────────────

    [Fact]
    public void Everyone_produces_an_unfiltered_query()
    {
        var filter = AccessEvaluator.BuildFilterDocument(
            Profile(new AccessMapping { Kind = AccessKind.Everyone }), User());

        Assert.Equal(0, filter.ElementCount);
    }

    [Fact]
    public void Group_mapping_filters_to_the_callers_groups()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.EntraGroupField,
            Field = "teamGroupId"
        });

        var filter = AccessEvaluator.BuildFilterDocument(
            profile, User(("groups", "g1"), ("groups", "g2")));

        Assert.Equal(
            BsonDocument.Parse("{ teamGroupId: { $in: ['g1', 'g2'] } }"),
            filter);
    }

    [Fact]
    public void A_caller_with_no_groups_matches_nothing_rather_than_everything()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.EntraGroupField,
            Field = "teamGroupId"
        });

        var filter = AccessEvaluator.BuildFilterDocument(profile, User());

        // Fail closed: the absence of a claim must not widen the result set.
        Assert.Equal(BsonDocument.Parse("{ _id: { $exists: false } }"), filter);
    }

    [Fact]
    public void An_anonymous_caller_matches_nothing()
    {
        var profile = Profile(new AccessMapping { Kind = AccessKind.UpnField, Field = "ownerUpn" });

        Assert.Equal(
            BsonDocument.Parse("{ _id: { $exists: false } }"),
            AccessEvaluator.BuildFilterDocument(profile, null));
    }

    [Fact]
    public void Upn_mapping_filters_to_the_signed_in_user()
    {
        var profile = Profile(new AccessMapping { Kind = AccessKind.UpnField, Field = "ownerUpn" });

        var filter = AccessEvaluator.BuildFilterDocument(
            profile, User(("preferred_username", "sam@contoso.com")));

        Assert.Equal(BsonDocument.Parse("{ ownerUpn: 'sam@contoso.com' }"), filter);
    }

    [Fact]
    public void Tenant_mapping_uses_the_configured_claim()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.TenantField,
            Field = "tenantId",
            ClaimType = "tid"
        });

        var filter = AccessEvaluator.BuildFilterDocument(profile, User(("tid", "t-1")));

        Assert.Equal(BsonDocument.Parse("{ tenantId: 't-1' }"), filter);
    }

    // ── Index-time ACLs ─────────────────────────────────────────────────────

    [Fact]
    public void Everyone_grants_to_everyone()
    {
        var acl = AccessEvaluator.BuildAcl(
            Profile(new AccessMapping { Kind = AccessKind.Everyone }), new BsonDocument());

        var entry = Assert.Single(acl!);
        Assert.Equal(("everyone", "everyone", "grant"), (entry.Type, entry.Value, entry.AccessType));
    }

    [Fact]
    public void Group_field_grants_to_that_group()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.EntraGroupField,
            Field = "teamGroupId"
        });

        var acl = AccessEvaluator.BuildAcl(profile, new BsonDocument("teamGroupId", "group-1"));

        var entry = Assert.Single(acl!);
        Assert.Equal(("group", "group-1", "grant"), (entry.Type, entry.Value, entry.AccessType));
    }

    [Fact]
    public void An_array_access_field_grants_to_every_value()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.EntraGroupField,
            Field = "teamGroupIds"
        });

        var document = new BsonDocument("teamGroupIds", new BsonArray { "g1", "g2" });
        var acl = AccessEvaluator.BuildAcl(profile, document);

        Assert.Equal(2, acl!.Count);
    }

    [Fact]
    public void A_missing_access_field_yields_no_acl_so_the_document_is_skipped()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.EntraGroupField,
            Field = "teamGroupId"
        });

        // Null signals the publisher to skip: publishing with a wrong or default grant
        // would be visible tenant-wide until a re-crawl corrected it.
        Assert.Null(AccessEvaluator.BuildAcl(profile, new BsonDocument("other", 1)));
    }

    [Fact]
    public void A_null_access_field_yields_no_acl()
    {
        var profile = Profile(new AccessMapping
        {
            Kind = AccessKind.EntraGroupField,
            Field = "teamGroupId"
        });

        Assert.Null(AccessEvaluator.BuildAcl(profile, new BsonDocument("teamGroupId", BsonNull.Value)));
    }

    [Fact]
    public void Tenant_scoping_is_not_expressible_as_an_index_time_acl()
    {
        var access = new AccessMapping { Kind = AccessKind.TenantField, Field = "tenantId" };

        Assert.False(AccessEvaluator.IsIndexable(access));
        Assert.True(AccessEvaluator.IsIndexable(new AccessMapping { Kind = AccessKind.Everyone }));
    }
}

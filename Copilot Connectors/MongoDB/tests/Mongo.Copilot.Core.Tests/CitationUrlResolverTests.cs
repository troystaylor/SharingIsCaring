using System.Security.Claims;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Mongo.Copilot.Core.Access;
using Mongo.Copilot.Core.Citations;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Tests;

public class CitationUrlResolverTests
{
    private static CollectionProfile Ticket(string? urlPath = "/ticket/{_id}") => new()
    {
        Name = "tickets",
        Mode = ProfileMode.Indexed,
        Database = "helpdesk",
        Description = "Tickets",
        TitleField = "subject",
        UrlPath = urlPath,
        Access = new AccessMapping { Kind = AccessKind.Everyone }
    };

    private static CitationUrlResolver Resolver(UiOptions ui) =>
        new(Options.Create(ui));

    [Fact]
    public void Global_base_url_is_combined_with_the_profile_path()
    {
        var resolver = Resolver(new UiOptions { BaseUrl = "https://helpdesk.contoso.com" });
        var doc = new BsonDocument("_id", new ObjectId("507f1f77bcf86cd799439011"));

        Assert.Equal(
            "https://helpdesk.contoso.com/ticket/507f1f77bcf86cd799439011",
            resolver.Resolve(Ticket(), doc));
    }

    [Fact]
    public void Per_collection_base_url_wins_over_the_global_one()
    {
        var ui = new UiOptions
        {
            BaseUrl = "https://helpdesk.contoso.com",
            Collections = { ["tickets"] = new UiCollectionOptions { BaseUrl = "https://other.contoso.com" } }
        };

        var doc = new BsonDocument("_id", "abc");

        Assert.Equal("https://other.contoso.com/ticket/abc", Resolver(ui).Resolve(Ticket(), doc));
    }

    [Fact]
    public void Absolute_per_collection_url_overrides_the_profile_path()
    {
        var ui = new UiOptions
        {
            BaseUrl = "https://helpdesk.contoso.com",
            Collections =
            {
                ["tickets"] = new UiCollectionOptions { Url = "https://finance.contoso.com/view?doc={ref}" }
            }
        };

        var doc = new BsonDocument("ref", "INV-1");

        Assert.Equal("https://finance.contoso.com/view?doc=INV-1", Resolver(ui).Resolve(Ticket(), doc));
    }

    [Fact]
    public void No_base_url_configured_yields_no_citation()
    {
        var doc = new BsonDocument("_id", "abc");
        Assert.Null(Resolver(new UiOptions()).Resolve(Ticket(), doc));
    }

    [Fact]
    public void Substituted_values_are_url_encoded()
    {
        var resolver = Resolver(new UiOptions { BaseUrl = "https://x.contoso.com" });
        var doc = new BsonDocument("_id", "a b&c");

        Assert.Equal("https://x.contoso.com/ticket/a%20b%26c", resolver.Resolve(Ticket(), doc));
    }

    [Fact]
    public void Dotted_paths_read_into_subdocuments()
    {
        var profile = Ticket("/c/{customer.accountId}");
        var resolver = Resolver(new UiOptions { BaseUrl = "https://x.contoso.com" });
        var doc = new BsonDocument("customer", new BsonDocument("accountId", "A-9"));

        Assert.Equal("https://x.contoso.com/c/A-9", resolver.Resolve(profile, doc));
    }

    [Fact]
    public void A_missing_token_produces_no_link_rather_than_a_broken_one()
    {
        var resolver = Resolver(new UiOptions { BaseUrl = "https://x.contoso.com" });

        // The document has no _id, so the placeholder cannot be filled.
        Assert.Null(resolver.Resolve(Ticket(), new BsonDocument("other", 1)));
    }

    [Fact]
    public void Fingerprint_changes_when_the_base_url_changes()
    {
        var profile = Ticket();

        var before = new UiOptions { BaseUrl = "https://a.contoso.com" }.Fingerprint([profile]);
        var after = new UiOptions { BaseUrl = "https://b.contoso.com" }.Fingerprint([profile]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Fingerprint_is_stable_for_identical_configuration()
    {
        var profile = Ticket();

        Assert.Equal(
            new UiOptions { BaseUrl = "https://a.contoso.com" }.Fingerprint([profile]),
            new UiOptions { BaseUrl = "https://a.contoso.com" }.Fingerprint([profile]));
    }
}

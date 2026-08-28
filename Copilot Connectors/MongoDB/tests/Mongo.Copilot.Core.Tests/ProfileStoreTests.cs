using Microsoft.Extensions.Options;
using Mongo.Copilot.Core.Citations;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Tests;

public class ProfileStoreTests
{
    private static CollectionProfile Valid(ProfileMode mode = ProfileMode.Live) => new()
    {
        Mode = mode,
        Database = "db",
        Description = "Some collection",
        TitleField = "title",
        UrlPath = "/x/{_id}",
        Access = new AccessMapping { Kind = AccessKind.Everyone }
    };

    private static ProfileStore Build(MongoOptions mongo, UiOptions? ui = null) =>
        new(Options.Create(mongo), Options.Create(ui ?? new UiOptions
        {
            BaseUrl = "https://x.contoso.com"
        }));

    private static MongoOptions Options_(params (string Name, CollectionProfile Profile)[] collections)
    {
        var options = new MongoOptions
        {
            ConnectionString = "mongodb://localhost",
            Database = "db"
        };

        foreach (var (name, profile) in collections)
            options.Collections[name] = profile;

        return options;
    }

    // ── Azure hosting guards ────────────────────────────────────────────────

    [Fact]
    public void Indexing_against_request_unit_cosmos_is_refused()
    {
        var options = Options_(("tickets", Valid(ProfileMode.Indexed)));
        options.ConnectionString = "mongodb://acct:key@acct.mongo.cosmos.azure.com:10255/?ssl=true";

        var ex = Assert.Throws<ProfileValidationException>(() => Build(options));

        // Without delete events the index would keep returning documents removed upstream.
        Assert.Contains("does not emit change-stream delete events", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_only_collections_are_permitted_against_request_unit_cosmos()
    {
        var options = Options_(("orders", Valid()));
        options.ConnectionString = "mongodb://acct:key@acct.mongo.cosmos.azure.com:10255/?ssl=true";

        // Live queries never depend on change streams, so this configuration is safe.
        Assert.Single(Build(options).Live);
    }

    [Fact]
    public void Indexing_against_documentdb_vcore_is_permitted()
    {
        var options = Options_(("tickets", Valid(ProfileMode.Indexed)));
        options.ConnectionString = "mongodb+srv://c.global.mongocluster.cosmos.azure.com/";

        var store = Build(options);

        Assert.Single(store.Indexed);
        Assert.Equal(MongoProvider.AzureDocumentDb, store.Provider);
    }

    [Fact]
    public void Managed_identity_against_documentdb_needs_no_explicit_token_resource()
    {
        var options = Options_(("tickets", Valid()));
        options.ConnectionString = "mongodb+srv://c.global.mongocluster.cosmos.azure.com/";
        options.AuthMode = MongoAuthMode.ManagedIdentity;

        Assert.Single(Build(options).All);
    }

    [Fact]
    public void Managed_identity_against_atlas_requires_a_token_resource()
    {
        var options = Options_(("tickets", Valid()));
        options.ConnectionString = "mongodb+srv://cluster0.ab12c.mongodb.net/";
        options.AuthMode = MongoAuthMode.ManagedIdentity;

        var ex = Assert.Throws<ProfileValidationException>(() => Build(options));

        Assert.Contains("Mongo:TokenResource is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Credentials_left_in_the_connection_string_under_managed_identity_are_rejected()
    {
        var options = Options_(("tickets", Valid()));
        options.ConnectionString = "mongodb+srv://user:pw@c.global.mongocluster.cosmos.azure.com/";
        options.AuthMode = MongoAuthMode.ManagedIdentity;

        var ex = Assert.Throws<ProfileValidationException>(() => Build(options));

        Assert.Contains("appears to contain credentials", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_configuration_loads()
    {
        var store = Build(Options_(("tickets", Valid())));

        Assert.Single(store.All);
        Assert.Equal("tickets", store.All.First().Name);
    }

    [Fact]
    public void A_missing_access_rule_is_a_startup_error()
    {
        var profile = new CollectionProfile
        {
            Database = "db",
            Description = "No access rule",
            TitleField = "t",
            Access = null
        };

        var ex = Assert.Throws<ProfileValidationException>(() => Build(Options_(("tickets", profile))));

        Assert.Contains("Access is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_based_access_rule_without_a_field_is_a_startup_error()
    {
        var profile = new CollectionProfile
        {
            Database = "db",
            Description = "Missing field",
            TitleField = "t",
            Access = new AccessMapping { Kind = AccessKind.EntraGroupField }
        };

        var ex = Assert.Throws<ProfileValidationException>(() => Build(Options_(("tickets", profile))));

        Assert.Contains("Access.Field is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_indexed_collection_without_a_resolvable_url_is_a_startup_error()
    {
        var ex = Assert.Throws<ProfileValidationException>(() =>
            Build(Options_(("tickets", Valid(ProfileMode.Indexed))), new UiOptions { RequireUrl = true }));

        Assert.Contains("No citation URL can be resolved", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Uncited_indexing_is_permitted_when_deliberately_opted_into()
    {
        var store = Build(
            Options_(("tickets", Valid(ProfileMode.Indexed))),
            new UiOptions { RequireUrl = false });

        Assert.Single(store.Indexed);
    }

    [Fact]
    public void A_live_collection_does_not_require_a_url()
    {
        var store = Build(Options_(("tickets", Valid())), new UiOptions { RequireUrl = true });

        Assert.Single(store.Live);
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var broken = new CollectionProfile { Database = "db" };

        var ex = Assert.Throws<ProfileValidationException>(() => Build(Options_(("a", broken))));

        Assert.Contains("Description is required", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Access is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ungrouped_collections_get_one_connection_each()
    {
        var options = Options_(
            ("tickets", Valid(ProfileMode.Indexed)),
            ("articles", Valid(ProfileMode.Indexed)));

        var groups = Build(options).IndexedByConnection().ToList();

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Grouped_collections_share_one_connection()
    {
        var a = new CollectionProfile
        {
            Mode = ProfileMode.Indexed,
            Database = "db",
            Description = "A",
            TitleField = "t",
            UrlPath = "/a/{_id}",
            ConnectionGroup = "content",
            Access = new AccessMapping { Kind = AccessKind.Everyone }
        };

        var b = new CollectionProfile
        {
            Mode = ProfileMode.Indexed,
            Database = "db",
            Description = "B",
            TitleField = "t",
            UrlPath = "/b/{_id}",
            ConnectionGroup = "content",
            Access = new AccessMapping { Kind = AccessKind.Everyone }
        };

        var groups = Build(Options_(("a", a), ("b", b))).IndexedByConnection().ToList();

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count());
    }

    [Fact]
    public void Grouped_collections_with_conflicting_property_types_are_rejected()
    {
        CollectionProfile WithType(string name, PropType type) => new()
        {
            Mode = ProfileMode.Indexed,
            Database = "db",
            Description = name,
            TitleField = "t",
            UrlPath = "/x/{_id}",
            ConnectionGroup = "content",
            Properties = new Dictionary<string, PropertyProfile> { ["amount"] = new() { Type = type } },
            Access = new AccessMapping { Kind = AccessKind.Everyone }
        };

        var ex = Assert.Throws<ProfileValidationException>(() => Build(Options_(
            ("a", WithType("a", PropType.String)),
            ("b", WithType("b", PropType.Double)))));

        Assert.Contains("conflicting types", ex.Message, StringComparison.Ordinal);
    }
}

using Mongo.Copilot.Core.Data;

namespace Mongo.Copilot.Core.Tests;

public class MongoProviderDetectorTests
{
    [Theory]
    [InlineData("mongodb+srv://user:pw@cluster0.ab12c.mongodb.net/?retryWrites=true", MongoProvider.Atlas)]
    [InlineData("mongodb://host.mongodb.net:27017", MongoProvider.Atlas)]
    public void Atlas_hosts_are_detected(string connectionString, MongoProvider expected) =>
        Assert.Equal(expected, MongoProviderDetector.Detect(connectionString));

    [Theory]
    [InlineData("mongodb+srv://mycluster.global.mongocluster.cosmos.azure.com/?tls=true")]
    [InlineData("mongodb+srv://mycluster.mongocluster.cosmos.azure.com/")]
    public void DocumentDb_vcore_hosts_are_detected(string connectionString) =>
        Assert.Equal(MongoProvider.AzureDocumentDb, MongoProviderDetector.Detect(connectionString));

    [Fact]
    public void Request_unit_cosmos_hosts_are_detected()
    {
        // The RU offering and the vCore offering differ only by hostname segment, and only
        // one of them emits change-stream deletes, so this distinction is load-bearing.
        Assert.Equal(
            MongoProvider.AzureCosmosRu,
            MongoProviderDetector.Detect("mongodb://acct:key@acct.mongo.cosmos.azure.com:10255/?ssl=true"));
    }

    [Fact]
    public void Unknown_hosts_are_treated_as_self_managed() =>
        Assert.Equal(MongoProvider.SelfManaged, MongoProviderDetector.Detect("mongodb://10.0.0.4:27017"));

    [Fact]
    public void A_malformed_connection_string_does_not_throw() =>
        Assert.Equal(MongoProvider.SelfManaged, MongoProviderDetector.Detect("not-a-connection-string"));

    [Fact]
    public void An_empty_connection_string_does_not_throw() =>
        Assert.Equal(MongoProvider.SelfManaged, MongoProviderDetector.Detect(null));

    [Fact]
    public void Only_the_request_unit_offering_lacks_delete_events()
    {
        Assert.False(MongoProvider.AzureCosmosRu.SupportsChangeStreamDeletes());
        Assert.True(MongoProvider.AzureDocumentDb.SupportsChangeStreamDeletes());
        Assert.True(MongoProvider.Atlas.SupportsChangeStreamDeletes());
        Assert.True(MongoProvider.SelfManaged.SupportsChangeStreamDeletes());
    }

    [Fact]
    public void DocumentDb_has_a_documented_default_token_resource()
    {
        Assert.Equal(
            "https://ossrdbms-aad.database.windows.net/.default",
            MongoProvider.AzureDocumentDb.DefaultTokenResource());

        // Atlas uses an installation-specific Application ID URI, so there is no default.
        Assert.Null(MongoProvider.Atlas.DefaultTokenResource());
    }
}

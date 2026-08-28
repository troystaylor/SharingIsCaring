using MongoDB.Driver;

namespace Mongo.Copilot.Core.Data;

/// <summary>
/// The MongoDB-compatible service behind a connection string. Detected from the host so
/// that provider-specific behaviour is decided at startup, without a network round trip.
/// </summary>
public enum MongoProvider
{
    /// <summary>Self-managed MongoDB, or any host not recognised as a managed service.</summary>
    SelfManaged,

    /// <summary>MongoDB Atlas, including Atlas deployed into an Azure region.</summary>
    Atlas,

    /// <summary>
    /// Azure DocumentDB, previously Azure Cosmos DB for MongoDB vCore. Change streams are
    /// GA here and emit deletes, so the indexer is supported.
    /// </summary>
    AzureDocumentDb,

    /// <summary>
    /// Azure Cosmos DB for MongoDB on the request-unit model. Change streams omit delete
    /// events entirely, so an index built from them silently goes stale.
    /// </summary>
    AzureCosmosRu
}

public static class MongoProviderDetector
{
    /// <summary>
    /// Identifies the backing service from the connection string host.
    /// </summary>
    /// <remarks>
    /// The two Azure offerings are distinguishable by hostname: the vCore/DocumentDB service
    /// uses <c>*.mongocluster.cosmos.azure.com</c>, while the request-unit service uses
    /// <c>*.mongo.cosmos.azure.com</c>. That distinction matters because only one of them
    /// emits change-stream deletes.
    /// </remarks>
    public static MongoProvider Detect(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return MongoProvider.SelfManaged;

        string host;
        try
        {
            var url = new MongoUrl(connectionString);
            host = url.Servers.FirstOrDefault()?.Host ?? string.Empty;
        }
        catch (MongoConfigurationException)
        {
            return MongoProvider.SelfManaged;
        }

        return DetectFromHost(host);
    }

    public static MongoProvider DetectFromHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return MongoProvider.SelfManaged;

        host = host.ToLowerInvariant();

        if (host.EndsWith(".mongodb.net", StringComparison.Ordinal) ||
            host.EndsWith(".mongodb-dev.net", StringComparison.Ordinal))
            return MongoProvider.Atlas;

        // Check the vCore suffix first: it also contains ".cosmos.azure.com".
        if (host.Contains(".mongocluster.cosmos.azure.com", StringComparison.Ordinal))
            return MongoProvider.AzureDocumentDb;

        if (host.EndsWith(".mongo.cosmos.azure.com", StringComparison.Ordinal))
            return MongoProvider.AzureCosmosRu;

        return MongoProvider.SelfManaged;
    }

    /// <summary>
    /// Whether this provider emits change-stream delete events. Without them, a document
    /// removed upstream stays in the Microsoft 365 index and Copilot keeps citing it.
    /// </summary>
    public static bool SupportsChangeStreamDeletes(this MongoProvider provider) =>
        provider != MongoProvider.AzureCosmosRu;

    /// <summary>
    /// Default Entra token audience for providers that document one. Atlas uses the
    /// Application ID URI of the app registration backing its workload identity federation,
    /// which is installation-specific and therefore has no default.
    /// </summary>
    public static string? DefaultTokenResource(this MongoProvider provider) => provider switch
    {
        MongoProvider.AzureDocumentDb => "https://ossrdbms-aad.database.windows.net/.default",
        _ => null
    };
}

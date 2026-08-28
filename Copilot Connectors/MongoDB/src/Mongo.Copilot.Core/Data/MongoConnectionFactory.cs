using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Data;

/// <summary>
/// Shared <see cref="IMongoClient"/>. The driver pools connections internally, so this is
/// registered as a singleton and reused across requests.
/// </summary>
public sealed class MongoConnectionFactory
{
    private readonly Lazy<IMongoClient> _client;
    private readonly ProfileStore _profiles;

    public MongoConnectionFactory(IOptions<MongoOptions> options, ProfileStore profiles)
    {
        _profiles = profiles;
        var config = options.Value;
        Provider = MongoProviderDetector.Detect(config.ConnectionString);

        _client = new Lazy<IMongoClient>(() =>
        {
            var settings = MongoClientSettings.FromConnectionString(config.ConnectionString);
            settings.ApplicationName = "mongo-copilot";

            if (config.AuthMode == MongoAuthMode.ManagedIdentity)
            {
                // Passwordless: the driver requests an Entra token per authentication rather
                // than presenting a stored credential, so no secret exists to leak or rotate.
                var resource = config.ResolveTokenResource(Provider)
                    ?? throw new InvalidOperationException(
                        "Mongo:TokenResource is required for managed identity auth against this provider.");

                settings.Credential = MongoCredential.CreateOidcCredential(
                    new AzureIdentityOidcCallback(resource, config.ManagedIdentityClientId));
            }

            return new MongoClient(settings);
        });
    }

    /// <summary>Backing service, detected from the connection string host.</summary>
    public MongoProvider Provider { get; }

    public IMongoClient Client => _client.Value;

    public IMongoCollection<BsonDocument> Collection(CollectionProfile profile) =>
        Client.GetDatabase(_profiles.DatabaseFor(profile))
              .GetCollection<BsonDocument>(profile.Name);
}

using Microsoft.Extensions.Options;
using Mongo.Copilot.Core.Citations;
using Mongo.Copilot.Core.Data;

namespace Mongo.Copilot.Core.Profiles;

/// <summary>How the connector authenticates to MongoDB.</summary>
public enum MongoAuthMode
{
    /// <summary>Credentials embedded in the connection string.</summary>
    ConnectionString,

    /// <summary>
    /// Passwordless <c>MONGODB-OIDC</c> using an Azure managed identity. Supported by Azure
    /// DocumentDB and by Atlas via workload identity federation.
    /// </summary>
    ManagedIdentity
}

/// <summary>
/// Options bound from the <c>Mongo</c> configuration section.
/// </summary>
public sealed class MongoOptions
{
    /// <summary>
    /// Connection string. With <see cref="MongoAuthMode.ManagedIdentity"/> this carries the
    /// host only and no password, so it is not a secret.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public MongoAuthMode AuthMode { get; set; } = MongoAuthMode.ConnectionString;

    /// <summary>
    /// Entra audience requested for the database token. Defaults per provider; required for
    /// Atlas, where it is the Application ID URI of the app registration backing workload
    /// identity federation.
    /// </summary>
    public string? TokenResource { get; set; }

    /// <summary>Client ID of a user-assigned managed identity. Empty for system-assigned.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Default database for profiles that don't name one.</summary>
    public string? Database { get; set; }

    /// <summary>Cap on documents returned by any single live query.</summary>
    public int MaxResults { get; set; } = 50;

    public Dictionary<string, CollectionProfile> Collections { get; set; } = [];

    internal string? ResolveTokenResource(MongoProvider provider) =>
        string.IsNullOrWhiteSpace(TokenResource) ? provider.DefaultTokenResource() : TokenResource;
}

/// <summary>
/// Validated, immutable view of the configured collection profiles.
/// </summary>
public sealed class ProfileStore
{
    private readonly Dictionary<string, CollectionProfile> _byName;

    public ProfileStore(IOptions<MongoOptions> mongo, IOptions<UiOptions> ui)
    {
        var options = mongo.Value;
        foreach (var (name, profile) in options.Collections)
            profile.Name = name;

        Validate(options, ui.Value);

        _byName = new Dictionary<string, CollectionProfile>(
            options.Collections, StringComparer.OrdinalIgnoreCase);
        DefaultDatabase = options.Database;
        MaxResults = options.MaxResults;
        Provider = MongoProviderDetector.Detect(options.ConnectionString);
    }

    public string? DefaultDatabase { get; }
    public int MaxResults { get; }

    /// <summary>Backing service, detected from the connection string host.</summary>
    public MongoProvider Provider { get; }

    public IReadOnlyCollection<CollectionProfile> All => _byName.Values;
    public IEnumerable<CollectionProfile> Live => _byName.Values.Where(p => p.IsLive);
    public IEnumerable<CollectionProfile> Indexed => _byName.Values.Where(p => p.IsIndexed);

    public bool TryGet(string name, out CollectionProfile profile) =>
        _byName.TryGetValue(name, out profile!);

    /// <summary>Resolves the database for a profile, falling back to the default.</summary>
    public string DatabaseFor(CollectionProfile profile) =>
        profile.Database
        ?? DefaultDatabase
        ?? throw new InvalidOperationException(
            $"Collection '{profile.Name}' does not specify a database and no default is configured.");

    /// <summary>
    /// Indexed profiles grouped into the Graph connection they belong to. Profiles sharing a
    /// <c>ConnectionGroup</c> are merged; everything else gets its own connection.
    /// </summary>
    public IEnumerable<IGrouping<string, CollectionProfile>> IndexedByConnection() =>
        Indexed.GroupBy(p => p.ConnectionKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fail-closed validation. Every problem is collected and reported at once, because an
    /// installer editing JSON would otherwise fix them one restart at a time.
    /// </summary>
    private static void Validate(MongoOptions options, UiOptions ui)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            errors.Add("Mongo:ConnectionString is not configured.");

        if (options.Collections.Count == 0)
            errors.Add("No collections are configured under Mongo:Collections.");

        var provider = MongoProviderDetector.Detect(options.ConnectionString);
        var hasIndexed = options.Collections.Values.Any(p => p.IsIndexed);

        // Azure Cosmos DB for MongoDB on the request-unit model does not emit change-stream
        // delete events, so a document removed upstream would remain in the Microsoft 365
        // index indefinitely and Copilot would keep citing it. Refuse rather than build an
        // index that silently rots.
        if (provider == MongoProvider.AzureCosmosRu && hasIndexed)
        {
            errors.Add(
                "The connection string points at Azure Cosmos DB for MongoDB (request-unit model), " +
                "which does not emit change-stream delete events. Indexed collections would keep " +
                "returning documents that were deleted upstream. Use Azure DocumentDB " +
                "(Cosmos DB for MongoDB vCore) or MongoDB Atlas for indexing, or set every " +
                "collection to Mode 'Live'.");
        }

        if (options.AuthMode == MongoAuthMode.ManagedIdentity)
        {
            if (options.ResolveTokenResource(provider) is null)
            {
                errors.Add(
                    "Mongo:TokenResource is required for managed identity auth against this provider. " +
                    "For MongoDB Atlas this is the Application ID URI of the app registration backing " +
                    "workload identity federation.");
            }

            if (!string.IsNullOrWhiteSpace(options.ConnectionString) &&
                options.ConnectionString.Contains('@', StringComparison.Ordinal))
            {
                errors.Add(
                    "Mongo:ConnectionString appears to contain credentials, but AuthMode is " +
                    "ManagedIdentity. Remove the username and password from the connection string.");
            }
        }

        foreach (var (name, profile) in options.Collections)
        {
            if (string.IsNullOrWhiteSpace(profile.IdField))
                errors.Add($"[{name}] IdField must not be empty.");

            if (string.IsNullOrWhiteSpace(profile.Database) && string.IsNullOrWhiteSpace(options.Database))
                errors.Add($"[{name}] No Database set and no Mongo:Database default configured.");

            if (string.IsNullOrWhiteSpace(profile.Description))
                errors.Add($"[{name}] Description is required; Copilot uses it to choose the tool.");

            // Fail closed: an omitted access rule is an error, never an implicit grant.
            if (profile.Access is null)
            {
                errors.Add(
                    $"[{name}] Access is required. Use {{\"Kind\":\"Everyone\"}} to deliberately " +
                    "grant to all users, or a field-based mapping to scope by audience.");
            }
            else if (profile.Access.RequiresField && string.IsNullOrWhiteSpace(profile.Access.Field))
            {
                errors.Add($"[{name}] Access.Field is required for Kind '{profile.Access.Kind}'.");
            }

            foreach (var (propName, prop) in profile.Properties)
            {
                if (string.IsNullOrWhiteSpace(propName))
                    errors.Add($"[{name}] A property name is empty.");

                if (prop.Type == PropType.StringCollection && prop.IsRefinable is false && prop.IsQueryable is false)
                    continue; // collections are commonly retrieve-only; not an error
            }

            if (profile.IsIndexed)
            {
                if (string.IsNullOrWhiteSpace(profile.TitleField))
                    errors.Add($"[{name}] TitleField is required for indexed collections.");

                if (ui.RequireUrl && !ui.CanResolve(profile))
                {
                    errors.Add(
                        $"[{name}] No citation URL can be resolved. Set Ui:BaseUrl, " +
                        $"Ui:Collections:{name}:BaseUrl, or Ui:Collections:{name}:Url — " +
                        "or set Ui:RequireUrl to false to publish uncited items deliberately.");
                }
            }
        }

        var groups = options.Collections.Values
            .Where(p => p.IsIndexed && p.ConnectionGroup is not null)
            .GroupBy(p => p.ConnectionGroup!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var conflicting = group
                .SelectMany(p => p.Properties)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(kv => kv.Value.Type).Distinct().Count() > 1)
                .Select(g => g.Key);

            foreach (var property in conflicting)
            {
                errors.Add(
                    $"[ConnectionGroup '{group.Key}'] Property '{property}' is declared with " +
                    "conflicting types across grouped collections; a shared connection needs one schema.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ProfileValidationException(
                "Collection profile configuration is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        }
    }
}

public sealed class ProfileValidationException(string message) : Exception(message);

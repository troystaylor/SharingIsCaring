using System.Text;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Indexer.Graph;

/// <summary>
/// Creates the Graph external connection and registers its schema from the collection
/// profiles. One connection per <see cref="CollectionProfile.ConnectionKey"/>, so ungrouped
/// collections each get their own flat schema instead of a sparse union.
/// </summary>
public sealed class ConnectionManager(GraphClient graph, ILogger<ConnectionManager> logger)
{
    /// <summary>
    /// Property carrying the indexer's own state. Present in every schema so the reserved
    /// state item has somewhere to live; see <see cref="GraphStateStore"/>.
    /// </summary>
    public const string StateProperty = "stateJson";

    public async Task EnsureConnectionAsync(
        string connectionId,
        string displayName,
        IReadOnlyCollection<CollectionProfile> profiles,
        CancellationToken cancellationToken)
    {
        var existing = await graph.GetAsync<ConnectionResponse>(
            $"external/connections/{connectionId}", cancellationToken);

        if (existing is null)
        {
            logger.LogInformation("Creating Graph connection {ConnectionId}.", connectionId);

            await graph.SendAsync(HttpMethod.Post, "external/connections", new
            {
                id = connectionId,
                name = displayName,
                description = BuildDescription(profiles),
                // Signals the nature of the content so Graph can tune ranking and semantics.
                contentCategory = "knowledgeBase"
            }, cancellationToken);

            await RegisterSchemaAsync(connectionId, profiles, cancellationToken);
        }
        else
        {
            logger.LogInformation("Graph connection {ConnectionId} already exists.", connectionId);
        }
    }

    /// <summary>
    /// Registers the flat schema. Graph runs this asynchronously and rejects items until it
    /// completes, so the returned operation is polled to completion before any crawl starts.
    /// </summary>
    private async Task RegisterSchemaAsync(
        string connectionId,
        IReadOnlyCollection<CollectionProfile> profiles,
        CancellationToken cancellationToken)
    {
        var properties = BuildSchemaProperties(profiles);

        logger.LogInformation(
            "Registering schema for {ConnectionId} with {Count} properties.", connectionId, properties.Count);

        using var response = await graph.SendAsync(
            HttpMethod.Post,
            $"external/connections/{connectionId}/schema",
            new { baseType = "microsoft.graph.externalItem", properties },
            cancellationToken);

        if (response.Headers.Location is { } location)
            await PollOperationAsync(location.ToString(), cancellationToken);
    }

    private async Task PollOperationAsync(string location, CancellationToken cancellationToken)
    {
        var path = location.Contains("/v1.0/", StringComparison.OrdinalIgnoreCase)
            ? location[(location.IndexOf("/v1.0/", StringComparison.OrdinalIgnoreCase) + 6)..]
            : location;

        for (var attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            var operation = await graph.GetAsync<OperationResponse>(path, cancellationToken);

            switch (operation?.Status?.ToLowerInvariant())
            {
                case "completed":
                    logger.LogInformation("Schema registration completed.");
                    return;

                case "failed":
                    throw new GraphException(
                        $"Schema registration failed: {operation.Error?.Message ?? "no detail returned"}");

                default:
                    continue;
            }
        }

        throw new GraphException("Schema registration did not complete within 10 minutes.");
    }

    /// <summary>
    /// Projects profile properties into Graph schema entries. Grouped collections are merged
    /// by name; startup validation has already rejected conflicting types across a group.
    /// </summary>
    private static List<SchemaProperty> BuildSchemaProperties(IReadOnlyCollection<CollectionProfile> profiles)
    {
        var properties = new Dictionary<string, SchemaProperty>(StringComparer.OrdinalIgnoreCase)
        {
            // Semantic labels Graph uses for result display and grounding.
            ["title"] = new()
            {
                Name = "title",
                Type = "String",
                IsSearchable = true,
                IsRetrievable = true,
                Labels = ["title"]
            },
            ["url"] = new()
            {
                Name = "url",
                Type = "String",
                IsRetrievable = true,
                Labels = ["url"]
            },
            ["collection"] = new()
            {
                Name = "collection",
                Type = "String",
                IsQueryable = true,
                IsRetrievable = true,
                IsRefinable = true
            },
            [StateProperty] = new()
            {
                Name = StateProperty,
                Type = "String",
                IsRetrievable = true
            }
        };

        foreach (var profile in profiles)
        {
            foreach (var (name, property) in profile.Properties)
            {
                if (properties.ContainsKey(name))
                    continue;

                properties[name] = new SchemaProperty
                {
                    Name = name,
                    Type = property.Type.ToString(),
                    IsSearchable = property.IsSearchable,
                    IsQueryable = property.IsQueryable,
                    IsRetrievable = property.IsRetrievable,
                    IsRefinable = property.IsRefinable,
                    Labels = property.Labels.Count > 0 ? [.. property.Labels] : null
                };
            }
        }

        return [.. properties.Values];
    }

    private static string BuildDescription(IReadOnlyCollection<CollectionProfile> profiles)
    {
        var builder = new StringBuilder("MongoDB content: ");
        builder.Append(string.Join(", ", profiles.Select(p => p.Name)));

        return builder.Length > 1000 ? builder.ToString(0, 1000) : builder.ToString();
    }

    /// <summary>
    /// Graph connection IDs must be 3–32 alphanumeric characters and cannot start with
    /// "Microsoft", so collection names are sanitized and truncated with a hash suffix to
    /// keep them unique after truncation.
    /// </summary>
    public static string SanitizeConnectionId(string key)
    {
        var cleaned = new string([.. key.Where(char.IsLetterOrDigit)]);

        if (cleaned.Length == 0)
            cleaned = "mongo";

        if (char.IsDigit(cleaned[0]))
            cleaned = "m" + cleaned;

        if (cleaned.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
            cleaned = "mdb" + cleaned;

        if (cleaned.Length > 32)
        {
            var hash = Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(key)))[..6];
            cleaned = cleaned[..26] + hash;
        }

        while (cleaned.Length < 3)
            cleaned += "x";

        return cleaned;
    }
}

public sealed class SchemaProperty
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
    public bool IsSearchable { get; set; }
    public bool IsQueryable { get; set; }
    public bool IsRetrievable { get; set; } = true;
    public bool IsRefinable { get; set; }
    public string[]? Labels { get; set; }
}

public sealed class ConnectionResponse
{
    public string? Id { get; set; }
    public string? State { get; set; }
}

public sealed class OperationResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public OperationError? Error { get; set; }
}

public sealed class OperationError
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}

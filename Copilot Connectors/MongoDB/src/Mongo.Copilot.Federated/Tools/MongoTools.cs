using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using MongoDB.Bson;
using MongoDB.Driver;
using Mongo.Copilot.Core.Access;
using Mongo.Copilot.Core.Data;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Federated.Tools;

/// <summary>
/// Read-only MongoDB tools exposed to Microsoft 365 Copilot.
/// </summary>
/// <remarks>
/// Every tool applies the mandatory access filter derived from the caller's validated token
/// before touching the collection, and every result carries the citation URL resolved by the
/// shared core. There is deliberately no create, update, or delete surface here: federated
/// read-only status is declared rather than enforced by the platform, so the guarantee has
/// to be structural.
/// </remarks>
[McpServerToolType]
public sealed class MongoTools(
    ProfileStore profiles,
    MongoConnectionFactory factory,
    DocumentProjector projector,
    IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Title = "List collections", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List the MongoDB collections available to the signed-in user, with a " +
                 "description of what each one contains. Call this first to discover what " +
                 "data can be queried.")]
    public string ListCollections()
    {
        var items = profiles.Live
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                name = p.Name,
                description = p.Description,
                fields = p.Properties.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray()
            });

        return Serialize(new { collections = items });
    }

    [McpServerTool(Title = "Get collection schema", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Describe the queryable fields of one collection and their types. Use this " +
                 "before building a filter so that field names and types are correct.")]
    public string CollectionSchema(
        [Description("Collection name, as returned by list-collections.")] string collection)
    {
        var profile = Require(collection);

        var fields = profile.Properties
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new
            {
                name = kv.Key,
                type = kv.Value.Type.ToString(),
                queryable = kv.Value.IsQueryable,
                searchable = kv.Value.IsSearchable
            });

        return Serialize(new
        {
            collection = profile.Name,
            description = profile.Description,
            idField = profile.IdField,
            titleField = profile.TitleField,
            fields
        });
    }

    [McpServerTool(Title = "Find documents", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Find documents in a collection matching a MongoDB filter. The filter is a " +
                 "JSON object using MongoDB query syntax, for example " +
                 "{\"status\":\"open\",\"priority\":{\"$in\":[\"high\",\"urgent\"]}}. " +
                 "Results are limited to what the signed-in user is permitted to see.")]
    public async Task<string> Find(
        [Description("Collection name.")] string collection,
        [Description("MongoDB filter as a JSON object. Omit or pass {} to match all permitted documents.")]
        string? filter = null,
        [Description("Field to sort by. Prefix with '-' for descending, for example '-updatedAt'.")]
        string? sort = null,
        [Description("Maximum documents to return.")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var profile = Require(collection);
        var query = Combine(profile, ParseFilter(filter));

        var find = factory.Collection(profile).Find(query).Limit(Clamp(limit));

        if (!string.IsNullOrWhiteSpace(sort))
        {
            var descending = sort.StartsWith('-');
            var field = descending ? sort[1..] : sort;
            find = find.Sort(descending
                ? Builders<BsonDocument>.Sort.Descending(field)
                : Builders<BsonDocument>.Sort.Ascending(field));
        }

        var documents = await find.ToListAsync(cancellationToken);

        return Serialize(new
        {
            collection = profile.Name,
            count = documents.Count,
            documents = documents.Select(d => Shape(profile, d))
        });
    }

    [McpServerTool(Title = "Count documents", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Count documents matching a filter, without returning them. Use this for " +
                 "questions about how many records exist.")]
    public async Task<string> Count(
        [Description("Collection name.")] string collection,
        [Description("MongoDB filter as a JSON object. Omit to count all permitted documents.")]
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        var profile = Require(collection);
        var query = Combine(profile, ParseFilter(filter));

        var count = await factory.Collection(profile)
            .CountDocumentsAsync(query, cancellationToken: cancellationToken);

        return Serialize(new { collection = profile.Name, count });
    }

    [McpServerTool(Title = "Aggregate documents", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Run a read-only MongoDB aggregation pipeline for grouping and summarizing, " +
                 "for example counting tickets by status. Supply the pipeline as a JSON array " +
                 "of stages. Only read stages are permitted: $match, $group, $sort, $limit, " +
                 "$project, $count, $unwind, $facet and similar. Stages that write or run " +
                 "server-side code are rejected.")]
    public async Task<string> Aggregate(
        [Description("Collection name.")] string collection,
        [Description("Aggregation pipeline as a JSON array of stage objects.")] string pipeline,
        [Description("Maximum documents to return.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var profile = Require(collection);
        var stages = ParsePipeline(pipeline);

        // Reject writes and server-side code before anything reaches the driver.
        try
        {
            PipelineGuard.Validate(stages);
        }
        catch (PipelineRejectedException ex)
        {
            throw new McpException(ex.Message);
        }

        // The access filter is prepended, so a caller cannot widen their own scope with a
        // broader $match at the head of their pipeline.
        var full = new List<BsonDocument>
        {
            new("$match", AccessEvaluator.BuildFilterDocument(profile, http.HttpContext?.User))
        };
        full.AddRange(stages);
        full.Add(new BsonDocument("$limit", Clamp(limit)));

        var results = await factory.Collection(profile)
            .Aggregate<BsonDocument>(
                PipelineDefinition<BsonDocument, BsonDocument>.Create(full),
                cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        return Serialize(new
        {
            collection = profile.Name,
            count = results.Count,
            results = results.Select(BsonValues.ToPlainObject)
        });
    }

    private CollectionProfile Require(string collection)
    {
        if (!profiles.TryGet(collection, out var profile) || !profile.IsLive)
        {
            var available = string.Join(", ", profiles.Live.Select(p => p.Name));
            throw new McpException(
                $"Collection '{collection}' is not available. Available collections: {available}.");
        }

        return profile;
    }

    /// <summary>
    /// Combines the caller's filter with the mandatory access filter, so a caller-supplied
    /// filter can only ever narrow the result set.
    /// </summary>
    private FilterDefinition<BsonDocument> Combine(CollectionProfile profile, BsonDocument? userFilter)
    {
        var access = AccessEvaluator.BuildFilter(profile, http.HttpContext?.User);

        return userFilter is null || userFilter.ElementCount == 0
            ? access
            : Builders<BsonDocument>.Filter.And(access, userFilter);
    }

    private object Shape(CollectionProfile profile, BsonDocument document)
    {
        var projected = projector.Project(profile, document);

        return new
        {
            id = projected.Id,
            title = projected.Title,
            url = projected.Url,
            content = Truncate(projected.Content),
            properties = projected.Properties
        };
    }

    /// <summary>Keeps one tool response from flooding the model's context window.</summary>
    private static string? Truncate(string? content, int max = 2000) =>
        content is { Length: > 0 } && content.Length > max
            ? string.Concat(content.AsSpan(0, max), "… [truncated]")
            : content;

    private int Clamp(int limit) => Math.Clamp(limit, 1, profiles.MaxResults);

    private static BsonDocument? ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        try
        {
            return BsonDocument.Parse(filter);
        }
        catch (Exception ex)
        {
            throw new McpException($"Filter is not valid JSON: {ex.Message}");
        }
    }

    private static List<BsonDocument> ParsePipeline(string pipeline)
    {
        if (string.IsNullOrWhiteSpace(pipeline))
            throw new McpException("Pipeline is required.");

        try
        {
            // Wrapped in a document because BsonDocument.Parse cannot parse a bare array.
            var wrapper = BsonDocument.Parse($"{{\"stages\":{pipeline}}}");
            return [.. wrapper["stages"].AsBsonArray.Select(v => v.AsBsonDocument)];
        }
        catch (Exception ex)
        {
            throw new McpException($"Pipeline is not a valid JSON array of stages: {ex.Message}");
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}


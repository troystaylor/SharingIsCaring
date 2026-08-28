using MongoDB.Bson;
using Mongo.Copilot.Core.Citations;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Data;

/// <summary>A document flattened according to its profile.</summary>
public sealed class ProjectedDocument
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public string? Content { get; init; }
    public string? Url { get; init; }
    public required Dictionary<string, object?> Properties { get; init; }
}

/// <summary>
/// Projects MongoDB documents into the flat shape both heads consume. Sharing this is the
/// reason the core exists: an indexed item and a live tool response cannot drift, because
/// they are produced by the same code from the same profile.
/// </summary>
public sealed class DocumentProjector(CitationUrlResolver citations)
{
    public ProjectedDocument Project(CollectionProfile profile, BsonDocument document)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (name, property) in profile.Properties)
        {
            var value = BsonValues.ReadPath(document, profile.PathFor(name));
            var coerced = BsonValues.Coerce(value, property.Type);

            // Omit rather than emit a null: Graph rejects null-valued properties, and a
            // missing key is a truer signal to Copilot than an empty string.
            if (coerced is not null)
                properties[name] = coerced;
        }

        return new ProjectedDocument
        {
            Id = ReadId(profile, document),
            Title = ReadText(document, profile.TitleField),
            Content = ReadText(document, profile.ContentField),
            Url = citations.Resolve(profile, document),
            Properties = properties
        };
    }

    private static string ReadId(CollectionProfile profile, BsonDocument document)
    {
        var value = BsonValues.ReadPath(document, profile.IdField)
            ?? throw new InvalidOperationException(
                $"[{profile.Name}] Document is missing id field '{profile.IdField}'.");

        return BsonValues.ToPlainString(value);
    }

    private static string? ReadText(BsonDocument document, string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        var value = BsonValues.ReadPath(document, field);
        return value is null or BsonNull ? null : BsonValues.ToPlainString(value);
    }
}

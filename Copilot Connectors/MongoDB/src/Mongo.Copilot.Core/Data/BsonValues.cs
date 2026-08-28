using System.Globalization;
using MongoDB.Bson;
using Mongo.Copilot.Core.Profiles;

namespace Mongo.Copilot.Core.Data;

/// <summary>
/// Reading and coercing BSON values. MongoDB documents are nested and dynamically typed;
/// both heads need the same flattening rules so an indexed field and a live field agree.
/// </summary>
public static class BsonValues
{
    /// <summary>
    /// Reads a dotted path such as <c>customer.accountId</c>. Returns null when any
    /// segment is missing rather than throwing.
    /// </summary>
    public static BsonValue? ReadPath(BsonDocument document, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        BsonValue current = document;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not BsonDocument doc || !doc.TryGetValue(segment, out var next))
                return null;

            current = next;
        }

        return ReferenceEquals(current, document) ? null : current;
    }

    /// <summary>
    /// Renders a scalar for URL substitution and text output. ObjectId becomes its
    /// 24-character hex form, which is what a document URL almost always expects.
    /// </summary>
    public static string ToPlainString(BsonValue value) => value.BsonType switch
    {
        BsonType.ObjectId => value.AsObjectId.ToString(),
        BsonType.String => value.AsString,
        BsonType.Int32 => value.AsInt32.ToString(CultureInfo.InvariantCulture),
        BsonType.Int64 => value.AsInt64.ToString(CultureInfo.InvariantCulture),
        BsonType.Double => value.AsDouble.ToString(CultureInfo.InvariantCulture),
        BsonType.Decimal128 => value.AsDecimal.ToString(CultureInfo.InvariantCulture),
        BsonType.Boolean => value.AsBoolean ? "true" : "false",
        BsonType.DateTime => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        BsonType.Null or BsonType.Undefined => string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// Coerces a BSON value into the CLR type a Graph external item property expects.
    /// Returns null when the value cannot be represented, so the property is omitted
    /// rather than published as a wrong-typed value.
    /// </summary>
    public static object? Coerce(BsonValue? value, PropType type)
    {
        if (value is null || value.IsBsonNull || value.BsonType == BsonType.Undefined)
            return null;

        try
        {
            return type switch
            {
                PropType.String => ToPlainString(value),
                PropType.Int64 => value.BsonType switch
                {
                    BsonType.Int32 or BsonType.Int64 or BsonType.Double or BsonType.Decimal128 => value.ToInt64(),
                    BsonType.String when long.TryParse(value.AsString, out var parsed) => parsed,
                    BsonType.Boolean => value.AsBoolean ? 1L : 0L,
                    _ => null
                },
                PropType.Double => value.BsonType switch
                {
                    BsonType.Int32 or BsonType.Int64 or BsonType.Double or BsonType.Decimal128 => value.ToDouble(),
                    BsonType.String when double.TryParse(
                        value.AsString, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => null
                },
                PropType.Boolean => value.BsonType switch
                {
                    BsonType.Boolean => value.AsBoolean,
                    BsonType.Int32 or BsonType.Int64 => value.ToInt64() != 0,
                    BsonType.String when bool.TryParse(value.AsString, out var parsed) => parsed,
                    _ => null
                },
                PropType.DateTime => value.BsonType switch
                {
                    BsonType.DateTime => value.ToUniversalTime(),
                    BsonType.Timestamp => DateTimeOffset
                        .FromUnixTimeSeconds(value.AsBsonTimestamp.Timestamp).UtcDateTime,
                    BsonType.String when DateTimeOffset.TryParse(
                        value.AsString, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var parsed) => parsed.UtcDateTime,
                    _ => null
                },
                PropType.StringCollection => value.BsonType switch
                {
                    BsonType.Array => value.AsBsonArray
                        .Where(v => !v.IsBsonNull)
                        .Select(ToPlainString)
                        .Where(s => s.Length > 0)
                        .ToArray(),
                    _ => new[] { ToPlainString(value) }
                },
                _ => null
            };
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a document to a plain dictionary for JSON serialization in MCP tool
    /// responses, keeping ObjectId and dates readable rather than emitting extended JSON.
    /// </summary>
    public static object? ToPlainObject(BsonValue value) => value.BsonType switch
    {
        BsonType.Document => value.AsBsonDocument.Elements
            .ToDictionary(e => e.Name, e => ToPlainObject(e.Value)),
        BsonType.Array => value.AsBsonArray.Select(ToPlainObject).ToArray(),
        BsonType.Null or BsonType.Undefined => null,
        BsonType.Boolean => value.AsBoolean,
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Double => value.AsDouble,
        BsonType.Decimal128 => value.AsDecimal,
        BsonType.DateTime => value.ToUniversalTime(),
        _ => ToPlainString(value)
    };
}

using MongoDB.Bson;

namespace Mongo.Copilot.Core.Data;

/// <summary>
/// Structural guard for caller-supplied aggregation pipelines.
/// </summary>
/// <remarks>
/// Federated read-only status is declared, not enforced: Microsoft 365 trusts the
/// <c>readOnlyHint</c> annotation at registration time and does not block a mutating call
/// at runtime. An aggregation pipeline is the one place a "read" tool can still write, via
/// <c>$out</c> or <c>$merge</c>, or execute server-side JavaScript via <c>$function</c>.
/// This allow-list rejects those before the pipeline reaches the driver.
/// </remarks>
public static class PipelineGuard
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "$match", "$group", "$sort", "$limit", "$skip", "$project",
        "$count", "$unwind", "$addFields", "$set", "$sortByCount",
        "$bucket", "$bucketAuto", "$facet", "$replaceRoot", "$sample"
    };

    /// <summary>Stages that write, execute code, or reach other collections.</summary>
    private static readonly HashSet<string> ExplicitlyDenied = new(StringComparer.Ordinal)
    {
        "$out", "$merge", "$function", "$accumulator", "$where",
        "$lookup", "$graphLookup", "$unionWith", "$collStats", "$indexStats",
        "$currentOp", "$listSessions", "$listLocalSessions", "$planCacheStats"
    };

    public static void Validate(IEnumerable<BsonDocument> pipeline)
    {
        foreach (var stage in pipeline)
        {
            if (stage.ElementCount != 1)
            {
                throw new PipelineRejectedException(
                    "Each aggregation stage must contain exactly one operator.");
            }

            var name = stage.GetElement(0).Name;

            if (ExplicitlyDenied.Contains(name))
            {
                throw new PipelineRejectedException(
                    $"Stage '{name}' is not permitted: this connector is read-only and " +
                    "does not allow writes, server-side code, or cross-collection reads.");
            }

            if (!Allowed.Contains(name))
            {
                throw new PipelineRejectedException(
                    $"Stage '{name}' is not in the allowed set: {string.Join(", ", Allowed.Order())}.");
            }

            // $function and $where can also appear nested inside an otherwise-allowed stage.
            RejectNestedCode(stage);
        }
    }

    private static void RejectNestedCode(BsonValue value)
    {
        switch (value)
        {
            case BsonDocument doc:
                foreach (var element in doc.Elements)
                {
                    if (element.Name is "$function" or "$where" or "$accumulator")
                    {
                        throw new PipelineRejectedException(
                            $"Operator '{element.Name}' is not permitted anywhere in a pipeline.");
                    }

                    RejectNestedCode(element.Value);
                }
                break;

            case BsonArray array:
                foreach (var item in array)
                    RejectNestedCode(item);
                break;

            case BsonJavaScript:
                throw new PipelineRejectedException(
                    "Server-side JavaScript is not permitted in a pipeline.");
        }
    }
}

public sealed class PipelineRejectedException(string message) : Exception(message);

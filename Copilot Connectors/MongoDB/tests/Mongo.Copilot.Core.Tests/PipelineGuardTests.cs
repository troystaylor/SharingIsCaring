using MongoDB.Bson;
using Mongo.Copilot.Core.Data;

namespace Mongo.Copilot.Core.Tests;

public class PipelineGuardTests
{
    private static List<BsonDocument> Pipeline(string json) =>
        [.. BsonDocument.Parse($"{{\"s\":{json}}}")["s"].AsBsonArray.Select(v => v.AsBsonDocument)];

    [Fact]
    public void Read_only_stages_are_allowed()
    {
        var pipeline = Pipeline("""
            [ { "$match": { "status": "open" } },
              { "$group": { "_id": "$priority", "n": { "$sum": 1 } } },
              { "$sort": { "n": -1 } },
              { "$limit": 10 } ]
            """);

        PipelineGuard.Validate(pipeline);
    }

    [Theory]
    [InlineData("$out")]
    [InlineData("$merge")]
    public void Write_stages_are_rejected(string stage)
    {
        var pipeline = Pipeline($$"""[ { "{{stage}}": "other_collection" } ]""");

        var ex = Assert.Throws<PipelineRejectedException>(() => PipelineGuard.Validate(pipeline));
        Assert.Contains(stage, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$lookup")]
    [InlineData("$unionWith")]
    [InlineData("$graphLookup")]
    public void Cross_collection_stages_are_rejected(string stage)
    {
        var pipeline = Pipeline($$"""[ { "{{stage}}": { "from": "secrets" } } ]""");

        Assert.Throws<PipelineRejectedException>(() => PipelineGuard.Validate(pipeline));
    }

    [Fact]
    public void Server_side_code_is_rejected_even_when_nested_in_an_allowed_stage()
    {
        // $function inside $match would otherwise slip past a stage-name-only check.
        var pipeline = Pipeline("""
            [ { "$match": { "$expr": { "$function": { "body": "function(){return true}", "args": [], "lang": "js" } } } } ]
            """);

        var ex = Assert.Throws<PipelineRejectedException>(() => PipelineGuard.Validate(pipeline));
        Assert.Contains("$function", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_is_rejected_when_nested()
    {
        var pipeline = Pipeline("""[ { "$match": { "$where": "this.a == 1" } } ]""");

        Assert.Throws<PipelineRejectedException>(() => PipelineGuard.Validate(pipeline));
    }

    [Fact]
    public void Unknown_stages_are_rejected_rather_than_passed_through()
    {
        var pipeline = Pipeline("""[ { "$somethingNew": {} } ]""");

        Assert.Throws<PipelineRejectedException>(() => PipelineGuard.Validate(pipeline));
    }

    [Fact]
    public void A_stage_with_multiple_operators_is_rejected()
    {
        var pipeline = Pipeline("""[ { "$match": {}, "$out": "x" } ]""");

        Assert.Throws<PipelineRejectedException>(() => PipelineGuard.Validate(pipeline));
    }
}

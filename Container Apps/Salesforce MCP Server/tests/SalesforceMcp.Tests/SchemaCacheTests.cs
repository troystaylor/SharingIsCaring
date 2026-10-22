using System.Net;
using SalesforceMcp.Configuration;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tests;

public sealed class SchemaCacheTests
{
    private static readonly DateTimeOffset Modified = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Ok(string json, DateTimeOffset? lastModified = null) =>
        FakeSalesforce.Json(HttpStatusCode.OK, json, lastModified);

    private static int DescribeCalls(Harness h) => h.Fake.ApiCalls.Count(c => c.Path.EndsWith("/describe"));

    /// <summary>An org that serves a description with a Last-Modified date and honors If-Modified-Since.</summary>
    private static Harness Conditional(Action<SalesforceOptions>? configure = null)
    {
        var h = new Harness(configure);
        h.Fake.Api = call => call.IfModifiedSince is { } since && since >= Modified
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(Schemas.Demo, Modified);
        return h;
    }

    [Fact]
    public async Task A_second_call_inside_the_window_is_served_from_memory()
    {
        var h = Conditional();

        var first = await h.Read.DescribeObject("Demo__c");
        var second = await h.Read.DescribeObject("Demo__c");

        Assert.Equal(1, DescribeCalls(h));
        Assert.Equal("fetched", (string?)first["schema"]!["source"]);
        Assert.Equal("cache", (string?)second["schema"]!["source"]);
        Assert.Equal((string?)first["schema"]!["retrievedAt"], (string?)second["schema"]!["retrievedAt"]);
    }

    [Fact]
    public async Task After_the_window_the_cache_revalidates_and_a_304_reuses_the_stored_description()
    {
        var h = Conditional();
        await h.Read.DescribeObject("Demo__c");

        h.Time.Advance(TimeSpan.FromSeconds(301));
        var result = await h.Read.DescribeObject("Demo__c");

        var conditional = h.Fake.ApiCalls.Last(c => c.Path.EndsWith("/describe"));
        Assert.Equal(Modified, conditional.IfModifiedSince);
        Assert.Equal(2, DescribeCalls(h));
        Assert.Equal("revalidated", (string?)result["schema"]!["source"]);
        Assert.Equal("Demo__c", (string?)result["name"]);
        Assert.Equal(6, (int?)result["totalFields"]);

        // The revalidation restarts the window.
        var third = await h.Read.DescribeObject("Demo__c");
        Assert.Equal(2, DescribeCalls(h));
        Assert.Equal("cache", (string?)third["schema"]!["source"]);
    }

    [Fact]
    public async Task A_changed_description_replaces_the_cached_one()
    {
        var h = new Harness();
        var newer = Modified.AddDays(1);
        var current = (Schemas.Demo, Modified);
        h.Fake.Api = call => call.IfModifiedSince is { } since && since >= current.Item2
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(current.Item1, current.Item2);

        var before = await h.Read.DescribeObject("Demo__c");
        current = (Schemas.WithoutField(Schemas.Demo, "Owner__c"), newer);
        h.Time.Advance(TimeSpan.FromSeconds(301));
        var after = await h.Read.DescribeObject("Demo__c");

        Assert.Equal(6, (int?)before["totalFields"]);
        Assert.Equal(5, (int?)after["totalFields"]);
        Assert.Equal("fetched", (string?)after["schema"]!["source"]);

        h.Fake.Api = _ => throw new InvalidOperationException("must be served from the refreshed cache");
        Assert.Equal(5, (int?)(await h.Read.DescribeObject("Demo__c"))["totalFields"]);
    }

    [Fact]
    public async Task Without_a_last_modified_header_the_description_is_fetched_again_after_the_window()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok(Schemas.Demo);

        await h.Read.DescribeObject("Demo__c");
        h.Time.Advance(TimeSpan.FromSeconds(301));
        var result = await h.Read.DescribeObject("Demo__c");

        Assert.All(h.Fake.ApiCalls.Where(c => c.Path.EndsWith("/describe")), c => Assert.Null(c.IfModifiedSince));
        Assert.Equal(2, DescribeCalls(h));
        Assert.Equal("fetched", (string?)result["schema"]!["source"]);
    }

    [Fact]
    public async Task A_zero_second_window_revalidates_every_call()
    {
        var h = Conditional(o => o.SchemaCacheSeconds = 0);

        await h.Read.DescribeObject("Demo__c");
        var second = await h.Read.DescribeObject("Demo__c");
        var third = await h.Read.DescribeObject("Demo__c");

        Assert.Equal(3, DescribeCalls(h));
        Assert.Equal("revalidated", (string?)second["schema"]!["source"]);
        Assert.Equal("revalidated", (string?)third["schema"]!["source"]);
    }

    [Fact]
    public async Task Each_api_version_and_object_has_its_own_cache_entry()
    {
        var h = Conditional();

        await h.Schema.DescribeAsync("Demo__c", "v66.0", default);
        await h.Schema.DescribeAsync("Demo__c", "v52.0", default);
        await h.Schema.DescribeAsync("Other__c", "v66.0", default);
        await h.Schema.DescribeAsync("demo__c", "v66.0", default);

        Assert.Equal(3, DescribeCalls(h));
        Assert.Equal(
            ["/services/data/v66.0/sobjects/Demo__c/describe", "/services/data/v52.0/sobjects/Demo__c/describe", "/services/data/v66.0/sobjects/Other__c/describe"],
            h.Fake.ApiCalls.Select(c => c.Path));
    }

    [Fact]
    public async Task The_cache_is_bounded_and_drops_the_oldest_entry_first()
    {
        var h = Conditional(o => o.SchemaCacheMaxEntries = 2);

        foreach (var name in new[] { "A__c", "B__c", "C__c" })
        {
            await h.Schema.DescribeAsync(name, "v66.0", default);
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(2, h.Cache.Count);
        Assert.Equal(3, DescribeCalls(h));

        await h.Schema.DescribeAsync("C__c", "v66.0", default);
        await h.Schema.DescribeAsync("B__c", "v66.0", default);
        Assert.Equal(3, DescribeCalls(h));

        await h.Schema.DescribeAsync("A__c", "v66.0", default);
        Assert.Equal(4, DescribeCalls(h));
    }

    [Fact]
    public async Task Changing_a_returned_description_does_not_change_what_is_cached()
    {
        var h = Conditional();

        var first = await h.Schema.DescribeAsync("Demo__c", "v66.0", default);
        first.Body["name"] = "Tampered";
        first.Body["fields"]!.AsArray().Clear();

        var second = await h.Schema.DescribeAsync("Demo__c", "v66.0", default);

        Assert.Equal("Demo__c", (string?)second.Body["name"]);
        Assert.Equal(6, second.Body["fields"]!.AsArray().Count);
    }

    [Fact]
    public async Task A_failed_revalidation_surfaces_the_error_and_keeps_the_stored_description()
    {
        var h = Conditional();
        await h.Schema.DescribeAsync("Demo__c", "v66.0", default);

        h.Time.Advance(TimeSpan.FromSeconds(301));
        h.Fake.Api = _ => FakeSalesforce.Json(HttpStatusCode.ServiceUnavailable, "<html>down</html>");
        await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => h.Schema.DescribeAsync("Demo__c", "v66.0", default));

        h.Fake.Api = call => call.IfModifiedSince is not null
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(Schemas.Demo, Modified);
        var recovered = await h.Schema.DescribeAsync("Demo__c", "v66.0", default);
        Assert.Equal(SchemaSource.Revalidated, recovered.Source);
    }

    [Fact]
    public async Task The_object_list_is_cached_and_revalidated_the_same_way()
    {
        var h = new Harness();
        var listModified = Modified;
        h.Fake.Api = call => call.IfModifiedSince >= listModified
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok("""{"sobjects":[{"name":"Demo__c","label":"Demo","custom":true,"queryable":true,"keyPrefix":"a01"}]}""", listModified);

        var first = await h.Read.ListObjects();
        var second = await h.Read.ListObjects();
        h.Time.Advance(TimeSpan.FromSeconds(301));
        var third = await h.Read.ListObjects();

        Assert.Equal(["fetched", "cache", "revalidated"],
            new[] { first, second, third }.Select(r => (string?)r["schema"]!["source"]));
        Assert.Equal(2, h.Fake.ApiCalls.Count(c => c.Path == "/services/data/v66.0/sobjects"));
        Assert.Equal("Demo__c", (string?)third["objects"]![0]!["name"]);
    }

    [Fact]
    public async Task Describe_object_reports_the_version_it_used()
    {
        var h = new Harness();
        h.Fake.Api = call => call.Path == "/services/data/"
            ? Ok(Schemas.VersionList)
            : Ok(Schemas.Demo);

        var result = await h.Read.DescribeObject("Demo__c", apiVersion: "v52.0");

        Assert.Equal("v52.0", (string?)result["apiVersion"]);
        Assert.Equal("/services/data/v52.0/sobjects/Demo__c/describe", h.Fake.ApiCalls.Last().Path);
    }
}

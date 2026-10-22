using System.Net;
using ModelContextProtocol;
using SalesforceMcp.Configuration;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tests;

public sealed class VersionTests
{
    private static HttpResponseMessage Ok(string json) => FakeSalesforce.Json(HttpStatusCode.OK, json);

    private static bool IsVersionList(Call call) => call.Path == "/services/data/";

    /// <summary>A harness whose org supports v52.0, v66.0 and v67.0, and answers every other call with one account.</summary>
    private static Harness Org(Action<SalesforceOptions>? configure = null, Func<Call, HttpResponseMessage>? other = null)
    {
        var h = new Harness(configure);
        h.Fake.Api = call => IsVersionList(call) ? Ok(Schemas.VersionList) : other?.Invoke(call) ?? Ok(Sample.Query(1));
        return h;
    }

    [Theory]
    [InlineData("v52.0")]
    [InlineData("V52.0")]
    [InlineData("52.0")]
    [InlineData("v52")]
    [InlineData("52")]
    [InlineData("  v52.0  ")]
    public void Normalizes_every_common_way_of_writing_a_version(string requested) =>
        Assert.Equal("v52.0", SalesforceVersions.Normalize(requested));

    [Theory]
    [InlineData("v52.5")]
    [InlineData("v5")]
    [InlineData("latest")]
    [InlineData("v52.0/../limits")]
    [InlineData("v52.0?x=1")]
    [InlineData("v1234")]
    public void Rejects_anything_that_is_not_a_whole_numbered_version(string requested) =>
        Assert.Throws<McpException>(() => SalesforceVersions.Normalize(requested));

    [Fact]
    public async Task Without_an_override_the_server_default_is_used_and_the_org_is_not_asked()
    {
        var h = Org();

        await h.Read.SoqlQuery("SELECT Id FROM Account");

        var call = Assert.Single(h.Fake.ApiCalls);
        Assert.Equal("/services/data/v66.0/query", call.Path);
    }

    [Fact]
    public async Task A_supported_override_is_used_in_the_request_path()
    {
        var h = Org();

        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "52");

        Assert.Equal(
            ["/services/data/", "/services/data/v52.0/query"],
            h.Fake.ApiCalls.Select(c => c.Path));
    }

    [Fact]
    public async Task An_override_equal_to_the_default_is_not_checked_against_the_org()
    {
        var h = Org();

        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v66.0");

        Assert.DoesNotContain(h.Fake.ApiCalls, IsVersionList);
    }

    [Fact]
    public async Task An_unsupported_version_is_refused_before_any_data_call_and_names_the_range()
    {
        var h = Org();

        var ex = await Assert.ThrowsAsync<McpException>(() => h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v99.0"));

        Assert.Contains("does not support API version v99.0", ex.Message);
        Assert.Contains("v52.0 through v67.0", ex.Message);
        Assert.Single(h.Fake.ApiCalls);
        Assert.True(IsVersionList(h.Fake.ApiCalls.Single()));
    }

    [Fact]
    public async Task A_malformed_version_never_reaches_salesforce()
    {
        var h = Org();

        await Assert.ThrowsAsync<McpException>(() => h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v52.0/../limits"));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task The_org_version_list_is_fetched_once_then_refreshed_after_an_hour()
    {
        var h = Org();

        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v52.0");
        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v67.0");
        Assert.Equal(1, h.Fake.ApiCalls.Count(IsVersionList));

        h.Time.Advance(TimeSpan.FromMinutes(61));
        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v52.0");
        Assert.Equal(2, h.Fake.ApiCalls.Count(IsVersionList));
    }

    [Fact]
    public async Task When_the_version_list_cannot_be_fetched_the_version_is_passed_through()
    {
        var h = new Harness();
        h.Fake.Api = call => IsVersionList(call)
            ? FakeSalesforce.Json(HttpStatusCode.InternalServerError, "{}")
            : Ok(Sample.Query(1));

        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v52.0");

        Assert.Equal("/services/data/v52.0/query", h.Fake.ApiCalls.Last().Path);
    }

    [Fact]
    public async Task List_api_versions_reports_default_latest_and_whether_the_default_is_supported()
    {
        var h = Org();

        var result = await h.Read.ListApiVersions();

        Assert.Equal("v66.0", (string?)result["default"]);
        Assert.True((bool?)result["defaultSupported"]);
        Assert.Equal("v67.0", (string?)result["latest"]);
        Assert.Equal("Winter '27", (string?)result["latestRelease"]);
        Assert.Equal(["v52.0", "v66.0", "v67.0"], result["versions"]!.AsArray().Select(v => (string?)v));
    }

    [Fact]
    public async Task List_api_versions_flags_a_default_the_org_does_not_support()
    {
        var h = Org(o => o.ApiVersion = "v60.0");

        var result = await h.Read.ListApiVersions();

        Assert.False((bool?)result["defaultSupported"]);
    }

    [Fact]
    public async Task Versions_are_sorted_numerically_not_alphabetically()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""[{"version":"100.0","label":"Future"},{"version":"9.0","label":"Old"},{"version":"66.0","label":"Now"}]""");

        // "9.0" has one digit and is not a version this server accepts, so only 66.0 and 100.0 are kept.
        var list = await h.Versions.ListAsync(default);

        Assert.Equal(["v66.0", "v100.0"], list.Select(v => v.Version));
    }

    [Fact]
    public async Task Create_and_update_accept_an_override_and_delete_uses_the_default()
    {
        var h = Org(other: call => call.Method == HttpMethod.Post
            ? FakeSalesforce.Json(HttpStatusCode.Created, """{"id":"001xx000003DGb1AAG","success":true}""")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        var fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>("""{"Name":"x"}""")!;

        await h.Write.CreateRecord("Account", fields, apiVersion: "v52.0");
        await h.Write.UpdateRecord("Account", Sample.AccountId, fields, apiVersion: "v67.0");
        await h.Write.DeleteRecord("Account", Sample.AccountId);

        Assert.Equal(
            ["/services/data/v52.0/sobjects/Account",
             "/services/data/v67.0/sobjects/Account/" + Sample.AccountId,
             "/services/data/v66.0/sobjects/Account/" + Sample.AccountId],
            h.Fake.ApiCalls.Where(c => !IsVersionList(c)).Select(c => c.Path));
    }
}

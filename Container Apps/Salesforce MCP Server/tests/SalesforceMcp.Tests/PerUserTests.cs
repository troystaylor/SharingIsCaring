using System.Net;
using ModelContextProtocol;
using SalesforceMcp.Configuration;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tests;

/// <summary>The caller's own Salesforce token is what reaches Salesforce, and nothing else does.</summary>
public sealed class PerUserTests
{
    private static HttpResponseMessage Ok(string json) => FakeSalesforce.Json(HttpStatusCode.OK, json);

    [Fact]
    public async Task Calls_to_salesforce_carry_the_callers_own_token()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok(Sample.Query(1));

        await h.Read.SoqlQuery("SELECT Id FROM Account");
        h.User.SignInAs(FakeSalesforce.BobToken, FakeSalesforce.BobId);
        await h.Read.SoqlQuery("SELECT Id FROM Account");

        Assert.Equal(
            [$"Bearer {FakeSalesforce.AliceToken}", $"Bearer {FakeSalesforce.BobToken}"],
            h.Fake.ApiCalls.Select(c => c.Authorization));
    }

    [Fact]
    public async Task A_request_with_no_token_is_refused_before_anything_is_sent_to_salesforce()
    {
        var h = new Harness();
        h.User.AccessToken = null;

        var ex = await Assert.ThrowsAsync<McpException>(() => h.Read.SoqlQuery("SELECT Id FROM Account"));

        Assert.Contains("no Salesforce sign-in", ex.Message);
        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task An_expired_or_revoked_token_is_reported_and_not_retried()
    {
        var h = new Harness();
        h.Fake.Api = _ => FakeSalesforce.Json(HttpStatusCode.Unauthorized,
            """[{"message":"Session expired or invalid","errorCode":"INVALID_SESSION_ID"}]""");

        var ex = await Assert.ThrowsAsync<McpException>(() => h.Read.SoqlQuery("SELECT Id FROM Account"));

        Assert.Contains("expired or been revoked", ex.Message);
        Assert.Contains("Sign in to Salesforce again", ex.Message);
        Assert.Single(h.Fake.ApiCalls);
    }

    [Fact]
    public async Task Object_descriptions_are_never_shared_between_users()
    {
        var h = new Harness();
        h.Fake.Api = call => Ok(call.Authorization!.Contains(FakeSalesforce.AliceToken)
            ? Schemas.Demo
            : Schemas.WithoutField(Schemas.Demo, "Owner__c"));

        var alice = await h.Read.DescribeObject("Demo__c");
        h.User.SignInAs(FakeSalesforce.BobToken, FakeSalesforce.BobId);
        var bob = await h.Read.DescribeObject("Demo__c");
        var bobAgain = await h.Read.DescribeObject("Demo__c");

        Assert.Equal(6, (int?)alice["totalFields"]);
        Assert.Equal(5, (int?)bob["totalFields"]);
        Assert.Equal("fetched", (string?)bob["schema"]!["source"]);
        Assert.Equal("cache", (string?)bobAgain["schema"]!["source"]);
        Assert.Equal(2, h.Fake.ApiCalls.Count(c => c.Path.EndsWith("/describe")));
    }

    [Fact]
    public async Task The_object_list_is_also_kept_per_user()
    {
        var h = new Harness();
        h.Fake.Api = call => Ok(call.Authorization!.Contains(FakeSalesforce.AliceToken)
            ? """{"sobjects":[{"name":"Secret__c","label":"Secret","queryable":true}]}"""
            : """{"sobjects":[{"name":"Public__c","label":"Public","queryable":true}]}""");

        var alice = await h.Read.ListObjects();
        h.User.SignInAs(FakeSalesforce.BobToken, FakeSalesforce.BobId);
        var bob = await h.Read.ListObjects();

        Assert.Equal("Secret__c", (string?)alice["objects"]![0]!["name"]);
        Assert.Equal("Public__c", (string?)bob["objects"]![0]!["name"]);
    }

    [Fact]
    public async Task The_org_wide_version_list_is_shared_because_it_is_the_same_for_everyone()
    {
        var h = new Harness();
        h.Fake.Api = call => call.Path == "/services/data/" ? Ok(Schemas.VersionList) : Ok(Sample.Query(1));

        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v52.0");
        h.User.SignInAs(FakeSalesforce.BobToken, FakeSalesforce.BobId);
        await h.Read.SoqlQuery("SELECT Id FROM Account", apiVersion: "v52.0");

        Assert.Equal(1, h.Fake.ApiCalls.Count(c => c.Path == "/services/data/"));
    }

    [Fact]
    public async Task Without_a_checked_user_the_cache_is_still_keyed_by_the_token()
    {
        var h = new Harness();
        h.Fake.Api = call => Ok(call.Authorization!.Contains("one") ? Schemas.Demo : Schemas.WithoutField(Schemas.Demo, "Owner__c"));
        h.User.UserId = null;

        h.User.AccessToken = "token-one";
        var first = await h.Read.DescribeObject("Demo__c");
        h.User.AccessToken = "token-two";
        var second = await h.Read.DescribeObject("Demo__c");

        Assert.Equal(6, (int?)first["totalFields"]);
        Assert.Equal(5, (int?)second["totalFields"]);
    }

    [Fact]
    public async Task Connection_info_names_the_signed_in_user_not_a_service_account()
    {
        var h = new Harness();
        h.Fake.Api = call => call.Q!.Contains("FROM Organization")
            ? Ok("""{"records":[{"attributes":{"type":"Organization"},"Id":"00Dxx0000000001EAA","Name":"Acme","IsSandbox":false}]}""")
            : Ok("""{"records":[{"attributes":{"type":"User"},"Id":"005xx0000000001AAA","Name":"Alice","Profile":{"attributes":{"type":"Profile"},"Name":"Sales"}}]}""");

        var result = await h.Read.GetConnectionInfo();

        Assert.Equal("Alice", (string?)result["signedInUser"]!["Name"]);
        Assert.Null(result["runAsUser"]);
        Assert.Contains($"Id = '{FakeSalesforce.AliceId}'", h.Fake.ApiCalls.Last().Q);
    }
}

public sealed class TokenValidatorTests
{
    [Fact]
    public async Task A_token_salesforce_accepts_yields_the_owner_and_org()
    {
        var h = new Harness();

        var info = await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);

        Assert.Equal(FakeSalesforce.AliceId, info!.UserId);
        Assert.Equal("00Dxx0000000001EAA", info.OrganizationId);
        Assert.Equal("alice@example.com", info.Username);
        Assert.Equal("Alice", info.DisplayName);

        var call = Assert.Single(h.Fake.UserInfoCalls);
        Assert.Equal("https://acme.my.salesforce.com/services/oauth2/userinfo", call.Url);
        Assert.Equal($"Bearer {FakeSalesforce.AliceToken}", call.Authorization);
    }

    [Fact]
    public async Task A_token_salesforce_refuses_yields_nothing()
    {
        var h = new Harness();

        Assert.Null(await h.Validator.ValidateAsync("forged-token", default));
    }

    [Fact]
    public async Task An_accepted_token_is_trusted_for_the_configured_window_and_then_checked_again()
    {
        var h = new Harness();
        h.Auth.ValidationCacheSeconds = 60;

        await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);
        await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);
        Assert.Single(h.Fake.UserInfoCalls);

        h.Time.Advance(TimeSpan.FromSeconds(61));
        await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);
        Assert.Equal(2, h.Fake.UserInfoCalls.Count());
    }

    [Fact]
    public async Task A_zero_second_window_checks_every_time()
    {
        var h = new Harness();
        h.Auth.ValidationCacheSeconds = 0;

        await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);
        await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);

        Assert.Equal(2, h.Fake.UserInfoCalls.Count());
    }

    [Fact]
    public async Task A_refused_token_is_remembered_only_briefly_so_a_fresh_sign_in_is_not_locked_out()
    {
        var h = new Harness();

        await h.Validator.ValidateAsync("forged-token", default);
        await h.Validator.ValidateAsync("forged-token", default);
        Assert.Single(h.Fake.UserInfoCalls);

        h.Time.Advance(TimeSpan.FromSeconds(11));
        await h.Validator.ValidateAsync("forged-token", default);
        Assert.Equal(2, h.Fake.UserInfoCalls.Count());
    }

    [Fact]
    public async Task Each_token_is_checked_on_its_own()
    {
        var h = new Harness();

        var alice = await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default);
        var bob = await h.Validator.ValidateAsync(FakeSalesforce.BobToken, default);

        Assert.NotEqual(alice!.UserId, bob!.UserId);
        Assert.Equal(2, h.Fake.UserInfoCalls.Count());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_salesforce_outage_is_not_mistaken_for_a_bad_token_and_is_not_remembered(HttpStatusCode status)
    {
        var h = new Harness();
        h.Fake.UserInfo = _ => FakeSalesforce.Json(status, "<html>down</html>");

        await Assert.ThrowsAsync<SalesforceUnavailableException>(() => h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default));

        h.Fake.UserInfo = null;
        Assert.NotNull(await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default));
    }

    [Theory]
    [InlineData("""{"error":"x"}""")]
    [InlineData("""{"user_id":"005xx0000000001AAA"}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task A_response_without_a_user_and_org_is_not_accepted(string body)
    {
        var h = new Harness();
        h.Fake.UserInfo = _ => FakeSalesforce.Json(HttpStatusCode.OK, body);

        Assert.Null(await h.Validator.ValidateAsync(FakeSalesforce.AliceToken, default));
    }

    [Theory]
    [InlineData("Bearer abc", "abc")]
    [InlineData("bearer abc", "abc")]
    [InlineData("Bearer   abc  ", "abc")]
    [InlineData("Bearer ", null)]
    [InlineData("Basic abc", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Reads_the_bearer_token_from_the_authorization_header(string? header, string? expected) =>
        Assert.Equal(expected, HttpContextSalesforceUser.BearerToken(header));

    [Fact]
    public void The_cache_key_is_the_user_when_known_and_a_hash_of_the_token_when_not()
    {
        Assert.Equal("005xx0000000001AAA", HttpContextSalesforceUser.CacheKeyFor("005xx0000000001AAA", "t"));

        var viaToken = HttpContextSalesforceUser.CacheKeyFor(null, "secret-token");
        Assert.StartsWith("token-", viaToken);
        Assert.DoesNotContain("secret-token", viaToken);
        Assert.NotEqual(viaToken, HttpContextSalesforceUser.CacheKeyFor(null, "other-token"));
        Assert.Equal("anonymous", HttpContextSalesforceUser.CacheKeyFor(null, null));
    }
}

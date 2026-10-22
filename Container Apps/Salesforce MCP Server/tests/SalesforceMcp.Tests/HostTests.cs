using System.Net;
using System.Text.Json;

namespace SalesforceMcp.Tests;

public sealed class HostTests
{
    private static readonly string[] ReadTools =
    [
        "describe_object", "get_connection_info", "get_limits", "get_record", "list_api_versions", "list_objects",
        "query_more", "soql_query", "sosl_search"
    ];

    private static readonly string[] SchemaTools =
    [
        "compare_api_versions", "compare_schema", "get_object_info", "get_page_layout", "get_picklist_values",
        "get_record_types", "get_schema_snapshot"
    ];

    private static readonly string[] ToolingTools =
        ["get_field_metadata", "get_object_metadata", "list_custom_fields", "list_validation_rules"];

    private static readonly string[] WriteTools = ["create_record", "delete_record", "update_record"];

    private static readonly string[] DefaultTools = [.. ReadTools, .. SchemaTools, .. ToolingTools];

    private static HttpResponseMessage Ok(string json) => FakeSalesforce.Json(HttpStatusCode.OK, json);

    [Fact]
    public async Task Default_deployment_exposes_only_read_tools_and_marks_them_read_only()
    {
        using var app = new TestApp();
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var tools = await Mcp.ListToolsAsync(client);

        Assert.Equal(DefaultTools.Order(), tools.Select(t => t.GetProperty("name").GetString()!).Order());
        Assert.All(tools, t => Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
    }

    [Fact]
    public async Task Tooling_tools_can_be_switched_off()
    {
        using var app = new TestApp().With("Salesforce:EnableToolingTools", "false");
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var names = await Mcp.ListToolNamesAsync(client);

        Assert.Equal(ReadTools.Concat(SchemaTools).Order(), names);
    }

    [Fact]
    public async Task Every_data_tool_offers_an_api_version_override()
    {
        using var app = new TestApp().With("Salesforce:EnableWriteTools", "true");
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var tools = await Mcp.ListToolsAsync(client);
        string[] exempt = ["list_api_versions", "query_more", "get_connection_info", "delete_record", "compare_api_versions"];

        foreach (var tool in tools.Where(t => !exempt.Contains(t.GetProperty("name").GetString())))
        {
            Assert.True(
                tool.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("apiVersion", out _),
                $"{tool.GetProperty("name").GetString()} has no apiVersion parameter");
        }
    }

    [Fact]
    public async Task Write_tools_appear_only_when_enabled_and_are_marked_as_changing_data()
    {
        using var app = new TestApp().With("Salesforce:EnableWriteTools", "true");
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var tools = await Mcp.ListToolsAsync(client);

        Assert.Equal(DefaultTools.Concat(WriteTools).Order(), tools.Select(t => t.GetProperty("name").GetString()!).Order());
        foreach (var t in tools.Where(t => WriteTools.Contains(t.GetProperty("name").GetString())))
        {
            Assert.False(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        }

        var delete = tools.Single(t => t.GetProperty("name").GetString() == "delete_record");
        Assert.True(delete.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean());
    }

    [Fact]
    public async Task A_tool_call_runs_end_to_end_with_the_callers_own_salesforce_token()
    {
        using var app = new TestApp();
        app.Salesforce.Api = _ => Ok(Sample.Query(1));
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var (isError, text) = await Mcp.CallToolAsync(client, "soql_query", new { query = "SELECT Id, Name FROM Account LIMIT 1" });

        Assert.False(isError);
        using var payload = JsonDocument.Parse(text);
        Assert.Equal("https://acme.my.salesforce.com/001xx000003DGb001AAG",
            payload.RootElement.GetProperty("records")[0].GetProperty("recordUrl").GetString());
        Assert.Equal($"Bearer {FakeSalesforce.AliceToken}", app.Salesforce.ApiCalls.Single().Authorization);
    }

    [Fact]
    public async Task Two_callers_each_reach_salesforce_as_themselves_and_never_as_each_other()
    {
        using var app = new TestApp();
        app.Salesforce.Api = _ => Ok(Sample.Query(1));
        using var alice = app.ClientWith(FakeSalesforce.AliceToken);
        using var bob = app.ClientWith(FakeSalesforce.BobToken);

        await Mcp.CallToolAsync(alice, "soql_query", new { query = "SELECT Id FROM Account" });
        await Mcp.CallToolAsync(bob, "soql_query", new { query = "SELECT Id FROM Account" });
        await Mcp.CallToolAsync(alice, "soql_query", new { query = "SELECT Id FROM Account" });

        Assert.Equal(
            [$"Bearer {FakeSalesforce.AliceToken}", $"Bearer {FakeSalesforce.BobToken}", $"Bearer {FakeSalesforce.AliceToken}"],
            app.Salesforce.ApiCalls.Select(c => c.Authorization));
    }

    [Fact]
    public async Task Descriptions_fetched_for_one_caller_are_not_served_to_another()
    {
        using var app = new TestApp();
        app.Salesforce.Api = call => call.Path.EndsWith("/describe")
            ? Ok(call.Authorization!.Contains(FakeSalesforce.AliceToken) ? Schemas.Demo : Schemas.WithoutField(Schemas.Demo, "Owner__c"))
            : Ok("{}");
        using var alice = app.ClientWith(FakeSalesforce.AliceToken);
        using var bob = app.ClientWith(FakeSalesforce.BobToken);

        var (_, forAlice) = await Mcp.CallToolAsync(alice, "describe_object", new { sobject = "Demo__c" });
        var (_, forBob) = await Mcp.CallToolAsync(bob, "describe_object", new { sobject = "Demo__c" });

        Assert.Contains("Owner__c", forAlice);
        Assert.DoesNotContain("Owner__c", forBob);
    }

    [Fact]
    public async Task A_salesforce_error_comes_back_as_a_tool_error_the_model_can_read()
    {
        using var app = new TestApp();
        app.Salesforce.Api = _ => FakeSalesforce.Json(HttpStatusCode.BadRequest,
            """[{"message":"No such column 'Nmae' on entity 'Account'.","errorCode":"INVALID_FIELD"}]""");
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var (isError, text) = await Mcp.CallToolAsync(client, "soql_query", new { query = "SELECT Nmae FROM Account" });

        Assert.True(isError);
        Assert.Contains("INVALID_FIELD", text);
    }

    [Fact]
    public async Task A_request_with_no_token_is_turned_away_and_pointed_at_how_to_sign_in()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        var (response, _) = await Mcp.CallAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", string.Join(' ', response.Headers.WwwAuthenticate.Select(h => h.ToString())));
        Assert.Empty(app.Salesforce.Calls);
    }

    [Fact]
    public async Task A_token_salesforce_refuses_is_turned_away_and_no_data_call_is_made()
    {
        using var app = new TestApp();
        using var client = app.ClientWith("forged-token");

        var (response, _) = await Mcp.CallAsync(client, "tools/call", new { name = "soql_query", arguments = new { query = "SELECT Id FROM Account" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Salesforce.ApiCalls);
        Assert.Single(app.Salesforce.UserInfoCalls);
    }

    [Fact]
    public async Task A_salesforce_outage_while_checking_a_sign_in_does_not_let_the_request_through()
    {
        using var app = new TestApp();
        app.Salesforce.UserInfo = _ => FakeSalesforce.Json(HttpStatusCode.ServiceUnavailable, "down");
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        var (response, _) = await Mcp.CallAsync(client, "tools/list");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Salesforce.ApiCalls);
    }

    [Fact]
    public async Task A_token_is_checked_with_salesforce_once_across_many_calls()
    {
        using var app = new TestApp();
        app.Salesforce.Api = _ => Ok(Sample.Query(1));
        using var client = app.ClientWith(FakeSalesforce.AliceToken);

        for (var i = 0; i < 4; i++)
        {
            await Mcp.CallToolAsync(client, "soql_query", new { query = "SELECT Id FROM Account" });
        }

        Assert.Single(app.Salesforce.UserInfoCalls);
    }

    [Fact]
    public async Task Publishes_protected_resource_metadata_naming_the_salesforce_org_as_the_sign_in_provider()
    {
        using var app = new TestApp();
        using var client = app.ClientWith();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var metadata = JsonDocument.Parse(body);
        Assert.Equal(
            ["https://acme.my.salesforce.com"],
            metadata.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(
            ["api", "refresh_token", "openid"],
            metadata.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public async Task Rate_limits_each_person_separately()
    {
        using var app = new TestApp().With("RateLimit:PermitsPerMinute", "2");
        using var alice = app.ClientWith(FakeSalesforce.AliceToken);
        using var bob = app.ClientWith(FakeSalesforce.BobToken);

        var aliceCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            aliceCodes.Add((await Mcp.CallAsync(alice, "tools/list")).Response.StatusCode);
        }

        var bobCode = (await Mcp.CallAsync(bob, "tools/list")).Response.StatusCode;

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], aliceCodes);
        Assert.Equal(HttpStatusCode.OK, bobCode);
    }

    [Fact]
    public async Task Readiness_reports_whether_salesforce_is_reachable_and_degrades_instead_of_failing()
    {
        using var app = new TestApp();
        app.Salesforce.Api = _ => Ok("""[{"version":"66.0","label":"Summer '26"}]""");
        using var client = app.ClientWith();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        var healthy = await client.GetAsync("/ready");
        Assert.Equal("Healthy", await healthy.Content.ReadAsStringAsync());
        Assert.All(app.Salesforce.Calls, c => Assert.Null(c.Authorization));
    }

    [Fact]
    public async Task Readiness_is_degraded_when_salesforce_is_down_but_liveness_is_unaffected()
    {
        using var app = new TestApp();
        app.Salesforce.Api = _ => FakeSalesforce.Json(HttpStatusCode.ServiceUnavailable, "down");
        using var client = app.ClientWith();

        var live = await client.GetAsync("/healthz");
        var ready = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_sign_in_checking_off_the_tools_can_be_listed_but_data_still_needs_a_token()
    {
        using var app = new TestApp().With("Auth:Enabled", "false");
        using var anonymous = app.ClientWith();

        Assert.NotEmpty(await Mcp.ListToolNamesAsync(anonymous));

        var (isError, text) = await Mcp.CallToolAsync(anonymous, "soql_query", new { query = "SELECT Id FROM Account" });
        Assert.True(isError);
        Assert.Contains("no Salesforce sign-in", text);
        Assert.Empty(app.Salesforce.Calls);
    }

    [Fact]
    public async Task With_sign_in_checking_off_a_supplied_token_is_still_passed_to_salesforce_unchecked()
    {
        using var app = new TestApp().With("Auth:Enabled", "false");
        app.Salesforce.Api = _ => Ok(Sample.Query(1));
        using var client = app.ClientWith("any-token");

        var (isError, _) = await Mcp.CallToolAsync(client, "soql_query", new { query = "SELECT Id FROM Account" });

        Assert.False(isError);
        Assert.Empty(app.Salesforce.UserInfoCalls);
        Assert.Equal("Bearer any-token", app.Salesforce.ApiCalls.Single().Authorization);
    }

    [Theory]
    [InlineData("Salesforce:InstanceUrl", "http://acme.my.salesforce.com", "https")]
    [InlineData("Salesforce:InstanceUrl", "", "Salesforce:InstanceUrl")]
    [InlineData("Salesforce:InstanceUrl", "https://acme.my.salesforce.com/services", "no path")]
    [InlineData("Salesforce:ApiVersion", "66", "Salesforce:ApiVersion")]
    [InlineData("Salesforce:SchemaCacheSeconds", "-1", "Salesforce:SchemaCacheSeconds")]
    [InlineData("Salesforce:SchemaCacheMaxEntries", "0", "Salesforce:SchemaCacheMaxEntries")]
    [InlineData("Auth:ValidationCacheSeconds", "-5", "Auth:ValidationCacheSeconds")]
    public void Refuses_to_start_with_a_bad_configuration(string key, string value, string expectedInMessage)
    {
        using var app = new TestApp().With(key, value);

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(expectedInMessage, ex.ToString());
    }

    [Fact]
    public void The_server_needs_no_salesforce_credential_of_its_own_to_start()
    {
        using var app = new TestApp();

        using var client = app.CreateClient();

        Assert.NotNull(client);
    }
}

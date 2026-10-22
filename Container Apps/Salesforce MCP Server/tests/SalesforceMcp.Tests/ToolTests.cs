using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace SalesforceMcp.Tests;

public sealed class ReadToolTests
{
    private static HttpResponseMessage Ok(string json) => FakeSalesforce.Json(HttpStatusCode.OK, json);

    [Fact]
    public async Task Soql_query_url_encodes_the_query_and_returns_cleaned_records()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok(Sample.Query(2));

        var result = await h.Read.SoqlQuery("SELECT Id, Name FROM Account WHERE Name = 'A&B' LIMIT 2");

        var call = Assert.Single(h.Fake.ApiCalls);
        Assert.Equal(
            "https://acme.my.salesforce.com/services/data/v66.0/query?q=SELECT%20Id%2C%20Name%20FROM%20Account%20WHERE%20Name%20%3D%20%27A%26B%27%20LIMIT%202",
            call.Url);

        Assert.Equal(2, (int?)result["returned"]);
        Assert.True((bool?)result["done"]);
        var first = result["records"]![0]!.AsObject();
        Assert.Null(first["attributes"]);
        Assert.Equal("Account", (string?)first["sobjectType"]);
        Assert.Equal("https://acme.my.salesforce.com/001xx000003DGb001AAG", (string?)first["recordUrl"]);
        Assert.Equal("User", (string?)first["Owner"]!["sobjectType"]);
        Assert.Null(first["Owner"]!["attributes"]);
    }

    [Fact]
    public async Task An_empty_result_is_a_success()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""{"totalSize":0,"done":true,"records":[]}""");

        var result = await h.Read.SoqlQuery("SELECT Id FROM Account WHERE Name = 'nothing'");

        Assert.Equal(0, (int?)result["returned"]);
        Assert.Empty(result["records"]!.AsArray());
    }

    [Fact]
    public async Task A_non_select_statement_never_reaches_salesforce()
    {
        var h = new Harness();

        await Assert.ThrowsAsync<McpException>(() => h.Read.SoqlQuery("DELETE FROM Account"));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task Offers_a_continuation_when_the_batch_is_complete_but_the_query_is_not()
    {
        var h = new Harness();
        const string next = "/services/data/v66.0/query/01gxx0000000001AAA-2000";
        h.Fake.Api = _ => Ok(Sample.Query(3, done: false, next: next));

        var result = await h.Read.SoqlQuery("SELECT Id FROM Account");

        Assert.False((bool?)result["done"]);
        Assert.Equal(next, (string?)result["nextRecordsUrl"]);
        Assert.Null(result["truncated"]);
    }

    [Fact]
    public async Task Truncating_a_batch_withholds_the_continuation_so_no_records_are_skipped()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok(Sample.Query(10, done: false, next: "/services/data/v66.0/query/01gxx0000000001AAA-2000"));

        var result = await h.Read.SoqlQuery("SELECT Id FROM Account", maxRecords: 3);

        Assert.Equal(3, (int?)result["returned"]);
        Assert.Equal(10, (int?)result["totalSize"]);
        Assert.True((bool?)result["truncated"]);
        Assert.Null(result["nextRecordsUrl"]);
        Assert.Contains("LIMIT", (string?)result["note"]);
    }

    [Fact]
    public async Task The_configured_record_cap_wins_over_the_requested_count()
    {
        var h = new Harness(o => o.MaxRecords = 4);
        h.Fake.Api = _ => Ok(Sample.Query(10));

        var result = await h.Read.SoqlQuery("SELECT Id FROM Account", maxRecords: 200);

        Assert.Equal(4, (int?)result["returned"]);
        Assert.True((bool?)result["truncated"]);
    }

    [Fact]
    public async Task The_character_budget_limits_output_but_always_keeps_one_record()
    {
        var h = new Harness(o => o.MaxResponseChars = 5_000);
        var big = new string('x', 6_000);
        h.Fake.Api = _ => Ok($$"""{"totalSize":2,"done":true,"records":[{"Id":"001xx000003DGb1AAG","Notes":"{{big}}"},{"Id":"001xx000003DGb2AAG","Notes":"{{big}}"}]}""");

        var result = await h.Read.SoqlQuery("SELECT Id, Notes FROM Account");

        Assert.Equal(1, (int?)result["returned"]);
        Assert.True((bool?)result["truncated"]);
    }

    [Fact]
    public async Task Query_more_follows_only_a_query_continuation_url()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok(Sample.Query(1));

        await h.Read.QueryMore("/services/data/v66.0/query/01gxx0000000001AAA-2000");

        Assert.Equal("/services/data/v66.0/query/01gxx0000000001AAA-2000", Assert.Single(h.Fake.ApiCalls).Path);
    }

    [Fact]
    public async Task Query_more_refuses_any_other_path_without_calling_salesforce()
    {
        var h = new Harness();

        await Assert.ThrowsAsync<McpException>(() => h.Read.QueryMore("/services/data/v66.0/sobjects/User"));
        await Assert.ThrowsAsync<McpException>(() => h.Read.QueryMore("https://evil.example/x"));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task Sosl_search_returns_cleaned_search_records()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok($$"""{"searchRecords":[{{Sample.Account(1)}}]}""");

        var result = await h.Read.SoslSearch("FIND {Acme} IN NAME FIELDS RETURNING Account(Id, Name)");

        Assert.Equal("/services/data/v66.0/search", Assert.Single(h.Fake.ApiCalls).Path);
        Assert.Equal(1, (int?)result["returned"]);
        Assert.Equal("Account", (string?)result["records"]![0]!["sobjectType"]);
    }

    [Fact]
    public async Task Get_record_requests_only_the_named_fields()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok(Sample.Account(1));

        var result = await h.Read.GetRecord("Account", Sample.AccountId, ["Name", "Industry"]);

        var call = Assert.Single(h.Fake.ApiCalls);
        Assert.Equal($"/services/data/v66.0/sobjects/Account/{Sample.AccountId}", call.Path);
        Assert.Equal("?fields=Name%2CIndustry", new Uri(call.Url).Query);
        Assert.Equal("Account", (string?)result["sobjectType"]);
    }

    [Fact]
    public async Task Get_record_rejects_a_field_name_that_is_really_a_query_fragment()
    {
        var h = new Harness();

        await Assert.ThrowsAsync<McpException>(() => h.Read.GetRecord("Account", Sample.AccountId, ["Name&q=x"]));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task List_objects_filters_by_name_or_label_and_by_queryable()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""
            {"sobjects":[
              {"name":"Account","label":"Account","custom":false,"queryable":true,"keyPrefix":"001"},
              {"name":"Invoice__c","label":"Invoice","custom":true,"queryable":true,"keyPrefix":"a01"},
              {"name":"InvoiceFeed","label":"Invoice Feed","custom":false,"queryable":false,"keyPrefix":null}
            ]}
            """);

        var result = await h.Read.ListObjects("invoice");

        Assert.Equal(1, (int?)result["totalMatches"]);
        Assert.Equal("Invoice__c", (string?)result["objects"]![0]!["name"]);

        var all = await h.Read.ListObjects("invoice", queryableOnly: false);
        Assert.Equal(2, (int?)all["totalMatches"]);
    }

    [Fact]
    public async Task Describe_object_trims_fields_to_what_a_query_author_needs()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""
            {"name":"Account","label":"Account","custom":false,"queryable":true,"createable":true,"updateable":true,"deletable":true,
             "fields":[
               {"name":"Name","label":"Account Name","type":"string","length":255,"nillable":false,"defaultedOnCreate":false,"custom":false,"createable":true,"updateable":true,"referenceTo":[],"picklistValues":[],"soapType":"xsd:string","extraNoise":{"a":1}},
               {"name":"Industry","label":"Industry","type":"picklist","length":40,"nillable":true,"custom":false,"createable":true,"updateable":true,"referenceTo":[],
                "picklistValues":[{"value":"Retail","label":"Retail","active":true},{"value":"Old","label":"Old","active":false}]},
               {"name":"OwnerId","label":"Owner ID","type":"reference","length":18,"nillable":false,"defaultedOnCreate":true,"custom":false,"createable":true,"updateable":true,"referenceTo":["User"],"relationshipName":"Owner","picklistValues":[]}
             ],
             "childRelationships":[{"childSObject":"Contact","field":"AccountId","relationshipName":"Contacts"},{"childSObject":"Hidden","field":"X","relationshipName":null}]}
            """);

        var result = await h.Read.DescribeObject("Account");

        Assert.Equal(3, (int?)result["matchingFields"]);
        var fields = result["fields"]!.AsArray();
        Assert.Null(fields[0]!["extraNoise"]);
        Assert.True((bool?)fields[0]!["required"]);
        Assert.False((bool?)fields[1]!["required"]);
        Assert.Equal(["Retail"], fields[1]!["picklistValues"]!.AsArray().Select(v => (string?)v));
        Assert.Equal("User", (string?)fields[2]!["referenceTo"]![0]);
        Assert.Equal("Owner", (string?)fields[2]!["relationshipName"]);
        Assert.False((bool?)fields[2]!["required"]);
        Assert.Equal("Contacts", (string?)Assert.Single(result["childRelationships"]!.AsArray())!["relationshipName"]);
    }

    [Fact]
    public async Task Describe_object_can_narrow_to_matching_fields()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""{"name":"Account","fields":[{"name":"Name","label":"Account Name","type":"string"},{"name":"Industry","label":"Industry","type":"picklist"}]}""");

        var result = await h.Read.DescribeObject("Account", fieldNameContains: "indust");

        Assert.Equal(2, (int?)result["totalFields"]);
        Assert.Equal("Industry", (string?)Assert.Single(result["fields"]!.AsArray())!["name"]);
    }

    [Fact]
    public async Task Get_limits_lists_only_limits_that_apply()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""{"DailyApiRequests":{"Max":15000,"Remaining":14000},"Unused":{"Max":0,"Remaining":0}}""");

        var result = await h.Read.GetLimits();

        var limit = Assert.Single(result["limits"]!.AsArray())!;
        Assert.Equal("DailyApiRequests", (string?)limit["name"]);
        Assert.Equal(1000, (int?)limit["used"]);
    }

    [Fact]
    public async Task Connection_info_reports_the_org_and_the_signed_in_user()
    {
        var h = new Harness();
        h.Fake.Api = call => call.Url.Contains("FROM%20Organization")
            ? Ok("""{"totalSize":1,"done":true,"records":[{"attributes":{"type":"Organization"},"Id":"00Dxx0000000001EAA","Name":"Acme","IsSandbox":true}]}""")
            : Ok("""{"totalSize":1,"done":true,"records":[{"attributes":{"type":"User"},"Id":"005xx0000000001AAA","Name":"Alice Rep","Profile":{"attributes":{"type":"Profile"},"Name":"Sales"}}]}""");

        var result = await h.Read.GetConnectionInfo();

        Assert.Equal("Acme", (string?)result["organization"]!["Name"]);
        Assert.Equal("Alice Rep", (string?)result["signedInUser"]!["Name"]);
        Assert.Equal("v66.0", (string?)result["apiVersion"]);
        Assert.Contains("WHERE%20Id%20%3D%20%27005xx0000000001AAA%27", h.Fake.ApiCalls.Last().Url);
    }
}

public sealed class WriteToolTests
{
    [Fact]
    public async Task Create_posts_the_fields_and_returns_the_new_id_and_link()
    {
        var h = new Harness();
        h.Fake.Api = _ => FakeSalesforce.Json(HttpStatusCode.Created, """{"id":"001xx000003DGb1AAG","success":true,"errors":[]}""");

        var result = await h.Write.CreateRecord("Account", Fields("""{"Name":"Acme","NumberOfEmployees":50}"""));

        var call = Assert.Single(h.Fake.ApiCalls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/services/data/v66.0/sobjects/Account", call.Path);
        var body = JsonNode.Parse(call.Body!)!;
        Assert.Equal("Acme", (string?)body["Name"]);
        Assert.Equal(50, (int?)body["NumberOfEmployees"]);
        Assert.Equal("001xx000003DGb1AAG", (string?)result["id"]);
        Assert.Equal("https://acme.my.salesforce.com/001xx000003DGb1AAG", (string?)result["recordUrl"]);
    }

    [Fact]
    public async Task Update_patches_only_the_given_fields()
    {
        var h = new Harness();
        h.Fake.Api = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await h.Write.UpdateRecord("Opportunity", Sample.AccountId, Fields("""{"StageName":"Closed Won"}"""));

        var call = Assert.Single(h.Fake.ApiCalls);
        Assert.Equal(HttpMethod.Patch, call.Method);
        Assert.Equal($"/services/data/v66.0/sobjects/Opportunity/{Sample.AccountId}", call.Path);
        Assert.Equal("""{"StageName":"Closed Won"}""", call.Body);
        Assert.True((bool?)result["updated"]);
    }

    [Fact]
    public async Task Delete_sends_a_delete()
    {
        var h = new Harness();
        h.Fake.Api = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await h.Write.DeleteRecord("Contact", Sample.AccountId);

        Assert.Equal(HttpMethod.Delete, Assert.Single(h.Fake.ApiCalls).Method);
        Assert.True((bool?)result["deleted"]);
    }

    [Fact]
    public async Task Rejects_bad_field_names_and_empty_bodies_before_calling_salesforce()
    {
        var h = new Harness();

        await Assert.ThrowsAsync<McpException>(() => h.Write.CreateRecord("Account", Fields("""{"Name\"":"x"}""")));
        await Assert.ThrowsAsync<McpException>(() => h.Write.CreateRecord("Account", Fields("""{"attributes":"x"}""")));
        await Assert.ThrowsAsync<McpException>(() => h.Write.CreateRecord("Account", []));
        await Assert.ThrowsAsync<McpException>(() => h.Write.UpdateRecord("Account", "not-an-id", Fields("""{"Name":"x"}""")));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task A_validation_failure_from_salesforce_reaches_the_model()
    {
        var h = new Harness();
        h.Fake.Api = _ => FakeSalesforce.Json(HttpStatusCode.BadRequest,
            """[{"message":"Required fields are missing: [Name]","errorCode":"REQUIRED_FIELD_MISSING","fields":["Name"]}]""");

        var ex = await Assert.ThrowsAsync<McpException>(() => h.Write.CreateRecord("Account", Fields("""{"Industry":"Retail"}""")));

        Assert.Contains("REQUIRED_FIELD_MISSING", ex.Message);
        Assert.Contains("Required fields are missing", ex.Message);
    }

    private static Dictionary<string, System.Text.Json.JsonElement> Fields(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json)!;
}

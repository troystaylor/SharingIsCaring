using System.Net;
using ModelContextProtocol;
using SalesforceMcp.Tools;

namespace SalesforceMcp.Tests;

public sealed class MetadataToolTests
{
    private static HttpResponseMessage Ok(string json) => FakeSalesforce.Json(HttpStatusCode.OK, json);

    private static bool IsTooling(Call call) => call.Path == "/services/data/v66.0/tooling/query";

    private static IEnumerable<Call> ToolingCalls(Harness h) => h.Fake.ApiCalls.Where(IsTooling);

    /// <summary>Serves a description for the object and routes Tooling queries to <paramref name="tooling"/>.</summary>
    private static Harness Org(Func<Call, HttpResponseMessage> tooling)
    {
        var h = new Harness();
        h.Fake.Api = call => call.Path.EndsWith("/describe") ? Ok(Schemas.Demo) : IsTooling(call) ? tooling(call) : Ok("{}");
        return h;
    }

    private static string Records(params string[] records) => "{\"size\":" + records.Length + ",\"records\":[" + string.Join(',', records) + "]}";

    [Fact]
    public async Task Object_metadata_adds_the_setup_description_for_a_custom_object()
    {
        var h = Org(_ => Ok(Records("""{"attributes":{"type":"CustomObject"},"Id":"01Ixx0000000001AAA","DeveloperName":"Demo","NamespacePrefix":null,"Description":"Tracks demos","ManageableState":"unmanaged"}""")));

        var result = await h.Metadata.GetObjectMetadata("Demo__c");

        Assert.Equal("Tracks demos", (string?)result["description"]);
        Assert.Equal("unmanaged", (string?)result["manageableState"]);
        Assert.Equal("Demo", (string?)result["label"]);
        Assert.Equal(6, (int?)result["fieldCount"]);
        Assert.Equal(4, (int?)result["customFieldCount"]);
        Assert.Equal(2, (int?)result["recordTypeCount"]);
        Assert.Equal(
            "SELECT Id, DeveloperName, NamespacePrefix, Description, ManageableState FROM CustomObject WHERE DeveloperName = 'Demo' AND NamespacePrefix = null",
            Assert.Single(ToolingCalls(h)).Q);
    }

    [Fact]
    public async Task A_namespaced_object_is_looked_up_by_namespace_and_developer_name()
    {
        var h = Org(_ => Ok(Records()));

        var result = await h.Metadata.GetObjectMetadata("acme__Demo__c");

        Assert.Contains("DeveloperName = 'Demo' AND NamespacePrefix = 'acme'", Assert.Single(ToolingCalls(h)).Q);
        Assert.Contains("no custom object record", (string?)result["setupNote"]);
    }

    [Theory]
    [InlineData("Demo__c", null, "Demo")]
    [InlineData("acme__Demo__c", "acme", "Demo")]
    [InlineData("acme__My_Demo__c", "acme", "My_Demo")]
    [InlineData("My_Demo__c", null, "My_Demo")]
    public void Splits_custom_object_names_into_namespace_and_developer_name(string apiName, string? ns, string developerName) =>
        Assert.Equal((ns, developerName), SalesforceMetadataTools.SplitCustomObjectName(apiName));

    [Fact]
    public async Task A_standard_object_has_no_setup_description_and_makes_no_tooling_call()
    {
        var h = Org(_ => Ok(Records()));

        var result = await h.Metadata.GetObjectMetadata("Account");

        Assert.Empty(ToolingCalls(h));
        Assert.Contains("custom objects", (string?)result["setupNote"]);
    }

    [Fact]
    public async Task A_tooling_permission_failure_becomes_a_note_and_the_rest_of_the_result_survives()
    {
        var h = Org(_ => FakeSalesforce.Json(HttpStatusCode.Forbidden,
            """[{"message":"You do not have access to the Tooling API","errorCode":"INSUFFICIENT_ACCESS"}]"""));

        var result = await h.Metadata.GetObjectMetadata("Demo__c");

        Assert.Equal("Demo", (string?)result["label"]);
        Assert.Contains("INSUFFICIENT_ACCESS", (string?)result["setupNote"]);
    }

    private const string FieldRows = """
        {"records":[
          {"QualifiedApiName":"Name","Label":"Demo Name","DataType":"Text(80)","Description":null,"InlineHelpText":null,"IsCalculated":false,"NamespacePrefix":null},
          {"QualifiedApiName":"Region__c","Label":"Region","DataType":"Text(40)","Description":"Sales region","InlineHelpText":"Pick the billing region","IsCalculated":false,"NamespacePrefix":null},
          {"QualifiedApiName":"Tier__c","Label":"Tier","DataType":"Picklist","Description":null,"InlineHelpText":"Customer tier","IsCalculated":false,"NamespacePrefix":null}]}
        """;

    [Fact]
    public async Task Field_definitions_default_to_custom_fields_with_their_descriptions()
    {
        var h = Org(_ => Ok(FieldRows));

        var result = await h.Metadata.ListCustomFields("Demo__c");

        Assert.Equal(["Region__c", "Tier__c"], result["fields"]!.AsArray().Select(f => (string?)f!["name"]));
        var region = result["fields"]![0]!;
        Assert.Equal("Sales region", (string?)region["description"]);
        Assert.Equal("Pick the billing region", (string?)region["helpText"]);
        Assert.Equal(
            "SELECT QualifiedApiName, Label, DataType, Description, InlineHelpText, IsCalculated, NamespacePrefix FROM FieldDefinition WHERE EntityDefinition.QualifiedApiName = 'Demo__c'",
            Assert.Single(ToolingCalls(h)).Q);
    }

    [Fact]
    public async Task Field_definitions_can_include_standard_fields_and_filter_by_name_or_label()
    {
        var h = Org(_ => Ok(FieldRows));

        var all = await h.Metadata.ListCustomFields("Demo__c", includeStandard: true);
        var tier = await h.Metadata.ListCustomFields("Demo__c", nameContains: "TIER");
        var byLabel = await h.Metadata.ListCustomFields("Demo__c", nameContains: "region");

        Assert.Equal(3, (int?)all["totalMatches"]);
        Assert.Equal(["Tier__c"], tier["fields"]!.AsArray().Select(f => (string?)f!["name"]));
        Assert.Equal(["Region__c"], byLabel["fields"]!.AsArray().Select(f => (string?)f!["name"]));
    }

    private const string DemoFieldRow = """
        {"records":[{"QualifiedApiName":"Score__c","Label":"Score","DataType":"Formula (Number)","Description":"Weighted score","InlineHelpText":"Calculated nightly","IsCalculated":true,"NamespacePrefix":null,"DurableId":"01Ixx0000000001.00Nxx0000000001"}]}
        """;

    [Fact]
    public async Task A_custom_field_returns_its_formula_and_definition()
    {
        var h = Org(call => call.Q!.Contains("FROM CustomField")
            ? Ok(Records("""{"Id":"00Nxx0000000001","FullName":"Demo__c.Score__c","Metadata":{"formula":"Amount__c * Weight__c","formulaTreatBlanksAs":"BlankAsZero","required":false,"unique":false,"precision":18,"scale":2}}"""))
            : Ok(DemoFieldRow));

        var result = await h.Metadata.GetFieldMetadata("Demo__c", "Score__c");

        Assert.Equal("Weighted score", (string?)result["description"]);
        Assert.Equal("Calculated nightly", (string?)result["helpText"]);
        Assert.True((bool?)result["calculated"]);
        Assert.Equal("Amount__c * Weight__c", (string?)result["definition"]!["formula"]);
        Assert.Equal("BlankAsZero", (string?)result["definition"]!["formulaTreatBlanksAs"]);
        Assert.Equal(18, (int?)result["definition"]!["precision"]);

        var calls = ToolingCalls(h).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Contains("QualifiedApiName = 'Score__c'", calls[0].Q);
        Assert.Equal("SELECT Id, FullName, Metadata FROM CustomField WHERE Id = '00Nxx0000000001'", calls[1].Q);
    }

    [Fact]
    public async Task A_standard_field_has_no_definition_lookup()
    {
        var h = Org(_ => Ok("""{"records":[{"QualifiedApiName":"Name","Label":"Demo Name","DataType":"Text(80)","DurableId":"01Ixx0000000001.Name"}]}"""));

        var result = await h.Metadata.GetFieldMetadata("Demo__c", "Name");

        Assert.Single(ToolingCalls(h));
        Assert.Null(result["definition"]);
        Assert.Contains("Standard fields", (string?)result["definitionNote"]);
    }

    [Fact]
    public async Task A_failed_definition_lookup_keeps_the_basic_field_details()
    {
        var h = Org(call => call.Q!.Contains("FROM CustomField")
            ? FakeSalesforce.Json(HttpStatusCode.Forbidden, """[{"message":"no access","errorCode":"INSUFFICIENT_ACCESS"}]""")
            : Ok(DemoFieldRow));

        var result = await h.Metadata.GetFieldMetadata("Demo__c", "Score__c");

        Assert.Equal("Weighted score", (string?)result["description"]);
        Assert.Contains("INSUFFICIENT_ACCESS", (string?)result["definitionNote"]);
    }

    [Fact]
    public async Task A_durable_id_that_is_not_a_custom_field_id_is_not_used_in_a_query()
    {
        var h = Org(_ => Ok("""{"records":[{"QualifiedApiName":"X__c","Label":"X","DataType":"Text","DurableId":"01Ixx0000000001.' OR Name != '"}]}"""));

        var result = await h.Metadata.GetFieldMetadata("Demo__c", "X__c");

        Assert.Single(ToolingCalls(h));
        Assert.NotNull(result["definitionNote"]);
    }

    [Fact]
    public async Task An_unknown_field_is_reported_with_a_way_forward()
    {
        var h = Org(_ => Ok(Records()));

        var ex = await Assert.ThrowsAsync<McpException>(() => h.Metadata.GetFieldMetadata("Demo__c", "Nope__c"));

        Assert.Contains("no field named 'Nope__c'", ex.Message);
        Assert.Contains("describe_object", ex.Message);
    }

    [Fact]
    public async Task Names_that_could_alter_a_tooling_query_never_reach_salesforce()
    {
        var h = Org(_ => Ok(Records()));

        await Assert.ThrowsAsync<McpException>(() => h.Metadata.GetFieldMetadata("Demo__c", "X' OR Name != '"));
        await Assert.ThrowsAsync<McpException>(() => h.Metadata.ListValidationRules("Demo__c' OR ''='"));
        await Assert.ThrowsAsync<McpException>(() => h.Metadata.GetObjectMetadata("Demo__c'--"));

        Assert.Empty(h.Fake.Calls);
    }

    private const string RuleRows = """
        {"records":[
          {"Id":"03dxx0000000002AAA","ValidationName":"Region_Required","Active":true,"Description":"Region is mandatory","ErrorDisplayField":"Region__c","ErrorMessage":"Pick a region","NamespacePrefix":null},
          {"Id":"03dxx0000000001AAA","ValidationName":"Amount_Positive","Active":false,"Description":null,"ErrorDisplayField":null,"ErrorMessage":"Amount must be positive","NamespacePrefix":null}]}
        """;

    [Fact]
    public async Task Validation_rules_list_name_state_and_message_without_formulas_by_default()
    {
        var h = Org(_ => Ok(RuleRows));

        var result = await h.Metadata.ListValidationRules("Demo__c");

        Assert.Single(ToolingCalls(h));
        Assert.Equal(["Amount_Positive", "Region_Required"], result["rules"]!.AsArray().Select(r => (string?)r!["name"]));
        var active = result["rules"]![1]!;
        Assert.True((bool?)active["active"]);
        Assert.Equal("Pick a region", (string?)active["errorMessage"]);
        Assert.Equal("Region__c", (string?)active["errorDisplayField"]);
        Assert.Null(active["formula"]);
        Assert.Contains("EntityDefinition.QualifiedApiName = 'Demo__c'", ToolingCalls(h).Single().Q);
    }

    [Fact]
    public async Task Formulas_cost_one_lookup_per_rule_when_asked_for()
    {
        var h = Org(call => call.Q!.StartsWith("SELECT Id, Metadata")
            ? Ok(Records(call.Q.Contains("03dxx0000000001AAA")
                ? """{"Id":"03dxx0000000001AAA","Metadata":{"errorConditionFormula":"Amount__c <= 0"}}"""
                : """{"Id":"03dxx0000000002AAA","Metadata":{"errorConditionFormula":"ISBLANK(Region__c)"}}"""))
            : Ok(RuleRows));

        var result = await h.Metadata.ListValidationRules("Demo__c", includeFormulas: true);

        Assert.Equal(3, ToolingCalls(h).Count());
        Assert.Equal(["Amount__c <= 0", "ISBLANK(Region__c)"], result["rules"]!.AsArray().Select(r => (string?)r!["formula"]));
    }

    [Fact]
    public async Task Formula_lookups_are_capped_and_the_cap_is_reported()
    {
        var many = Records(Enumerable.Range(1, 30).Select(i =>
            $"{{\"Id\":\"03dxx00000000{i:D2}AAA\",\"ValidationName\":\"Rule_{i:D2}\",\"Active\":true,\"ErrorMessage\":\"m\"}}").ToArray());
        var h = Org(call => call.Q!.StartsWith("SELECT Id, Metadata") ? Ok(Records("""{"Metadata":{"errorConditionFormula":"f"}}""")) : Ok(many));

        var result = await h.Metadata.ListValidationRules("Demo__c", includeFormulas: true);

        Assert.Equal(1 + 25, ToolingCalls(h).Count());
        Assert.Equal(30, (int?)result["returned"]);
        Assert.Contains("first 25", (string?)result["note"]);
    }

    [Fact]
    public async Task One_failed_formula_lookup_does_not_lose_the_other_rules()
    {
        var h = Org(call => call.Q!.StartsWith("SELECT Id, Metadata")
            ? FakeSalesforce.Json(HttpStatusCode.Forbidden, """[{"message":"no","errorCode":"INSUFFICIENT_ACCESS"}]""")
            : Ok(RuleRows));

        var result = await h.Metadata.ListValidationRules("Demo__c", includeFormulas: true);

        Assert.Equal(2, result["rules"]!.AsArray().Count);
        Assert.All(result["rules"]!.AsArray(), r => Assert.Contains("INSUFFICIENT_ACCESS", (string?)r!["formulaNote"]));
    }

    [Fact]
    public async Task An_object_with_no_validation_rules_returns_an_empty_list()
    {
        var h = Org(_ => Ok(Records()));

        var result = await h.Metadata.ListValidationRules("Demo__c");

        Assert.Equal(0, (int?)result["totalRules"]);
        Assert.Empty(result["rules"]!.AsArray());
    }

    [Fact]
    public async Task Tooling_queries_follow_a_per_call_api_version()
    {
        var h = new Harness();
        h.Fake.Api = call => call.Path == "/services/data/" ? Ok(Schemas.VersionList) : Ok(Records());

        await h.Metadata.ListValidationRules("Demo__c", apiVersion: "v52.0");

        Assert.Equal("/services/data/v52.0/tooling/query", h.Fake.ApiCalls.Last().Path);
    }
}

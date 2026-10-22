using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using SalesforceMcp.Salesforce;
using SalesforceMcp.Tools;

namespace SalesforceMcp.Tests;

public sealed class SchemaToolTests
{
    private const string DemoPath = "/services/data/v66.0/sobjects/Demo__c";

    private static HttpResponseMessage Ok(string json) => FakeSalesforce.Json(HttpStatusCode.OK, json);

    private static Harness Org(Func<Call, HttpResponseMessage>? other = null)
    {
        var h = new Harness();
        h.Fake.Api = call =>
        {
            if (call.Path == "/services/data/")
            {
                return Ok(Schemas.VersionList);
            }

            if (call.Path.EndsWith("/describe"))
            {
                return Ok(Schemas.Demo);
            }

            return other?.Invoke(call) ?? Ok("{}");
        };
        return h;
    }

    private const string Layouts = """
        {"layouts":[{"id":"00hxx0000000001AAA",
           "editLayoutSections":[
             {"heading":"Information","layoutRows":[
               {"layoutItems":[
                 {"label":"Demo Name","required":true,"editableForNew":true,"editableForUpdate":true,"layoutComponents":[{"type":"Field","value":"Name"}]},
                 {"label":"Tier","required":false,"editableForNew":true,"editableForUpdate":true,"layoutComponents":[{"type":"Field","value":"Tier__c"}]}]},
               {"layoutItems":[
                 {"label":"Created","required":false,"editableForNew":false,"editableForUpdate":false,"layoutComponents":[{"type":"Field","value":"CreatedDate"}]},
                 {"label":"","placeholder":true,"layoutComponents":[]},
                 {"label":"Chart","layoutComponents":[{"type":"ReportChart","value":"Chart1"}]}]}]}],
           "relatedLists":[{"label":"Notes","sobject":"Note"},{"label":"Files","sobject":"ContentDocumentLink"}]}],
         "recordTypeMappings":[
           {"name":"Gold","recordTypeId":"012xx0000000001AAA","layoutId":"00hxx0000000001AAA","active":true,"available":true,"defaultRecordTypeMapping":true,
            "picklistsForRecordType":[{"picklistName":"Tier__c","picklistValues":[{"value":"Basic","label":"Basic"}]}]}]}
        """;

    [Fact]
    public async Task Object_info_reads_the_bare_sobject_resource_and_links_recent_items()
    {
        var h = Org(call => call.Path == DemoPath
            ? Ok("""
                {"objectDescribe":{"name":"Demo__c","label":"Demo","labelPlural":"Demos","keyPrefix":"a01","custom":true,"queryable":true,"createable":true},
                 "recentItems":[{"attributes":{"type":"Demo__c","url":"/x"},"Id":"a01xx0000000001AAA","Name":"First demo"}]}
                """)
            : Ok("{}"));

        var result = await h.SchemaTools.GetObjectInfo("Demo__c");

        Assert.Equal(DemoPath, h.Fake.ApiCalls.Last().Path);
        Assert.Equal("Demo__c", (string?)result["name"]);
        Assert.Equal("a01", (string?)result["keyPrefix"]);
        var item = result["recentItems"]![0]!;
        Assert.Equal("Demo__c", (string?)item["sobjectType"]);
        Assert.Equal("https://acme.my.salesforce.com/a01xx0000000001AAA", (string?)item["recordUrl"]);
        Assert.Null(item["attributes"]);
    }

    [Fact]
    public async Task Object_info_for_an_object_with_no_recent_items_returns_an_empty_list()
    {
        var h = Org(_ => Ok("""{"objectDescribe":{"name":"Demo__c"},"recentItems":[]}"""));

        var result = await h.SchemaTools.GetObjectInfo("Demo__c", apiVersion: "v66.0");

        Assert.Empty(result["recentItems"]!.AsArray());
    }

    [Fact]
    public async Task Record_types_skip_the_master_and_inactive_ones_by_default()
    {
        var h = Org();

        var result = await h.SchemaTools.GetRecordTypes("Demo__c");

        Assert.True((bool?)result["hasRecordTypes"]);
        var gold = Assert.Single(result["recordTypes"]!.AsArray())!;
        Assert.Equal("Gold", (string?)gold["name"]);
        Assert.Equal(Sample.GoldRecordType, (string?)gold["recordTypeId"]);
        Assert.True((bool?)gold["default"]);

        var all = await h.SchemaTools.GetRecordTypes("Demo__c", includeInactive: true);
        Assert.Equal(["Gold", "Retired"], all["recordTypes"]!.AsArray().Select(r => (string?)r!["name"]));
    }

    [Fact]
    public async Task An_object_with_only_the_master_record_type_says_it_has_none()
    {
        var h = new Harness();
        h.Fake.Api = _ => Ok("""{"name":"Account","recordTypeInfos":[{"name":"Master","master":true,"recordTypeId":"012000000000000AAA"}],"fields":[]}""");

        var result = await h.SchemaTools.GetRecordTypes("Account");

        Assert.False((bool?)result["hasRecordTypes"]);
        Assert.Empty(result["recordTypes"]!.AsArray());
        Assert.Contains("no record types", (string?)result["note"]);
    }

    [Fact]
    public async Task Page_layout_for_a_record_type_lists_fields_with_required_and_read_only_flags()
    {
        var h = Org(call => call.Path.EndsWith("/describe/layouts/" + Sample.GoldRecordType) ? Ok(Layouts) : Ok("{}"));

        var result = await h.SchemaTools.GetPageLayout("Demo__c", Sample.GoldRecordType);

        Assert.Equal($"{DemoPath}/describe/layouts/{Sample.GoldRecordType}", h.Fake.ApiCalls.Last().Path);
        var layout = result["layout"]!;
        Assert.Equal("00hxx0000000001AAA", (string?)layout["id"]);

        var section = layout["sections"]![0]!;
        Assert.Equal("Information", (string?)section["heading"]);
        var fields = section["fields"]!.AsArray();
        Assert.Equal(["Name", "Tier__c", "CreatedDate"], fields.Select(f => (string?)f!["field"]));
        Assert.True((bool?)fields[0]!["required"]);
        Assert.False((bool?)fields[0]!["readOnly"]);
        Assert.True((bool?)fields[2]!["readOnly"]);

        Assert.Equal(["Notes", "Files"], layout["relatedLists"]!.AsArray().Select(r => (string?)r!["label"]));
    }

    [Fact]
    public async Task Page_layout_mappings_leave_out_the_bulky_picklist_lists()
    {
        var h = Org(call => call.Path.Contains("/describe/layouts") ? Ok(Layouts) : Ok("{}"));

        var result = await h.SchemaTools.GetPageLayout("Demo__c");

        var mapping = Assert.Single(result["recordTypeMappings"]!.AsArray())!;
        Assert.Equal("Gold", (string?)mapping["name"]);
        Assert.Equal("00hxx0000000001AAA", (string?)mapping["layoutId"]);
        Assert.Null(mapping["picklistsForRecordType"]);
    }

    [Fact]
    public async Task An_object_with_several_layouts_asks_for_a_record_type_instead_of_dumping_them_all()
    {
        var h = Org(call => call.Path.EndsWith("/describe/layouts")
            ? Ok("""{"layouts":[{"id":"00hxx0000000001AAA"},{"id":"00hxx0000000002AAA"}],"recordTypeMappings":[]}""")
            : Ok("{}"));

        var result = await h.SchemaTools.GetPageLayout("Demo__c");

        Assert.Null(result["layout"]);
        Assert.Equal(2, (int?)result["layoutCount"]);
        Assert.Contains("recordTypeId", (string?)result["note"]);
    }

    [Fact]
    public async Task A_bad_record_type_id_never_reaches_salesforce()
    {
        var h = Org();

        await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.GetPageLayout("Demo__c", "012/../x"));
        await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.GetPicklistValues("Demo__c", "Tier__c", "bad id"));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task Picklist_values_list_active_values_with_the_default_marked()
    {
        var h = Org();

        var result = await h.SchemaTools.GetPicklistValues("Demo__c", "Tier__c");

        Assert.Equal(["Basic", "Plus"], result["values"]!.AsArray().Select(v => (string?)v!["value"]));
        Assert.True((bool?)result["values"]![0]!["default"]);
        Assert.False((bool?)result["multiSelect"]);
        Assert.Equal(2, (int?)result["totalValues"]);
        Assert.Null(result["values"]![0]!["validWhen"]);
    }

    [Fact]
    public async Task A_dependent_picklist_says_which_controlling_values_make_each_value_valid()
    {
        var h = Org();

        var result = await h.SchemaTools.GetPicklistValues("Demo__c", "Perk__c");

        Assert.Equal("Is_Premium__c", (string?)result["controllingField"]);
        var values = result["values"]!.AsArray();
        Assert.Equal(["Unchecked"], values[0]!["validWhen"]!.AsArray().Select(v => (string?)v));
        Assert.Equal(["Checked"], values[1]!["validWhen"]!.AsArray().Select(v => (string?)v));
    }

    [Theory]
    [InlineData("oA==", new[] { "a", "c" })]
    [InlineData("gA==", new[] { "a" })]
    [InlineData("AAA=", new string[0])]
    [InlineData(null, new string[0])]
    [InlineData("not base64!", new string[0])]
    public void Decodes_the_valid_for_bit_set_from_the_high_bit(string? validFor, string[] expected) =>
        Assert.Equal(expected, SalesforceSchemaTools.DecodeValidFor(validFor, ["a", "b", "c"]));

    [Fact]
    public void Decodes_bits_that_fall_in_a_later_byte()
    {
        var controllers = Enumerable.Range(0, 10).Select(i => $"c{i}").ToList();

        // Second byte 0x80 is bit 8, the ninth controlling value.
        var bytes = Convert.ToBase64String([0x00, 0x80]);

        Assert.Equal(["c8"], SalesforceSchemaTools.DecodeValidFor(bytes, controllers));
    }

    [Fact]
    public async Task A_record_type_narrows_the_values_to_what_it_allows()
    {
        var h = Org(call => call.Path.EndsWith("/describe/layouts/" + Sample.GoldRecordType) ? Ok(Layouts) : Ok("{}"));

        var result = await h.SchemaTools.GetPicklistValues("Demo__c", "Tier__c", Sample.GoldRecordType);

        Assert.Equal(["Basic"], result["values"]!.AsArray().Select(v => (string?)v!["value"]));
        Assert.Equal(Sample.GoldRecordType, (string?)result["recordTypeId"]);
        Assert.Null(result["recordTypeNote"]);
    }

    [Fact]
    public async Task A_15_character_record_type_id_matches_the_18_character_one()
    {
        var h = Org(call => call.Path.Contains("/describe/layouts/") ? Ok(Layouts) : Ok("{}"));

        var result = await h.SchemaTools.GetPicklistValues("Demo__c", "Tier__c", Sample.GoldRecordType[..15]);

        Assert.Equal(["Basic"], result["values"]!.AsArray().Select(v => (string?)v!["value"]));
    }

    [Fact]
    public async Task A_picklist_with_no_record_type_restriction_shows_every_active_value_and_says_so()
    {
        var h = Org(call => call.Path.Contains("/describe/layouts/") ? Ok(Layouts) : Ok("{}"));

        var result = await h.SchemaTools.GetPicklistValues("Demo__c", "Perk__c", Sample.GoldRecordType);

        Assert.Equal(2, result["values"]!.AsArray().Count);
        Assert.Contains("no restriction", (string?)result["recordTypeNote"]);
    }

    [Fact]
    public async Task An_unknown_record_type_is_reported()
    {
        var h = Org(call => call.Path.Contains("/describe/layouts/") ? Ok(Layouts) : Ok("{}"));

        var ex = await Assert.ThrowsAsync<McpException>(
            () => h.SchemaTools.GetPicklistValues("Demo__c", "Tier__c", "012xx0000000009AAA"));

        Assert.Contains("get_record_types", ex.Message);
    }

    [Fact]
    public async Task Asking_for_picklist_values_of_a_text_field_or_a_missing_field_explains_why_not()
    {
        var h = Org();

        var text = await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.GetPicklistValues("Demo__c", "Name"));
        var missing = await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.GetPicklistValues("Demo__c", "Nope__c"));

        Assert.Contains("string field, not a picklist", text.Message);
        Assert.Contains("no field named 'Nope__c'", missing.Message);
    }

    [Fact]
    public async Task A_snapshot_holds_one_signature_per_field_and_a_fingerprint()
    {
        var h = Org();

        var snapshot = await h.SchemaTools.GetSchemaSnapshot("Demo__c");

        Assert.Equal(6, (int?)snapshot["fieldCount"]);
        Assert.Equal("v66.0", (string?)snapshot["apiVersion"]);
        Assert.Equal(16, ((string?)snapshot["fingerprint"])!.Length);

        var fields = snapshot["fields"]!.AsObject();
        Assert.Equal("string(80)|required", (string?)fields["Name"]);
        Assert.Equal("id(18)|!createable|!updateable", (string?)fields["Id"]);
        Assert.Equal("reference(18)|ref=Group/User", (string?)fields["Owner__c"]);
        Assert.StartsWith("picklist(255)|values=2:", (string?)fields["Tier__c"]);
        Assert.StartsWith("picklist(255)|controlledBy=Is_Premium__c|values=2:", (string?)fields["Perk__c"]);
    }

    [Fact]
    public async Task Comparing_an_unchanged_object_to_its_snapshot_reports_no_change()
    {
        var h = Org();
        var snapshot = await h.SchemaTools.GetSchemaSnapshot("Demo__c");

        var result = await h.SchemaTools.CompareSchema(
            "Demo__c", ToDictionary(snapshot["fields"]!), (string?)snapshot["fingerprint"]);

        Assert.True((bool?)result["identical"]);
        Assert.True((bool?)result["fingerprintMatches"]);
        Assert.Equal(6, (int?)result["counts"]!["unchanged"]);
    }

    [Fact]
    public async Task Comparing_reports_added_removed_and_changed_fields_including_picklist_edits()
    {
        var h = Org();
        var snapshot = await h.SchemaTools.GetSchemaSnapshot("Demo__c");

        var edited = Schemas.WithoutField(Schemas.Demo, "Owner__c")
            .Replace("\"label\":\"Demo Name\",\"type\":\"string\",\"length\":80", "\"label\":\"Demo Name\",\"type\":\"string\",\"length\":120")
            .Replace("\"value\":\"Plus\",\"label\":\"Plus\",\"active\":true", "\"value\":\"Plus\",\"label\":\"Plus\",\"active\":false")
            .Replace("\"fields\":[", "\"fields\":[{\"name\":\"Region__c\",\"type\":\"string\",\"length\":40,\"nillable\":true,\"createable\":true,\"updateable\":true},");
        h.Fake.Api = call => call.Path.EndsWith("/describe") ? Ok(edited) : Ok("{}");
        h.Time.Advance(TimeSpan.FromMinutes(10));

        var result = await h.SchemaTools.CompareSchema(
            "Demo__c", ToDictionary(snapshot["fields"]!), (string?)snapshot["fingerprint"]);

        Assert.False((bool?)result["identical"]);
        Assert.False((bool?)result["fingerprintMatches"]);
        Assert.Equal(["Region__c"], result["added"]!.AsArray().Select(a => (string?)a!["field"]));
        Assert.Equal(["Owner__c"], result["removed"]!.AsArray().Select(r => (string?)r!["field"]));

        var changed = result["changed"]!.AsArray();
        Assert.Equal(["Name", "Tier__c"], changed.Select(c => (string?)c!["field"]));
        Assert.Equal("string(80)|required", (string?)changed[0]!["before"]);
        Assert.Equal("string(120)|required", (string?)changed[0]!["after"]);
    }

    [Fact]
    public async Task Comparing_needs_a_real_baseline()
    {
        var h = Org();

        await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.CompareSchema("Demo__c", []));
        await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.CompareSchema("Demo__c", null!));

        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public async Task Two_api_versions_of_one_object_can_be_compared()
    {
        var h = Org();
        h.Fake.Api = call =>
        {
            if (call.Path == "/services/data/")
            {
                return Ok(Schemas.VersionList);
            }

            return call.Path.Contains("/v52.0/")
                ? Ok(Schemas.WithoutField(Schemas.Demo, "Perk__c"))
                : Ok(Schemas.Demo);
        };

        var result = await h.SchemaTools.CompareApiVersions("Demo__c", "v52.0", "v67.0");

        Assert.Equal("v52.0", (string?)result["from"]!["apiVersion"]);
        Assert.Equal("v67.0", (string?)result["to"]!["apiVersion"]);
        Assert.Equal(5, (int?)result["from"]!["fieldCount"]);
        Assert.Equal(6, (int?)result["to"]!["fieldCount"]);
        Assert.Equal(["Perk__c"], result["added"]!.AsArray().Select(a => (string?)a!["field"]));
        Assert.Empty(result["removed"]!.AsArray());
    }

    [Fact]
    public async Task Comparing_a_version_with_itself_is_refused()
    {
        var h = Org();

        var same = await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.CompareApiVersions("Demo__c", "v66.0"));
        var missing = await Assert.ThrowsAsync<McpException>(() => h.SchemaTools.CompareApiVersions("Demo__c", " "));

        Assert.Contains("different versions", same.Message);
        Assert.Contains("fromApiVersion is required", missing.Message);
    }

    [Fact]
    public void Labels_and_help_text_do_not_count_as_a_schema_change()
    {
        var a = JsonNode.Parse("""{"name":"Name","label":"Old label","type":"string","length":80,"nillable":true,"inlineHelpText":"a"}""")!.AsObject();
        var b = JsonNode.Parse("""{"name":"Name","label":"New label","type":"string","length":80,"nillable":true,"inlineHelpText":"b"}""")!.AsObject();

        Assert.Equal(SchemaDiff.Signature(a), SchemaDiff.Signature(b));
    }

    [Fact]
    public void Picklist_signatures_ignore_order_and_inactive_values_but_notice_real_edits()
    {
        JsonObject Field(params (string Value, bool Active)[] values) => new()
        {
            ["name"] = "P__c",
            ["type"] = "picklist",
            ["picklistValues"] = new JsonArray(values.Select(v =>
                (JsonNode?)new JsonObject { ["value"] = v.Value, ["active"] = v.Active }).ToArray())
        };

        var baseline = SchemaDiff.Signature(Field(("A", true), ("B", true)));

        Assert.Equal(baseline, SchemaDiff.Signature(Field(("B", true), ("A", true), ("Old", false))));
        Assert.NotEqual(baseline, SchemaDiff.Signature(Field(("A", true), ("C", true))));
    }

    private static Dictionary<string, string> ToDictionary(JsonNode fields) =>
        fields.AsObject().ToDictionary(p => p.Key, p => (string)p.Value!);
}

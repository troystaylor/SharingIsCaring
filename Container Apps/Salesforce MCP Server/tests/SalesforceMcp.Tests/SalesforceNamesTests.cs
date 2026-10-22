using ModelContextProtocol;
using SalesforceMcp.Salesforce;

namespace SalesforceMcp.Tests;

public sealed class SalesforceNamesTests
{
    [Theory]
    [InlineData("Account")]
    [InlineData("Invoice__c")]
    [InlineData("acme__Thing__c")]
    [InlineData("  Contact  ")]
    public void Accepts_real_sobject_names(string name) =>
        Assert.Equal(name.Trim(), SalesforceNames.SObject(name));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Account/describe")]
    [InlineData("Account?x=1")]
    [InlineData("../limits")]
    [InlineData("Account Name")]
    [InlineData("1Account")]
    public void Rejects_names_that_could_change_the_request_path(string? name) =>
        Assert.Throws<McpException>(() => SalesforceNames.SObject(name));

    [Theory]
    [InlineData("001xx000003DGb1AAG")]
    [InlineData("001xx000003DGb1")]
    public void Accepts_15_and_18_character_ids(string id) =>
        Assert.Equal(id, SalesforceNames.RecordId(id));

    [Theory]
    [InlineData("001xx")]
    [InlineData("001xx000003DGb1AAG/../x")]
    [InlineData("001xx000003DGb1AAG?fields=Name")]
    public void Rejects_malformed_ids(string id) =>
        Assert.Throws<McpException>(() => SalesforceNames.RecordId(id));

    [Fact]
    public void Accepts_only_a_query_continuation_url()
    {
        const string good = "/services/data/v66.0/query/01gxx0000000001AAA-2000";
        Assert.Equal(good, SalesforceNames.NextRecordsUrl(good));

        foreach (var bad in new[]
                 {
                     "https://evil.example/services/data/v66.0/query/01gxx0000000001AAA-2000",
                     "/services/data/v66.0/sobjects/User",
                     "/services/data/v66.0/query/01gxx0000000001AAA-2000/../../limits",
                     "/services/data/v66.0/query/01gxx0000000001AAA-2000?x=1",
                     "//evil.example/query/01gxx0000000001AAA-2000",
                     ""
                 })
        {
            Assert.Throws<McpException>(() => SalesforceNames.NextRecordsUrl(bad));
        }
    }

    [Theory]
    [InlineData("SELECT Id FROM Account")]
    [InlineData("  select Id from Account")]
    public void Soql_must_start_with_select(string query) => Assert.Equal(query.Trim(), SalesforceNames.Soql(query));

    [Theory]
    [InlineData("DELETE FROM Account")]
    [InlineData("")]
    [InlineData("FIND {x}")]
    public void Rejects_other_statements_as_soql(string query) =>
        Assert.Throws<McpException>(() => SalesforceNames.Soql(query));

    [Fact]
    public void Sosl_must_start_with_find()
    {
        Assert.Equal("FIND {Acme}", SalesforceNames.Sosl("FIND {Acme}"));
        Assert.Throws<McpException>(() => SalesforceNames.Sosl("SELECT Id FROM Account"));
    }
}

using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesforceMcp.Configuration;
using SalesforceMcp.Salesforce;
using SalesforceMcp.Tools;

namespace SalesforceMcp.Tests;

public sealed record Call(
    HttpMethod Method, string Url, string? Authorization, string? Body, DateTimeOffset? IfModifiedSince = null)
{
    public string Path => new Uri(Url).AbsolutePath;

    /// <summary>The decoded <c>q</c> query parameter, which is the SOQL or SOSL text.</summary>
    public string? Q => System.Web.HttpUtility.ParseQueryString(new Uri(Url).Query)["q"];
}

/// <summary>Stands in for Salesforce: serves the userinfo endpoint and routes API calls to a handler.</summary>
public sealed class FakeSalesforce : HttpMessageHandler
{
    public const string UserInfoPath = "/services/oauth2/userinfo";

    public const string AliceToken = "alice-token";
    public const string BobToken = "bob-token";
    public const string AliceId = "005xx0000000001AAA";
    public const string BobId = "005xx0000000002AAA";

    public List<Call> Calls { get; } = [];

    public Func<Call, HttpResponseMessage> Api { get; set; } = _ => Json(HttpStatusCode.OK, "{}");

    /// <summary>Overrides the sign-in check. By default Alice's and Bob's tokens are accepted and any other is refused.</summary>
    public Func<Call, HttpResponseMessage>? UserInfo { get; set; }

    public IEnumerable<Call> ApiCalls => Calls.Where(c => c.Path != UserInfoPath);

    public IEnumerable<Call> UserInfoCalls => Calls.Where(c => c.Path == UserInfoPath);

    public static HttpResponseMessage Json(HttpStatusCode status, string json, DateTimeOffset? lastModified = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Content.Headers.LastModified = lastModified;
        return response;
    }

    public static string UserInfoJson(string userId, string name, string username) =>
        "{\"sub\":\"https://login.salesforce.com/id/00Dxx0000000001EAA/" + userId + "\","
        + "\"user_id\":\"" + userId + "\",\"organization_id\":\"00Dxx0000000001EAA\","
        + "\"preferred_username\":\"" + username + "\",\"name\":\"" + name + "\",\"email\":\"" + username + "\"}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var call = new Call(
            request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body,
            request.Headers.IfModifiedSince);

        lock (Calls)
        {
            Calls.Add(call);
        }

        if (call.Path != UserInfoPath)
        {
            return Api(call);
        }

        if (UserInfo is not null)
        {
            return UserInfo(call);
        }

        return call.Authorization switch
        {
            $"Bearer {AliceToken}" => Json(HttpStatusCode.OK, UserInfoJson(AliceId, "Alice", "alice@example.com")),
            $"Bearer {BobToken}" => Json(HttpStatusCode.OK, UserInfoJson(BobId, "Bob", "bob@example.com")),
            _ => Json(HttpStatusCode.Forbidden, "{\"error\":\"Bad_OAuth_Token\"}")
        };
    }
}

public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>A signed-in user whose identity a test can change between calls.</summary>
public sealed class TestUser : ISalesforceUser
{
    public string? AccessToken { get; set; } = FakeSalesforce.AliceToken;

    public string? UserId { get; set; } = FakeSalesforce.AliceId;

    public string? OrganizationId { get; set; } = "00Dxx0000000001EAA";

    public string? Username { get; set; } = "alice@example.com";

    public string? DisplayName { get; set; } = "Alice";

    public string CacheKey => HttpContextSalesforceUser.CacheKeyFor(UserId, AccessToken);

    public void SignInAs(string token, string userId)
    {
        AccessToken = token;
        UserId = userId;
    }
}

/// <summary>Wires the Salesforce client and tools to a fake Salesforce and a stand-in user, with no web host.</summary>
public sealed class Harness
{
    public FakeSalesforce Fake { get; } = new();

    public ManualTime Time { get; } = new();

    public SalesforceOptions Options { get; }

    public AuthOptions Auth { get; } = new();

    public TestUser User { get; } = new();

    public SalesforceTokenValidator Validator { get; }

    public SalesforceClient Client { get; }

    public MetadataCache Cache { get; }

    public SalesforceVersions Versions { get; }

    public SchemaService Schema { get; }

    public SalesforceReadTools Read { get; }

    public SalesforceSchemaTools SchemaTools { get; }

    public SalesforceMetadataTools Metadata { get; }

    public SalesforceWriteTools Write { get; }

    public Harness(Action<SalesforceOptions>? configure = null)
    {
        Options = new SalesforceOptions { InstanceUrl = "https://acme.my.salesforce.com" };
        configure?.Invoke(Options);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Auth));
        services.AddSingleton(User);
        services.AddSingleton<ISalesforceUser>(User);
        services.AddHttpClient(SalesforceHttp.PublicClientName, h => h.BaseAddress = Options.InstanceUri)
            .ConfigurePrimaryHttpMessageHandler(() => Fake);
        services.AddHttpClient<SalesforceClient>(SalesforceClient.HttpClientName, h => h.BaseAddress = Options.InstanceUri)
            .ConfigurePrimaryHttpMessageHandler(() => Fake);
        services.AddSingleton<SalesforceTokenValidator>();
        services.AddSingleton<MetadataCache>();
        services.AddTransient<SalesforceVersions>();
        services.AddTransient<SchemaService>();
        services.AddTransient<SalesforceReadTools>();
        services.AddTransient<SalesforceSchemaTools>();
        services.AddTransient<SalesforceMetadataTools>();
        services.AddTransient<SalesforceWriteTools>();

        var provider = services.BuildServiceProvider();
        Validator = provider.GetRequiredService<SalesforceTokenValidator>();
        Client = provider.GetRequiredService<SalesforceClient>();
        Cache = provider.GetRequiredService<MetadataCache>();
        Versions = provider.GetRequiredService<SalesforceVersions>();
        Schema = provider.GetRequiredService<SchemaService>();
        Read = provider.GetRequiredService<SalesforceReadTools>();
        SchemaTools = provider.GetRequiredService<SalesforceSchemaTools>();
        Metadata = provider.GetRequiredService<SalesforceMetadataTools>();
        Write = provider.GetRequiredService<SalesforceWriteTools>();
    }
}

/// <summary>Canned Salesforce payloads for the schema tests.</summary>
public static class Schemas
{
    public const string VersionList = """
        [{"label":"Winter '22","url":"/services/data/v52.0","version":"52.0"},
         {"label":"Summer '26","url":"/services/data/v66.0","version":"66.0"},
         {"label":"Winter '27","url":"/services/data/v67.0","version":"67.0"}]
        """;

    /// <summary>A custom object with a record type, a controlling checkbox, and a dependent picklist.</summary>
    public const string Demo = """
        {"name":"Demo__c","label":"Demo","labelPlural":"Demos","keyPrefix":"a01","custom":true,
         "queryable":true,"createable":true,"updateable":true,"deletable":true,
         "recordTypeInfos":[
           {"name":"Master","master":true,"recordTypeId":"012000000000000AAA","active":true,"available":true,"defaultRecordTypeMapping":false},
           {"name":"Gold","developerName":"Gold","master":false,"recordTypeId":"012xx0000000001AAA","active":true,"available":true,"defaultRecordTypeMapping":true},
           {"name":"Retired","developerName":"Retired","master":false,"recordTypeId":"012xx0000000002AAA","active":false,"available":false,"defaultRecordTypeMapping":false}],
         "fields":[
           {"name":"Id","label":"Record ID","type":"id","length":18,"nillable":false,"defaultedOnCreate":true,"createable":false,"updateable":false},
           {"name":"Name","label":"Demo Name","type":"string","length":80,"nillable":false,"createable":true,"updateable":true},
           {"name":"Is_Premium__c","label":"Premium","type":"boolean","nillable":false,"defaultedOnCreate":true,"custom":true,"createable":true,"updateable":true},
           {"name":"Tier__c","label":"Tier","type":"picklist","length":255,"nillable":true,"custom":true,"createable":true,"updateable":true,
            "picklistValues":[
              {"value":"Basic","label":"Basic","active":true,"defaultValue":true},
              {"value":"Plus","label":"Plus","active":true,"defaultValue":false},
              {"value":"Legacy","label":"Legacy","active":false,"defaultValue":false}]},
           {"name":"Perk__c","label":"Perk","type":"picklist","length":255,"nillable":true,"custom":true,"controllerName":"Is_Premium__c",
            "picklistValues":[
              {"value":"Support","label":"Support","active":true,"defaultValue":false,"validFor":"gA=="},
              {"value":"Priority","label":"Priority","active":true,"defaultValue":false,"validFor":"QA=="}]},
           {"name":"Owner__c","label":"Owner","type":"reference","length":18,"nillable":true,"custom":true,"referenceTo":["User","Group"],"relationshipName":"Owner__r"}],
         "childRelationships":[]}
        """;

    public static string WithoutField(string describeJson, string field)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(describeJson)!.AsObject();
        var fields = node["fields"]!.AsArray();
        var match = fields.First(f => (string?)f!["name"] == field)!;
        fields.Remove(match);
        return node.ToJsonString();
    }
}

public static class Sample
{
    public const string AccountId = "001xx000003DGb1AAG";
    public const string GoldRecordType = "012xx0000000001AAA";

    public static string Account(int i)
    {
        var id = $"001xx000003DGb{i:D3}AAG";
        return "{\"attributes\":{\"type\":\"Account\",\"url\":\"/services/data/v66.0/sobjects/Account/" + id + "\"},"
            + "\"Id\":\"" + id + "\",\"Name\":\"Account " + i + "\","
            + "\"Owner\":{\"attributes\":{\"type\":\"User\",\"url\":\"/services/data/v66.0/sobjects/User/005xx000000001AAA\"},\"Name\":\"Jo Rep\"}}";
    }

    public static string Query(int count, bool done = true, string? next = null)
    {
        var records = string.Join(',', Enumerable.Range(1, count).Select(Account));
        var nextPart = next is null ? string.Empty : $",\"nextRecordsUrl\":\"{next}\"";
        return $"{{\"totalSize\":{count},\"done\":{(done ? "true" : "false")}{nextPart},\"records\":[{records}]}}";
    }
}

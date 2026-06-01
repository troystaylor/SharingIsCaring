using NetSuiteCoworkMcp.Auth;
using NetSuiteCoworkMcp.Endpoints;
using NetSuiteCoworkMcp.NetSuite;
using NetSuiteCoworkMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

var aiConn = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(aiConn))
{
    builder.Services.AddApplicationInsightsTelemetry(o => o.ConnectionString = aiConn);
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IBearerTokenAccessor, BearerTokenAccessor>();

builder.Services.AddHttpClient<INetSuiteClient, NetSuiteClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["NetSuite:BaseUrl"] ?? Environment.GetEnvironmentVariable("NETSUITE_BASE_URL");
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        var accountId = cfg["NetSuite:AccountId"] ?? Environment.GetEnvironmentVariable("NETSUITE_ACCOUNT_ID");
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var subdomain = accountId.Replace('-', '_');
            baseUrl = $"https://{subdomain}.suitetalk.api.netsuite.com/services/rest";
        }
        else
        {
            baseUrl = "https://your-account.suitetalk.api.netsuite.com/services/rest";
        }
    }
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(60);
})
.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.UseJitter = true;
});

builder.Services.AddSingleton<ToolRegistry>(sp =>
{
    var reg = new ToolRegistry();
    reg.RegisterAll(sp);
    return reg;
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/status", (IBearerTokenAccessor tokens) =>
{
    var d = tokens.Diagnose();
    return Results.Ok(new
    {
        name = "NetSuite Cowork MCP",
        routes = new[] { "/mcp/full", "/mcp/federated" },
        transport = "streamable-http",
        token = new
        {
            present = d.Present,
            source = d.Source,
            shape = d.Shape,
            length = d.Length,
            expiresAtUnix = d.ExpiresAtUnix,
            secondsUntilExpiry = d.SecondsUntilExpiry,
        },
    });
});

app.MapMcpRoute("/mcp/full", filter: null);
app.MapMcpRoute("/mcp/federated", filter: ToolRegistry.FederatedReadOnlyTools);

app.Run();

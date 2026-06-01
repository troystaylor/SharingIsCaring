using SalesforceCoworkMcp.Auth;
using SalesforceCoworkMcp.Endpoints;
using SalesforceCoworkMcp.Salesforce;
using SalesforceCoworkMcp.Tools;

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

builder.Services.AddHttpClient<ISalesforceClient, SalesforceClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["Salesforce:BaseUrl"] ?? Environment.GetEnvironmentVariable("SALESFORCE_BASE_URL");
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        baseUrl = "https://your-instance.my.salesforce.com";
    }
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
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
        name = "Salesforce Cowork MCP",
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

using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Http.Resilience;
using SharePointTransferMcp.Auth;
using SharePointTransferMcp.Endpoints;
using SharePointTransferMcp.Graph;
using SharePointTransferMcp.Tools;

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

builder.Services.AddHttpClient<IGraphClient, GraphClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["Graph:BaseUrl"]
        ?? Environment.GetEnvironmentVariable("GRAPH_BASE_URL")
        ?? "https://graph.microsoft.com/v1.0";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(100);
})
.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.UseJitter = true;
});

builder.Services.AddHttpClient("graph-upload", c => c.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHttpClient("graph-ingest", c => c.Timeout = TimeSpan.FromMinutes(30));

builder.Services.AddSingleton<UploadSessionRunner>();

builder.Services.AddSingleton<IUploadSessionStore>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<AzureTableUploadSessionStore>>();
    var endpoint = cfg["UploadSessions:TableEndpoint"]
        ?? Environment.GetEnvironmentVariable("UploadSessions__TableEndpoint");
    var tableName = cfg["UploadSessions:TableName"]
        ?? Environment.GetEnvironmentVariable("UploadSessions__TableName")
        ?? "uploadSessions";

    TableClient? table = null;
    if (!string.IsNullOrWhiteSpace(endpoint))
    {
        var cred = new DefaultAzureCredential();
        table = new TableClient(new Uri(endpoint), tableName, cred);
        try { table.CreateIfNotExists(); } catch { }
    }
    return new AzureTableUploadSessionStore(table, logger);
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
        name = "SharePoint File Transfer MCP",
        routes = new[] { "/mcp" },
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

app.MapMcpRoute("/mcp");

app.Run();

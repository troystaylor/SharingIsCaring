using PlannerCoworkMcp.Auth;
using PlannerCoworkMcp.Endpoints;
using PlannerCoworkMcp.Planner;
using PlannerCoworkMcp.Tools;

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

builder.Services.AddHttpClient<IPlannerGraphClient, PlannerGraphClient>((_, client) =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
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

app.MapGet("/status", () => Results.Ok(new
{
    name = "planner-cowork-mcp",
    routes = new[] { "/mcp/full", "/mcp/federated" },
    transport = "streamable-http",
}));

app.MapMcpRoute("/mcp/full", filter: null);
app.MapMcpRoute("/mcp/federated", filter: ToolRegistry.FederatedReadOnlyTools);

app.Run();

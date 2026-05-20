using SlackCoworkMcp.Auth;
using SlackCoworkMcp.Endpoints;
using SlackCoworkMcp.Slack;
using SlackCoworkMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Structured logging + Application Insights
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

// HttpContext for bearer-token forwarding from inbound to outbound Slack calls
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IBearerTokenAccessor, BearerTokenAccessor>();

// OAuth shim — bridges Cowork Plugin Vault's standard OAuth 2.0 flow to Slack v2's
// user_scope quirk so the MCP receives xoxp-* user tokens.
builder.Services.Configure<OAuthShimOptions>(builder.Configuration.GetSection(OAuthShimOptions.SectionName));
builder.Services.AddSingleton<IOAuthShimStore, InMemoryOAuthShimStore>();
builder.Services.AddHttpClient("slack-oauth", c =>
{
    c.BaseAddress = new Uri("https://slack.com/");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
    c.Timeout = TimeSpan.FromSeconds(30);
});

// Slack capability index (embedded JSON resource)
builder.Services.AddSingleton<SlackCapabilityIndex>(_ => SlackCapabilityIndex.LoadEmbedded());

// Slack typed HttpClient with retry + 429 handling
builder.Services.AddHttpClient<ISlackClient, SlackClient>(client =>
{
    client.BaseAddress = new Uri("https://slack.com/api/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler(o =>
{
    // Retry on 5xx; Slack 429 has Retry-After and is handled inside SlackClient
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.UseJitter = true;
});

// Tool registry — central, with per-route filtering
builder.Services.AddSingleton<ToolRegistry>(sp =>
{
    var reg = new ToolRegistry();
    reg.RegisterAll(sp);
    return reg;
});

builder.Services.AddCors(); // not used; explicitly disabled below
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();

// Health endpoints
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Status / landing
app.MapGet("/status", () => Results.Ok(new
{
    name = "Slack Cowork MCP",
    routes = new[] { "/mcp/full", "/mcp/federated" },
    transport = "streamable-http",
}));

// Dual MCP routes — same registry, different tool-name filters
app.MapMcpRoute("/mcp/full", filter: null); // all tools
app.MapMcpRoute("/mcp/federated", filter: ToolRegistry.FederatedReadOnlyTools);

// OAuth shim — /oauth/authorize, /oauth/callback, /oauth/token
app.MapOAuthShim();

app.Run();

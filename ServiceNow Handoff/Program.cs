using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.Blobs;
using Azure.Identity;
using Microsoft.Extensions.Http.Resilience;
using ServiceNowHandoff;
using ServiceNowHandoff.ServiceNow;
using ServiceNowHandoff.Services;

var builder = WebApplication.CreateBuilder(args);

// Application Insights — set the connection string in appsettings.json or
// via APPLICATIONINSIGHTS_CONNECTION_STRING environment variable.
builder.Services.AddApplicationInsightsTelemetry();

// Agents SDK hosting
builder.AddAgentApplicationOptions();

// Storage — Azure Blob Storage for persistent state across restarts and instances.
var storageBlobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
var storageConnStr = builder.Configuration["Storage:ConnectionString"];
if (!string.IsNullOrEmpty(storageBlobEndpoint))
{
    // Use managed identity (no shared keys needed)
    builder.Services.AddSingleton<IStorage>(
        new BlobsStorage(new Uri($"{storageBlobEndpoint.TrimEnd('/')}/bot-state"), new DefaultAzureCredential()));
}
else if (!string.IsNullOrEmpty(storageConnStr))
{
    builder.Services.AddSingleton<IStorage>(new BlobsStorage(storageConnStr, "bot-state"));
}
else
{
    // Fallback to MemoryStorage for local dev (loses state on restart)
    builder.Services.AddSingleton<IStorage, MemoryStorage>();
}
builder.Services.AddSingleton<AgentApplicationOptions>();

// Copilot Studio via Direct Line
builder.Services.AddSingleton<DirectLineCopilotService>();

// ServiceNow services
var snConfig = builder.Configuration.GetSection("ServiceNow").Get<ServiceNowConnectionSettings>()
    ?? new ServiceNowConnectionSettings();
builder.Services.Configure<ServiceNowConnectionSettings>(
    builder.Configuration.GetSection("ServiceNow"));
builder.Services.AddSingleton<IServiceNowConnectionSettings>(snConfig);

// ServiceNow HTTP client with retry/resilience policy:
//   - Retries up to 3 times on transient HTTP errors (5xx, 408, 429)
//   - Exponential backoff with jitter
//   - Circuit breaker after 5 consecutive failures (30s break)
builder.Services.AddHttpClient("ServiceNow")
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient("DirectLine");
builder.Services.AddSingleton<ServiceNowTokenProvider>();
builder.Services.AddSingleton<ServiceNowMessageSender>();
builder.Services.AddSingleton<ServiceNowWebhookHandler>();
builder.Services.AddSingleton<ConversationMappingStore>();

// State management
builder.Services.AddSingleton<ConversationStateManager>();

// ServiceNow disconnect detection polling service
builder.Services.AddHostedService<ServiceNowNotificationService>();

// Register the agent
builder.AddAgent<ServiceNowHandoffAgent>();

// ---- Startup validation ----
// Warn about missing configuration so developers see issues at startup, not at runtime.
ValidateConfiguration(builder.Configuration);

var app = builder.Build();

// Bot Framework messaging endpoint
app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response,
    IAgentHttpAdapter adapter, IAgent agent, CancellationToken ct) =>
{
    await adapter.ProcessAsync(request, response, agent, ct);
});

// ServiceNow webhook endpoint for agent replies
app.MapPost("/api/servicenow/webhook", async (
    HttpContext httpContext,
    ServiceNowWebhookHandler handler) =>
{
    var result = await handler.HandleAsync(httpContext.Request, httpContext.RequestServices);
    return result;
});

app.Run();

// ---- Configuration validation helper ----
static void ValidateConfiguration(IConfiguration config)
{
    var warnings = new List<string>();

    // Copilot Studio via Direct Line
    var dlSecret = config["DirectLine:Secret"];
    if (string.IsNullOrEmpty(dlSecret))
        warnings.Add("DirectLine:Secret is not configured. " +
            "Get it from Copilot Studio > Settings > Security > Web channel security > Copy secret.");

    // ServiceNow
    var sn = config.GetSection("ServiceNow");
    if (string.IsNullOrEmpty(sn["InstanceUrl"]) || sn["InstanceUrl"]!.Contains("{{"))
        warnings.Add("ServiceNow:InstanceUrl is not configured. Set to https://YOUR-INSTANCE.service-now.com");
    if (string.IsNullOrEmpty(sn["ClientId"]))
        warnings.Add("ServiceNow:ClientId is empty. Create an OAuth app in ServiceNow > " +
            "System OAuth > Application Registry > Create an OAuth API endpoint for external clients.");
    if (string.IsNullOrEmpty(sn["ClientSecret"]))
        warnings.Add("ServiceNow:ClientSecret is empty.");
    if (string.IsNullOrEmpty(sn["WebhookSecret"]))
        warnings.Add("ServiceNow:WebhookSecret is empty. Webhook signature validation will be SKIPPED (insecure).");

    // Azure Bot / MSAL
    var conn = config.GetSection("Connections:BotServiceConnection:Settings");
    if (string.IsNullOrEmpty(conn["ClientId"]) || conn["ClientId"]!.Contains("{{"))
        warnings.Add("Connections:BotServiceConnection:Settings:ClientId is not configured. " +
            "Create an Azure Bot resource and use its App ID.");

    if (warnings.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           CONFIGURATION WARNINGS AT STARTUP             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        foreach (var w in warnings)
            Console.WriteLine($"  ⚠  {w}");
        Console.WriteLine();
        Console.ResetColor();
    }
}

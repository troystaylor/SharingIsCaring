using Azure.Monitor.OpenTelemetry.AspNetCore;
using Mongo.Copilot.Core;
using Mongo.Copilot.Core.Profiles;
using Mongo.Copilot.Indexer;
using Mongo.Copilot.Indexer.Graph;

// A web host rather than a plain worker, purely so the change-stream tail can expose a
// liveness signal. Container Apps probes run against the container port and do not require
// ingress, so nothing is published externally.
var builder = WebApplication.CreateBuilder(args);

// Profiles are shared verbatim with the federated head, which is what guarantees an
// indexed field and a live field cannot drift apart. Resolved from the content root
// upwards so local runs and container runs both find the file.
builder.Configuration.AddMongoCopilotProfiles(builder.Environment.ContentRootPath);

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddMongoCopilotCore(builder.Configuration);

builder.Services.Configure<GraphOptions>(builder.Configuration.GetSection("Graph"));
builder.Services.Configure<IndexerOptions>(builder.Configuration.GetSection("Indexer"));

builder.Services.AddHttpClient<GraphClient>();
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<GraphStateStore>();
builder.Services.AddSingleton<PrincipalResolver>();
builder.Services.AddSingleton<ItemPublisher>();
builder.Services.AddSingleton<IndexerHeartbeat>();
builder.Services.AddHostedService<IndexerService>();

builder.Services.AddHealthChecks()
    .AddCheck<IndexerHealthCheck>("indexer");

var app = builder.Build();

// Fail fast: publishing a large batch of items with an unresolvable ACL or a missing
// citation URL is far more expensive to undo than refusing to start.
try
{
    app.Services.GetRequiredService<ProfileStore>();
}
catch (ProfileValidationException ex)
{
    app.Logger.LogCritical("{Message}", ex.Message);
    return 1;
}

// Validate Graph settings before the credential chain is built, so a missing tenant ID
// reports the setting rather than an Azure.Identity message about an invalid tenant.
var graphErrors = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphOptions>>()
    .Value.Validate();

if (graphErrors.Count > 0)
{
    app.Logger.LogCritical(
        "Microsoft Graph configuration is invalid:{NewLine}{Errors}",
        Environment.NewLine,
        string.Join(Environment.NewLine, graphErrors.Select(e => "  - " + e)));

    return 1;
}

app.MapHealthChecks("/health");

app.Run();
return 0;


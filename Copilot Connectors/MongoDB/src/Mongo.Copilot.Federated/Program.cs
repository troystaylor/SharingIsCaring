using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Protocol;
using Mongo.Copilot.Core;
using Mongo.Copilot.Core.Profiles;
using Mongo.Copilot.Federated.Tools;

var builder = WebApplication.CreateBuilder(args);

// Collection profiles live in their own file so an installer can edit them without
// touching host configuration, and can mount them as a volume or config map. The path is
// resolved from the content root upwards, so this works both in a container and from
// `dotnet run --project src/Mongo.Copilot.Federated` at the solution root.
builder.Configuration.AddMongoCopilotProfiles(builder.Environment.ContentRootPath);

// ── Authentication ──────────────────────────────────────────────────────────
//
// Entra SSO authenticates the *user* to this server. The server then reaches MongoDB with
// its own credential, which is what avoids needing MongoDB to understand Entra identities.
// Claims from this validated token drive the per-user access filter in AccessEvaluator.

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var auth = builder.Configuration.GetSection("Auth");
    options.Authority = auth["Authority"];
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAudiences = auth.GetSection("ValidAudiences").Get<string[]>() ?? []
    };
})
.AddMcp(options =>
{
    var auth = builder.Configuration.GetSection("Auth");
    options.ResourceMetadata = new()
    {
        AuthorizationServers =
        {
            auth["Authority"] ?? "https://login.microsoftonline.com/common/v2.0"
        },
        ScopesSupported = auth.GetSection("Scopes").Get<string[]>()?.ToList() ?? ["mcp:tools"]
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// ── Shared core ─────────────────────────────────────────────────────────────
builder.Services.AddMongoCopilotCore(builder.Configuration);

// ── MCP server ──────────────────────────────────────────────────────────────
var mcp = builder.Configuration.GetSection("McpServer");

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = mcp["Name"] ?? "mongodb-copilot",
            Version = mcp["Version"] ?? "1.0.0",
            Title = mcp["Title"] ?? "MongoDB"
        };
    })
    .WithHttpTransport()
    .WithTools<MongoTools>();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Fail fast on invalid profiles. Resolving the store here means a misconfigured deployment
// never starts, rather than failing on the first user query.
try
{
    app.Services.GetRequiredService<ProfileStore>();
}
catch (ProfileValidationException ex)
{
    app.Logger.LogCritical("{Message}", ex.Message);
    return 1;
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

// In Development, auth is skipped so MCP Inspector can connect without a token. Access
// filters then see no claims, which — being fail-closed — yields empty results for any
// collection not mapped to Everyone. That is intentional: a local run should not be able
// to see more than a real caller would.
var endpoint = app.MapMcp();
if (!app.Environment.IsDevelopment())
    endpoint.RequireAuthorization();

app.Run();
return 0;


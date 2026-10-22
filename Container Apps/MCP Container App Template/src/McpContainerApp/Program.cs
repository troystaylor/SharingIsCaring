using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using McpContainerApp.Configuration;
using McpContainerApp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ModelContextProtocol.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
auth.Validate();

var rateLimit = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new();

builder.Services.AddSingleton(auth);
builder.Services.Configure<UpstreamOptions>(builder.Configuration.GetSection(UpstreamOptions.SectionName));

var upstream = builder.Configuration.GetSection(UpstreamOptions.SectionName).Get<UpstreamOptions>() ?? new();

builder.Services
    .AddHttpClient<ExampleUpstreamClient>(http =>
    {
        if (Uri.TryCreate(upstream.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            // The trailing slash keeps relative request paths from replacing the last base segment.
            http.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        }

        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .AddStandardResilienceHandler();

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "mcp-container-app", Version = "1.0.0" })
    // Container Apps load-balances across replicas with no session affinity, so sessions must not
    // live in process memory. Stateless mode makes every request self-contained.
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<ExampleTools>();

if (auth.Enabled)
{
    builder.Services
        .AddAuthentication(o =>
        {
            // MCP owns the challenge so a 401 carries the RFC 9728 resource_metadata pointer that
            // lets clients discover where to sign in. JwtBearer does the token validation.
            o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o =>
        {
            o.Authority = auth.Authority;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidAudiences = auth.ValidAudiences;
        })
        .AddMcp(o => o.ResourceMetadata = new()
        {
            Resource = auth.ResourceUri,
            AuthorizationServers = { auth.Authority },
            ScopesSupported = [auth.FullScope]
        });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(AuthOptions.PolicyName, p => p
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => auth.Allows(ctx.User)));
}

if (rateLimit.PermitsPerMinute > 0)
{
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(RateLimitOptions.PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue("oid")
                    ?? context.User.FindFirstValue("sub")
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimit.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    });
}

builder.Services.AddHealthChecks();

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

if (auth.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
else
{
    app.Logger.LogWarning("Auth:Enabled is false. The MCP endpoint is open to anyone who can reach it.");
}

if (rateLimit.PermitsPerMinute > 0)
{
    app.UseRateLimiter();
}

// Probes live on their own routes. The MCP endpoint expects JSON-RPC POSTs and rejects a plain GET.
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/ready").AllowAnonymous();

var mcp = app.MapMcp("/mcp");
if (auth.Enabled)
{
    mcp.RequireAuthorization(AuthOptions.PolicyName);
}

if (rateLimit.PermitsPerMinute > 0)
{
    mcp.RequireRateLimiting(RateLimitOptions.PolicyName);
}

app.Run();

/// <summary>Exposed so the test project can host the app in-process.</summary>
public partial class Program;

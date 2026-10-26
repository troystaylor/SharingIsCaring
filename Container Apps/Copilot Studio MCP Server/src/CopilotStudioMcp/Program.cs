using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using CopilotStudioMcp.Configuration;
using CopilotStudioMcp.Gateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
auth.Validate();

var rateLimit = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new();
var gateway = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ?? new();

builder.Services.AddSingleton(auth);
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<ExchangeOptions>(builder.Configuration.GetSection(ExchangeOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = gateway.MaxRequestBytes);

// The token exchange gets its own client so Entra calls never share the Copilot Studio retry and timeout pipeline.
builder.Services.AddHttpClient<ITokenBroker, OnBehalfOfBroker>(http => http.Timeout = TimeSpan.FromSeconds(30));

builder.Services
    .AddHttpClient<UpstreamClient>(http =>
    {
        http.Timeout = Timeout.InfiniteTimeSpan; // The resilience pipeline owns timeouts.
        http.MaxResponseContentBufferSize = gateway.MaxResponseBytes;
    })
    .AddStandardResilienceHandler(o =>
    {
        var timeout = TimeSpan.FromSeconds(gateway.UpstreamTimeoutSeconds);
        o.AttemptTimeout.Timeout = timeout;
        o.TotalRequestTimeout.Timeout = timeout;
        o.CircuitBreaker.SamplingDuration = timeout * 2 + TimeSpan.FromSeconds(1);
        // Retrying a tool call could create or change something twice, so nothing is retried.
        o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
    });

builder.Services.AddSingleton<ToolCatalog>();
builder.Services.AddSingleton<McpGateway>();

if (auth.Enabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.Authority = auth.Authority;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidAudiences = auth.ValidAudiences;
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

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/ready").AllowAnonymous();

var mcp = app.MapPost("/mcp", async (HttpContext http, McpGateway mcpGateway, CancellationToken ct) =>
{
    var header = http.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    var token = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : string.Empty;
    if (token.Length == 0)
    {
        http.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Json(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32001, message = "Sign in is required." } }, statusCode: 401);
    }

    JsonNode? message;
    try
    {
        message = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct);
    }
    catch (JsonException)
    {
        return Results.Json(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32700, message = "Invalid JSON." } }, statusCode: 400);
    }

    var tenant = http.User.FindFirstValue("tid") ?? "default";
    var reply = await mcpGateway.HandleAsync(message, token, tenant, ct);
    if (reply.WwwAuthenticate is not null)
    {
        http.Response.Headers.WWWAuthenticate = reply.WwwAuthenticate;
    }

    return reply.Body is null
        ? Results.StatusCode(reply.Status)
        : Results.Content(reply.Body.ToJsonString(), "application/json", statusCode: reply.Status);
});

// Stateless JSON responses only: there is no server-to-client stream and no session to delete.
app.MapMethods("/mcp", ["GET", "DELETE"], () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();

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

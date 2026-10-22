using System.Security.Claims;
using System.Threading.RateLimiting;
using Atlassian.Mcp.Bitbucket;
using Atlassian.Mcp.Confluence;
using Atlassian.Mcp.Core;
using Atlassian.Mcp.Host;
using Atlassian.Mcp.Jira;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
auth.Validate();

var rateLimit = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new();

var limits = builder.Configuration.GetSection(LimitOptions.SectionName).Get<LimitOptions>() ?? new();
limits.Validate();

var atlassian = builder.Configuration.GetSection(AtlassianOptions.SectionName).Get<AtlassianOptions>() ?? new();
var jira = builder.Configuration.GetSection(JiraOptions.SectionName).Get<JiraOptions>() ?? new();
var confluence = builder.Configuration.GetSection(ConfluenceOptions.SectionName).Get<ConfluenceOptions>() ?? new();
var bitbucket = builder.Configuration.GetSection(BitbucketOptions.SectionName).Get<BitbucketOptions>() ?? new();

var usesAtlassian = jira.Enabled || confluence.Enabled;
if (!usesAtlassian && !bitbucket.Enabled)
{
    throw new InvalidOperationException("Enable at least one of Jira, Confluence, or Bitbucket.");
}

if (usesAtlassian)
{
    atlassian.Validate();
}

var enabled = Products.All
    .Where(p => p.Key switch { "jira" => jira.Enabled, "confluence" => confluence.Enabled, _ => bitbucket.Enabled })
    .ToList();

builder.Services.AddSingleton(auth);
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<LimitOptions>(builder.Configuration.GetSection(LimitOptions.SectionName));
builder.Services.Configure<AtlassianOptions>(builder.Configuration.GetSection(AtlassianOptions.SectionName));
builder.Services.Configure<BitbucketOptions>(builder.Configuration.GetSection(BitbucketOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

// The server holds no Atlassian or Bitbucket credential. Each request carries the caller's own token, which
// the clients read from the request through ICaller.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ICaller, HttpContextCaller>();

// A retried create would make a duplicate, so only safe methods are retried.
if (usesAtlassian)
{
    builder.Services
        .AddHttpClient(HttpNames.Atlassian, http => http.BaseAddress = new Uri(atlassian.ApiBaseUrl.TrimEnd('/') + "/"))
        .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
    builder.Services.AddSingleton<AtlassianTokenValidator>();
}

if (bitbucket.Enabled)
{
    builder.Services
        .AddHttpClient(HttpNames.Bitbucket, http => http.BaseAddress = new Uri(bitbucket.ApiBaseUrl.TrimEnd('/') + "/"))
        .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
    builder.Services.AddSingleton<BitbucketTokenValidator>();
}

var mcp = builder.Services
    .AddMcpServer()
    // Container Apps load-balances across replicas with no session affinity, so sessions must not live in
    // process memory. Stateless mode makes every request self-contained. In that mode this callback runs on
    // every request, which is how each endpoint gets its own tool set and instructions.
    .WithHttpTransport(o =>
    {
        o.Stateless = true;
        o.ConfigureSessionOptions = (context, options, _) =>
        {
            if (Products.ForPath(context.Request.Path) is { } product)
            {
                options.ServerInfo = new() { Name = $"{product.Key}-mcp", Title = product.Title, Version = "1.0.0" };
                options.ServerInstructions = product.Instructions;

                var tools = new McpServerPrimitiveCollection<McpServerTool>();
                foreach (var tool in options.ToolCollection ?? [])
                {
                    if (tool.ProtocolTool.Name.StartsWith(product.ToolPrefix, StringComparison.Ordinal))
                    {
                        tools.Add(tool);
                    }
                }

                options.ToolCollection = tools;
            }

            return Task.CompletedTask;
        };
    });

if (jira.Enabled)
{
    builder.Services.AddTransient<JiraClient>();
    mcp.WithTools<JiraReadTools>();
    if (jira.EnableWriteTools)
    {
        mcp.WithTools<JiraWriteTools>();
    }
}

if (confluence.Enabled)
{
    builder.Services.AddTransient<ConfluenceClient>();
    mcp.WithTools<ConfluenceReadTools>();
    if (confluence.EnableWriteTools)
    {
        mcp.WithTools<ConfluenceWriteTools>();
    }
}

if (bitbucket.Enabled)
{
    builder.Services.AddTransient<BitbucketClient>();
    mcp.WithTools<BitbucketReadTools>();
    if (bitbucket.EnableWriteTools)
    {
        mcp.WithTools<BitbucketWriteTools>();
    }
}

var writeEnabled = new Dictionary<string, bool>
{
    ["jira"] = jira.Enabled && jira.EnableWriteTools,
    ["confluence"] = confluence.Enabled && confluence.EnableWriteTools,
    ["bitbucket"] = bitbucket.Enabled && bitbucket.EnableWriteTools
};

if (auth.Enabled)
{
    var authentication = builder.Services.AddAuthentication(o =>
    {
        // MCP owns the challenge so a 401 carries the RFC 9728 resource_metadata pointer that tells a client
        // where to sign in. The token handler for the endpoint being called checks the token itself.
        o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        o.DefaultAuthenticateScheme = TokenSchemes.ByEndpoint;
        o.DefaultForbidScheme = TokenSchemes.ByEndpoint;
    });

    if (usesAtlassian)
    {
        authentication.AddScheme<AuthenticationSchemeOptions, AtlassianTokenHandler>(TokenSchemes.Atlassian, _ => { });
    }

    if (bitbucket.Enabled)
    {
        authentication.AddScheme<AuthenticationSchemeOptions, BitbucketTokenHandler>(TokenSchemes.Bitbucket, _ => { });
    }

    var fallbackScheme = enabled[0].AuthScheme;
    authentication.AddPolicyScheme(TokenSchemes.ByEndpoint, TokenSchemes.ByEndpoint, o =>
    {
        o.ForwardDefault = fallbackScheme;
        o.ForwardDefaultSelector = context => Products.ForPath(context.Request.Path)?.AuthScheme;
    });

    authentication.AddMcp(o => o.Events.OnResourceMetadataRequest = context =>
    {
        // Each endpoint is a separate resource with its own authorization server and scopes. The request path is
        // /.well-known/oauth-protected-resource/<endpoint path>.
        const string wellKnown = "/.well-known/oauth-protected-resource";
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        var resourcePath = path.StartsWith(wellKnown, StringComparison.OrdinalIgnoreCase) ? path[wellKnown.Length..] : path;

        if (Products.ForPath(resourcePath) is { } product && enabled.Contains(product))
        {
            var request = context.HttpContext.Request;
            context.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = $"{request.Scheme}://{request.Host}{product.Path}",
                AuthorizationServers = { product.AuthorizationServer },
                // Write scopes are advertised only when the write tools exist, so people are not asked to
                // consent to access the server will never use.
                ScopesSupported = [.. product.Scopes, .. (writeEnabled[product.Key] ? product.WriteScopes : Array.Empty<string>())]
            };
        }

        return Task.CompletedTask;
    });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(AuthOptions.PolicyName, p => p.RequireAuthenticatedUser());
}

if (rateLimit.PermitsPerMinute > 0)
{
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(RateLimitOptions.PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(CallerClaims.AccountId)
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

var health = builder.Services.AddHealthChecks();
if (usesAtlassian)
{
    health.Add(new HealthCheckRegistration(
        "atlassian",
        sp => new ReachabilityHealthCheck(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ReachabilityHealthCheck>>(),
            "Atlassian", HttpNames.Atlassian, "oauth/token/accessible-resources"),
        null,
        null));
}

if (bitbucket.Enabled)
{
    health.Add(new HealthCheckRegistration(
        "bitbucket",
        sp => new ReachabilityHealthCheck(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ReachabilityHealthCheck>>(),
            "Bitbucket", HttpNames.Bitbucket, "user"),
        null,
        null));
}

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
    app.Logger.LogWarning("Auth:Enabled is false. The MCP endpoints are open to anyone who can reach them.");
}

if (rateLimit.PermitsPerMinute > 0)
{
    app.UseRateLimiter();
}

// Probes live on their own routes. The MCP endpoints expect JSON-RPC POSTs and reject a plain GET.
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/ready").AllowAnonymous();

// Teams and Power Platform cannot add the audience parameter Atlassian requires, so their authorization URL
// points here. Sign-in has no token yet, so the endpoint is anonymous; it only ever redirects to Atlassian.
if (usesAtlassian)
{
    var authorize = app.MapGet(AtlassianAuthorize.Path, AtlassianAuthorize.Handle).AllowAnonymous();
    if (rateLimit.PermitsPerMinute > 0)
    {
        authorize.RequireRateLimiting(RateLimitOptions.PolicyName);
    }
}

foreach (var product in enabled)
{
    var endpoint = app.MapMcp(product.Path);
    if (auth.Enabled)
    {
        endpoint.RequireAuthorization(AuthOptions.PolicyName);
    }

    if (rateLimit.PermitsPerMinute > 0)
    {
        endpoint.RequireRateLimiting(RateLimitOptions.PolicyName);
    }
}

app.Run();

/// <summary>Exposed so the test project can host the app in-process.</summary>
public partial class Program;

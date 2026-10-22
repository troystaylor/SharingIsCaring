using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using ModelContextProtocol.AspNetCore.Authentication;
using SalesforceMcp.Configuration;
using SalesforceMcp.Health;
using SalesforceMcp.Salesforce;
using SalesforceMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
auth.Validate();

var rateLimit = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new();

var salesforce = builder.Configuration.GetSection(SalesforceOptions.SectionName).Get<SalesforceOptions>() ?? new();
salesforce.Validate();

builder.Services.AddSingleton(auth);
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<SalesforceOptions>(builder.Configuration.GetSection(SalesforceOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

// The server holds no Salesforce credential. Each request carries the caller's own token, which tools read
// from the request through ISalesforceUser.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ISalesforceUser, HttpContextSalesforceUser>();
builder.Services.AddSingleton<SalesforceTokenValidator>();

// Calls that carry no caller token: checking a sign-in, and the health probe.
builder.Services
    .AddHttpClient(SalesforceHttp.PublicClientName, http => http.BaseAddress = salesforce.InstanceUri)
    .AddStandardResilienceHandler();

// Caches live in singletons because everything that fills them is created per request.
builder.Services.AddSingleton<MetadataCache>();
builder.Services.AddTransient<SalesforceVersions>();
builder.Services.AddTransient<SchemaService>();

builder.Services
    .AddHttpClient<SalesforceClient>(SalesforceClient.HttpClientName, http => http.BaseAddress = salesforce.InstanceUri)
    .AddStandardResilienceHandler(o =>
    {
        // A retried create would make a duplicate record, so only safe methods are retried.
        o.Retry.DisableForUnsafeHttpMethods();
    });

var mcp = builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "salesforce-mcp", Title = "Salesforce MCP Server", Version = "1.0.0" };
        o.ServerInstructions =
            "Reads Salesforce data through the Salesforce REST API, as the Salesforce user who is signed in, so " +
            "results are limited to what that person can see. If you are unsure of an object or field name, call list_objects " +
            "and describe_object before writing a SOQL query. Select specific fields and add a LIMIT. Use sosl_search " +
            "to find a name or keyword when you do not know which object holds it. Every record includes a " +
            "recordUrl you can cite. An empty records list means nothing matched, not that the call failed. " +
            "Tools use the server's default API version unless you pass apiVersion; call list_api_versions to see " +
            "what the org supports. Before creating or updating a record, call get_record_types and " +
            "get_picklist_values so the values you send are valid.";
    })
    // Container Apps load-balances across replicas with no session affinity, so sessions must not
    // live in process memory. Stateless mode makes every request self-contained.
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<SalesforceReadTools>()
    .WithTools<SalesforceSchemaTools>();

if (salesforce.EnableToolingTools)
{
    mcp.WithTools<SalesforceMetadataTools>();
}

if (salesforce.EnableWriteTools)
{
    mcp.WithTools<SalesforceWriteTools>();
}

if (auth.Enabled)
{
    builder.Services
        .AddAuthentication(o =>
        {
            // MCP owns the challenge so a 401 carries the RFC 9728 resource_metadata pointer that tells a
            // client where to sign in. The Salesforce handler checks the token itself.
            o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            o.DefaultAuthenticateScheme = SalesforceTokenDefaults.AuthenticationScheme;
            o.DefaultForbidScheme = SalesforceTokenDefaults.AuthenticationScheme;
        })
        .AddScheme<AuthenticationSchemeOptions, SalesforceTokenHandler>(
            SalesforceTokenDefaults.AuthenticationScheme, _ => { })
        .AddMcp(o => o.ResourceMetadata = new()
        {
            Resource = auth.ResourceUri,
            AuthorizationServers = { salesforce.InstanceUrl.TrimEnd('/') },
            ScopesSupported = ["api", "refresh_token", "openid"]
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
                context.User.FindFirstValue("user_id")
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

builder.Services.AddSingleton<SalesforceHealthCheck>();
builder.Services.AddHealthChecks().AddCheck<SalesforceHealthCheck>("salesforce");

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

var endpoint = app.MapMcp("/mcp");
if (auth.Enabled)
{
    endpoint.RequireAuthorization(AuthOptions.PolicyName);
}

if (rateLimit.PermitsPerMinute > 0)
{
    endpoint.RequireRateLimiting(RateLimitOptions.PolicyName);
}

app.Run();

/// <summary>Exposed so the test project can host the app in-process.</summary>
public partial class Program;

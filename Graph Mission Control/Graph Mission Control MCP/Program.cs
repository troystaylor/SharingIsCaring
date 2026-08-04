using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using GraphMissionControl.Mcp.Graph;
using GraphMissionControl.Mcp.Mcp;
using GraphMissionControl.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using OpenTelemetry.Instrumentation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Incomplete Entra config does not fail until a request arrives, and then it surfaces as
// a 500 rather than a 401 — which reads like a server bug instead of a deployment mistake.
foreach (var key in (string[])["Instance", "TenantId", "ClientId", "Audience"])
{
    if (string.IsNullOrWhiteSpace(builder.Configuration[$"AzureAd:{key}"]))
        throw new InvalidOperationException(
            $"AzureAd:{key} is not configured. Set it via the AzureAd__{key} environment variable.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

// Absent when running locally or under test, in which case there is nothing to export to.
var telemetryConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(telemetryConnection))
{
    // The two probes hit /health roughly every four seconds combined, which would otherwise
    // bury real traffic and dominate the ingestion bill.
    builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(
        o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"));

    var telemetryIdentity = builder.Configuration["AzureAd:ClientCredentials:0:ManagedIdentityClientId"];
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
    {
        o.ConnectionString = telemetryConnection;
        // Local auth is disabled on the component, so the ingestion key in the connection
        // string is inert and telemetry authenticates as the same identity as everything else.
        // ManagedIdentityCredential is unusable here: Azure.Core and Azure.Identity both
        // declare it in the same namespace, so no qualification can disambiguate it.
        if (!string.IsNullOrWhiteSpace(telemetryIdentity))
            o.Credential = new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { ManagedIdentityClientId = telemetryIdentity });
    });
}

// Copilot presents a token whose audience is the Entra SSO registration's Application ID URI
// rather than this app's own api:// URI. Both have to be accepted, or every Copilot call is
// rejected while direct clients keep working.
var extraAudiences = builder.Configuration["AzureAd:ExtraAudiences"];
if (!string.IsNullOrWhiteSpace(extraAudiences))
{
    var audiences = new List<string> { builder.Configuration["AzureAd:Audience"]! };
    audiences.AddRange(extraAudiences.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    builder.Services.PostConfigure<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme,
        o => o.TokenValidationParameters.ValidAudiences = audiences);
}

// The index is immutable and embedded; the registry is built once and validates
// read-only at construction, so a violation fails startup rather than a request.
builder.Services.AddSingleton(_ => CapabilityIndex.LoadEmbedded());
builder.Services.AddSingleton(sp => WorkTools.Build(sp.GetRequiredService<CapabilityIndex>()));
builder.Services.AddScoped<GraphClient>();

var app = builder.Build();

// Resolve both now so a missing or malformed index, or a tool that declares itself
// writable, fails startup rather than the first request.
var index = app.Services.GetRequiredService<CapabilityIndex>();
var registry = app.Services.GetRequiredService<ToolRegistry>();
app.Logger.LogInformation(
    "graph mission control ready: {Operations} read-only operations, {Tools} tools",
    index.ReadOnlyOperations.Count, registry.Tools.Count);

// Read back the audiences that survived configuration rather than the values that went in.
// Copilot presents the SSO Application ID URI, and a wrong audience fails only at the first
// real call — which reads as a Copilot fault rather than a misconfiguration here.
var jwt = app.Services
    .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme)
    .TokenValidationParameters;

var validAudiences = jwt.ValidAudiences?.ToArray() ?? [];
if (validAudiences.Length == 0 && jwt.ValidAudience is not null) validAudiences = [jwt.ValidAudience];
app.Logger.LogInformation("accepted token audiences: {Audiences}", string.Join(" | ", validAudiences));

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// MCP clients read this to learn how to authenticate instead of being configured by hand.
app.MapGet("/.well-known/oauth-protected-resource", (Delegate)((HttpContext ctx) =>
{
    var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var cfg = config.GetSection("AzureAd");
    var instance = cfg["Instance"]?.TrimEnd('/') ?? "https://login.microsoftonline.com";
    var tenant = cfg["TenantId"] ?? "common";
    var audience = cfg["Audience"] ?? "";

    // The ingress terminates TLS, so the request arrives as plain HTTP on an internal host.
    // Deriving the identifier from the request would both report the wrong scheme and let a
    // caller change it through the Host header, so the deployed URL is supplied explicitly.
    var publicUrl = config["Mcp:PublicUrl"];

    return Results.Ok(new
    {
        resource = string.IsNullOrWhiteSpace(publicUrl)
            ? $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            : publicUrl.TrimEnd('/'),
        authorization_servers = new[] { $"{instance}/{tenant}/v2.0" },
        scopes_supported = new[] { audience is "" ? "" : $"{audience}/.default" },
        bearer_methods_supported = new[] { "header" },
    });
})).AllowAnonymous();

app.MapMcpRoute("/mcp").RequireAuthorization();

app.Run();

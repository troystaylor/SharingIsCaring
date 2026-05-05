using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using FederatedMcpTemplate.Tools;

var builder = WebApplication.CreateBuilder(args);

// ── Authentication ──────────────────────────────────────────────────────────
//
// Federated Copilot connectors support two auth models:
//   - Entra SSO:  User authenticates with Microsoft 365 credentials (default)
//   - OAuth 2.0:  User authenticates with a third-party identity provider
//
// Both are registered in the Teams Developer Portal before creating the
// connector in the M365 admin center.
//
// The MCP C# SDK's .AddMcp() extension auto-hosts the Protected Resource
// Metadata (PRM) document at /.well-known/oauth-protected-resource and
// enables incremental scope consent.

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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
        ValidAudiences = auth.GetSection("ValidAudiences").Get<string[]>()
    };

    // ── OAuth 2.0 (third-party) ─────────────────────────────────────
    // For third-party OAuth, uncomment and configure:
    //
    // options.Authority = "https://accounts.google.com";          // OIDC discovery
    // options.TokenValidationParameters.ValidAudiences = new[] { "your-client-id" };
    //
    // Some third-party providers issue opaque tokens instead of JWTs.
    // In that case, use introspection or validate via the provider's
    // userinfo endpoint in a custom handler.
})
.AddMcp(options =>
{
    var auth = builder.Configuration.GetSection("Auth");
    options.ResourceMetadata = new()
    {
        ResourceDocumentation = (auth["DocumentationUrl"] ?? "https://example.com/docs"),
        AuthorizationServers =
        {
            (auth["Authority"] ?? "https://login.microsoftonline.com/common/v2.0")
        },
        ScopesSupported = auth.GetSection("Scopes").Get<string[]>() ?? ["mcp:tools"]
    };
});

builder.Services.AddAuthorization();

// ── HttpContext access (for token passthrough) ─────────────────────────────
builder.Services.AddHttpContextAccessor();

// ── HTTP Client for upstream API calls ──────────────────────────────────────
//
// Use IHttpClientFactory to call the upstream data source (Graph, HubSpot, etc.).
// The named client "upstream" is pre-configured with the base URL.
// Tools inject IHttpClientFactory and create clients per request.

builder.Services.AddHttpClient("upstream", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>().GetSection("Upstream");
    var baseUrl = config["BaseUrl"];
    if (!string.IsNullOrEmpty(baseUrl))
        client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ── MCP Server ──────────────────────────────────────────────────────────────
//
// Register the MCP server with the official C# SDK.
// Tools are discovered via [McpServerToolType] attribute classes.
// Each connector template should replace ExampleTools with its own tool class.
//
// Key constraints for federated connectors:
//   - Tools MUST be read-only (search, get, list, query)
//   - No create, update, or delete operations
//   - Include descriptive titles and descriptions for Copilot

var mcpConfig = builder.Configuration.GetSection("McpServer");

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = mcpConfig["Name"] ?? "federated-mcp-server",
            Version = mcpConfig["Version"] ?? "1.0.0",
            Title = mcpConfig["Title"] ?? "Federated MCP Server",
            Description = mcpConfig["Description"] ?? "A federated Copilot connector powered by MCP"
        };
    })
    .WithHttpTransport()
    .WithTools<ExampleTools>();

// ── Health checks ───────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ─────────────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint (unauthenticated, used by Azure Container Apps probes)
app.MapHealthChecks("/health").AllowAnonymous();

// MCP endpoint
// M365 admin center "Base URL" should point to this app's root URL.
// The SDK maps the MCP endpoint via MapMcp().
//
// In Development, auth is skipped so MCP Inspector / VS Code can connect
// without a token. In Production, auth is always required.
var mcpEndpoint = app.MapMcp();
if (!app.Environment.IsDevelopment())
{
    mcpEndpoint.RequireAuthorization();
}

app.Run();

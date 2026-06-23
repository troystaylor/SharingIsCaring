# Server-side Bearer validation patch

The current `Decision Duck/mcp-server/Program.cs` accepts any caller. Once Cowork is in the picture, the MCP server must validate the incoming Entra Bearer token. This document is the diff plan — apply, redeploy the container, then point Cowork at the same URL.

## What you need before editing

From the Entra app registration in [`../auth/README.md`](../auth/README.md):

- **Application (client) ID** — e.g. `8a3f1c5b-7d9e-4f2a-b1c3-d8e5f7a9c2b1`
- **Application ID URI** — e.g. `api://8a3f1c5b-7d9e-4f2a-b1c3-d8e5f7a9c2b1`
- **Required scope** — `access_as_user`

These become environment variables on the Container App:

| Env var | Example |
|--------|--------|
| `ENTRA_AUDIENCE` | `api://8a3f1c5b-7d9e-4f2a-b1c3-d8e5f7a9c2b1` |
| `ENTRA_CLIENT_ID` | `8a3f1c5b-7d9e-4f2a-b1c3-d8e5f7a9c2b1` |
| `ENTRA_REQUIRED_SCOPE` | `access_as_user` |

Leave them unset and the middleware short-circuits to "allow all" — useful for local `dotnet run`.

## 1. Add the NuGet package

In `Decision Duck/mcp-server/DecisionDuck.McpServer.csproj`, add:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.10" />
```

## 2. Wire JWT bearer auth in `Program.cs`

Insert near the top of `Program.cs`, after `var builder = WebApplication.CreateBuilder(args);` and before `builder.Build()`:

```csharp
var entraAudience = Environment.GetEnvironmentVariable("ENTRA_AUDIENCE");
var entraClientId = Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID");
var entraRequiredScope = Environment.GetEnvironmentVariable("ENTRA_REQUIRED_SCOPE") ?? "access_as_user";
var authEnabled = !string.IsNullOrWhiteSpace(entraAudience) && !string.IsNullOrWhiteSpace(entraClientId);

if (authEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = "https://login.microsoftonline.com/common/v2.0";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidAudiences = new[] { entraAudience, entraClientId },
                ValidateIssuer = false,        // multi-tenant: trust any v2.0 Entra issuer
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("DecisionDuckCaller", policy =>
            policy
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                {
                    var scp = ctx.User.FindFirst("scp")?.Value
                              ?? ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value;
                    return scp?.Split(' ').Contains(entraRequiredScope, StringComparer.OrdinalIgnoreCase) == true;
                }));
    });
}
```

Add the using imports at the top:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
```

After `var app = builder.Build();`, register the middleware:

```csharp
if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
```

## 3. Protect the MCP endpoint

Change the existing `app.MapPost("/mcp", …)` registration so it requires the policy when auth is enabled:

```csharp
var mcpEndpoint = app.MapPost("/mcp", async (HttpRequest request, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    // …existing body unchanged…
});

if (authEnabled)
{
    mcpEndpoint.RequireAuthorization("DecisionDuckCaller");
}
```

Leave `/health` and `GET /mcp` anonymous so platform probes and "is it alive?" checks still work.

## 4. Set the env vars on Container Apps

```powershell
az containerapp update `
  --name decisionduck-mcp `
  --resource-group rg-decisionduck `
  --set-env-vars `
    ENTRA_AUDIENCE="api://8a3f1c5b-7d9e-4f2a-b1c3-d8e5f7a9c2b1" `
    ENTRA_CLIENT_ID="8a3f1c5b-7d9e-4f2a-b1c3-d8e5f7a9c2b1" `
    ENTRA_REQUIRED_SCOPE="access_as_user"
```

Then redeploy or restart so the revision picks the vars up.

## 5. Smoke test

```powershell
# Should now return 401
curl -i -X POST https://decisionduck-mcp.mangocliff-85624d1f.westus2.azurecontainerapps.io/mcp `
  -H "Content-Type: application/json" `
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# /health should still be 200
curl -i https://decisionduck-mcp.mangocliff-85624d1f.westus2.azurecontainerapps.io/health
```

Once Cowork is connected, the same call from inside Cowork will succeed because Cowork adds the Bearer header from the Vault.

## What stays the same

- `resources/list` / `resources/read` for the MCP Apps inline UI (`ui://decision-duck/comparative-analysis.html`) — Cowork ignores it, M365 Copilot still renders it.
- `tools/list`, `tools/call`, `initialize` handlers — unchanged.
- Local `dotnet run` — leave `ENTRA_*` unset and the middleware no-ops.

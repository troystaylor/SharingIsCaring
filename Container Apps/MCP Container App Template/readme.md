# MCP Container App Template

A production-shaped starting point for hosting your own [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server on **Azure Container Apps**. You write the tools. The template supplies the host, sign-in, rate limiting, health probes, infrastructure, and deployment script.

It is a standalone MCP server that calls your system's own API. It is not a wrapper around a vendor-hosted MCP server.

## What you get

| Area | What the template does |
|------|------------------------|
| Server | ASP.NET Core on .NET 10 with the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore` 2.2.0), served at `/mcp` over Streamable HTTP |
| Scaling | Stateless transport, so any replica can answer any request. No session affinity needed |
| Sign-in | Validates Microsoft Entra access tokens (audience, issuer, lifetime, and a required scope or app role). A `401` carries the `resource_metadata` pointer that MCP clients use to discover sign-in, and the server publishes [protected resource metadata](https://datatracker.ietf.org/doc/html/rfc9728) |
| Abuse control | Per-caller rate limit (default 120 requests per minute) |
| Upstream calls | A typed `HttpClient` with the standard resilience pipeline (retry, timeout, circuit breaker) |
| Probes | `/healthz` (liveness, no dependencies) and `/ready` (readiness). Both are anonymous and separate from `/mcp` |
| Telemetry | Application Insights through OpenTelemetry, enabled when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set |
| Infrastructure | Bicep for Container Apps environment, Container Registry, Key Vault, Log Analytics, Application Insights, and a user-assigned managed identity |
| Secrets | Key Vault references resolved by the managed identity. No registry admin account, no secrets in environment variables |
| Deployment | One script that provisions, builds the image in Azure Container Registry, and rolls the app |
| Copilot Studio | A connector definition that passes MCP traffic through with OAuth |

## Layout

| Path | Purpose |
|------|---------|
| `src/McpContainerApp/` | The server: `Program.cs`, configuration, tools |
| `src/McpContainerApp/Tools/` | **Replace these.** An example tool class and its upstream client |
| `tests/McpContainerApp.Tests/` | In-process tests for sign-in, scope enforcement, rate limiting, metadata, and tools |
| `infra/main.bicep` | Azure resources |
| `infra/deploy.ps1` | Provision, build, and deploy |
| `scripts/New-EntraAppRegistration.ps1` | Creates the Entra app registration for the server |
| `connector/` | Power Platform connector for Copilot Studio |
| `Dockerfile` | Multi-stage build, runs as a non-root user on port 8080 |

## Prerequisites

- An Azure subscription where you can create resources and role assignments
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.62 or later, signed in with `az login`
- Permission to register applications in your Microsoft Entra tenant

## Quick start

### 1. Run it locally

```powershell
dotnet run --project src/McpContainerApp --environment Development
```

In `Development`, `Auth:Enabled` is `false` so you can connect without a token. Point [MCP Inspector](https://github.com/modelcontextprotocol/inspector) or VS Code at `http://localhost:5000/mcp` (the port is printed at startup). The server logs a warning whenever auth is off.

### 2. Make it yours

1. Copy this folder and rename it.
2. Rename `McpContainerApp` to your project name. That covers the folders, `.csproj`, `.slnx`, the namespace, the `Dockerfile`, and the `McpContainerApp` segment in `Directory.Build.props`.
3. Replace `Tools/ExampleTools.cs` and `Tools/ExampleUpstreamClient.cs` with your own tools and API client. Register tool classes in `Program.cs` with `.WithTools<YourTools>()`.
4. Rename `Upstream` options in `Configuration/UpstreamOptions.cs` to match your system, and update `appsettings.json`.
5. Update the server name and version in `Program.cs` and the titles in `connector/`.

Write each tool's `[Description]` for the model. It is the only thing the model sees when it decides which tool to call and what to pass.

### 3. Create the Entra app registration

```powershell
.\scripts\New-EntraAppRegistration.ps1 -DisplayName "My MCP Server"
```

It prints your tenant ID and client ID. The registration exposes one delegated scope, `access_as_user`, and issues v2 access tokens. The server only accepts v2 tokens.

### 4. Deploy

```powershell
.\infra\deploy.ps1 `
    -ResourceGroup rg-my-mcp `
    -TenantId <tenant-id> `
    -ClientId <client-id> `
    -AppName mymcp `
    -UpstreamBaseUrl https://api.example.com `
    -UpstreamClientSecret (Read-Host -AsSecureString "Upstream secret")
```

The script prints your MCP endpoint (`https://<fqdn>/mcp`). `-AppName` is 3 to 12 characters and drives resource names and the public hostname, so pick it once.

### 5. Test the deployment

```powershell
curl https://<fqdn>/healthz                                          # 200
curl https://<fqdn>/.well-known/oauth-protected-resource/mcp         # 200, names your tenant as the authorization server
curl -i -X POST https://<fqdn>/mcp                                    # 401 with a WWW-Authenticate header
```

## Connect a client

**Copilot Studio.** Edit `connector/apiDefinition.swagger.json` and `connector/apiProperties.json`, replacing each `[YOUR_...]` value. Deploy with the Power Platform CLI:

```powershell
pac connector create --api-definition-file connector/apiDefinition.swagger.json `
                     --api-properties-file connector/apiProperties.json
```

The connector signs users in with OAuth and PKCE using a **separate public client** app registration. Create one for the connector and:

- Register the connector's redirect URL under **Mobile and desktop applications** (`publicClient`), not Web or Single-page application. Other platform types accept the URL at registration but fail at consent.
- Grant it the delegated `access_as_user` permission on your server's app registration, and grant admin consent, or add the client under **Expose an API → Authorized client applications**.

After deployment the platform generates a redirect URL for the connector. Read it back from the connector's Security tab and register that exact URL on the public client. Add one for each environment you deploy the connector to.

**VS Code or another MCP client.** Send a bearer token in the `Authorization` header. To get one for testing, allow the Azure CLI to request tokens for your API (add client ID `04b07795-8ddb-4961-a4ec-4b1b5f3d8d86` under **Expose an API → Authorized client applications**), then:

```powershell
az account get-access-token --resource api://<client-id> --query accessToken -o tsv
```

## Configuration

Set these as environment variables in Azure using `__` for nesting (`Auth__TenantId`). `infra/main.bicep` already does this.

| Setting | Default | Purpose |
|---------|---------|---------|
| `Auth:Enabled` | `true` | Turns token validation on. Startup fails if it is on without a valid tenant and client ID |
| `Auth:TenantId` | | Entra tenant that issues tokens |
| `Auth:ClientId` | | Client ID of the server's app registration. Accepted audiences are this value and `api://<ClientId>` |
| `Auth:ScopeName` | `access_as_user` | Delegated scope a signed-in caller must hold |
| `Auth:AppRole` | none | App role that lets an app-only caller (no user) in |
| `Auth:AdditionalAudiences` | none | Extra accepted `aud` values |
| `Auth:ResourceUri` | inferred | Overrides the resource identifier in protected resource metadata |
| `RateLimit:PermitsPerMinute` | `120` | Per-caller limit. `0` turns it off |
| `Upstream:BaseUrl` | | Base URL of the API you front |
| `Upstream:ClientSecret` | | Upstream credential. In Azure it comes from Key Vault |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | | Enables telemetry |

### Infrastructure parameters

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `minReplicas` | `1` | Keep at 1 or more for interactive use. Zero adds a cold start to the first tool call |
| `maxReplicas` | `5` | Scale-out ceiling. Scaling is driven by 50 concurrent requests per replica |
| `allowedIpRanges` | none | Restrict ingress to these CIDR ranges |
| `corsAllowedOrigins` | none | Only needed for browser-based clients. Desktop clients such as VS Code do not need it |

## Run the tests

```powershell
dotnet test McpContainerApp.slnx
```

## Security notes

- Treat every tool argument as untrusted input. Validate it before it reaches your API.
- Return upstream failures as a status, not a response body. Upstream bodies can expose internals.
- Return an empty result as a success. An agent that sees an error for "nothing found" tends to invent an answer.
- Tools that read data should set `ReadOnly = true`. Tools that change data should set `Destructive` correctly. These are hints to the client, so also enforce permissions in your upstream credential.
- Container Apps also offers [built-in authentication](https://learn.microsoft.com/azure/container-apps/authentication) with Entra. This template validates tokens in the app instead, so the server can publish resource metadata and enforce a scope or role. If you prefer the platform feature, set `Auth:Enabled` to `false` and keep the ingress restricted.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| App fails at startup with `Auth:TenantId must be a GUID` | Auth is on and `authTenantId` or `authClientId` was not passed to the deployment |
| `401` for a valid-looking token | The token's audience or issuer does not match. Confirm the app registration issues v2 tokens (`requestedAccessTokenVersion` is `2`) and the token's `aud` is the client ID or `api://<client-id>` |
| `403` | The token is valid but has no `access_as_user` scope, and no matching app role. Request the scope when signing in |
| `AADSTS7000218` or `AADSTS9002327` at connector sign-in | The connector's redirect URL is registered under the wrong platform. Move it to **Mobile and desktop applications** |
| First revision never becomes ready | Check the container's logs in Log Analytics. A crash at startup is usually the auth validation above |
| `429` | The caller exceeded `RateLimit:PermitsPerMinute` |
| Secret reference error on first provision | Role assignments take a short time to propagate. Run `deploy.ps1` again |

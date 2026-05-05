# Federated MCP Template

A template for building **custom federated Copilot connectors** that bring real-time data into Microsoft 365 Copilot via Model Context Protocol (MCP). Built on the official [MCP C# SDK v1.0](https://github.com/modelcontextprotocol/csharp-sdk) and ASP.NET Core.

## Architecture

```
M365 Copilot Chat / Researcher
    │
    │  MCP (JSON-RPC 2.0 over Streamable HTTP)
    │  Auth: Entra SSO or OAuth 2.0 (per-user)
    │
    ▼
┌──────────────────────────────────┐
│  Federated MCP Server            │
│  (Azure Container Apps)          │
│                                  │
│  Program.cs ── MCP SDK + Auth    │
│  Tools/    ── [McpServerTool]    │
│                                  │
│  Token passthrough to upstream   │
└──────────┬───────────────────────┘
           │
           │  REST API (bearer token forwarded)
           │
           ▼
    Upstream Data Source
    (Graph, HubSpot, Gong, Jira, etc.)
```

**Option B architecture**: Each connector is deployed as its own container app with its own auth configuration and MCP endpoint. This gives:
- Per-connector auth isolation (Entra SSO for Microsoft APIs, OAuth 2.0 for third-party)
- Independent scaling and deployment
- Clean mapping to M365 admin center (one connector = one Base URL)

## How Federated Connectors Differ from Sync Connectors

| | Sync (Graph Connectors) | Federated (This Template) |
|---|---|---|
| Data freshness | Crawl schedule | Real-time at query time |
| Data location | Copied to M365 tenant | Stays in source system |
| Protocol | Graph External Items API | MCP (JSON-RPC 2.0) |
| Tools | N/A | Read-only tools |
| Surfaces | Copilot, Search, Context IQ | Copilot Chat, Researcher, Excel |

## Prerequisites

- [.NET 10 SDK](https://get.dot.net/10)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`)
- [Docker](https://docs.docker.com/get-docker/) (for container builds)
- Azure subscription with permission to create Container Apps and Container Registry
- Microsoft 365 tenant with Copilot license
- Admin access: Global Administrator or AI Administrator in M365 admin center

## Quick Start

### 1. Clone and configure

```bash
cp -r "Connector-Code/Federated MCP Template" MyConnector
cd MyConnector
```

Edit `appsettings.json`:
- `McpServer.Name` — unique connector identifier
- `McpServer.Title` — user-facing name in M365 Copilot
- `Auth.Authority` — your Entra ID authority URL
- `Auth.ValidAudiences` — your app registration client ID
- `Upstream.BaseUrl` — the API your connector proxies

### 2. Add your tools

Replace `Tools/ExampleTools.cs` with tools for your data source. Rules for federated connectors:

- **Read-only only**: search, get, list, query — no create/update/delete
- **Descriptive**: clear `[Description]` attributes for Copilot's tool selection
- **Citations**: include source URLs in responses for verification links

```csharp
[McpServerToolType]
public class MyTools(IHttpClientFactory httpClientFactory, IHttpContextAccessor contextAccessor)
{
    [McpServerTool(Title = "Search Deals")]
    [Description("Search for deals matching criteria. Returns deal name, stage, value, and owner.")]
    public async Task<string> SearchDeals(
        [Description("Search query")] string query,
        [Description("Max results (1-50)")] int top = 10,
        CancellationToken ct = default)
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/deals?q={Uri.EscapeDataString(query)}&limit={top}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

Register your tools in `Program.cs`:
```csharp
.WithTools<MyTools>();
```

### 3. Run locally

```bash
dotnet run
```

Test with any MCP client (VS Code, MCP Inspector) at `http://localhost:5000`. In Development mode, authentication is skipped so you can test tools without configuring JWT tokens. Auth is always enforced in Production.

### 4. Deploy to Azure

```powershell
cd infra
.\deploy.ps1 `
    -ConnectorName "my-connector" `
    -ResourceGroup "rg-mcp-connectors" `
    -RegistryName "mcpregistry" `
    -TenantId "00000000-0000-0000-0000-000000000000" `
    -AppClientId "11111111-1111-1111-1111-111111111111" `
    -UpstreamBaseUrl "https://api.example.com"
```

The script outputs the **MCP Base URL** needed for the next step.

## Register as a Federated Connector

### Step 1: Set up authentication

**For Entra SSO** (Microsoft APIs):
1. Create/update an Entra ID app registration
2. In [Teams Developer Portal](https://dev.teams.microsoft.com/) > Tools > OAuth Client Registration, create an SSO registration
3. Copy the **SSO registration ID**

**For OAuth 2.0** (third-party APIs like HubSpot, Gong):
1. Register your app with the OAuth provider, using redirect URI: `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`
2. In [Teams Developer Portal](https://dev.teams.microsoft.com/) > Tools > OAuth Client Registration, create an OAuth connection with the provider's client ID, secret, and endpoints
3. Copy the **OAuth registration ID**

### Step 2: Create the connector

1. Sign in to [M365 admin center](https://admin.microsoft.com/)
2. Navigate to **Copilot > Connectors > Gallery**
3. Under **Created by your org**, select **Create a new connector > Connect to MCP server**
4. Enter:
   - **Display name**: User-facing connector name
   - **Base URL**: The MCP Base URL from deployment
   - **Registration ID**: The SSO or OAuth ID from Step 1
5. **Save**

### Step 3: Stage rollout

1. Select your connector in **Your Connections**
2. Select **Staged rollout**
3. Add test users or groups
4. When ready, select **Deploy to all users**

## Project Structure

```
├── Program.cs                   # ASP.NET Core host, auth, MCP server config
├── FederatedMcpTemplate.csproj  # .NET 10 project, MCP SDK references
├── appsettings.json             # Server, auth, and upstream configuration
├── Tools/
│   └── ExampleTools.cs          # MCP tool definitions (replace with yours)
├── Dockerfile                   # Multi-stage build for Azure Container Apps
└── infra/
    ├── main.bicep               # Container App + environment + logging
    └── deploy.ps1               # End-to-end build, push, deploy script
```

## Auth Patterns

### Token passthrough (simplest)

The template includes a `CreateAuthenticatedClient()` helper that extracts the bearer token from the incoming MCP request and forwards it to the upstream API. This works when:
- The upstream API accepts the same token (e.g., Microsoft Graph with Entra SSO)
- The third-party API accepts its own OAuth token directly

### On-Behalf-Of (OBO) flow

For calling a downstream API that requires a different audience:
```csharp
// In your tool, exchange the incoming token for a downstream token
var incomingToken = contextAccessor.HttpContext?.Request.Headers.Authorization
    .ToString().Replace("Bearer ", "");
var downstreamToken = await confidentialClient
    .AcquireTokenOnBehalfOf(scopes, new UserAssertion(incomingToken))
    .ExecuteAsync();
```

### Service-to-service (managed identity)

For APIs that use Azure managed identity instead of user tokens:
```csharp
var credential = new DefaultAzureCredential();
var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://api.example.com/.default" }));
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
```

## SDK Features Available

The [MCP C# SDK v1.0](https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/) provides:

| Feature | Status | Notes |
|---|---|---|
| Tool registration | `[McpServerTool]` attribute | Auto-discovered, type-safe |
| Auth (PRM + JWT) | Built-in | `.AddMcp()` auto-hosts PRM document |
| Incremental scope consent | Built-in | 401/403 with scopes in WWW-Authenticate |
| Long-running requests | `EnablePollingAsync()` | SSE + client polling |
| Tasks (experimental) | `IMcpTaskStore` | Durable result tracking with TTL |
| Icons | `IconSource` parameter | On tools, resources, prompts |
| Resources | `[McpServerResource]` | Static and template-based |
| Prompts | `[McpServerPrompt]` | Reusable prompt templates |

## Three Surfaces, One Server

A single MCP server built from this template can serve:

| Surface | How |
|---|---|
| **M365 Copilot Chat / Researcher** | Register as federated connector in M365 admin center |
| **Copilot Studio** | Wrap with a Power Platform custom connector using `x-ms-agentic-protocol` |
| **Direct MCP clients** | Point VS Code, Claude Desktop, or MCP Inspector at the Base URL |

## References

- [Federated Copilot connectors — bringing real-time enterprise data within Microsoft 365 Copilot](https://techcommunity.microsoft.com/blog/microsoft365copilotblog/federated-copilot-connectors---bringing-real-time-enterprise-data-within-microso/4515993) — announcement blog
- [Federated connectors overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/federated-connectors-overview) — admin and architecture overview
- [Set up custom federated connectors](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/set-up-custom-federated-connectors) — step-by-step setup guide
- [Manage federated connectors](https://learn.microsoft.com/en-us/microsoft-365/copilot/connectors/manage-federated-connectors) — admin center management
- [MCP C# SDK v1.0 release](https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/) — SDK features and patterns
- [MCP C# SDK repository](https://github.com/modelcontextprotocol/csharp-sdk) — source and API reference
- [MCP Specification (2025-11-25)](https://modelcontextprotocol.io/specification/2025-11-25) — protocol specification
- [Teams Developer Portal](https://dev.teams.microsoft.com/) — OAuth / SSO registration
- [M365 admin center](https://admin.microsoft.com/) — connector creation and management

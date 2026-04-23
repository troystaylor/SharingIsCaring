# Decision Duck

Decision Duck is an agent-plus-plugin project for Microsoft 365 Copilot. It provides structured decision-support actions and reusable knowledge assets without using a Microsoft 365 Copilot connector.

## What This Project Includes

- 4 analysis actions:
  - `GetSecondOpinion`
  - `AnalyzeRisk`
  - `IdentifyCognitiveBiases`
  - `ComparativeAnalysis`
- 2 knowledge actions:
  - `ListKnowledgeAssets`
  - `GetKnowledgeAsset`
- MCP Apps support in v1:
  - `comparative_analysis` advertises a UI resource
  - `ui://decision-duck/comparative-analysis.html` renders an inline app view
- Agent package artifacts:
  - `agent-package/declarativeAgent.json`
  - `agent-package/plugin.json`
  - `agent-package/manifest.json`
- Remote MCP runtime in `mcp-server`.

## Files

- `agent-package/declarativeAgent.json`: Declarative agent manifest (schema v1.6)
- `agent-package/plugin.json`: Plugin manifest (schema v2.4) using `RemoteMCPServer`
- `agent-package/manifest.json`: Microsoft 365 app manifest template referencing the declarative agent
- `mcp-server/DecisionDuck.McpServer.csproj`: .NET 8 MCP server project
- `mcp-server/Program.cs`: MCP endpoint implementation (`/mcp` and `/health`)
- `infra/main.bicep`: Azure Container Apps deployment template
- `infra/deploy.ps1`: Deployment helper for Bicep template
- `readme.md`: Setup and usage guidance

## GitHub Prep Checklist

Before pushing this folder to GitHub:

1. Keep local/generated files out of source control using `.gitignore`:
  - Build outputs (`mcp-server/bin`, `mcp-server/obj`)
  - Packaged zip artifacts (`decisionduck-app.zip`)
  - Local debug/deployment artifacts (`DEPLOYMENT_STATUS.md`, temporary auth patch files)
2. Never commit secrets:
  - API keys
  - access tokens
  - full connection strings
3. Treat endpoint and tenant-specific IDs as environment-specific values and update for your destination environment as needed:
  - `agent-package/plugin.json` (`runtimes[].spec.url`, `auth.reference_id`)
  - `agent-package/manifest.json` (`id`, `validDomains`)

## Runtime Requirement

The declarative agent plugin requires a reachable MCP endpoint that implements the tool contract in `agent-package/plugin.json`.

Use the included server in `mcp-server` and expose `/mcp` over HTTPS.

For MCP Apps rendering, the endpoint must also support `resources/list` and `resources/read` for the `ui://` resource.

## Agent + Plugin Setup (No Copilot Connector)

1. Deploy `mcp-server` where M365 Copilot can reach it.
2. In `agent-package/plugin.json`, update `spec.url` with your MCP endpoint URL (for example, `https://your-host/mcp`).
3. In `agent-package/manifest.json`, replace:
  - app `id` with a real GUID
  - `validDomains` with your host domain
  - icon files (`color.png`, `outline.png`) with real assets
4. Package and deploy with Microsoft 365 Agents Toolkit.
5. Install the app and run the declarative agent in Microsoft 365 Copilot.

This path intentionally skips Microsoft 365 Copilot connectors.

## Next Build Step

Host the MCP server and secure it with Entra auth for production, then point `agent-package/plugin.json` to that endpoint.

## Local Run (MCP Server)

```powershell
cd ".\mcp-server"
dotnet run
```

Default model settings:
- `FOUNDRY_ENDPOINT` (default: `http://localhost:60311/v1`)
- `FOUNDRY_MODEL` (default: `phi-4`)
- `FOUNDRY_API_KEY` (optional)

## Azure Deployment

Deploy the opinionated runtime stack (Container Apps + Managed Identity + App Insights + Log Analytics + Key Vault):

```powershell
cd ".\infra"
.\deploy.ps1 `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroupName "rg-decisionduck" `
  -Location "westus2" `
  -FoundryEndpoint "https://<foundry-endpoint>/openai/v1" `
  -FoundryModel "phi-4"
```

By default, the deploy script creates/uses Azure Container Registry, builds the image from `mcp-server/Dockerfile`, pushes it, and deploys Container Apps.

Optional overrides:
- `-ContainerImage` to use a prebuilt image
- `-AcrName` to control the registry name when auto-building

After deployment, use the reported MCP endpoint host in `agent-package/plugin.json` under `runtimes[0].spec.url`.

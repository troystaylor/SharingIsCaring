# C3 AI plugin

Microsoft 365 Copilot plugin for the [C3 Agentic AI Platform](https://docs.c3.ai/docs/platform/8.11/topic/platform-overview). It connects Copilot to your C3 application through its built-in **App MCP** server, so Copilot can explore entity types and data, read time series metrics, make confirmed data changes, support the developer loop (types, package issues, tests, seed data) and help you design agent tools. A second connector reaches the **C3 Central MCP** server for C3 platform documentation and coding guidance.

Every call runs as you, under your C3 roles. Your application records each call in **MCP > Monitor** in C3 AI Studio. The plugin contains no tokens, server addresses or IDs. You supply your own through placeholders, so you can share this folder as is.

| Component | Path |
| --- | --- |
| Connector `c3-app-mcp` (your application's App MCP server, C3 token as an API key) | `appPackage/manifest.json` |
| Connector `c3-central-mcp` (`https://mcp.c3.ai/mcp/server`, C3 token as an API key) | `appPackage/manifest.json` |
| Shared skills: `c3-access`, `c3-write-safety` | `appPackage/skills/*/SKILL.md` |
| Task skills | `appPackage/skills/*/SKILL.md` |

| Skill | What it does | Connector tools it uses | Writes |
| --- | --- | --- | --- |
| `c3-explore-data` | List entity types and fields, then filter, count and aggregate records | `getEntityTypes`, `entityFieldRetriever`, `entityDataRetriever` | No |
| `c3-metrics` | List and evaluate time series metrics for a type | `listTypeMetrics` and your metric tools | No |
| `c3-change-data` | Create, update and delete records with preview and verification | `entityDataModifier` | After confirmation |
| `c3-developer-loop` | Look up types, check package issues, run tests, save seed data, search C3 docs | `getC3TypeInformation`, `readPkgIssues`, `runTest`, `upsertSeedData`, Central MCP | Seed data, after confirmation |
| `c3-build-agent-tools` | Design, document and test custom tools and prompts for App MCP | `getC3TypeInformation`, your custom tools | No |

## How tools are exposed

The plugin has no `mcpToolDescription`, so Copilot reads the live tool list from your application. That means the tools you enable, and any custom tools you add, work without a plugin update. C3 turns on `runPyCode` and `runJsCode` by default and seeds the other tools disabled. An App Admin enables them under **MCP > Tools** by switching on **Enabled for MCP**. The skills only use tools that appear in the live list, and treat code execution as a change that needs your confirmation.

## Prerequisites

- Node.js 24.15 or later and [Work IQ Dev Tools (WIQD)](https://aka.ms/wiqd/docs) 0.25 or later: `wiqd --version` should work. On older Node versions, `wiqd` extensions fail to load and `wiqd plugin validate` reports `load-failed`.
- A Microsoft 365 tenant where you can upload custom plugins.
- A C3 Agentic AI Platform application on version 8.9 or later. Version 8.10 and later include the `mcpServer` package. On 8.9, add `mcpServer` as a dependency in your application package.
- A C3 role that lets you open **MCP > Access** in C3 AI Studio. An App Admin must switch the MCP server on and enable the tools you want.
- For the Central MCP connector: a C3 AI Developer Portal login.

## Setup

1. **Get your App MCP URL and token.** In C3 AI Studio, open your application and go to **MCP > Access > My Tokens**. Choose **Other**, select **Generate Token**, and copy the server URL and the token. The URL looks like `https://<cluster>/<env>/<app>/mcp/server`. Tokens last 12 months.
2. **Get your Central MCP token.** Sign in at [mcp.c3.ai](https://mcp.c3.ai) with your C3 AI Developer Portal credentials and select **Copy Token**.
3. **Register the tokens** in the Teams Developer Portal ([dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools)) under Tools > API key registration > Register API key. Create one registration for each connector:
   - **Base URL**: the App MCP URL from step 1, or `https://mcp.c3.ai/mcp/server`.
   - **Restrict usage by org**: **My organization only**.
   - **Restrict usage by app**: **Any Teams app**.
   - **API key**: `C3Bearer <token>`, with the word `C3Bearer`, a space, then the token. C3 expects this exact scheme in the `Authorization` header.
   - **Add the key to**: **HTTP header**, with the parameter name `Authorization`.
   - Note the **API key registration ID** the portal shows.
4. **Create your environment files.** From this folder:

   ```powershell
   Copy-Item env\.env.dev.sample env\.env.dev
   Copy-Item env\.env.dev.user.sample env\.env.dev.user
   ```

   Edit `env/.env.dev.user` and replace the four placeholders: `C3_APP_MCP_SERVER_URL`, `C3_APP_MCP_SERVER_DOMAIN` (the host of that URL), `C3_APP_MCP_API_KEY_REFERENCE_ID` and `C3_CENTRAL_MCP_API_KEY_REFERENCE_ID`. Both files are gitignored. The manifest only references these variables.

## Build and share

```powershell
wiqd auth login --interactive
wiqd plugin validate
wiqd plugin provision --env dev
wiqd plugin package --env dev          # only after provision
wiqd plugin validate --mode deep --env dev
wiqd plugin share --scope users --email <address>
```

`wiqd plugin validate` on its own only checks statically, and passes for a plugin with skills and connectors but no declarative agent. Run the deep validation after packaging for the full check.

Bump `version` in `appPackage/manifest.json` before uploading a rebuilt package. After provisioning, ask your admin to review and assign the plugin in the Microsoft 365 admin center under **Agents > Tools**.

Each person who uses the plugin should have their own C3 token and API key registration, so calls run as that person. A shared token makes every call run as one user and removes the per-user audit trail in **MCP > Monitor**.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Tool call returns 401 or 403 | Your C3 token is missing, expired or revoked. Generate a new one under **MCP > Access** and update the API key registration. |
| Copilot lists no C3 tools | The MCP server is off, or no tools are enabled. An App Admin switches the server on from the **MCP > Tools** header and enables tools with **Enabled for MCP**. |
| A tool you expect is missing | The tool is a draft, disabled, or outside your toolkit when toolkit filtering is on. Store and enable it under **MCP > Tools**. |
| Connector can't reach the server | Check that `C3_APP_MCP_SERVER_URL` ends in `/mcp/server`, that the application is running, and that `C3_APP_MCP_SERVER_DOMAIN` is the URL's host. |
| `runTest` or `readPkgIssues` shows old results | They check what is provisioned in the application, not your local edits. Sync your package and run them again. |
| Central MCP asks you to sign in again | Sign in at [mcp.c3.ai](https://mcp.c3.ai), copy a new token and update its API key registration. |

## Using this with the C3 AI VS Code extension

The official [C3 AI VS Code extension](https://marketplace.visualstudio.com/items?itemName=C3ai.c3-ai-dx-v8) is where you write, sync, test and debug C3 code. It requires platform 8.4 or later. This plugin doesn't replace it. It lets Microsoft 365 Copilot work with your running application's data, metrics, tests and tool design, and it can't edit your local files. For code you've changed locally, sync your package in the extension first (the file must show as synced or validated), then ask Copilot to run `readPkgIssues` or `runTest`.

Platform versions 8.4 to 8.8 work with the extension but not with this plugin, which needs the App MCP server (8.9 or later).

## Notes

- Replace `appPackage/color.png` and `appPackage/outline.png` with your own icons before publishing if you want your own branding.
- The skills describe the seeded C3 tools by name. Check each tool's input schema in your application, because parameters vary.
- Plugins are available in the experiences the Microsoft Copilot plugin registry supports. Your admin controls who gets this plugin.

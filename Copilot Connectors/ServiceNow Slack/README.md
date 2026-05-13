# ServiceNow Slack Copilot Connector

A Microsoft 365 Copilot connector that ingests Slack messages and attachments indexed by ServiceNow AI Search into Microsoft Graph, making them searchable in Microsoft 365 Copilot.

## How It Works

```
Slack (public channels)
    │
    ▼
ServiceNow AI Search ──── Slack External Content Connector
    │                      (built-in, crawls via Slack API)
    │
    ▼
ServiceNow Indexed Source Table (e.g. u_slack_content)
    │
    ▼
This Connector (Azure Function) ──── reads via Table API
    │
    ▼
Microsoft Graph External Connection
    │
    ▼
Microsoft 365 Copilot (searchable)
```

ServiceNow's built-in Slack connector crawls public channels and indexes the content. This connector reads that indexed content via the ServiceNow Table API and pushes it to Microsoft Graph as external items, making it searchable in M365 Copilot.

## What Gets Indexed

| Content Type | Fields |
|---|---|
| **Messages** | Text, author, channel, timestamp, thread info, reactions, replies |
| **Attachments** | File names, file types, associated message context |

All content comes from **public Slack channels only**. ACLs are set to tenant-wide access since ServiceNow's Slack connector explicitly scopes to public channels.

## Prerequisites

### 1. Slack API Application

Create a Slack API app to allow ServiceNow to crawl your workspace:

1. Navigate to https://api.slack.com/apps and select **Create an App** > **From scratch**
2. Name the app and select your workspace
3. Record the **Client ID** and **Client Secret** from App Credentials
4. Navigate to **OAuth & Permissions**:
   - Add redirect URL: `https://<your-instance>.service-now.com`
   - Add **Bot Token Scopes**: `channels:history`, `channels:join`, `channels:read`, `files:read`, `remote_files:read`, `team:read`, `users:read`
5. Install the app to your workspace and select **Allow**
6. Opt into **Advanced token security via token rotation**

### 2. ServiceNow Slack External Content Connector

Create the connector in ServiceNow (requires `sn_ext_conn.xcc_admin` role):

1. Navigate to **All > External Content Connectors > External Content Admin Home**
2. Switch to the **External Content Connectors Admin** scope
3. Select **New** > **Slack** tile
4. Enter connection settings:
   - **Connector name**: Your chosen name
   - **URL**: `https://slack.com`
   - **Client ID**: From the Slack API app
   - **Client secret**: From the Slack API app
5. Select **Validate Connection**
6. Optionally configure crawl settings:
   - **Channel filtering**: Include/exclude specific channels by URL (`https://workspace.slack.com/archives/{channelId}`)
   - **Attachment filtering**: Include/exclude file extensions
   - Default: crawl all public channels, all supported file extensions
   - Indexing limit: 10,000,000 items per connector
7. Create and run a content crawl

### 3. Azure and Microsoft 365

- **Azure subscription** with permission to create resources
- **Microsoft 365 tenant** with a Copilot license
- [Node.js](https://nodejs.org/) v18, v20, or v22
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`)
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) (`azd`)

## Quick Start

### 1. Clone and install

```bash
cd "Copilot Connectors/ServiceNow Slack"
npm install
```

### 2. Register a Microsoft Entra app

```powershell
az login
.\scripts\entra-app-setup.ps1
```

This creates an Entra app with `ExternalConnection.ReadWrite.OwnedBy` and `ExternalItem.ReadWrite.OwnedBy` permissions, generates a client secret, and updates `.env.local`.

A **Global Admin** must grant admin consent afterward.

### 3. Configure environment variables

Copy the template if you didn't run the Entra setup script:

```bash
cp .env.local.template .env.local
```

Edit `.env.local`:

| Variable | Required | Description |
|---|---|---|
| `SN_INSTANCE_URL` | Yes | `https://YOUR-INSTANCE.service-now.com` |
| `SN_CLIENT_ID` | Yes | ServiceNow OAuth app Client ID |
| `SN_CLIENT_SECRET` | Yes | ServiceNow OAuth app Client Secret |
| `SN_USERNAME` | Dev only | ServiceNow user (password grant) |
| `SN_PASSWORD` | Dev only | ServiceNow password (password grant) |
| `SN_AUTH_FLOW` | No | `client_credentials` (default) or `password` |
| `SN_SLACK_INDEXED_TABLE` | No | Indexed source table name (default: `u_slack_content`) |
| `CONNECTOR_ID` | Yes | Unique ID for the Graph external connection |
| `CONNECTOR_NAME` | No | Display name (default: ServiceNow Slack) |
| `CONNECTOR_DESCRIPTION` | No | Description shown in M365 admin center |
| `AZURE_TENANT_ID` | Yes | Microsoft Entra tenant ID |
| `AZURE_CLIENT_ID` | Yes | Entra app registration client ID |
| `AZURE_CLIENT_SECRET` | Yes | Entra app registration client secret |

Generate `local.settings.json` for Azure Functions Core Tools:

```bash
npm run generate-local-settings
```

### 4. Test locally

```bash
npm run build
npx func start
```

Then trigger setup and crawl:

```bash
curl -X POST http://localhost:7071/api/setup
curl -X POST http://localhost:7071/api/fullCrawl
curl http://localhost:7071/api/crawlStatus
```

### 5. Deploy to Azure

```bash
azd auth login
azd up
```

Provisions Function App, Storage Account, Key Vault, and Application Insights.

### 6. Enable in Microsoft 365

1. Go to [Microsoft 365 admin center](https://admin.microsoft.com) > **Settings** > **Search & Intelligence** > **Data sources**
2. Find your connector and enable it
3. Optionally add as a knowledge source for a declarative Copilot agent

## API Endpoints

All endpoints require a function key (`?code=<key>`) when deployed to Azure.

| Endpoint | Method | Description |
|---|---|---|
| `/api/setup` | POST | Create Graph connection + register schema |
| `/api/fullCrawl` | POST | Full crawl of all indexed Slack content (returns 202, runs async) |
| `/api/incrementalCrawl` | POST | Incremental crawl. Optional: `?since=ISO_DATE` |
| `/api/status` | GET | Connection status, schema status, ServiceNow info |
| `/api/health` | GET | Health check — ServiceNow token + Graph connection |
| `/api/deleteConnection` | DELETE | Delete the Graph external connection |
| `/api/deleteItem` | DELETE | Delete a single item. Requires `?itemId=<id>` |
| `/api/crawlStatus` | GET | Poll background crawl jobs. Optional: `?jobId=<id>` |
| `/api/servicenow/connectors` | GET | List Slack external content connectors in ServiceNow |
| `/api/servicenow/crawls` | GET | List ServiceNow crawl history. Optional: `?connectorSysId=<id>` |
| `/api/servicenow/triggerCrawl` | POST | Trigger a ServiceNow content crawl |
| Timer: `scheduledIncrementalCrawl` | — | Automated incremental crawl every 15 minutes |

## Project Structure

```
ServiceNow Slack/
├── infra/
│   ├── main.bicep              # Azure infrastructure (Bicep)
│   └── main.bicepparam         # Parameter values
├── scripts/
│   ├── entra-app-setup.ps1     # Entra app registration
│   └── generate-local-settings.js  # .env.local → local.settings.json
├── src/
│   ├── auth/
│   │   └── servicenowAuth.ts   # OAuth token management
│   ├── config/
│   │   └── connectorConfig.ts  # Environment variable loading
│   ├── crawlers/
│   │   ├── fullCrawl.ts        # Full crawl orchestration
│   │   └── incrementalCrawl.ts # Incremental crawl (delta sync)
│   ├── models/
│   │   └── types.ts            # Graph + ServiceNow type definitions
│   ├── references/
│   │   ├── connectionManager.ts # Graph external connection lifecycle
│   │   └── schema.ts           # Graph schema (15 properties)
│   ├── servicenow/
│   │   ├── connectorManagement.ts # ServiceNow connector/crawl management
│   │   ├── restClient.ts       # Generic Table API client
│   │   ├── slackContentClient.ts # Indexed Slack content queries
│   │   └── transformer.ts      # ServiceNow → Graph ExternalItem mapping
│   └── index.ts                # Azure Functions entry point (11 endpoints + 1 timer)
├── azure.yaml                  # Azure Developer CLI configuration
├── .env.local.template         # Environment variable template
├── host.json                   # Azure Functions host configuration
├── package.json                # Dependencies
└── tsconfig.json               # TypeScript configuration
```

## ServiceNow OAuth Setup

### Production (client_credentials)

1. In ServiceNow, navigate to **System OAuth > Application Registry**
2. Create a new OAuth API endpoint for external clients
3. Note the **Client ID** and **Client Secret**
4. Ensure the service account has read access to the indexed source table

### Development (password grant)

1. Set `SN_AUTH_FLOW=password` in `.env.local`
2. Provide `SN_USERNAME` and `SN_PASSWORD`
3. Ensure `glide.oauth.inbound.client.credential.grant_type.enabled = true`

## Indexed Source Table

The connector reads from the ServiceNow indexed source table configured for your Slack external content connector. The default table name is `u_slack_content` but this varies by instance. To find your table name:

1. In ServiceNow, navigate to **AI Search > AI Search Index > Indexed Sources**
2. Find the indexed source created by your Slack connector
3. Note the **Table name** value
4. Set `SN_SLACK_INDEXED_TABLE` to this value

## Related Resources

- [ServiceNow: Configure Slack for external content indexing](https://www.servicenow.com/docs/r/zurich/platform-administration/ai-search/cfg-src-sys-settings-slack-ext-cont-connector.html)
- [ServiceNow: Create a Slack external content connector](https://www.servicenow.com/docs/r/zurich/platform-administration/ai-search/create-ext-cont-connector-slack.html)
- [ServiceNow: Configure crawl settings](https://www.servicenow.com/docs/r/zurich/platform-administration/ai-search/configure-crawl-settings-slack-ext-cont-connector.html)
- [Microsoft Graph External Connectors](https://learn.microsoft.com/graph/connecting-external-content-connectors-overview)
- [Microsoft 365 Copilot Extensibility](https://learn.microsoft.com/microsoft-365-copilot/extensibility/)

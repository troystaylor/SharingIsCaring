# Salesforce Copilot Connector

A Microsoft 365 Copilot connector that ingests Salesforce CRM data into Microsoft Graph, enabling Microsoft 365 Copilot to reason over your Salesforce records — Accounts, Contacts, Opportunities, Cases, Leads, Tasks, Events, Knowledge Articles, Reports, Chatter posts, and more.

## What Gets Indexed

The connector crawls **19 Salesforce data sources** and ingests them as Microsoft Graph external items:

| Category | Objects |
|----------|---------|
| **Core CRM** | Account, Contact, Opportunity, Case, Lead |
| **Activities** | Task, Event |
| **Products & Pricing** | Product2, PricebookEntry, Quote, QuoteLineItem |
| **Marketing** | Campaign, CampaignMember |
| **Knowledge** | KnowledgeArticleVersion |
| **Content** | Report, Dashboard, Chatter Posts |
| **Analytics** | CRM Analytics Datasets, CRM Analytics Dashboards |

Each item includes owner-based ACLs so Copilot respects Salesforce record ownership. Items without a resolvable owner fall back to tenant-wide access.

## Prerequisites

Before deploying, ensure you have:

- **Azure subscription** with permission to create resources (Function App, Storage Account, Key Vault)
- **Microsoft 365 tenant** with a Copilot license (or Microsoft 365 Developer tenant)
- **Salesforce org** (Developer Edition, sandbox, or production) with API access enabled
- [Node.js](https://nodejs.org/) v18, v20, or v22
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`)
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd) (`azd`)
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4 (optional, for local testing)
- [Visual Studio Code](https://code.visualstudio.com/) with [Microsoft 365 Agents Toolkit](https://marketplace.visualstudio.com/items?itemName=TeamsDevApp.ms-teams-vscode-extension) (optional, for local F5 debugging)

## Quick Start (Deploy to Your Azure)

### 1. Clone and install dependencies

```bash
git clone https://github.com/troystaylor/SharingIsCaring.git
cd SharingIsCaring/Copilot\ Connectors/Salesforce
npm install
```

### 2. Create a Salesforce Connected App

1. In Salesforce Setup, go to **App Manager** > **New Connected App**
2. **Enable OAuth Settings**
3. Set **Callback URL** to `http://localhost:8080/callback`
4. Select scopes: `api`, `refresh_token`, `offline_access`
5. Save and note the **Consumer Key** (client ID) and **Consumer Secret** (client secret)

> The Connected App may take up to 10 minutes to become active after creation.

### 3. Register a Microsoft Entra app

Run the included PowerShell script:

```powershell
az login
.\scripts\entra-app-setup.ps1
```

This creates an Entra app registration with the required Graph permissions (`ExternalConnection.ReadWrite.OwnedBy`, `ExternalItem.ReadWrite.OwnedBy`), generates a client secret, and updates `.env.local` automatically.

> A **Global Admin** must grant admin consent for the app afterward — either through the Entra admin center or via the link the script outputs.

### 4. Authenticate with Salesforce

**Option A — Refresh Token Flow (recommended for most users):**

```bash
node scripts/sf-token-refresh.js
```

This starts a local server on port 8080, opens your browser for Salesforce login, captures the OAuth callback, and writes the tokens to `.env.local`. You run this once (or when your refresh token is revoked).

**Option B — Client Credentials Flow (server-to-server, no user interaction):**

Configure a [Client Credentials Connected App](https://help.salesforce.com/s/articleView?id=sf.connected_app_client_credentials_setup.htm) in Salesforce, then set `SF_AUTH_FLOW=client_credentials` in your environment.

### 5. Configure environment variables

Copy the template and fill in your values:

```bash
cp .env.local.template .env.local
```

Then edit `.env.local` with your Salesforce and Entra credentials:

| Variable | Required | Description |
|----------|----------|-------------|
| `SF_INSTANCE_URL` | Yes | Salesforce instance name or full URL (e.g. `yourorg-dev-ed` or `https://yourorg-dev-ed.my.salesforce.com`) |
| `SF_CLIENT_ID` | Yes | Connected App Consumer Key |
| `SF_CLIENT_SECRET` | Yes | Connected App Consumer Secret |
| `SF_REFRESH_TOKEN` | Yes* | OAuth refresh token (*not needed if using `client_credentials` flow) |
| `SF_API_VERSION` | No | Salesforce API version (default: `v66.0`) |
| `SF_AUTH_FLOW` | No | `refresh_token` (default) or `client_credentials` |
| `CONNECTOR_ID` | Yes | Unique ID for the Graph external connection |
| `CONNECTOR_NAME` | Yes | Display name shown in M365 admin center |
| `CONNECTOR_DESCRIPTION` | Yes | Description shown in M365 admin center |
| `AZURE_TENANT_ID` | Yes | Your Microsoft Entra tenant ID |
| `AZURE_CLIENT_ID` | Yes | Entra app registration client ID |
| `AZURE_CLIENT_SECRET` | Yes | Entra app registration client secret |

### 6. Test locally (optional)

```bash
npm run build
```

Press **F5** in VS Code with the Agents Toolkit extension, or run:

```bash
npx func start
```

Then trigger a crawl:

```bash
curl -X POST http://localhost:7071/api/setup
curl -X POST http://localhost:7071/api/fullCrawl
```

### 7. Deploy to Azure

```bash
azd auth login
azd up
```

This provisions all Azure infrastructure (Function App, Storage Account, Key Vault, Application Insights) and deploys the connector code. You'll be prompted for:

- **Environment name** (e.g. `salesforce-connector-prod`)
- **Azure subscription**
- **Azure region** (e.g. `westus2`)

After deployment, the CLI outputs your Function App URL.

> **Important**: The deployment uses identity-based storage authentication (managed identity). The Bicep template automatically assigns required RBAC roles. If your subscription enforces `allowSharedKeyAccess: false` on storage accounts, this is already handled.

### 8. Configure Azure app settings

Add your Salesforce and Entra credentials to the deployed Function App. The Bicep template stores secrets in Key Vault automatically, but you may need to update values:

```bash
# Set Salesforce secrets in the Function App
az functionapp config appsettings set \
  --name <your-function-app-name> \
  --resource-group <your-resource-group> \
  --settings \
    SF_INSTANCE_URL=yourorg \
    SF_CLIENT_ID=your_consumer_key \
    SF_CLIENT_SECRET=your_consumer_secret \
    SF_REFRESH_TOKEN=your_refresh_token
```

### 9. Run setup and first crawl

Get your function key:

```bash
az functionapp keys list --name <your-function-app-name> --resource-group <your-resource-group> --query "functionKeys.default" -o tsv
```

Then trigger the setup and crawl:

```bash
# Create the Graph external connection and register schema
curl -X POST "https://<your-function-app>.azurewebsites.net/api/setup?code=<function-key>"

# Start the full crawl (returns 202 immediately, runs in background)
curl -X POST "https://<your-function-app>.azurewebsites.net/api/fullCrawl?code=<function-key>"

# Poll crawl progress
curl "https://<your-function-app>.azurewebsites.net/api/crawlStatus?code=<function-key>"
```

### 10. Enable in Microsoft 365

1. Go to [Microsoft 365 admin center](https://admin.microsoft.com) > **Settings** > **Search & Intelligence** > **Data sources**
2. Find your connector (named by `CONNECTOR_NAME`) and enable it
3. Optionally add it as a knowledge source for a declarative Copilot agent

> Schema registration can take up to 10 minutes after the setup call completes.

## API Endpoints

All endpoints require a function key (`?code=<key>`) when deployed to Azure.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/setup` | POST | Create Graph external connection and register schema |
| `/api/fullCrawl` | POST | Full crawl of all 19 data sources (returns 202, runs async) |
| `/api/incrementalCrawl` | POST | Incremental crawl of changed records (returns 202). Optional: `?since=ISO_DATE` |
| `/api/extendedCrawl` | POST | Crawl Reports, Chatter, Analytics only (returns 202). Optional: `?source=reports\|chatter\|analytics\|all` |
| `/api/crawlStatus` | GET | Poll background crawl jobs. Optional: `?jobId=<id>` for a specific job |
| `/api/health` | GET | Health check — tests Salesforce and Graph connectivity |
| `/api/status` | GET | Connection status — connector ID, schema status, CDC state |
| `/api/startCdc` | POST | Start real-time Change Data Capture subscription |
| `/api/stopCdc` | POST | Stop CDC subscription |
| `/api/deleteConnection` | DELETE | Delete the Graph external connection and all indexed items |
| `/api/deleteItem` | DELETE | Delete a single item. Requires `?itemId=<id>` |
| `/api/scheduledIncrementalCrawl` | Timer | Automated incremental crawl every 15 minutes |

## Project Structure

```
Salesforce/
├── infra/                       # Azure infrastructure (Bicep)
│   ├── main.bicep               # Function App, Storage, Key Vault, RBAC
│   ├── main.bicepparam          # Parameter values
│   └── abbreviations.json       # Resource naming conventions
├── proto/
│   └── pubsub_api.proto         # Salesforce Pub/Sub API gRPC definition
├── scripts/
│   ├── entra-app-setup.ps1      # Microsoft Entra app registration
│   ├── grant-user-read.js       # Show admin consent steps for User.Read.All
│   ├── grant-user-read-api.js   # Grant User.Read.All via Graph API
│   ├── grant-user-read-admin.js # Grant User.Read.All via ROPC admin token
│   ├── sf-auth-setup.js         # One-time Salesforce OAuth via SF CLI
│   └── sf-token-refresh.js      # Browser-based OAuth token refresh
├── src/
│   ├── auth/
│   │   └── salesforceAuth.ts    # OAuth token management (refresh + client_credentials)
│   ├── config/
│   │   └── connectorConfig.ts   # Environment variable loading & validation
│   ├── crawlers/
│   │   ├── fullCrawl.ts         # Full crawl orchestration (REST + Bulk API 2.0)
│   │   └── incrementalCrawl.ts  # Incremental crawl (SOQL delta + CDC real-time)
│   ├── custom/
│   │   ├── analyticsClient.ts   # CRM Analytics / Wave API
│   │   ├── bulkClient.ts        # Bulk API 2.0 (large dataset crawls)
│   │   ├── chatterClient.ts     # Chatter / Connect REST API
│   │   ├── reportsClient.ts     # Reports & Dashboards API
│   │   ├── restClient.ts        # Core REST API + SOQL client
│   │   ├── soqlBuilder.ts       # SOQL query builder (14 object types)
│   │   ├── transformer.ts       # Salesforce → Graph ExternalItem mapping
│   │   └── userMapper.ts        # Salesforce OwnerId → Entra ID for ACLs
│   ├── models/
│   │   ├── graphTypes.ts        # Microsoft Graph external connector types
│   │   └── salesforceTypes.ts   # Salesforce object interfaces (14+ types)
│   ├── references/
│   │   ├── connectionManager.ts # Graph external connection lifecycle
│   │   └── schema.ts            # Schema definition (~90 properties)
│   └── index.ts                 # Azure Functions entry point (12 endpoints)
├── azure.yaml                   # Azure Developer CLI configuration
├── .env.local.template          # Environment variable template (copy to .env.local)
├── .env.local                   # Environment variables (not committed)
├── .funcignore                  # Deployment package exclusions
├── host.json                    # Azure Functions host configuration
├── package.json
└── tsconfig.json
```

## Authentication

### Salesforce Authentication

The connector supports two OAuth 2.0 flows, controlled by the `SF_AUTH_FLOW` environment variable:

| Flow | When to Use |
|------|-------------|
| `refresh_token` (default) | Most deployments. Run the token setup script once, then the connector auto-refreshes. |
| `client_credentials` | Server-to-server integrations with no interactive user. Requires a Salesforce Connected App configured for Client Credentials. |

**References:**
- [OAuth 2.0 Web Server Flow](https://help.salesforce.com/s/articleView?id=xcloud.remoteaccess_oauth_web_server_flow.htm&type=5)
- [OAuth 2.0 Refresh Token Flow](https://help.salesforce.com/s/articleView?id=xcloud.remoteaccess_oauth_refresh_token_flow.htm&type=5)
- [Client Credentials Flow](https://help.salesforce.com/s/articleView?id=sf.connected_app_client_credentials_setup.htm)

### Microsoft Graph Authentication

The connector authenticates to Microsoft Graph using a **client credentials** grant (Entra app registration with application permissions). The Entra app needs:

- `ExternalConnection.ReadWrite.OwnedBy` — create/manage the Graph external connection
- `ExternalItem.ReadWrite.OwnedBy` — create/update/delete indexed items

When deployed to Azure, secrets are stored in Key Vault and referenced by the Function App.

### Security Notes

- Never pass secrets in URL query strings — use POST body or Authorization header
- Store `refresh_token` securely; it is a long-lived credential
- For production, all secrets are stored in Azure Key Vault (handled by the Bicep template)
- `.env.local` is gitignored and excluded from deployment packages

## Azure Infrastructure

The `infra/main.bicep` template provisions:

| Resource | Purpose |
|----------|---------|
| **Function App** (B1 Linux) | Hosts the connector (Node.js 22) |
| **App Service Plan** (Basic B1) | Dedicated compute (~$13/month) |
| **Storage Account** | Azure Functions runtime state (identity-based auth, no shared keys) |
| **Key Vault** | Stores Salesforce and Entra secrets (RBAC authorization) |
| **Application Insights** | Monitoring, logs, crawl telemetry |
| **Log Analytics Workspace** | Backing store for Application Insights |

The Function App uses a **system-assigned managed identity** with RBAC roles for storage access (Blob Data Owner, Queue Data Contributor, Table Data Contributor, Account Contributor) and Key Vault secrets access.

## Monitoring

After deployment, monitor crawl activity in **Application Insights**:

```kusto
// Recent crawl activity
traces
| where message contains "crawl" or message contains "Crawl"
| project timestamp, message
| order by timestamp desc
| take 50

// Health status
traces
| where message contains "Healthy" or message contains "unhealthy"
| project timestamp, message
| order by timestamp desc
```

Or use the built-in endpoints:

```bash
# Health check (Salesforce + Graph connectivity)
curl "https://<your-function-app>.azurewebsites.net/api/health?code=<key>"

# Connection status
curl "https://<your-function-app>.azurewebsites.net/api/status?code=<key>"

# Crawl job history
curl "https://<your-function-app>.azurewebsites.net/api/crawlStatus?code=<key>"
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `AuthorizationPermissionMismatch` on storage | Ensure the Function App's managed identity has RBAC roles on the storage account. Run `az role assignment list --assignee <principalId> --scope <storageId>` to verify. |
| Health endpoint returns 404 | Functions aren't loading. Check Application Insights for module errors. Verify `.funcignore` isn't excluding `node_modules/**/src/` directories. |
| `504 Gateway Timeout` on crawl | Crawl endpoints return 202 and run async. If you see 504, redeploy — you may be on an older synchronous version. |
| Salesforce 401 Unauthorized | Refresh token may be expired/revoked. Run `node scripts/sf-token-refresh.js` to get a new one. |
| Schema registration pending | After calling `/api/setup`, schema registration can take up to 10 minutes. Check status with `/api/status`. |
| No items appearing in M365 Search | Ensure admin has enabled the connector in M365 admin center > Search & Intelligence > Data sources. |
| Functions return 401 | Include the function key as `?code=<key>` query parameter. Get the key with `az functionapp keys list`. |

## Tear Down

To remove all Azure resources:

```bash
azd down --force --purge
```

To delete only the Graph external connection (keeps Azure resources):

```bash
curl -X DELETE "https://<your-function-app>.azurewebsites.net/api/deleteConnection?code=<key>"
```

## API Analysis

See [API-PRIORITIZATION.md](./API-PRIORITIZATION.md) for the full Salesforce API analysis and implementation phasing.

## License

MIT

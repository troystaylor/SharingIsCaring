# Azure Databricks

Power Mission Control MCP connector for Azure Databricks. Provides progressive API discovery with scan, launch, and sequence tools for managing clusters, jobs, SQL warehouses, notebooks, Unity Catalog, and more via the Databricks REST API.

## Prerequisites

- An Azure Databricks workspace
- Account admin access to the Databricks account console at `https://accounts.azuredatabricks.net`
- A Power Platform environment with Copilot Studio or Power Automate

## Security Configuration

This connector uses OAuth 2.0 Authorization Code flow with Databricks native OIDC endpoints (not Azure AD). You must register a custom OAuth application in Databricks as a **confidential client** since Power Platform connectors do not support PKCE.

### Step 1: Create the Custom Connector

1. Import the connector into your Power Platform environment using the PAC CLI or the maker portal.
2. After creation, go to the connector's **Security** tab.
3. Copy the **Redirect URL** — this is auto-generated and unique to your connector instance (`GlobalPerConnector` mode).

### Step 2: Register an OAuth App in Databricks

1. Sign in to the [Databricks Account Console](https://accounts.azuredatabricks.net).
2. Navigate to **Settings** > **App connections**.
3. Click **Add app connection**.
4. Configure the application:
   - **Name**: Power Platform Connector (or any descriptive name)
   - **Redirect URLs**: Paste the redirect URL copied from Step 1
   - **Access scopes**: Select `all-apis` and `offline_access`
   - **App type**: Select **Confidential** (this generates a client secret)
5. Click **Add**.
6. Copy the **Client ID** and **Client Secret**.

### Step 3: Get Your Account ID

1. In the Databricks Account Console, click your username in the top bar.
2. Select **Account settings**.
3. Copy your **Account ID** (a UUID).

### Step 4: Get Your Workspace URL

1. In the Azure portal, navigate to your Azure Databricks workspace resource.
2. Copy the **Workspace URL** from the overview page (e.g., `adb-1234567890123456.7.azuredatabricks.net`).

### Step 5: Update Connector Files

Before deploying, update the following placeholders in the connector files:

**apiDefinition.swagger.json:**

| Placeholder | Replace With | Example |
|---|---|---|
| `adb-YOUR_WORKSPACE_ID.azuredatabricks.net` | Your workspace URL | `adb-1234567890123456.7.azuredatabricks.net` |
| `YOUR_ACCOUNT_ID` (in securityDefinitions) | Your Databricks account ID | `a1b2c3d4-e5f6-7890-abcd-ef1234567890` |

**apiProperties.json:**

| Placeholder | Replace With | Example |
|---|---|---|
| `YOUR_CUSTOM_APP_CLIENT_ID` | Client ID from Step 2 | `a1b2c3d4-e5f6-7890-abcd-ef1234567890` |
| `YOUR_ACCOUNT_ID` (in URL templates) | Your Databricks account ID | `a1b2c3d4-e5f6-7890-abcd-ef1234567890` |

**script.csx:**

| Placeholder | Replace With | Example |
|---|---|---|
| `adb-YOUR_WORKSPACE_ID.azuredatabricks.net` (in BaseApiUrl) | Your workspace URL | `adb-1234567890123456.7.azuredatabricks.net` |

The **Client Secret** is entered in the connector's Security tab in the Power Platform maker portal — it is not stored in the connector files.

### Step 6: Deploy and Test

1. Deploy the updated connector using PAC CLI:
   ```
   paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx
   ```
2. In the maker portal, edit the connector and go to the **Security** tab.
3. Enter the **Client Secret** from Step 2.
4. Save and test by creating a new connection — you should be redirected to the Databricks login page.

## OAuth Flow Summary

| Component | Value |
|---|---|
| Identity provider | `oauth2generic` (Databricks native OIDC) |
| Authorization endpoint | `https://accounts.azuredatabricks.net/oidc/accounts/{account-id}/v1/authorize` |
| Token endpoint | `https://accounts.azuredatabricks.net/oidc/accounts/{account-id}/v1/token` |
| Grant type | Authorization Code (no PKCE) |
| Client type | Confidential (client_id + client_secret) |
| Scopes | `all-apis offline_access` |
| Token lifetime | 1 hour (auto-refreshed via refresh token) |

## How It Works

This connector uses the Power Mission Control pattern (MCP Template v3) to expose the full Databricks REST API through three tools:

- **`scan_databricks`** — Search for available API operations by intent (e.g., "list clusters", "create job")
- **`launch_databricks`** — Execute any Databricks REST API endpoint with auth forwarding
- **`sequence_databricks`** — Execute multiple API operations in a single call

The capability index is embedded in `script.csx` and covers clusters, jobs, SQL warehouses, notebooks, DBFS, Unity Catalog, secrets, repos, and more.

## Supported Operations

The connector proxies requests to the Databricks REST API (`/api/2.0/` and `/api/2.1/` endpoints). All workspace-level operations are supported, including:

- **Compute**: Clusters, cluster policies, instance pools
- **Jobs**: Create, list, run, cancel jobs and workflows
- **SQL**: SQL warehouses, query execution, query history
- **Workspace**: Notebooks, repos, workspace objects
- **Data**: DBFS, Unity Catalog (catalogs, schemas, tables, volumes)
- **Security**: Secrets, token management, permissions
- **Machine Learning**: MLflow experiments, model registry, model serving

## Important Notes

- The workspace URL is hardcoded in `apiDefinition.swagger.json` because `oauth2generic` does not allow additional connection parameters beyond the OAuth token.
- Each connector instance is tied to a single Databricks workspace. To connect to multiple workspaces, deploy separate connector instances.
- Access tokens are valid for 1 hour and automatically refreshed using the `offline_access` scope.
- API permissions are determined by the authenticating user's Databricks workspace permissions.

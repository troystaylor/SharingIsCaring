# Azure Databricks

Power Mission Control MCP connector for Azure Databricks. Provides progressive API discovery with scan, launch, and sequence tools for managing clusters, jobs, SQL warehouses, notebooks, Unity Catalog, and more via the Databricks REST API.

## Prerequisites

- An Azure Databricks workspace
- A Microsoft Entra ID (Azure AD) tenant with permissions to register applications
- A Power Platform environment with Copilot Studio or Power Automate

## Security Configuration

This connector uses OAuth 2.0 Authorization Code flow with Microsoft Entra ID to authenticate users against Azure Databricks. The Entra ID token is scoped to the `AzureDatabricks` resource (`2ff814a6-3304-4ab8-85cb-cd0e6f879c1d`) with `user_impersonation` delegation.

### Step 1: Register an App in Microsoft Entra ID

1. Sign in to the [Azure Portal](https://portal.azure.com).
2. Navigate to **Microsoft Entra ID** > **App registrations** > **New registration**.
3. Configure the application:
   - **Name**: `Azure Databricks Connector` (or any descriptive name)
   - **Supported account types**: Accounts in this organizational directory only (Single tenant)
   - **Redirect URI**: Select **Web** and enter `https://global.consent.azure-apim.net/redirect` (you'll update this after deploying)
4. Click **Register**.
5. On the Overview page, copy the **Application (client) ID** and **Directory (tenant) ID**.

### Step 2: Create a Client Secret

1. In your app registration, go to **Certificates & secrets** > **New client secret**.
2. Add a description and select an expiry period.
3. Click **Add** and copy the **Value** (not the Secret ID).

### Step 3: Add API Permissions

1. Go to **API permissions** > **Add a permission**.
2. Select the **APIs my organization uses** tab.
3. Search for `AzureDatabricks` (or `2ff814a6-3304-4ab8-85cb-cd0e6f879c1d`).
4. Select **Delegated permissions** > check **user_impersonation**.
5. Click **Add permissions**.
6. Click **Add a permission** again > select **Microsoft Graph** > **Delegated permissions**.
7. Search for and check **offline_access** and **User.Read**.
8. Click **Add permissions**.
9. Click **Grant admin consent for [your tenant]**.

Your final API permissions should be:

| API | Permission | Type |
|---|---|---|
| AzureDatabricks | user_impersonation | Delegated |
| Microsoft Graph | offline_access | Delegated |
| Microsoft Graph | User.Read | Delegated |

### Step 4: Ensure User Has Databricks Workspace Access

The user signing in must be added to the Azure Databricks workspace:

1. Open your Databricks workspace (e.g., `https://adb-XXXXX.azuredatabricks.net`).
2. Go to **Admin Settings** > **Users**.
3. Add the user's email if not already present.

### Step 5: Get Your Workspace URL

1. In the Azure portal, navigate to your Azure Databricks workspace resource.
2. Copy the **Workspace URL** from the overview page (e.g., `adb-1234567890123456.7.azuredatabricks.net`).

### Step 6: Update Connector Files

Before deploying, update the following placeholders in the connector files:

**apiDefinition.swagger.json:**

| Placeholder | Replace With | Example |
|---|---|---|
| `adb-YOUR_WORKSPACE_ID.azuredatabricks.net` | Your workspace URL | `adb-1234567890123456.7.azuredatabricks.net` |

**apiProperties.json:**

| Field | Replace With | Example |
|---|---|---|
| `clientId` | Application (client) ID from Step 1 | `21f7780d-c98e-45f3-bf32-76cd646bbdec` |
| Tenant ID in auth URLs | Directory (tenant) ID from Step 1 | `fe69ead4-dc67-4951-85c2-a5e6505fec7d` |

**script.csx:**

| Placeholder | Replace With | Example |
|---|---|---|
| `adb-YOUR_WORKSPACE_ID.azuredatabricks.net` (in BaseApiUrl) | Your workspace URL | `adb-1234567890123456.7.azuredatabricks.net` |

### Step 7: Deploy the Connector

1. Deploy the connector using PAC CLI:
   ```
   pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx
   ```
2. In the maker portal, edit the connector and go to the **Security** tab.
3. Enter the **Client Secret** from Step 2.
4. Copy the **Redirect URL** shown on the Security tab.
5. Go back to your Entra app registration > **Authentication** > **Web** > add the redirect URL from the previous step.
6. Save and test by creating a new connection.

## OAuth Flow Summary

| Component | Value |
|---|---|
| Identity provider | Microsoft Entra ID (OAuth 2.0) |
| Authorization endpoint | `https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/authorize` |
| Token endpoint | `https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token` |
| Grant type | Authorization Code |
| Resource ID | `2ff814a6-3304-4ab8-85cb-cd0e6f879c1d` (AzureDatabricks) |
| Scopes | `2ff814a6-3304-4ab8-85cb-cd0e6f879c1d/user_impersonation offline_access` |
| Token lifetime | ~1 hour (auto-refreshed via refresh token) |

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

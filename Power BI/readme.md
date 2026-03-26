# Power BI

## Overview

Power Platform MCP connector for the Power BI REST API using Power Mission Control. Provides progressive API discovery with `scan_powerbi`, `launch_powerbi`, and `sequence_powerbi` tools plus 16 typed operations for workspaces, reports, dashboards, datasets, apps, DAX queries, and report export.

## Tools

### Orchestration Tools (Scan → Launch → Sequence)

| Tool | Description |
|------|-------------|
| `scan_powerbi` | Discover available Power BI operations by intent or domain |
| `launch_powerbi` | Execute any Power BI API endpoint |
| `sequence_powerbi` | Execute multiple Power BI API operations in one call |

The capability index covers 35 operations across 6 domains: workspaces, reports, dashboards, datasets, apps, and export.

### Capability Index Domains

| Domain | Operations | Examples |
|--------|-----------|----------|
| **workspaces** | 3 | List workspaces, get workspace, list workspace users |
| **reports** | 7 | List/get reports, get report pages, clone report |
| **dashboards** | 6 | List/get dashboards, list dashboard tiles |
| **datasets** | 11 | List/get datasets, refresh, refresh history, datasources, execute DAX queries |
| **apps** | 6 | List/get installed apps, app reports, app dashboards |
| **export** | 2 | Trigger report export, poll export status |

### Export Workflow

The export operations use orchestration hints in their descriptions so Copilot Studio's planner chains them automatically:

1. `scan_powerbi` → find `export_report`
2. `launch_powerbi` → trigger `POST /reports/{reportId}/ExportTo` with format (PDF, PPTX, PNG)
3. `scan_powerbi` → find `get_export_status`
4. `launch_powerbi` → poll `GET /reports/{reportId}/exports/{exportId}` until `status: Succeeded`
5. Share the `resourceLocation` download URL with the user

## REST Operations (Power Automate / Power Apps)

| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| List Workspaces | `ListWorkspaces` | List all workspaces the user has access to |
| List Reports | `ListReports` | List reports from My workspace |
| List Workspace Reports | `ListWorkspaceReports` | List reports from a specific workspace |
| Get Report | `GetReport` | Get details of a specific report |
| Get Report Pages | `GetReportPages` | List pages within a report |
| List Dashboards | `ListDashboards` | List dashboards from My workspace |
| List Workspace Dashboards | `ListWorkspaceDashboards` | List dashboards from a specific workspace |
| List Dashboard Tiles | `ListDashboardTiles` | List tiles within a dashboard |
| List Datasets | `ListDatasets` | List datasets from My workspace |
| List Workspace Datasets | `ListWorkspaceDatasets` | List datasets from a specific workspace |
| Execute DAX Query | `ExecuteQueries` | Run DAX queries against a dataset |
| Get Refresh History | `GetRefreshHistory` | Get refresh history for a dataset |
| Refresh Dataset | `RefreshDataset` | Trigger a dataset refresh |
| List Apps | `ListApps` | List installed Power BI apps |
| Export Report | `ExportReport` | Trigger a report export to PDF/PPTX/PNG |
| Get Export Status | `GetExportStatus` | Check the status of an export job |

## Prerequisites

1. An Azure AD app registration with delegated permissions for Power BI
2. A Power Platform environment with a custom connector license
3. The **Dataset Execute Queries REST API** tenant setting must be enabled in the Power BI admin portal (under Integration settings) for DAX query execution

## Setup

### 1. Register Azure AD Application

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Click **New registration**
   - Name: `Power BI Connector`
   - Supported account types: **Accounts in any organizational directory (Multitenant)**
   - Redirect URI: **Web** → `https://global.consent.azure-apim.net/redirect`
3. After creation, note the **Application (client) ID**
4. Go to **Certificates & secrets** → **New client secret** → Note the secret value
5. Go to **API permissions** → **Add a permission** → **Power BI Service** → **Delegated permissions**:
   - `Dashboard.Read.All`
   - `Dataset.Read.All`
   - `Dataset.ReadWrite.All`
   - `Report.Read.All`
   - `Workspace.Read.All`
   - `App.Read.All`
6. Click **Grant admin consent** (or have a tenant admin do this)

### 2. Update apiProperties.json

Replace `[[REPLACE_WITH_APP_ID]]` with your Application (client) ID.

### 3. Create Custom Connector

1. Go to [Power Platform Maker Portal](https://make.powerapps.com)
2. Navigate to **Custom connectors** → **+ New custom connector** → **Import an OpenAPI file**
3. Upload `apiDefinition.swagger.json`
4. On the **Security** tab:
   - Authentication type: **OAuth 2.0**
   - Identity Provider: **Azure Active Directory**
   - Client ID: Your Application ID
   - Client Secret: Your secret
   - Resource URL: `https://analysis.windows.net/powerbi/api`
5. On the **Code** tab:
   - Enable **Code**
   - Upload `script.csx`
6. Click **Create connector**

### 4. Test Connection

1. Click **Test** tab → **+ New connection**
2. Sign in with your Microsoft account
3. Test the `InvokeMCP` operation with:
```json
{
  "jsonrpc": "2.0",
  "method": "initialize",
  "id": "1",
  "params": {
    "protocolVersion": "2025-11-25",
    "clientInfo": { "name": "test" }
  }
}
```

### 5. Add to Copilot Studio

1. In Copilot Studio, open your agent
2. Add this connector as an action — Copilot Studio detects the MCP endpoint via `x-ms-agentic-protocol`
3. Test with prompts like "What workspaces do I have?" or "List my reports"

## Required Permissions

| Permission | Type | Purpose |
|------------|------|---------|
| `Dashboard.Read.All` | Delegated | Read dashboards and tiles |
| `Dataset.Read.All` | Delegated | Read datasets, execute DAX queries |
| `Dataset.ReadWrite.All` | Delegated | Trigger dataset refreshes |
| `Report.Read.All` | Delegated | Read reports, export reports |
| `Workspace.Read.All` | Delegated | Read workspaces and workspace users |
| `App.Read.All` | Delegated | Read installed apps |

## Required Tenant Settings

| Setting | Location | Required For |
|---------|----------|-------------|
| Dataset Execute Queries REST API | Admin portal → Integration settings | DAX query execution |

## Authentication

| Setting | Value |
|---------|-------|
| Identity Provider | Azure Active Directory |
| Authorization URL | `https://login.microsoftonline.com/common/oauth2/v2.0/authorize` |
| Token URL | `https://login.microsoftonline.com/common/oauth2/v2.0/token` |
| Resource URL | `https://analysis.windows.net/powerbi/api` |
| On-behalf-of login | Enabled |

## Usage Examples

### List workspaces

Use the `ListWorkspaces` REST operation — no parameters required.

### Run a DAX query

Use the `ExecuteQueries` REST operation:
- **Dataset ID**: `cfafbeb1-8037-4d0c-896e-a46fb27ff229`
- **Body**:
```json
{
  "queries": [
    { "query": "EVALUATE VALUES(MyTable)" }
  ],
  "serializerSettings": {
    "includeNulls": true
  }
}
```

### Discover and execute via MCP

1. Call `scan_powerbi` with **query**: `"refresh a dataset"`
2. Review the results to find `refresh_dataset` or `refresh_workspace_dataset`
3. Call `launch_powerbi` with **endpoint**: `/datasets/{datasetId}/refreshes`, **method**: `POST`, **body**: `{ "notifyOption": "MailOnFailure" }`

### Export a report to PDF

1. Call `scan_powerbi` with **query**: `"export report"`
2. Call `launch_powerbi` with **endpoint**: `/reports/{reportId}/ExportTo`, **method**: `POST`, **body**: `{ "format": "PDF" }`
3. Note the `id` from the response
4. Call `launch_powerbi` with **endpoint**: `/reports/{reportId}/exports/{exportId}`, **method**: `GET`
5. Repeat until `status` is `Succeeded`, then share the `resourceLocation` URL

### Batch operations

Use `sequence_powerbi` to list reports and datasets in one call:
```json
{
  "requests": [
    { "id": "1", "endpoint": "/reports", "method": "GET" },
    { "id": "2", "endpoint": "/datasets", "method": "GET" }
  ]
}
```

## Power BI API Notes

- All Power BI REST API endpoints use base URL `https://api.powerbi.com/v1.0/myorg`
- Workspaces are called "groups" in the API (e.g., `/groups/{groupId}`)
- DAX queries are limited to 100,000 rows or 1,000,000 values per query, whichever is hit first
- Power BI throttles at 120 query requests per minute per user (HTTP 429 with `Retry-After` header, handled automatically)
- Report export is asynchronous — trigger with `ExportTo`, poll status, then download via `resourceLocation` URL
- Export supports PDF, PPTX, and PNG for Power BI reports; paginated reports additionally support XLSX, DOCX, CSV, XML, and MHTML

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI 2.0 definition with MCP endpoint and 16 REST operations |
| `apiProperties.json` | OAuth config with Power BI scopes and script operation bindings |
| `script.csx` | Mission Control v3 framework with 35-entry capability index across 6 domains |
| `readme.md` | This file |

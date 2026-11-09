# Azure Databricks Custom

A dual-purpose Azure Databricks connector for Power Platform with **nothing to type when you create a connection**. People sign in with Microsoft Entra ID and that's it. There is no Server Hostname and no HTTP Path.

- **Power Automate, Power Apps and Logic Apps:** 113 standard operations across SQL, SQL warehouses, Unity Catalog, jobs, Genie, saved queries, alerts and dashboards.
- **Copilot Studio:** an MCP endpoint (`InvokeMCP`) with 15 tools.
- **Microsoft 365 Copilot:** a matching [Copilot plugin](<../Copilot Plugins/Azure Databricks Custom/>).

## Why this exists

The Microsoft [Azure Databricks connector](https://learn.microsoft.com/connectors/databricks/) asks every user for a **Server Hostname** and an **HTTP Path** when they create a connection. Those values describe your environment, not the user, and most users don't know where to find them. This connector moves them out of the connection:

| Value | Where it comes from here |
|-------|--------------------------|
| Server Hostname | Set once per environment in a Power Platform environment variable, `new_DatabricksWorkspaceHost`. A policy on the connector builds the workspace URL from it. |
| HTTP Path | Set once per environment in a second environment variable, `new_DatabricksSqlHttpPath`. It holds the same SQL warehouse HTTP path you would have typed into the connection, and a policy passes it to the connector. Set it to `auto` and the connector picks a warehouse for you. |
| Sign-in | OAuth 2.0 with Microsoft Entra ID, as the signed-in user. |

OAuth connectors can't have extra connection parameters, so these values can't be asked for at connection time. Instead, they live in **environment variables** that belong to the environment, and two **policies** on the connector pass them along on every call. The connection itself stays OAuth only.

## How it works

```
 Environment variables (one value per environment)
   new_DatabricksWorkspaceHost   adb-123.7.azuredatabricks.net
   new_DatabricksSqlHttpPath     /sql/1.0/warehouses/abc123
                |
                v
 Policies on the connector (apiProperties.json)
   1. Set Host URL      sends every call to https://<workspace host>
   2. Set HTTP Header   adds the header x-databricks-http-path: <HTTP path>
                |
                v
 Connector script (script.csx)
   - reads the warehouse ID from the end of the HTTP path
   - fills in warehouse_id when the caller didn't pick a warehouse
   - removes the header before the request goes on to Databricks
                |
                v
 Azure Databricks, called with the signed-in user's own token
```

| Piece | What it does | Where it lives |
|-------|--------------|----------------|
| Environment variables | Hold the host and the HTTP path for this environment. Change them per environment (development, test, production) when you import the solution. | Your solution |
| **Set Host URL** policy | Replaces the connector's host with `https://` plus the host variable, for every operation. | `policyTemplateInstances` in `apiProperties.json` |
| **Set HTTP Header** policy | Copies the HTTP path variable into a request header, for the two operations that run the script (`ExecuteStatement` and `InvokeMCP`). | `policyTemplateInstances` in `apiProperties.json` |
| Script | Turns the header into a SQL warehouse ID and applies it when the caller leaves the warehouse blank. | `script.csx` |

Because the values come from policies and the script, not from the connection, the connection only needs the Microsoft Entra sign-in.

Environment variables in policies are a preview feature, and they work only when the connector is in a solution.

## How the SQL warehouse is chosen

A SQL warehouse is what the HTTP path points to, and it usually differs between development, test and production. The connector calls the Databricks SQL Statement Execution API, which takes a warehouse ID, so it reads the ID from the end of the HTTP path. Calls that don't name a warehouse (`ExecuteStatement` and the `execute_sql` MCP tool) use the first match from this list:

1. The warehouse the caller picked, in the **SQL warehouse** drop-down or the `warehouse_id` input.
2. The environment's HTTP path, from the `new_DatabricksSqlHttpPath` environment variable. A bare warehouse ID also works.
3. The warehouse ID in `DEFAULT_WAREHOUSE_ID` at the top of `script.csx`, if you set one.
4. A **running** warehouse you can use.
5. A **starting** warehouse.
6. A serverless warehouse, which starts in seconds.
7. The first warehouse in the list.

Steps 4 to 7 apply when the variable is `auto`, and run with the caller's own token, so people only get warehouses they have access to. In flows and apps, the **SQL warehouse** box is a drop-down of warehouse names, so people who want a specific one pick it by name.

## Prerequisites

- An Azure Databricks workspace, and its URL (for example `adb-1234567890123456.7.azuredatabricks.net`).
- Access to at least one SQL warehouse for the people who will use the connector.
- A Microsoft Entra tenant where you can register an application. Power Platform and Azure Databricks must be in the same tenant.
- A Power Platform environment with Dataverse, where you can create solutions and custom connectors, and the [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`).

## Setup

### 1. Register an app in Microsoft Entra ID

1. In the [Azure portal](https://portal.azure.com), go to **Microsoft Entra ID** > **App registrations** > **New registration**.
2. Name it, for example `Azure Databricks Custom`. Choose **Accounts in this organizational directory only**. Under **Redirect URI**, choose **Web** and enter `https://global.consent.azure-apim.net/redirect`.
3. Copy the **Application (client) ID** and **Directory (tenant) ID**.
4. Go to **Certificates & secrets** > **New client secret**, and copy the secret **Value**.
5. Go to **API permissions** > **Add a permission** > **APIs my organization uses**, search for `AzureDatabricks`, and add the delegated permission **user_impersonation**.
6. Add the Microsoft Graph delegated permission **offline_access**, then choose **Grant admin consent**.

### 2. Make sure people can use the workspace

Everyone who signs in must be a user in the Databricks workspace, with access to the SQL warehouses, catalogs and jobs they need. Add them in your workspace under **Settings** > **Identity and access**.

### 3. Create a solution and the environment variables

1. In [make.powerapps.com](https://make.powerapps.com), create a solution, for example `AzureDatabricksCustom`, and note its **unique name** and the **publisher prefix** (the default is `new`).
2. In the solution, choose **New** > **More** > **Environment variable**, and create two **Text** variables:

| Display name | Name | Current value |
|--------------|------|---------------|
| Databricks workspace host | `new_DatabricksWorkspaceHost` | Your workspace host without `https://`, for example `adb-1234567890123456.7.azuredatabricks.net` |
| Databricks SQL HTTP path | `new_DatabricksSqlHttpPath` | The HTTP path of the SQL warehouse for this environment, for example `/sql/1.0/warehouses/1234567890abcdef`, or `auto` |

Find a warehouse's HTTP path in Databricks under **SQL Warehouses** > your warehouse > **Connection details**. Give each variable a value, because the policies read both on every call. When you move the solution to test and production, set each environment's own values on import.

If your prefix isn't `new`, change both variable names in the policies in `apiProperties.json` to match.

### 4. Fill in your values

| File | Replace | With |
|------|---------|------|
| `apiDefinition.swagger.json` | `YOUR_TENANT_ID` in the two URLs under `securityDefinitions` | Your Directory (tenant) ID |
| `apiProperties.json` | `[YOUR_CLIENT_ID]` | Your Application (client) ID |
| `apiProperties.json` | `YOUR_TENANT_ID` in the three URLs | Your Directory (tenant) ID |
| `apiProperties.json` | `new_DatabricksWorkspaceHost` and `new_DatabricksSqlHttpPath`, only if your prefix isn't `new` | Your environment variables' names |
| `script.csx` (optional) | `DEFAULT_WAREHOUSE_ID` | A SQL warehouse ID to fall back to when the HTTP path variable is `auto` |

Leave `adb-YOUR_WORKSPACE_ID.azuredatabricks.net` in `host` as it is. The policy replaces it on every call. Also leave `[YOUR_REDIRECT_URL]`, because the redirect URL is generated when the connector is created.

### 5. Deploy the connector

```powershell
pac auth create --environment <YOUR_ENVIRONMENT_ID>
pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx --solution-unique-name <YOUR_SOLUTION_UNIQUE_NAME>
```

If you later change an environment variable's value, save the connector again so it picks up the new value.

### 6. Add the secret and redirect URL

1. In [make.powerapps.com](https://make.powerapps.com), open your solution, edit the connector, and go to the **Security** tab.
2. Paste your client secret and save.
3. Copy the **Redirect URL** that appears.
4. In your Entra app registration, go to **Authentication** and add that URL under **Web** redirect URIs.

### 7. Create a connection

Create a connection to the connector and sign in. The sign-in window is the only step. Check that **Test** > `ListWarehouses` returns your warehouses.

### Without a solution

Environment variables in policies need a solution. If you can't use one, delete the `policyTemplateInstances` block from `apiProperties.json`, set `host` in `apiDefinition.swagger.json` to your workspace URL, set `DEFAULT_WAREHOUSE_ID` in `script.csx` (or leave it empty to pick a warehouse automatically), skip step 3 and the `--solution-unique-name` option, and deploy a copy of the connector for each workspace. Environments without Dataverse can't use `pac connector create`. Use `paconn create` there.

## Operations

The connector has 113 operations. Each one maps to a documented endpoint in the [Databricks REST API reference](https://docs.databricks.com/api), with the parameters and request bodies that the reference lists. Jobs use the current `2.2` API.

| Area | Operations |
|------|------------|
| SQL statements (4) | `CancelStatement`, `ExecuteStatement`, `GetStatement`, `GetStatementResultChunk` |
| SQL warehouses (9) | `CreateWarehouse`, `DeleteWarehouse`, `EditWarehouse`, `GetWarehouse`, `GetWorkspaceWarehouseConfig`, `ListWarehouses`, `SetWorkspaceWarehouseConfig`, `StartWarehouse`, `StopWarehouse` |
| Query history (1) | `ListQueryHistory` |
| Saved queries (5) | `CreateQuery`, `GetQuery`, `ListQueries`, `TrashQuery`, `UpdateQuery` |
| Alerts (5) | `CreateAlert`, `GetAlert`, `ListAlerts`, `TrashAlert`, `UpdateAlert` |
| Dashboards (20) | `CreateDashboard`, `CreateDashboardSchedule`, `CreateDashboardSubscription`, `DeleteDashboardSchedule`, `DeleteDashboardSubscription`, `GetDashboard`, `GetDashboardSchedule`, `GetDashboardSubscription`, `GetPublishedDashboard`, `GetPublishedDashboardTokenInfo`, `ListDashboardSchedules`, `ListDashboardSubscriptions`, `ListDashboards`, `MigrateDashboard`, `PublishDashboard`, `RevertDashboard`, `TrashDashboard`, `UnpublishDashboard`, `UpdateDashboard`, `UpdateDashboardSchedule` |
| Genie (22) | `CancelGenieAgentResponse`, `CreateGenieMessage`, `CreateGenieMessageComment`, `CreateGenieSpace`, `DeleteGenieConversation`, `DeleteGenieMessage`, `DownloadGenieVisualization`, `ExecuteGenieAttachmentQuery`, `GetGenieMessage`, `GetGenieQueryResult`, `GetGenieQueryResultDownload`, `GetGenieSpace`, `ListGenieConversationComments`, `ListGenieConversations`, `ListGenieMessageComments`, `ListGenieMessages`, `ListGenieSpaces`, `SendGenieMessageFeedback`, `StartGenieConversation`, `StartGenieQueryResultDownload`, `TrashGenieSpace`, `UpdateGenieSpace` |
| Jobs (19) | `CancelAllJobRuns`, `CancelJobRun`, `CreateJob`, `DeleteJob`, `DeleteJobRun`, `EnforceJobPolicyCompliance`, `ExportJobRun`, `GetJob`, `GetJobPolicyCompliance`, `GetJobRun`, `GetJobRunOutput`, `ListJobComplianceForPolicy`, `ListJobRuns`, `ListJobs`, `RepairJobRun`, `ResetJob`, `RunJobNow`, `SubmitJobRun`, `UpdateJob` |
| Unity Catalog (28) | `CreateCatalog`, `CreateFunction`, `CreateSchema`, `CreateTable`, `CreateTableConstraint`, `CreateVolume`, `DeleteCatalog`, `DeleteFunction`, `DeleteSchema`, `DeleteTable`, `DeleteTableConstraint`, `DeleteVolume`, `GetCatalog`, `GetFunction`, `GetSchema`, `GetTable`, `GetVolume`, `ListCatalogs`, `ListFunctions`, `ListSchemas`, `ListTableSummaries`, `ListTables`, `ListVolumes`, `TableExists`, `UpdateCatalog`, `UpdateFunction`, `UpdateSchema`, `UpdateVolume` |
| Copilot Studio (1) | `InvokeMCP` |

Catalog, schema, warehouse, job and Genie space inputs are drop-downs that fill in from your workspace.

Every call runs as the signed-in user, so Databricks permissions decide what each person can do. Many operations create, change or delete things (jobs, warehouses, catalogs, queries, dashboards). Give people only the Databricks permissions they need.

Generally available and Public Preview endpoints are included. Beta endpoints and fields aren't, because they can change without notice. Preview endpoints work only if the feature is turned on in your workspace.

### Running SQL in a flow

`ExecuteStatement` waits up to the **Wait timeout** (30 seconds by default, 50 at most). If the statement is still running, the response has a `status.state` of `PENDING` or `RUNNING`. Loop on `GetStatement` with the `statement_id` until the state is `SUCCEEDED` or `FAILED`.

### Regenerating the definition

`apiDefinition.swagger.json` is generated from the Databricks API reference, so edit the generator, not the JSON. To refresh it:

1. Run `tools/download-api-reference.ps1 -Output <folder>` to download the reference pages.
2. Run `python tools/generate-swagger.py <folder> apiDefinition.swagger.json apiDefinition.swagger.json`.

The generator keeps the connector settings and the `InvokeMCP` operation, rebuilds every other operation, and adds the drop-downs.

## MCP tools for Copilot Studio

| Tool | What it does |
|------|--------------|
| `execute_sql` | Runs SQL, waits for the result, and returns rows as objects keyed by column name. |
| `get_statement` | Checks a statement, or reads a later chunk of a large result. |
| `cancel_statement` | Cancels a running statement. |
| `list_warehouses` | Lists warehouses with state and size. |
| `manage_warehouse` | Starts or stops a warehouse. |
| `browse_catalog` | Lists catalogs, then the schemas in a catalog, then the tables in a schema. |
| `find_tables` | Finds tables by name pattern across catalogs. |
| `describe_table` | Reads a table's columns and types. |
| `list_jobs`, `run_job`, `get_job_run`, `get_job_run_output`, `cancel_job_run` | Find, start, check, read the output of, and cancel jobs. |
| `list_genie_spaces`, `ask_genie` | Ask Genie a question and get the answer, SQL and rows back. |

The MCP tools cover the common read and run tasks. For anything else, use the standard operations in a flow or app. For the whole Databricks REST API through three discovery tools, see the [Azure Databricks](../Azure%20Databricks/) Power Mission Control connector.

## Limits

- **One workspace per environment.** The host comes from the environment variable, so each Power Platform environment points at one workspace. To use two workspaces in the same environment, deploy a second copy of the connector with its own variable.
- **Same tenant.** The connector signs in users with OAuth. If Power Platform and Databricks are in different tenants, use the Microsoft connector with a service principal.
- **No service principal sign-in.** Each call runs as the signed-in user.
- **Premium.** Custom connectors need Power Apps or Power Automate Premium licenses.
- **Private networks.** If your workspace uses private connectivity, set up Power Platform virtual network support as described in the [Microsoft connector documentation](https://learn.microsoft.com/connectors/databricks/).

## Application Insights (optional)

To log calls, set `APP_INSIGHTS_ENABLED` to `true` and replace `[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]` in `script.csx` before you deploy. Events and errors for MCP requests and `ExecuteStatement` are sent. If you leave it off, nothing is sent.

## Troubleshooting

| Symptom | What to do |
|---------|------------|
| Sign-in fails with `AADSTS50011` | The redirect URL isn't registered. Add the URL from the connector's **Security** tab to your Entra app. |
| Sign-in fails with `AADSTS65001` | Admin consent is missing. Grant consent for `user_impersonation` on the AzureDatabricks API. |
| Calls return `Databricks returned 401` or `403` | The user isn't in the workspace, or doesn't have access to the resource. Add them in Databricks. |
| `No SQL warehouse is available to you` | The HTTP path variable is `auto` and the user has no `CAN USE` permission on any SQL warehouse. Ask a workspace admin. |
| `The configured HTTP path is not valid` | The HTTP path variable isn't a SQL warehouse HTTP path, a warehouse ID, or `auto`. Check the value. |
| Calls use a different warehouse than the variable says | The caller picked a warehouse, which wins over the variable. Or the connector wasn't saved again after you changed the variable. |
| Statements stay `PENDING` | The warehouse is starting. Wait, and call `GetStatement` again. |
| `CreateCatalog` fails with `Metastore storage root URL does not exist` | Your metastore has no default storage root. Pass `storage_root` with a storage location you own. |
| `GetJobRunOutput` fails for a job run | For a job with more than one task, pass a task's run ID from `GetJobRun`, not the job run ID. |
| `ListJobs` returns nothing for part of a name | The `name` filter matches a whole job name, ignoring case. List without a filter to search. |
| `DownloadGenieVisualization` returns `not a visualization` | The message has no chart attachment. Use a message whose answer includes a visualization. |
| Calls can't reach the workspace | The environment variable is empty or wrong, or the workspace is private. Check that the value is the host only (no `https://`), save the connector again after any change, and check your network setup. |
| Saving the connector fails and mentions an environment variable | The connector isn't in a solution, or the variable's name in the policy doesn't match. Deploy with `--solution-unique-name` and check the name, including your publisher prefix. |
| Copilot Studio doesn't list the tools | Check that the connector's `InvokeMCP` operation is in the agent's tools, and that the connection works. |

## Author

Troy Taylor, [troy@troystaylor.com](mailto:troy@troystaylor.com), [github.com/troystaylor](https://github.com/troystaylor)

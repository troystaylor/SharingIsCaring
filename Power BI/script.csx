using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Power BI MCP — Mission Control Connector                                  ║
// ║                                                                            ║
// ║  Provides progressive discovery of Power BI REST API operations:            ║
// ║  workspaces, reports, dashboards, datasets, apps, DAX queries, and          ║
// ║  report export — via scan_powerbi, launch_powerbi, sequence_powerbi.       ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "power-bi-mcp",
            Version = "1.0.0",
            Title = "Power BI MCP Server",
            Description = "Power BI REST API connector with progressive discovery for Copilot Studio. Covers workspaces, reports, dashboards, datasets, apps, DAX queries, and report export."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Logging = true,
            Completions = true
        }
    };

    // ── Mission Control Configuration ─────────────────────────────────────

    private static readonly MissionControlOptions McOptions = new MissionControlOptions
    {
        ServiceName = "powerbi",
        BaseApiUrl = "https://api.powerbi.com/v1.0/myorg",
        DiscoveryMode = DiscoveryMode.Static,
        BatchMode = BatchMode.Sequential,
        MaxBatchSize = 20,
        DefaultPageSize = 25,
        CacheExpiryMinutes = 10,
        MaxDiscoverResults = 5,
        SummarizeResponses = true,
        MaxBodyLength = 500,
        MaxTextLength = 1000,
    };

    // ── Capability Index ─────────────────────────────────────────────────

    private const string CAPABILITY_INDEX = @"[
        {
            ""cid"": ""list_workspaces"",
            ""endpoint"": ""/groups"",
            ""method"": ""GET"",
            ""outcome"": ""List all workspaces (groups) the current user has access to"",
            ""domain"": ""workspaces"",
            ""requiredParams"": [],
            ""optionalParams"": [""$filter"", ""$top"", ""$skip""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""$filter\"":{\""type\"":\""string\"",\""description\"":\""OData filter expression\""},\""$top\"":{\""type\"":\""integer\"",\""description\"":\""Number of results to return\""},\""$skip\"":{\""type\"":\""integer\"",\""description\"":\""Number of results to skip\""}}}""
        },
        {
            ""cid"": ""get_workspace"",
            ""endpoint"": ""/groups/{groupId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific workspace by its ID"",
            ""domain"": ""workspaces"",
            ""requiredParams"": [""groupId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""}},\""required\"":[\""groupId\""]}""
        },
        {
            ""cid"": ""get_workspace_users"",
            ""endpoint"": ""/groups/{groupId}/users"",
            ""method"": ""GET"",
            ""outcome"": ""List users that have access to the specified workspace"",
            ""domain"": ""workspaces"",
            ""requiredParams"": [""groupId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""}},\""required\"":[\""groupId\""]}""
        },
        {
            ""cid"": ""list_reports"",
            ""endpoint"": ""/reports"",
            ""method"": ""GET"",
            ""outcome"": ""List all reports from My workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        },
        {
            ""cid"": ""list_workspace_reports"",
            ""endpoint"": ""/groups/{groupId}/reports"",
            ""method"": ""GET"",
            ""outcome"": ""List all reports from the specified workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [""groupId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""}},\""required\"":[\""groupId\""]}""
        },
        {
            ""cid"": ""get_report"",
            ""endpoint"": ""/reports/{reportId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific report from My workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [""reportId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""}},\""required\"":[\""reportId\""]}""
        },
        {
            ""cid"": ""get_workspace_report"",
            ""endpoint"": ""/groups/{groupId}/reports/{reportId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific report from the specified workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [""groupId"", ""reportId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""}},\""required\"":[\""groupId\"",\""reportId\""]}""
        },
        {
            ""cid"": ""get_report_pages"",
            ""endpoint"": ""/reports/{reportId}/pages"",
            ""method"": ""GET"",
            ""outcome"": ""List pages within a report from My workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [""reportId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""}},\""required\"":[\""reportId\""]}""
        },
        {
            ""cid"": ""get_workspace_report_pages"",
            ""endpoint"": ""/groups/{groupId}/reports/{reportId}/pages"",
            ""method"": ""GET"",
            ""outcome"": ""List pages within a report from the specified workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [""groupId"", ""reportId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""}},\""required\"":[\""groupId\"",\""reportId\""]}""
        },
        {
            ""cid"": ""clone_report"",
            ""endpoint"": ""/reports/{reportId}/Clone"",
            ""method"": ""POST"",
            ""outcome"": ""Clone a report from My workspace"",
            ""domain"": ""reports"",
            ""requiredParams"": [""reportId"", ""name""],
            ""optionalParams"": [""targetModelId"", ""targetWorkspaceId""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""},\""name\"":{\""type\"":\""string\"",\""description\"":\""The new report name\""},\""targetModelId\"":{\""type\"":\""string\"",\""description\"":\""Optional target dataset ID\""},\""targetWorkspaceId\"":{\""type\"":\""string\"",\""description\"":\""Optional target workspace ID\""}},\""required\"":[\""reportId\"",\""name\""]}""
        },
        {
            ""cid"": ""list_dashboards"",
            ""endpoint"": ""/dashboards"",
            ""method"": ""GET"",
            ""outcome"": ""List all dashboards from My workspace"",
            ""domain"": ""dashboards"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        },
        {
            ""cid"": ""list_workspace_dashboards"",
            ""endpoint"": ""/groups/{groupId}/dashboards"",
            ""method"": ""GET"",
            ""outcome"": ""List all dashboards from the specified workspace"",
            ""domain"": ""dashboards"",
            ""requiredParams"": [""groupId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""}},\""required\"":[\""groupId\""]}""
        },
        {
            ""cid"": ""get_dashboard"",
            ""endpoint"": ""/dashboards/{dashboardId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific dashboard from My workspace"",
            ""domain"": ""dashboards"",
            ""requiredParams"": [""dashboardId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""dashboardId\"":{\""type\"":\""string\"",\""description\"":\""The dashboard ID\""}},\""required\"":[\""dashboardId\""]}""
        },
        {
            ""cid"": ""get_workspace_dashboard"",
            ""endpoint"": ""/groups/{groupId}/dashboards/{dashboardId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific dashboard from the specified workspace"",
            ""domain"": ""dashboards"",
            ""requiredParams"": [""groupId"", ""dashboardId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""dashboardId\"":{\""type\"":\""string\"",\""description\"":\""The dashboard ID\""}},\""required\"":[\""groupId\"",\""dashboardId\""]}""
        },
        {
            ""cid"": ""list_dashboard_tiles"",
            ""endpoint"": ""/dashboards/{dashboardId}/tiles"",
            ""method"": ""GET"",
            ""outcome"": ""List tiles within a dashboard from My workspace"",
            ""domain"": ""dashboards"",
            ""requiredParams"": [""dashboardId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""dashboardId\"":{\""type\"":\""string\"",\""description\"":\""The dashboard ID\""}},\""required\"":[\""dashboardId\""]}""
        },
        {
            ""cid"": ""list_workspace_dashboard_tiles"",
            ""endpoint"": ""/groups/{groupId}/dashboards/{dashboardId}/tiles"",
            ""method"": ""GET"",
            ""outcome"": ""List tiles within a dashboard from the specified workspace"",
            ""domain"": ""dashboards"",
            ""requiredParams"": [""groupId"", ""dashboardId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""dashboardId\"":{\""type\"":\""string\"",\""description\"":\""The dashboard ID\""}},\""required\"":[\""groupId\"",\""dashboardId\""]}""
        },
        {
            ""cid"": ""list_datasets"",
            ""endpoint"": ""/datasets"",
            ""method"": ""GET"",
            ""outcome"": ""List all datasets from My workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        },
        {
            ""cid"": ""list_workspace_datasets"",
            ""endpoint"": ""/groups/{groupId}/datasets"",
            ""method"": ""GET"",
            ""outcome"": ""List all datasets from the specified workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""groupId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""}},\""required\"":[\""groupId\""]}""
        },
        {
            ""cid"": ""get_dataset"",
            ""endpoint"": ""/datasets/{datasetId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific dataset from My workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""datasetId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""}},\""required\"":[\""datasetId\""]}""
        },
        {
            ""cid"": ""get_workspace_dataset"",
            ""endpoint"": ""/groups/{groupId}/datasets/{datasetId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific dataset from the specified workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""groupId"", ""datasetId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""}},\""required\"":[\""groupId\"",\""datasetId\""]}""
        },
        {
            ""cid"": ""refresh_dataset"",
            ""endpoint"": ""/datasets/{datasetId}/refreshes"",
            ""method"": ""POST"",
            ""outcome"": ""Trigger a refresh for a dataset in My workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""datasetId""],
            ""optionalParams"": [""notifyOption""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""},\""notifyOption\"":{\""type\"":\""string\"",\""description\"":\""Notification option: MailOnFailure or NoNotification\"",\""enum\"":[\""MailOnFailure\"",\""NoNotification\""]}},\""required\"":[\""datasetId\""]}""
        },
        {
            ""cid"": ""refresh_workspace_dataset"",
            ""endpoint"": ""/groups/{groupId}/datasets/{datasetId}/refreshes"",
            ""method"": ""POST"",
            ""outcome"": ""Trigger a refresh for a dataset in the specified workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""groupId"", ""datasetId""],
            ""optionalParams"": [""notifyOption""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""},\""notifyOption\"":{\""type\"":\""string\"",\""description\"":\""Notification option: MailOnFailure or NoNotification\"",\""enum\"":[\""MailOnFailure\"",\""NoNotification\""]}},\""required\"":[\""groupId\"",\""datasetId\""]}""
        },
        {
            ""cid"": ""get_refresh_history"",
            ""endpoint"": ""/datasets/{datasetId}/refreshes"",
            ""method"": ""GET"",
            ""outcome"": ""Get refresh history for a dataset in My workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""datasetId""],
            ""optionalParams"": [""$top""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""},\""$top\"":{\""type\"":\""integer\"",\""description\"":\""Number of refresh entries to return\""}},\""required\"":[\""datasetId\""]}""
        },
        {
            ""cid"": ""get_workspace_refresh_history"",
            ""endpoint"": ""/groups/{groupId}/datasets/{datasetId}/refreshes"",
            ""method"": ""GET"",
            ""outcome"": ""Get refresh history for a dataset in the specified workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""groupId"", ""datasetId""],
            ""optionalParams"": [""$top""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""},\""$top\"":{\""type\"":\""integer\"",\""description\"":\""Number of refresh entries to return\""}},\""required\"":[\""groupId\"",\""datasetId\""]}""
        },
        {
            ""cid"": ""get_datasources"",
            ""endpoint"": ""/datasets/{datasetId}/datasources"",
            ""method"": ""GET"",
            ""outcome"": ""List data sources for a dataset in My workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""datasetId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""}},\""required\"":[\""datasetId\""]}""
        },
        {
            ""cid"": ""get_workspace_datasources"",
            ""endpoint"": ""/groups/{groupId}/datasets/{datasetId}/datasources"",
            ""method"": ""GET"",
            ""outcome"": ""List data sources for a dataset in the specified workspace"",
            ""domain"": ""datasets"",
            ""requiredParams"": [""groupId"", ""datasetId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""groupId\"":{\""type\"":\""string\"",\""description\"":\""The workspace (group) ID\""},\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID\""}},\""required\"":[\""groupId\"",\""datasetId\""]}""
        },
        {
            ""cid"": ""execute_queries"",
            ""endpoint"": ""/datasets/{datasetId}/executeQueries"",
            ""method"": ""POST"",
            ""outcome"": ""Execute a DAX query against a dataset. The dataset must reside in My workspace or another workspace. Only DAX queries are supported. Limited to 100000 rows or 1000000 values per query."",
            ""domain"": ""datasets"",
            ""requiredParams"": [""datasetId"", ""queries""],
            ""optionalParams"": [""impersonatedUserName"", ""includeNulls""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""datasetId\"":{\""type\"":\""string\"",\""description\"":\""The dataset ID (path parameter)\""},\""queries\"":{\""type\"":\""array\"",\""description\"":\""Array of DAX queries to execute. Each item has a query property with the DAX expression.\"",\""items\"":{\""type\"":\""object\"",\""properties\"":{\""query\"":{\""type\"":\""string\"",\""description\"":\""The DAX query to execute, e.g. EVALUATE VALUES(MyTable)\""}},\""required\"":[\""query\""]}},\""impersonatedUserName\"":{\""type\"":\""string\"",\""description\"":\""UPN of user to impersonate (only applies if RLS is enabled)\""},\""includeNulls\"":{\""type\"":\""boolean\"",\""description\"":\""Whether null values should be included in results. Default false.\""}},\""required\"":[\""datasetId\"",\""queries\""]}""
        },
        {
            ""cid"": ""list_apps"",
            ""endpoint"": ""/apps"",
            ""method"": ""GET"",
            ""outcome"": ""List all installed Power BI apps for the current user"",
            ""domain"": ""apps"",
            ""requiredParams"": [],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{}}""
        },
        {
            ""cid"": ""get_app"",
            ""endpoint"": ""/apps/{appId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific installed app"",
            ""domain"": ""apps"",
            ""requiredParams"": [""appId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""appId\"":{\""type\"":\""string\"",\""description\"":\""The app ID\""}},\""required\"":[\""appId\""]}""
        },
        {
            ""cid"": ""get_app_reports"",
            ""endpoint"": ""/apps/{appId}/reports"",
            ""method"": ""GET"",
            ""outcome"": ""List reports from the specified installed app"",
            ""domain"": ""apps"",
            ""requiredParams"": [""appId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""appId\"":{\""type\"":\""string\"",\""description\"":\""The app ID\""}},\""required\"":[\""appId\""]}""
        },
        {
            ""cid"": ""get_app_report"",
            ""endpoint"": ""/apps/{appId}/reports/{reportId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific report from the specified installed app"",
            ""domain"": ""apps"",
            ""requiredParams"": [""appId"", ""reportId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""appId\"":{\""type\"":\""string\"",\""description\"":\""The app ID\""},\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""}},\""required\"":[\""appId\"",\""reportId\""]}""
        },
        {
            ""cid"": ""get_app_dashboards"",
            ""endpoint"": ""/apps/{appId}/dashboards"",
            ""method"": ""GET"",
            ""outcome"": ""List dashboards from the specified installed app"",
            ""domain"": ""apps"",
            ""requiredParams"": [""appId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""appId\"":{\""type\"":\""string\"",\""description\"":\""The app ID\""}},\""required\"":[\""appId\""]}""
        },
        {
            ""cid"": ""get_app_dashboard"",
            ""endpoint"": ""/apps/{appId}/dashboards/{dashboardId}"",
            ""method"": ""GET"",
            ""outcome"": ""Get details of a specific dashboard from the specified installed app"",
            ""domain"": ""apps"",
            ""requiredParams"": [""appId"", ""dashboardId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""appId\"":{\""type\"":\""string\"",\""description\"":\""The app ID\""},\""dashboardId\"":{\""type\"":\""string\"",\""description\"":\""The dashboard ID\""}},\""required\"":[\""appId\"",\""dashboardId\""]}""
        },
        {
            ""cid"": ""export_report"",
            ""endpoint"": ""/reports/{reportId}/ExportTo"",
            ""method"": ""POST"",
            ""outcome"": ""Trigger an export of a report to a file format (PDF, PPTX, or PNG for Power BI reports). This is an asynchronous operation. After calling this, you MUST poll with get_export_status using the returned export ID until the status is Succeeded, then share the resourceLocation download URL with the user."",
            ""domain"": ""export"",
            ""requiredParams"": [""reportId"", ""format""],
            ""optionalParams"": [""pages"", ""includeHiddenPages"", ""locale""],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID (path parameter)\""},\""format\"":{\""type\"":\""string\"",\""description\"":\""Export file format\"",\""enum\"":[\""PDF\"",\""PPTX\"",\""PNG\""]},\""pages\"":{\""type\"":\""array\"",\""description\"":\""Optional list of specific pages to export\"",\""items\"":{\""type\"":\""object\"",\""properties\"":{\""pageName\"":{\""type\"":\""string\"",\""description\"":\""The page name\""}}}},\""includeHiddenPages\"":{\""type\"":\""boolean\"",\""description\"":\""Whether to include hidden pages. Default false.\""},\""locale\"":{\""type\"":\""string\"",\""description\"":\""Locale to apply (e.g. en-US)\""}},\""required\"":[\""reportId\"",\""format\""]}""
        },
        {
            ""cid"": ""get_export_status"",
            ""endpoint"": ""/reports/{reportId}/exports/{exportId}"",
            ""method"": ""GET"",
            ""outcome"": ""Check the status of a report export job. Returns status (NotStarted, Running, Succeeded, Failed), percentComplete, and resourceLocation. If status is Running or NotStarted, call this endpoint again after a few seconds. When status is Succeeded, the resourceLocation field contains a direct download URL — share this URL with the user so they can download the exported file."",
            ""domain"": ""export"",
            ""requiredParams"": [""reportId"", ""exportId""],
            ""optionalParams"": [],
            ""schemaJson"": ""{\""type\"":\""object\"",\""properties\"":{\""reportId\"":{\""type\"":\""string\"",\""description\"":\""The report ID\""},\""exportId\"":{\""type\"":\""string\"",\""description\"":\""The export job ID returned by export_report\""}},\""required\"":[\""reportId\"",\""exportId\""]}""
        }
    ]";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // Route REST operations (hybrid pattern: MCP + Swagger)
        if (this.Context.OperationId != "InvokeMCP")
        {
            return await HandleRestOperationAsync(correlationId).ConfigureAwait(false);
        }

        // MCP handler for InvokeMCP
        var handler = new McpRequestHandler(Options);

        // Register mission control tools (scan, launch, sequence)
        MissionControl.RegisterMission(handler, McOptions, CAPABILITY_INDEX, this);

        // Wire up logging (optional)
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // Handle the request
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── REST Operation Handler (Hybrid Pattern) ──────────────────────────

    private async Task<HttpResponseMessage> HandleRestOperationAsync(string correlationId)
    {
        try
        {
            await LogToAppInsights("RestRequest", new { CorrelationId = correlationId, OperationId = this.Context.OperationId }, correlationId);

            // Forward the request to the Power BI API with auth
            var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RestError", new { CorrelationId = correlationId, OperationId = this.Context.OperationId, Error = ex.Message }, correlationId);
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<JObject> SendExternalRequestAsync(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API request failed ({(int)response.StatusCode}): {content}");

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private string GetConnectionParameter(string name)
    {
        try
        {
            return null;
        }
        catch { return null; }
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new { ver = 2, name = eventName, properties = propsDict }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK + ORCHESTRATION ENGINE                           ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler (MCP 2025-11-25) plus mission control classes:  ║
// ║  MissionControlOptions, CapabilityIndex, ApiProxy, McpChainClient,         ║
// ║  DiscoveryEngine, MissionControl.                                          ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Mission Control Configuration ────────────────────────────────────────────

public enum DiscoveryMode { Static, Hybrid, McpChain }
public enum BatchMode { Sequential, BatchEndpoint }

public class MissionControlOptions
{
    public string ServiceName { get; set; } = "api";
    public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.Static;
    public string BaseApiUrl { get; set; }
    public string DefaultApiVersion { get; set; }
    public BatchMode BatchMode { get; set; } = BatchMode.Sequential;
    public string BatchEndpointPath { get; set; } = "/$batch";
    public int MaxBatchSize { get; set; } = 20;
    public int DefaultPageSize { get; set; } = 25;
    public int CacheExpiryMinutes { get; set; } = 10;
    public int DescribeCacheTTL { get; set; } = 30;
    public int MaxDiscoverResults { get; set; } = 3;
    public bool SummarizeResponses { get; set; } = true;
    public int MaxBodyLength { get; set; } = 500;
    public int MaxTextLength { get; set; } = 1000;
    public string DescribeEndpointPattern { get; set; }
    public string McpChainEndpoint { get; set; }
    public string McpChainToolName { get; set; }
    public string McpChainQueryPrefix { get; set; }
    public Dictionary<string, Action<string, JObject>> SmartDefaults { get; set; }
}

// ── Capability Entry ─────────────────────────────────────────────────────────

public class CapabilityEntry
{
    public string Cid { get; set; }
    public string Endpoint { get; set; }
    public string Method { get; set; }
    public string Outcome { get; set; }
    public string Domain { get; set; }
    public string[] RequiredParams { get; set; }
    public string[] OptionalParams { get; set; }
    public string SchemaJson { get; set; }
}

// ── Capability Index ─────────────────────────────────────────────────────────

public class CapabilityIndex
{
    private readonly List<CapabilityEntry> _entries;

    public CapabilityIndex(string indexJson)
    {
        _entries = new List<CapabilityEntry>();
        if (string.IsNullOrWhiteSpace(indexJson)) return;

        var array = JArray.Parse(indexJson);
        foreach (var item in array)
        {
            _entries.Add(new CapabilityEntry
            {
                Cid = item.Value<string>("cid") ?? "",
                Endpoint = item.Value<string>("endpoint") ?? "",
                Method = (item.Value<string>("method") ?? "GET").ToUpperInvariant(),
                Outcome = item.Value<string>("outcome") ?? "",
                Domain = item.Value<string>("domain") ?? "",
                RequiredParams = item["requiredParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                OptionalParams = item["optionalParams"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                SchemaJson = item.Value<string>("schemaJson")
            });
        }
    }

    public List<CapabilityEntry> Search(string query, string domain = null, int maxResults = 3)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(domain))
            return _entries.Take(maxResults).ToList();

        var queryLower = (query ?? "").ToLowerInvariant();
        var queryWords = queryLower.Split(new[] { ' ', '_', '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var domainLower = (domain ?? "").ToLowerInvariant();

        var scored = new List<KeyValuePair<CapabilityEntry, int>>();

        foreach (var entry in _entries)
        {
            int score = 0;
            var cidLower = entry.Cid.ToLowerInvariant();
            var outcomeLower = entry.Outcome.ToLowerInvariant();
            var entryDomainLower = entry.Domain.ToLowerInvariant();
            var endpointLower = entry.Endpoint.ToLowerInvariant();

            if (cidLower == queryLower) score += 100;
            else if (cidLower.Contains(queryLower) || queryLower.Contains(cidLower)) score += 60;

            if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower == domainLower) score += 50;
            else if (!string.IsNullOrWhiteSpace(domainLower) && entryDomainLower != domainLower) score -= 20;

            foreach (var word in queryWords)
            {
                if (word.Length < 2) continue;
                if (outcomeLower.Contains(word)) score += 10;
                if (cidLower.Contains(word)) score += 15;
                if (endpointLower.Contains(word)) score += 8;
            }

            var methodUpper = entry.Method.ToUpperInvariant();
            if (queryLower.Contains(methodUpper.ToLowerInvariant()) && methodUpper.Length > 2) score += 5;

            if (score > 0) scored.Add(new KeyValuePair<CapabilityEntry, int>(entry, score));
        }

        return scored
            .OrderByDescending(kv => kv.Value)
            .Take(maxResults)
            .Select(kv => kv.Key)
            .ToList();
    }

    public CapabilityEntry Get(string cid)
    {
        return _entries.FirstOrDefault(e =>
            string.Equals(e.Cid, cid, StringComparison.OrdinalIgnoreCase));
    }

    public List<CapabilityEntry> GetAll() => new List<CapabilityEntry>(_entries);

    public int Count => _entries.Count;
}

// ── Cache Infrastructure ─────────────────────────────────────────────────────

internal class CacheEntry
{
    public JObject Result { get; set; }
    public DateTime Expiry { get; set; }
}

// ── API Proxy ────────────────────────────────────────────────────────────────

public class ApiProxy
{
    private readonly MissionControlOptions _options;
    private const int MAX_RETRIES = 3;

    private static readonly string[] CollectionPatterns = new[]
    {
        "/messages", "/events", "/users", "/groups", "/teams", "/channels",
        "/members", "/children", "/items", "/lists", "/tasks", "/contacts",
        "/calendars", "/drives", "/sites", "/records", "/customers", "/orders",
        "/products", "/invoices", "/accounts", "/leads", "/cases", "/tickets",
        "/reports", "/dashboards", "/datasets", "/apps", "/tiles", "/pages",
        "/refreshes", "/datasources", "/exports"
    };

    public ApiProxy(MissionControlOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<JObject> InvokeAsync(
        ScriptBase context,
        string endpoint,
        string method,
        JObject body = null,
        JObject queryParams = null,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        string warning = null;
        if (index != null)
        {
            var match = index.GetAll().Any(e =>
                string.Equals(e.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase) ||
                EndpointMatchesPattern(e.Endpoint, endpoint));
            if (!match)
                warning = $"Endpoint '{endpoint}' is not in the capability index. Proceeding anyway.";
        }

        var url = BuildUrl(endpoint, method, queryParams, apiVersion);

        return await ExecuteWithRetryAsync(context, url, method, body, warning).ConfigureAwait(false);
    }

    public async Task<JObject> BatchInvokeAsync(
        ScriptBase context,
        JArray requests,
        string apiVersion = null,
        CapabilityIndex index = null)
    {
        if (requests == null || requests.Count == 0)
            return CreateErrorResult("batch_empty", "No requests provided",
                "Provide at least one request in the requests array.",
                "The requests array is empty.");

        if (requests.Count > _options.MaxBatchSize)
            return CreateErrorResult("batch_too_large",
                $"Batch exceeds maximum size of {_options.MaxBatchSize}",
                $"Split into batches of {_options.MaxBatchSize} or fewer.",
                $"Too many requests. Maximum is {_options.MaxBatchSize}, got {requests.Count}.");

        if (_options.BatchMode == BatchMode.BatchEndpoint)
            return await ExecuteBatchEndpointAsync(context, requests, apiVersion).ConfigureAwait(false);

        return await ExecuteSequentialBatchAsync(context, requests, apiVersion, index).ConfigureAwait(false);
    }

    public void SummarizeResponse(JToken token)
    {
        if (!_options.SummarizeResponses) return;
        SummarizeToken(token, _options.MaxBodyLength, _options.MaxTextLength);
    }

    private string BuildUrl(string endpoint, string method, JObject queryParams, string apiVersion)
    {
        var baseUrl = _options.BaseApiUrl?.TrimEnd('/') ?? "";
        var version = apiVersion ?? _options.DefaultApiVersion;

        var path = endpoint?.TrimStart('/') ?? "";
        string url;
        if (!string.IsNullOrWhiteSpace(version) && !baseUrl.Contains(version))
            url = $"{baseUrl}/{version}/{path}";
        else
            url = $"{baseUrl}/{path}";

        queryParams = queryParams ?? new JObject();
        ApplySmartDefaults(endpoint, method, queryParams);

        var queryParts = new List<string>();
        foreach (var prop in queryParams.Properties())
        {
            var val = prop.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
                queryParts.Add($"{Uri.EscapeDataString(prop.Name)}={Uri.EscapeDataString(val)}");
        }

        if (queryParts.Count > 0)
            url += (url.Contains("?") ? "&" : "?") + string.Join("&", queryParts);

        return url;
    }

    private void ApplySmartDefaults(string endpoint, string method, JObject queryParams)
    {
        var endpointLower = (endpoint ?? "").ToLowerInvariant();
        var methodUpper = (method ?? "GET").ToUpperInvariant();

        if (methodUpper == "GET" && IsCollectionEndpoint(endpointLower))
        {
            if (queryParams["$top"] == null && queryParams["top"] == null &&
                queryParams["per_page"] == null && queryParams["limit"] == null &&
                queryParams["pageSize"] == null)
            {
                queryParams["$top"] = _options.DefaultPageSize;
            }
        }

        if (_options.SmartDefaults != null)
        {
            foreach (var kvp in _options.SmartDefaults)
            {
                if (endpointLower.Contains(kvp.Key.ToLowerInvariant()))
                    kvp.Value?.Invoke(endpoint, queryParams);
            }
        }
    }

    private bool IsCollectionEndpoint(string endpointLower)
    {
        var lastSegment = endpointLower.Split('/').LastOrDefault() ?? "";
        if (lastSegment.StartsWith("{") || Guid.TryParse(lastSegment, out _)) return false;

        return CollectionPatterns.Any(p => endpointLower.EndsWith(p, StringComparison.OrdinalIgnoreCase))
            || (lastSegment.Length > 2 && !lastSegment.Contains("{"));
    }

    private static bool EndpointMatchesPattern(string pattern, string endpoint)
    {
        var patternParts = pattern.Split('/');
        var endpointParts = endpoint.Split('/');
        if (patternParts.Length != endpointParts.Length) return false;

        for (int i = 0; i < patternParts.Length; i++)
        {
            if (patternParts[i].StartsWith("{") && patternParts[i].EndsWith("}")) continue;
            if (!string.Equals(patternParts[i], endpointParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private async Task<JObject> ExecuteWithRetryAsync(
        ScriptBase context, string url, string method,
        JObject body, string warning, int retryCount = 0)
    {
        var httpMethod = new HttpMethod(method.ToUpperInvariant());
        var request = new HttpRequestMessage(httpMethod, url);

        if (context.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = context.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (httpMethod == HttpMethod.Post || httpMethod.Method == "PATCH" || httpMethod.Method == "PUT"))
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await context.Context.SendAsync(request, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return CreateErrorResult("connection_error", ex.Message,
                "Check that the API is reachable and try again.",
                $"Failed to connect to {url}: {ex.Message}");
        }

        var statusCode = (int)response.StatusCode;
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (statusCode == 429 && retryCount < MAX_RETRIES)
        {
            var retryAfter = 5;
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var val = retryValues.FirstOrDefault();
                if (int.TryParse(val, out var seconds))
                    retryAfter = Math.Min(seconds, 30);
            }
            await Task.Delay(retryAfter * 1000).ConfigureAwait(false);
            return await ExecuteWithRetryAsync(context, url, method, body, warning, retryCount + 1).ConfigureAwait(false);
        }

        if (statusCode == 401 || statusCode == 403)
        {
            return CreateErrorResult("permission_denied", $"HTTP {statusCode}",
                "Check that your account has the required permissions. Contact your administrator if needed.",
                $"Access denied ({statusCode}). You don't have permission for this operation.");
        }

        if (statusCode == 404)
        {
            return CreateErrorResult("not_found", $"HTTP 404",
                "Verify the endpoint path and any resource IDs. Use scan to find the correct endpoint.",
                $"Resource not found at {url}. The endpoint may be incorrect or the resource may not exist.");
        }

        if (statusCode >= 400)
        {
            var errorDetail = "";
            try { errorDetail = JObject.Parse(responseBody)?["message"]?.ToString() ?? responseBody; }
            catch { errorDetail = responseBody; }

            return CreateErrorResult("api_error", $"HTTP {statusCode}: {errorDetail}",
                "Check the request parameters and try again.",
                $"API returned error {statusCode}: {errorDetail}");
        }

        JToken data;
        try
        {
            data = string.IsNullOrWhiteSpace(responseBody)
                ? new JObject { ["success"] = true }
                : JToken.Parse(responseBody);
        }
        catch
        {
            data = new JObject { ["text"] = responseBody };
        }

        SummarizeResponse(data);

        var result = new JObject
        {
            ["success"] = true,
            ["data"] = data
        };

        if (data is JObject dataObj)
        {
            var nextLink = dataObj.Value<string>("@odata.nextLink")
                ?? dataObj.Value<string>("nextLink")
                ?? dataObj.Value<string>("next_page_url")
                ?? dataObj.Value<string>("next");

            if (!string.IsNullOrWhiteSpace(nextLink))
            {
                result["hasMore"] = true;
                result["nextLink"] = nextLink;
                result["nextPageHint"] = $"Call launch again with the nextLink value as the full URL to get the next page.";
            }
        }

        if (!string.IsNullOrWhiteSpace(warning))
            result["warning"] = warning;

        return result;
    }

    private async Task<JObject> ExecuteSequentialBatchAsync(
        ScriptBase context, JArray requests, string apiVersion, CapabilityIndex index)
    {
        var responses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var req in requests)
        {
            var id = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            var endpoint = req.Value<string>("endpoint") ?? "";
            var method = req.Value<string>("method") ?? "GET";
            var body = req["body"] as JObject;
            var qp = req["query_params"] as JObject;

            try
            {
                var result = await InvokeAsync(context, endpoint, method, body, qp, apiVersion, index).ConfigureAwait(false);
                var success = result.Value<bool?>("success") ?? false;
                if (success) successCount++; else errorCount++;

                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = success,
                    ["status"] = success ? 200 : 400,
                    ["data"] = result
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                responses.Add(new JObject
                {
                    ["id"] = id,
                    ["success"] = false,
                    ["status"] = 500,
                    ["error"] = ex.Message
                });
            }
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = responses
        };
    }

    private async Task<JObject> ExecuteBatchEndpointAsync(
        ScriptBase context, JArray requests, string apiVersion)
    {
        var baseUrl = _options.BaseApiUrl?.TrimEnd('/') ?? "";
        var version = apiVersion ?? _options.DefaultApiVersion;
        var batchUrl = !string.IsNullOrWhiteSpace(version)
            ? $"{baseUrl}/{version}{_options.BatchEndpointPath}"
            : $"{baseUrl}{_options.BatchEndpointPath}";

        var batchRequests = new JArray();
        foreach (var req in requests)
        {
            var batchReq = new JObject
            {
                ["id"] = req.Value<string>("id") ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                ["method"] = (req.Value<string>("method") ?? "GET").ToUpperInvariant(),
                ["url"] = req.Value<string>("endpoint") ?? ""
            };

            var body = req["body"] as JObject;
            var method = batchReq.Value<string>("method");
            if (body != null && (method == "POST" || method == "PATCH" || method == "PUT"))
            {
                batchReq["body"] = body;
                batchReq["headers"] = new JObject { ["Content-Type"] = "application/json" };
            }

            batchRequests.Add(batchReq);
        }

        var batchBody = new JObject { ["requests"] = batchRequests };
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, batchUrl)
        {
            Content = new StringContent(batchBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };

        if (context.Context.Request.Headers.Authorization != null)
            httpRequest.Headers.Authorization = context.Context.Request.Headers.Authorization;

        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await context.Context.SendAsync(httpRequest, context.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject batchResponse;
        try { batchResponse = JObject.Parse(content); }
        catch { return CreateErrorResult("batch_parse_error", "Failed to parse batch response", "Try individual requests instead.", content); }

        var batchResponses = batchResponse["responses"] as JArray ?? new JArray();
        var resultResponses = new JArray();
        int successCount = 0, errorCount = 0;

        foreach (var resp in batchResponses)
        {
            var status = resp.Value<int?>("status") ?? 0;
            var success = status >= 200 && status < 300;
            if (success) successCount++; else errorCount++;

            var processed = new JObject
            {
                ["id"] = resp.Value<string>("id"),
                ["status"] = status,
                ["success"] = success
            };

            if (success)
            {
                var bodyData = resp["body"];
                if (bodyData != null) SummarizeResponse(bodyData);
                processed["data"] = bodyData;
            }
            else
            {
                processed["error"] = resp["body"];
            }

            resultResponses.Add(processed);
        }

        return new JObject
        {
            ["success"] = errorCount == 0,
            ["batchSize"] = requests.Count,
            ["successCount"] = successCount,
            ["errorCount"] = errorCount,
            ["responses"] = resultResponses
        };
    }

    private void SummarizeToken(JToken token, int maxBodyLength, int maxTextLength)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                var name = prop.Name.ToLowerInvariant();
                if ((name == "body" || name == "bodypreview" || name == "description") && prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    var stripped = StripHtml(val);
                    if (stripped.Length > maxBodyLength)
                    {
                        obj[prop.Name] = stripped.Substring(0, maxBodyLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                    else
                    {
                        obj[prop.Name] = stripped;
                    }
                }
                else if (prop.Value.Type == JTokenType.String)
                {
                    var val = prop.Value.ToString();
                    if (val.Length > maxTextLength)
                    {
                        obj[prop.Name] = val.Substring(0, maxTextLength) + "...";
                        obj[prop.Name + "_truncated"] = true;
                    }
                }
                else if (prop.Value is JObject || prop.Value is JArray)
                {
                    SummarizeToken(prop.Value, maxBodyLength, maxTextLength);
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
                SummarizeToken(item, maxBodyLength, maxTextLength);
        }
    }

    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";
        html = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
                    .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'");
        html = Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }

    private static JObject CreateErrorResult(string error, string message, string suggestion, string friendlyMessage)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = error,
            ["code"] = error,
            ["message"] = message,
            ["friendlyMessage"] = friendlyMessage,
            ["suggestion"] = suggestion
        };
    }
}

// ── MCP Chain Client ─────────────────────────────────────────────────────────

public class McpChainClient
{
    private static readonly Dictionary<string, CacheEntry> _chainCache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    public async Task<JObject> DiscoverAsync(
        ScriptBase context,
        MissionControlOptions options,
        string query,
        string category = null)
    {
        var cacheKey = $"{query}|{category ?? ""}".ToLowerInvariant();

        if (_chainCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            var cachedResult = cached.Result.DeepClone() as JObject;
            cachedResult["cached"] = true;
            return cachedResult;
        }

        try
        {
            var endpoint = options.McpChainEndpoint;
            var toolName = options.McpChainToolName;
            var prefix = options.McpChainQueryPrefix ?? "";

            var initRequest = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JObject
                {
                    ["protocolVersion"] = "2025-11-25",
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "power-mission-control",
                        ["version"] = "3.0.0"
                    }
                }
            };

            await SendMcpRequestAsync(context, endpoint, initRequest).ConfigureAwait(false);

            var notifRequest = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            };
            await SendMcpRequestAsync(context, endpoint, notifRequest).ConfigureAwait(false);

            var enhancedQuery = string.IsNullOrWhiteSpace(prefix)
                ? query
                : $"{prefix} {category ?? ""} API {query}".Trim();

            var toolRequest = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/call",
                ["params"] = new JObject
                {
                    ["name"] = toolName,
                    ["arguments"] = new JObject { ["query"] = enhancedQuery }
                }
            };

            var toolResponse = await SendMcpRequestAsync(context, endpoint, toolRequest).ConfigureAwait(false);

            var operations = ExtractOperationsFromMcpResponse(toolResponse);

            var result = new JObject
            {
                ["success"] = true,
                ["operationCount"] = operations.Count,
                ["operations"] = operations,
                ["cached"] = false
            };

            _chainCache[cacheKey] = new CacheEntry
            {
                Result = result.DeepClone() as JObject,
                Expiry = DateTime.UtcNow.AddMinutes(options.CacheExpiryMinutes)
            };

            return result;
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = "mcp_chain_failed",
                ["message"] = ex.Message,
                ["friendlyMessage"] = "Failed to discover operations via external documentation. Try a different query.",
                ["suggestion"] = "Rephrase your query or try invoking a known endpoint directly."
            };
        }
    }

    private async Task<JObject> SendMcpRequestAsync(ScriptBase context, string endpoint, JObject request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(request.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await context.Context.SendAsync(httpRequest, context.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        try { return JObject.Parse(content); }
        catch { return new JObject { ["text"] = content }; }
    }

    private JArray ExtractOperationsFromMcpResponse(JObject response)
    {
        var operations = new JArray();
        var contentArray = response?["result"]?["content"] as JArray;
        if (contentArray == null) return operations;

        var fullText = "";
        foreach (var content in contentArray)
        {
            if (content.Value<string>("type") == "text")
                fullText += content.Value<string>("text") + "\n";
        }

        var patterns = new[]
        {
            @"(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)",
            @"(?:endpoint|path|url|route):\s*[`""']?(/[\w\{\}/\-\.]+)[`""']?",
            @"```\s*(GET|POST|PATCH|PUT|DELETE)\s+(/[\w\{\}/\-\.]+)"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(fullText, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string httpMethod, path;
                if (match.Groups.Count >= 3)
                {
                    httpMethod = match.Groups[1].Value.ToUpperInvariant();
                    path = match.Groups[2].Value;
                }
                else
                {
                    httpMethod = "GET";
                    path = match.Groups[1].Value;
                }

                var key = $"{httpMethod} {path}";
                if (seen.Contains(key)) continue;
                seen.Add(key);

                operations.Add(new JObject
                {
                    ["endpoint"] = path,
                    ["method"] = httpMethod,
                    ["description"] = $"{httpMethod} {path}",
                    ["source"] = "documentation"
                });
            }
        }

        return operations;
    }
}

// ── Discovery Engine ─────────────────────────────────────────────────────────

public class DiscoveryEngine
{
    private readonly MissionControlOptions _options;
    private readonly CapabilityIndex _index;
    private readonly McpChainClient _chainClient;
    private readonly ApiProxy _proxy;

    private static readonly Dictionary<string, CacheEntry> _describeCache =
        new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

    public DiscoveryEngine(MissionControlOptions options, CapabilityIndex index, ApiProxy proxy, McpChainClient chainClient = null)
    {
        _options = options;
        _index = index;
        _proxy = proxy;
        _chainClient = chainClient;
    }

    public async Task<JObject> DiscoverAsync(
        ScriptBase context,
        string query,
        string domain = null,
        bool includeSchema = false)
    {
        switch (_options.DiscoveryMode)
        {
            case DiscoveryMode.Static:
                return DiscoverStatic(query, domain, includeSchema);

            case DiscoveryMode.Hybrid:
                return await DiscoverHybridAsync(context, query, domain, includeSchema).ConfigureAwait(false);

            case DiscoveryMode.McpChain:
                if (_chainClient == null)
                    return new JObject { ["success"] = false, ["error"] = "McpChainClient not configured" };
                return await _chainClient.DiscoverAsync(context, _options, query, domain).ConfigureAwait(false);

            default:
                return DiscoverStatic(query, domain, includeSchema);
        }
    }

    private JObject DiscoverStatic(string query, string domain, bool includeSchema)
    {
        if (_index == null)
            return new JObject { ["success"] = false, ["error"] = "No capability index configured" };

        var matches = _index.Search(query, domain, _options.MaxDiscoverResults);
        var operations = new JArray();

        foreach (var entry in matches)
        {
            var op = new JObject
            {
                ["cid"] = entry.Cid,
                ["endpoint"] = entry.Endpoint,
                ["method"] = entry.Method,
                ["outcome"] = entry.Outcome,
                ["domain"] = entry.Domain
            };

            if (entry.RequiredParams != null && entry.RequiredParams.Length > 0)
                op["requiredParams"] = new JArray(entry.RequiredParams);
            if (entry.OptionalParams != null && entry.OptionalParams.Length > 0)
                op["optionalParams"] = new JArray(entry.OptionalParams);

            if (includeSchema && !string.IsNullOrWhiteSpace(entry.SchemaJson))
            {
                try { op["inputSchema"] = JObject.Parse(entry.SchemaJson); }
                catch { op["inputSchema"] = entry.SchemaJson; }
            }

            operations.Add(op);
        }

        return new JObject
        {
            ["success"] = true,
            ["operationCount"] = operations.Count,
            ["totalCapabilities"] = _index.Count,
            ["operations"] = operations,
            ["cached"] = false
        };
    }

    private async Task<JObject> DiscoverHybridAsync(
        ScriptBase context, string query, string domain, bool includeSchema)
    {
        var result = DiscoverStatic(query, domain, includeSchema);
        if (!result.Value<bool>("success")) return result;

        if (includeSchema && !string.IsNullOrWhiteSpace(_options.DescribeEndpointPattern))
        {
            var operations = result["operations"] as JArray;
            if (operations != null)
            {
                foreach (var op in operations)
                {
                    var endpoint = op.Value<string>("endpoint") ?? "";
                    var resource = ExtractResourceFromEndpoint(endpoint);
                    if (string.IsNullOrWhiteSpace(resource)) continue;

                    var describeCacheKey = $"describe:{resource}";
                    if (_describeCache.TryGetValue(describeCacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                    {
                        op["liveSchema"] = cached.Result.DeepClone();
                        op["liveSchemaSource"] = "cache";
                        continue;
                    }

                    try
                    {
                        var describePath = _options.DescribeEndpointPattern.Replace("{resource}", resource);
                        var describeResult = await _proxy.InvokeAsync(context, describePath, "GET").ConfigureAwait(false);

                        if (describeResult.Value<bool?>("success") == true)
                        {
                            var describeData = describeResult["data"];
                            op["liveSchema"] = describeData;
                            op["liveSchemaSource"] = "live";

                            _describeCache[describeCacheKey] = new CacheEntry
                            {
                                Result = (describeData as JObject)?.DeepClone() as JObject ?? new JObject(),
                                Expiry = DateTime.UtcNow.AddMinutes(_options.DescribeCacheTTL)
                            };
                        }
                    }
                    catch { }
                }
            }
        }

        return result;
    }

    private static string ExtractResourceFromEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        var segments = endpoint.Trim('/').Split('/');
        foreach (var seg in segments)
        {
            if (!seg.StartsWith("{") && !string.IsNullOrWhiteSpace(seg))
                return seg;
        }
        return null;
    }
}

// ── MissionControl (Registration) ────────────────────────────────────────────

public static class MissionControl
{
    public static void RegisterMission(
        McpRequestHandler handler,
        MissionControlOptions options,
        string capabilityIndexJson,
        ScriptBase context)
    {
        var index = !string.IsNullOrWhiteSpace(capabilityIndexJson)
            ? new CapabilityIndex(capabilityIndexJson)
            : null;

        var proxy = new ApiProxy(options);
        var chainClient = options.DiscoveryMode == DiscoveryMode.McpChain ? new McpChainClient() : null;
        var discovery = new DiscoveryEngine(options, index, proxy, chainClient);

        var serviceName = options.ServiceName ?? "api";

        // ── scan_{service} ────────────────────────────────────────────────

        var scanDescription = $"Scan for available {serviceName} operations matching your intent. " +
            $"Always call this before launch_{serviceName} to find the correct endpoint and required parameters. " +
            $"Returns operation summaries with endpoints, methods, and descriptions. " +
            $"Use include_schema=true to get full input parameter details for a specific operation.";

        handler.AddTool($"scan_{serviceName}", scanDescription,
            schemaConfig: s => s
                .String("query", "Natural language description of what you want to do (e.g., 'list reports', 'run DAX query', 'export report to PDF')", required: true)
                .String("domain", $"Filter by domain category: workspaces, reports, dashboards, datasets, apps, export", required: false)
                .Boolean("include_schema", "Set true to include full input parameter schemas in the results (costs more tokens)", required: false),
            handler: async (args, ct) =>
            {
                var query = args.Value<string>("query") ?? "";
                var domain = args.Value<string>("domain");
                var includeSchema = args.Value<bool?>("include_schema") ?? false;

                return await discovery.DiscoverAsync(context, query, domain, includeSchema).ConfigureAwait(false);
            },
            annotationsConfig: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── launch_{service} ─────────────────────────────────────────────

        var launchDescription = $"Launch a {serviceName} API operation. " +
            $"Use scan_{serviceName} first to find the correct endpoint. " +
            $"Replace any {{id}} placeholders in the endpoint with actual values.";

        handler.AddTool($"launch_{serviceName}", launchDescription,
            schemaConfig: s => s
                .String("endpoint", "API endpoint path (e.g., '/reports', '/datasets/abc123/executeQueries')", required: true)
                .String("method", "HTTP method", required: true, enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .Object("body", "Request body for POST/PATCH/PUT operations", nested => { }, required: false)
                .Object("query_params", "Query parameters as key-value pairs", nested => { }, required: false)
                .String("api_version", "API version override (optional)", required: false),
            handler: async (args, ct) =>
            {
                var endpoint = args.Value<string>("endpoint") ?? "";
                var method = args.Value<string>("method") ?? "GET";
                var body = args["body"] as JObject;
                var queryParams = args["query_params"] as JObject;
                var apiVersion = args.Value<string>("api_version");

                return await proxy.InvokeAsync(context, endpoint, method, body, queryParams, apiVersion, index).ConfigureAwait(false);
            });

        // ── sequence_{service} ───────────────────────────────────────────

        var batchDescription = $"Launch a sequence of multiple {serviceName} API operations in a single call. " +
            $"Maximum {options.MaxBatchSize} requests per sequence. " +
            $"Each request needs an id, endpoint, and method.";

        handler.AddTool($"sequence_{serviceName}", batchDescription,
            schemaConfig: s => s
                .Array("requests", "Array of API requests to execute",
                    itemSchema: new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["id"] = new JObject { ["type"] = "string", ["description"] = "Unique identifier for this request" },
                            ["endpoint"] = new JObject { ["type"] = "string", ["description"] = "API endpoint path" },
                            ["method"] = new JObject { ["type"] = "string", ["description"] = "HTTP method (GET, POST, PATCH, PUT, DELETE)" },
                            ["body"] = new JObject { ["type"] = "object", ["description"] = "Request body (for POST/PATCH/PUT)" },
                            ["query_params"] = new JObject { ["type"] = "object", ["description"] = "Query parameters" }
                        },
                        ["required"] = new JArray("endpoint", "method")
                    }, required: true)
                .String("api_version", "API version override (optional)", required: false),
            handler: async (args, ct) =>
            {
                var requests = args["requests"] as JArray;
                var apiVersion = args.Value<string>("api_version");

                return await proxy.BatchInvokeAsync(context, requests, apiVersion, index).ConfigureAwait(false);
            });
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject { ["type"] = "object", ["properties"] = _properties };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

// ── Internal Registration Classes ────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────

public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    public McpRequestHandler AddTool(
        string name, string description,
        Action<McpSchemaBuilder> schemaConfig,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotationsConfig = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        JObject outputSchema = null;
        if (outputSchemaConfig != null) { var ob = new McpSchemaBuilder(); outputSchemaConfig(ob); outputSchema = ob.Build(); }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotations,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };
        return this;
    }

    public McpRequestHandler AddResource(
        string uri, string name, string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        _resources[uri] = new McpResourceDefinition
        {
            Uri = uri, Name = name, Description = description,
            MimeType = mimeType, Annotations = annotations, Handler = handler
        };
        return this;
    }

    public McpRequestHandler AddResourceTemplate(
        string uriTemplate, string name, string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null) { annotations = new JObject(); annotationsConfig(annotations); }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate, Name = name, Description = description,
            MimeType = mimeType, Annotations = annotations, Handler = handler
        });
        return this;
    }

    public McpRequestHandler AddPrompt(
        string name, string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        _prompts[name] = new McpPromptDefinition
        {
            Name = name, Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };
        return this;
    }

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try { request = JObject.Parse(body); }
        catch (JsonException) { return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON"); }

        var method = request.Value<string>("method") ?? "";
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize": return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping": return SerializeSuccess(id, new JObject());

                case "tools/list": return HandleToolsList(id);
                case "tools/call": return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list": return HandleResourcesList(id);
                case "resources/templates/list": return HandleResourceTemplatesList(id);
                case "resources/read": return await HandleResourcesReadAsync(id, request, cancellationToken).ConfigureAwait(false);
                case "resources/subscribe":
                case "resources/unsubscribe": return SerializeSuccess(id, new JObject());

                case "prompts/list": return HandlePromptsList(id);
                case "prompts/get": return await HandlePromptsGetAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false }
                    });

                case "logging/setLevel": return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex) { return SerializeError(id, ex.Code, ex.Message); }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
    }

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools) capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources) capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts) capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging) capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions) capabilities["completions"] = new JObject();

        var serverInfo = new JObject { ["name"] = _options.ServerInfo.Name, ["version"] = _options.ServerInfo.Version };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title)) serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description)) serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject { ["protocolVersion"] = clientProtocolVersion, ["capabilities"] = capabilities, ["serverInfo"] = serverInfo };
        if (!string.IsNullOrWhiteSpace(_options.Instructions)) result["instructions"] = _options.Instructions;

        Log("McpInitialized", new { Server = _options.ServerInfo.Name, Version = _options.ServerInfo.Version });
        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.InputSchema };
            if (!string.IsNullOrWhiteSpace(tool.Title)) toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null) toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0) toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }
        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private string HandleResourcesList(JToken id)
    {
        var arr = new JArray();
        foreach (var r in _resources.Values)
        {
            var o = new JObject { ["uri"] = r.Uri, ["name"] = r.Name };
            if (!string.IsNullOrWhiteSpace(r.Description)) o["description"] = r.Description;
            if (!string.IsNullOrWhiteSpace(r.MimeType)) o["mimeType"] = r.MimeType;
            if (r.Annotations != null && r.Annotations.Count > 0) o["annotations"] = r.Annotations;
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["resources"] = arr });
    }

    private string HandleResourceTemplatesList(JToken id)
    {
        var arr = new JArray();
        foreach (var t in _resourceTemplates)
        {
            var o = new JObject { ["uriTemplate"] = t.UriTemplate, ["name"] = t.Name };
            if (!string.IsNullOrWhiteSpace(t.Description)) o["description"] = t.Description;
            if (!string.IsNullOrWhiteSpace(t.MimeType)) o["mimeType"] = t.MimeType;
            if (t.Annotations != null && t.Annotations.Count > 0) o["annotations"] = t.Annotations;
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["resourceTemplates"] = arr });
    }

    private async Task<string> HandleResourcesReadAsync(JToken id, JObject request, CancellationToken ct)
    {
        var uri = (request["params"] as JObject)?.Value<string>("uri");
        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(id, McpErrorCode.InvalidParams, "Resource URI is required");

        if (_resources.TryGetValue(uri, out var resource))
        {
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                return SerializeSuccess(id, new JObject { ["contents"] = contents });
            }
            catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
        }

        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    return SerializeSuccess(id, new JObject { ["contents"] = contents });
                }
                catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
            }
        }

        return SerializeError(id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    private static bool MatchesUriTemplate(string template, string uri)
    {
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return false;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}")) continue;
            if (!string.Equals(tp[i], up[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tp = template.Split('/');
        var up = uri.Split('/');
        if (tp.Length != up.Length) return result;
        for (int i = 0; i < tp.Length; i++)
        {
            if (tp[i].StartsWith("{") && tp[i].EndsWith("}"))
                result[tp[i].Substring(1, tp[i].Length - 2)] = up[i];
        }
        return result;
    }

    private string HandlePromptsList(JToken id)
    {
        var arr = new JArray();
        foreach (var p in _prompts.Values)
        {
            var o = new JObject { ["name"] = p.Name };
            if (!string.IsNullOrWhiteSpace(p.Description)) o["description"] = p.Description;
            if (p.Arguments.Count > 0)
            {
                var args = new JArray();
                foreach (var a in p.Arguments)
                {
                    var ao = new JObject { ["name"] = a.Name };
                    if (!string.IsNullOrWhiteSpace(a.Description)) ao["description"] = a.Description;
                    if (a.Required) ao["required"] = true;
                    args.Add(ao);
                }
                o["arguments"] = args;
            }
            arr.Add(o);
        }
        return SerializeSuccess(id, new JObject { ["prompts"] = arr });
    }

    private async Task<string> HandlePromptsGetAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var name = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(name))
            return SerializeError(id, McpErrorCode.InvalidParams, "Prompt name is required");
        if (!_prompts.TryGetValue(name, out var prompt))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Prompt not found: {name}");

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description)) result["description"] = prompt.Description;
            return SerializeSuccess(id, result);
        }
        catch (Exception ex) { return SerializeError(id, McpErrorCode.InternalError, ex.Message); }
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");
        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject { ["content"] = contentArray, ["isError"] = jobj.Value<bool?>("isError") ?? false };
                if (jobj["structuredContent"] is JObject structured) callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject po) text = po.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s) text = s;
                else text = result == null ? "{}" : JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } }, ["isError"] = true });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } }, ["isError"] = true });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });
            return SerializeSuccess(id, new JObject
            { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" } }, ["isError"] = true });
        }
    }

    public static JObject TextContent(string text) => new JObject { ["type"] = "text", ["text"] = text };
    public static JObject ImageContent(string base64Data, string mimeType) => new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };
    public static JObject AudioContent(string base64Data, string mimeType) => new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject { ["type"] = "resource", ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType } };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    private string SerializeSuccess(JToken id, JObject result) =>
        new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Newtonsoft.Json.Formatting.None);

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null) =>
        SerializeError(id, (int)code, message, data);

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;
        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data) => OnLog?.Invoke(eventName, data);
}

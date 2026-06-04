using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // MCP server metadata
    private const string ServerName = "jira-mcp";
    private const string ServerVersion = "1.3.0";
    private const string ServerTitle = "Jira MCP";
    private const string ServerDescription = "Jira Cloud MCP tools for issues, comments, worklogs, watchers, votes, links, attachments, filters, projects, versions, components, users, boards, sprints, and epics.";
    private const string ProtocolVersion = "2025-11-25";

    // Atlassian OAuth 2.0 (3LO) routes API calls through api.atlassian.com using a per-site cloudId.
    private const string AtlassianApiBase = "https://api.atlassian.com";
    private const string AccessibleResourcesUrl = "https://api.atlassian.com/oauth/token/accessible-resources";
    private const string PlatformApiPrefix = "/rest/api/3";
    private const string AgileApiPrefix = "/rest/agile/1.0";
    private const int AttachmentMaxBytes = 10 * 1024 * 1024; // 10 MB practical ceiling for base64 envelope uploads
    private string _cachedCloudId;

    /// <summary>
    /// Entry point for connector operations.
    /// </summary>
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    return await HandleMcpAsync(correlationId, startTime).ConfigureAwait(false);

                case "ListProjects":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/project", null, true).ConfigureAwait(false);

                case "SearchIssues":
                    return await SearchIssuesProxyAsync(correlationId).ConfigureAwait(false);

                case "GetIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath(), null, true).ConfigureAwait(false);

                case "CreateIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/issue", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "UpdateIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildIssuePath(), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "ListFields":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/field", null, true).ConfigureAwait(false);

                case "GetUser":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/user", null, true).ConfigureAwait(false);

                case "SearchUsers":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/user/search", null, true).ConfigureAwait(false);

                case "ListIssueTypes":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/issuetype", null, true).ConfigureAwait(false);

                case "GetProjectRoles":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildProjectPath("/role"), null, true).ConfigureAwait(false);

                case "GetProjectStatuses":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildProjectPath("/statuses"), null, true).ConfigureAwait(false);

                case "ListComments":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath("/comment"), null, true).ConfigureAwait(false);

                case "AddComment":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildIssuePath("/comment"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetTransitions":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath("/transitions"), null, true).ConfigureAwait(false);

                case "TransitionIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildIssuePath("/transitions"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "ListFilters":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/filter/search", null, true).ConfigureAwait(false);

                case "ListUsersByProject":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/user/assignable/search", null, true).ConfigureAwait(false);

                case "GetTask":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildTaskPath(), null, true).ConfigureAwait(false);

                case "CancelTask":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildTaskPath("/cancel"), null, false).ConfigureAwait(false);

                case "ListAccessibleResources":
                    return await ProxyAccessibleResourcesAsync(correlationId).ConfigureAwait(false);

                // === Phase B: Issues + engagement ===

                case "DeleteIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildIssuePath(), null, true).ConfigureAwait(false);

                case "AssignIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildIssuePath("/assignee"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetEditIssueMeta":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath("/editmeta"), null, true).ConfigureAwait(false);

                case "NotifyIssue":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildIssuePath("/notify"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "BulkCreateIssues":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/issue/bulk", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "BulkFetchIssues":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/issue/bulkfetch", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "ArchiveIssues":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, "/rest/api/3/issue/archive", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "UnarchiveIssues":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, "/rest/api/3/issue/unarchive", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetComment":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssueSubResourcePath("/comment/"), null, true).ConfigureAwait(false);

                case "UpdateComment":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildIssueSubResourcePath("/comment/"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "DeleteComment":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildIssueSubResourcePath("/comment/"), null, true).ConfigureAwait(false);

                case "GetIssueWatchers":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath("/watchers"), null, true).ConfigureAwait(false);

                case "AddWatcher":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildIssuePath("/watchers"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "RemoveWatcher":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildIssuePath("/watchers"), null, true).ConfigureAwait(false);

                case "GetVotes":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath("/votes"), null, true).ConfigureAwait(false);

                case "AddVote":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildIssuePath("/votes"), null, false).ConfigureAwait(false);

                case "RemoveVote":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildIssuePath("/votes"), null, false).ConfigureAwait(false);

                case "LinkIssues":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/issueLink", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetIssueLink":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssueLinkPath(), null, true).ConfigureAwait(false);

                case "DeleteIssueLink":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildIssueLinkPath(), null, false).ConfigureAwait(false);

                case "GetIssueLinkTypes":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/issueLinkType", null, true).ConfigureAwait(false);

                case "AddWorklog":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, BuildIssuePath("/worklog"), await ReadBodyAsync(), true).ConfigureAwait(false);

                case "GetIssueWorklogs":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildIssuePath("/worklog"), null, true).ConfigureAwait(false);

                case "UpdateWorklog":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildIssueSubResourcePath("/worklog/"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "DeleteWorklog":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildIssueSubResourcePath("/worklog/"), null, false).ConfigureAwait(false);

                // === Phase B: Filters ===

                case "CreateFilter":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/filter", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetFilter":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildFilterPath(), null, true).ConfigureAwait(false);

                case "UpdateFilter":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildFilterPath(), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "DeleteFilter":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildFilterPath(), null, false).ConfigureAwait(false);

                case "GetMyFilters":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/filter/my", null, true).ConfigureAwait(false);

                case "GetFavouriteFilters":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/filter/favourite", null, true).ConfigureAwait(false);

                case "SetFilterFavourite":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildFilterPath("/favourite"), null, false).ConfigureAwait(false);

                case "DeleteFilterFavourite":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildFilterPath("/favourite"), null, false).ConfigureAwait(false);

                // === Phase B: Projects + versions + components + groups + statuses ===

                case "GetProject":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildProjectPath(null), null, true).ConfigureAwait(false);

                case "SearchProjects":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/project/search", null, true).ConfigureAwait(false);

                case "GetProjectVersions":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildProjectPath("/versions"), null, true).ConfigureAwait(false);

                case "CreateVersion":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/version", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetVersion":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildVersionPath(), null, true).ConfigureAwait(false);

                case "UpdateVersion":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildVersionPath(), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "DeleteVersion":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildVersionPath(), null, false).ConfigureAwait(false);

                case "GetProjectComponents":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildProjectPath("/components"), null, true).ConfigureAwait(false);

                case "CreateComponent":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/component", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "GetComponent":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildComponentPath(), null, true).ConfigureAwait(false);

                case "UpdateComponent":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Put, BuildComponentPath(), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "DeleteComponent":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildComponentPath(), null, true).ConfigureAwait(false);

                case "FindGroups":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/groups/picker", null, true).ConfigureAwait(false);

                case "GetGroupMembers":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/group/member", null, true).ConfigureAwait(false);

                case "GetStatuses":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, "/rest/api/3/statuses", null, true).ConfigureAwait(false);

                // === Phase C: Attachments ===

                case "UploadAttachment":
                    return await ProxyAttachmentUploadAsync(correlationId, BuildIssuePath("/attachments"), await ReadBodyAsync()).ConfigureAwait(false);

                case "GetAttachmentMetadata":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Get, BuildAttachmentPath(), null, false).ConfigureAwait(false);

                case "DeleteAttachment":
                    return await ProxyJiraAsync(correlationId, HttpMethod.Delete, BuildAttachmentPath(), null, false).ConfigureAwait(false);

                // === Phase D: Agile ===

                case "ListBoards":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, "/board", null, true).ConfigureAwait(false);

                case "GetBoard":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath(), null, false).ConfigureAwait(false);

                case "GetBoardConfiguration":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath("/configuration"), null, false).ConfigureAwait(false);

                case "GetBoardIssues":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath("/issue"), null, true).ConfigureAwait(false);

                case "GetBoardBacklog":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath("/backlog"), null, true).ConfigureAwait(false);

                case "GetBoardProjects":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath("/project"), null, true).ConfigureAwait(false);

                case "ListBoardSprints":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath("/sprint"), null, true).ConfigureAwait(false);

                case "GetSprint":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildSprintPath(), null, false).ConfigureAwait(false);

                case "CreateSprint":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Post, "/sprint", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "UpdateSprint":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Post, BuildSprintPath(), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "DeleteSprint":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Delete, BuildSprintPath(), null, false).ConfigureAwait(false);

                case "GetSprintIssues":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildSprintPath("/issue"), null, true).ConfigureAwait(false);

                case "MoveIssuesToSprint":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Post, BuildSprintPath("/issue"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "MoveIssuesToBacklog":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Post, "/backlog/issue", await ReadBodyAsync(), false).ConfigureAwait(false);

                case "ListBoardEpics":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildBoardPath("/epic"), null, true).ConfigureAwait(false);

                case "GetEpic":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildEpicPath(), null, false).ConfigureAwait(false);

                case "GetEpicIssues":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Get, BuildEpicPath("/issue"), null, true).ConfigureAwait(false);

                case "MoveIssuesToEpic":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Post, BuildEpicPath("/issue"), await ReadBodyAsync(), false).ConfigureAwait(false);

                case "RemoveIssuesFromEpic":
                    return await ProxyAgileAsync(correlationId, HttpMethod.Post, "/epic/none/issue", await ReadBodyAsync(), false).ConfigureAwait(false);

                default:
                    return CreateErrorResponse($"Unknown operation: {this.Context.OperationId}", HttpStatusCode.BadRequest);
            }
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                Operation = this.Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
            return CreateErrorResponse(ex.Message, HttpStatusCode.InternalServerError);
        }
        finally
        {
            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                Operation = this.Context.OperationId,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
        }
    }

    // ========================================
    // MCP HANDLERS
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId, DateTime startTime)
    {
        string body;
        try
        {
            body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Unable to read request body");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return CreateJsonRpcErrorResponse(null, -32600, "Invalid Request", "Empty request body");
        }

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error", "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var requestId = request["id"];

        await LogToAppInsights("McpRequestReceived", new
        {
            CorrelationId = correlationId,
            Method = method
        });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(request, requestId);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleToolsList(requestId);

            case "tools/call":
                return await HandleToolsCallAsync(correlationId, request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject request, JToken requestId)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = ServerTitle,
                ["description"] = ServerDescription
            }
        };

        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // Discovery
            ToolListProjects(),
            ToolListIssueTypes(),
            ToolListProjectRoles(),
            ToolListProjectStatuses(),
            ToolListFilters(),
            ToolListUsersByProject(),
            ToolListAccessibleResources(),
            ToolSearchUsers(),
            ToolGetIssueLinkTypes(),
            ToolSearchProjects(),
            ToolGetProject(),
            ToolGetProjectVersions(),
            ToolGetProjectComponents(),

            // Issues (core)
            ToolSearchIssues(),
            ToolGetIssue(),
            ToolCreateIssue(),
            ToolCreateIssueSimple(),
            ToolUpdateIssue(),
            ToolDeleteIssue(),
            ToolAssignIssue(),
            ToolBulkCreateIssues(),
            ToolBulkFetchIssues(),

            // Comments / transitions
            ToolListComments(),
            ToolAddComment(),
            ToolGetTransitions(),
            ToolTransitionIssue(),

            // Engagement
            ToolGetIssueWatchers(),
            ToolAddWatcher(),
            ToolRemoveWatcher(),
            ToolLinkIssues(),
            ToolAddWorklog(),
            ToolGetIssueWorklogs(),
            ToolUploadAttachment(),

            // Filters (CRUD)
            ToolGetFilter(),
            ToolCreateFilter(),
            ToolUpdateFilter(),
            ToolDeleteFilter(),

            // Project versions / components write
            ToolCreateVersion(),
            ToolCreateComponent(),

            // Async tasks
            ToolGetTask(),
            ToolCancelTask(),

            // Agile (boards / sprints / epics)
            ToolListBoards(),
            ToolGetBoardIssues(),
            ToolGetBoardBacklog(),
            ToolListBoardSprints(),
            ToolListBoardEpics(),
            ToolCreateSprint(),
            ToolUpdateSprint(),
            ToolGetSprintIssues(),
            ToolMoveIssuesToSprint()
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(string correlationId, JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        if (paramsObj == null)
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "params object is required");
        }

        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateJsonRpcErrorResponse(requestId, -32602, "Invalid params", "Tool name is required");
        }

        await LogToAppInsights("McpToolCallStarted", new
        {
            CorrelationId = correlationId,
            ToolName = toolName
        });

        try
        {
            var toolResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);

            await LogToAppInsights("McpToolCallCompleted", new
            {
                CorrelationId = correlationId,
                ToolName = toolName,
                IsError = false
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (ArgumentException ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Invalid arguments: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("McpToolCallError", new
            {
                CorrelationId = correlationId,
                ToolName = toolName,
                ErrorMessage = ex.Message
            });

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Tool execution failed: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject arguments)
    {
        switch (toolName)
        {
            // === Discovery ===
            case "jira_list_projects":
                return await ExecuteJiraToolAsync(HttpMethod.Get, "/rest/api/3/project", null, true).ConfigureAwait(false);

            case "jira_list_issue_types":
                return await ExecuteJiraToolAsync(HttpMethod.Get, "/rest/api/3/issuetype", null, true).ConfigureAwait(false);

            case "jira_list_project_roles":
                var projectId = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projectId)}/role", null, true).ConfigureAwait(false);

            case "jira_list_project_statuses":
                var projectKey = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projectKey)}/statuses", null, true).ConfigureAwait(false);

            case "jira_list_filters":
                return await ExecuteListFiltersToolAsync(arguments).ConfigureAwait(false);

            case "jira_list_users_by_project":
                return await ExecuteListUsersByProjectToolAsync(arguments).ConfigureAwait(false);

            case "jira_list_accessible_resources":
                return await ExecuteListAccessibleResourcesToolAsync().ConfigureAwait(false);

            case "jira_search_users":
                return await ExecuteSearchUsersToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_issue_link_types":
                return await ExecuteJiraToolAsync(HttpMethod.Get, "/rest/api/3/issueLinkType", null, false).ConfigureAwait(false);

            case "jira_search_projects":
                return await ExecuteSearchProjectsToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_project":
                var projGet = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projGet)}", null, true).ConfigureAwait(false);

            case "jira_get_project_versions":
                var projVer = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projVer)}/versions", null, true).ConfigureAwait(false);

            case "jira_get_project_components":
                var projComp = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projComp)}/components", null, true).ConfigureAwait(false);

            // === Issues (core) ===
            case "jira_search_issues":
                return await ExecuteSearchIssuesToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_issue":
                var issueId = RequireArgument(arguments, "issueIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}", null, true).ConfigureAwait(false);

            case "jira_create_issue":
                return await ExecuteCreateIssueToolAsync(arguments).ConfigureAwait(false);

            case "jira_create_issue_simple":
                return await ExecuteCreateIssueSimpleToolAsync(arguments).ConfigureAwait(false);

            case "jira_update_issue":
                return await ExecuteUpdateIssueToolAsync(arguments).ConfigureAwait(false);

            case "jira_delete_issue":
                var issueDel = RequireArgument(arguments, "issueIdOrKey");
                var delPath = $"/rest/api/3/issue/{Uri.EscapeDataString(issueDel)}";
                if (arguments["deleteSubtasks"] != null) delPath += "?deleteSubtasks=" + Uri.EscapeDataString(arguments["deleteSubtasks"].ToString().ToLowerInvariant());
                return await ExecuteJiraToolAsync(HttpMethod.Delete, delPath, null, false).ConfigureAwait(false);

            case "jira_assign_issue":
                return await ExecuteAssignIssueToolAsync(arguments).ConfigureAwait(false);

            case "jira_bulk_create_issues":
                return await ExecuteBulkCreateIssuesToolAsync(arguments).ConfigureAwait(false);

            case "jira_bulk_fetch_issues":
                return await ExecuteBulkFetchIssuesToolAsync(arguments).ConfigureAwait(false);

            // === Comments / transitions ===
            case "jira_list_comments":
                return await ExecuteListCommentsToolAsync(arguments).ConfigureAwait(false);

            case "jira_add_comment":
                return await ExecuteAddCommentToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_transitions":
                return await ExecuteGetTransitionsToolAsync(arguments).ConfigureAwait(false);

            case "jira_transition_issue":
                return await ExecuteTransitionIssueToolAsync(arguments).ConfigureAwait(false);

            // === Engagement ===
            case "jira_get_watchers":
                var issueW = RequireArgument(arguments, "issueIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/issue/{Uri.EscapeDataString(issueW)}/watchers", null, false).ConfigureAwait(false);

            case "jira_add_watcher":
                return await ExecuteAddWatcherToolAsync(arguments).ConfigureAwait(false);

            case "jira_remove_watcher":
                return await ExecuteRemoveWatcherToolAsync(arguments).ConfigureAwait(false);

            case "jira_link_issues":
                return await ExecuteLinkIssuesToolAsync(arguments).ConfigureAwait(false);

            case "jira_add_worklog":
                return await ExecuteAddWorklogToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_issue_worklogs":
                var issueWl = RequireArgument(arguments, "issueIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/issue/{Uri.EscapeDataString(issueWl)}/worklog", null, true).ConfigureAwait(false);

            case "jira_upload_attachment":
                return await ExecuteUploadAttachmentToolAsync(arguments).ConfigureAwait(false);

            // === Filters CRUD ===
            case "jira_get_filter":
                var fid = RequireArgument(arguments, "id");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/filter/{Uri.EscapeDataString(fid)}", null, false).ConfigureAwait(false);

            case "jira_create_filter":
                return await ExecuteCreateFilterToolAsync(arguments).ConfigureAwait(false);

            case "jira_update_filter":
                return await ExecuteUpdateFilterToolAsync(arguments).ConfigureAwait(false);

            case "jira_delete_filter":
                var fidDel = RequireArgument(arguments, "id");
                return await ExecuteJiraToolAsync(HttpMethod.Delete, $"/rest/api/3/filter/{Uri.EscapeDataString(fidDel)}", null, false).ConfigureAwait(false);

            // === Versions / components ===
            case "jira_create_version":
                return await ExecuteCreateVersionToolAsync(arguments).ConfigureAwait(false);

            case "jira_create_component":
                return await ExecuteCreateComponentToolAsync(arguments).ConfigureAwait(false);

            // === Async tasks ===
            case "jira_get_task":
                return await ExecuteGetTaskToolAsync(arguments).ConfigureAwait(false);

            case "jira_cancel_task":
                return await ExecuteCancelTaskToolAsync(arguments).ConfigureAwait(false);

            // === Agile ===
            case "jira_list_boards":
                return await ExecuteListBoardsToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_board_issues":
                return await ExecuteGetBoardIssuesToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_board_backlog":
                return await ExecuteGetBoardBacklogToolAsync(arguments).ConfigureAwait(false);

            case "jira_list_board_sprints":
                return await ExecuteListBoardSprintsToolAsync(arguments).ConfigureAwait(false);

            case "jira_list_board_epics":
                return await ExecuteListBoardEpicsToolAsync(arguments).ConfigureAwait(false);

            case "jira_create_sprint":
                return await ExecuteCreateSprintToolAsync(arguments).ConfigureAwait(false);

            case "jira_update_sprint":
                return await ExecuteUpdateSprintToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_sprint_issues":
                return await ExecuteGetSprintIssuesToolAsync(arguments).ConfigureAwait(false);

            case "jira_move_issues_to_sprint":
                return await ExecuteMoveIssuesToSprintToolAsync(arguments).ConfigureAwait(false);

            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    // ========================================
    // TOOL DEFINITIONS
    // ========================================

    private JObject ToolListProjects()
    {
        return new JObject
        {
            ["name"] = "jira_list_projects",
            ["description"] = "List Jira projects visible to the user.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        };
    }

    private JObject ToolListIssueTypes()
    {
        return new JObject
        {
            ["name"] = "jira_list_issue_types",
            ["description"] = "List Jira issue types visible to the user.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        };
    }

    private JObject ToolListProjectRoles()
    {
        return new JObject
        {
            ["name"] = "jira_list_project_roles",
            ["description"] = DescribeWithDeps("List roles for a Jira project.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Project ID or key"
                    }
                },
                ["required"] = new JArray { "projectIdOrKey" }
            }
        };
    }

    private JObject ToolListProjectStatuses()
    {
        return new JObject
        {
            ["name"] = "jira_list_project_statuses",
            ["description"] = DescribeWithDeps("List statuses for a Jira project.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Project ID or key"
                    }
                },
                ["required"] = new JArray { "projectIdOrKey" }
            }
        };
    }

    private JObject ToolSearchIssues()
    {
        return new JObject
        {
            ["name"] = "jira_search_issues",
            ["description"] = "Search Jira issues using JQL (enhanced search /rest/api/3/search/jql). Returns one page (~50 issues) by default. Set `limit` to fetch multiple pages automatically, or pass `nextPageToken` from a prior response to resume.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["jql"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "JQL query string"
                    },
                    ["nextPageToken"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Cursor from a previous response. Use to resume paging when `limit` is not set."
                    },
                    ["maxResults"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Items per Jira page (1-100)."
                    },
                    ["limit"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Optional total cap. When set, the tool auto-pages until this many records are collected."
                    },
                    ["fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "Field names to return"
                    }
                },
                ["required"] = new JArray { "jql" }
            }
        };
    }

    private JObject ToolGetIssue()
    {
        return new JObject
        {
            ["name"] = "jira_get_issue",
            ["description"] = DescribeWithDeps("Get a Jira issue by ID or key.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue ID or key"
                    }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolCreateIssue()
    {
        return new JObject
        {
            ["name"] = "jira_create_issue",
            ["description"] = DescribeWithDeps("Create a new Jira issue.", "jira_list_projects", "jira_list_issue_types"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["fields"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Issue fields payload"
                    }
                },
                ["required"] = new JArray { "fields" }
            }
        };
    }

    private JObject ToolCreateIssueSimple()
    {
        return new JObject
        {
            ["name"] = "jira_create_issue_simple",
            ["description"] = DescribeWithDeps("Create a Jira issue from common fields (project, issue type, summary, description).", "jira_list_projects", "jira_list_issue_types"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Project key (for example, ENG)"
                    },
                    ["issueType"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue type name (for example, Task)"
                    },
                    ["summary"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue summary"
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Plain-text description (converted to Jira document format)"
                    }
                },
                ["required"] = new JArray { "projectKey", "issueType", "summary" }
            }
        };
    }

    private JObject ToolUpdateIssue()
    {
        return new JObject
        {
            ["name"] = "jira_update_issue",
            ["description"] = DescribeWithDeps("Update an existing Jira issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue ID or key"
                    },
                    ["fields"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Issue fields payload"
                    },
                    ["update"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Issue update operations"
                    }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolListComments()
    {
        return new JObject
        {
            ["name"] = "jira_list_comments",
            ["description"] = DescribeWithDeps("List comments for an issue. Returns one page (~50 comments) by default. Set `limit` to auto-page through all comments.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue ID or key"
                    },
                    ["startAt"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Index of the first comment to return"
                    },
                    ["maxResults"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of comments per Jira page (1-100)."
                    },
                    ["limit"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Optional total cap. When set, the tool auto-pages until this many comments are collected."
                    }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolAddComment()
    {
        return new JObject
        {
            ["name"] = "jira_add_comment",
            ["description"] = DescribeWithDeps("Add a comment to an issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue ID or key"
                    },
                    ["body"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Plain-text comment (converted to Jira document format)"
                    }
                },
                ["required"] = new JArray { "issueIdOrKey", "body" }
            }
        };
    }

    private JObject ToolGetTransitions()
    {
        return new JObject
        {
            ["name"] = "jira_get_transitions",
            ["description"] = DescribeWithDeps("List available transitions for an issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue ID or key"
                    }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolTransitionIssue()
    {
        return new JObject
        {
            ["name"] = "jira_transition_issue",
            ["description"] = DescribeWithDeps("Transition an issue to a new status.", "jira_get_transitions"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Issue ID or key"
                    },
                    ["transitionId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Transition ID"
                    },
                    ["comment"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional comment for the transition"
                    }
                },
                ["required"] = new JArray { "issueIdOrKey", "transitionId" }
            }
        };
    }

    private JObject ToolListFilters()
    {
        return new JObject
        {
            ["name"] = "jira_list_filters",
            ["description"] = "Search Jira issue filters visible to the user.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["filterName"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Substring matched against filter name"
                    },
                    ["accountId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by owner Atlassian account ID"
                    },
                    ["groupId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by group ID shared with"
                    },
                    ["projectId"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Filter by project ID shared with"
                    },
                    ["orderBy"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Order results by field (e.g. name, -name, owner, favourite_count)"
                    },
                    ["startAt"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Index of the first item to return (zero-based)"
                    },
                    ["maxResults"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of filters to return"
                    },
                    ["expand"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Comma-separated list of properties to expand (e.g. description,owner,jql,sharePermissions)"
                    }
                },
                ["required"] = new JArray()
            }
        };
    }

    private JObject ToolListUsersByProject()
    {
        return new JObject
        {
            ["name"] = "jira_list_users_by_project",
            ["description"] = DescribeWithDeps("List users assignable to issues in the specified project.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["project"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Project key (for example, ENG)"
                    },
                    ["query"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Substring matched against display name and email address"
                    },
                    ["accountId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Filter by Atlassian account ID"
                    },
                    ["startAt"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Index of the first user to return"
                    },
                    ["maxResults"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of users to return (1-1000)"
                    },
                    ["actionDescriptorId"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Workflow transition ID used to limit users by transition permissions"
                    },
                    ["recommend"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Whether to use the recommendations service"
                    }
                },
                ["required"] = new JArray { "project" }
            }
        };
    }

    private JObject ToolGetTask()
    {
        return new JObject
        {
            ["name"] = "jira_get_task",
            ["description"] = "Get the status of a long-running asynchronous task.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["taskId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Task ID"
                    }
                },
                ["required"] = new JArray { "taskId" }
            }
        };
    }

    private JObject ToolCancelTask()
    {
        return new JObject
        {
            ["name"] = "jira_cancel_task",
            ["description"] = "Cancel a long-running asynchronous task that has not yet completed.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["taskId"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "Task ID"
                    }
                },
                ["required"] = new JArray { "taskId" }
            }
        };
    }

    private JObject ToolListAccessibleResources()
    {
        return new JObject
        {
            ["name"] = "jira_list_accessible_resources",
            ["description"] = "List the Atlassian sites (resources) the authenticated user has access to.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        };
    }

    // ========================================
    // TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteSearchIssuesToolAsync(JObject arguments)
    {
        var request = new JObject
        {
            ["jql"] = RequireArgument(arguments, "jql")
        };

        if (arguments["nextPageToken"] != null) request["nextPageToken"] = arguments["nextPageToken"];
        if (arguments["maxResults"] != null) request["maxResults"] = arguments["maxResults"];
        if (arguments["fields"] != null) request["fields"] = arguments["fields"];

        int? limit = arguments["limit"]?.Type == JTokenType.Integer ? (int?)arguments.Value<int>("limit") : null;
        return await SearchIssuesPagedAsync(request, limit).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateIssueToolAsync(JObject arguments)
    {
        var fields = arguments["fields"] as JObject;
        if (fields == null)
        {
            throw new ArgumentException("fields is required");
        }

        var request = new JObject { ["fields"] = fields };
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/issue", request, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateIssueToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var request = new JObject();

        if (arguments["fields"] != null) request["fields"] = arguments["fields"];
        if (arguments["update"] != null) request["update"] = arguments["update"];

        if (!request.Properties().Any())
        {
            throw new ArgumentException("fields or update is required");
        }

        return await ExecuteJiraToolAsync(HttpMethod.Put, $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}", request, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateIssueSimpleToolAsync(JObject arguments)
    {
        var projectKey = RequireArgument(arguments, "projectKey");
        var issueType = RequireArgument(arguments, "issueType");
        var summary = RequireArgument(arguments, "summary");
        var description = arguments["description"]?.ToString();

        var fields = new JObject
        {
            ["project"] = new JObject { ["key"] = projectKey },
            ["issuetype"] = new JObject { ["name"] = issueType },
            ["summary"] = summary
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            fields["description"] = BuildAdfText(description);
        }

        var request = new JObject { ["fields"] = fields };
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/issue", request, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListCommentsToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        int? startAt = arguments["startAt"]?.Type == JTokenType.Integer ? (int?)arguments.Value<int>("startAt") : null;
        int? maxResults = arguments["maxResults"]?.Type == JTokenType.Integer ? (int?)arguments.Value<int>("maxResults") : null;
        int? limit = arguments["limit"]?.Type == JTokenType.Integer ? (int?)arguments.Value<int>("limit") : null;

        return await ListCommentsPagedAsync(issueId, startAt, maxResults, limit).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAddCommentToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var body = RequireArgument(arguments, "body");

        var request = new JObject
        {
            ["body"] = BuildAdfText(body)
        };

        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/comment";
        return await ExecuteJiraToolAsync(HttpMethod.Post, path, request, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetTransitionsToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/transitions";
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, true).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteTransitionIssueToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var transitionId = RequireArgument(arguments, "transitionId");
        var comment = arguments["comment"]?.ToString();

        var request = new JObject
        {
            ["transition"] = new JObject { ["id"] = transitionId }
        };

        if (!string.IsNullOrWhiteSpace(comment))
        {
            request["update"] = new JObject
            {
                ["comment"] = new JArray
                {
                    new JObject
                    {
                        ["add"] = new JObject
                        {
                            ["body"] = BuildAdfText(comment)
                        }
                    }
                }
            };
        }

        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/transitions";
        return await ExecuteJiraToolAsync(HttpMethod.Post, path, request, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListFiltersToolAsync(JObject arguments)
    {
        var query = new Dictionary<string, object>
        {
            ["filterName"] = arguments["filterName"]?.ToString(),
            ["accountId"] = arguments["accountId"]?.ToString(),
            ["groupId"] = arguments["groupId"]?.ToString(),
            ["projectId"] = arguments["projectId"]?.ToString(),
            ["orderBy"] = arguments["orderBy"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString(),
            ["expand"] = arguments["expand"]?.ToString()
        };

        var path = "/rest/api/3/filter/search" + BuildQueryString(query);
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListUsersByProjectToolAsync(JObject arguments)
    {
        var project = RequireArgument(arguments, "project");
        var query = new Dictionary<string, object>
        {
            ["project"] = project,
            ["query"] = arguments["query"]?.ToString(),
            ["accountId"] = arguments["accountId"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString(),
            ["actionDescriptorId"] = arguments["actionDescriptorId"]?.ToString(),
            ["recommend"] = arguments["recommend"]?.ToString()
        };

        var path = "/rest/api/3/user/assignable/search" + BuildQueryString(query);
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetTaskToolAsync(JObject arguments)
    {
        var taskId = RequireArgument(arguments, "taskId");
        var path = $"/rest/api/3/task/{Uri.EscapeDataString(taskId)}";
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCancelTaskToolAsync(JObject arguments)
    {
        var taskId = RequireArgument(arguments, "taskId");
        var path = $"/rest/api/3/task/{Uri.EscapeDataString(taskId)}/cancel";
        return await ExecuteJiraToolAsync(HttpMethod.Post, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListAccessibleResourcesToolAsync()
    {
        var response = await SendAccessibleResourcesAsync().ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new JObject { ["statusCode"] = (int)response.StatusCode };
        }

        try
        {
            var token = JToken.Parse(payload);
            return new JObject { ["data"] = token };
        }
        catch
        {
            return new JObject
            {
                ["statusCode"] = (int)response.StatusCode,
                ["raw"] = payload
            };
        }
    }

    // ========================================
    // PHASE B/C/D — TOOL DEFINITIONS
    // ========================================

    private JObject ToolSearchUsers()
    {
        return new JObject
        {
            ["name"] = "jira_search_users",
            ["description"] = "Search Jira users by query string, account ID, or username.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Substring matched against display name and email" },
                    ["accountId"] = new JObject { ["type"] = "string", ["description"] = "Filter by Atlassian account ID" },
                    ["username"] = new JObject { ["type"] = "string", ["description"] = "Filter by username (where supported)" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of the first user to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Maximum users to return (1-1000)" }
                },
                ["required"] = new JArray()
            }
        };
    }

    private JObject ToolGetIssueLinkTypes()
    {
        return new JObject
        {
            ["name"] = "jira_get_issue_link_types",
            ["description"] = "List available issue link types (e.g. Blocks, Relates).",
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() }
        };
    }

    private JObject ToolSearchProjects()
    {
        return new JObject
        {
            ["name"] = "jira_search_projects",
            ["description"] = "Search Jira projects with paging.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Substring matched against project name and key" },
                    ["orderBy"] = new JObject { ["type"] = "string", ["description"] = "Order results (e.g. name, -name, key)" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first project to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max projects to return" }
                },
                ["required"] = new JArray()
            }
        };
    }

    private JObject ToolGetProject()
    {
        return new JObject
        {
            ["name"] = "jira_get_project",
            ["description"] = DescribeWithDeps("Get a Jira project by ID or key.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Project ID or key" }
                },
                ["required"] = new JArray { "projectIdOrKey" }
            }
        };
    }

    private JObject ToolGetProjectVersions()
    {
        return new JObject
        {
            ["name"] = "jira_get_project_versions",
            ["description"] = DescribeWithDeps("List versions defined on a Jira project.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Project ID or key" }
                },
                ["required"] = new JArray { "projectIdOrKey" }
            }
        };
    }

    private JObject ToolGetProjectComponents()
    {
        return new JObject
        {
            ["name"] = "jira_get_project_components",
            ["description"] = DescribeWithDeps("List components defined on a Jira project.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Project ID or key" }
                },
                ["required"] = new JArray { "projectIdOrKey" }
            }
        };
    }

    private JObject ToolDeleteIssue()
    {
        return new JObject
        {
            ["name"] = "jira_delete_issue",
            ["description"] = DescribeWithDeps("Permanently delete a Jira issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" },
                    ["deleteSubtasks"] = new JObject { ["type"] = "boolean", ["description"] = "Also delete subtasks (default false)" }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolAssignIssue()
    {
        return new JObject
        {
            ["name"] = "jira_assign_issue",
            ["description"] = DescribeWithDeps("Assign a Jira issue to a user. Use accountId '-1' for the project default assignee, or omit to unassign.", "jira_search_issues", "jira_search_users"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" },
                    ["accountId"] = new JObject { ["type"] = "string", ["description"] = "Assignee Atlassian account ID. Use '-1' for project default, or null to unassign." }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolBulkCreateIssues()
    {
        return new JObject
        {
            ["name"] = "jira_bulk_create_issues",
            ["description"] = DescribeWithDeps("Create multiple Jira issues in a single bulk request.", "jira_list_projects", "jira_list_issue_types"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueUpdates"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of issue create payloads, each shaped like { fields: { project: { key }, issuetype: { name }, summary } }",
                        ["items"] = new JObject { ["type"] = "object" }
                    }
                },
                ["required"] = new JArray { "issueUpdates" }
            }
        };
    }

    private JObject ToolBulkFetchIssues()
    {
        return new JObject
        {
            ["name"] = "jira_bulk_fetch_issues",
            ["description"] = DescribeWithDeps("Fetch up to 100 issues by IDs or keys in a single request.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdsOrKeys"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Issue IDs or keys (up to 100)",
                        ["items"] = new JObject { ["type"] = "string" }
                    },
                    ["fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Field names to return",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JArray { "issueIdsOrKeys" }
            }
        };
    }

    private JObject ToolGetIssueWatchers()
    {
        return new JObject
        {
            ["name"] = "jira_get_watchers",
            ["description"] = DescribeWithDeps("Get the watchers list for a Jira issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolAddWatcher()
    {
        return new JObject
        {
            ["name"] = "jira_add_watcher",
            ["description"] = DescribeWithDeps("Add a user as a watcher on a Jira issue.", "jira_search_issues", "jira_search_users"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" },
                    ["accountId"] = new JObject { ["type"] = "string", ["description"] = "Atlassian account ID of the user to add. If omitted, the calling user is added." }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolRemoveWatcher()
    {
        return new JObject
        {
            ["name"] = "jira_remove_watcher",
            ["description"] = DescribeWithDeps("Remove a user from the watchers list of a Jira issue.", "jira_search_issues", "jira_get_watchers"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" },
                    ["accountId"] = new JObject { ["type"] = "string", ["description"] = "Atlassian account ID of the user to remove" }
                },
                ["required"] = new JArray { "issueIdOrKey", "accountId" }
            }
        };
    }

    private JObject ToolLinkIssues()
    {
        return new JObject
        {
            ["name"] = "jira_link_issues",
            ["description"] = DescribeWithDeps("Create a link between two Jira issues (e.g. Blocks, Relates).", "jira_search_issues", "jira_get_issue_link_types"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["inwardIssueKey"] = new JObject { ["type"] = "string", ["description"] = "Inward issue key (e.g. 'ENG-1')" },
                    ["outwardIssueKey"] = new JObject { ["type"] = "string", ["description"] = "Outward issue key (e.g. 'ENG-2')" },
                    ["linkTypeName"] = new JObject { ["type"] = "string", ["description"] = "Link type name (e.g. 'Blocks', 'Relates')" }
                },
                ["required"] = new JArray { "inwardIssueKey", "outwardIssueKey", "linkTypeName" }
            }
        };
    }

    private JObject ToolAddWorklog()
    {
        return new JObject
        {
            ["name"] = "jira_add_worklog",
            ["description"] = DescribeWithDeps("Add a worklog entry to a Jira issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" },
                    ["timeSpent"] = new JObject { ["type"] = "string", ["description"] = "Time spent in Jira duration format (e.g. '3h 20m'). Either timeSpent or timeSpentSeconds is required." },
                    ["timeSpentSeconds"] = new JObject { ["type"] = "integer", ["description"] = "Time spent in seconds. Either timeSpent or timeSpentSeconds is required." },
                    ["started"] = new JObject { ["type"] = "string", ["description"] = "Work start timestamp in ISO 8601 (e.g. 2026-06-04T10:00:00.000+0000). Defaults to now." },
                    ["comment"] = new JObject { ["type"] = "string", ["description"] = "Optional plain-text worklog comment (converted to Jira document format)" }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolGetIssueWorklogs()
    {
        return new JObject
        {
            ["name"] = "jira_get_issue_worklogs",
            ["description"] = DescribeWithDeps("List worklog entries for a Jira issue.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" }
                },
                ["required"] = new JArray { "issueIdOrKey" }
            }
        };
    }

    private JObject ToolUploadAttachment()
    {
        return new JObject
        {
            ["name"] = "jira_upload_attachment",
            ["description"] = DescribeWithDeps("Upload an attachment to a Jira issue. Content must be base64-encoded and 10 MB or smaller.", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["issueIdOrKey"] = new JObject { ["type"] = "string", ["description"] = "Issue ID or key" },
                    ["filename"] = new JObject { ["type"] = "string", ["description"] = "File name (e.g. report.pdf)" },
                    ["contentBase64"] = new JObject { ["type"] = "string", ["description"] = "File content encoded as base64 (max 10 MB)" },
                    ["contentType"] = new JObject { ["type"] = "string", ["description"] = "MIME type (defaults to application/octet-stream)" }
                },
                ["required"] = new JArray { "issueIdOrKey", "filename", "contentBase64" }
            }
        };
    }

    private JObject ToolGetFilter()
    {
        return new JObject
        {
            ["name"] = "jira_get_filter",
            ["description"] = DescribeWithDeps("Get a Jira filter by ID.", "jira_list_filters"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Filter ID" }
                },
                ["required"] = new JArray { "id" }
            }
        };
    }

    private JObject ToolCreateFilter()
    {
        return new JObject
        {
            ["name"] = "jira_create_filter",
            ["description"] = "Create a new Jira filter with a name and JQL query.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Filter name" },
                    ["jql"] = new JObject { ["type"] = "string", ["description"] = "JQL query string" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Filter description" },
                    ["favourite"] = new JObject { ["type"] = "boolean", ["description"] = "Mark as favourite" }
                },
                ["required"] = new JArray { "name", "jql" }
            }
        };
    }

    private JObject ToolUpdateFilter()
    {
        return new JObject
        {
            ["name"] = "jira_update_filter",
            ["description"] = DescribeWithDeps("Update an existing Jira filter (name, JQL, description, or favourite flag).", "jira_list_filters"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Filter ID" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Filter name" },
                    ["jql"] = new JObject { ["type"] = "string", ["description"] = "JQL query string" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Filter description" },
                    ["favourite"] = new JObject { ["type"] = "boolean", ["description"] = "Mark as favourite" }
                },
                ["required"] = new JArray { "id" }
            }
        };
    }

    private JObject ToolDeleteFilter()
    {
        return new JObject
        {
            ["name"] = "jira_delete_filter",
            ["description"] = DescribeWithDeps("Delete a Jira filter by ID.", "jira_list_filters"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Filter ID" }
                },
                ["required"] = new JArray { "id" }
            }
        };
    }

    private JObject ToolCreateVersion()
    {
        return new JObject
        {
            ["name"] = "jira_create_version",
            ["description"] = DescribeWithDeps("Create a new project version (fix version / release).", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectId"] = new JObject { ["type"] = "integer", ["description"] = "Project ID this version belongs to" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Version name" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Version description" },
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Start date (YYYY-MM-DD)" },
                    ["releaseDate"] = new JObject { ["type"] = "string", ["description"] = "Release date (YYYY-MM-DD)" },
                    ["released"] = new JObject { ["type"] = "boolean", ["description"] = "Whether the version is released" },
                    ["archived"] = new JObject { ["type"] = "boolean", ["description"] = "Whether the version is archived" }
                },
                ["required"] = new JArray { "name", "projectId" }
            }
        };
    }

    private JObject ToolCreateComponent()
    {
        return new JObject
        {
            ["name"] = "jira_create_component",
            ["description"] = DescribeWithDeps("Create a new project component.", "jira_list_projects"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["project"] = new JObject { ["type"] = "string", ["description"] = "Project key (e.g. ENG)" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Component name" },
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "Component description" },
                    ["leadAccountId"] = new JObject { ["type"] = "string", ["description"] = "Lead user account ID" },
                    ["assigneeType"] = new JObject { ["type"] = "string", ["description"] = "PROJECT_DEFAULT | COMPONENT_LEAD | PROJECT_LEAD | UNASSIGNED" }
                },
                ["required"] = new JArray { "name", "project" }
            }
        };
    }

    private JObject ToolListBoards()
    {
        return new JObject
        {
            ["name"] = "jira_list_boards",
            ["description"] = "List Agile boards visible to the user.",
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["projectKeyOrId"] = new JObject { ["type"] = "string", ["description"] = "Filter by project key or ID" },
                    ["type"] = new JObject { ["type"] = "string", ["description"] = "Filter by type (scrum, kanban, simple)" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Substring matched against board name" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first board to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max boards to return" }
                },
                ["required"] = new JArray()
            }
        };
    }

    private JObject ToolGetBoardIssues()
    {
        return new JObject
        {
            ["name"] = "jira_get_board_issues",
            ["description"] = DescribeWithDeps("Get issues on an Agile board (optionally filtered by JQL).", "jira_list_boards"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["boardId"] = new JObject { ["type"] = "integer", ["description"] = "Board ID" },
                    ["jql"] = new JObject { ["type"] = "string", ["description"] = "Optional JQL filter" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first issue to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max issues to return" }
                },
                ["required"] = new JArray { "boardId" }
            }
        };
    }

    private JObject ToolGetBoardBacklog()
    {
        return new JObject
        {
            ["name"] = "jira_get_board_backlog",
            ["description"] = DescribeWithDeps("Get backlog issues for an Agile board.", "jira_list_boards"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["boardId"] = new JObject { ["type"] = "integer", ["description"] = "Board ID" },
                    ["jql"] = new JObject { ["type"] = "string", ["description"] = "Optional JQL filter" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first issue to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max issues to return" }
                },
                ["required"] = new JArray { "boardId" }
            }
        };
    }

    private JObject ToolListBoardSprints()
    {
        return new JObject
        {
            ["name"] = "jira_list_board_sprints",
            ["description"] = DescribeWithDeps("List sprints associated with a Scrum board.", "jira_list_boards"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["boardId"] = new JObject { ["type"] = "integer", ["description"] = "Board ID" },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "Filter by state (comma-separated: future, active, closed)" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first sprint to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max sprints to return" }
                },
                ["required"] = new JArray { "boardId" }
            }
        };
    }

    private JObject ToolListBoardEpics()
    {
        return new JObject
        {
            ["name"] = "jira_list_board_epics",
            ["description"] = DescribeWithDeps("List epics associated with a board.", "jira_list_boards"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["boardId"] = new JObject { ["type"] = "integer", ["description"] = "Board ID" },
                    ["done"] = new JObject { ["type"] = "boolean", ["description"] = "Filter by done status" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first epic to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max epics to return" }
                },
                ["required"] = new JArray { "boardId" }
            }
        };
    }

    private JObject ToolCreateSprint()
    {
        return new JObject
        {
            ["name"] = "jira_create_sprint",
            ["description"] = DescribeWithDeps("Create a new sprint on a Scrum board.", "jira_list_boards"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["originBoardId"] = new JObject { ["type"] = "integer", ["description"] = "Origin board ID (the Scrum board to create the sprint on)" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Sprint name" },
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Sprint start date (ISO 8601)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "Sprint end date (ISO 8601)" },
                    ["goal"] = new JObject { ["type"] = "string", ["description"] = "Sprint goal" }
                },
                ["required"] = new JArray { "name", "originBoardId" }
            }
        };
    }

    private JObject ToolUpdateSprint()
    {
        return new JObject
        {
            ["name"] = "jira_update_sprint",
            ["description"] = DescribeWithDeps("Update a sprint (name, state, dates, goal). Set state='active' to start, state='closed' to complete.", "jira_list_board_sprints"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sprintId"] = new JObject { ["type"] = "integer", ["description"] = "Sprint ID" },
                    ["name"] = new JObject { ["type"] = "string", ["description"] = "Sprint name" },
                    ["state"] = new JObject { ["type"] = "string", ["description"] = "Sprint state (future, active, closed)" },
                    ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Sprint start date (ISO 8601)" },
                    ["endDate"] = new JObject { ["type"] = "string", ["description"] = "Sprint end date (ISO 8601)" },
                    ["completeDate"] = new JObject { ["type"] = "string", ["description"] = "Sprint complete date (ISO 8601)" },
                    ["goal"] = new JObject { ["type"] = "string", ["description"] = "Sprint goal" }
                },
                ["required"] = new JArray { "sprintId" }
            }
        };
    }

    private JObject ToolGetSprintIssues()
    {
        return new JObject
        {
            ["name"] = "jira_get_sprint_issues",
            ["description"] = DescribeWithDeps("Get issues in a sprint.", "jira_list_board_sprints"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sprintId"] = new JObject { ["type"] = "integer", ["description"] = "Sprint ID" },
                    ["jql"] = new JObject { ["type"] = "string", ["description"] = "Optional JQL filter" },
                    ["startAt"] = new JObject { ["type"] = "integer", ["description"] = "Index of first issue to return" },
                    ["maxResults"] = new JObject { ["type"] = "integer", ["description"] = "Max issues to return" }
                },
                ["required"] = new JArray { "sprintId" }
            }
        };
    }

    private JObject ToolMoveIssuesToSprint()
    {
        return new JObject
        {
            ["name"] = "jira_move_issues_to_sprint",
            ["description"] = DescribeWithDeps("Move one or more issues into a sprint.", "jira_list_board_sprints", "jira_search_issues"),
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sprintId"] = new JObject { ["type"] = "integer", ["description"] = "Sprint ID" },
                    ["issues"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Issue keys or IDs to move",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JArray { "sprintId", "issues" }
            }
        };
    }

    // ========================================
    // PHASE B/C/D — TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteSearchUsersToolAsync(JObject arguments)
    {
        var query = new Dictionary<string, object>
        {
            ["query"] = arguments["query"]?.ToString(),
            ["accountId"] = arguments["accountId"]?.ToString(),
            ["username"] = arguments["username"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        var path = "/rest/api/3/user/search" + BuildQueryString(query);
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteSearchProjectsToolAsync(JObject arguments)
    {
        var query = new Dictionary<string, object>
        {
            ["query"] = arguments["query"]?.ToString(),
            ["orderBy"] = arguments["orderBy"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        var path = "/rest/api/3/project/search" + BuildQueryString(query);
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAssignIssueToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var body = new JObject();
        body["accountId"] = arguments["accountId"]?.ToString();
        return await ExecuteJiraToolAsync(HttpMethod.Put, $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/assignee", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteBulkCreateIssuesToolAsync(JObject arguments)
    {
        var updates = arguments["issueUpdates"] as JArray;
        if (updates == null || updates.Count == 0)
        {
            throw new ArgumentException("issueUpdates is required (array of issue create payloads)");
        }
        var body = new JObject { ["issueUpdates"] = updates };
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/issue/bulk", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteBulkFetchIssuesToolAsync(JObject arguments)
    {
        var keys = arguments["issueIdsOrKeys"] as JArray;
        if (keys == null || keys.Count == 0)
        {
            throw new ArgumentException("issueIdsOrKeys is required");
        }
        var body = new JObject { ["issueIdsOrKeys"] = keys };
        if (arguments["fields"] is JArray fields) body["fields"] = fields;
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/issue/bulkfetch", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAddWatcherToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/watchers";
        var accountId = arguments["accountId"]?.ToString();
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return await ExecuteJiraToolAsync(HttpMethod.Post, path, null, false).ConfigureAwait(false);
        }
        // Body must be the bare account ID as a JSON string
        var response = await ProxyJiraAsync(null, HttpMethod.Post, path,
            "\"" + accountId.Replace("\"", "\\\"") + "\"", false).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new JObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["body"] = TryParseToken(payload)
        };
    }

    private async Task<JObject> ExecuteRemoveWatcherToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var accountId = RequireArgument(arguments, "accountId");
        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/watchers?accountId={Uri.EscapeDataString(accountId)}";
        return await ExecuteJiraToolAsync(HttpMethod.Delete, path, null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteLinkIssuesToolAsync(JObject arguments)
    {
        var inward = RequireArgument(arguments, "inwardIssueKey");
        var outward = RequireArgument(arguments, "outwardIssueKey");
        var typeName = RequireArgument(arguments, "linkTypeName");
        var body = new JObject
        {
            ["type"] = new JObject { ["name"] = typeName },
            ["inwardIssue"] = new JObject { ["key"] = inward },
            ["outwardIssue"] = new JObject { ["key"] = outward }
        };
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/issueLink", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteAddWorklogToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var timeSpent = arguments["timeSpent"]?.ToString();
        var timeSpentSeconds = arguments["timeSpentSeconds"];
        if (string.IsNullOrWhiteSpace(timeSpent) && (timeSpentSeconds == null || timeSpentSeconds.Type == JTokenType.Null))
        {
            throw new ArgumentException("timeSpent or timeSpentSeconds is required");
        }
        var body = new JObject();
        if (!string.IsNullOrWhiteSpace(timeSpent)) body["timeSpent"] = timeSpent;
        if (timeSpentSeconds != null && timeSpentSeconds.Type != JTokenType.Null) body["timeSpentSeconds"] = timeSpentSeconds;
        var started = arguments["started"]?.ToString();
        if (!string.IsNullOrWhiteSpace(started)) body["started"] = started;
        var comment = arguments["comment"]?.ToString();
        if (!string.IsNullOrWhiteSpace(comment)) body["comment"] = BuildAdfText(comment);

        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/worklog";
        return await ExecuteJiraToolAsync(HttpMethod.Post, path, body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUploadAttachmentToolAsync(JObject arguments)
    {
        var issueId = RequireArgument(arguments, "issueIdOrKey");
        var filename = RequireArgument(arguments, "filename");
        var contentBase64 = RequireArgument(arguments, "contentBase64");
        var contentType = arguments["contentType"]?.ToString();
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("contentBase64 is not valid base64: " + ex.Message);
        }
        if (bytes.Length > AttachmentMaxBytes)
        {
            throw new ArgumentException($"Attachment exceeds {AttachmentMaxBytes / (1024 * 1024)} MB size limit.");
        }

        var cloudId = await GetCloudIdAsync().ConfigureAwait(false);
        var requestUri = new Uri($"{AtlassianApiBase}/ex/jira/{cloudId}/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/attachments");

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", filename);

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = multipart };
        request.Headers.Add("X-Atlassian-Token", "no-check");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authHeader.Scheme, authHeader.Parameter);
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new JObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["data"] = TryParseToken(payload)
        };
    }

    private async Task<JObject> ExecuteCreateFilterToolAsync(JObject arguments)
    {
        var body = new JObject
        {
            ["name"] = RequireArgument(arguments, "name"),
            ["jql"] = RequireArgument(arguments, "jql")
        };
        if (arguments["description"] != null) body["description"] = arguments["description"];
        if (arguments["favourite"] != null) body["favourite"] = arguments["favourite"];
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/filter", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateFilterToolAsync(JObject arguments)
    {
        var id = RequireArgument(arguments, "id");
        var body = new JObject();
        if (arguments["name"] != null) body["name"] = arguments["name"];
        if (arguments["jql"] != null) body["jql"] = arguments["jql"];
        if (arguments["description"] != null) body["description"] = arguments["description"];
        if (arguments["favourite"] != null) body["favourite"] = arguments["favourite"];
        if (!body.Properties().Any())
        {
            throw new ArgumentException("At least one of name, jql, description, or favourite must be provided");
        }
        return await ExecuteJiraToolAsync(HttpMethod.Put, $"/rest/api/3/filter/{Uri.EscapeDataString(id)}", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateVersionToolAsync(JObject arguments)
    {
        var body = new JObject
        {
            ["name"] = RequireArgument(arguments, "name")
        };
        var projectId = arguments["projectId"];
        if (projectId == null || projectId.Type == JTokenType.Null)
        {
            throw new ArgumentException("projectId is required");
        }
        body["projectId"] = projectId;
        if (arguments["description"] != null) body["description"] = arguments["description"];
        if (arguments["startDate"] != null) body["startDate"] = arguments["startDate"];
        if (arguments["releaseDate"] != null) body["releaseDate"] = arguments["releaseDate"];
        if (arguments["released"] != null) body["released"] = arguments["released"];
        if (arguments["archived"] != null) body["archived"] = arguments["archived"];
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/version", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateComponentToolAsync(JObject arguments)
    {
        var body = new JObject
        {
            ["name"] = RequireArgument(arguments, "name"),
            ["project"] = RequireArgument(arguments, "project")
        };
        if (arguments["description"] != null) body["description"] = arguments["description"];
        if (arguments["leadAccountId"] != null) body["leadAccountId"] = arguments["leadAccountId"];
        if (arguments["assigneeType"] != null) body["assigneeType"] = arguments["assigneeType"];
        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/component", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListBoardsToolAsync(JObject arguments)
    {
        var query = new Dictionary<string, object>
        {
            ["projectKeyOrId"] = arguments["projectKeyOrId"]?.ToString(),
            ["type"] = arguments["type"]?.ToString(),
            ["name"] = arguments["name"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        return await ExecuteAgileToolAsync(HttpMethod.Get, "/board" + BuildQueryString(query), null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBoardIssuesToolAsync(JObject arguments)
    {
        var boardId = RequireIntArgument(arguments, "boardId");
        var query = new Dictionary<string, object>
        {
            ["jql"] = arguments["jql"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        return await ExecuteAgileToolAsync(HttpMethod.Get, $"/board/{boardId}/issue" + BuildQueryString(query), null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBoardBacklogToolAsync(JObject arguments)
    {
        var boardId = RequireIntArgument(arguments, "boardId");
        var query = new Dictionary<string, object>
        {
            ["jql"] = arguments["jql"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        return await ExecuteAgileToolAsync(HttpMethod.Get, $"/board/{boardId}/backlog" + BuildQueryString(query), null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListBoardSprintsToolAsync(JObject arguments)
    {
        var boardId = RequireIntArgument(arguments, "boardId");
        var query = new Dictionary<string, object>
        {
            ["state"] = arguments["state"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        return await ExecuteAgileToolAsync(HttpMethod.Get, $"/board/{boardId}/sprint" + BuildQueryString(query), null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListBoardEpicsToolAsync(JObject arguments)
    {
        var boardId = RequireIntArgument(arguments, "boardId");
        var query = new Dictionary<string, object>
        {
            ["done"] = arguments["done"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        return await ExecuteAgileToolAsync(HttpMethod.Get, $"/board/{boardId}/epic" + BuildQueryString(query), null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateSprintToolAsync(JObject arguments)
    {
        var body = new JObject
        {
            ["name"] = RequireArgument(arguments, "name"),
            ["originBoardId"] = RequireIntArgument(arguments, "originBoardId")
        };
        if (arguments["startDate"] != null) body["startDate"] = arguments["startDate"];
        if (arguments["endDate"] != null) body["endDate"] = arguments["endDate"];
        if (arguments["goal"] != null) body["goal"] = arguments["goal"];
        return await ExecuteAgileToolAsync(HttpMethod.Post, "/sprint", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateSprintToolAsync(JObject arguments)
    {
        var sprintId = RequireIntArgument(arguments, "sprintId");
        var body = new JObject();
        if (arguments["name"] != null) body["name"] = arguments["name"];
        if (arguments["state"] != null) body["state"] = arguments["state"];
        if (arguments["startDate"] != null) body["startDate"] = arguments["startDate"];
        if (arguments["endDate"] != null) body["endDate"] = arguments["endDate"];
        if (arguments["completeDate"] != null) body["completeDate"] = arguments["completeDate"];
        if (arguments["goal"] != null) body["goal"] = arguments["goal"];
        if (!body.Properties().Any())
        {
            throw new ArgumentException("At least one updatable field must be provided");
        }
        return await ExecuteAgileToolAsync(HttpMethod.Post, $"/sprint/{sprintId}", body, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetSprintIssuesToolAsync(JObject arguments)
    {
        var sprintId = RequireIntArgument(arguments, "sprintId");
        var query = new Dictionary<string, object>
        {
            ["jql"] = arguments["jql"]?.ToString(),
            ["startAt"] = arguments["startAt"]?.ToString(),
            ["maxResults"] = arguments["maxResults"]?.ToString()
        };
        return await ExecuteAgileToolAsync(HttpMethod.Get, $"/sprint/{sprintId}/issue" + BuildQueryString(query), null, false).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteMoveIssuesToSprintToolAsync(JObject arguments)
    {
        var sprintId = RequireIntArgument(arguments, "sprintId");
        var issues = arguments["issues"] as JArray;
        if (issues == null || issues.Count == 0)
        {
            throw new ArgumentException("issues array is required");
        }
        var body = new JObject { ["issues"] = issues };
        return await ExecuteAgileToolAsync(HttpMethod.Post, $"/sprint/{sprintId}/issue", body, false).ConfigureAwait(false);
    }

    private long RequireIntArgument(JObject arguments, string name)
    {
        var token = arguments[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            throw new ArgumentException($"{name} is required");
        }
        if (token.Type == JTokenType.Integer)
        {
            return token.Value<long>();
        }
        if (long.TryParse(token.ToString(), out var parsed))
        {
            return parsed;
        }
        throw new ArgumentException($"{name} must be an integer");
    }

    private async Task<HttpResponseMessage> SearchIssuesProxyAsync(string correlationId)
    {
        var bodyText = await ReadBodyAsync().ConfigureAwait(false);
        JObject request;
        try
        {
            request = string.IsNullOrWhiteSpace(bodyText) ? new JObject() : JObject.Parse(bodyText);
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse("Invalid JSON body: " + ex.Message, HttpStatusCode.BadRequest);
        }

        int? limit = null;
        if (request["limit"] != null && request["limit"].Type == JTokenType.Integer)
        {
            limit = request.Value<int>("limit");
        }
        request.Remove("limit");

        var aggregated = await SearchIssuesPagedAsync(request, limit).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(aggregated.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task<JObject> SearchIssuesPagedAsync(JObject userRequest, int? limit)
    {
        var aggregated = new JArray();
        var working = (JObject)userRequest.DeepClone();
        working.Remove("limit");

        int pageSize = working["maxResults"]?.Type == JTokenType.Integer ? working.Value<int>("maxResults") : 100;
        if (pageSize <= 0) pageSize = 100;
        if (pageSize > 100) pageSize = 100;

        string nextPageToken = working.Value<string>("nextPageToken");
        bool isLast = true;

        while (true)
        {
            if (string.IsNullOrEmpty(nextPageToken)) working.Remove("nextPageToken");
            else working["nextPageToken"] = nextPageToken;

            int requestSize = pageSize;
            if (limit.HasValue)
            {
                var remaining = limit.Value - aggregated.Count;
                if (remaining <= 0) break;
                if (remaining < requestSize) requestSize = remaining;
            }
            working["maxResults"] = requestSize;

            var response = await ProxyJiraAsync(null, HttpMethod.Post, "/rest/api/3/search/jql",
                working.ToString(Newtonsoft.Json.Formatting.None), false).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new JObject
                {
                    ["error"] = true,
                    ["statusCode"] = (int)response.StatusCode,
                    ["body"] = TryParseToken(payload),
                    ["fetched"] = aggregated.Count,
                    ["issues"] = aggregated
                };
            }

            JObject page;
            try
            {
                page = string.IsNullOrWhiteSpace(payload) ? new JObject() : JObject.Parse(payload);
            }
            catch
            {
                page = new JObject();
            }

            var pageIssues = page["issues"] as JArray ?? new JArray();
            foreach (var issue in pageIssues) aggregated.Add(issue);

            isLast = page.Value<bool?>("isLast") ?? true;
            nextPageToken = page.Value<string>("nextPageToken");

            if (!limit.HasValue) break;
            if (isLast || string.IsNullOrEmpty(nextPageToken)) break;
            if (aggregated.Count >= limit.Value) break;
            if (pageIssues.Count == 0) break;
        }

        return new JObject
        {
            ["isLast"] = isLast || string.IsNullOrEmpty(nextPageToken),
            ["nextPageToken"] = nextPageToken,
            ["fetched"] = aggregated.Count,
            ["issues"] = aggregated
        };
    }

    private async Task<JObject> ListCommentsPagedAsync(string issueIdOrKey, int? startAt, int? maxResults, int? limit)
    {
        var aggregated = new JArray();
        int initialStart = startAt ?? 0;
        int currentStart = initialStart;
        int pageSize = maxResults ?? 100;
        if (pageSize <= 0) pageSize = 100;
        if (pageSize > 100) pageSize = 100;
        int total = -1;
        int lastMax = pageSize;

        while (true)
        {
            int requestSize = pageSize;
            if (limit.HasValue)
            {
                var remaining = limit.Value - aggregated.Count;
                if (remaining <= 0) break;
                if (remaining < requestSize) requestSize = remaining;
            }

            var query = $"?startAt={currentStart}&maxResults={requestSize}";
            var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueIdOrKey)}/comment{query}";

            var response = await ProxyJiraAsync(null, HttpMethod.Get, path, null, false).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new JObject
                {
                    ["error"] = true,
                    ["statusCode"] = (int)response.StatusCode,
                    ["body"] = TryParseToken(payload),
                    ["fetched"] = aggregated.Count,
                    ["comments"] = aggregated
                };
            }

            JObject page;
            try
            {
                page = string.IsNullOrWhiteSpace(payload) ? new JObject() : JObject.Parse(payload);
            }
            catch
            {
                page = new JObject();
            }

            var pageComments = page["comments"] as JArray ?? new JArray();
            foreach (var comment in pageComments) aggregated.Add(comment);

            total = page.Value<int?>("total") ?? aggregated.Count;
            lastMax = page.Value<int?>("maxResults") ?? requestSize;
            int pageStart = page.Value<int?>("startAt") ?? currentStart;

            if (!limit.HasValue) break;
            if (pageComments.Count == 0) break;
            currentStart = pageStart + pageComments.Count;
            if (currentStart >= total) break;
            if (aggregated.Count >= limit.Value) break;
        }

        return new JObject
        {
            ["startAt"] = initialStart,
            ["maxResults"] = lastMax,
            ["total"] = total,
            ["fetched"] = aggregated.Count,
            ["comments"] = aggregated
        };
    }

    private static JToken TryParseToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return JValue.CreateNull();
        try { return JToken.Parse(text); }
        catch { return new JValue(text); }
    }

    private async Task<JObject> ExecuteJiraToolAsync(HttpMethod method, string path, JObject body, bool includeQuery)
    {
        var response = await ProxyJiraAsync(null, method, path, body?.ToString(Newtonsoft.Json.Formatting.None), includeQuery).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new JObject { ["statusCode"] = (int)response.StatusCode };
        }

        try
        {
            var token = JToken.Parse(payload);
            return new JObject { ["data"] = token };
        }
        catch
        {
            return new JObject
            {
                ["statusCode"] = (int)response.StatusCode,
                ["raw"] = payload
            };
        }
    }

    private async Task<JObject> ExecuteAgileToolAsync(HttpMethod method, string path, JObject body, bool includeQuery)
    {
        var cloudId = await GetCloudIdAsync().ConfigureAwait(false);
        var requestUri = new Uri($"{AtlassianApiBase}/ex/jira/{cloudId}{AgileApiPrefix}{path}");

        var request = new HttpRequestMessage(method, requestUri);
        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authHeader.Scheme, authHeader.Parameter);
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new JObject { ["statusCode"] = (int)response.StatusCode };
        }

        try
        {
            var token = JToken.Parse(payload);
            return new JObject { ["data"] = token };
        }
        catch
        {
            return new JObject
            {
                ["statusCode"] = (int)response.StatusCode,
                ["raw"] = payload
            };
        }
    }

    private string DescribeWithDeps(string baseDescription, params string[] dependencies)
    {
        if (dependencies == null || dependencies.Length == 0) return baseDescription;
        var deps = string.Join(" or ", dependencies.Select(d => "`" + d + "`"));
        return baseDescription + " Call " + deps + " first to discover the required identifier(s).";
    }

    // ========================================
    // JIRA PROXY HELPERS
    // ========================================

    private async Task<HttpResponseMessage> ProxyJiraAsync(string correlationId, HttpMethod method, string path, string body, bool includeQuery)
    {
        var cloudId = await GetCloudIdAsync().ConfigureAwait(false);
        var query = includeQuery ? this.Context.Request.RequestUri.Query : string.Empty;
        var requestUri = new Uri($"{AtlassianApiBase}/ex/jira/{cloudId}{path}{query}");

        await LogToAppInsights("RequestReceived", new
        {
            CorrelationId = correlationId,
            Method = method.Method,
            Path = requestUri.AbsolutePath
        });

        var request = new HttpRequestMessage(method, requestUri);

        if (!string.IsNullOrWhiteSpace(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authHeader.Scheme, authHeader.Parameter);
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ProxyAgileAsync(string correlationId, HttpMethod method, string path, string body, bool includeQuery)
    {
        var cloudId = await GetCloudIdAsync().ConfigureAwait(false);
        var query = includeQuery ? this.Context.Request.RequestUri.Query : string.Empty;
        var requestUri = new Uri($"{AtlassianApiBase}/ex/jira/{cloudId}{AgileApiPrefix}{path}{query}");

        await LogToAppInsights("RequestReceived", new
        {
            CorrelationId = correlationId,
            Method = method.Method,
            Path = requestUri.AbsolutePath
        });

        var request = new HttpRequestMessage(method, requestUri);

        if (!string.IsNullOrWhiteSpace(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authHeader.Scheme, authHeader.Parameter);
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> ProxyAttachmentUploadAsync(string correlationId, string path, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return BuildErrorResponse(HttpStatusCode.BadRequest, "UploadAttachment requires a JSON body with 'filename' and 'contentBase64'.");
        }

        JObject json;
        try
        {
            json = JObject.Parse(body);
        }
        catch (Exception ex)
        {
            return BuildErrorResponse(HttpStatusCode.BadRequest, "UploadAttachment body must be valid JSON: " + ex.Message);
        }

        var filename = json["filename"]?.ToString();
        var contentBase64 = json["contentBase64"]?.ToString();
        var contentType = json["contentType"]?.ToString();
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";

        if (string.IsNullOrWhiteSpace(filename))
        {
            return BuildErrorResponse(HttpStatusCode.BadRequest, "UploadAttachment requires 'filename'.");
        }
        if (string.IsNullOrWhiteSpace(contentBase64))
        {
            return BuildErrorResponse(HttpStatusCode.BadRequest, "UploadAttachment requires 'contentBase64'.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException ex)
        {
            return BuildErrorResponse(HttpStatusCode.BadRequest, "'contentBase64' is not valid base64: " + ex.Message);
        }

        if (bytes.Length > AttachmentMaxBytes)
        {
            return BuildErrorResponse(HttpStatusCode.RequestEntityTooLarge, $"Attachment exceeds {AttachmentMaxBytes / (1024 * 1024)} MB size limit.");
        }

        var cloudId = await GetCloudIdAsync().ConfigureAwait(false);
        var requestUri = new Uri($"{AtlassianApiBase}/ex/jira/{cloudId}{path}");

        await LogToAppInsights("RequestReceived", new
        {
            CorrelationId = correlationId,
            Method = "POST",
            Path = requestUri.AbsolutePath,
            Filename = filename,
            Bytes = bytes.Length
        });

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", filename);

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = multipart
        };
        request.Headers.Add("X-Atlassian-Token", "no-check");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authHeader.Scheme, authHeader.Parameter);
        }

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private HttpResponseMessage BuildErrorResponse(HttpStatusCode status, string message)
    {
        var body = new JObject
        {
            ["error"] = new JObject
            {
                ["code"] = (int)status,
                ["message"] = message
            }
        };
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private async Task<HttpResponseMessage> ProxyAccessibleResourcesAsync(string correlationId)
    {
        await LogToAppInsights("RequestReceived", new
        {
            CorrelationId = correlationId,
            Method = "GET",
            Path = "/oauth/token/accessible-resources"
        });

        return await SendAccessibleResourcesAsync().ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAccessibleResourcesAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AccessibleResourcesUrl));
        var authHeader = this.Context.Request.Headers.Authorization;
        if (authHeader != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authHeader.Scheme, authHeader.Parameter);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetCloudIdAsync()
    {
        if (!string.IsNullOrEmpty(_cachedCloudId))
        {
            return _cachedCloudId;
        }

        var response = await SendAccessibleResourcesAsync().ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to resolve Atlassian cloudId (status {(int)response.StatusCode}): {payload}");
        }

        JArray sites;
        try
        {
            sites = JArray.Parse(payload);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected accessible-resources response: {payload}", ex);
        }

        var first = sites.FirstOrDefault() as JObject;
        var id = first?["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("No accessible Jira sites returned for the authenticated user.");
        }

        _cachedCloudId = id;
        return _cachedCloudId;
    }

    private async Task<string> ReadBodyAsync()
    {
        return await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private string BuildIssuePath(string suffix = null)
    {
        var issueIdOrKey = ExtractPathSegment("/rest/api/3/issue/");
        var basePath = "/rest/api/3/issue/" + issueIdOrKey;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildProjectPath(string suffix)
    {
        var projectIdOrKey = ExtractPathSegment("/rest/api/3/project/");
        var basePath = "/rest/api/3/project/" + projectIdOrKey;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildTaskPath(string suffix = null)
    {
        var taskId = ExtractPathSegment("/rest/api/3/task/");
        var basePath = "/rest/api/3/task/" + taskId;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildFilterPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/api/3/filter/");
        var basePath = "/rest/api/3/filter/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildVersionPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/api/3/version/");
        var basePath = "/rest/api/3/version/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildComponentPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/api/3/component/");
        var basePath = "/rest/api/3/component/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildAttachmentPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/api/3/attachment/");
        var basePath = "/rest/api/3/attachment/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildIssueLinkPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/api/3/issueLink/");
        var basePath = "/rest/api/3/issueLink/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildIssueSubResourcePath(string marker)
    {
        // marker like "/comment/" or "/worklog/" — extracts both the issue key/id and the sub-id
        var issueIdOrKey = ExtractPathSegment("/rest/api/3/issue/");
        var subId = ExtractPathSegment("/rest/api/3/issue/" + Uri.UnescapeDataString(issueIdOrKey) + marker);
        return "/rest/api/3/issue/" + issueIdOrKey + marker + subId;
    }

    private string BuildBoardPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/agile/1.0/board/");
        var basePath = "/board/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildSprintPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/agile/1.0/sprint/");
        var basePath = "/sprint/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string BuildEpicPath(string suffix = null)
    {
        var id = ExtractPathSegment("/rest/agile/1.0/epic/");
        var basePath = "/epic/" + id;
        return string.IsNullOrWhiteSpace(suffix) ? basePath : basePath + suffix;
    }

    private string ExtractPathSegment(string marker)
    {
        var path = this.Context.Request.RequestUri.AbsolutePath ?? string.Empty;
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            throw new ArgumentException($"Path segment not found for marker: {marker}");
        }

        var remainder = path.Substring(index + marker.Length);
        var segment = remainder.Split(new[] { '/' }, 2)[0];
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException("Required path segment is missing");
        }

        return Uri.EscapeDataString(segment);
    }

    private string BuildQueryString(Dictionary<string, object> values)
    {
        var parts = new List<string>();
        foreach (var entry in values)
        {
            if (entry.Value == null) continue;
            var valueText = entry.Value.ToString();
            if (string.IsNullOrWhiteSpace(valueText)) continue;
            parts.Add(Uri.EscapeDataString(entry.Key) + "=" + Uri.EscapeDataString(valueText));
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private JObject BuildAdfText(string text)
    {
        return new JObject
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "paragraph",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "text",
                            ["text"] = text
                        }
                    }
                }
            }
        };
    }

    private string RequireArgument(JObject args, string name)
    {
        var value = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required");
        }
        return value;
    }

    // ========================================
    // JSON-RPC RESPONSE HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result,
            ["id"] = id
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };

        if (!string.IsNullOrWhiteSpace(data))
        {
            error["data"] = data;
        }

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateErrorResponse(string message, HttpStatusCode statusCode)
    {
        var error = new JObject
        {
            ["error"] = new JObject
            {
                ["message"] = message,
                ["statusCode"] = (int)statusCode
            }
        };

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(error.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ========================================
    // APPLICATION INSIGHTS
    // ========================================

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>();
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
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        return key == "IngestionEndpoint" ? "https://dc.services.visualstudio.com/" : null;
    }
}

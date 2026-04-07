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
    private const string ServerVersion = "1.0.0";
    private const string ServerTitle = "Jira MCP";
    private const string ServerDescription = "Jira Cloud MCP tools for projects and issues.";
    private const string ProtocolVersion = "2025-11-25";

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
                    return await ProxyJiraAsync(correlationId, HttpMethod.Post, "/rest/api/3/search", await ReadBodyAsync(), false).ConfigureAwait(false);

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
            ToolListProjects(),
            ToolListIssueTypes(),
            ToolListProjectRoles(),
            ToolListProjectStatuses(),
            ToolSearchIssues(),
            ToolGetIssue(),
            ToolCreateIssue(),
            ToolCreateIssueSimple(),
            ToolUpdateIssue(),
            ToolListComments(),
            ToolAddComment(),
            ToolGetTransitions(),
            ToolTransitionIssue()
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
            case "jira_list_projects":
                return await ExecuteJiraToolAsync(HttpMethod.Get, "/rest/api/3/project", null, true).ConfigureAwait(false);

            case "jira_search_issues":
                return await ExecuteSearchIssuesToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_issue":
                var issueId = RequireArgument(arguments, "issueIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}", null, true).ConfigureAwait(false);

            case "jira_list_issue_types":
                return await ExecuteJiraToolAsync(HttpMethod.Get, "/rest/api/3/issuetype", null, true).ConfigureAwait(false);

            case "jira_list_project_roles":
                var projectId = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projectId)}/role", null, true).ConfigureAwait(false);

            case "jira_list_project_statuses":
                var projectKey = RequireArgument(arguments, "projectIdOrKey");
                return await ExecuteJiraToolAsync(HttpMethod.Get, $"/rest/api/3/project/{Uri.EscapeDataString(projectKey)}/statuses", null, true).ConfigureAwait(false);

            case "jira_create_issue":
                return await ExecuteCreateIssueToolAsync(arguments).ConfigureAwait(false);

            case "jira_create_issue_simple":
                return await ExecuteCreateIssueSimpleToolAsync(arguments).ConfigureAwait(false);

            case "jira_update_issue":
                return await ExecuteUpdateIssueToolAsync(arguments).ConfigureAwait(false);

            case "jira_list_comments":
                return await ExecuteListCommentsToolAsync(arguments).ConfigureAwait(false);

            case "jira_add_comment":
                return await ExecuteAddCommentToolAsync(arguments).ConfigureAwait(false);

            case "jira_get_transitions":
                return await ExecuteGetTransitionsToolAsync(arguments).ConfigureAwait(false);

            case "jira_transition_issue":
                return await ExecuteTransitionIssueToolAsync(arguments).ConfigureAwait(false);

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
            ["description"] = "List roles for a Jira project.",
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
            ["description"] = "List statuses for a Jira project.",
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
            ["description"] = "Search Jira issues using JQL.",
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
                    ["startAt"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Index of the first item to return"
                    },
                    ["maxResults"] = new JObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Maximum number of items to return"
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
            ["description"] = "Get a Jira issue by ID or key.",
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
            ["description"] = "Create a new Jira issue.",
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
            ["description"] = "Create a Jira issue from common fields (project, issue type, summary, description).",
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
            ["description"] = "Update an existing Jira issue.",
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
            ["description"] = "List comments for an issue.",
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
                        ["description"] = "Maximum number of comments to return"
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
            ["description"] = "Add a comment to an issue.",
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
            ["description"] = "List available transitions for an issue.",
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
            ["description"] = "Transition an issue to a new status.",
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

    // ========================================
    // TOOL IMPLEMENTATIONS
    // ========================================

    private async Task<JObject> ExecuteSearchIssuesToolAsync(JObject arguments)
    {
        var request = new JObject
        {
            ["jql"] = RequireArgument(arguments, "jql")
        };

        if (arguments["startAt"] != null) request["startAt"] = arguments["startAt"];
        if (arguments["maxResults"] != null) request["maxResults"] = arguments["maxResults"];
        if (arguments["fields"] != null) request["fields"] = arguments["fields"];

        return await ExecuteJiraToolAsync(HttpMethod.Post, "/rest/api/3/search", request, false).ConfigureAwait(false);
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
        var query = BuildQueryString(new Dictionary<string, object>
        {
            ["startAt"] = arguments["startAt"],
            ["maxResults"] = arguments["maxResults"]
        });

        var path = $"/rest/api/3/issue/{Uri.EscapeDataString(issueId)}/comment{query}";
        return await ExecuteJiraToolAsync(HttpMethod.Get, path, null, false).ConfigureAwait(false);
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

    // ========================================
    // JIRA PROXY HELPERS
    // ========================================

    private async Task<HttpResponseMessage> ProxyJiraAsync(string correlationId, HttpMethod method, string path, string body, bool includeQuery)
    {
        var siteUrl = GetRequiredConnectionParameter("siteUrl");
        var baseUrl = NormalizeSiteUrl(siteUrl);
        var query = includeQuery ? this.Context.Request.RequestUri.Query : string.Empty;
        var requestUri = new Uri(baseUrl.TrimEnd('/') + path + query);

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

    private string GetRequiredConnectionParameter(string name)
    {
        var value = GetConnectionParameter(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing connection parameter: {name}");
        }
        return value;
    }

    private string GetConnectionParameter(string name)
    {
        try
        {
            var raw = this.Context.ConnectionParameters[name]?.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
    }

    private string NormalizeSiteUrl(string siteUrl)
    {
        var trimmed = (siteUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("siteUrl is required");
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        return trimmed.TrimEnd('/');
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

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

public class Script : ScriptBase
{
    // MCP Server metadata
    private const string SERVER_NAME = "managed-apps-mcp";
    private const string SERVER_VERSION = "1.0.0";

    // Power Platform API
    private const string GLOBAL_API_BASE = "https://api.powerplatform.com";
    private const string PP_HOST_SUFFIX = "api.powerplatform.com";
    private const string APP_FRAMEWORK_API_VERSION = "1";
    private const string ENV_API_VERSION = "2022-03-01-preview";

    // Public cloud splits the last 2 hex characters of the environment ID
    // into their own host label. Sovereign clouds use 1.
    private const int HEX_SUFFIX_LENGTH = 2;

    // Permission filters accepted by the appframework list endpoints
    private const string PERMISSION_READ = "Repositories.MicrosoftApps.Play.Read";
    private const string PERMISSION_WRITE = "Repositories.Repositories.Write";

    // GitHub device code flow defaults, applied when the server omits them
    private const string GITHUB_DEFAULT_OAUTH_BASE_URI = "https://github.com";
    private const int GITHUB_AUTH_DEFAULT_POLL_SECONDS = 5;
    private const int GITHUB_AUTH_DEFAULT_EXPIRES_IN = 900;

    // Artifact upload caps. The service rejects anything past 500 MB with HTTP 413,
    // but Power Platform caps the overall payload through a custom connector at
    // 100 MB, so the connector hits its ceiling first. Both are checked before
    // sending so oversized artifacts fail with a clear message instead of a 413.
    private const long SERVICE_MAX_ARTIFACT_BYTES = 524288000;
    private const long CONNECTOR_MAX_PAYLOAD_BYTES = 104857600;

    // Application Insights
    private const string APP_INSIGHTS_CONNECTION_STRING = "[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // ─── Operation Router ───────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            switch (this.Context.OperationId)
            {
                // MCP
                case "InvokeMCP":
                    return await HandleMcpEntryAsync().ConfigureAwait(false);

                // Apps
                case "ListApps":
                    return await HandleTypedListApps().ConfigureAwait(false);
                case "CreateApp":
                    return await HandleTypedCreateApp().ConfigureAwait(false);
                case "GetApp":
                    return await HandleTypedGetApp().ConfigureAwait(false);
                case "DeleteApp":
                    return await HandleTypedDeleteApp().ConfigureAwait(false);
                case "LocateApp":
                    return await HandleTypedLocateApp().ConfigureAwait(false);

                // Build and deploy
                case "BuildApp":
                    return await HandleTypedBuildApp().ConfigureAwait(false);
                case "GetBuildOperation":
                    return await HandleTypedGetBuildOperation().ConfigureAwait(false);
                case "GetBuildOperationLog":
                    return await HandleTypedGetBuildOperationLog().ConfigureAwait(false);
                case "DeployApp":
                    return await HandleTypedDeployApp().ConfigureAwait(false);
                case "UploadBuild":
                    return await HandleTypedUploadBuild().ConfigureAwait(false);

                // Sharing
                case "ListShareLinks":
                    return await HandleTypedListShareLinks().ConfigureAwait(false);
                case "CreateShareLink":
                    return await HandleTypedCreateShareLink().ConfigureAwait(false);
                case "DeleteShareLink":
                    return await HandleTypedDeleteShareLink().ConfigureAwait(false);
                case "ListRoleAssignments":
                    return await HandleTypedListRoleAssignments().ConfigureAwait(false);
                case "ModifyRoleAssignments":
                    return await HandleTypedModifyRoleAssignments().ConfigureAwait(false);

                // Connectivity
                case "ListConnectors":
                    return await HandleTypedListConnectors().ConfigureAwait(false);
                case "GetConnector":
                    return await HandleTypedGetConnector().ConfigureAwait(false);
                case "ListConnections":
                    return await HandleTypedListConnections().ConfigureAwait(false);

                // GitHub repository binding
                case "StartGitHubAuth":
                    return await HandleTypedStartGitHubAuth().ConfigureAwait(false);
                case "GetGitHubAuthStatus":
                    return await HandleTypedGetGitHubAuthStatus().ConfigureAwait(false);

                // Dropdowns
                case "GetEnvironmentDropdown":
                    return await HandleTypedGetEnvironmentDropdown().ConfigureAwait(false);

                default:
                    return CreateJsonRpcErrorResponse(null, -32601, $"Unknown operation: {this.Context.OperationId}");
            }
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Unhandled error: {ex.Message}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    new JObject { ["error"] = ex.Message }.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json"
                )
            };
        }
    }

    // ─── MCP Entry Point ────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpEntryAsync()
    {
        var body = this.Context.Request.Content == null
            ? null
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return CreateJsonRpcErrorResponse(null, -32600, "Request body is required");
        }

        JObject payload;
        try
        {
            payload = JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        if (payload.ContainsKey("jsonrpc"))
        {
            return await HandleMcpAsync(payload).ConfigureAwait(false);
        }

        return CreateJsonRpcErrorResponse(null, -32600, "Invalid request format. Expected JSON-RPC 2.0.");
    }

    // ─── MCP Protocol Router ────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpAsync(JObject request)
    {
        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        this.Context.Logger.LogInformation($"MCP method: {method}");
        await LogToAppInsights("MCPMethodCall", new Dictionary<string, string> { ["Method"] = method });

        switch (method)
        {
            case "initialize":
                var protocolVersion = @params["protocolVersion"]?.ToString() ?? "2024-11-05";
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["protocolVersion"] = protocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = SERVER_NAME,
                        ["version"] = SERVER_VERSION
                    }
                });

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["tools"] = GetToolDefinitions()
                });

            case "tools/call":
                return await HandleToolsCall(@params, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["resources"] = new JArray()
                });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, $"Method not found: {method}");
        }
    }

    // ─── MCP Tool Definitions ───────────────────────────────────────────

    private JArray GetToolDefinitions()
    {
        var environmentProperty = new JObject
        {
            ["type"] = "string",
            ["description"] = "The Power Platform environment ID that hosts the app. Call managedapps_list_environments first if it is not known."
        };

        var appIdProperty = new JObject
        {
            ["type"] = "string",
            ["description"] = "The Copilot Managed Runtime app ID (GUID)."
        };

        return new JArray
        {
            // Apps
            new JObject
            {
                ["name"] = "managedapps_list_environments",
                ["description"] = "List Power Platform environments available to the caller. Use this to discover the environment ID required by every other tool.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "managedapps_list_apps",
                ["description"] = "List Copilot Managed Runtime apps in a Power Platform environment. Returns app ID, display name, description, and backing repository.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["permission"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Filter by the permission the caller holds: 'read' for apps you can play, 'write' for apps you can edit. Defaults to 'read'.",
                            ["enum"] = new JArray { "read", "write" }
                        },
                        ["skipToken"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Continuation token from a previous call to fetch the next page."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_get_app",
                ["description"] = "Get details for a single Copilot Managed Runtime app by ID.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone()
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_create_app",
                ["description"] = "Create a new Copilot Managed Runtime app in a Power Platform environment. Returns the new app ID and its backing repository ID.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["displayName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Display name for the new app."
                        },
                        ["description"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Description of the new app."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "displayName" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_delete_app",
                ["description"] = "Delete a Copilot Managed Runtime app. This is irreversible. Reports whether the app existed before the call.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone()
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_locate_app",
                ["description"] = "Resolve which tenant, environment, and geography host a Copilot Managed Runtime app. Use when an app ID is known but its location is not.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone()
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },

            // Build and deploy
            new JObject
            {
                ["name"] = "managedapps_build_app",
                ["description"] = "Start a server-side build of a Copilot Managed Runtime app from its backing repository. Returns a build operation ID to poll.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["commitSha"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Specific commit to build. Omit to build the latest commit."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_get_build_operation",
                ["description"] = "Get the status and result of a Copilot Managed Runtime app build operation.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["operationId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The build operation ID returned by managedapps_build_app."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId", "operationId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_get_build_log",
                ["description"] = "Get the build log for a Copilot Managed Runtime app build operation. Use this to diagnose a failed build.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["operationId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The build operation ID."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId", "operationId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_deploy_app",
                ["description"] = "Deploy a built commit of a Copilot Managed Runtime app so it becomes available to players.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["commitSha"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The built commit SHA to deploy."
                        },
                        ["stageName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional deployment stage name."
                        },
                        ["forceReauth"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Force connection re-authorization as part of the deployment."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId", "commitSha" }
                }
            },

            // Sharing
            new JObject
            {
                ["name"] = "managedapps_list_share_links",
                ["description"] = "List the share links that currently grant access to a Copilot Managed Runtime app.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone()
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_create_share_link",
                ["description"] = "Create a share link that grants access to a Copilot Managed Runtime app.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["roleName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Role to grant through the link."
                        },
                        ["expiresOn"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Expiration timestamp in ISO 8601 format."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_delete_share_link",
                ["description"] = "Revoke a share link so it no longer grants access to the Copilot Managed Runtime app.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["shareLinkId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The share link ID to revoke."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId", "shareLinkId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_list_role_assignments",
                ["description"] = "List the users and groups assigned roles on a Copilot Managed Runtime app.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["skipToken"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Continuation token from a previous call to fetch the next page."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_modify_role_assignments",
                ["description"] = "Grant or revoke Copilot Managed Runtime app roles for users and groups in a single call.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = appIdProperty.DeepClone(),
                        ["put"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Role assignments to create or update.",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["principalId"] = new JObject { ["type"] = "string", ["description"] = "Object ID of the user or group." },
                                    ["principalType"] = new JObject { ["type"] = "string", ["description"] = "Type of principal, for example User or Group." },
                                    ["roleName"] = new JObject { ["type"] = "string", ["description"] = "Role to assign." }
                                }
                            }
                        },
                        ["delete"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Role assignments to remove.",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["id"] = new JObject { ["type"] = "string", ["description"] = "Role assignment ID to remove." },
                                    ["principalId"] = new JObject { ["type"] = "string", ["description"] = "Object ID of the user or group to remove." }
                                }
                            }
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId" }
                }
            },

            // Connectivity
            new JObject
            {
                ["name"] = "managedapps_list_connectors",
                ["description"] = "List the connectors available as Copilot Managed Runtime app data sources in an environment.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone()
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_get_connector",
                ["description"] = "Get metadata for a single connector available as a Copilot Managed Runtime app data source.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["connectorId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The connector name, for example shared_sharepointonline."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "connectorId" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_list_connections",
                ["description"] = "List the caller's existing connections for a connector, for binding as a Copilot Managed Runtime app data source.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["connectorId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The connector name, for example shared_sharepointonline."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "connectorId" }
                }
            },

            // GitHub repository binding
            new JObject
            {
                ["name"] = "managedapps_upload_build",
                ["description"] = "Upload a built app artifact as a zip. The service commits it to the app's repository and returns a commit SHA that managedapps_deploy_app can then deploy. The zip must be supplied base64 encoded, and base64 inflates the payload by about a third, so this suits small artifacts only. For large artifacts use the 'ms deploy --artifact' CLI command instead.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["appId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The Copilot Managed Runtime app ID."
                        },
                        ["zipContentBase64"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The built app output, packaged as a zip and base64 encoded."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "appId", "zipContentBase64" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_start_github_auth",
                ["description"] = "Start GitHub authorization for a bring-your-own repository. Returns a one-time user code and a verification URI the user must visit to authorize the Managed Apps GitHub App. Show the user the code and the URI, then poll managedapps_get_github_auth_status until it completes.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["repositoryUrl"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The repository URL, for example https://github.com/owner/repo. GitHub Enterprise Cloud hosts such as https://contoso.ghe.com/owner/repo are also supported."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "repositoryUrl" }
                }
            },
            new JObject
            {
                ["name"] = "managedapps_get_github_auth_status",
                ["description"] = "Check whether the user has finished GitHub authorization. Returns a state of Pending, Complete, Denied, NotFound, or Expired. Keep polling while the state is Pending, waiting the pollIntervalSeconds returned when the session started.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = environmentProperty.DeepClone(),
                        ["sessionId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The session ID returned by managedapps_start_github_auth."
                        },
                        ["oauthBaseUri"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The oauthBaseUri returned by managedapps_start_github_auth. Defaults to https://github.com when omitted."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "sessionId" }
                }
            }
        };
    }

    // ─── MCP Tool Dispatch ──────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        this.Context.Logger.LogInformation($"Tool call: {toolName}");
        await LogToAppInsights("ToolCall", new Dictionary<string, string>
        {
            ["ToolName"] = toolName,
            ["HasArguments"] = (arguments.Count > 0).ToString()
        });

        try
        {
            JToken result;
            switch (toolName)
            {
                case "managedapps_list_environments":
                    result = await ListEnvironmentsAsync().ConfigureAwait(false);
                    break;
                case "managedapps_list_apps":
                    result = await ListAppsAsync(
                        RequireArg(arguments, "environmentId"),
                        arguments["permission"]?.ToString(),
                        arguments["skipToken"]?.ToString()).ConfigureAwait(false);
                    break;
                case "managedapps_get_app":
                    result = await GetAppAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId")).ConfigureAwait(false);
                    break;
                case "managedapps_create_app":
                    result = await CreateAppAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "displayName"),
                        arguments["description"]?.ToString()).ConfigureAwait(false);
                    break;
                case "managedapps_delete_app":
                    result = await DeleteAppAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId")).ConfigureAwait(false);
                    break;
                case "managedapps_locate_app":
                    result = await LocateAppAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId")).ConfigureAwait(false);
                    break;
                case "managedapps_build_app":
                    result = await BuildAppAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        arguments["commitSha"]?.ToString()).ConfigureAwait(false);
                    break;
                case "managedapps_get_build_operation":
                    result = await GetBuildOperationAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        RequireArg(arguments, "operationId")).ConfigureAwait(false);
                    break;
                case "managedapps_get_build_log":
                    result = await GetBuildOperationLogAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        RequireArg(arguments, "operationId")).ConfigureAwait(false);
                    break;
                case "managedapps_deploy_app":
                    result = await DeployAppAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        RequireArg(arguments, "commitSha"),
                        arguments["stageName"]?.ToString(),
                        arguments["forceReauth"]?.Type == JTokenType.Boolean && arguments["forceReauth"].Value<bool>()).ConfigureAwait(false);
                    break;
                case "managedapps_list_share_links":
                    result = await ListShareLinksAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId")).ConfigureAwait(false);
                    break;
                case "managedapps_create_share_link":
                    result = await CreateShareLinkAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        arguments["roleName"]?.ToString(),
                        arguments["expiresOn"]?.ToString()).ConfigureAwait(false);
                    break;
                case "managedapps_delete_share_link":
                    result = await DeleteShareLinkAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        RequireArg(arguments, "shareLinkId")).ConfigureAwait(false);
                    break;
                case "managedapps_list_role_assignments":
                    result = await ListRoleAssignmentsAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        arguments["skipToken"]?.ToString()).ConfigureAwait(false);
                    break;
                case "managedapps_modify_role_assignments":
                    result = await ModifyRoleAssignmentsAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        arguments["put"] as JArray,
                        arguments["delete"] as JArray).ConfigureAwait(false);
                    break;
                case "managedapps_list_connectors":
                    result = await ListConnectorsAsync(
                        RequireArg(arguments, "environmentId")).ConfigureAwait(false);
                    break;
                case "managedapps_get_connector":
                    result = await GetConnectorAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "connectorId")).ConfigureAwait(false);
                    break;
                case "managedapps_list_connections":
                    result = await ListConnectionsAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "connectorId")).ConfigureAwait(false);
                    break;
                case "managedapps_start_github_auth":
                    result = await StartGitHubAuthAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "repositoryUrl")).ConfigureAwait(false);
                    break;
                case "managedapps_upload_build":
                    result = await UploadBuildAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "appId"),
                        DecodeBase64Artifact(RequireArg(arguments, "zipContentBase64"))).ConfigureAwait(false);
                    break;
                case "managedapps_get_github_auth_status":
                    result = await GetGitHubAuthStatusAsync(
                        RequireArg(arguments, "environmentId"),
                        RequireArg(arguments, "sessionId"),
                        arguments["oauthBaseUri"]?.ToString()).ConfigureAwait(false);
                    break;

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32602, $"Unknown tool: {toolName}");
            }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Tool {toolName} failed: {ex.Message}");
            await LogToAppInsights("ToolCallError", new Dictionary<string, string>
            {
                ["ToolName"] = toolName,
                ["Error"] = ex.Message
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

    // ─── Core Operations ────────────────────────────────────────────────

    private async Task<JToken> ListEnvironmentsAsync()
    {
        var response = await CallGlobalApi(
            HttpMethod.Get,
            $"/environmentmanagement/environments?api-version={ENV_API_VERSION}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var environments = data["value"] as JArray ?? new JArray();

        var list = new JArray();
        foreach (var env in environments)
        {
            list.Add(new JObject
            {
                ["id"] = ReadEnvironmentId(env),
                ["name"] = ReadEnvironmentDisplayName(env),
                ["geo"] = env["geo"] ?? env["properties"]?["geo"],
                ["state"] = env["state"] ?? env["properties"]?["states"]?["management"]?["id"]
            });
        }

        return new JObject
        {
            ["environmentCount"] = list.Count,
            ["environments"] = list
        };
    }

    private async Task<JToken> ListAppsAsync(string environmentId, string permission, string skipToken)
    {
        var query = $"?api-version={APP_FRAMEWORK_API_VERSION}&permission={Uri.EscapeDataString(ResolvePermission(permission))}";
        if (!string.IsNullOrEmpty(skipToken))
            query += $"&$skiptoken={Uri.EscapeDataString(skipToken)}";

        var response = await CallAppFrameworkApi(environmentId, HttpMethod.Get, $"/appframework/apps{query}").ConfigureAwait(false);
        var data = JObject.Parse(response);
        var apps = data["value"] as JArray ?? new JArray();

        var list = new JArray();
        foreach (var app in apps)
        {
            list.Add(ProjectApp(app));
        }

        var nextLink = data["nextLink"]?.ToString();
        return new JObject
        {
            ["appCount"] = list.Count,
            ["nextLink"] = nextLink,
            ["skipToken"] = ExtractSkipToken(nextLink),
            ["apps"] = list
        };
    }

    private async Task<JToken> GetAppAsync(string environmentId, string appId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}?api-version={APP_FRAMEWORK_API_VERSION}"
        ).ConfigureAwait(false);

        return ProjectApp(JObject.Parse(response));
    }

    private async Task<JToken> CreateAppAsync(string environmentId, string displayName, string description)
    {
        var body = new JObject
        {
            ["displayName"] = displayName,
            ["environmentId"] = environmentId
        };
        if (!string.IsNullOrEmpty(description))
            body["description"] = description;

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps?api-version={APP_FRAMEWORK_API_VERSION}",
            body.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return ProjectApp(JObject.Parse(response));
    }

    private async Task<JToken> DeleteAppAsync(string environmentId, string appId)
    {
        // The service returns 200 when the app existed and 204 when it did not.
        var result = await CallAppFrameworkRaw(
            environmentId,
            HttpMethod.Delete,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}?api-version={APP_FRAMEWORK_API_VERSION}"
        ).ConfigureAwait(false);

        return new JObject
        {
            ["appId"] = appId,
            ["existed"] = result.StatusCode == 200,
            ["deleted"] = true
        };
    }

    private async Task<JToken> LocateAppAsync(string environmentId, string appId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/locate?api-version={APP_FRAMEWORK_API_VERSION}",
            "{}"
        ).ConfigureAwait(false);

        return JObject.Parse(response);
    }

    private async Task<JToken> BuildAppAsync(string environmentId, string appId, string commitSha)
    {
        var body = new JObject();
        if (!string.IsNullOrEmpty(commitSha))
            body["commitSha"] = commitSha;

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/build?api-version={APP_FRAMEWORK_API_VERSION}",
            body.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return ParseJsonOrWrap(response);
    }

    private async Task<JToken> GetBuildOperationAsync(string environmentId, string appId, string operationId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/commitBuildOperations/{Uri.EscapeDataString(operationId)}?api-version={APP_FRAMEWORK_API_VERSION}"
        ).ConfigureAwait(false);

        return ParseJsonOrWrap(response);
    }

    private async Task<JToken> GetBuildOperationLogAsync(string environmentId, string appId, string operationId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/commitBuildOperations/{Uri.EscapeDataString(operationId)}/log?api-version={APP_FRAMEWORK_API_VERSION}"
        ).ConfigureAwait(false);

        // The log endpoint may return plain text rather than JSON.
        return new JObject
        {
            ["operationId"] = operationId,
            ["log"] = response
        };
    }

    private async Task<JToken> DeployAppAsync(string environmentId, string appId, string commitSha, string stageName, bool forceReauth)
    {
        var body = new JObject { ["commitSha"] = commitSha };
        if (!string.IsNullOrEmpty(stageName))
            body["stageName"] = stageName;
        if (forceReauth)
            body["forceReauth"] = true;

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/deploy?api-version={APP_FRAMEWORK_API_VERSION}",
            body.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return ParseJsonOrWrap(response);
    }

    private async Task<JToken> ListShareLinksAsync(string environmentId, string appId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/shareLinks?api-version={APP_FRAMEWORK_API_VERSION}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var links = data["value"] as JArray ?? new JArray();

        return new JObject
        {
            ["shareLinkCount"] = links.Count,
            ["shareLinks"] = links
        };
    }

    private async Task<JToken> CreateShareLinkAsync(string environmentId, string appId, string roleName, string expiresOn)
    {
        var body = new JObject();
        if (!string.IsNullOrEmpty(roleName))
            body["roleName"] = roleName;
        if (!string.IsNullOrEmpty(expiresOn))
            body["expiresOn"] = expiresOn;

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/shareLinks?api-version={APP_FRAMEWORK_API_VERSION}",
            body.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return ParseJsonOrWrap(response);
    }

    private async Task<JToken> DeleteShareLinkAsync(string environmentId, string appId, string shareLinkId)
    {
        await CallAppFrameworkRaw(
            environmentId,
            HttpMethod.Delete,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/shareLinks/{Uri.EscapeDataString(shareLinkId)}?api-version={APP_FRAMEWORK_API_VERSION}"
        ).ConfigureAwait(false);

        return new JObject
        {
            ["shareLinkId"] = shareLinkId,
            ["deleted"] = true
        };
    }

    private async Task<JToken> ListRoleAssignmentsAsync(string environmentId, string appId, string skipToken)
    {
        var query = $"?api-version={APP_FRAMEWORK_API_VERSION}";
        if (!string.IsNullOrEmpty(skipToken))
            query += $"&$skiptoken={Uri.EscapeDataString(skipToken)}";

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/roleAssignments{query}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var assignments = data["value"] as JArray ?? new JArray();
        var nextLink = data["nextLink"]?.ToString();

        return new JObject
        {
            ["roleAssignmentCount"] = assignments.Count,
            ["nextLink"] = nextLink,
            ["skipToken"] = ExtractSkipToken(nextLink),
            ["roleAssignments"] = assignments
        };
    }

    private async Task<JToken> ModifyRoleAssignmentsAsync(string environmentId, string appId, JArray put, JArray delete)
    {
        var body = new JObject();
        if (put != null) body["put"] = put;
        if (delete != null) body["delete"] = delete;

        await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/modifyRoleAssignments?api-version={APP_FRAMEWORK_API_VERSION}",
            body.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return new JObject
        {
            ["appId"] = appId,
            ["grantedCount"] = put?.Count ?? 0,
            ["revokedCount"] = delete?.Count ?? 0
        };
    }

    private async Task<JToken> ListConnectorsAsync(string environmentId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/connectivity/connectors?api-version={APP_FRAMEWORK_API_VERSION}{EnvironmentFilter(environmentId)}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var connectors = data["value"] as JArray ?? new JArray();

        var list = new JArray();
        foreach (var c in connectors)
        {
            list.Add(ProjectConnector(c));
        }

        return new JObject
        {
            ["connectorCount"] = list.Count,
            ["connectors"] = list
        };
    }

    private async Task<JToken> GetConnectorAsync(string environmentId, string connectorId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/connectivity/connectors/{Uri.EscapeDataString(connectorId)}?api-version={APP_FRAMEWORK_API_VERSION}{EnvironmentFilter(environmentId)}"
        ).ConfigureAwait(false);

        return ProjectConnector(JObject.Parse(response));
    }

    private async Task<JToken> ListConnectionsAsync(string environmentId, string connectorId)
    {
        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Get,
            $"/connectivity/connectors/{Uri.EscapeDataString(connectorId)}/connections?api-version={APP_FRAMEWORK_API_VERSION}{EnvironmentFilter(environmentId)}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var connections = data["value"] as JArray ?? new JArray();

        var list = new JArray();
        foreach (var c in connections)
        {
            var statuses = c["properties"]?["statuses"] as JArray;
            var status = statuses != null && statuses.Count > 0
                ? statuses[0]["status"]
                : c["status"];

            list.Add(new JObject
            {
                ["name"] = c["name"],
                ["id"] = c["id"],
                ["displayName"] = c["properties"]?["displayName"] ?? c["displayName"],
                ["status"] = status
            });
        }

        return new JObject
        {
            ["connectionCount"] = list.Count,
            ["connections"] = list
        };
    }

    // ─── Artifact Upload ────────────────────────────────────────────────

    /// <summary>
    /// Uploads a built app artifact as a zip. The service commits it to the app's
    /// backing repository and returns the resulting commit SHA, which can then be
    /// passed to DeployApp. This is the server-side equivalent of pushing a commit;
    /// it does not replace Git for ordinary source control.
    /// </summary>
    private async Task<JToken> UploadBuildAsync(string environmentId, string appId, byte[] artifact)
    {
        if (artifact == null || artifact.Length == 0)
        {
            throw new ArgumentException(
                "A zip artifact is required. Provide the built app output as a zip file.");
        }

        if (artifact.Length > CONNECTOR_MAX_PAYLOAD_BYTES)
        {
            throw new ArgumentException(
                $"The artifact is {DescribeBytes(artifact.Length)}, which exceeds the {DescribeBytes(CONNECTOR_MAX_PAYLOAD_BYTES)} payload limit for Power Platform custom connectors. " +
                $"The service itself accepts up to {DescribeBytes(SERVICE_MAX_ARTIFACT_BYTES)}, so for artifacts this large use the 'ms deploy --artifact' CLI command instead of this connector.");
        }

        if (!HasZipSignature(artifact))
        {
            throw new ArgumentException(
                "The uploaded content is not a zip archive. Provide the built app output packaged as a zip file.");
        }

        var result = await CallAppFrameworkBinary(
            environmentId,
            HttpMethod.Post,
            $"/appframework/apps/{Uri.EscapeDataString(appId)}/uploadBuild?api-version={APP_FRAMEWORK_API_VERSION}",
            artifact,
            "application/zip"
        ).ConfigureAwait(false);

        if (result.StatusCode < 200 || result.StatusCode >= 300)
        {
            throw new InvalidOperationException(DescribeUploadFailure(result));
        }

        var data = JObject.Parse(result.Body);
        var commitSha = data["commitSha"]?.ToString();

        if (string.IsNullOrEmpty(commitSha))
        {
            throw new InvalidOperationException(
                "The upload succeeded but the service did not return a commitSha, so there is nothing to deploy.");
        }

        return new JObject
        {
            ["commitSha"] = commitSha,
            ["artifactBytes"] = artifact.Length,
            ["artifactSize"] = DescribeBytes(artifact.Length),
            ["message"] = $"Artifact committed as {commitSha}. Pass this commit SHA to Deploy Copilot Managed Runtime app."
        };
    }

    /// <summary>
    /// Maps the documented upload failures onto messages that say what to do next.
    /// </summary>
    private string DescribeUploadFailure(ApiResult result)
    {
        var code = ReadErrorCode(result.Body);

        switch (result.StatusCode)
        {
            case 413:
                return $"The service rejected the artifact as too large. The maximum is {DescribeBytes(SERVICE_MAX_ARTIFACT_BYTES)}.";
            case 422:
                return string.Equals(code, "ArtifactInvalid", StringComparison.OrdinalIgnoreCase)
                    ? $"The service rejected the artifact as invalid. Confirm the zip contains the built app output and its configuration file. Details: {TruncateForError(result.Body)}"
                    : $"The service rejected the artifact: {TruncateForError(result.Body)}";
            case 409:
                return string.Equals(code, "RepoTypeMismatch", StringComparison.OrdinalIgnoreCase)
                    ? "The app's repository type does not match the service's record, so the artifact cannot be committed."
                    : $"The upload conflicted with the app's current state: {TruncateForError(result.Body)}";
            case 502:
                return "The build service is temporarily unavailable. Try again.";
            default:
                return $"Artifact upload returned {result.StatusCode}: {TruncateForError(result.Body)}";
        }
    }

    private static string ReadErrorCode(string body)
    {
        try
        {
            var parsed = JObject.Parse(body);
            return parsed["code"]?.ToString() ?? parsed["error"]?["code"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks for the local file header that every non-empty zip begins with, so a
    /// mistakenly supplied file fails here rather than as a 422 from the service.
    /// </summary>
    private static bool HasZipSignature(byte[] content)
    {
        return content.Length >= 4
            && content[0] == 0x50 && content[1] == 0x4B
            && (content[2] == 0x03 || content[2] == 0x05 || content[2] == 0x07);
    }

    private static string DescribeBytes(long bytes)
    {
        if (bytes >= 1048576)
            return $"{(bytes / 1048576.0):0.#} MB";
        if (bytes >= 1024)
            return $"{(bytes / 1024.0):0.#} KB";
        return $"{bytes} bytes";
    }

    private static byte[] DecodeBase64Artifact(string value)
    {
        try
        {
            return Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            throw new ArgumentException(
                "zipContentBase64 is not valid base64. Supply the zip file's bytes base64 encoded.");
        }
    }

    // ─── GitHub Repository Binding ──────────────────────────────────────

    /// <summary>
    /// Starts the GitHub device code flow used to authorize the Managed Apps GitHub App
    /// against a bring-your-own repository. Returns a one-time code the user enters at
    /// the returned verification URI, plus the session ID used to poll for completion.
    /// </summary>
    private async Task<JToken> StartGitHubAuthAsync(string environmentId, string repositoryUrl)
    {
        var repo = ParseGitHubRepositoryUrl(repositoryUrl);

        var payload = new JObject
        {
            ["owner"] = repo.Owner,
            ["repo"] = repo.Repo,
            ["oauthBaseUri"] = repo.OAuthBaseUri
        };

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/github/auth/device?api-version={APP_FRAMEWORK_API_VERSION}",
            payload.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);

        return new JObject
        {
            ["sessionId"] = data["sessionId"],
            ["userCode"] = data["userCode"],
            ["verificationUri"] = data["verificationUri"],
            ["expiresIn"] = PositiveOrDefault(data["expiresIn"], GITHUB_AUTH_DEFAULT_EXPIRES_IN),
            ["pollIntervalSeconds"] = PositiveOrDefault(data["pollIntervalSeconds"], GITHUB_AUTH_DEFAULT_POLL_SECONDS),
            // Echoed back so the caller can pass it straight to GetGitHubAuthStatus.
            ["oauthBaseUri"] = repo.OAuthBaseUri,
            ["owner"] = repo.Owner,
            ["repo"] = repo.Repo
        };
    }

    /// <summary>
    /// Polls a GitHub device code session. Terminal states are Complete, Denied,
    /// NotFound, and Expired; Pending means the user has not finished signing in yet.
    /// </summary>
    private async Task<JToken> GetGitHubAuthStatusAsync(string environmentId, string sessionId, string oauthBaseUri)
    {
        var baseUri = string.IsNullOrWhiteSpace(oauthBaseUri)
            ? GITHUB_DEFAULT_OAUTH_BASE_URI
            : oauthBaseUri.TrimEnd('/');

        var payload = new JObject
        {
            ["sessionId"] = sessionId,
            ["oauthBaseUri"] = baseUri
        };

        var response = await CallAppFrameworkApi(
            environmentId,
            HttpMethod.Post,
            $"/appframework/github/auth/status?api-version={APP_FRAMEWORK_API_VERSION}",
            payload.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var state = data["state"]?.ToString();

        return new JObject
        {
            ["state"] = state,
            ["isComplete"] = string.Equals(state, "Complete", StringComparison.OrdinalIgnoreCase),
            ["isPending"] = string.Equals(state, "Pending", StringComparison.OrdinalIgnoreCase),
            ["githubLogin"] = data["githubLogin"],
            ["mappingExpiresAt"] = data["mappingExpiresAt"],
            ["message"] = DescribeGitHubAuthState(state)
        };
    }

    private static string DescribeGitHubAuthState(string state)
    {
        switch (state)
        {
            case "Pending":
                return "Waiting for the user to enter the code at the verification URI.";
            case "Complete":
                return "Authorization complete. The repository can now be bound.";
            case "Denied":
                return "The user declined to authorize the Managed Apps GitHub App. Repository binding cannot proceed.";
            case "NotFound":
                return "The authentication session was not found. Start a new one.";
            case "Expired":
                return "The authentication session expired before sign-in completed. Start a new one.";
            default:
                return $"Unexpected authentication state '{state}' returned by the server.";
        }
    }

    private static int PositiveOrDefault(JToken value, int fallback)
    {
        if (value != null &&
            (value.Type == JTokenType.Integer || value.Type == JTokenType.Float))
        {
            var parsed = value.Value<double>();
            if (parsed > 0 && parsed <= int.MaxValue)
                return (int)parsed;
        }

        return fallback;
    }

    /// <summary>
    /// Splits a repository URL into the owner, repository, and OAuth base URI the
    /// device code endpoints expect. Accepts HTTPS and SCP-style SSH forms, and any
    /// GitHub host so that GitHub Enterprise Cloud repositories work.
    /// </summary>
    private static GitHubRepositoryRef ParseGitHubRepositoryUrl(string repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            throw new ArgumentException("repositoryUrl is required, for example https://github.com/owner/repo.");

        var trimmed = repositoryUrl.Trim();
        string host;
        string path;

        var scpMatch = Regex.Match(trimmed, @"^git@([^:/]+):(.+)$");
        if (scpMatch.Success)
        {
            host = scpMatch.Groups[1].Value;
            path = scpMatch.Groups[2].Value;
        }
        else
        {
            Uri uri;
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException(
                    $"Could not parse a GitHub host from '{repositoryUrl}'. Provide a URL such as https://github.com/owner/repo or https://contoso.ghe.com/owner/repo.");
            }

            host = uri.Host;
            path = uri.AbsolutePath;
        }

        var segments = path.Trim('/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            throw new ArgumentException(
                $"Could not parse an owner and repository from '{repositoryUrl}'. Provide a URL such as https://github.com/owner/repo.");
        }

        var owner = segments[0];
        var repo = segments[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo.Substring(0, repo.Length - 4);

        return new GitHubRepositoryRef
        {
            Owner = owner,
            Repo = repo,
            OAuthBaseUri = $"https://{host}"
        };
    }

    private class GitHubRepositoryRef
    {
        public string Owner { get; set; }
        public string Repo { get; set; }
        public string OAuthBaseUri { get; set; }
    }

    // ─── Typed Operation Handlers ───────────────────────────────────────

    private async Task<HttpResponseMessage> HandleTypedListApps()
    {
        var result = await ListAppsAsync(
            GetQueryParam("environmentId"),
            GetQueryParam("permission"),
            GetQueryParam("skipToken")).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedCreateApp()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await CreateAppAsync(
            GetQueryParam("environmentId"),
            body["displayName"]?.ToString(),
            body["description"]?.ToString()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetApp()
    {
        var result = await GetAppAsync(GetQueryParam("environmentId"), GetPathSegment(2)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedDeleteApp()
    {
        var result = await DeleteAppAsync(GetQueryParam("environmentId"), GetPathSegment(2)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedLocateApp()
    {
        var result = await LocateAppAsync(GetQueryParam("environmentId"), GetPathSegment(2)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedBuildApp()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await BuildAppAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            body["commitSha"]?.ToString()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetBuildOperation()
    {
        var result = await GetBuildOperationAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            GetPathSegment(4)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetBuildOperationLog()
    {
        var result = await GetBuildOperationLogAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            GetPathSegment(4)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedDeployApp()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await DeployAppAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            body["commitSha"]?.ToString(),
            body["stageName"]?.ToString(),
            body["forceReauth"]?.Type == JTokenType.Boolean && body["forceReauth"].Value<bool>()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedUploadBuild()
    {
        var artifact = this.Context.Request.Content == null
            ? null
            : await this.Context.Request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        var result = await UploadBuildAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            artifact).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListShareLinks()
    {
        var result = await ListShareLinksAsync(GetQueryParam("environmentId"), GetPathSegment(2)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedCreateShareLink()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await CreateShareLinkAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            body["roleName"]?.ToString(),
            body["expiresOn"]?.ToString()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedDeleteShareLink()
    {
        var result = await DeleteShareLinkAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            GetPathSegment(4)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListRoleAssignments()
    {
        var result = await ListRoleAssignmentsAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            GetQueryParam("skipToken")).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedModifyRoleAssignments()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await ModifyRoleAssignmentsAsync(
            GetQueryParam("environmentId"),
            GetPathSegment(2),
            body["put"] as JArray,
            body["delete"] as JArray).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListConnectors()
    {
        var result = await ListConnectorsAsync(GetQueryParam("environmentId")).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetConnector()
    {
        var result = await GetConnectorAsync(GetQueryParam("environmentId"), GetPathSegment(2)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListConnections()
    {
        var result = await ListConnectionsAsync(GetQueryParam("environmentId"), GetPathSegment(2)).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedStartGitHubAuth()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await StartGitHubAuthAsync(
            GetQueryParam("environmentId"),
            body["repositoryUrl"]?.ToString()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetGitHubAuthStatus()
    {
        var body = await ReadRequestBodyAsync().ConfigureAwait(false);
        var result = await GetGitHubAuthStatusAsync(
            GetQueryParam("environmentId"),
            body["sessionId"]?.ToString(),
            body["oauthBaseUri"]?.ToString()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetEnvironmentDropdown()
    {
        try
        {
            var response = await CallGlobalApi(
                HttpMethod.Get,
                $"/environmentmanagement/environments?api-version={ENV_API_VERSION}"
            ).ConfigureAwait(false);

            var data = JObject.Parse(response);
            var environments = data["value"] as JArray ?? new JArray();

            var dropdown = new JArray();
            foreach (var env in environments)
            {
                var id = ReadEnvironmentId(env)?.ToString();
                if (string.IsNullOrEmpty(id)) continue;

                dropdown.Add(new JObject
                {
                    ["id"] = id,
                    ["name"] = ReadEnvironmentDisplayName(env)
                });
            }

            return CreateTypedResponse(dropdown);
        }
        catch
        {
            return CreateTypedResponse(new JArray());
        }
    }

    // ─── Endpoint Resolution ────────────────────────────────────────────

    /// <summary>
    /// Builds the environment-scoped Power Platform API host. The environment ID is
    /// lowercased with dashes removed, then the final characters are split off into
    /// their own host label, matching the scheme used by the Copilot Managed Runtime CLI.
    /// </summary>
    private static string BuildEnvironmentHost(string environmentId)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
            throw new ArgumentException("environmentId is required.");

        if (!Regex.IsMatch(environmentId, "^[a-zA-Z0-9-]+$"))
            throw new ArgumentException(
                $"Cannot build a Power Platform API endpoint because the environment identifier contains characters that are not valid in a host name. Only letters, digits, and dashes are expected: {environmentId}");

        var normalized = environmentId.ToLowerInvariant().Replace("-", "");
        if (normalized.Length <= HEX_SUFFIX_LENGTH)
            throw new ArgumentException(
                $"Cannot build a Power Platform API endpoint because the normalized environment identifier must be longer than {HEX_SUFFIX_LENGTH} characters: {normalized}");

        var suffix = normalized.Substring(normalized.Length - HEX_SUFFIX_LENGTH);
        var prefix = normalized.Substring(0, normalized.Length - HEX_SUFFIX_LENGTH);

        return $"{prefix}.{suffix}.environment.{PP_HOST_SUFFIX}";
    }

    /// <summary>
    /// Builds the environment OData filter that the connectivity endpoints require.
    /// The service scopes connector and connection lookups by environment even on an
    /// environment-scoped host, so every /connectivity call carries this filter.
    /// </summary>
    private static string EnvironmentFilter(string environmentId)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
            return string.Empty;

        return $"&$filter=environment+eq+'{Uri.EscapeDataString(environmentId)}'";
    }

    private static string ResolvePermission(string permission)
    {
        if (string.IsNullOrEmpty(permission))
            return PERMISSION_READ;

        switch (permission.ToLowerInvariant())
        {
            case "read":
                return PERMISSION_READ;
            case "write":
                return PERMISSION_WRITE;
            default:
                throw new ArgumentException($"Invalid permission '{permission}'. Must be 'read' or 'write'.");
        }
    }

    // ─── API Call Helpers ───────────────────────────────────────────────

    private class ApiResult
    {
        public int StatusCode { get; set; }
        public string Body { get; set; }
    }

    private async Task<string> CallAppFrameworkApi(string environmentId, HttpMethod method, string path, string body = null)
    {
        var result = await CallAppFrameworkRaw(environmentId, method, path, body).ConfigureAwait(false);
        return result.Body;
    }

    private async Task<ApiResult> CallAppFrameworkRaw(string environmentId, HttpMethod method, string path, string body = null)
    {
        var url = $"https://{BuildEnvironmentHost(environmentId)}{path}";
        return await SendAsync(method, url, body, "Copilot Managed Runtime API").ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a binary payload to the environment-scoped API. Used by the artifact
    /// upload, which sends a zip rather than JSON. Errors are returned rather than
    /// thrown so the caller can map status codes onto actionable messages.
    /// </summary>
    private async Task<ApiResult> CallAppFrameworkBinary(
        string environmentId, HttpMethod method, string path, byte[] payload, string contentType)
    {
        var url = $"https://{BuildEnvironmentHost(environmentId)}{path}";

        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        return await SendContentAsync(
            method, url, content, "Copilot Managed Runtime API", throwOnError: false).ConfigureAwait(false);
    }

    private async Task<string> CallGlobalApi(HttpMethod method, string path, string body = null)
    {
        var result = await SendAsync(method, $"{GLOBAL_API_BASE}{path}", body, "Power Platform API").ConfigureAwait(false);
        return result.Body;
    }

    private async Task<ApiResult> SendAsync(HttpMethod method, string url, string body, string apiLabel)
    {
        HttpContent content = body != null
            ? new StringContent(body, Encoding.UTF8, "application/json")
            : null;

        return await SendContentAsync(method, url, content, apiLabel).ConfigureAwait(false);
    }

    private async Task<ApiResult> SendContentAsync(
        HttpMethod method, string url, HttpContent content, string apiLabel, bool throwOnError = true)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        request.Headers.Add("Accept", "application/json");

        if (content != null)
        {
            request.Content = content;
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"{apiLabel} error {response.StatusCode}: {responseBody}");

            if (throwOnError)
            {
                throw new InvalidOperationException(
                    $"{apiLabel} returned {(int)response.StatusCode} {response.ReasonPhrase}: {TruncateForError(responseBody)}"
                );
            }
        }

        return new ApiResult { StatusCode = (int)response.StatusCode, Body = responseBody };
    }

    // ─── Projection Helpers ─────────────────────────────────────────────

    private static JObject ProjectApp(JToken app)
    {
        return new JObject
        {
            ["id"] = app["id"] ?? app["name"],
            ["name"] = app["name"],
            ["displayName"] = app["displayName"] ?? app["properties"]?["displayName"],
            ["description"] = app["description"] ?? app["properties"]?["description"],
            ["environmentId"] = app["environmentId"] ?? app["properties"]?["environmentId"],
            ["repositoryId"] = app["repositoryId"] ?? app["properties"]?["repositoryId"],
            ["location"] = app["location"] ?? app["properties"]?["location"]
        };
    }

    private static JObject ProjectConnector(JToken connector)
    {
        return new JObject
        {
            ["name"] = connector["name"],
            ["id"] = connector["id"],
            ["displayName"] = connector["properties"]?["displayName"] ?? connector["displayName"],
            ["isCustomApi"] = connector["properties"]?["isCustomApi"] ?? connector["isCustomApi"],
            ["capabilities"] = connector["properties"]?["capabilities"] ?? connector["capabilities"] ?? new JArray()
        };
    }

    private static JToken ReadEnvironmentId(JToken env)
    {
        return env["id"] ?? env["name"];
    }

    private static JToken ReadEnvironmentDisplayName(JToken env)
    {
        return env["displayName"]
            ?? env["properties"]?["displayName"]
            ?? env["name"]
            ?? env["id"];
    }

    /// <summary>
    /// Pulls the $skiptoken out of a nextLink so callers can page without
    /// reconstructing the full URL.
    /// </summary>
    private static string ExtractSkipToken(string nextLink)
    {
        if (string.IsNullOrEmpty(nextLink)) return null;

        try
        {
            var query = new Uri(nextLink).Query;
            return System.Web.HttpUtility.ParseQueryString(query)["$skiptoken"];
        }
        catch
        {
            return null;
        }
    }

    private static JToken ParseJsonOrWrap(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new JObject();

        try
        {
            return JToken.Parse(response);
        }
        catch (JsonException)
        {
            return new JObject { ["response"] = response };
        }
    }

    private static string RequireArg(JObject arguments, string name)
    {
        var value = arguments[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.");
        return value;
    }

    // ─── Request Helpers ────────────────────────────────────────────────

    private async Task<JObject> ReadRequestBodyAsync()
    {
        if (this.Context.Request.Content == null)
            return new JObject();

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return new JObject();

        try
        {
            return JObject.Parse(body);
        }
        catch (JsonException)
        {
            return new JObject();
        }
    }

    private string GetQueryParam(string name)
    {
        var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        return query[name];
    }

    /// <summary>
    /// Reads a zero-indexed segment of the inbound request path, so typed operations
    /// can recover the path parameters the platform substituted into the URL.
    /// </summary>
    private string GetPathSegment(int index)
    {
        var segments = this.Context.Request.RequestUri.AbsolutePath
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        if (index >= segments.Length)
            throw new ArgumentException($"Expected a path segment at position {index} but the request path was '{this.Context.Request.RequestUri.AbsolutePath}'.");

        return Uri.UnescapeDataString(segments[index]);
    }

    // ─── Response Helpers ───────────────────────────────────────────────

    private HttpResponseMessage CreateTypedResponse(JToken result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                result.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    // ─── JSON-RPC Helpers ───────────────────────────────────────────────

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
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                response.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    // ─── Application Insights ───────────────────────────────────────────

    private async Task LogToAppInsights(string eventName, Dictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_CONNECTION_STRING) || APP_INSIGHTS_CONNECTION_STRING.Contains("INSERT_YOUR"))
            return;

        try
        {
            var iKey = "";
            foreach (var part in APP_INSIGHTS_CONNECTION_STRING.Split(';'))
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    iKey = part.Substring("InstrumentationKey=".Length);
                    break;
                }
            }
            if (string.IsNullOrEmpty(iKey)) return;

            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = iKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = $"{SERVER_NAME}.{eventName}",
                        ["properties"] = properties != null
                            ? JObject.FromObject(properties)
                            : new JObject()
                    }
                }
            };

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT);
            telemetryRequest.Content = new StringContent(
                telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }

    // ─── Utility ────────────────────────────────────────────────────────

    private string TruncateForError(string text, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "... (truncated)";
    }
}

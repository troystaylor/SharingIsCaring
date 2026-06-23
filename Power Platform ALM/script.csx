using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    // MCP Server metadata
    private const string SERVER_NAME = "power-platform-alm-mcp";
    private const string SERVER_VERSION = "1.0.0";

    // Power Platform Admin API (for environment resolution)
    private const string ADMIN_API_BASE = "https://api.powerplatform.com";
    private const string ENV_API_VERSION = "2024-10-01";

    // Dataverse API version
    private const string DATAVERSE_API_VERSION = "v9.2";

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

                // Solutions
                case "ListSolutions":
                    return await HandleTypedListSolutions().ConfigureAwait(false);
                case "GetSolution":
                    return await HandleTypedGetSolution().ConfigureAwait(false);
                case "ExportSolution":
                    return await HandleTypedExportSolution().ConfigureAwait(false);
                case "ImportSolution":
                    return await HandleTypedImportSolution().ConfigureAwait(false);
                case "PublishCustomizations":
                    return await HandleTypedPublishCustomizations().ConfigureAwait(false);
                case "DeleteSolution":
                    return await HandleTypedDeleteSolution().ConfigureAwait(false);
                case "SetSolutionVersion":
                    return await HandleTypedSetSolutionVersion().ConfigureAwait(false);
                case "CloneAsPatch":
                    return await HandleTypedCloneAsPatch().ConfigureAwait(false);
                case "CloneAsSolution":
                    return await HandleTypedCloneAsSolution().ConfigureAwait(false);
                case "DeleteAndPromote":
                    return await HandleTypedDeleteAndPromote().ConfigureAwait(false);

                // Pipelines
                case "ListPipelines":
                    return await HandleTypedListPipelines().ConfigureAwait(false);
                case "ListPipelineStages":
                    return await HandleTypedListPipelineStages().ConfigureAwait(false);
                case "DeployToPipeline":
                    return await HandleTypedDeployToPipeline().ConfigureAwait(false);
                case "GetDeploymentStatus":
                    return await HandleTypedGetDeploymentStatus().ConfigureAwait(false);
                case "ListDeploymentHistory":
                    return await HandleTypedListDeploymentHistory().ConfigureAwait(false);

                // Dropdowns
                case "GetEnvironmentDropdown":
                    return await HandleTypedGetEnvironmentDropdown().ConfigureAwait(false);
                case "GetSolutionDropdown":
                    return await HandleTypedGetSolutionDropdown().ConfigureAwait(false);
                case "GetPipelineDropdown":
                    return await HandleTypedGetPipelineDropdown().ConfigureAwait(false);

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
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

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
        return new JArray
        {
            // Solutions
            new JObject
            {
                ["name"] = "alm_list_solutions",
                ["description"] = "List all solutions in a Power Platform environment. Returns unique name, display name, version, managed status, publisher, and timestamps. Use this first to discover solution names for other tools.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID). Use alm_list_solutions after selecting from environment list."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "alm_get_solution",
                ["description"] = "Get details of a specific solution by unique name. Returns version, components, publisher, description, and install date.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        },
                        ["solutionUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The solution unique name (not display name)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "solutionUniqueName" }
                }
            },
            new JObject
            {
                ["name"] = "alm_export_solution",
                ["description"] = "Export a solution from an environment as managed or unmanaged zip. Returns base64-encoded zip file. Use managed=true for deployment to non-dev environments.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The source environment ID (GUID)."
                        },
                        ["solutionName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The solution unique name to export."
                        },
                        ["managed"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Export as managed (true) or unmanaged (false). Default: true."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "solutionName" }
                }
            },
            new JObject
            {
                ["name"] = "alm_import_solution",
                ["description"] = "Import a solution zip file into a target environment. Provide the base64-encoded zip. The import runs asynchronously — use the returned job ID to track progress.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The target environment ID (GUID)."
                        },
                        ["customizationFile"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Base64-encoded solution zip file."
                        },
                        ["overwriteUnmanagedCustomizations"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to overwrite unmanaged customizations. Default: false."
                        },
                        ["publishWorkflows"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Whether to publish workflows after import. Default: true."
                        },
                        ["holdingSolution"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Import as holding solution staged for upgrade. Default: false."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "customizationFile" }
                }
            },
            new JObject
            {
                ["name"] = "alm_publish_customizations",
                ["description"] = "Publish all customizations in an environment. Makes all unpublished changes live. Run after importing unmanaged solutions or making customization changes.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "alm_delete_solution",
                ["description"] = "Delete a solution from an environment. This removes the solution container but not components shared with other solutions. This is a destructive operation — confirm with the user first.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        },
                        ["solutionUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The solution unique name to delete."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "solutionUniqueName" }
                }
            },
            new JObject
            {
                ["name"] = "alm_set_solution_version",
                ["description"] = "Update the version number of an unmanaged solution. Use format like 1.0.0.1. Version must be higher than current for pipeline deployments.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        },
                        ["solutionUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The solution unique name."
                        },
                        ["version"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "New version number (e.g., 1.0.0.1)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "solutionUniqueName", "version" }
                }
            },
            new JObject
            {
                ["name"] = "alm_clone_as_patch",
                ["description"] = "Create a patch solution for an existing solution. Patches contain only changed components and are smaller than full solutions. The patch version is auto-incremented from the parent.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        },
                        ["parentSolutionUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The parent solution unique name to create a patch for."
                        },
                        ["displayName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Display name for the patch solution."
                        },
                        ["versionNumber"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Version number for the patch (e.g., 1.0.1.0)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "parentSolutionUniqueName", "displayName" }
                }
            },
            new JObject
            {
                ["name"] = "alm_clone_as_solution",
                ["description"] = "Clone an existing solution as a new independent solution. Used for branching or forking a solution for parallel development.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        },
                        ["parentSolutionUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The source solution unique name to clone."
                        },
                        ["displayName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Display name for the new cloned solution."
                        },
                        ["versionNumber"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Version number for the clone (e.g., 2.0.0.0)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "parentSolutionUniqueName", "displayName" }
                }
            },
            new JObject
            {
                ["name"] = "alm_delete_and_promote",
                ["description"] = "Delete the base solution and promote the holding solution to complete a staged upgrade. Use this after importing with holdingSolution=true. This is a destructive operation — confirm with the user first.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID)."
                        },
                        ["solutionUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The solution unique name to promote (the holding solution's base name)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "solutionUniqueName" }
                }
            },

            // Pipelines
            new JObject
            {
                ["name"] = "alm_list_pipelines",
                ["description"] = "List deployment pipelines configured in a pipeline host environment. Returns pipeline name, description, and stage count.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline host environment ID (GUID)."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "alm_list_pipeline_stages",
                ["description"] = "List deployment stages for a pipeline. Returns stage name, order, and target environment details.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline host environment ID (GUID)."
                        },
                        ["pipelineId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline ID (GUID)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "pipelineId" }
                }
            },
            new JObject
            {
                ["name"] = "alm_deploy_to_pipeline",
                ["description"] = "Trigger a deployment through a pipeline stage. Deploys the specified solution to the target environment of the selected stage. This is a destructive operation — confirm with the user first.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline host environment ID (GUID)."
                        },
                        ["pipelineId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline ID (GUID)."
                        },
                        ["stageId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The target stage ID (GUID)."
                        },
                        ["solutionName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The solution unique name to deploy."
                        },
                        ["currentVersion"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Current solution version (e.g., 1.0.0.0). Required by pipeline."
                        },
                        ["newVersion"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "New solution version to deploy (e.g., 1.0.0.1). Required by pipeline."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "pipelineId", "stageId", "solutionName" }
                }
            },
            new JObject
            {
                ["name"] = "alm_get_deployment_status",
                ["description"] = "Get the status of a deployment run. Returns status (Queued, Running, Succeeded, Failed), timestamps, and error details if applicable.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline host environment ID (GUID)."
                        },
                        ["deploymentRunId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The deployment run ID (GUID)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "deploymentRunId" }
                }
            },
            new JObject
            {
                ["name"] = "alm_list_deployment_history",
                ["description"] = "List past deployment runs for a pipeline. Returns run status, solution, stage, timestamps, and requesting user.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline host environment ID (GUID)."
                        },
                        ["pipelineId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The pipeline ID (GUID)."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "pipelineId" }
                }
            }
        };
    }

    // ─── MCP Tool Call Router ───────────────────────────────────────────

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
                // Solutions
                case "alm_list_solutions":
                    result = await HandleListSolutions(arguments).ConfigureAwait(false);
                    break;
                case "alm_get_solution":
                    result = await HandleGetSolution(arguments).ConfigureAwait(false);
                    break;
                case "alm_export_solution":
                    result = await HandleExportSolution(arguments).ConfigureAwait(false);
                    break;
                case "alm_import_solution":
                    result = await HandleImportSolution(arguments).ConfigureAwait(false);
                    break;
                case "alm_publish_customizations":
                    result = await HandlePublishCustomizations(arguments).ConfigureAwait(false);
                    break;
                case "alm_delete_solution":
                    result = await HandleDeleteSolution(arguments).ConfigureAwait(false);
                    break;
                case "alm_set_solution_version":
                    result = await HandleSetSolutionVersion(arguments).ConfigureAwait(false);
                    break;
                case "alm_clone_as_patch":
                    result = await HandleCloneAsPatch(arguments).ConfigureAwait(false);
                    break;
                case "alm_clone_as_solution":
                    result = await HandleCloneAsSolution(arguments).ConfigureAwait(false);
                    break;
                case "alm_delete_and_promote":
                    result = await HandleDeleteAndPromote(arguments).ConfigureAwait(false);
                    break;

                // Pipelines
                case "alm_list_pipelines":
                    result = await HandleListPipelines(arguments).ConfigureAwait(false);
                    break;
                case "alm_list_pipeline_stages":
                    result = await HandleListPipelineStages(arguments).ConfigureAwait(false);
                    break;
                case "alm_deploy_to_pipeline":
                    result = await HandleDeployToPipeline(arguments).ConfigureAwait(false);
                    break;
                case "alm_get_deployment_status":
                    result = await HandleGetDeploymentStatus(arguments).ConfigureAwait(false);
                    break;
                case "alm_list_deployment_history":
                    result = await HandleListDeploymentHistory(arguments).ConfigureAwait(false);
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

    // ─── Environment Resolution ─────────────────────────────────────────

    private async Task<string> ResolveDataverseUrl(string environmentId)
    {
        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/environmentmanagement/environments/{environmentId}?api-version={ENV_API_VERSION}"
        ).ConfigureAwait(false);

        var env = JObject.Parse(response);
        var orgUrl = env["properties"]?["linkedEnvironmentMetadata"]?["instanceUrl"]?.ToString();

        if (string.IsNullOrEmpty(orgUrl))
            throw new InvalidOperationException($"Environment {environmentId} does not have a Dataverse instance URL. Ensure Dataverse is provisioned.");

        return orgUrl.TrimEnd('/');
    }

    // ─── Solution Handlers ──────────────────────────────────────────────

    private async Task<JToken> HandleListSolutions(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/solutions?$select=solutionid,uniquename,friendlyname,version,ismanaged,_publisherid_value,createdon,modifiedon&$filter=isvisible eq true&$orderby=friendlyname asc"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var solutions = data["value"] as JArray ?? new JArray();

        var summary = new JArray();
        foreach (var sol in solutions)
        {
            summary.Add(new JObject
            {
                ["solutionId"] = sol["solutionid"],
                ["uniqueName"] = sol["uniquename"],
                ["friendlyName"] = sol["friendlyname"],
                ["version"] = sol["version"],
                ["isManaged"] = sol["ismanaged"],
                ["publisherName"] = sol["_publisherid_value@OData.Community.Display.V1.FormattedValue"] ?? sol["_publisherid_value"],
                ["createdOn"] = sol["createdon"],
                ["modifiedOn"] = sol["modifiedon"]
            });
        }

        return new JObject
        {
            ["solutionCount"] = summary.Count,
            ["solutions"] = summary
        };
    }

    private async Task<JToken> HandleGetSolution(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var solutionName = arguments["solutionUniqueName"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(solutionName)) throw new ArgumentException("solutionUniqueName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/solutions?$filter=uniquename eq '{Uri.EscapeDataString(solutionName)}'&$select=solutionid,uniquename,friendlyname,version,ismanaged,description,_publisherid_value,installedon,createdon,modifiedon"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var solutions = data["value"] as JArray;
        if (solutions == null || solutions.Count == 0)
            throw new InvalidOperationException($"Solution '{solutionName}' not found in environment {envId}.");

        var sol = solutions[0];
        return new JObject
        {
            ["solutionId"] = sol["solutionid"],
            ["uniqueName"] = sol["uniquename"],
            ["friendlyName"] = sol["friendlyname"],
            ["version"] = sol["version"],
            ["isManaged"] = sol["ismanaged"],
            ["publisherName"] = sol["_publisherid_value@OData.Community.Display.V1.FormattedValue"] ?? sol["_publisherid_value"],
            ["description"] = sol["description"],
            ["installedOn"] = sol["installedon"],
            ["createdOn"] = sol["createdon"],
            ["modifiedOn"] = sol["modifiedon"]
        };
    }

    private async Task<JToken> HandleExportSolution(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var solutionName = arguments["solutionName"]?.ToString();
        var managed = arguments["managed"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(solutionName)) throw new ArgumentException("solutionName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var exportBody = new JObject
        {
            ["SolutionName"] = solutionName,
            ["Managed"] = managed
        };

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/ExportSolution",
            exportBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var result = JObject.Parse(response);
        var exportedFile = result["ExportSolutionFile"]?.ToString() ?? "";

        // Calculate approximate size from base64
        var sizeBytes = (int)(exportedFile.Length * 3.0 / 4.0);

        return new JObject
        {
            ["solutionName"] = solutionName,
            ["managed"] = managed,
            ["exportedFile"] = exportedFile,
            ["fileSizeBytes"] = sizeBytes
        };
    }

    private async Task<JToken> HandleImportSolution(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var customizationFile = arguments["customizationFile"]?.ToString();
        var overwrite = arguments["overwriteUnmanagedCustomizations"]?.Value<bool>() ?? false;
        var publishWorkflows = arguments["publishWorkflows"]?.Value<bool>() ?? true;
        var holdingSolution = arguments["holdingSolution"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(customizationFile)) throw new ArgumentException("customizationFile is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var importBody = new JObject
        {
            ["OverwriteUnmanagedCustomizations"] = overwrite,
            ["PublishWorkflows"] = publishWorkflows,
            ["CustomizationFile"] = customizationFile,
            ["HoldingSolution"] = holdingSolution
        };

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/ImportSolution",
            importBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        // ImportSolution returns an import job ID in the response
        var result = new JObject
        {
            ["status"] = "importStarted",
            ["environmentId"] = envId,
            ["message"] = "Solution import has been initiated. The operation may take several minutes to complete."
        };

        if (!string.IsNullOrEmpty(response))
        {
            try
            {
                var parsed = JObject.Parse(response);
                result["importJobId"] = parsed["ImportJobKey"] ?? parsed["importjobid"];
            }
            catch { /* response may be empty for sync imports */ }
        }

        return result;
    }

    private async Task<JToken> HandlePublishCustomizations(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/PublishAllXml"
        ).ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["environmentId"] = envId,
            ["message"] = "All customizations have been published."
        };
    }

    private async Task<JToken> HandleDeleteSolution(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var solutionName = arguments["solutionUniqueName"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(solutionName)) throw new ArgumentException("solutionUniqueName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        // First, find the solution ID
        var lookupResponse = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/solutions?$filter=uniquename eq '{Uri.EscapeDataString(solutionName)}'&$select=solutionid"
        ).ConfigureAwait(false);

        var lookupData = JObject.Parse(lookupResponse);
        var solutions = lookupData["value"] as JArray;
        if (solutions == null || solutions.Count == 0)
            throw new InvalidOperationException($"Solution '{solutionName}' not found.");

        var solutionId = solutions[0]["solutionid"]?.ToString();

        await CallDataverseApi(
            orgUrl,
            HttpMethod.Delete,
            $"/api/data/{DATAVERSE_API_VERSION}/solutions({solutionId})"
        ).ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["solutionUniqueName"] = solutionName,
            ["message"] = $"Solution '{solutionName}' has been deleted."
        };
    }

    private async Task<JToken> HandleSetSolutionVersion(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var solutionName = arguments["solutionUniqueName"]?.ToString();
        var version = arguments["version"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(solutionName)) throw new ArgumentException("solutionUniqueName is required.");
        if (string.IsNullOrEmpty(version)) throw new ArgumentException("version is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        // Find the solution ID
        var lookupResponse = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/solutions?$filter=uniquename eq '{Uri.EscapeDataString(solutionName)}'&$select=solutionid"
        ).ConfigureAwait(false);

        var lookupData = JObject.Parse(lookupResponse);
        var solutions = lookupData["value"] as JArray;
        if (solutions == null || solutions.Count == 0)
            throw new InvalidOperationException($"Solution '{solutionName}' not found.");

        var solutionId = solutions[0]["solutionid"]?.ToString();

        var updateBody = new JObject { ["version"] = version };
        await CallDataverseApi(
            orgUrl,
            new HttpMethod("PATCH"),
            $"/api/data/{DATAVERSE_API_VERSION}/solutions({solutionId})",
            updateBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["solutionUniqueName"] = solutionName,
            ["version"] = version,
            ["message"] = $"Solution '{solutionName}' version updated to {version}."
        };
    }

    private async Task<JToken> HandleCloneAsPatch(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var parentName = arguments["parentSolutionUniqueName"]?.ToString();
        var displayName = arguments["displayName"]?.ToString();
        var versionNumber = arguments["versionNumber"]?.ToString();

        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(parentName)) throw new ArgumentException("parentSolutionUniqueName is required.");
        if (string.IsNullOrEmpty(displayName)) throw new ArgumentException("displayName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        // Resolve parent solution ID
        var lookupResponse = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/solutions?$filter=uniquename eq '{Uri.EscapeDataString(parentName)}'&$select=solutionid"
        ).ConfigureAwait(false);

        var lookupData = JObject.Parse(lookupResponse);
        var solutions = lookupData["value"] as JArray;
        if (solutions == null || solutions.Count == 0)
            throw new InvalidOperationException($"Solution '{parentName}' not found.");

        var parentSolutionId = solutions[0]["solutionid"]?.ToString();

        var cloneBody = new JObject
        {
            ["ParentSolutionUniqueName"] = parentName,
            ["DisplayName"] = displayName
        };
        if (!string.IsNullOrEmpty(versionNumber)) cloneBody["VersionNumber"] = versionNumber;

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/CloneAsPatch",
            cloneBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var result = JObject.Parse(response);
        return new JObject
        {
            ["solutionId"] = result["SolutionId"],
            ["uniqueName"] = result["UniqueName"] ?? result["SolutionUniqueName"],
            ["version"] = result["Version"] ?? result["SolutionVersion"],
            ["message"] = $"Patch solution created for '{parentName}'."
        };
    }

    private async Task<JToken> HandleCloneAsSolution(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var parentName = arguments["parentSolutionUniqueName"]?.ToString();
        var displayName = arguments["displayName"]?.ToString();
        var versionNumber = arguments["versionNumber"]?.ToString();

        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(parentName)) throw new ArgumentException("parentSolutionUniqueName is required.");
        if (string.IsNullOrEmpty(displayName)) throw new ArgumentException("displayName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var cloneBody = new JObject
        {
            ["ParentSolutionUniqueName"] = parentName,
            ["DisplayName"] = displayName
        };
        if (!string.IsNullOrEmpty(versionNumber)) cloneBody["VersionNumber"] = versionNumber;

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/CloneAsSolution",
            cloneBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var result = JObject.Parse(response);
        return new JObject
        {
            ["solutionId"] = result["SolutionId"],
            ["uniqueName"] = result["UniqueName"] ?? result["SolutionUniqueName"],
            ["version"] = result["Version"] ?? result["SolutionVersion"],
            ["message"] = $"Solution cloned from '{parentName}' as '{displayName}'."
        };
    }

    private async Task<JToken> HandleDeleteAndPromote(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var solutionName = arguments["solutionUniqueName"]?.ToString();

        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(solutionName)) throw new ArgumentException("solutionUniqueName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var promoteBody = new JObject
        {
            ["UniqueName"] = solutionName
        };

        await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/DeleteAndPromote",
            promoteBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["solutionUniqueName"] = solutionName,
            ["message"] = $"Holding solution for '{solutionName}' has been promoted. The base solution was deleted and replaced."
        };
    }

    // ─── Pipeline Handlers ──────────────────────────────────────────────

    private async Task<JToken> HandleListPipelines(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/deploymentpipelines?$select=deploymentpipelineid,name,description,createdon"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var pipelines = data["value"] as JArray ?? new JArray();

        var summary = new JArray();
        foreach (var p in pipelines)
        {
            summary.Add(new JObject
            {
                ["pipelineId"] = p["deploymentpipelineid"],
                ["name"] = p["name"],
                ["description"] = p["description"],
                ["createdOn"] = p["createdon"]
            });
        }

        return new JObject
        {
            ["pipelineCount"] = summary.Count,
            ["pipelines"] = summary
        };
    }

    private async Task<JToken> HandleListPipelineStages(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var pipelineId = arguments["pipelineId"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(pipelineId)) throw new ArgumentException("pipelineId is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/deploymentstages?$filter=_deploymentpipelineid_value eq '{pipelineId}'&$select=deploymentstageid,name,deploymentstageorder,_targetdeploymentenvironmentid_value&$orderby=deploymentstageorder asc"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var stages = data["value"] as JArray ?? new JArray();

        var summary = new JArray();
        foreach (var s in stages)
        {
            summary.Add(new JObject
            {
                ["stageId"] = s["deploymentstageid"],
                ["name"] = s["name"],
                ["order"] = s["deploymentstageorder"],
                ["targetEnvironmentId"] = s["_targetdeploymentenvironmentid_value"],
                ["targetEnvironmentName"] = s["_targetdeploymentenvironmentid_value@OData.Community.Display.V1.FormattedValue"]
            });
        }

        return new JObject
        {
            ["stageCount"] = summary.Count,
            ["stages"] = summary
        };
    }

    private async Task<JToken> HandleDeployToPipeline(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var pipelineId = arguments["pipelineId"]?.ToString();
        var stageId = arguments["stageId"]?.ToString();
        var solutionName = arguments["solutionName"]?.ToString();

        var currentVersion = arguments["currentVersion"]?.ToString();
        var newVersion = arguments["newVersion"]?.ToString();

        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(pipelineId)) throw new ArgumentException("pipelineId is required.");
        if (string.IsNullOrEmpty(stageId)) throw new ArgumentException("stageId is required.");
        if (string.IsNullOrEmpty(solutionName)) throw new ArgumentException("solutionName is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        // Create a deployment run record
        var runBody = new JObject
        {
            ["name"] = $"Deploy {solutionName}",
            ["DeploymentPipelineId@odata.bind"] = $"/deploymentpipelines({pipelineId})",
            ["DeploymentStageId@odata.bind"] = $"/deploymentstages({stageId})",
            ["ArtifactName"] = solutionName
        };
        if (!string.IsNullOrEmpty(currentVersion)) runBody["CurrentVersion"] = currentVersion;
        if (!string.IsNullOrEmpty(newVersion)) runBody["NewVersion"] = newVersion;

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Post,
            $"/api/data/{DATAVERSE_API_VERSION}/deploymentstageruns",
            runBody.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var result = new JObject
        {
            ["status"] = "deploymentStarted",
            ["pipelineId"] = pipelineId,
            ["stageId"] = stageId,
            ["solutionName"] = solutionName,
            ["message"] = $"Deployment of '{solutionName}' has been initiated. Use alm_get_deployment_status to track progress."
        };

        if (!string.IsNullOrEmpty(response))
        {
            try
            {
                var parsed = JObject.Parse(response);
                result["deploymentRunId"] = parsed["deploymentstagerunid"];
            }
            catch { }
        }

        return result;
    }

    private async Task<JToken> HandleGetDeploymentStatus(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var runId = arguments["deploymentRunId"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(runId)) throw new ArgumentException("deploymentRunId is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/deploymentstageruns({runId})?$select=deploymentstagerunid,name,statuscode,statecode,createdon,completedon,_deploymentpipelineid_value,_deploymentstageid_value"
        ).ConfigureAwait(false);

        var run = JObject.Parse(response);

        return new JObject
        {
            ["deploymentRunId"] = run["deploymentstagerunid"],
            ["status"] = run["statuscode@OData.Community.Display.V1.FormattedValue"] ?? run["statuscode"],
            ["solutionName"] = run["name"],
            ["stageName"] = run["_deploymentstageid_value@OData.Community.Display.V1.FormattedValue"],
            ["startedOn"] = run["createdon"],
            ["completedOn"] = run["completedon"],
            ["errorMessage"] = run["errormessage"]
        };
    }

    private async Task<JToken> HandleListDeploymentHistory(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var pipelineId = arguments["pipelineId"]?.ToString();
        if (string.IsNullOrEmpty(envId)) throw new ArgumentException("environmentId is required.");
        if (string.IsNullOrEmpty(pipelineId)) throw new ArgumentException("pipelineId is required.");

        var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

        var response = await CallDataverseApi(
            orgUrl,
            HttpMethod.Get,
            $"/api/data/{DATAVERSE_API_VERSION}/deploymentstageruns?$filter=_deploymentpipelineid_value eq '{pipelineId}'&$select=deploymentstagerunid,name,statuscode,_deploymentstageid_value,createdon,completedon,_createdby_value&$orderby=createdon desc&$top=50"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var runs = data["value"] as JArray ?? new JArray();

        var summary = new JArray();
        foreach (var r in runs)
        {
            summary.Add(new JObject
            {
                ["deploymentRunId"] = r["deploymentstagerunid"],
                ["status"] = r["statuscode@OData.Community.Display.V1.FormattedValue"] ?? r["statuscode"],
                ["solutionName"] = r["name"],
                ["stageName"] = r["_deploymentstageid_value@OData.Community.Display.V1.FormattedValue"],
                ["startedOn"] = r["createdon"],
                ["completedOn"] = r["completedon"],
                ["requestedBy"] = r["_createdby_value@OData.Community.Display.V1.FormattedValue"]
            });
        }

        return new JObject
        {
            ["runCount"] = summary.Count,
            ["runs"] = summary
        };
    }

    // ─── Typed Operations (Power Automate) ──────────────────────────────

    private async Task<HttpResponseMessage> HandleTypedListSolutions()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var result = await HandleListSolutions(new JObject { ["environmentId"] = envId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetSolution()
    {
        var envId = GetQueryParam("environmentId");
        var name = GetQueryParam("solutionUniqueName");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);
        if (string.IsNullOrEmpty(name)) return CreateTypedErrorResponse("solutionUniqueName is required.", 400);

        var result = await HandleGetSolution(new JObject { ["environmentId"] = envId, ["solutionUniqueName"] = name }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedExportSolution()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleExportSolution(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedImportSolution()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleImportSolution(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedPublishCustomizations()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var result = await HandlePublishCustomizations(new JObject { ["environmentId"] = envId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedDeleteSolution()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleDeleteSolution(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedSetSolutionVersion()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleSetSolutionVersion(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedCloneAsPatch()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleCloneAsPatch(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedCloneAsSolution()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleCloneAsSolution(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedDeleteAndPromote()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleDeleteAndPromote(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListPipelines()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var result = await HandleListPipelines(new JObject { ["environmentId"] = envId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListPipelineStages()
    {
        var envId = GetQueryParam("environmentId");
        var pipelineId = GetQueryParam("pipelineId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);
        if (string.IsNullOrEmpty(pipelineId)) return CreateTypedErrorResponse("pipelineId is required.", 400);

        var result = await HandleListPipelineStages(new JObject { ["environmentId"] = envId, ["pipelineId"] = pipelineId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedDeployToPipeline()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        bodyObj["environmentId"] = envId;

        var result = await HandleDeployToPipeline(bodyObj).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetDeploymentStatus()
    {
        var envId = GetQueryParam("environmentId");
        var runId = GetQueryParam("deploymentRunId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);
        if (string.IsNullOrEmpty(runId)) return CreateTypedErrorResponse("deploymentRunId is required.", 400);

        var result = await HandleGetDeploymentStatus(new JObject { ["environmentId"] = envId, ["deploymentRunId"] = runId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListDeploymentHistory()
    {
        var envId = GetQueryParam("environmentId");
        var pipelineId = GetQueryParam("pipelineId");
        if (string.IsNullOrEmpty(envId)) return CreateTypedErrorResponse("environmentId is required.", 400);
        if (string.IsNullOrEmpty(pipelineId)) return CreateTypedErrorResponse("pipelineId is required.", 400);

        var result = await HandleListDeploymentHistory(new JObject { ["environmentId"] = envId, ["pipelineId"] = pipelineId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    // ─── Dropdown Handlers ──────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleTypedGetEnvironmentDropdown()
    {
        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/environmentmanagement/environments?api-version={ENV_API_VERSION}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var environments = data["value"] as JArray ?? new JArray();

        var dropdown = new JArray();
        foreach (var env in environments)
        {
            var orgUrl = env["properties"]?["linkedEnvironmentMetadata"]?["instanceUrl"]?.ToString();
            if (!string.IsNullOrEmpty(orgUrl))
            {
                dropdown.Add(new JObject
                {
                    ["id"] = env["id"],
                    ["name"] = env["properties"]?["displayName"]?.ToString() ?? env["name"]?.ToString() ?? env["id"]?.ToString()
                });
            }
        }

        return CreateTypedResponse(dropdown);
    }

    private async Task<HttpResponseMessage> HandleTypedGetSolutionDropdown()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedResponse(new JArray());

        try
        {
            var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

            var response = await CallDataverseApi(
                orgUrl,
                HttpMethod.Get,
                $"/api/data/{DATAVERSE_API_VERSION}/solutions?$select=uniquename,friendlyname&$filter=isvisible eq true and ismanaged eq false&$orderby=friendlyname asc"
            ).ConfigureAwait(false);

            var data = JObject.Parse(response);
            var solutions = data["value"] as JArray ?? new JArray();

            var dropdown = new JArray();
            foreach (var sol in solutions)
            {
                dropdown.Add(new JObject
                {
                    ["uniqueName"] = sol["uniquename"],
                    ["friendlyName"] = sol["friendlyname"]
                });
            }

            return CreateTypedResponse(dropdown);
        }
        catch
        {
            return CreateTypedResponse(new JArray());
        }
    }

    private async Task<HttpResponseMessage> HandleTypedGetPipelineDropdown()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedResponse(new JArray());

        try
        {
            var orgUrl = await ResolveDataverseUrl(envId).ConfigureAwait(false);

            var response = await CallDataverseApi(
                orgUrl,
                HttpMethod.Get,
                $"/api/data/{DATAVERSE_API_VERSION}/deploymentpipelines?$select=deploymentpipelineid,name"
            ).ConfigureAwait(false);

            var data = JObject.Parse(response);
            var pipelines = data["value"] as JArray ?? new JArray();

            var dropdown = new JArray();
            foreach (var p in pipelines)
            {
                dropdown.Add(new JObject
                {
                    ["pipelineId"] = p["deploymentpipelineid"],
                    ["name"] = p["name"]
                });
            }

            return CreateTypedResponse(dropdown);
        }
        catch
        {
            return CreateTypedResponse(new JArray());
        }
    }

    // ─── API Call Helpers ───────────────────────────────────────────────

    private async Task<string> CallAdminApi(HttpMethod method, string path, string body = null)
    {
        var url = $"{ADMIN_API_BASE}{path}";
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        request.Headers.Add("Accept", "application/json");

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"Admin API error {response.StatusCode}: {responseBody}");
            throw new InvalidOperationException(
                $"Power Platform Admin API returned {(int)response.StatusCode} {response.ReasonPhrase}: {TruncateForError(responseBody)}"
            );
        }

        return responseBody;
    }

    private async Task<string> CallDataverseApi(string orgUrl, HttpMethod method, string path, string body = null)
    {
        var url = $"{orgUrl}{path}";
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("Prefer", "odata.include-annotations=*");

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"Dataverse API error {response.StatusCode}: {responseBody}");
            throw new InvalidOperationException(
                $"Dataverse API returned {(int)response.StatusCode} {response.ReasonPhrase}: {TruncateForError(responseBody)}"
            );
        }

        return responseBody;
    }

    // ─── Response Helpers ───────────────────────────────────────────────

    private string GetQueryParam(string name)
    {
        var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        return query[name];
    }

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

    private HttpResponseMessage CreateTypedErrorResponse(string message, int statusCode)
    {
        return new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(
                new JObject { ["error"] = message }.ToString(Newtonsoft.Json.Formatting.None),
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

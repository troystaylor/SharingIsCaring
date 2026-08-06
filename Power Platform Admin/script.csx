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
    private const string SERVER_NAME = "power-platform-admin-mcp";
    private const string SERVER_VERSION = "1.2.0";

    // Power Platform Admin API
    private const string ADMIN_API_BASE = "https://api.powerplatform.com";
    private const string ENV_API_VERSION = "2024-10-01";
    private const string SETTINGS_API_VERSION = "2022-03-01-preview";

    // Resource query (agent inventory)
    private const string RESOURCEQUERY_API_VERSION = "2022-03-01-preview";
    private const string AGENT_RESOURCE_TYPE = "'microsoft.copilotstudio/agents'";
    private const int AGENT_PAGE_SIZE = 1000;
    private const int AGENT_MAX_PAGES = 10;
    private const int EDITOR_BREADTH_THRESHOLD = 10;

    // Entra Agent IDs became mandatory for newly created agents in July 2026, so a gap after this date is a stronger signal.
    private static readonly DateTimeOffset ENTRA_AGENT_ID_MANDATE = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    // Licensing allocations API (unsupported endpoint, tenant-routed host)
    private const string TENANT_HOST_SUFFIX = ".tenant.api.powerplatform.com";
    private const string LICENSING_API_VERSION = "1";
    private static readonly string[] TENANT_POOL_ENTITLEMENTS = { "MCSMessages", "MCSSessions" };

    // Application Insights
    private const string APP_INSIGHTS_CONNECTION_STRING = "[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    private string _tenantId;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    return await HandleMcpEntryAsync().ConfigureAwait(false);
                case "ListEnvironments":
                    return await HandleTypedListEnvironments().ConfigureAwait(false);
                case "GetEnvironment":
                    return await HandleTypedGetEnvironment().ConfigureAwait(false);
                case "GetSettings":
                    return await HandleTypedGetSettings().ConfigureAwait(false);
                case "UpdateSettings":
                    return await HandleTypedUpdateSettings().ConfigureAwait(false);
                case "GetTenantPoolDraw":
                    return await HandleTypedGetTenantPoolDraw().ConfigureAwait(false);
                case "SetTenantPoolDraw":
                    return await HandleTypedSetTenantPoolDraw().ConfigureAwait(false);
                case "ListAgents":
                    return await HandleTypedListAgents().ConfigureAwait(false);
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

    // ─── Tool Definitions ───────────────────────────────────────────────

    private JArray GetToolDefinitions()
    {
        return new JArray
        {
            // Category 1: Environment Management
            new JObject
            {
                ["name"] = "admin_list_environments",
                ["description"] = "List all Power Platform environments the user can administer. Returns environment name, type, state, capacity metrics (Database, File, Log in MB), Dataverse URL, and update cadence. Use this first to discover environment IDs for other tools.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "admin_get_environment",
                ["description"] = "Get full details of a specific environment including properties, database settings, capacity breakdown, runtime endpoints, protection status, retention details, and virtual network configuration.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID). Use admin_list_environments to find this."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "admin_get_settings",
                ["description"] = "Get Power Platform admin center (PPAC) management settings for an environment. Returns settings like EnableIpBasedStorageAccessSignatureRule, LoggingEnabledForIpBasedStorageAccessSignature, and other toggles. Use $select to filter specific settings.",
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
                        ["select"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Comma-separated list of setting names to return. Omit to return all settings."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "admin_update_setting",
                ["description"] = "Update one or more PPAC management settings on an environment. Provide the setting names and their new values as key-value pairs. Example: {\"EnableIpBasedStorageAccessSignatureRule\": true}. This is a destructive operation — confirm with the user before executing.",
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
                        ["settings"] = new JObject
                        {
                            ["type"] = "object",
                            ["description"] = "Key-value pairs of setting names and their new values. Example: {\"EnableIpBasedStorageAccessSignatureRule\": true}"
                        }
                    },
                    ["required"] = new JArray { "environmentId", "settings" }
                }
            },
            new JObject
            {
                ["name"] = "admin_compare_settings",
                ["description"] = "Compare a specific management setting across all environments. Returns a table showing the setting value for each environment, making it easy to identify inconsistencies. Example: compare EnableIpBasedStorageAccessSignatureRule across all environments.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["settingName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The exact setting name to compare across environments."
                        }
                    },
                    ["required"] = new JArray { "settingName" }
                }
            },

            // Category 2: Governance & Security
            new JObject
            {
                ["name"] = "admin_get_copilot_governance",
                ["description"] = "Get Copilot governance features and settings for the tenant. Returns which Copilot features are enabled, deployment policies, and AI governance controls.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. The environment ID (GUID). Omit for tenant-level settings."
                        }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "admin_update_copilot_governance",
                ["description"] = "Update Copilot governance settings. This is a destructive operation — confirm with the user before executing.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. The environment ID (GUID). Omit for tenant-level settings."
                        },
                        ["settings"] = new JObject
                        {
                            ["type"] = "object",
                            ["description"] = "Key-value pairs of Copilot governance settings to update."
                        }
                    },
                    ["required"] = new JArray { "settings" }
                }
            },
            new JObject
            {
                ["name"] = "admin_get_tenant_pool_draw",
                ["description"] = "Check whether an environment draws Copilot Studio message and session capacity (MCSMessages, MCSSessions) from the tenant pool. An environment with no allocation document draws from the pool by default. Read-only. Note: this uses an unsupported licensing allocations endpoint that may change without notice.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID). Use admin_list_environments to find this."
                        }
                    },
                    ["required"] = new JArray { "environmentId" }
                }
            },
            new JObject
            {
                ["name"] = "admin_set_tenant_pool_draw",
                ["description"] = "Enable or disable whether an environment draws Copilot Studio message and session capacity (MCSMessages, MCSSessions) from the tenant pool. Reads the current allocation document first and changes only the TenantPool enforcement rule, preserving all other allocation and enforcement data. This is a destructive operation — confirm with the user before executing. Note: this uses an unsupported licensing allocations endpoint that may change without notice.",
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
                        ["enabled"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "true to draw from the tenant pool, false to stop drawing from it."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "enabled" }
                }
            },
            new JObject
            {
                ["name"] = "admin_get_security_recommendations",
                ["description"] = "Get security recommendations from Power Platform Advisor. Returns actionable recommendations for improving the security posture of your Power Platform tenant and environments.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. The environment ID (GUID) to scope recommendations. Omit for tenant-level recommendations."
                        }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "admin_get_cross_tenant_connections",
                ["description"] = "Get cross-tenant connection reports for compliance auditing. Returns connections that span tenant boundaries, useful for identifying data flow risks.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. The environment ID (GUID) to scope the report. Omit for all environments."
                        }
                    },
                    ["required"] = new JArray()
                }
            },

            // Category 3: Resource Inventory
            new JObject
            {
                ["name"] = "admin_list_connectors",
                ["description"] = "List connectors available in an environment. Returns connector name, type (certified, custom, virtual, MCP), publisher, and tier.",
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
                ["name"] = "admin_list_apps",
                ["description"] = "List Power Apps in an environment. Returns app name, owner, last modified date, and sharing status.",
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
                ["name"] = "admin_list_agents",
                ["description"] = "Inventory Copilot Studio and Agent Builder agents, enriched with environment, environment group, sharing, identity, and provenance detail. Each agent carries a computed riskLevel (None/Low/Medium/High) with the riskSignals that produced it. Omit environmentId to inventory the whole tenant. Note that isCLIAgent is 'true', 'false', or 'unknown' — 'unknown' means the platform did not report the field and must not be read as 'false'.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Restrict the inventory to a single environment. Omit to inventory every environment in the tenant."
                        }
                    },
                    ["required"] = new JArray()
                }
            },

            // Category 4: Application Lifecycle
            new JObject
            {
                ["name"] = "admin_install_package",
                ["description"] = "Install a Microsoft application package in an environment. Returns the operation ID for tracking installation progress. This is a destructive operation — confirm with the user before executing.",
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
                        ["packageUniqueName"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The unique name of the application package to install."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "packageUniqueName" }
                }
            }
        };
    }

    // ─── Tool Call Router ───────────────────────────────────────────────

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
                // Category 1: Environment Management
                case "admin_list_environments":
                    result = await HandleListEnvironments(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_environment":
                    result = await HandleGetEnvironment(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_settings":
                    result = await HandleGetSettings(arguments).ConfigureAwait(false);
                    break;
                case "admin_update_setting":
                    result = await HandleUpdateSetting(arguments).ConfigureAwait(false);
                    break;
                case "admin_compare_settings":
                    result = await HandleCompareSettings(arguments).ConfigureAwait(false);
                    break;

                // Category 2: Governance & Security
                case "admin_get_copilot_governance":
                    result = await HandleGetCopilotGovernance(arguments).ConfigureAwait(false);
                    break;
                case "admin_update_copilot_governance":
                    result = await HandleUpdateCopilotGovernance(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_tenant_pool_draw":
                    result = await HandleGetTenantPoolDraw(arguments).ConfigureAwait(false);
                    break;
                case "admin_set_tenant_pool_draw":
                    result = await HandleSetTenantPoolDraw(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_security_recommendations":
                    result = await HandleGetSecurityRecommendations(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_cross_tenant_connections":
                    result = await HandleGetCrossTenantConnections(arguments).ConfigureAwait(false);
                    break;

                // Category 3: Resource Inventory
                case "admin_list_connectors":
                    result = await HandleListConnectors(arguments).ConfigureAwait(false);
                    break;
                case "admin_list_apps":
                    result = await HandleListApps(arguments).ConfigureAwait(false);
                    break;
                case "admin_list_agents":
                    result = await HandleListAgents(arguments).ConfigureAwait(false);
                    break;

                // Category 4: Application Lifecycle
                case "admin_install_package":
                    result = await HandleInstallPackage(arguments).ConfigureAwait(false);
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

    // ─── Category 1: Environment Management ─────────────────────────────

    private async Task<JToken> HandleListEnvironments(JObject arguments)
    {
        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/environmentmanagement/environments?api-version={ENV_API_VERSION}"
        ).ConfigureAwait(false);

        var data = JObject.Parse(response);
        var environments = data["value"] as JArray ?? new JArray();

        var summary = new JArray();
        foreach (var env in environments)
        {
            var envSummary = new JObject
            {
                ["id"] = env["id"],
                ["name"] = env["properties"]?["displayName"] ?? env["name"],
                ["type"] = env["properties"]?["environmentType"],
                ["state"] = env["properties"]?["states"]?["management"]?["id"],
                ["location"] = env["location"],
                ["dataverseUrl"] = env["properties"]?["linkedEnvironmentMetadata"]?["instanceUrl"],
                ["updateCadence"] = env["properties"]?["updateCadence"]
            };

            var capacity = env["properties"]?["capacity"] as JArray;
            if (capacity != null)
            {
                var capacitySummary = new JObject();
                foreach (var cap in capacity)
                {
                    var capName = cap["capacityType"]?.ToString();
                    if (!string.IsNullOrEmpty(capName))
                    {
                        capacitySummary[capName] = new JObject
                        {
                            ["actualConsumption"] = cap["actualConsumption"],
                            ["ratedConsumption"] = cap["ratedConsumption"],
                            ["unit"] = cap["unit"]
                        };
                    }
                }
                envSummary["capacity"] = capacitySummary;
            }

            summary.Add(envSummary);
        }

        return new JObject
        {
            ["environmentCount"] = summary.Count,
            ["environments"] = summary
        };
    }

    private async Task<JToken> HandleGetEnvironment(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/environmentmanagement/environments/{envId}?api-version={ENV_API_VERSION}"
        ).ConfigureAwait(false);

        return JObject.Parse(response);
    }

    private async Task<JToken> HandleGetSettings(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var select = arguments["select"]?.ToString();
        var url = $"/environmentmanagement/environments/{envId}/settings?api-version={SETTINGS_API_VERSION}";
        if (!string.IsNullOrEmpty(select))
        {
            url += $"&$select={Uri.EscapeDataString(select)}";
        }

        var response = await CallAdminApi(HttpMethod.Get, url).ConfigureAwait(false);
        return JObject.Parse(response);
    }

    private async Task<JToken> HandleUpdateSetting(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var settings = arguments["settings"] as JObject;
        if (settings == null || settings.Count == 0)
            throw new ArgumentException("settings object with at least one key-value pair is required.");

        var response = await CallAdminApi(
            new HttpMethod("PATCH"),
            $"/environmentmanagement/environments/{envId}/settings?api-version={SETTINGS_API_VERSION}",
            settings.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["environmentId"] = envId,
            ["updatedSettings"] = settings,
            ["message"] = $"Successfully updated {settings.Count} setting(s)."
        };
    }

    private async Task<JToken> HandleCompareSettings(JObject arguments)
    {
        var settingName = arguments["settingName"]?.ToString();
        if (string.IsNullOrEmpty(settingName))
            throw new ArgumentException("settingName is required.");

        // Step 1: List all environments
        var envResponse = await CallAdminApi(
            HttpMethod.Get,
            $"/environmentmanagement/environments?api-version={ENV_API_VERSION}"
        ).ConfigureAwait(false);

        var envData = JObject.Parse(envResponse);
        var environments = envData["value"] as JArray ?? new JArray();

        // Step 2: Get the setting for each environment
        var comparison = new JArray();
        foreach (var env in environments)
        {
            var envId = env["id"]?.ToString();
            var envName = env["properties"]?["displayName"]?.ToString() ?? env["name"]?.ToString();

            try
            {
                var settingsResponse = await CallAdminApi(
                    HttpMethod.Get,
                    $"/environmentmanagement/environments/{envId}/settings?api-version={SETTINGS_API_VERSION}&$select={Uri.EscapeDataString(settingName)}"
                ).ConfigureAwait(false);

                var settingsData = JObject.Parse(settingsResponse);
                comparison.Add(new JObject
                {
                    ["environmentId"] = envId,
                    ["environmentName"] = envName,
                    ["settingName"] = settingName,
                    ["value"] = settingsData[settingName],
                    ["status"] = "retrieved"
                });
            }
            catch (Exception ex)
            {
                comparison.Add(new JObject
                {
                    ["environmentId"] = envId,
                    ["environmentName"] = envName,
                    ["settingName"] = settingName,
                    ["value"] = null,
                    ["status"] = $"error: {ex.Message}"
                });
            }
        }

        return new JObject
        {
            ["settingName"] = settingName,
            ["environmentCount"] = comparison.Count,
            ["comparison"] = comparison
        };
    }

    // ─── Category 2: Governance & Security ──────────────────────────────

    private async Task<JToken> HandleGetCopilotGovernance(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();

        var settingsUrl = string.IsNullOrEmpty(envId)
            ? $"/copilotgovernance/settings?api-version={SETTINGS_API_VERSION}"
            : $"/copilotgovernance/environments/{envId}/settings?api-version={SETTINGS_API_VERSION}";

        var featuresUrl = string.IsNullOrEmpty(envId)
            ? $"/copilotgovernance/features?api-version={SETTINGS_API_VERSION}"
            : $"/copilotgovernance/environments/{envId}/features?api-version={SETTINGS_API_VERSION}";

        string settingsResponse = null;
        string featuresResponse = null;

        try
        {
            settingsResponse = await CallAdminApi(HttpMethod.Get, settingsUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            settingsResponse = new JObject { ["error"] = ex.Message }.ToString();
        }

        try
        {
            featuresResponse = await CallAdminApi(HttpMethod.Get, featuresUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            featuresResponse = new JObject { ["error"] = ex.Message }.ToString();
        }

        return new JObject
        {
            ["scope"] = string.IsNullOrEmpty(envId) ? "tenant" : envId,
            ["settings"] = JToken.Parse(settingsResponse),
            ["features"] = JToken.Parse(featuresResponse)
        };
    }

    private async Task<JToken> HandleUpdateCopilotGovernance(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        var settings = arguments["settings"] as JObject;
        if (settings == null || settings.Count == 0)
            throw new ArgumentException("settings object with at least one key-value pair is required.");

        var url = string.IsNullOrEmpty(envId)
            ? $"/copilotgovernance/settings?api-version={SETTINGS_API_VERSION}"
            : $"/copilotgovernance/environments/{envId}/settings?api-version={SETTINGS_API_VERSION}";

        var response = await CallAdminApi(
            new HttpMethod("PATCH"),
            url,
            settings.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        return new JObject
        {
            ["status"] = "success",
            ["scope"] = string.IsNullOrEmpty(envId) ? "tenant" : envId,
            ["updatedSettings"] = settings,
            ["message"] = $"Successfully updated {settings.Count} Copilot governance setting(s)."
        };
    }

    private async Task<JToken> HandleGetTenantPoolDraw(JObject arguments)
    {
        var envId = RequireEnvironmentGuid(arguments["environmentId"]?.ToString());

        var document = await FetchAllocationDocument(envId).ConfigureAwait(false);

        var entitlements = new JObject();
        var allEnabled = true;
        foreach (var entitlementId in TENANT_POOL_ENTITLEMENTS)
        {
            var ruleEnabled = ReadTenantPoolEnabled(document, entitlementId);
            entitlements[entitlementId] = ruleEnabled;
            if (!ruleEnabled) allEnabled = false;
        }

        string message;
        if (document == null)
            message = "No allocation document exists for this environment, so it draws from the tenant pool by default.";
        else if (allEnabled)
            message = "This environment draws Copilot Studio message and session capacity from the tenant pool.";
        else
            message = "This environment does not draw Copilot Studio message and session capacity from the tenant pool.";

        return new JObject
        {
            ["environmentId"] = envId,
            ["enabled"] = allEnabled,
            ["source"] = document == null ? "default" : "allocationDocument",
            ["entitlements"] = entitlements,
            ["message"] = message
        };
    }

    private async Task<JToken> HandleSetTenantPoolDraw(JObject arguments)
    {
        var envId = RequireEnvironmentGuid(arguments["environmentId"]?.ToString());

        var enabledToken = arguments["enabled"];
        if (enabledToken == null || enabledToken.Type == JTokenType.Null)
            throw new ArgumentException("enabled is required and must be true or false.");
        var enabled = enabledToken.Value<bool>();

        var tenantId = ResolveTenantId();

        // Read-modify-write: the PUT replaces the whole document, so a blind write
        // would drop any other allocation or enforcement data on this environment.
        var document = await FetchAllocationDocument(envId).ConfigureAwait(false);
        var entitlements = document == null ? null : document["allocatedEntitlements"] as JArray;
        if (entitlements == null) entitlements = new JArray();

        foreach (var entitlementId in TENANT_POOL_ENTITLEMENTS)
        {
            var entitlement = FindEntitlement(entitlements, entitlementId);
            if (entitlement == null)
            {
                entitlement = new JObject
                {
                    ["allocation"] = new JObject
                    {
                        ["quantity"] = 0.0,
                        ["autoAllocated"] = 0.0,
                        ["unit"] = "NotSpecified"
                    },
                    ["entitlementId"] = entitlementId,
                    ["enforcementRules"] = new JArray()
                };
                entitlements.Add(entitlement);
            }

            var rules = entitlement["enforcementRules"] as JArray;
            if (rules == null)
            {
                rules = new JArray();
                entitlement["enforcementRules"] = rules;
            }

            var rule = FindTenantPoolRule(entitlement);
            if (rule == null)
                rules.Add(new JObject { ["ruleType"] = "TenantPool", ["enabled"] = enabled });
            else
                rule["enabled"] = enabled;
        }

        var payload = new JObject
        {
            ["scope"] = new JObject
            {
                ["tenantId"] = tenantId,
                ["environmentId"] = envId
            },
            ["allocatedEntitlements"] = entitlements
        };

        var result = await CallLicensingApi(
            HttpMethod.Put,
            $"/licensing/allocations?api-version={LICENSING_API_VERSION}",
            payload.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"Licensing allocations API returned {(int)result.Status} {result.Status}: {TruncateForError(result.Body)}");

        var affected = new JArray();
        foreach (var entitlementId in TENANT_POOL_ENTITLEMENTS) affected.Add(entitlementId);

        return new JObject
        {
            ["status"] = "success",
            ["environmentId"] = envId,
            ["enabled"] = enabled,
            ["entitlements"] = affected,
            ["message"] = enabled
                ? "This environment now draws Copilot Studio message and session capacity from the tenant pool."
                : "This environment no longer draws Copilot Studio message and session capacity from the tenant pool."
        };
    }

    private async Task<JToken> HandleGetSecurityRecommendations(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();

        var url = string.IsNullOrEmpty(envId)
            ? $"/security/recommendations?api-version={SETTINGS_API_VERSION}"
            : $"/security/environments/{envId}/recommendations?api-version={SETTINGS_API_VERSION}";

        try
        {
            var response = await CallAdminApi(HttpMethod.Get, url).ConfigureAwait(false);
            return JToken.Parse(response);
        }
        catch (Exception)
        {
            // Fall back to analytics advisor
            var advisorUrl = string.IsNullOrEmpty(envId)
                ? $"/analytics/advisorRecommendations?api-version={SETTINGS_API_VERSION}"
                : $"/analytics/environments/{envId}/advisorRecommendations?api-version={SETTINGS_API_VERSION}";

            var advisorResponse = await CallAdminApi(HttpMethod.Get, advisorUrl).ConfigureAwait(false);
            return JToken.Parse(advisorResponse);
        }
    }

    private async Task<JToken> HandleGetCrossTenantConnections(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();

        var url = string.IsNullOrEmpty(envId)
            ? $"/governance/crossTenantConnectionReports?api-version={SETTINGS_API_VERSION}"
            : $"/governance/environments/{envId}/crossTenantConnectionReports?api-version={SETTINGS_API_VERSION}";

        var response = await CallAdminApi(HttpMethod.Get, url).ConfigureAwait(false);
        return JToken.Parse(response);
    }

    // ─── Category 3: Resource Inventory ─────────────────────────────────

    private async Task<JToken> HandleListConnectors(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/connectivity/environments/{envId}/connectors?api-version={SETTINGS_API_VERSION}"
        ).ConfigureAwait(false);

        return JToken.Parse(response);
    }

    private async Task<JToken> HandleListApps(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/powerapps/environments/{envId}/apps?api-version={SETTINGS_API_VERSION}"
        ).ConfigureAwait(false);

        return JToken.Parse(response);
    }

    private async Task<JToken> HandleListAgents(JObject arguments)
    {
        var environmentId = NormalizeEnvironmentFilter(arguments["environmentId"]?.ToString());

        var agents = new JArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skip = 0;
        var truncated = false;
        var pages = 0;

        while (true)
        {
            var body = BuildAgentQuery(environmentId, skip).ToString(Newtonsoft.Json.Formatting.None);

            var response = await CallAdminApi(
                HttpMethod.Post,
                $"/resourcequery/resources/query?api-version={RESOURCEQUERY_API_VERSION}",
                body
            ).ConfigureAwait(false);

            var page = JObject.Parse(response);
            var rows = page["data"] as JArray ?? new JArray();

            foreach (var row in rows)
            {
                var resourceName = row["name"]?.ToString();
                if (!string.IsNullOrEmpty(resourceName) && !seen.Add(resourceName)) continue;

                agents.Add(ProjectAgent(row));
            }

            pages++;

            if (rows.Count < AGENT_PAGE_SIZE) break;

            if (pages >= AGENT_MAX_PAGES)
            {
                truncated = true;
                break;
            }

            skip += AGENT_PAGE_SIZE;
        }

        return new JObject
        {
            ["agentCount"] = agents.Count,
            ["truncated"] = truncated,
            ["environmentId"] = string.IsNullOrEmpty(environmentId) ? (JToken)JValue.CreateNull() : environmentId,
            ["agents"] = agents
        };
    }

    // Verified against the live API: SkipToken does not advance between calls, so Skip is the only working pager.
    // Every clause must serialize "$type" first — it is a polymorphic discriminator and the service rejects the document otherwise.
    private JObject BuildAgentQuery(string environmentId, int skip)
    {
        var clauses = new JArray
        {
            new JObject
            {
                ["$type"] = "where",
                ["FieldName"] = "type",
                ["Operator"] = "in~",
                ["Values"] = new JArray { AGENT_RESOURCE_TYPE }
            },
            new JObject
            {
                ["$type"] = "extend",
                ["FieldName"] = "joinKey",
                ["Expression"] = "tolower(tostring(properties.environmentId))"
            }
        };

        if (!string.IsNullOrEmpty(environmentId))
        {
            clauses.Add(new JObject
            {
                ["$type"] = "where",
                ["FieldName"] = "joinKey",
                ["Operator"] = "==",
                ["Values"] = new JArray { $"'{environmentId}'" }
            });
        }

        clauses.Add(new JObject
        {
            ["$type"] = "project",
            ["FieldList"] = new JArray { "joinKey", "name", "type", "properties" }
        });

        clauses.Add(new JObject
        {
            ["$type"] = "join",
            ["JoinKind"] = "leftouter",
            ["RightTable"] = new JObject
            {
                ["TableName"] = "PowerPlatformResources",
                ["Clauses"] = new JArray
                {
                    new JObject
                    {
                        ["$type"] = "where",
                        ["FieldName"] = "type",
                        ["Operator"] = "==",
                        ["Values"] = new JArray { "'microsoft.powerplatform/environments'" }
                    },
                    new JObject
                    {
                        ["$type"] = "project",
                        ["FieldList"] = new JArray
                        {
                            "joinKey = tolower(name)",
                            "environmentName = properties.displayName",
                            "environmentRegion = location",
                            "environmentType = properties.environmentType",
                            "isManagedEnvironment = properties.isManaged",
                            "environmentGroupId = tolower(properties.environmentGroupId)"
                        }
                    }
                }
            },
            ["LeftColumnName"] = "joinKey",
            ["RightColumnName"] = "joinKey"
        });

        clauses.Add(new JObject
        {
            ["$type"] = "join",
            ["JoinKind"] = "leftouter",
            ["RightTable"] = new JObject
            {
                ["TableName"] = "PowerPlatformResources",
                ["Clauses"] = new JArray
                {
                    new JObject
                    {
                        ["$type"] = "where",
                        ["FieldName"] = "type",
                        ["Operator"] = "==",
                        ["Values"] = new JArray { "'microsoft.powerplatform/environmentgroups'" }
                    },
                    new JObject
                    {
                        ["$type"] = "project",
                        ["FieldList"] = new JArray
                        {
                            "environmentGroupId = tolower(name)",
                            "environmentGroupName = properties.displayName"
                        }
                    }
                }
            },
            ["LeftColumnName"] = "environmentGroupId",
            ["RightColumnName"] = "environmentGroupId"
        });

        clauses.Add(new JObject
        {
            ["$type"] = "orderby",
            ["FieldNamesAscDesc"] = new JObject
            {
                ["tostring(properties.createdAt)"] = "desc",
                // createdAt ties are common; without a unique tiebreaker the skip window shifts and drops rows.
                ["name"] = "asc"
            }
        });

        return new JObject
        {
            ["Options"] = new JObject
            {
                ["Top"] = AGENT_PAGE_SIZE,
                ["Skip"] = skip,
                ["SkipToken"] = string.Empty
            },
            ["TableName"] = "PowerPlatformResources",
            ["Clauses"] = clauses
        };
    }

    private static JObject ProjectAgent(JToken row)
    {
        var props = row["properties"];
        var viewers = props?["sharedWithViewers"];
        var editors = props?["sharedWithEditors"];

        var agent = new JObject
        {
            ["displayName"] = Val(props?["displayName"]),
            ["name"] = Val(row["name"]),
            ["schemaName"] = Val(props?["schemaName"]),
            ["entraAgentId"] = Val(props?["entraAgentId"]),
            ["entraAgentBlueprintId"] = Val(props?["entraAgentBlueprintId"]),
            ["entraAppId"] = Val(props?["entraAppId"]),
            ["environmentId"] = Val(props?["environmentId"]),
            ["environmentName"] = Val(row["environmentName"]),
            ["environmentRegion"] = Val(row["environmentRegion"]),
            ["environmentType"] = Val(row["environmentType"]),
            ["environmentIsManaged"] = Val(row["isManagedEnvironment"]),
            ["environmentGroupId"] = Val(row["environmentGroupId"]),
            ["environmentGroupName"] = Val(row["environmentGroupName"]),
            ["createdAt"] = Val(props?["createdAt"]),
            ["createdBy"] = Val(props?["createdBy"]),
            ["createdIn"] = Val(props?["createdIn"]),
            ["lastPublishedAt"] = Val(props?["lastPublishedAt"]),
            ["ownerId"] = Val(props?["ownerId"]),
            ["authentication"] = Val(props?["authentication"]),
            ["orchestration"] = Val(props?["orchestration"]),
            ["model"] = Val(props?["model"]),
            ["isManaged"] = Val(props?["isManaged"]),
            ["isQuarantined"] = Val(props?["isQuarantined"]),
            ["isCLIAgent"] = TriState(props, "isCLIAgent"),
            ["sharedWithViewersUserCount"] = Val(viewers?["userCount"]),
            ["sharedWithViewersGroupCount"] = Val(viewers?["groupCount"]),
            ["sharedWithViewersEntireTenant"] = Val(viewers?["entireTenant"]),
            ["sharedWithEditorsUserCount"] = Val(editors?["userCount"]),
            ["sharedWithEditorsGroupCount"] = Val(editors?["groupCount"]),
            ["channels"] = props?["channels"] as JArray ?? new JArray()
        };

        ApplyRisk(agent);
        return agent;
    }

    // Absent is reported as "unknown" so a missing field is never mistaken for false.
    private static string TriState(JToken parent, string propertyName)
    {
        var token = parent?[propertyName];
        if (token == null || token.Type == JTokenType.Null) return "unknown";
        if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";

        bool parsed;
        return bool.TryParse(token.ToString(), out parsed) ? (parsed ? "true" : "false") : "unknown";
    }

    private static void ApplyRisk(JObject agent)
    {
        var signals = new JArray();
        var score = 0;

        var cliAuthored = string.Equals(agent["isCLIAgent"]?.ToString(), "true", StringComparison.Ordinal);
        var tenantWide = IsTrue(agent["sharedWithViewersEntireTenant"]);
        var quarantined = IsTrue(agent["isQuarantined"]);
        var managedEnvironment = IsTrue(agent["environmentIsManaged"]);
        var hasEntraIdentity = !string.IsNullOrEmpty(agent["entraAgentId"]?.ToString());
        var published = !string.IsNullOrEmpty(agent["lastPublishedAt"]?.ToString());
        var editorUsers = ToInt(agent["sharedWithEditorsUserCount"]);

        var environmentType = agent["environmentType"]?.ToString();
        var defaultEnvironment = string.Equals(environmentType, "Default", StringComparison.OrdinalIgnoreCase);
        var nonProduction = string.Equals(environmentType, "Developer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentType, "Trial", StringComparison.OrdinalIgnoreCase);

        if (quarantined) { score += 3; signals.Add("quarantined"); }
        if (tenantWide) { score += 2; signals.Add("sharedWithEntireTenant"); }
        if (editorUsers >= EDITOR_BREADTH_THRESHOLD) { score += 2; signals.Add("broadEditorAccess"); }
        if (cliAuthored && tenantWide) { score += 2; signals.Add("cliAuthoredAndTenantWide"); }

        if (!hasEntraIdentity)
        {
            if (CreatedAfterEntraMandate(agent)) { score += 2; signals.Add("missingEntraAgentIdPostMandate"); }
            else { score += 1; signals.Add("noEntraAgentId"); }
        }

        if (cliAuthored) { score += 1; signals.Add("createdOutsideMakerPortal"); }

        // The default environment is never a Managed Environment, so scoring it as "unmanaged" would fire on nearly every agent.
        if (defaultEnvironment) { score += 1; signals.Add("defaultEnvironment"); }
        else if (!managedEnvironment) { score += 1; signals.Add("unmanagedEnvironment"); }

        if (nonProduction && tenantWide) { score += 1; signals.Add("tenantWideInNonProductionEnvironment"); }

        if (!published) { score -= 1; signals.Add("neverPublished"); }

        if (score < 0) score = 0;

        agent["riskScore"] = score;
        agent["riskSignals"] = signals;
        agent["riskLevel"] = score >= 5 ? "High" : score >= 3 ? "Medium" : score >= 1 ? "Low" : "None";
    }

    private static bool CreatedAfterEntraMandate(JObject agent)
    {
        DateTimeOffset createdAt;
        return DateTimeOffset.TryParse(
            agent["createdAt"]?.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out createdAt) && createdAt >= ENTRA_AGENT_ID_MANDATE;
    }

    private static int ToInt(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return 0;
        if (token.Type == JTokenType.Integer) return token.Value<int>();

        int parsed;
        return int.TryParse(token.ToString(), out parsed) ? parsed : 0;
    }

    private static bool IsTrue(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return false;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>();

        bool parsed;
        return bool.TryParse(token.ToString(), out parsed) && parsed;
    }

    private static JToken Val(JToken token)
    {
        return token ?? JValue.CreateNull();
    }

    // The value is embedded in the resource-query clause document, so restrict it to id-safe characters.
    private static string NormalizeEnvironmentFilter(string environmentId)
    {
        if (string.IsNullOrWhiteSpace(environmentId)) return null;

        var trimmed = environmentId.Trim();
        foreach (var character in trimmed)
        {
            if (!char.IsLetterOrDigit(character) && character != '-')
                throw new ArgumentException("environmentId may contain only letters, digits, and hyphens.");
        }

        return trimmed.ToLowerInvariant();
    }

    // ─── Category 4: Application Lifecycle ──────────────────────────────

    private async Task<JToken> HandleInstallPackage(JObject arguments)
    {
        var envId = arguments["environmentId"]?.ToString();
        if (string.IsNullOrEmpty(envId))
            throw new ArgumentException("environmentId is required.");

        var packageName = arguments["packageUniqueName"]?.ToString();
        if (string.IsNullOrEmpty(packageName))
            throw new ArgumentException("packageUniqueName is required.");

        var response = await CallAdminApi(
            HttpMethod.Post,
            $"/appmanagement/environments/{envId}/applicationPackages/{Uri.EscapeDataString(packageName)}/install?api-version={SETTINGS_API_VERSION}",
            "{}"
        ).ConfigureAwait(false);

        var result = JToken.Parse(response);

        return new JObject
        {
            ["status"] = "installationStarted",
            ["environmentId"] = envId,
            ["packageName"] = packageName,
            ["operationDetails"] = result,
            ["message"] = "Package installation has been initiated. The operation may take several minutes to complete."
        };
    }

    // ─── API Call Helper ────────────────────────────────────────────────

    private async Task<string> CallAdminApi(HttpMethod method, string path, string body = null)
    {
        var url = $"{ADMIN_API_BASE}{path}";
        var request = new HttpRequestMessage(method, url);

        // Forward the auth token from the connector
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

    // ─── Licensing Allocations (unsupported endpoint) ───────────────────

    private class ApiResult
    {
        public HttpStatusCode Status;
        public string Body;
        public bool IsSuccess { get { return (int)Status >= 200 && (int)Status < 300; } }
    }

    private static string RequireEnvironmentGuid(string environmentId)
    {
        Guid parsed;
        if (string.IsNullOrEmpty(environmentId) || !Guid.TryParse(environmentId, out parsed))
            throw new ArgumentException("environmentId is required and must be a GUID.");
        return parsed.ToString();
    }

    private string ResolveTenantId()
    {
        if (!string.IsNullOrEmpty(_tenantId)) return _tenantId;

        var auth = this.Context.Request.Headers.Authorization;
        if (auth == null || string.IsNullOrEmpty(auth.Parameter))
            throw new InvalidOperationException("Authorization header is missing; cannot resolve the tenant.");

        var segments = auth.Parameter.Split('.');
        if (segments.Length < 2)
            throw new InvalidOperationException("Access token is not a JWT; cannot resolve the tenant.");

        var payload = segments[1].Replace('-', '+').Replace('_', '/');
        if (payload.Length % 4 == 2) payload += "==";
        else if (payload.Length % 4 == 3) payload += "=";

        var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        var tid = claims["tid"]?.ToString();
        if (string.IsNullOrEmpty(tid))
            throw new InvalidOperationException("Access token has no 'tid' claim; cannot resolve the tenant.");

        _tenantId = tid;
        return _tenantId;
    }

    // Licensing is served from a tenant-routed host: first 30 hex, dot, last 2 hex.
    private string ResolveTenantHost()
    {
        var dashless = ResolveTenantId().Replace("-", "").ToLowerInvariant();
        if (dashless.Length != 32)
            throw new InvalidOperationException("Tenant id is not 32 hex characters; cannot build the licensing host.");

        return $"{dashless.Substring(0, 30)}.{dashless.Substring(30)}{TENANT_HOST_SUFFIX}";
    }

    private async Task<ApiResult> CallLicensingApi(HttpMethod method, string path, string body = null)
    {
        var request = new HttpRequestMessage(method, $"https://{ResolveTenantHost()}{path}");

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
            this.Context.Logger.LogError($"Licensing allocations API error {response.StatusCode}: {responseBody}");
        }

        return new ApiResult { Status = response.StatusCode, Body = responseBody };
    }

    private async Task<JObject> FetchAllocationDocument(string environmentId)
    {
        var filter = $"environmentId eq '{environmentId}' and EntitlementId in ({string.Join(",", TENANT_POOL_ENTITLEMENTS)})";
        var result = await CallLicensingApi(
            HttpMethod.Get,
            $"/licensing/allocationsV2?$filter={Uri.EscapeDataString(filter)}&api-version={LICENSING_API_VERSION}"
        ).ConfigureAwait(false);

        // A missing document is the implicit "draws from the pool" default, not a failure.
        // RouteNotFound shares the 404 status, so discriminate on the body code.
        if (result.Status == HttpStatusCode.NotFound &&
            result.Body != null &&
            result.Body.IndexOf("AllocationDocumentDoesNotExist", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return null;
        }

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"Licensing allocations API returned {(int)result.Status} {result.Status}: {TruncateForError(result.Body)}");

        return ExtractAllocationDocument(result.Body);
    }

    private JObject ExtractAllocationDocument(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        var parsed = JToken.Parse(body) as JObject;
        if (parsed == null) return null;

        if (parsed["allocatedEntitlements"] as JArray != null) return parsed;

        var value = parsed["value"];

        var valueObject = value as JObject;
        if (valueObject != null && valueObject["allocatedEntitlements"] as JArray != null) return valueObject;

        var valueArray = value as JArray;
        if (valueArray != null)
        {
            foreach (var item in valueArray)
            {
                var candidate = item as JObject;
                if (candidate != null && candidate["allocatedEntitlements"] as JArray != null) return candidate;
            }
        }

        return null;
    }

    // An absent entitlement or TenantPool rule means the environment draws from the pool.
    private bool ReadTenantPoolEnabled(JObject document, string entitlementId)
    {
        var entitlements = document == null ? null : document["allocatedEntitlements"] as JArray;
        var entitlement = FindEntitlement(entitlements, entitlementId);
        if (entitlement == null) return true;

        var rule = FindTenantPoolRule(entitlement);
        if (rule == null) return true;

        var enabled = rule["enabled"];
        if (enabled == null || enabled.Type == JTokenType.Null) return true;

        return enabled.Value<bool>();
    }

    private JObject FindEntitlement(JArray entitlements, string entitlementId)
    {
        if (entitlements == null) return null;

        foreach (var item in entitlements)
        {
            var entitlement = item as JObject;
            if (entitlement != null &&
                string.Equals(entitlement["entitlementId"]?.ToString(), entitlementId, StringComparison.OrdinalIgnoreCase))
            {
                return entitlement;
            }
        }

        return null;
    }

    private JObject FindTenantPoolRule(JObject entitlement)
    {
        var rules = entitlement["enforcementRules"] as JArray;
        if (rules == null) return null;

        foreach (var item in rules)
        {
            var rule = item as JObject;
            if (rule != null &&
                string.Equals(rule["ruleType"]?.ToString(), "TenantPool", StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    // ─── Typed Operations (Power Automate) ────────────────────────────

    private async Task<HttpResponseMessage> HandleTypedListEnvironments()
    {
        var result = await HandleListEnvironments(new JObject()).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetEnvironment()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var result = await HandleGetEnvironment(new JObject { ["environmentId"] = envId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetSettings()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var args = new JObject { ["environmentId"] = envId };
        var select = GetQueryParam("select");
        if (!string.IsNullOrEmpty(select)) args["select"] = select;

        var result = await HandleGetSettings(args).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedUpdateSettings()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = JObject.Parse(body);
        var settings = bodyObj["settings"] as JObject;
        if (settings == null || settings.Count == 0)
            return CreateTypedErrorResponse("Request body must contain a 'settings' object with key-value pairs.", 400);

        var args = new JObject { ["environmentId"] = envId, ["settings"] = settings };
        var result = await HandleUpdateSetting(args).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedGetTenantPoolDraw()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var result = await HandleGetTenantPoolDraw(new JObject { ["environmentId"] = envId }).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedSetTenantPoolDraw()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrEmpty(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var bodyObj = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);

        var enabled = bodyObj["enabled"];
        if (enabled == null || enabled.Type == JTokenType.Null)
            return CreateTypedErrorResponse("Request body must contain a boolean 'enabled' property.", 400);

        var args = new JObject { ["environmentId"] = envId, ["enabled"] = enabled };
        var result = await HandleSetTenantPoolDraw(args).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    private async Task<HttpResponseMessage> HandleTypedListAgents()
    {
        var args = new JObject();

        var envId = GetQueryParam("environmentId");
        if (!string.IsNullOrWhiteSpace(envId)) args["environmentId"] = envId;

        try
        {
            var result = await HandleListAgents(args).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

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
            dropdown.Add(new JObject
            {
                ["id"] = env["id"],
                ["name"] = env["properties"]?["displayName"]?.ToString() ?? env["name"]?.ToString() ?? env["id"]?.ToString()
            });
        }

        return CreateTypedResponse(dropdown);
    }

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
            // Parse instrumentation key from connection string
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




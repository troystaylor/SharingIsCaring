using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string ServerName = "ppundoc";
    private const string ServerVersion = "1.0.0";
    private const string ProtocolVersion = "2025-11-25";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (this.Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpRequestAsync().ConfigureAwait(false);

            default:
                return await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
                    .ConfigureAwait(false);
        }
    }

    // ========================================
    // MCP PROTOCOL HANDLER
    // ========================================

    private async Task<HttpResponseMessage> HandleMcpRequestAsync()
    {
        var requestBody = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error: empty request body");
        }

        JObject request;
        try { request = JObject.Parse(requestBody); }
        catch { return CreateJsonRpcErrorResponse(null, -32700, "Parse error: invalid JSON"); }

        var method = request.Value<string>("method");
        var requestId = request["id"];

        if (requestId == null || requestId.Type == JTokenType.Null)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        switch (method)
        {
            case "initialize":
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false },
                        ["resources"] = new JObject { ["listChanged"] = false },
                        ["prompts"] = new JObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JObject { ["name"] = ServerName, ["version"] = ServerVersion }
                });

            case "ping":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return HandleMcpToolsList(requestId);

            case "tools/call":
                return await HandleMcpToolsCallAsync(request, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

            case "prompts/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["prompts"] = new JArray() });

            default:
                return CreateJsonRpcErrorResponse(requestId, -32601, $"Method not found: {method}");
        }
    }

    private HttpResponseMessage HandleMcpToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            McpTool("list_environments", "List all Power Platform environments with capacity, state, and metadata. Use BAP admin API.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["select"] = new JObject { ["type"] = "string", ["description"] = "Fields to return (OData $select)" },
                    ["expand"] = new JObject { ["type"] = "string", ["description"] = "Properties to expand" }
                }
            }),
            McpTool("get_tenant_settings", "Get all Power Platform tenant-level admin settings.", new JObject
            {
                ["type"] = "object", ["properties"] = new JObject()
            }),
            McpTool("list_dlp_policies", "List all Data Loss Prevention policies in the tenant.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max results (default 100)" }
                }
            }),
            McpTool("list_flows_admin", "List all flows in an environment as admin, including all makers.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["top"] = new JObject { ["type"] = "integer", ["description"] = "Max results" }
                },
                ["required"] = new JArray { "environment_id" }
            }),
            McpTool("get_licenses", "Get full list of Power Platform license assignments for the tenant.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["tenant_id"] = new JObject { ["type"] = "string", ["description"] = "Tenant ID" }
                },
                ["required"] = new JArray { "tenant_id" }
            }),
            McpTool("get_tenant_capacity", "Get tenant storage and capacity information.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["tenant_id"] = new JObject { ["type"] = "string", ["description"] = "Tenant ID" }
                },
                ["required"] = new JArray { "tenant_id" }
            }),
            McpTool("list_canvas_apps_analytics", "Get inventory of Power Apps canvas apps with usage metrics from Admin Analytics.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("get_tenant_advisor", "Get Power Platform Advisor recommendations for governance, security, and performance.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("who_am_i", "Get current user identity and organization from Dataverse.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("find_user", "Find a Dataverse system user by UPN (email).", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["upn"] = new JObject { ["type"] = "string", ["description"] = "User principal name (email)" }
                },
                ["required"] = new JArray { "upn" }
            }),
            McpTool("list_agents", "List all Copilot Studio agents in the Dataverse environment.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("list_solutions", "List all solutions in the Dataverse environment.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("list_custom_connectors", "List all custom connectors in the Dataverse environment.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("get_flow_definition", "Get the full definition of a flow including actions, connections, and triggers.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["resource_id"] = new JObject { ["type"] = "string", ["description"] = "Flow resource ID" }
                },
                ["required"] = new JArray { "environment_id", "resource_id" }
            }),
            McpTool("get_flow_run_history", "Get run history for a flow to troubleshoot failures.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["resource_id"] = new JObject { ["type"] = "string", ["description"] = "Flow resource ID" }
                },
                ["required"] = new JArray { "environment_id", "resource_id" }
            }),
            McpTool("share_flow", "Share a flow with a user by their Entra Object ID.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["flow_id"] = new JObject { ["type"] = "string", ["description"] = "Flow ID" },
                    ["user_id"] = new JObject { ["type"] = "string", ["description"] = "Entra Object ID of the user" },
                    ["user_type"] = new JObject { ["type"] = "string", ["description"] = "User or ServicePrincipal" }
                },
                ["required"] = new JArray { "environment_id", "flow_id", "user_id" }
            }),
            McpTool("list_connections", "List all connections in a Power Apps environment.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" }
                },
                ["required"] = new JArray { "environment_id" }
            }),
            McpTool("list_desktop_flows", "Get inventory of desktop flows (RPA) from Admin Analytics.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("list_cloud_flows_analytics", "Get inventory of cloud flows with usage analytics.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("security_insights", "Get security analytics insights summary for the tenant.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["tenant_id"] = new JObject { ["type"] = "string", ["description"] = "Tenant ID" }
                },
                ["required"] = new JArray { "tenant_id" }
            }),
            McpTool("licenses_by_environment", "Get license breakdown per environment. Answers: which environments have which licenses allocated.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["tenant_id"] = new JObject { ["type"] = "string", ["description"] = "Tenant ID" }
                },
                ["required"] = new JArray { "tenant_id" }
            }),
            McpTool("list_gateways", "List all on-premises data gateways registered in the tenant.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("update_tenant_settings", "Update Power Platform tenant-level admin settings (e.g., weekly digest, environment routing).", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["settings"] = new JObject { ["type"] = "string", ["description"] = "JSON string of settings to update" }
                },
                ["required"] = new JArray { "settings" }
            }),
            McpTool("turn_on_flow", "Enable a disabled flow.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["flow_id"] = new JObject { ["type"] = "string", ["description"] = "Flow ID" }
                },
                ["required"] = new JArray { "environment_id", "flow_id" }
            }),
            McpTool("turn_off_flow", "Disable an active flow.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["flow_id"] = new JObject { ["type"] = "string", ["description"] = "Flow ID" }
                },
                ["required"] = new JArray { "environment_id", "flow_id" }
            }),
            McpTool("cancel_all_runs", "Abort all running instances of a flow.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["flow_id"] = new JObject { ["type"] = "string", ["description"] = "Flow ID" }
                },
                ["required"] = new JArray { "environment_id", "flow_id" }
            }),
            McpTool("check_flow_errors", "Check a flow for errors and issues.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["flow_id"] = new JObject { ["type"] = "string", ["description"] = "Flow ID" }
                },
                ["required"] = new JArray { "environment_id", "flow_id" }
            }),
            McpTool("get_flow_owners", "List all owners of a flow.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["environment_id"] = new JObject { ["type"] = "string", ["description"] = "Environment ID" },
                    ["flow_id"] = new JObject { ["type"] = "string", ["description"] = "Flow ID" }
                },
                ["required"] = new JArray { "environment_id", "flow_id" }
            }),
            McpTool("list_unblockable_connectors", "Get connectors that cannot be blocked by DLP policies.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            }),
            McpTool("check_role_permissions", "Get all privileges assigned to a Dataverse security role.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["role_id"] = new JObject { ["type"] = "string", ["description"] = "Security role GUID" }
                },
                ["required"] = new JArray { "role_id" }
            }),
            McpTool("add_on_licenses", "Get add-on license allocations (capacity packs, AI Builder credits) across environments.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["tenant_id"] = new JObject { ["type"] = "string", ["description"] = "Tenant ID" }
                },
                ["required"] = new JArray { "tenant_id" }
            }),
            McpTool("request_license_report", "Generate a license/capacity consumption report.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["tenant_id"] = new JObject { ["type"] = "string", ["description"] = "Tenant ID" },
                    ["report_params"] = new JObject { ["type"] = "string", ["description"] = "JSON string of report parameters" }
                },
                ["required"] = new JArray { "tenant_id" }
            }),
            McpTool("app_diagnostics", "Get diagnostic and usage analytics for a specific app.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["resource_id"] = new JObject { ["type"] = "string", ["description"] = "App resource ID" },
                    ["start_time"] = new JObject { ["type"] = "string", ["description"] = "Start time ISO 8601 (e.g., 2026-01-01T00:00:00Z)" },
                    ["end_time"] = new JObject { ["type"] = "string", ["description"] = "End time ISO 8601" }
                },
                ["required"] = new JArray { "resource_id" }
            }),
            McpTool("list_copilot_agents_analytics", "Get inventory of Copilot Studio agents with usage analytics.", new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject()
            })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    private JObject McpTool(string name, string description, JObject inputSchema)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private async Task<HttpResponseMessage> HandleMcpToolsCallAsync(JObject request, JToken requestId)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj.Value<string>("name");
        var arguments = paramsObj["arguments"] as JObject ?? new JObject();

        try
        {
            string apiPath;
            string host;
            HttpMethod httpMethod = HttpMethod.Get;
            JObject body = null;

            switch (toolName.ToLowerInvariant())
            {
                case "list_environments":
                    host = "api.bap.microsoft.com";
                    apiPath = "/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2021-04-01";
                    var select = arguments.Value<string>("select");
                    var expand = arguments.Value<string>("expand");
                    if (!string.IsNullOrWhiteSpace(select)) apiPath += $"&$select={select}";
                    if (!string.IsNullOrWhiteSpace(expand)) apiPath += $"&$expand={expand}";
                    break;

                case "get_tenant_settings":
                    host = "api.bap.microsoft.com";
                    apiPath = "/providers/Microsoft.BusinessAppPlatform/listTenantSettings?api-version=2021-04-01";
                    break;

                case "list_dlp_policies":
                    host = "api.bap.microsoft.com";
                    var top = arguments.Value<int?>("top") ?? 100;
                    apiPath = $"/providers/PowerPlatform.Governance/v1/policies?$top={top}";
                    break;

                case "list_flows_admin":
                    host = "api.flow.microsoft.com";
                    var envId = arguments.Value<string>("environment_id");
                    var flowTop = arguments.Value<int?>("top") ?? 25;
                    apiPath = $"/Providers/Microsoft.ProcessSimple/scopes/admin/environments/{envId}/flows?api-version=2016-11-01-beta&$top={flowTop}";
                    break;

                case "get_licenses":
                    host = "licensing.powerplatform.microsoft.com";
                    var tenantId = arguments.Value<string>("tenant_id");
                    apiPath = $"/v0.1-alpha/tenants/{tenantId}/CurrencyReports";
                    break;

                case "get_tenant_capacity":
                    host = "licensing.powerplatform.microsoft.com";
                    var capTenantId = arguments.Value<string>("tenant_id");
                    apiPath = $"/v0.1-alpha/tenants/{capTenantId}/TenantCapacity";
                    break;

                case "list_canvas_apps_analytics":
                    host = "na.adminanalytics.powerplatform.microsoft.com";
                    apiPath = "/api/v1/metrics/resourceType/powerapps/resourceSubType/canvasapp/latest";
                    httpMethod = HttpMethod.Post;
                    body = new JObject();
                    break;

                case "get_tenant_advisor":
                    var baseUri = this.Context.Request.RequestUri;
                    host = baseUri.Host;
                    apiPath = "/analytics/advisorRecommendations?api-version=1&source=AdvisorPage&includeMetadata=true&includeHints=true&includeTrends=true";
                    break;

                case "who_am_i":
                    host = this.Context.Request.RequestUri.Host;
                    apiPath = "/api/data/v9.2/WhoAmI";
                    break;

                case "find_user":
                    host = this.Context.Request.RequestUri.Host;
                    var upn = arguments.Value<string>("upn");
                    apiPath = $"/api/data/v9.0/systemusers?$filter=domainname eq '{upn}'&$select=systemuserid,fullname,internalemailaddress";
                    break;

                case "list_agents":
                    host = this.Context.Request.RequestUri.Host;
                    apiPath = "/api/data/v9.2/bots";
                    break;

                case "list_solutions":
                    host = this.Context.Request.RequestUri.Host;
                    apiPath = "/api/data/v9.2/solutions";
                    break;

                case "list_custom_connectors":
                    host = this.Context.Request.RequestUri.Host;
                    apiPath = "/api/data/v9.2/connectors";
                    break;

                case "get_flow_definition":
                    host = "api.flow.microsoft.com";
                    var defEnvId = arguments.Value<string>("environment_id");
                    var defResId = arguments.Value<string>("resource_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{defEnvId}/flows/{defResId}?api-version=2016-11-01&$expand=properties.connectionreferences.apidefinition,operationDefinition,plan";
                    break;

                case "get_flow_run_history":
                    host = "api.flow.microsoft.com";
                    var histEnvId = arguments.Value<string>("environment_id");
                    var histResId = arguments.Value<string>("resource_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{histEnvId}/flows/{histResId}/runs?api-version=2016-11-01";
                    break;

                case "share_flow":
                    host = "api.flow.microsoft.com";
                    var shareEnvId = arguments.Value<string>("environment_id");
                    var shareFlowId = arguments.Value<string>("flow_id");
                    var shareUserId = arguments.Value<string>("user_id");
                    var shareUserType = arguments.Value<string>("user_type") ?? "User";
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{shareEnvId}/flows/{shareFlowId}/modifyowners?api-version=2016-11-01&cascadeoperation=true";
                    httpMethod = HttpMethod.Post;
                    body = new JObject
                    {
                        ["put"] = new JArray
                        {
                            new JObject
                            {
                                ["properties"] = new JObject
                                {
                                    ["roleName"] = "CanEdit",
                                    ["principal"] = new JObject
                                    {
                                        ["id"] = shareUserId,
                                        ["type"] = shareUserType
                                    }
                                }
                            }
                        }
                    };
                    break;

                case "list_connections":
                    host = "api.powerapps.com";
                    var connEnvId = arguments.Value<string>("environment_id");
                    apiPath = $"/providers/Microsoft.PowerApps/connections?api-version=2016-11-01&$filter=ApiId not in ('shared_logicflows','shared_powerflows','shared_pqogenericconnector') and environment eq '{connEnvId}'";
                    break;

                case "list_desktop_flows":
                    host = "na.adminanalytics.powerplatform.microsoft.com";
                    apiPath = "/api/v1/metrics/resourceType/powerautomate/resourceSubType/desktopflow/latest";
                    httpMethod = HttpMethod.Post;
                    body = new JObject();
                    break;

                case "list_cloud_flows_analytics":
                    host = "na.adminanalytics.powerplatform.microsoft.com";
                    apiPath = "/api/v1/metrics/resourceType/powerautomate/resourceSubType/cloudflow/latest";
                    httpMethod = HttpMethod.Post;
                    body = new JObject();
                    break;

                case "security_insights":
                    host = "licensing.powerplatform.microsoft.com";
                    var secTenantId = arguments.Value<string>("tenant_id");
                    apiPath = $"/v1.0/tenants/{secTenantId}/AnalyticsInsights/SecuritySummary";
                    break;

                case "licenses_by_environment":
                    host = "licensing.powerplatform.microsoft.com";
                    var licEnvTenantId = arguments.Value<string>("tenant_id");
                    apiPath = $"/v0.1/tenants/{licEnvTenantId}/allocationsV2/getmany";
                    break;

                case "list_gateways":
                    host = this.Context.Request.RequestUri.Host;
                    apiPath = "/gateway/cluster?api-version=1";
                    break;

                case "update_tenant_settings":
                    host = "api.bap.microsoft.com";
                    apiPath = "/providers/Microsoft.BusinessAppPlatform/listTenantSettings?api-version=2021-04-01";
                    httpMethod = HttpMethod.Post;
                    var settingsJson = arguments.Value<string>("settings");
                    try { body = JObject.Parse(settingsJson); }
                    catch { body = new JObject(); }
                    break;

                case "turn_on_flow":
                    host = "api.flow.microsoft.com";
                    var onEnvId = arguments.Value<string>("environment_id");
                    var onFlowId = arguments.Value<string>("flow_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{onEnvId}/flows/{onFlowId}/start?api-version=2016-11-01";
                    httpMethod = HttpMethod.Post;
                    break;

                case "turn_off_flow":
                    host = "api.flow.microsoft.com";
                    var offEnvId = arguments.Value<string>("environment_id");
                    var offFlowId = arguments.Value<string>("flow_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{offEnvId}/flows/{offFlowId}/stop?api-version=2016-11-01";
                    httpMethod = HttpMethod.Post;
                    break;

                case "cancel_all_runs":
                    host = "api.flow.microsoft.com";
                    var cancelEnvId = arguments.Value<string>("environment_id");
                    var cancelFlowId = arguments.Value<string>("flow_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{cancelEnvId}/flows/{cancelFlowId}/runs/abort?api-version=2016-11-01";
                    httpMethod = HttpMethod.Post;
                    break;

                case "check_flow_errors":
                    host = "api.flow.microsoft.com";
                    var errEnvId = arguments.Value<string>("environment_id");
                    var errFlowId = arguments.Value<string>("flow_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{errEnvId}/flows/{errFlowId}/checkFlowErrors?api-version=2016-11-01";
                    httpMethod = HttpMethod.Post;
                    body = new JObject { ["definition"] = new JObject() };
                    break;

                case "get_flow_owners":
                    host = "api.flow.microsoft.com";
                    var ownEnvId = arguments.Value<string>("environment_id");
                    var ownFlowId = arguments.Value<string>("flow_id");
                    apiPath = $"/providers/Microsoft.ProcessSimple/environments/{ownEnvId}/flows/{ownFlowId}/owners?api-version=2016-11-01";
                    break;

                case "list_unblockable_connectors":
                    host = "api.bap.microsoft.com";
                    apiPath = "/providers/PowerPlatform.Governance/v1/connectors/metadata/unblockable";
                    break;

                case "check_role_permissions":
                    host = this.Context.Request.RequestUri.Host;
                    var roleId = arguments.Value<string>("role_id");
                    apiPath = $"/api/data/v9.2/RetrieveRolePrivilegesRole(RoleId={roleId})";
                    break;

                case "add_on_licenses":
                    host = "licensing.powerplatform.microsoft.com";
                    var addOnTenantId = arguments.Value<string>("tenant_id");
                    apiPath = $"/v0.1-alpha/tenants/{addOnTenantId}/AllocationsByEnvironment";
                    break;

                case "request_license_report":
                    host = "licensing.powerplatform.microsoft.com";
                    var rptTenantId = arguments.Value<string>("tenant_id");
                    apiPath = $"/v0.1-alpha/tenants/{rptTenantId}/TenantConsumptionReport/GenerateReportURL";
                    httpMethod = HttpMethod.Post;
                    var rptParams = arguments.Value<string>("report_params");
                    try { body = JObject.Parse(rptParams ?? "{}"); }
                    catch { body = new JObject(); }
                    break;

                case "app_diagnostics":
                    host = "na.adminanalytics.powerplatform.microsoft.com";
                    apiPath = "/api/v1/metrics/diagnosticlogs";
                    httpMethod = HttpMethod.Post;
                    var diagResId = arguments.Value<string>("resource_id");
                    var diagStart = arguments.Value<string>("start_time") ?? DateTime.UtcNow.AddDays(-7).ToString("o");
                    var diagEnd = arguments.Value<string>("end_time") ?? DateTime.UtcNow.ToString("o");
                    body = new JObject
                    {
                        ["metricName"] = "powerapps.app_launch",
                        ["metricType"] = "DailyAvailabilityMetric",
                        ["startTime"] = diagStart,
                        ["endTime"] = diagEnd,
                        ["resourceId"] = diagResId,
                        ["limit"] = 100
                    };
                    break;

                case "list_copilot_agents_analytics":
                    host = "na.adminanalytics.powerplatform.microsoft.com";
                    apiPath = "/api/v1/metrics/resourceType/copilotstudio/resourceSubType/agent/latest";
                    httpMethod = HttpMethod.Post;
                    body = new JObject();
                    break;

                default:
                    throw new ArgumentException($"Unknown tool: {toolName}");
            }

            var url = $"https://{host}{apiPath}";
            var apiRequest = new HttpRequestMessage(httpMethod, url);

            if (this.Context.Request.Headers.Contains("Authorization"))
            {
                apiRequest.Headers.Add("Authorization", this.Context.Request.Headers.GetValues("Authorization"));
            }

            if (body != null)
            {
                apiRequest.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            }

            var response = await this.Context.SendAsync(apiRequest, this.CancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            JToken result;
            try { result = JToken.Parse(content); }
            catch { result = new JValue(content); }

            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented) }
                },
                ["isError"] = !response.IsSuccessStatusCode
            });
        }
        catch (Exception ex)
        {
            return CreateJsonRpcSuccessResponse(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    // ========================================
    // JSON-RPC HELPERS
    // ========================================

    private HttpResponseMessage CreateJsonRpcSuccessResponse(JToken id, JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result
            }.ToString(Newtonsoft.Json.Formatting.None))
        };
    }

    private HttpResponseMessage CreateJsonRpcErrorResponse(JToken id, int code, string message)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateJsonContent(new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = id,
                ["error"] = new JObject { ["code"] = code, ["message"] = message }
            }.ToString(Newtonsoft.Json.Formatting.None))
        };
    }
}

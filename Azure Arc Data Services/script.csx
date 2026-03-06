using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private const string API_VERSION = "2024-01-01";
    private const string ARM_HOST = "https://management.azure.com";

    // Application Insights configuration - set your connection string to enable telemetry
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    // Stores the last telemetry error for diagnostics (null = no error)
    private string _lastTelemetryError = null;

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            var authScheme = Context.Request.Headers.Authorization?.Scheme ?? "none";
            var hasAuthHeader = Context.Request.Headers.Authorization != null;
            var tokenLength = Context.Request.Headers.Authorization?.Parameter?.Length ?? 0;

            await LogToAppInsights("RequestReceived", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                Path = Context.Request.RequestUri?.AbsolutePath ?? "unknown",
                AuthScheme = authScheme,
                HasAuthHeader = hasAuthHeader,
                TokenLength = tokenLength
            });

            HttpResponseMessage response;

            switch (Context.OperationId)
            {
                case "InvokeMCP":
                    response = await HandleMcpAsync(correlationId);
                    break;
                default:
                    response = await HandleRestAsync();
                    if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        await LogToAppInsights("OAuthFailure_Passthrough", new
                        {
                            CorrelationId = correlationId,
                            OperationId = Context.OperationId,
                            StatusCode = (int)response.StatusCode,
                            AuthScheme = authScheme,
                            HasAuthHeader = hasAuthHeader,
                            TokenLength = tokenLength,
                            ArmError = TruncateForLog(errorBody, 500)
                        });
                    }
                    break;
            }

            await LogToAppInsights("RequestCompleted", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                StatusCode = (int)response.StatusCode,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });

            if (_lastTelemetryError != null)
            {
                response.Headers.TryAddWithoutValidation("X-Telemetry-Debug", _lastTelemetryError);
            }

            return response;
        }
        catch (Exception ex)
        {
            await LogToAppInsights("RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = Context.OperationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            });
            throw;
        }
    }

    // --- REST passthrough ---

    private async Task<HttpResponseMessage> HandleRestAsync()
    {
        var uriBuilder = new UriBuilder(this.Context.Request.RequestUri);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
        if (string.IsNullOrEmpty(query["api-version"]))
        {
            query["api-version"] = API_VERSION;
        }
        uriBuilder.Query = query.ToString();
        this.Context.Request.RequestUri = uriBuilder.Uri;

        return await this.Context.SendAsync(
            this.Context.Request,
            this.CancellationToken
        ).ConfigureAwait(false);
    }

    // --- MCP protocol handler ---

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch
        {
            return CreateJsonRpcErrorResponse(null, -32700, "Parse error");
        }

        var method = request["method"]?.ToString();
        var id = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        await LogToAppInsights("MCPRequest", new
        {
            CorrelationId = correlationId,
            Method = method,
            HasId = id != null
        });

        switch (method)
        {
            case "initialize":
                return HandleInitialize(id);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(id, new JObject());

            case "tools/list":
                return HandleToolsList(id);

            case "tools/call":
                return await HandleToolsCallAsync(@params, id, correlationId);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(id, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateJsonRpcSuccessResponse(id, new JObject());

            default:
                return CreateJsonRpcErrorResponse(id, -32601, "Method not found", method);
        }
    }

    private HttpResponseMessage HandleInitialize(JToken id)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "azure-arc-data-services",
                ["version"] = "2.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(id, result);
    }

    // --- MCP tool definitions ---

    private static JObject MakeTool(string name, string description, JObject properties, JArray required)
    {
        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            }
        };
    }

    private static JObject Prop(string type, string description)
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    private static readonly JObject P_SUB = Prop("string", "The Azure subscription ID.");
    private static readonly JObject P_RG = Prop("string", "The name of the Azure resource group.");
    private static readonly JObject P_INSTANCE = Prop("string", "The name of the SQL Server instance.");

    private static readonly JArray TOOLS = new JArray
    {
        // SQL Server Instances
        MakeTool("listSqlServerInstancesBySubscription",
            "List all Azure Arc-enabled SQL Server instances in a subscription.",
            new JObject { ["subscriptionId"] = P_SUB },
            new JArray { "subscriptionId" }),

        MakeTool("listSqlServerInstances",
            "List Azure Arc-enabled SQL Server instances in a resource group.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG },
            new JArray { "subscriptionId", "resourceGroupName" }),

        MakeTool("getSqlServerInstance",
            "Get details of a specific Azure Arc-enabled SQL Server instance.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName" }),

        MakeTool("createOrUpdateSqlServerInstance",
            "Create or update an Azure Arc-enabled SQL Server instance registration.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE,
                ["location"] = Prop("string", "Azure region (e.g. eastus, westus2)."),
                ["tags"] = new JObject { ["type"] = "object", ["description"] = "Resource tags as key-value pairs." },
                ["properties"] = new JObject { ["type"] = "object", ["description"] = "SQL Server instance properties (containerResourceId, version, edition, licenseType, etc.)." } },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName", "location" }),

        MakeTool("updateSqlServerInstance",
            "Partially update an Azure Arc-enabled SQL Server instance (tags, license type, monitoring, backup policy).",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE,
                ["tags"] = new JObject { ["type"] = "object", ["description"] = "Resource tags to update." },
                ["properties"] = new JObject { ["type"] = "object", ["description"] = "Properties to update (licenseType, monitoring, backupPolicy)." } },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName" }),

        MakeTool("deleteSqlServerInstance",
            "Delete an Azure Arc-enabled SQL Server instance registration.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName" }),

        // Databases
        MakeTool("listDatabases",
            "List databases on an Azure Arc-enabled SQL Server instance.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName" }),

        MakeTool("getDatabase",
            "Get details of a specific database on an Azure Arc-enabled SQL Server instance.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE,
                ["databaseName"] = Prop("string", "The name of the database.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName", "databaseName" }),

        // Availability Groups
        MakeTool("listAvailabilityGroups",
            "List SQL Server availability groups on an Azure Arc-enabled SQL Server instance.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName" }),

        MakeTool("getAvailabilityGroup",
            "Get details of a specific SQL Server availability group.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE,
                ["availabilityGroupName"] = Prop("string", "The name of the availability group.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName", "availabilityGroupName" }),

        // Failover Groups
        MakeTool("listFailoverGroups",
            "List failover groups on an Azure Arc-enabled SQL Server instance.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName" }),

        MakeTool("getFailoverGroup",
            "Get details of a specific failover group on an Azure Arc-enabled SQL Server instance.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG, ["sqlServerInstanceName"] = P_INSTANCE,
                ["failoverGroupName"] = Prop("string", "The name of the failover group.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerInstanceName", "failoverGroupName" }),

        // SQL Server Licenses
        MakeTool("listSqlServerLicenses",
            "List all SQL Server license resources in a subscription.",
            new JObject { ["subscriptionId"] = P_SUB },
            new JArray { "subscriptionId" }),

        MakeTool("getSqlServerLicense",
            "Get details of a specific SQL Server license resource.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlServerLicenseName"] = Prop("string", "The name of the SQL Server license.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerLicenseName" }),

        MakeTool("createOrUpdateSqlServerLicense",
            "Create or update a SQL Server license resource for billing management.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlServerLicenseName"] = Prop("string", "The name of the SQL Server license."),
                ["location"] = Prop("string", "Azure region (e.g. eastus)."),
                ["tags"] = new JObject { ["type"] = "object", ["description"] = "Resource tags." },
                ["properties"] = new JObject { ["type"] = "object", ["description"] = "License properties (billingPlan, physicalCoreCount, activationState, scopeType)." } },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerLicenseName", "location" }),

        MakeTool("deleteSqlServerLicense",
            "Delete a SQL Server license resource.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlServerLicenseName"] = Prop("string", "The name of the SQL Server license.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerLicenseName" }),

        // ESU Licenses
        MakeTool("listSqlServerEsuLicenses",
            "List all SQL Server Extended Security Update (ESU) license resources in a subscription.",
            new JObject { ["subscriptionId"] = P_SUB },
            new JArray { "subscriptionId" }),

        MakeTool("getSqlServerEsuLicense",
            "Get details of a specific SQL Server ESU license.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlServerEsuLicenseName"] = Prop("string", "The name of the ESU license.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerEsuLicenseName" }),

        MakeTool("createOrUpdateSqlServerEsuLicense",
            "Create or update a SQL Server Extended Security Update (ESU) license.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlServerEsuLicenseName"] = Prop("string", "The name of the ESU license."),
                ["location"] = Prop("string", "Azure region (e.g. eastus)."),
                ["tags"] = new JObject { ["type"] = "object", ["description"] = "Resource tags." },
                ["properties"] = new JObject { ["type"] = "object", ["description"] = "ESU license properties (billingPlan, physicalCoreCount, activationState, version, esuYear)." } },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerEsuLicenseName", "location" }),

        MakeTool("deleteSqlServerEsuLicense",
            "Delete a SQL Server ESU license.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlServerEsuLicenseName"] = Prop("string", "The name of the ESU license.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlServerEsuLicenseName" }),

        // Data Controllers
        MakeTool("listDataControllers",
            "List Azure Arc data controllers in a resource group.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG },
            new JArray { "subscriptionId", "resourceGroupName" }),

        MakeTool("getDataController",
            "Get details of a specific Azure Arc data controller.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["dataControllerName"] = Prop("string", "The name of the data controller.") },
            new JArray { "subscriptionId", "resourceGroupName", "dataControllerName" }),

        // SQL Managed Instances
        MakeTool("listSqlManagedInstances",
            "List SQL Managed Instances enabled by Azure Arc in a resource group.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG },
            new JArray { "subscriptionId", "resourceGroupName" }),

        MakeTool("getSqlManagedInstance",
            "Get details of a specific SQL Managed Instance enabled by Azure Arc.",
            new JObject { ["subscriptionId"] = P_SUB, ["resourceGroupName"] = P_RG,
                ["sqlManagedInstanceName"] = Prop("string", "The name of the SQL Managed Instance.") },
            new JArray { "subscriptionId", "resourceGroupName", "sqlManagedInstanceName" }),

        // Resource Graph
        MakeTool("queryResourceGraph",
            "Execute an Azure Resource Graph query to search across subscriptions for Arc Data Services resources.",
            new JObject {
                ["query"] = Prop("string", "The Azure Resource Graph KQL query."),
                ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope the query." } },
            new JArray { "query" })
    };

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        return CreateJsonRpcSuccessResponse(id, new JObject { ["tools"] = TOOLS });
    }

    // --- MCP tools/call dispatcher ---

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsights("MCPToolCall", new
        {
            CorrelationId = correlationId,
            Tool = toolName,
            HasArguments = arguments.HasValues
        });

        try
        {
            string path;
            string httpMethod = "GET";
            JObject requestBody = null;
            string apiVersion = API_VERSION;

            var sub = arguments["subscriptionId"]?.ToString();
            var rg = arguments["resourceGroupName"]?.ToString();
            var instance = arguments["sqlServerInstanceName"]?.ToString();

            switch (toolName)
            {
                // SQL Server Instances
                case "listSqlServerInstancesBySubscription":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/providers/Microsoft.AzureArcData/sqlServerInstances";
                    break;

                case "listSqlServerInstances":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances";
                    break;

                case "getSqlServerInstance":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}";
                    break;

                case "createOrUpdateSqlServerInstance":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}";
                    httpMethod = "PUT";
                    requestBody = new JObject { ["location"] = arguments["location"]?.ToString() };
                    if (arguments["tags"] != null) requestBody["tags"] = arguments["tags"];
                    if (arguments["properties"] != null) requestBody["properties"] = arguments["properties"];
                    break;

                case "updateSqlServerInstance":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}";
                    httpMethod = "PATCH";
                    requestBody = new JObject();
                    if (arguments["tags"] != null) requestBody["tags"] = arguments["tags"];
                    if (arguments["properties"] != null) requestBody["properties"] = arguments["properties"];
                    break;

                case "deleteSqlServerInstance":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}";
                    httpMethod = "DELETE";
                    break;

                // Databases
                case "listDatabases":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}/databases";
                    break;

                case "getDatabase":
                    var dbName = arguments["databaseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}/databases/{Uri.EscapeDataString(dbName)}";
                    break;

                // Availability Groups
                case "listAvailabilityGroups":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}/availabilityGroups";
                    break;

                case "getAvailabilityGroup":
                    var agName = arguments["availabilityGroupName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}/availabilityGroups/{Uri.EscapeDataString(agName)}";
                    break;

                // Failover Groups
                case "listFailoverGroups":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}/failoverGroups";
                    break;

                case "getFailoverGroup":
                    var fgName = arguments["failoverGroupName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerInstances/{Uri.EscapeDataString(instance)}/failoverGroups/{Uri.EscapeDataString(fgName)}";
                    break;

                // SQL Server Licenses
                case "listSqlServerLicenses":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/providers/Microsoft.AzureArcData/sqlServerLicenses";
                    break;

                case "getSqlServerLicense":
                {
                    var licenseName = arguments["sqlServerLicenseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerLicenses/{Uri.EscapeDataString(licenseName)}";
                    break;
                }

                case "createOrUpdateSqlServerLicense":
                {
                    var licenseName = arguments["sqlServerLicenseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerLicenses/{Uri.EscapeDataString(licenseName)}";
                    httpMethod = "PUT";
                    requestBody = new JObject { ["location"] = arguments["location"]?.ToString() };
                    if (arguments["tags"] != null) requestBody["tags"] = arguments["tags"];
                    if (arguments["properties"] != null) requestBody["properties"] = arguments["properties"];
                    break;
                }

                case "deleteSqlServerLicense":
                {
                    var licenseName = arguments["sqlServerLicenseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerLicenses/{Uri.EscapeDataString(licenseName)}";
                    httpMethod = "DELETE";
                    break;
                }

                // ESU Licenses
                case "listSqlServerEsuLicenses":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/providers/Microsoft.AzureArcData/sqlServerEsuLicenses";
                    break;

                case "getSqlServerEsuLicense":
                {
                    var esuName = arguments["sqlServerEsuLicenseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerEsuLicenses/{Uri.EscapeDataString(esuName)}";
                    break;
                }

                case "createOrUpdateSqlServerEsuLicense":
                {
                    var esuName = arguments["sqlServerEsuLicenseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerEsuLicenses/{Uri.EscapeDataString(esuName)}";
                    httpMethod = "PUT";
                    requestBody = new JObject { ["location"] = arguments["location"]?.ToString() };
                    if (arguments["tags"] != null) requestBody["tags"] = arguments["tags"];
                    if (arguments["properties"] != null) requestBody["properties"] = arguments["properties"];
                    break;
                }

                case "deleteSqlServerEsuLicense":
                {
                    var esuName = arguments["sqlServerEsuLicenseName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlServerEsuLicenses/{Uri.EscapeDataString(esuName)}";
                    httpMethod = "DELETE";
                    break;
                }

                // Data Controllers
                case "listDataControllers":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/dataControllers";
                    break;

                case "getDataController":
                {
                    var dcName = arguments["dataControllerName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/dataControllers/{Uri.EscapeDataString(dcName)}";
                    break;
                }

                // SQL Managed Instances
                case "listSqlManagedInstances":
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlManagedInstances";
                    break;

                case "getSqlManagedInstance":
                {
                    var miName = arguments["sqlManagedInstanceName"]?.ToString();
                    path = $"/subscriptions/{Uri.EscapeDataString(sub)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.AzureArcData/sqlManagedInstances/{Uri.EscapeDataString(miName)}";
                    break;
                }

                // Resource Graph
                case "queryResourceGraph":
                    path = "/providers/Microsoft.ResourceGraph/resources";
                    httpMethod = "POST";
                    apiVersion = "2021-03-01";
                    requestBody = new JObject { ["query"] = arguments["query"]?.ToString() };
                    if (arguments["subscriptions"] != null)
                        requestBody["subscriptions"] = arguments["subscriptions"];
                    break;

                default:
                    return CreateJsonRpcErrorResponse(id, -32602, $"Unknown tool: {toolName}");
            }

            var result = await CallArmApiAsync(path, httpMethod, requestBody, apiVersion);
            return CreateJsonRpcSuccessResponse(id, new JObject
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
            await LogToAppInsights("MCPToolError", new
            {
                CorrelationId = correlationId,
                Tool = toolName,
                ErrorMessage = ex.Message
            });

            return CreateJsonRpcSuccessResponse(id, new JObject
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

    // --- ARM API caller ---

    private async Task<JToken> CallArmApiAsync(string path, string httpMethod, JObject body = null, string apiVersion = null)
    {
        apiVersion = apiVersion ?? API_VERSION;
        var uri = new Uri($"{ARM_HOST}{path}?api-version={apiVersion}");

        var request = new HttpRequestMessage(new HttpMethod(httpMethod), uri);
        request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (body != null && (httpMethod == "PUT" || httpMethod == "PATCH" || httpMethod == "POST"))
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            JObject errorBody;
            try
            {
                errorBody = JObject.Parse(content);
            }
            catch
            {
                errorBody = new JObject
                {
                    ["error"] = new JObject
                    {
                        ["code"] = response.StatusCode.ToString(),
                        ["message"] = content
                    }
                };
            }
            throw new InvalidOperationException(
                $"ARM API returned {(int)response.StatusCode}: {errorBody.ToString(Newtonsoft.Json.Formatting.None)}"
            );
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new JObject { ["status"] = "Success" };
        }

        return JToken.Parse(content);
    }

    // --- JSON-RPC helpers ---

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

    // --- Application Insights telemetry ---

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);

            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return;

            var propsDict = new Dictionary<string, string>();
            if (properties != null)
            {
                var propsJson = Newtonsoft.Json.JsonConvert.SerializeObject(properties);
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(telemetryData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var telemetryResponse = await Context.SendAsync(request, CancellationToken).ConfigureAwait(false);
            if (!telemetryResponse.IsSuccessStatusCode)
            {
                var errorBody = await telemetryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                _lastTelemetryError = $"AppInsights returned {(int)telemetryResponse.StatusCode}: {errorBody}";
            }
        }
        catch (Exception ex)
        {
            _lastTelemetryError = $"Telemetry send failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("InstrumentationKey=".Length);
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    private string TruncateForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }
}

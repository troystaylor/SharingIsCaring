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
    private const string SERVER_NAME = "power-platform-inventory-mcp";
    private const string SERVER_VERSION = "1.0.0";

    // Power Platform Inventory API
    private const string INVENTORY_API_BASE = "https://api.powerplatform.com";
    private const string INVENTORY_API_VERSION = "2024-10-01";
    private const string INVENTORY_QUERY_PATH = "/resourcequery/resources/query";
    private const string DEFAULT_TABLE = "PowerPlatformResources";

    // Application Insights
    private const string APP_INSIGHTS_CONNECTION_STRING = "[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // Resource types that emit connector inventory data
    private static readonly string[] CONNECTOR_RESOURCE_TYPES = new[]
    {
        "microsoft.powerapps/canvasapps",
        "microsoft.powerapps/modeldrivenapps",
        "microsoft.powerautomate/cloudflows",
        "microsoft.powerautomate/agentflows",
        "microsoft.powerautomate/m365agentflows",
        "microsoft.copilotstudio/agents"
    };

    // Static catalog of supported resource types (from inventory schema reference)
    private static readonly (string Id, string Name)[] RESOURCE_TYPES = new[]
    {
        ("microsoft.powerapps/canvasapps", "Canvas apps"),
        ("microsoft.powerapps/modeldrivenapps", "Model-driven apps"),
        ("microsoft.powerapps/codeapps", "Code apps"),
        ("microsoft.powerapps/apps", "App Builder apps"),
        ("microsoft.powerautomate/cloudflows", "Cloud flows"),
        ("microsoft.powerautomate/agentflows", "Agent flows"),
        ("microsoft.powerautomate/m365agentflows", "Workflow agent flows"),
        ("microsoft.copilotstudio/agents", "Copilot Studio agents"),
        ("microsoft.powerplatform/environments", "Environments"),
        ("microsoft.powerplatform/environmentgroups", "Environment groups")
    };

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        try
        {
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    return await HandleMcpEntryAsync().ConfigureAwait(false);
                case "CountAllResources":
                    return CreateTypedResponse(await CountAllResources().ConfigureAwait(false));
                case "CountByType":
                    return CreateTypedResponse(await CountByType().ConfigureAwait(false));
                case "CountByEnvironment":
                    return CreateTypedResponse(await CountByEnvironment().ConfigureAwait(false));
                case "CountByRegion":
                    return CreateTypedResponse(await CountByRegion().ConfigureAwait(false));
                case "TopOwners":
                    return CreateTypedResponse(await TopOwners(GetIntQuery("top", 25)).ConfigureAwait(false));
                case "RecentResources":
                    return CreateTypedResponse(await RecentResources(GetIntQuery("hours", 24), GetIntQuery("top", 100)).ConfigureAwait(false));
                case "FindResource":
                    return CreateTypedResponse(await FindResource(GetStringQuery("resourceId"), GetStringQuery("resourceType")).ConfigureAwait(false));
                case "ListResourcesByType":
                    return CreateTypedResponse(await ListResourcesByType(
                        GetStringQuery("resourceType"),
                        GetStringQuery("environmentId"),
                        GetIntQuery("top", 100),
                        GetStringQuery("skipToken")).ConfigureAwait(false));
                case "ListEnvironments":
                    return CreateTypedResponse(await ListEnvironments(GetIntQuery("top", 200)).ConfigureAwait(false));
                case "ListEnvironmentGroups":
                    return CreateTypedResponse(await ListEnvironmentGroups().ConfigureAwait(false));
                case "TopConnectors":
                    return CreateTypedResponse(await TopConnectors(GetIntQuery("top", 10)).ConfigureAwait(false));
                case "ConnectorCountDistribution":
                    return CreateTypedResponse(await ConnectorCountDistribution().ConfigureAwait(false));
                case "ResourcesUsingConnector":
                    return CreateTypedResponse(await ResourcesUsingConnector(GetStringQuery("connectorId"), GetIntQuery("top", 200)).ConfigureAwait(false));
                case "ConnectorUsageByEnvironment":
                    return CreateTypedResponse(await ConnectorUsageByEnvironment(GetStringQuery("environmentId")).ConfigureAwait(false));
                case "RunQuery":
                    return await HandleRunQueryTyped().ConfigureAwait(false);
                case "GetEnvironmentDropdown":
                    return CreateTypedResponse(await GetEnvironmentDropdown().ConfigureAwait(false));
                case "GetResourceTypeDropdown":
                    return CreateTypedResponse(GetResourceTypeDropdown());
                case "GetConnectorDropdown":
                    return CreateTypedResponse(await GetConnectorDropdown().ConfigureAwait(false));
                default:
                    return CreateJsonRpcErrorResponse(null, -32601, $"Unknown operation: {this.Context.OperationId}");
            }
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"Unhandled error: {ex.Message}");
            await LogToAppInsights("UnhandledError", new Dictionary<string, string>
            {
                ["OperationId"] = this.Context.OperationId,
                ["Error"] = ex.Message
            }).ConfigureAwait(false);

            return CreateTypedErrorResponse(ex.Message, 500);
        }
    }

    // ─── MCP Entry ──────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpEntryAsync()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return CreateJsonRpcErrorResponse(null, -32600, "Request body is required");

        JObject payload;
        try
        {
            payload = JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            return CreateJsonRpcErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        if (!payload.ContainsKey("jsonrpc"))
            return CreateJsonRpcErrorResponse(null, -32600, "Invalid request format. Expected JSON-RPC 2.0.");

        return await HandleMcpAsync(payload).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> HandleMcpAsync(JObject request)
    {
        var method = request["method"]?.ToString();
        var requestId = request["id"];
        var @params = request["params"] as JObject ?? new JObject();

        this.Context.Logger.LogInformation($"MCP method: {method}");
        await LogToAppInsights("MCPMethodCall", new Dictionary<string, string> { ["Method"] = method ?? "" }).ConfigureAwait(false);

        switch (method)
        {
            case "initialize":
                var protocolVersion = @params["protocolVersion"]?.ToString() ?? "2024-11-05";
                return CreateJsonRpcSuccessResponse(requestId, new JObject
                {
                    ["protocolVersion"] = protocolVersion,
                    ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JObject { ["name"] = SERVER_NAME, ["version"] = SERVER_VERSION }
                });

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateJsonRpcSuccessResponse(requestId, new JObject());

            case "tools/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = GetToolDefinitions() });

            case "tools/call":
                return await HandleToolsCall(@params, requestId).ConfigureAwait(false);

            case "resources/list":
                return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

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
            ToolDef("inventory_count_all", "Return the total count of Power Platform resources in the tenant. Sample query: PowerPlatformResources | count.", new JObject(), new JArray()),

            ToolDef("inventory_count_by_type", "Return resource counts grouped by resource type (canvas apps, cloud flows, agents, etc.). Sample query: summarize count() by type.", new JObject(), new JArray()),

            ToolDef("inventory_count_by_environment", "Return resource counts grouped by environment ID. Sample query: summarize count() by environmentId.", new JObject(), new JArray()),

            ToolDef("inventory_count_by_region", "Return resource counts grouped by Azure region (location field). Sample query: summarize count() by location.", new JObject(), new JArray()),

            ToolDef("inventory_top_owners", "Return the top N owners ranked by number of Power Platform resources they own. Useful for spotting champions and orphan-risk users.", new JObject
            {
                ["top"] = new JObject { ["type"] = "integer", ["description"] = "Maximum number of owners to return (default 25)." }
            }, new JArray()),

            ToolDef("inventory_recent_resources", "Return resources created within the last N hours (default 24). Sample query: where createdAt >= ago(24h).", new JObject
            {
                ["hours"] = new JObject { ["type"] = "integer", ["description"] = "Look-back window in hours (default 24)." },
                ["top"] = new JObject { ["type"] = "integer", ["description"] = "Maximum rows to return (default 100)." }
            }, new JArray()),

            ToolDef("inventory_find_resource", "Find a single resource by its unique ID (the 'name' field). Optionally scope by resource type for a faster lookup.", new JObject
            {
                ["resourceId"] = new JObject { ["type"] = "string", ["description"] = "The unique resource ID (for example, the agent GUID from a Copilot Studio URL)." },
                ["resourceType"] = new JObject { ["type"] = "string", ["description"] = "Optional resource type filter (for example, microsoft.copilotstudio/agents)." }
            }, new JArray { "resourceId" }),

            ToolDef("inventory_list_resources_by_type", "List resources of a given type with optional environment filter and paging. Use this for tabular inventory views.", new JObject
            {
                ["resourceType"] = new JObject { ["type"] = "string", ["description"] = "Resource type identifier (for example, microsoft.powerapps/canvasapps)." },
                ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Optional environment ID filter." },
                ["top"] = new JObject { ["type"] = "integer", ["description"] = "Page size (default 100)." },
                ["skipToken"] = new JObject { ["type"] = "string", ["description"] = "Continuation token from a previous page." }
            }, new JArray { "resourceType" }),

            ToolDef("inventory_list_environments", "List every environment in the tenant with type, state, group assignment, and region.", new JObject
            {
                ["top"] = new JObject { ["type"] = "integer", ["description"] = "Maximum environments to return (default 200)." }
            }, new JArray()),

            ToolDef("inventory_list_environment_groups", "List every environment group in the tenant.", new JObject(), new JArray()),

            ToolDef("inventory_top_connectors", "List the connectors used by the most distinct resources across canvas apps, model-driven apps, cloud flows, agent flows, workflow agent flows, and Copilot Studio agents. (Connector inventory preview.)", new JObject
            {
                ["top"] = new JObject { ["type"] = "integer", ["description"] = "Maximum connectors to return (default 10)." }
            }, new JArray()),

            ToolDef("inventory_connector_count_distribution", "Show how many resources use 0, 1, 2, or more connectors. Useful for spotting complexity outliers. (Connector inventory preview.)", new JObject(), new JArray()),

            ToolDef("inventory_resources_using_connector", "Find every Power Platform resource that uses a specific connector. Critical for impact analysis when a connector is deprecated or requires new licensing. (Connector inventory preview.)", new JObject
            {
                ["connectorId"] = new JObject { ["type"] = "string", ["description"] = "Connector identifier (for example, shared_sharepointonline)." },
                ["top"] = new JObject { ["type"] = "integer", ["description"] = "Maximum resources to return (default 200)." }
            }, new JArray { "connectorId" }),

            ToolDef("inventory_connector_usage_by_environment", "List every connector used in every environment, with the count of distinct resources that use it. Informs DLP policy decisions. (Connector inventory preview.)", new JObject
            {
                ["environmentId"] = new JObject { ["type"] = "string", ["description"] = "Optional environment ID filter." }
            }, new JArray()),

            ToolDef("inventory_run_query", "Advanced escape hatch. Submit a raw inventory query payload (TableName + Clauses + Options) directly to the resource query endpoint. Use when no preset tool fits.", new JObject
            {
                ["TableName"] = new JObject { ["type"] = "string", ["description"] = "Target table (default PowerPlatformResources)." },
                ["Clauses"] = new JObject { ["type"] = "array", ["description"] = "Array of query clauses (where, project, take, orderby, summarize, extend, join, etc.)." },
                ["Options"] = new JObject { ["type"] = "object", ["description"] = "Optional pagination object: {Top, Skip, SkipToken}." }
            }, new JArray { "Clauses" })
        };
    }

    private JObject ToolDef(string name, string description, JObject properties, JArray required)
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

    // ─── Tool Call Router ───────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId)
    {
        var toolName = @params["name"]?.ToString();
        var args = @params["arguments"] as JObject ?? new JObject();

        this.Context.Logger.LogInformation($"Tool call: {toolName}");
        await LogToAppInsights("ToolCall", new Dictionary<string, string>
        {
            ["ToolName"] = toolName ?? "",
            ["HasArguments"] = (args.Count > 0).ToString()
        }).ConfigureAwait(false);

        try
        {
            JToken result;
            switch (toolName)
            {
                case "inventory_count_all":
                    result = await CountAllResources().ConfigureAwait(false);
                    break;
                case "inventory_count_by_type":
                    result = await CountByType().ConfigureAwait(false);
                    break;
                case "inventory_count_by_environment":
                    result = await CountByEnvironment().ConfigureAwait(false);
                    break;
                case "inventory_count_by_region":
                    result = await CountByRegion().ConfigureAwait(false);
                    break;
                case "inventory_top_owners":
                    result = await TopOwners(GetInt(args, "top", 25)).ConfigureAwait(false);
                    break;
                case "inventory_recent_resources":
                    result = await RecentResources(GetInt(args, "hours", 24), GetInt(args, "top", 100)).ConfigureAwait(false);
                    break;
                case "inventory_find_resource":
                    result = await FindResource(GetString(args, "resourceId"), GetString(args, "resourceType")).ConfigureAwait(false);
                    break;
                case "inventory_list_resources_by_type":
                    result = await ListResourcesByType(
                        GetString(args, "resourceType"),
                        GetString(args, "environmentId"),
                        GetInt(args, "top", 100),
                        GetString(args, "skipToken")).ConfigureAwait(false);
                    break;
                case "inventory_list_environments":
                    result = await ListEnvironments(GetInt(args, "top", 200)).ConfigureAwait(false);
                    break;
                case "inventory_list_environment_groups":
                    result = await ListEnvironmentGroups().ConfigureAwait(false);
                    break;
                case "inventory_top_connectors":
                    result = await TopConnectors(GetInt(args, "top", 10)).ConfigureAwait(false);
                    break;
                case "inventory_connector_count_distribution":
                    result = await ConnectorCountDistribution().ConfigureAwait(false);
                    break;
                case "inventory_resources_using_connector":
                    result = await ResourcesUsingConnector(GetString(args, "connectorId"), GetInt(args, "top", 200)).ConfigureAwait(false);
                    break;
                case "inventory_connector_usage_by_environment":
                    result = await ConnectorUsageByEnvironment(GetString(args, "environmentId")).ConfigureAwait(false);
                    break;
                case "inventory_run_query":
                    result = await RunRawQuery(args).ConfigureAwait(false);
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
                ["ToolName"] = toolName ?? "",
                ["Error"] = ex.Message
            }).ConfigureAwait(false);

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

    // ─── Sample Query Implementations ───────────────────────────────────

    private async Task<JToken> CountAllResources()
    {
        var clauses = new JArray { new JObject { ["$type"] = "count" } };
        var response = await PostQuery(BuildQuery(clauses)).ConfigureAwait(false);
        var data = response["data"] as JArray;
        var total = 0;
        if (data != null && data.Count > 0)
        {
            total = data[0]["Count"]?.Value<int?>()
                ?? data[0]["count_"]?.Value<int?>()
                ?? data[0]["count"]?.Value<int?>()
                ?? (response["totalRecords"]?.Value<int?>() ?? 0);
        }
        else
        {
            total = response["totalRecords"]?.Value<int?>() ?? 0;
        }
        return new JObject { ["totalCount"] = total };
    }

    private async Task<JToken> CountByType()
    {
        var clauses = new JArray
        {
            SummarizeCount("resourceCount", new JArray { "type" }),
            OrderBy(new JObject { ["resourceCount"] = "desc" })
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> CountByEnvironment()
    {
        var clauses = new JArray
        {
            Extend("environmentId", "tostring(properties.environmentId)"),
            SummarizeCount("resourceCount", new JArray { "environmentId" }),
            OrderBy(new JObject { ["resourceCount"] = "desc" })
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> CountByRegion()
    {
        var clauses = new JArray
        {
            SummarizeCount("resourceCount", new JArray { "location" }),
            OrderBy(new JObject { ["resourceCount"] = "desc" })
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> TopOwners(int top)
    {
        var clauses = new JArray
        {
            Extend("ownerId", "tostring(properties.ownerId)"),
            Where("ownerId", "!=", new JArray { "''" }),
            SummarizeCount("resourceCount", new JArray { "ownerId" }),
            OrderBy(new JObject { ["resourceCount"] = "desc" }),
            Take(top)
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> RecentResources(int hours, int top)
    {
        var clauses = new JArray
        {
            Extend("createdAt", "todatetime(properties.createdAt)"),
            Where("createdAt", ">=", new JArray { $"ago({hours}h)" }),
            Project(new JArray
            {
                "id",
                "name",
                "displayName = tostring(properties.displayName)",
                "type",
                "environmentId = tostring(properties.environmentId)",
                "createdAt = tostring(properties.createdAt)",
                "createdBy = tostring(properties.createdBy)"
            }),
            OrderBy(new JObject { ["createdAt"] = "desc" }),
            Take(top)
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> FindResource(string resourceId, string resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("resourceId is required.");

        var clauses = new JArray();
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            clauses.Add(Where("type", "==", new JArray { $"'{resourceType}'" }));
        }
        clauses.Add(Where("name", "==", new JArray { $"'{resourceId}'" }));
        clauses.Add(Take(10));

        var response = await PostQuery(BuildQuery(clauses)).ConfigureAwait(false);
        var rows = response["data"] as JArray ?? new JArray();
        return new JObject
        {
            ["rowCount"] = rows.Count,
            ["rows"] = rows
        };
    }

    private async Task<JToken> ListResourcesByType(string resourceType, string environmentId, int top, string skipToken)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            throw new ArgumentException("resourceType is required.");

        var clauses = new JArray
        {
            Where("type", "==", new JArray { $"'{resourceType}'" })
        };

        if (!string.IsNullOrWhiteSpace(environmentId))
        {
            clauses.Add(Extend("environmentId", "tostring(properties.environmentId)"));
            clauses.Add(Where("environmentId", "==", new JArray { $"'{environmentId}'" }));
        }

        clauses.Add(Project(new JArray
        {
            "name",
            "displayName = tostring(properties.displayName)",
            "type",
            "location",
            "environmentId = tostring(properties.environmentId)",
            "ownerId = tostring(properties.ownerId)",
            "createdAt = tostring(properties.createdAt)",
            "createdBy = tostring(properties.createdBy)",
            "lastModifiedAt = tostring(properties.lastModifiedAt)"
        }));

        var options = new JObject { ["Top"] = top };
        if (!string.IsNullOrWhiteSpace(skipToken))
            options["SkipToken"] = skipToken;

        var response = await PostQuery(BuildQuery(clauses, options)).ConfigureAwait(false);
        return new JObject
        {
            ["totalRecords"] = response["totalRecords"],
            ["count"] = response["count"],
            ["skipToken"] = response["skipToken"] ?? string.Empty,
            ["rows"] = response["data"] ?? new JArray()
        };
    }

    private async Task<JToken> ListEnvironments(int top)
    {
        var clauses = new JArray
        {
            Where("type", "==", new JArray { "'microsoft.powerplatform/environments'" }),
            Project(new JArray
            {
                "name",
                "displayName = tostring(properties.displayName)",
                "location",
                "environmentType = tostring(properties.environmentType)",
                "isManaged = tobool(properties.isManaged)",
                "environmentGroup = tostring(properties.environmentGroup)",
                "environmentGroupId = tostring(properties.environmentGroupId)"
            }),
            OrderBy(new JObject { ["displayName"] = "asc" }),
            Take(top)
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> ListEnvironmentGroups()
    {
        var clauses = new JArray
        {
            Where("type", "==", new JArray { "'microsoft.powerplatform/environmentgroups'" }),
            Project(new JArray
            {
                "name",
                "displayName = tostring(properties.displayName)",
                "description = tostring(properties.description)"
            }),
            OrderBy(new JObject { ["displayName"] = "asc" })
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> TopConnectors(int top)
    {
        var clauses = new JArray
        {
            Where("type", "in~", ConnectorResourceTypeValues()),
            new JObject { ["$type"] = "mvexpand", ["FieldName"] = "connector", ["Expression"] = "properties.powerPlatformConnectors" },
            Extend("connectorId", "tostring(connector.connectorId)"),
            Where("connectorId", "!=", new JArray { "''" }),
            new JObject
            {
                ["$type"] = "summarize",
                ["SummarizeClauseExpression"] = new JObject
                {
                    ["OperatorName"] = "dcount",
                    ["OperatorFieldName"] = "resourceCount",
                    ["FieldList"] = new JArray { "connectorId" },
                    ["DistinctFieldName"] = "name"
                }
            },
            OrderBy(new JObject { ["resourceCount"] = "desc" }),
            Take(top)
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> ConnectorCountDistribution()
    {
        var clauses = new JArray
        {
            Where("type", "in~", ConnectorResourceTypeValues()),
            Extend("connectorCount", "toint(array_length(properties.powerPlatformConnectors))"),
            SummarizeCount("resourceCount", new JArray { "connectorCount" }),
            OrderBy(new JObject { ["connectorCount"] = "asc" })
        };
        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> ResourcesUsingConnector(string connectorId, int top)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
            throw new ArgumentException("connectorId is required.");

        var clauses = new JArray
        {
            Where("type", "in~", ConnectorResourceTypeValues()),
            new JObject { ["$type"] = "mvexpand", ["FieldName"] = "connector", ["Expression"] = "properties.powerPlatformConnectors" },
            Where("tostring(connector.connectorId)", "==", new JArray { $"'{connectorId}'" }),
            Project(new JArray
            {
                "resourceName = tostring(properties.displayName)",
                "resourceId = name",
                "resourceType = type",
                "environmentId = tostring(properties.environmentId)",
                "operationsUsed = connector.operations"
            }),
            Take(top)
        };
        var response = await PostQuery(BuildQuery(clauses)).ConfigureAwait(false);
        return new JObject
        {
            ["connectorId"] = connectorId,
            ["rowCount"] = (response["data"] as JArray)?.Count ?? 0,
            ["rows"] = response["data"] ?? new JArray()
        };
    }

    private async Task<JToken> ConnectorUsageByEnvironment(string environmentId)
    {
        var clauses = new JArray
        {
            Where("type", "in~", ConnectorResourceTypeValues())
        };

        if (!string.IsNullOrWhiteSpace(environmentId))
        {
            clauses.Add(Extend("envFilter", "tostring(properties.environmentId)"));
            clauses.Add(Where("envFilter", "==", new JArray { $"'{environmentId}'" }));
        }

        clauses.Add(new JObject { ["$type"] = "mvexpand", ["FieldName"] = "connector", ["Expression"] = "properties.powerPlatformConnectors" });
        clauses.Add(Extend("connectorId", "tostring(connector.connectorId)"));
        clauses.Add(Where("connectorId", "!=", new JArray { "''" }));
        clauses.Add(Extend("environmentId", "tostring(properties.environmentId)"));
        clauses.Add(new JObject
        {
            ["$type"] = "summarize",
            ["SummarizeClauseExpression"] = new JObject
            {
                ["OperatorName"] = "dcount",
                ["OperatorFieldName"] = "resourceCount",
                ["FieldList"] = new JArray { "environmentId", "connectorId" },
                ["DistinctFieldName"] = "name"
            }
        });
        clauses.Add(OrderBy(new JObject { ["environmentId"] = "asc", ["resourceCount"] = "desc" }));

        return WrapRows(await PostQuery(BuildQuery(clauses)).ConfigureAwait(false));
    }

    private async Task<JToken> RunRawQuery(JObject args)
    {
        var query = new JObject
        {
            ["TableName"] = string.IsNullOrWhiteSpace(args["TableName"]?.ToString()) ? DEFAULT_TABLE : args["TableName"].ToString(),
            ["Clauses"] = args["Clauses"] as JArray ?? new JArray()
        };
        if (args["Options"] is JObject opts) query["Options"] = opts;

        return await PostQuery(query).ConfigureAwait(false);
    }

    // ─── Dropdown helpers ───────────────────────────────────────────────

    private async Task<JToken> GetEnvironmentDropdown()
    {
        var clauses = new JArray
        {
            Where("type", "==", new JArray { "'microsoft.powerplatform/environments'" }),
            Project(new JArray { "id = name", "name = tostring(properties.displayName)" }),
            OrderBy(new JObject { ["name"] = "asc" }),
            Take(500)
        };

        try
        {
            var response = await PostQuery(BuildQuery(clauses)).ConfigureAwait(false);
            var rows = response["data"] as JArray ?? new JArray();
            return rows;
        }
        catch
        {
            return new JArray();
        }
    }

    private JArray GetResourceTypeDropdown()
    {
        var arr = new JArray();
        foreach (var rt in RESOURCE_TYPES)
        {
            arr.Add(new JObject { ["id"] = rt.Id, ["name"] = $"{rt.Name} ({rt.Id})" });
        }
        return arr;
    }

    private async Task<JToken> GetConnectorDropdown()
    {
        try
        {
            var rows = await TopConnectors(500).ConfigureAwait(false) as JObject;
            var arr = new JArray();
            if (rows?["rows"] is JArray rowArr)
            {
                foreach (var row in rowArr)
                {
                    var connectorId = row["connectorId"]?.ToString();
                    if (!string.IsNullOrEmpty(connectorId))
                    {
                        arr.Add(new JObject { ["id"] = connectorId, ["name"] = connectorId });
                    }
                }
            }
            return arr;
        }
        catch
        {
            return new JArray();
        }
    }

    // ─── Typed raw query handler ────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleRunQueryTyped()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return CreateTypedErrorResponse("Request body is required.", 400);

        JObject args;
        try { args = JObject.Parse(body); }
        catch (JsonException ex) { return CreateTypedErrorResponse($"Invalid JSON: {ex.Message}", 400); }

        var result = await RunRawQuery(args).ConfigureAwait(false);
        return CreateTypedResponse(result);
    }

    // ─── Query Builder Helpers ──────────────────────────────────────────

    private JObject BuildQuery(JArray clauses, JObject options = null)
    {
        var query = new JObject
        {
            ["TableName"] = DEFAULT_TABLE,
            ["Clauses"] = clauses
        };
        if (options != null) query["Options"] = options;
        return query;
    }

    private JObject Where(string fieldName, string op, JArray values)
    {
        return new JObject
        {
            ["$type"] = "where",
            ["FieldName"] = fieldName,
            ["Operator"] = op,
            ["Values"] = values
        };
    }

    private JObject Project(JArray fields)
    {
        return new JObject
        {
            ["$type"] = "project",
            ["FieldList"] = fields
        };
    }

    private JObject Take(int count)
    {
        return new JObject
        {
            ["$type"] = "take",
            ["TakeCount"] = count
        };
    }

    private JObject OrderBy(JObject fields)
    {
        return new JObject
        {
            ["$type"] = "orderby",
            ["FieldNamesAscDesc"] = fields
        };
    }

    private JObject Extend(string fieldName, string expression)
    {
        return new JObject
        {
            ["$type"] = "extend",
            ["FieldName"] = fieldName,
            ["Expression"] = expression
        };
    }

    private JObject SummarizeCount(string outputFieldName, JArray groupByFields)
    {
        return new JObject
        {
            ["$type"] = "summarize",
            ["SummarizeClauseExpression"] = new JObject
            {
                ["OperatorName"] = "count",
                ["OperatorFieldName"] = outputFieldName,
                ["FieldList"] = groupByFields
            }
        };
    }

    private JArray ConnectorResourceTypeValues()
    {
        var arr = new JArray();
        foreach (var t in CONNECTOR_RESOURCE_TYPES)
        {
            arr.Add($"'{t}'");
        }
        return arr;
    }

    private JObject WrapRows(JObject response)
    {
        var rows = response["data"] as JArray ?? new JArray();
        return new JObject
        {
            ["rowCount"] = rows.Count,
            ["rows"] = rows
        };
    }

    // ─── HTTP Call to Inventory API ─────────────────────────────────────

    private async Task<JObject> PostQuery(JObject query)
    {
        var url = $"{INVENTORY_API_BASE}{INVENTORY_QUERY_PATH}?api-version={INVENTORY_API_VERSION}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        request.Headers.Add("Accept", "application/json");
        request.Content = new StringContent(
            query.ToString(Newtonsoft.Json.Formatting.None),
            Encoding.UTF8,
            "application/json"
        );

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            this.Context.Logger.LogError($"Inventory API error {response.StatusCode}: {body}");
            throw new InvalidOperationException(
                $"Power Platform Inventory API returned {(int)response.StatusCode} {response.ReasonPhrase}: {TruncateForError(body)}"
            );
        }

        return JObject.Parse(body);
    }

    // ─── Query/Argument Helpers ─────────────────────────────────────────

    private string GetStringQuery(string name)
    {
        var query = System.Web.HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        return query[name];
    }

    private int GetIntQuery(string name, int defaultValue)
    {
        var raw = GetStringQuery(name);
        if (int.TryParse(raw, out var value)) return value;
        return defaultValue;
    }

    private string GetString(JObject args, string name)
    {
        return args[name]?.ToString();
    }

    private int GetInt(JObject args, string name, int defaultValue)
    {
        var token = args[name];
        if (token == null) return defaultValue;
        if (token.Type == JTokenType.Integer) return token.Value<int>();
        if (int.TryParse(token.ToString(), out var value)) return value;
        return defaultValue;
    }

    // ─── Typed Response Helpers ─────────────────────────────────────────

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

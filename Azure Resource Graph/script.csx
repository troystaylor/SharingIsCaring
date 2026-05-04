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
    // Application Insights telemetry (hardcoded per repo standard)
    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";
    private const string CONNECTOR_NAME = "AzureResourceGraph";
    private const string ARG_API_VERSION = "2024-04-01";
    private const string ARG_QUERY_ENDPOINT = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources";
    private const string ARM_BASE = "https://management.azure.com";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        switch (Context.OperationId)
        {
            case "InvokeMCP":
                return await HandleMcpAsync();
            default:
                return await Context.SendAsync(Context.Request, CancellationToken);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  MCP Protocol Handler
    // ───────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleMcpAsync()
    {
        var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
        await LogToAppInsightsAsync("MCP_Request", new Dictionary<string, string> { ["correlationId"] = correlationId });

        try
        {
            var body = await Context.Request.Content.ReadAsStringAsync();
            var request = JObject.Parse(body);

            var method = request["method"]?.ToString();
            var requestId = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(requestId);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                case "tools/list":
                    return HandleToolsList(requestId);

                case "tools/call":
                    return await HandleToolsCall(@params, requestId, correlationId);

                case "resources/list":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject { ["resources"] = new JArray() });

                case "ping":
                    return CreateJsonRpcSuccessResponse(requestId, new JObject());

                default:
                    return CreateJsonRpcErrorResponse(requestId, -32601, "Method not found", method);
            }
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync("MCP_Error", ex, new Dictionary<string, string> { ["correlationId"] = correlationId });
            return CreateJsonRpcErrorResponse(null, -32603, "Internal error: " + ex.Message);
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  initialize
    // ───────────────────────────────────────────────────────────────

    private HttpResponseMessage HandleInitialize(JToken requestId)
    {
        var result = new JObject
        {
            ["protocolVersion"] = "2025-03-26",
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = "azure-resource-graph-mcp",
                ["version"] = "1.0.0"
            }
        };
        return CreateJsonRpcSuccessResponse(requestId, result);
    }

    // ───────────────────────────────────────────────────────────────
    //  tools/list — 21 tools
    // ───────────────────────────────────────────────────────────────

    private HttpResponseMessage HandleToolsList(JToken requestId)
    {
        var tools = new JArray
        {
            // ── Core Query Tools ──
            CreateTool("query_resources",
                "Execute a Kusto Query Language (KQL) query against Azure Resource Graph. Use this for any custom or complex resource query. Supports pagination and subscription/management group scoping.",
                new JObject
                {
                    ["query"] = JP("string", "The KQL query to execute. Example: Resources | where type =~ 'microsoft.compute/virtualmachines' | project name, location, resourceGroup"),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope the query. If omitted, queries all accessible subscriptions." },
                    ["managementGroups"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Management group names to scope the query." },
                    ["top"] = JP("integer", "Maximum rows to return (1-1000). Default: 100."),
                    ["skip"] = JP("integer", "Number of rows to skip for pagination."),
                    ["skipToken"] = JP("string", "Continuation token from a previous query for next-page retrieval.")
                },
                new[] { "query" }),

            CreateTool("list_tables",
                "List all available Azure Resource Graph tables with descriptions. Use this to discover which tables can be queried (e.g., resources, advisorresources, policyresources, healthresources, etc.).",
                new JObject(),
                new string[0]),

            CreateTool("list_resource_types",
                "List all distinct resource types available in Azure Resource Graph. Returns the full type identifier (e.g., microsoft.compute/virtualmachines). Optionally filter by a prefix.",
                new JObject
                {
                    ["prefix"] = JP("string", "Optional type prefix filter. Example: microsoft.compute"),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            // ── Infrastructure Discovery Tools ──
            CreateTool("list_subscriptions",
                "List all Azure subscriptions accessible to the current user. Returns subscription ID, display name, state, and tags.",
                new JObject(),
                new string[0]),

            CreateTool("list_resource_groups",
                "List Azure resource groups. Optionally filter by subscription ID.",
                new JObject
                {
                    ["subscriptionId"] = JP("string", "Optional subscription ID to scope results."),
                    ["top"] = JP("integer", "Maximum rows to return. Default: 200.")
                },
                new string[0]),

            CreateTool("list_management_groups",
                "List all Azure management groups accessible to the current user.",
                new JObject(),
                new string[0]),

            CreateTool("get_resource_by_id",
                "Look up a specific Azure resource by its full ARM resource ID. Returns all properties for that resource.",
                new JObject
                {
                    ["resourceId"] = JP("string", "The full ARM resource ID. Example: /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/myRG/providers/Microsoft.Compute/virtualMachines/myVM")
                },
                new[] { "resourceId" }),

            // ── Change Tracking Tools ──
            CreateTool("query_resource_changes",
                "Query resource configuration changes from the last 14 days using the resourcechanges table. Optionally filter by resource ID or time range.",
                new JObject
                {
                    ["resourceId"] = JP("string", "Optional ARM resource ID to filter changes for a specific resource."),
                    ["hoursBack"] = JP("integer", "Hours to look back (1-336, default: 24). Max is 336 (14 days)."),
                    ["top"] = JP("integer", "Maximum rows. Default: 50."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            // ── Governance Tools ──
            CreateTool("query_policy_compliance",
                "Query Azure Policy compliance state from the policyresources table. Returns policy compliance for resources.",
                new JObject
                {
                    ["complianceState"] = JP("string", "Filter by compliance state: NonCompliant, Compliant, Unknown, Exempt."),
                    ["policyDefinitionId"] = JP("string", "Filter by policy definition resource ID."),
                    ["resourceType"] = JP("string", "Filter by resource type (e.g., microsoft.compute/virtualmachines)."),
                    ["top"] = JP("integer", "Maximum rows. Default: 100."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            CreateTool("query_advisor_recommendations",
                "Query Azure Advisor recommendations from the advisorresources table. Returns recommendations for cost, security, reliability, operational excellence, and performance.",
                new JObject
                {
                    ["category"] = JP("string", "Filter by category: Cost, Security, HighAvailability, OperationalExcellence, Performance."),
                    ["top"] = JP("integer", "Maximum rows. Default: 100."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            CreateTool("query_security_assessments",
                "Query Microsoft Defender for Cloud security assessments from the securityresources table. Returns assessment status for resources.",
                new JObject
                {
                    ["status"] = JP("string", "Filter by status: Healthy, Unhealthy, NotApplicable."),
                    ["top"] = JP("integer", "Maximum rows. Default: 100."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            CreateTool("query_health_status",
                "Query resource health and availability status from the healthresources table. Returns availability state for resources.",
                new JObject
                {
                    ["availabilityState"] = JP("string", "Filter by state: Available, Unavailable, Degraded, Unknown."),
                    ["resourceType"] = JP("string", "Filter by resource type."),
                    ["top"] = JP("integer", "Maximum rows. Default: 100."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            CreateTool("query_service_health",
                "Query Azure service health events from the servicehealthresources table. Returns active service issues, planned maintenance, and health advisories.",
                new JObject
                {
                    ["eventType"] = JP("string", "Filter by event type: ServiceIssue, PlannedMaintenance, HealthAdvisory, SecurityAdvisory."),
                    ["top"] = JP("integer", "Maximum rows. Default: 50."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            CreateTool("query_role_assignments",
                "Query Azure role assignments from the authorizationresources table. Returns who has what access to which resources.",
                new JObject
                {
                    ["principalId"] = JP("string", "Filter by the principal (user/group/SP) object ID."),
                    ["roleDefinitionId"] = JP("string", "Filter by role definition ID (e.g., the GUID for Contributor)."),
                    ["scope"] = JP("string", "Filter by scope prefix (e.g., /subscriptions/xxx)."),
                    ["top"] = JP("integer", "Maximum rows. Default: 100."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            // ── Convenience Aggregation Tools ──
            CreateTool("summarize_resources",
                "Summarize Azure resources with counts grouped by type, location, subscription, or resource group. Useful for inventory overview.",
                new JObject
                {
                    ["groupBy"] = JP("string", "Field to group by: type, location, subscriptionId, resourceGroup. Default: type."),
                    ["resourceType"] = JP("string", "Optional filter to a specific resource type before summarizing."),
                    ["top"] = JP("integer", "Maximum groups to return. Default: 50."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            CreateTool("count_resources",
                "Count Azure resources, optionally filtered by type, location, tag, or a custom KQL where clause.",
                new JObject
                {
                    ["resourceType"] = JP("string", "Filter by resource type (e.g., microsoft.compute/virtualmachines)."),
                    ["location"] = JP("string", "Filter by Azure region (e.g., eastus)."),
                    ["tagName"] = JP("string", "Filter by tag name."),
                    ["tagValue"] = JP("string", "Filter by tag value (requires tagName)."),
                    ["whereClause"] = JP("string", "Custom KQL where clause (e.g., properties.hardwareProfile.vmSize == 'Standard_DS1_v2')."),
                    ["subscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope." }
                },
                new string[0]),

            // ── Saved Query Tools ──
            CreateTool("list_saved_queries",
                "List saved Resource Graph queries in a subscription.",
                new JObject
                {
                    ["subscriptionId"] = JP("string", "The subscription ID to list saved queries from.")
                },
                new[] { "subscriptionId" }),

            CreateTool("get_saved_query",
                "Get a specific saved Resource Graph query by name.",
                new JObject
                {
                    ["subscriptionId"] = JP("string", "The subscription ID."),
                    ["resourceGroupName"] = JP("string", "The resource group name."),
                    ["resourceName"] = JP("string", "The saved query resource name.")
                },
                new[] { "subscriptionId", "resourceGroupName", "resourceName" }),

            CreateTool("create_saved_query",
                "Create or update a saved Resource Graph query.",
                new JObject
                {
                    ["subscriptionId"] = JP("string", "The subscription ID."),
                    ["resourceGroupName"] = JP("string", "The resource group name."),
                    ["resourceName"] = JP("string", "The name for the saved query."),
                    ["query"] = JP("string", "The KQL query to save."),
                    ["description"] = JP("string", "Description of the query."),
                    ["location"] = JP("string", "Azure region for the resource. Default: global.")
                },
                new[] { "subscriptionId", "resourceGroupName", "resourceName", "query" }),

            CreateTool("delete_saved_query",
                "Delete a saved Resource Graph query.",
                new JObject
                {
                    ["subscriptionId"] = JP("string", "The subscription ID."),
                    ["resourceGroupName"] = JP("string", "The resource group name."),
                    ["resourceName"] = JP("string", "The saved query resource name.")
                },
                new[] { "subscriptionId", "resourceGroupName", "resourceName" }),

            CreateTool("run_saved_query",
                "Retrieve a saved Resource Graph query by name and immediately execute it. Combines get_saved_query and query_resources into one step.",
                new JObject
                {
                    ["subscriptionId"] = JP("string", "The subscription ID."),
                    ["resourceGroupName"] = JP("string", "The resource group name."),
                    ["resourceName"] = JP("string", "The saved query resource name."),
                    ["top"] = JP("integer", "Maximum rows to return. Default: 100."),
                    ["querySubscriptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Subscription IDs to scope the query execution." }
                },
                new[] { "subscriptionId", "resourceGroupName", "resourceName" })
        };

        return CreateJsonRpcSuccessResponse(requestId, new JObject { ["tools"] = tools });
    }

    // ───────────────────────────────────────────────────────────────
    //  tools/call dispatcher
    // ───────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCall(JObject @params, JToken requestId, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        await LogToAppInsightsAsync("MCP_ToolCall", new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["tool"] = toolName ?? "unknown"
        });

        try
        {
            string resultText;
            switch (toolName)
            {
                // Core Query
                case "query_resources": resultText = await HandleQueryResources(arguments); break;
                case "list_tables": resultText = HandleListTables(); break;
                case "list_resource_types": resultText = await HandleListResourceTypes(arguments); break;

                // Infrastructure Discovery
                case "list_subscriptions": resultText = await HandleListSubscriptions(arguments); break;
                case "list_resource_groups": resultText = await HandleListResourceGroups(arguments); break;
                case "list_management_groups": resultText = await HandleListManagementGroups(arguments); break;
                case "get_resource_by_id": resultText = await HandleGetResourceById(arguments); break;

                // Change Tracking
                case "query_resource_changes": resultText = await HandleQueryResourceChanges(arguments); break;

                // Governance
                case "query_policy_compliance": resultText = await HandleQueryPolicyCompliance(arguments); break;
                case "query_advisor_recommendations": resultText = await HandleQueryAdvisorRecommendations(arguments); break;
                case "query_security_assessments": resultText = await HandleQuerySecurityAssessments(arguments); break;
                case "query_health_status": resultText = await HandleQueryHealthStatus(arguments); break;
                case "query_service_health": resultText = await HandleQueryServiceHealth(arguments); break;
                case "query_role_assignments": resultText = await HandleQueryRoleAssignments(arguments); break;

                // Convenience Aggregation
                case "summarize_resources": resultText = await HandleSummarizeResources(arguments); break;
                case "count_resources": resultText = await HandleCountResources(arguments); break;

                // Saved Queries
                case "list_saved_queries": resultText = await HandleListSavedQueries(arguments); break;
                case "get_saved_query": resultText = await HandleGetSavedQuery(arguments); break;
                case "create_saved_query": resultText = await HandleCreateSavedQuery(arguments); break;
                case "delete_saved_query": resultText = await HandleDeleteSavedQuery(arguments); break;
                case "run_saved_query": resultText = await HandleRunSavedQuery(arguments); break;

                default:
                    return CreateToolErrorResult(requestId, $"Unknown tool: {toolName}");
            }

            return CreateToolSuccessResult(requestId, resultText);
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsightsAsync("MCP_ToolError", ex, new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["tool"] = toolName ?? "unknown"
            });
            return CreateToolErrorResult(requestId, $"Tool execution failed: {ex.Message}");
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  CORE QUERY TOOLS
    // ───────────────────────────────────────────────────────────────

    private async Task<string> HandleQueryResources(JObject args)
    {
        var query = args["query"]?.ToString();
        if (string.IsNullOrEmpty(query))
            throw new ArgumentException("The 'query' parameter is required.");

        var body = new JObject { ["query"] = query };

        var subs = args["subscriptions"] as JArray;
        if (subs != null && subs.Count > 0) body["subscriptions"] = subs;

        var mgs = args["managementGroups"] as JArray;
        if (mgs != null && mgs.Count > 0) body["managementGroups"] = mgs;

        var options = new JObject();
        if (args["top"] != null) options["$top"] = (int)args["top"];
        if (args["skip"] != null) options["$skip"] = (int)args["skip"];
        if (args["skipToken"] != null) options["$skipToken"] = args["skipToken"].ToString();
        options["resultFormat"] = "objectArray";
        if (options.Properties().Any()) body["options"] = options;

        return await ForwardToResourceGraph(body);
    }

    private string HandleListTables()
    {
        var tables = new JArray
        {
            TI("resources", "All Azure resources with their properties, tags, and metadata."),
            TI("resourcecontainers", "Subscriptions, resource groups, and management groups."),
            TI("advisorresources", "Azure Advisor recommendations for cost, security, reliability, performance."),
            TI("alertsmanagementresources", "Azure Monitor alert instances."),
            TI("authorizationresources", "Role assignments, role definitions, and classic administrators."),
            TI("computeresources", "VM scale set instances and network interfaces."),
            TI("desktopvirtualizationresources", "Azure Virtual Desktop session hosts."),
            TI("dnsresources", "DNS and Private DNS zone records."),
            TI("extendedlocationresources", "Azure Arc custom location enabled resource types."),
            TI("guestconfigurationresources", "Guest configuration assignments and compliance."),
            TI("healthresources", "Resource health and availability status."),
            TI("healthresourcechanges", "Resource health configuration changes."),
            TI("iotsecurityresources", "IoT security alerts, devices, recommendations, sensors."),
            TI("kubernetesconfigurationresources", "Arc Kubernetes extensions, flux configs."),
            TI("maintenanceresources", "Maintenance configurations and scheduled events."),
            TI("networkresources", "Network manager configurations, security perimeters."),
            TI("patchassessmentresources", "Patch assessment results for VMs and Arc machines."),
            TI("patchinstallationresources", "Patch installation results for VMs and Arc machines."),
            TI("policyresources", "Policy assignments, definitions, compliance states."),
            TI("recoveryservicesresources", "Backup items, jobs, policies, site recovery."),
            TI("resourcechanges", "Resource configuration changes (last 14 days)."),
            TI("resourcecontainerchanges", "Subscription and resource group changes."),
            TI("securityresources", "Security assessments, secure score, alerts, compliance."),
            TI("servicefabricresources", "Service Fabric clusters, applications, services."),
            TI("servicehealthresources", "Service health events, planned maintenance."),
            TI("sportresources", "Spot VM eviction rates, pricing history."),
            TI("tagresources", "Resource tags and tag namespaces."),
            TI("aksresources", "AKS fleet members, update runs, upgrade profiles."),
            TI("appserviceresources", "App Service and slot configurations."),
            TI("batchresources", "Batch account pools."),
            TI("chaosresources", "Chaos experiment executions and targets."),
            TI("deploymentresources", "Deployment stacks."),
            TI("edgeorderresources", "Edge order items."),
            TI("elasticsanresources", "Elastic SAN resources."),
            TI("featureresources", "Feature registrations and configurations."),
            TI("insightresources", "Data collection rule associations, tenant action groups."),
            TI("kustoresources", "Kusto (Data Explorer) data connections."),
            TI("managedserviceresources", "Lighthouse registration assignments and definitions."),
            TI("mirgateresources", "Azure Migrate assessments, projects, discovered machines."),
            TI("networkresourcechanges", "Network resource configuration changes."),
            TI("orbitalresources", "Spacecraft contacts."),
            TI("quotaresourcechanges", "Quota resource changes."),
            TI("impactreportresources", "Impact connectors and workload impacts.")
        };

        return new JObject { ["tables"] = tables }.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    private async Task<string> HandleListResourceTypes(JObject args)
    {
        var query = "Resources | distinct type | order by type asc";
        var prefix = args["prefix"]?.ToString();
        if (!string.IsNullOrEmpty(prefix))
        {
            query = $"Resources | distinct type | where type startswith '{EscapeKql(prefix)}' | order by type asc";
        }
        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, 1000);
    }

    // ───────────────────────────────────────────────────────────────
    //  INFRASTRUCTURE DISCOVERY TOOLS
    // ───────────────────────────────────────────────────────────────

    private async Task<string> HandleListSubscriptions(JObject args)
    {
        var query = "resourcecontainers | where type == 'microsoft.resources/subscriptions' | project subscriptionId, name, properties.state, tags | order by name asc";
        return await ExecuteKqlQuery(query, null, 1000);
    }

    private async Task<string> HandleListResourceGroups(JObject args)
    {
        var query = "resourcecontainers | where type == 'microsoft.resources/subscriptions/resourcegroups'";
        var subId = args["subscriptionId"]?.ToString();
        if (!string.IsNullOrEmpty(subId))
        {
            query += $" | where subscriptionId == '{EscapeKql(subId)}'";
        }
        var top = args["top"] != null ? (int)args["top"] : 200;
        query += " | project name, resourceGroup, subscriptionId, location, tags | order by name asc";
        return await ExecuteKqlQuery(query, null, top);
    }

    private async Task<string> HandleListManagementGroups(JObject args)
    {
        var query = "resourcecontainers | where type == 'microsoft.management/managementgroups' | project name, properties.displayName, properties.details.parent.id | order by name asc";
        return await ExecuteKqlQuery(query, null, 1000);
    }

    private async Task<string> HandleGetResourceById(JObject args)
    {
        var resourceId = args["resourceId"]?.ToString();
        if (string.IsNullOrEmpty(resourceId))
            throw new ArgumentException("The 'resourceId' parameter is required.");

        var query = $"Resources | where id =~ '{EscapeKql(resourceId)}'";
        return await ExecuteKqlQuery(query, null, 1);
    }

    // ───────────────────────────────────────────────────────────────
    //  CHANGE TRACKING TOOLS
    // ───────────────────────────────────────────────────────────────

    private async Task<string> HandleQueryResourceChanges(JObject args)
    {
        var hoursBack = args["hoursBack"] != null ? Math.Min((int)args["hoursBack"], 336) : 24;
        var top = args["top"] != null ? (int)args["top"] : 50;

        var query = $"resourcechanges | where properties.changeAttributes.timestamp > ago({hoursBack}h)";

        var resourceId = args["resourceId"]?.ToString();
        if (!string.IsNullOrEmpty(resourceId))
        {
            query += $" | where properties.targetResourceId =~ '{EscapeKql(resourceId)}'";
        }

        query += " | project properties.changeAttributes.timestamp, properties.targetResourceId, properties.targetResourceType, properties.changeType, properties.changes | order by properties_changeAttributes_timestamp desc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    // ───────────────────────────────────────────────────────────────
    //  GOVERNANCE TOOLS
    // ───────────────────────────────────────────────────────────────

    private async Task<string> HandleQueryPolicyCompliance(JObject args)
    {
        var top = args["top"] != null ? (int)args["top"] : 100;
        var query = "policyresources | where type == 'microsoft.policyinsights/policystates' | where properties.complianceState != ''";

        var state = args["complianceState"]?.ToString();
        if (!string.IsNullOrEmpty(state))
            query += $" | where properties.complianceState =~ '{EscapeKql(state)}'";

        var policyDef = args["policyDefinitionId"]?.ToString();
        if (!string.IsNullOrEmpty(policyDef))
            query += $" | where properties.policyDefinitionId =~ '{EscapeKql(policyDef)}'";

        var resType = args["resourceType"]?.ToString();
        if (!string.IsNullOrEmpty(resType))
            query += $" | where properties.resourceType =~ '{EscapeKql(resType)}'";

        query += " | project properties.resourceId, properties.complianceState, properties.policyDefinitionName, properties.policyAssignmentName, properties.resourceType | order by properties_complianceState asc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    private async Task<string> HandleQueryAdvisorRecommendations(JObject args)
    {
        var top = args["top"] != null ? (int)args["top"] : 100;
        var query = "advisorresources | where type == 'microsoft.advisor/recommendations'";

        var category = args["category"]?.ToString();
        if (!string.IsNullOrEmpty(category))
            query += $" | where properties.category =~ '{EscapeKql(category)}'";

        query += " | project id, name, properties.category, properties.impact, properties.impactedField, properties.impactedValue, properties.shortDescription.solution | order by properties_category asc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    private async Task<string> HandleQuerySecurityAssessments(JObject args)
    {
        var top = args["top"] != null ? (int)args["top"] : 100;
        var query = "securityresources | where type == 'microsoft.security/assessments'";

        var status = args["status"]?.ToString();
        if (!string.IsNullOrEmpty(status))
            query += $" | where properties.status.code =~ '{EscapeKql(status)}'";

        query += " | project id, name, properties.displayName, properties.status.code, properties.resourceDetails.Id, properties.metadata.severity | order by properties_status_code asc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    private async Task<string> HandleQueryHealthStatus(JObject args)
    {
        var top = args["top"] != null ? (int)args["top"] : 100;
        var query = "healthresources | where type == 'microsoft.resourcehealth/availabilitystatuses'";

        var state = args["availabilityState"]?.ToString();
        if (!string.IsNullOrEmpty(state))
            query += $" | where properties.availabilityState =~ '{EscapeKql(state)}'";

        var resType = args["resourceType"]?.ToString();
        if (!string.IsNullOrEmpty(resType))
            query += $" | where properties.targetResourceType =~ '{EscapeKql(resType)}'";

        query += " | project id, name, properties.availabilityState, properties.targetResourceType, properties.targetResourceId, properties.occurredTime | order by properties_availabilityState asc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    private async Task<string> HandleQueryServiceHealth(JObject args)
    {
        var top = args["top"] != null ? (int)args["top"] : 50;
        var query = "servicehealthresources | where type == 'microsoft.resourcehealth/events' | where properties.status =~ 'Active'";

        var eventType = args["eventType"]?.ToString();
        if (!string.IsNullOrEmpty(eventType))
            query += $" | where properties.eventType =~ '{EscapeKql(eventType)}'";

        query += " | project id, name, properties.eventType, properties.title, properties.status, properties.impactStartTime, properties.impactedServices | order by properties_impactStartTime desc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    private async Task<string> HandleQueryRoleAssignments(JObject args)
    {
        var top = args["top"] != null ? (int)args["top"] : 100;
        var query = "authorizationresources | where type == 'microsoft.authorization/roleassignments'";

        var principalId = args["principalId"]?.ToString();
        if (!string.IsNullOrEmpty(principalId))
            query += $" | where properties.principalId =~ '{EscapeKql(principalId)}'";

        var roleDef = args["roleDefinitionId"]?.ToString();
        if (!string.IsNullOrEmpty(roleDef))
            query += $" | where properties.roleDefinitionId contains '{EscapeKql(roleDef)}'";

        var scope = args["scope"]?.ToString();
        if (!string.IsNullOrEmpty(scope))
            query += $" | where properties.scope startswith '{EscapeKql(scope)}'";

        query += " | project id, properties.principalId, properties.principalType, properties.roleDefinitionId, properties.scope, properties.createdOn | order by properties_createdOn desc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    // ───────────────────────────────────────────────────────────────
    //  CONVENIENCE AGGREGATION TOOLS
    // ───────────────────────────────────────────────────────────────

    private async Task<string> HandleSummarizeResources(JObject args)
    {
        var groupBy = args["groupBy"]?.ToString() ?? "type";
        var top = args["top"] != null ? (int)args["top"] : 50;

        // Validate groupBy to prevent injection
        var validFields = new HashSet<string> { "type", "location", "subscriptionId", "resourceGroup" };
        if (!validFields.Contains(groupBy))
            throw new ArgumentException($"Invalid groupBy value. Must be one of: {string.Join(", ", validFields)}");

        var query = "Resources";
        var resType = args["resourceType"]?.ToString();
        if (!string.IsNullOrEmpty(resType))
            query += $" | where type =~ '{EscapeKql(resType)}'";

        query += $" | summarize count() by {groupBy} | order by count_ desc";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, top);
    }

    private async Task<string> HandleCountResources(JObject args)
    {
        var query = "Resources";
        var filters = new List<string>();

        var resType = args["resourceType"]?.ToString();
        if (!string.IsNullOrEmpty(resType))
            filters.Add($"type =~ '{EscapeKql(resType)}'");

        var location = args["location"]?.ToString();
        if (!string.IsNullOrEmpty(location))
            filters.Add($"location =~ '{EscapeKql(location)}'");

        var tagName = args["tagName"]?.ToString();
        var tagValue = args["tagValue"]?.ToString();
        if (!string.IsNullOrEmpty(tagName))
        {
            if (!string.IsNullOrEmpty(tagValue))
                filters.Add($"tags['{EscapeKql(tagName)}'] =~ '{EscapeKql(tagValue)}'");
            else
                filters.Add($"isnotnull(tags['{EscapeKql(tagName)}'])");
        }

        var whereClause = args["whereClause"]?.ToString();
        if (!string.IsNullOrEmpty(whereClause))
            filters.Add(whereClause);

        if (filters.Count > 0)
            query += " | where " + string.Join(" and ", filters);

        query += " | summarize totalCount = count()";

        return await ExecuteKqlQuery(query, args["subscriptions"] as JArray, 1);
    }

    // ───────────────────────────────────────────────────────────────
    //  SAVED QUERY TOOLS
    // ───────────────────────────────────────────────────────────────

    private async Task<string> HandleListSavedQueries(JObject args)
    {
        var subId = args["subscriptionId"]?.ToString();
        if (string.IsNullOrEmpty(subId))
            throw new ArgumentException("The 'subscriptionId' parameter is required.");

        var url = $"{ARM_BASE}/subscriptions/{Uri.EscapeDataString(subId)}/providers/Microsoft.ResourceGraph/queries?api-version={ARG_API_VERSION}";
        return await SendArmRequest(HttpMethod.Get, url);
    }

    private async Task<string> HandleGetSavedQuery(JObject args)
    {
        var url = BuildSavedQueryUrl(args);
        return await SendArmRequest(HttpMethod.Get, url);
    }

    private async Task<string> HandleCreateSavedQuery(JObject args)
    {
        var url = BuildSavedQueryUrl(args);
        var queryText = args["query"]?.ToString();
        if (string.IsNullOrEmpty(queryText))
            throw new ArgumentException("The 'query' parameter is required.");

        var body = new JObject
        {
            ["location"] = args["location"]?.ToString() ?? "global",
            ["properties"] = new JObject
            {
                ["query"] = queryText,
                ["description"] = args["description"]?.ToString() ?? ""
            }
        };

        return await SendArmRequest(HttpMethod.Put, url, body.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<string> HandleDeleteSavedQuery(JObject args)
    {
        var url = BuildSavedQueryUrl(args);
        return await SendArmRequest(HttpMethod.Delete, url);
    }

    private async Task<string> HandleRunSavedQuery(JObject args)
    {
        // Step 1: Get the saved query
        var url = BuildSavedQueryUrl(args);
        var savedJson = await SendArmRequest(HttpMethod.Get, url);
        var saved = JObject.Parse(savedJson);
        var kql = saved["properties"]?["query"]?.ToString();
        if (string.IsNullOrEmpty(kql))
            throw new InvalidOperationException("Saved query has no KQL query text.");

        // Step 2: Execute it
        var top = args["top"] != null ? (int)args["top"] : 100;
        return await ExecuteKqlQuery(kql, args["querySubscriptions"] as JArray, top);
    }

    // ───────────────────────────────────────────────────────────────
    //  HELPERS
    // ───────────────────────────────────────────────────────────────

    private string BuildSavedQueryUrl(JObject args)
    {
        var subId = args["subscriptionId"]?.ToString();
        var rg = args["resourceGroupName"]?.ToString();
        var name = args["resourceName"]?.ToString();
        if (string.IsNullOrEmpty(subId) || string.IsNullOrEmpty(rg) || string.IsNullOrEmpty(name))
            throw new ArgumentException("subscriptionId, resourceGroupName, and resourceName are all required.");

        return $"{ARM_BASE}/subscriptions/{Uri.EscapeDataString(subId)}/resourceGroups/{Uri.EscapeDataString(rg)}/providers/Microsoft.ResourceGraph/queries/{Uri.EscapeDataString(name)}?api-version={ARG_API_VERSION}";
    }

    private async Task<string> ExecuteKqlQuery(string query, JArray subscriptions, int top)
    {
        var body = new JObject
        {
            ["query"] = query,
            ["options"] = new JObject
            {
                ["$top"] = Math.Min(top, 1000),
                ["resultFormat"] = "objectArray"
            }
        };
        if (subscriptions != null && subscriptions.Count > 0)
            body["subscriptions"] = subscriptions;

        return await ForwardToResourceGraph(body);
    }

    private async Task<string> ForwardToResourceGraph(JObject requestBody)
    {
        var url = $"{ARG_QUERY_ENDPOINT}?api-version={ARG_API_VERSION}";
        return await SendArmRequest(HttpMethod.Post, url, requestBody.ToString(Newtonsoft.Json.Formatting.None));
    }

    private async Task<string> SendArmRequest(HttpMethod method, string url, string body = null)
    {
        using (var request = new HttpRequestMessage(method, url))
        {
            // Forward the Authorization header from the connector
            if (Context.Request.Headers.Authorization != null)
            {
                request.Headers.Authorization = Context.Request.Headers.Authorization;
            }

            if (body != null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await this.Context.SendAsync(request, CancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"ARM API returned {(int)response.StatusCode}: {responseBody}");
            }

            return responseBody;
        }
    }

    // ───────────────────────────────────────────────────────────────
    //  JSON-RPC helpers
    // ───────────────────────────────────────────────────────────────

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
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;

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

    private HttpResponseMessage CreateToolSuccessResult(JToken requestId, string text)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = false
        });
    }

    private HttpResponseMessage CreateToolErrorResult(JToken requestId, string errorMessage)
    {
        return CreateJsonRpcSuccessResponse(requestId, new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = errorMessage
                }
            },
            ["isError"] = true
        });
    }

    // ───────────────────────────────────────────────────────────────
    //  Tool definition helpers
    // ───────────────────────────────────────────────────────────────

    private JObject CreateTool(string name, string description, JObject properties, string[] required)
    {
        var tool = new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties
            }
        };
        if (required != null && required.Length > 0)
            tool["inputSchema"]["required"] = new JArray(required);
        return tool;
    }

    /// <summary>JSON property shorthand</summary>
    private JObject JP(string type, string description)
    {
        return new JObject { ["type"] = type, ["description"] = description };
    }

    /// <summary>Table info shorthand</summary>
    private JObject TI(string name, string description)
    {
        return new JObject { ["name"] = name, ["description"] = description };
    }

    /// <summary>Escape single quotes in KQL string literals to prevent injection.</summary>
    private string EscapeKql(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("'", "\\'");
    }

    // ───────────────────────────────────────────────────────────────
    //  Application Insights telemetry (hardcoded per repo standard)
    // ───────────────────────────────────────────────────────────────

    private async Task LogToAppInsightsAsync(string eventName, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        try
        {
            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = properties != null
                            ? JObject.FromObject(properties)
                            : new JObject()
                    }
                }
            };
            ((JObject)telemetryData["data"]["baseData"]["properties"])["connector"] = CONNECTOR_NAME;

            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                request.Content = new StringContent(
                    telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json"
                );
                await this.Context.SendAsync(request, CancellationToken);
            }
        }
        catch { /* Silent fail for telemetry */ }
    }

    private async Task LogExceptionToAppInsightsAsync(string eventName, Exception ex, IDictionary<string, string> properties = null)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        try
        {
            var props = properties != null
                ? new Dictionary<string, string>(properties)
                : new Dictionary<string, string>();
            props["exceptionType"] = ex.GetType().Name;
            props["exceptionMessage"] = ex.Message;
            props["connector"] = CONNECTOR_NAME;

            var telemetryData = new JObject
            {
                ["name"] = "Microsoft.ApplicationInsights.Exception",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = APP_INSIGHTS_KEY,
                ["data"] = new JObject
                {
                    ["baseType"] = "ExceptionData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["exceptions"] = new JArray
                        {
                            new JObject
                            {
                                ["typeName"] = ex.GetType().FullName,
                                ["message"] = ex.Message,
                                ["hasFullStack"] = false
                            }
                        },
                        ["properties"] = JObject.FromObject(props)
                    }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                request.Content = new StringContent(
                    telemetryData.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json"
                );
                await this.Context.SendAsync(request, CancellationToken);
            }
        }
        catch { /* Silent fail for telemetry */ }
    }
}
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Azure Cost Management MCP Server for Copilot Studio
/// Implements Model Context Protocol for cost analysis, budgets, forecasts, and dimensions
/// </summary>
public class Script : ScriptBase
{
    // ========================================
    // MCP SERVER CONFIGURATION
    // ========================================
    private const string SERVER_NAME = "azure-cost-management";
    private const string SERVER_VERSION = "1.0.0";
    private const string DEFAULT_PROTOCOL_VERSION = "2025-03-26";
    
    // Application Insights Configuration - set your connection string
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    /// <summary>
    /// Main entry point - routes to MCP or standard REST handling
    /// </summary>
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var operationId = this.Context.OperationId;
        
        try
        {
            // Route MCP requests based on OperationId from swagger
            if (operationId == "McpServer")
            {
                var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return await HandleMCPRequestAsync(correlationId, body).ConfigureAwait(false);
            }
            
            // Standard REST API handling
            return await HandleRestRequestAsync(correlationId, operationId, startTime).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("CostManagement_RequestError", new
            {
                CorrelationId = correlationId,
                OperationId = operationId,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name,
                DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            });
            
            throw;
        }
    }

    /// <summary>
    /// Handle standard REST API requests (non-MCP)
    /// </summary>
    private async Task<HttpResponseMessage> HandleRestRequestAsync(string correlationId, string operationId, DateTime startTime)
    {
        await LogToAppInsights("CostManagement_RequestReceived", new
        {
            CorrelationId = correlationId,
            OperationId = operationId,
            Path = this.Context.Request.RequestUri.AbsolutePath,
            Method = this.Context.Request.Method.Method
        });
        
        var response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
        
        if (response.IsSuccessStatusCode && response.Content != null)
        {
            var responseContent = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(continueOnCapturedContext: false);
            
            if (!string.IsNullOrEmpty(responseContent))
            {
                try
                {
                    var jsonContent = JObject.Parse(responseContent);
                    
                    if (operationId.Contains("Query") || operationId.Contains("Forecast"))
                    {
                        jsonContent = TransformQueryResults(jsonContent);
                        var rowCount = jsonContent["properties"]?["rows"]?.Count() ?? 0;
                        await LogToAppInsights("CostManagement_QueryProcessed", new
                        {
                            CorrelationId = correlationId,
                            OperationId = operationId,
                            RowCount = rowCount
                        });
                    }
                    
                    if (operationId.Contains("Budget"))
                    {
                        jsonContent = TransformBudgetResults(jsonContent);
                        await LogToAppInsights("CostManagement_BudgetProcessed", new
                        {
                            CorrelationId = correlationId,
                            OperationId = operationId
                        });
                    }
                    
                    response.Content = CreateJsonContent(jsonContent.ToString());
                }
                catch (JsonException) { }
            }
        }
        
        await LogToAppInsights("CostManagement_RequestCompleted", new
        {
            CorrelationId = correlationId,
            OperationId = operationId,
            StatusCode = (int)response.StatusCode,
            DurationMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
            Success = response.IsSuccessStatusCode
        });
        
        return response;
    }

    // ========================================
    // MCP PROTOCOL HANDLERS
    // ========================================

    /// <summary>
    /// Handle MCP JSON-RPC requests
    /// </summary>
    private async Task<HttpResponseMessage> HandleMCPRequestAsync(string correlationId, string body)
    {
        try
        {
            var request = JObject.Parse(body);
            if (!request.ContainsKey("jsonrpc")) request["jsonrpc"] = "2.0";

            var method = request["method"]?.ToString();
            var id = request["id"];
            var @params = request["params"] as JObject;

            await LogToAppInsights("MCPMethod", new { CorrelationId = correlationId, Method = method });

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(@params, id);
                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return CreateMCPSuccess(new JObject(), id);
                case "ping":
                    return CreateMCPSuccess(new JObject(), id);
                case "tools/list":
                    return HandleToolsList(id);
                case "tools/call":
                    return await HandleToolsCallAsync(@params, id, correlationId).ConfigureAwait(false);
                case "resources/list":
                    return CreateMCPSuccess(new JObject { ["resources"] = new JArray() }, id);
                case "resources/templates/list":
                    return CreateMCPSuccess(new JObject { ["resourceTemplates"] = new JArray() }, id);
                case "prompts/list":
                    return CreateMCPSuccess(new JObject { ["prompts"] = new JArray() }, id);
                case "completion/complete":
                    return CreateMCPSuccess(new JObject 
                    { 
                        ["completion"] = new JObject { ["values"] = new JArray(), ["total"] = 0, ["hasMore"] = false } 
                    }, id);
                case "logging/setLevel":
                    return CreateMCPSuccess(new JObject(), id);
                default:
                    await LogToAppInsights("MCPUnknownMethod", new { CorrelationId = correlationId, Method = method ?? "null" });
                    return CreateMCPError(id, -32601, "Method not found", method ?? "");
            }
        }
        catch (JsonException ex)
        {
            await LogToAppInsights("MCPParseError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateMCPError(null, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPError", new { CorrelationId = correlationId, Error = ex.Message });
            return CreateMCPError(null, -32603, "Internal error", ex.Message);
        }
    }

    private HttpResponseMessage HandleInitialize(JObject @params, JToken id)
    {
        var protocolVersion = @params?["protocolVersion"]?.ToString() ?? DEFAULT_PROTOCOL_VERSION;
        var result = new JObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
                ["logging"] = new JObject()
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION,
                ["title"] = "Azure Cost Management MCP",
                ["description"] = "Query Azure costs, manage budgets, get forecasts, analyze spending by dimensions across subscriptions, resource groups, management groups, and billing accounts."
            }
        };
        return CreateMCPSuccess(result, id);
    }

    private HttpResponseMessage HandleToolsList(JToken id)
    {
        return CreateMCPSuccess(new JObject { ["tools"] = GetToolDefinitions() }, id);
    }

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JObject @params, JToken id, string correlationId)
    {
        var toolName = @params?["name"]?.ToString();
        var args = @params?["arguments"] as JObject ?? new JObject();
        
        if (string.IsNullOrWhiteSpace(toolName))
            return CreateMCPError(id, -32602, "Tool name required", "name parameter is required");

        await LogToAppInsights("MCPToolCall", new { CorrelationId = correlationId, Tool = toolName });

        try
        {
            var result = await ExecuteToolAsync(toolName, args).ConfigureAwait(false);
            return CreateMCPSuccess(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented) } },
                ["isError"] = false
            }, id);
        }
        catch (ArgumentException ex)
        {
            return CreateMCPSuccess(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
        catch (Exception ex)
        {
            await LogToAppInsights("MCPToolError", new { CorrelationId = correlationId, Tool = toolName, Error = ex.Message });
            return CreateMCPSuccess(new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" } },
                ["isError"] = true
            }, id);
        }
    }

    // ========================================
    // MCP TOOL DEFINITIONS
    // ========================================

    private JArray GetToolDefinitions()
    {
        return new JArray
        {
            // Cost Query Tools
            new JObject
            {
                ["name"] = "query_subscription_costs",
                ["description"] = "Query cost data for an Azure subscription. Returns cost analysis grouped by service, resource group, or custom dimensions. Supports date ranges and granularity (daily/monthly).",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID (GUID)" },
                        ["queryType"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Usage", "ActualCost", "AmortizedCost" }, ["description"] = "Type of cost data to query" },
                        ["timeframe"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "MonthToDate", "BillingMonthToDate", "TheLastMonth", "WeekToDate", "Custom" }, ["description"] = "Time period for the query" },
                        ["fromDate"] = new JObject { ["type"] = "string", ["description"] = "Start date for Custom timeframe (ISO 8601)" },
                        ["toDate"] = new JObject { ["type"] = "string", ["description"] = "End date for Custom timeframe (ISO 8601)" },
                        ["granularity"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Monthly" }, ["description"] = "Granularity of results" },
                        ["groupBy"] = new JObject { ["type"] = "string", ["description"] = "Dimension to group by: ServiceName, ResourceGroup, ResourceType, Meter, etc." }
                    },
                    ["required"] = new JArray { "subscriptionId", "queryType", "timeframe" }
                }
            },
            new JObject
            {
                ["name"] = "query_resource_group_costs",
                ["description"] = "Query cost data for a specific resource group within a subscription.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["resourceGroupName"] = new JObject { ["type"] = "string", ["description"] = "Name of the resource group" },
                        ["queryType"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Usage", "ActualCost", "AmortizedCost" } },
                        ["timeframe"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "MonthToDate", "BillingMonthToDate", "TheLastMonth", "WeekToDate", "Custom" } },
                        ["fromDate"] = new JObject { ["type"] = "string", ["description"] = "Start date for Custom timeframe" },
                        ["toDate"] = new JObject { ["type"] = "string", ["description"] = "End date for Custom timeframe" },
                        ["granularity"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Monthly" } }
                    },
                    ["required"] = new JArray { "subscriptionId", "resourceGroupName", "queryType", "timeframe" }
                }
            },
            // Forecast Tools
            new JObject
            {
                ["name"] = "get_subscription_forecast",
                ["description"] = "Get cost forecast predictions for a subscription. Returns projected costs based on historical spending patterns.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["forecastType"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Usage", "ActualCost", "AmortizedCost" } },
                        ["fromDate"] = new JObject { ["type"] = "string", ["description"] = "Forecast start date (ISO 8601)" },
                        ["toDate"] = new JObject { ["type"] = "string", ["description"] = "Forecast end date (ISO 8601)" },
                        ["granularity"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Monthly" } },
                        ["includeActualCost"] = new JObject { ["type"] = "boolean", ["description"] = "Include actual costs alongside forecast" }
                    },
                    ["required"] = new JArray { "subscriptionId", "forecastType", "fromDate", "toDate" }
                }
            },
            // Budget Tools
            new JObject
            {
                ["name"] = "list_subscription_budgets",
                ["description"] = "List all budgets configured for a subscription. Returns budget names, amounts, time periods, and current spend.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" }
                    },
                    ["required"] = new JArray { "subscriptionId" }
                }
            },
            new JObject
            {
                ["name"] = "get_budget",
                ["description"] = "Get details of a specific budget including current spending, forecast, and notification thresholds.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["budgetName"] = new JObject { ["type"] = "string", ["description"] = "Name of the budget" }
                    },
                    ["required"] = new JArray { "subscriptionId", "budgetName" }
                }
            },
            new JObject
            {
                ["name"] = "create_budget",
                ["description"] = "Create a new cost budget with spending alerts. Configure amount, time grain (monthly/quarterly/annually), and notification thresholds.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["budgetName"] = new JObject { ["type"] = "string", ["description"] = "Name for the budget" },
                        ["amount"] = new JObject { ["type"] = "number", ["description"] = "Budget amount in subscription currency" },
                        ["timeGrain"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Monthly", "Quarterly", "Annually" }, ["description"] = "Budget reset period" },
                        ["startDate"] = new JObject { ["type"] = "string", ["description"] = "Budget start date (ISO 8601)" },
                        ["endDate"] = new JObject { ["type"] = "string", ["description"] = "Optional budget end date" },
                        ["alertThresholds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" }, ["description"] = "Alert percentages (e.g., [50, 80, 100])" },
                        ["alertEmails"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Email addresses for alerts" }
                    },
                    ["required"] = new JArray { "subscriptionId", "budgetName", "amount", "timeGrain", "startDate" }
                }
            },
            // Dimension Tools
            new JObject
            {
                ["name"] = "list_dimensions",
                ["description"] = "List available cost dimensions for filtering and grouping (e.g., ServiceName, ResourceGroup, Meter, Tag keys). Useful for discovering what dimensions are available before querying.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["filter"] = new JObject { ["type"] = "string", ["description"] = "OData filter expression" },
                        ["top"] = new JObject { ["type"] = "integer", ["description"] = "Maximum dimensions to return" }
                    },
                    ["required"] = new JArray { "subscriptionId" }
                }
            },
            // Alert Tools
            new JObject
            {
                ["name"] = "list_cost_alerts",
                ["description"] = "List cost alerts for a subscription including budget alerts, anomaly alerts, and quota alerts.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" }
                    },
                    ["required"] = new JArray { "subscriptionId" }
                }
            },
            // Export Tools
            new JObject
            {
                ["name"] = "list_exports",
                ["description"] = "List scheduled cost data exports configured for a subscription.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" }
                    },
                    ["required"] = new JArray { "subscriptionId" }
                }
            },
            new JObject
            {
                ["name"] = "run_export",
                ["description"] = "Manually trigger a cost data export to run immediately.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["exportName"] = new JObject { ["type"] = "string", ["description"] = "Name of the export to run" }
                    },
                    ["required"] = new JArray { "subscriptionId", "exportName" }
                }
            },
            // Budget Management (update/delete)
            new JObject
            {
                ["name"] = "update_budget",
                ["description"] = "Update an existing budget's amount, time period, or alert thresholds.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["budgetName"] = new JObject { ["type"] = "string", ["description"] = "Name of the budget to update" },
                        ["amount"] = new JObject { ["type"] = "number", ["description"] = "New budget amount" },
                        ["alertThresholds"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" }, ["description"] = "New alert thresholds (e.g., [50, 80, 100])" },
                        ["alertEmails"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "Email addresses for alerts" }
                    },
                    ["required"] = new JArray { "subscriptionId", "budgetName" }
                }
            },
            new JObject
            {
                ["name"] = "delete_budget",
                ["description"] = "Delete a budget from a subscription.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["budgetName"] = new JObject { ["type"] = "string", ["description"] = "Name of the budget to delete" }
                    },
                    ["required"] = new JArray { "subscriptionId", "budgetName" }
                }
            },
            // Alert Management
            new JObject
            {
                ["name"] = "dismiss_alert",
                ["description"] = "Dismiss a cost alert to mark it as resolved.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["alertId"] = new JObject { ["type"] = "string", ["description"] = "ID of the alert to dismiss" }
                    },
                    ["required"] = new JArray { "subscriptionId", "alertId" }
                }
            },
            // Cost Optimization
            new JObject
            {
                ["name"] = "get_benefit_recommendations",
                ["description"] = "Get cost savings recommendations for reservations and savings plans. Helps answer 'How can I reduce Azure costs?'",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["billingAccountId"] = new JObject { ["type"] = "string", ["description"] = "Billing account ID (for EA/MCA)" },
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Subscription ID (use if no billing account)" },
                        ["lookBackPeriod"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Last7Days", "Last30Days", "Last60Days" }, ["description"] = "Period for usage analysis" },
                        ["term"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "P1Y", "P3Y" }, ["description"] = "Commitment term (1 or 3 years)" }
                    },
                    ["required"] = new JArray { }
                }
            },
            new JObject
            {
                ["name"] = "get_benefit_utilization",
                ["description"] = "Get utilization data for reservations and savings plans. Shows if purchased benefits are being used effectively.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["billingAccountId"] = new JObject { ["type"] = "string", ["description"] = "Billing account ID" },
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Subscription ID (alternative scope)" },
                        ["grainFilter"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Monthly" }, ["description"] = "Granularity of results" }
                    },
                    ["required"] = new JArray { }
                }
            },
            // Management Group Query
            new JObject
            {
                ["name"] = "query_management_group_costs",
                ["description"] = "Query cost data across all subscriptions in a management group. Useful for enterprise-wide cost analysis.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["managementGroupId"] = new JObject { ["type"] = "string", ["description"] = "Management group ID" },
                        ["queryType"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Usage", "ActualCost", "AmortizedCost" } },
                        ["timeframe"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "MonthToDate", "BillingMonthToDate", "TheLastMonth", "WeekToDate", "Custom" } },
                        ["fromDate"] = new JObject { ["type"] = "string", ["description"] = "Start date for Custom timeframe" },
                        ["toDate"] = new JObject { ["type"] = "string", ["description"] = "End date for Custom timeframe" },
                        ["granularity"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Monthly" } },
                        ["groupBy"] = new JObject { ["type"] = "string", ["description"] = "Dimension to group by: SubscriptionName, ServiceName, ResourceGroup, etc." }
                    },
                    ["required"] = new JArray { "managementGroupId", "queryType", "timeframe" }
                }
            },
            // Export Management
            new JObject
            {
                ["name"] = "create_export",
                ["description"] = "Create a scheduled cost data export to Azure Storage.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["exportName"] = new JObject { ["type"] = "string", ["description"] = "Name for the export" },
                        ["storageAccountId"] = new JObject { ["type"] = "string", ["description"] = "Full resource ID of the storage account" },
                        ["containerName"] = new JObject { ["type"] = "string", ["description"] = "Blob container name" },
                        ["schedule"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Weekly", "Monthly" }, ["description"] = "Export frequency" },
                        ["exportType"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Usage", "ActualCost", "AmortizedCost" }, ["description"] = "Type of cost data" },
                        ["timeframe"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "MonthToDate", "TheLastMonth", "WeekToDate" } }
                    },
                    ["required"] = new JArray { "subscriptionId", "exportName", "storageAccountId", "containerName", "schedule", "exportType", "timeframe" }
                }
            },
            new JObject
            {
                ["name"] = "delete_export",
                ["description"] = "Delete a scheduled cost data export.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["exportName"] = new JObject { ["type"] = "string", ["description"] = "Name of the export to delete" }
                    },
                    ["required"] = new JArray { "subscriptionId", "exportName" }
                }
            },
            // View Management
            new JObject
            {
                ["name"] = "list_views",
                ["description"] = "List saved cost analysis views for a subscription.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" }
                    },
                    ["required"] = new JArray { "subscriptionId" }
                }
            },
            new JObject
            {
                ["name"] = "create_view",
                ["description"] = "Create a saved cost analysis view with predefined query settings.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["viewName"] = new JObject { ["type"] = "string", ["description"] = "Name for the view" },
                        ["displayName"] = new JObject { ["type"] = "string", ["description"] = "Display name for the view" },
                        ["chartType"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Area", "Line", "StackedColumn", "GroupedColumn", "Table" } },
                        ["timeframe"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "MonthToDate", "TheLastMonth", "WeekToDate" } },
                        ["granularity"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Daily", "Monthly" } },
                        ["groupBy"] = new JObject { ["type"] = "string", ["description"] = "Dimension to group by" }
                    },
                    ["required"] = new JArray { "subscriptionId", "viewName", "displayName", "chartType", "timeframe" }
                }
            },
            // Report Generation
            new JObject
            {
                ["name"] = "generate_cost_report",
                ["description"] = "Generate a detailed cost report asynchronously. Returns an operation ID to check status.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["subscriptionId"] = new JObject { ["type"] = "string", ["description"] = "Azure subscription ID" },
                        ["fromDate"] = new JObject { ["type"] = "string", ["description"] = "Report start date (ISO 8601)" },
                        ["toDate"] = new JObject { ["type"] = "string", ["description"] = "Report end date (ISO 8601)" },
                        ["metric"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "ActualCost", "AmortizedCost" }, ["description"] = "Cost metric type" }
                    },
                    ["required"] = new JArray { "subscriptionId", "fromDate", "toDate" }
                }
            }
        };
    }

    // ========================================
    // MCP TOOL EXECUTION
    // ========================================

    private async Task<JObject> ExecuteToolAsync(string toolName, JObject args)
    {
        switch (toolName)
        {
            case "query_subscription_costs":
                return await ExecuteQueryCostsAsync(args, "subscription").ConfigureAwait(false);
            case "query_resource_group_costs":
                return await ExecuteQueryCostsAsync(args, "resourceGroup").ConfigureAwait(false);
            case "get_subscription_forecast":
                return await ExecuteForecastAsync(args).ConfigureAwait(false);
            case "list_subscription_budgets":
                return await ExecuteListBudgetsAsync(args).ConfigureAwait(false);
            case "get_budget":
                return await ExecuteGetBudgetAsync(args).ConfigureAwait(false);
            case "create_budget":
                return await ExecuteCreateBudgetAsync(args).ConfigureAwait(false);
            case "list_dimensions":
                return await ExecuteListDimensionsAsync(args).ConfigureAwait(false);
            case "list_cost_alerts":
                return await ExecuteListAlertsAsync(args).ConfigureAwait(false);
            case "list_exports":
                return await ExecuteListExportsAsync(args).ConfigureAwait(false);
            case "run_export":
                return await ExecuteRunExportAsync(args).ConfigureAwait(false);
            case "update_budget":
                return await ExecuteUpdateBudgetAsync(args).ConfigureAwait(false);
            case "delete_budget":
                return await ExecuteDeleteBudgetAsync(args).ConfigureAwait(false);
            case "dismiss_alert":
                return await ExecuteDismissAlertAsync(args).ConfigureAwait(false);
            case "get_benefit_recommendations":
                return await ExecuteGetBenefitRecommendationsAsync(args).ConfigureAwait(false);
            case "get_benefit_utilization":
                return await ExecuteGetBenefitUtilizationAsync(args).ConfigureAwait(false);
            case "query_management_group_costs":
                return await ExecuteQueryManagementGroupCostsAsync(args).ConfigureAwait(false);
            case "create_export":
                return await ExecuteCreateExportAsync(args).ConfigureAwait(false);
            case "delete_export":
                return await ExecuteDeleteExportAsync(args).ConfigureAwait(false);
            case "list_views":
                return await ExecuteListViewsAsync(args).ConfigureAwait(false);
            case "create_view":
                return await ExecuteCreateViewAsync(args).ConfigureAwait(false);
            case "generate_cost_report":
                return await ExecuteGenerateCostReportAsync(args).ConfigureAwait(false);
            default:
                throw new ArgumentException($"Unknown tool: {toolName}");
        }
    }

    private async Task<JObject> ExecuteQueryCostsAsync(JObject args, string scope)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var queryType = RequireArg(args, "queryType");
        var timeframe = RequireArg(args, "timeframe");
        var granularity = args["granularity"]?.ToString() ?? "Daily";
        var groupBy = args["groupBy"]?.ToString();
        
        string url;
        if (scope == "resourceGroup")
        {
            var resourceGroupName = RequireArg(args, "resourceGroupName");
            url = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.CostManagement/query?api-version=2025-03-01";
        }
        else
        {
            url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/query?api-version=2025-03-01";
        }
        
        var body = new JObject
        {
            ["type"] = queryType,
            ["timeframe"] = timeframe,
            ["dataset"] = new JObject
            {
                ["granularity"] = granularity,
                ["aggregation"] = new JObject
                {
                    ["totalCost"] = new JObject { ["name"] = "Cost", ["function"] = "Sum" }
                }
            }
        };
        
        if (timeframe == "Custom")
        {
            body["timePeriod"] = new JObject
            {
                ["from"] = RequireArg(args, "fromDate"),
                ["to"] = RequireArg(args, "toDate")
            };
        }
        
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            body["dataset"]["grouping"] = new JArray
            {
                new JObject { ["type"] = "Dimension", ["name"] = groupBy }
            };
        }
        
        var result = await SendCostManagementRequestAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
        return TransformQueryResults(result);
    }

    private async Task<JObject> ExecuteForecastAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var forecastType = RequireArg(args, "forecastType");
        var fromDate = RequireArg(args, "fromDate");
        var toDate = RequireArg(args, "toDate");
        var granularity = args["granularity"]?.ToString() ?? "Daily";
        var includeActual = args["includeActualCost"]?.Value<bool>() ?? false;
        
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/forecast?api-version=2025-03-01";
        
        var body = new JObject
        {
            ["type"] = forecastType,
            ["timeframe"] = "Custom",
            ["timePeriod"] = new JObject { ["from"] = fromDate, ["to"] = toDate },
            ["includeActualCost"] = includeActual,
            ["dataset"] = new JObject
            {
                ["granularity"] = granularity,
                ["aggregation"] = new JObject
                {
                    ["totalCost"] = new JObject { ["name"] = "Cost", ["function"] = "Sum" }
                }
            }
        };
        
        var result = await SendCostManagementRequestAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
        return TransformQueryResults(result);
    }

    private async Task<JObject> ExecuteListBudgetsAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/budgets?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBudgetAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var budgetName = RequireArg(args, "budgetName");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/budgets/{budgetName}?api-version=2025-03-01";
        var result = await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
        return TransformBudgetResults(result);
    }

    private async Task<JObject> ExecuteCreateBudgetAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var budgetName = RequireArg(args, "budgetName");
        var amount = args["amount"]?.Value<decimal>() ?? throw new ArgumentException("amount is required");
        var timeGrain = RequireArg(args, "timeGrain");
        var startDate = RequireArg(args, "startDate");
        var endDate = args["endDate"]?.ToString();
        var alertThresholds = args["alertThresholds"] as JArray ?? new JArray { 80, 100 };
        var alertEmails = args["alertEmails"] as JArray ?? new JArray();
        
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/budgets/{budgetName}?api-version=2025-03-01";
        
        var notifications = new JObject();
        for (int i = 0; i < alertThresholds.Count; i++)
        {
            var threshold = alertThresholds[i].Value<decimal>();
            notifications[$"alert{threshold}"] = new JObject
            {
                ["enabled"] = true,
                ["operator"] = "GreaterThanOrEqualTo",
                ["threshold"] = threshold,
                ["thresholdType"] = "Actual",
                ["contactEmails"] = alertEmails
            };
        }
        
        var body = new JObject
        {
            ["properties"] = new JObject
            {
                ["category"] = "Cost",
                ["amount"] = amount,
                ["timeGrain"] = timeGrain,
                ["timePeriod"] = new JObject
                {
                    ["startDate"] = startDate,
                    ["endDate"] = string.IsNullOrWhiteSpace(endDate) ? null : endDate
                },
                ["notifications"] = notifications
            }
        };
        
        return await SendCostManagementRequestAsync(HttpMethod.Put, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListDimensionsAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var filter = args["filter"]?.ToString();
        var top = args["top"]?.Value<int?>();
        
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/dimensions?api-version=2025-03-01";
        if (!string.IsNullOrWhiteSpace(filter)) url += $"&$filter={Uri.EscapeDataString(filter)}";
        if (top.HasValue) url += $"&$top={top.Value}";
        
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListAlertsAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/alerts?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListExportsAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/exports?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteRunExportAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var exportName = RequireArg(args, "exportName");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/exports/{exportName}/run?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Post, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteUpdateBudgetAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var budgetName = RequireArg(args, "budgetName");
        
        // First get the existing budget
        var getUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/budgets/{budgetName}?api-version=2025-03-01";
        var existingBudget = await SendCostManagementRequestAsync(HttpMethod.Get, getUrl, null).ConfigureAwait(false);
        
        var properties = existingBudget["properties"] as JObject ?? new JObject();
        
        // Update amount if provided
        if (args["amount"] != null)
            properties["amount"] = args["amount"].Value<decimal>();
        
        // Update notifications if thresholds provided
        if (args["alertThresholds"] is JArray thresholds)
        {
            var alertEmails = args["alertEmails"] as JArray ?? properties["notifications"]?.First?.First?["contactEmails"] as JArray ?? new JArray();
            var notifications = new JObject();
            foreach (var threshold in thresholds)
            {
                var t = threshold.Value<decimal>();
                notifications[$"alert{t}"] = new JObject
                {
                    ["enabled"] = true,
                    ["operator"] = "GreaterThanOrEqualTo",
                    ["threshold"] = t,
                    ["thresholdType"] = "Actual",
                    ["contactEmails"] = alertEmails
                };
            }
            properties["notifications"] = notifications;
        }
        
        var body = new JObject { ["properties"] = properties };
        var putUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/budgets/{budgetName}?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Put, putUrl, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteBudgetAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var budgetName = RequireArg(args, "budgetName");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/budgets/{budgetName}?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDismissAlertAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var alertId = RequireArg(args, "alertId");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/alerts/{alertId}?api-version=2025-03-01";
        
        var body = new JObject
        {
            ["properties"] = new JObject
            {
                ["status"] = "Dismissed"
            }
        };
        
        return await SendCostManagementRequestAsync(new HttpMethod("PATCH"), url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBenefitRecommendationsAsync(JObject args)
    {
        string url;
        var lookBackPeriod = args["lookBackPeriod"]?.ToString() ?? "Last30Days";
        var term = args["term"]?.ToString() ?? "P1Y";
        var filter = $"properties/lookBackPeriod eq '{lookBackPeriod}' and properties/term eq '{term}'";
        
        if (args["billingAccountId"] != null)
        {
            var billingAccountId = args["billingAccountId"].ToString();
            url = $"https://management.azure.com/providers/Microsoft.Billing/billingAccounts/{billingAccountId}/providers/Microsoft.CostManagement/benefitRecommendations?api-version=2025-03-01&$filter={Uri.EscapeDataString(filter)}";
        }
        else if (args["subscriptionId"] != null)
        {
            var subscriptionId = args["subscriptionId"].ToString();
            url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/benefitRecommendations?api-version=2025-03-01&$filter={Uri.EscapeDataString(filter)}";
        }
        else
        {
            throw new ArgumentException("Either billingAccountId or subscriptionId is required");
        }
        
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGetBenefitUtilizationAsync(JObject args)
    {
        string url;
        var grainFilter = args["grainFilter"]?.ToString() ?? "Daily";
        var filter = $"properties/usageDate ge '{DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}'";
        
        if (args["billingAccountId"] != null)
        {
            var billingAccountId = args["billingAccountId"].ToString();
            url = $"https://management.azure.com/providers/Microsoft.Billing/billingAccounts/{billingAccountId}/providers/Microsoft.CostManagement/benefitUtilizationSummaries?api-version=2025-03-01&grainFilter={grainFilter}";
        }
        else if (args["subscriptionId"] != null)
        {
            var subscriptionId = args["subscriptionId"].ToString();
            url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/benefitUtilizationSummaries?api-version=2025-03-01&grainFilter={grainFilter}";
        }
        else
        {
            throw new ArgumentException("Either billingAccountId or subscriptionId is required");
        }
        
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteQueryManagementGroupCostsAsync(JObject args)
    {
        var managementGroupId = RequireArg(args, "managementGroupId");
        var queryType = RequireArg(args, "queryType");
        var timeframe = RequireArg(args, "timeframe");
        var granularity = args["granularity"]?.ToString() ?? "Daily";
        var groupBy = args["groupBy"]?.ToString();
        
        var url = $"https://management.azure.com/providers/Microsoft.Management/managementGroups/{managementGroupId}/providers/Microsoft.CostManagement/query?api-version=2025-03-01";
        
        var body = new JObject
        {
            ["type"] = queryType,
            ["timeframe"] = timeframe,
            ["dataset"] = new JObject
            {
                ["granularity"] = granularity,
                ["aggregation"] = new JObject
                {
                    ["totalCost"] = new JObject { ["name"] = "Cost", ["function"] = "Sum" }
                }
            }
        };
        
        if (timeframe == "Custom")
        {
            body["timePeriod"] = new JObject
            {
                ["from"] = RequireArg(args, "fromDate"),
                ["to"] = RequireArg(args, "toDate")
            };
        }
        
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            body["dataset"]["grouping"] = new JArray
            {
                new JObject { ["type"] = "Dimension", ["name"] = groupBy }
            };
        }
        
        var result = await SendCostManagementRequestAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
        return TransformQueryResults(result);
    }

    private async Task<JObject> ExecuteCreateExportAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var exportName = RequireArg(args, "exportName");
        var storageAccountId = RequireArg(args, "storageAccountId");
        var containerName = RequireArg(args, "containerName");
        var schedule = RequireArg(args, "schedule");
        var exportType = RequireArg(args, "exportType");
        var timeframe = RequireArg(args, "timeframe");
        
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/exports/{exportName}?api-version=2025-03-01";
        
        var body = new JObject
        {
            ["properties"] = new JObject
            {
                ["schedule"] = new JObject
                {
                    ["status"] = "Active",
                    ["recurrence"] = schedule,
                    ["recurrencePeriod"] = new JObject
                    {
                        ["from"] = DateTime.UtcNow.ToString("yyyy-MM-ddT00:00:00Z"),
                        ["to"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-ddT00:00:00Z")
                    }
                },
                ["format"] = "Csv",
                ["deliveryInfo"] = new JObject
                {
                    ["destination"] = new JObject
                    {
                        ["resourceId"] = storageAccountId,
                        ["container"] = containerName,
                        ["rootFolderPath"] = exportName
                    }
                },
                ["definition"] = new JObject
                {
                    ["type"] = exportType,
                    ["timeframe"] = timeframe,
                    ["dataSet"] = new JObject
                    {
                        ["granularity"] = "Daily"
                    }
                }
            }
        };
        
        return await SendCostManagementRequestAsync(HttpMethod.Put, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteDeleteExportAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var exportName = RequireArg(args, "exportName");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/exports/{exportName}?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Delete, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteListViewsAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/views?api-version=2025-03-01";
        return await SendCostManagementRequestAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteCreateViewAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var viewName = RequireArg(args, "viewName");
        var displayName = RequireArg(args, "displayName");
        var chartType = RequireArg(args, "chartType");
        var timeframe = RequireArg(args, "timeframe");
        var granularity = args["granularity"]?.ToString() ?? "Daily";
        var groupBy = args["groupBy"]?.ToString() ?? "ServiceName";
        
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/views/{viewName}?api-version=2025-03-01";
        
        var body = new JObject
        {
            ["properties"] = new JObject
            {
                ["displayName"] = displayName,
                ["scope"] = $"/subscriptions/{subscriptionId}",
                ["chart"] = chartType,
                ["accumulated"] = "false",
                ["metric"] = "ActualCost",
                ["query"] = new JObject
                {
                    ["type"] = "Usage",
                    ["timeframe"] = timeframe,
                    ["dataSet"] = new JObject
                    {
                        ["granularity"] = granularity,
                        ["aggregation"] = new JObject
                        {
                            ["totalCost"] = new JObject { ["name"] = "Cost", ["function"] = "Sum" }
                        },
                        ["grouping"] = new JArray
                        {
                            new JObject { ["type"] = "Dimension", ["name"] = groupBy }
                        }
                    }
                }
            }
        };
        
        return await SendCostManagementRequestAsync(HttpMethod.Put, url, body).ConfigureAwait(false);
    }

    private async Task<JObject> ExecuteGenerateCostReportAsync(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var fromDate = RequireArg(args, "fromDate");
        var toDate = RequireArg(args, "toDate");
        var metric = args["metric"]?.ToString() ?? "ActualCost";
        
        var url = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/generateCostDetailsReport?api-version=2025-03-01";
        
        var body = new JObject
        {
            ["metric"] = metric,
            ["timePeriod"] = new JObject
            {
                ["start"] = fromDate,
                ["end"] = toDate
            }
        };
        
        return await SendCostManagementRequestAsync(HttpMethod.Post, url, body).ConfigureAwait(false);
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    private string RequireArg(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    private async Task<JObject> SendCostManagementRequestAsync(HttpMethod method, string url, JObject body)
    {
        var request = new HttpRequestMessage(method, url);
        
        // Forward authorization from original request
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }
        
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        if (body != null && (method == HttpMethod.Post || method == HttpMethod.Put))
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }
        
        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Cost Management API error ({(int)response.StatusCode}): {content}");
        }
        
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };
        }
        
        return JObject.Parse(content);
    }

    // ========================================
    // MCP RESPONSE HELPERS
    // ========================================

    private HttpResponseMessage CreateMCPSuccess(JObject result, JToken id)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateMCPError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data)) error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    #region Application Insights Telemetry

    /// <summary>
    /// Log telemetry event to Application Insights
    /// </summary>
    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
                return; // Telemetry disabled
            
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
        catch { } // Suppress telemetry errors to not affect main flow
    }

    /// <summary>
    /// Extract instrumentation key from connection string
    /// </summary>
    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("InstrumentationKey=".Length);
        return null;
    }

    /// <summary>
    /// Extract ingestion endpoint from connection string
    /// </summary>
    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) 
            return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
            if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return part.Substring("IngestionEndpoint=".Length);
        return "https://dc.services.visualstudio.com/";
    }

    #endregion
    
    /// <summary>
    /// Transform query and forecast results for better Power Platform compatibility
    /// Converts row arrays to named objects based on column definitions
    /// </summary>
    private JObject TransformQueryResults(JObject content)
    {
        if (content["properties"] == null)
        {
            return content;
        }
        
        var properties = content["properties"] as JObject;
        if (properties == null)
        {
            return content;
        }
        
        var columns = properties["columns"] as JArray;
        var rows = properties["rows"] as JArray;
        
        if (columns == null || rows == null)
        {
            return content;
        }
        
        // Create named row objects for easier consumption in Power Platform
        var namedRows = new JArray();
        
        foreach (var row in rows)
        {
            var rowArray = row as JArray;
            if (rowArray == null) continue;
            
            var namedRow = new JObject();
            for (int i = 0; i < columns.Count && i < rowArray.Count; i++)
            {
                var columnName = columns[i]["name"]?.ToString() ?? $"Column{i}";
                namedRow[columnName] = rowArray[i];
            }
            namedRows.Add(namedRow);
        }
        
        // Add the transformed rows as a new property
        properties["namedRows"] = namedRows;
        
        return content;
    }
    
    /// <summary>
    /// Transform budget results for better Power Platform compatibility
    /// </summary>
    private JObject TransformBudgetResults(JObject content)
    {
        // Add computed properties for easier consumption
        var properties = content["properties"] as JObject;
        if (properties != null)
        {
            // Calculate budget utilization percentage if current spend exists
            var amount = properties["amount"]?.Value<decimal?>() ?? 0;
            var currentSpend = properties["currentSpend"]?["amount"]?.Value<decimal?>() ?? 0;
            
            if (amount > 0)
            {
                properties["utilizationPercentage"] = Math.Round((currentSpend / amount) * 100, 2);
            }
            
            // Add formatted currency values if available
            var currentSpendObj = properties["currentSpend"] as JObject;
            if (currentSpendObj != null)
            {
                var spendAmount = currentSpendObj["amount"]?.Value<decimal?>() ?? 0;
                var unit = currentSpendObj["unit"]?.ToString() ?? "USD";
                currentSpendObj["formattedAmount"] = $"{unit} {spendAmount:N2}";
            }
            
            var forecastSpendObj = properties["forecastSpend"] as JObject;
            if (forecastSpendObj != null)
            {
                var forecastAmount = forecastSpendObj["amount"]?.Value<decimal?>() ?? 0;
                var unit = forecastSpendObj["unit"]?.ToString() ?? "USD";
                forecastSpendObj["formattedAmount"] = $"{unit} {forecastAmount:N2}";
            }
        }
        
        return content;
    }
}

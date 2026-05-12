using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Azure Platform Intelligence — MCP Connector                                ║
// ║                                                                            ║
// ║  Tools: list_subscriptions, list_resource_groups, estimate_cost,            ║
// ║         analyze_security, detect_drift, validate_template,                  ║
// ║         visualize_resources, check_policy, import_resources                  ║
// ║                                                                            ║
// ║  Auth: OAuth delegated (management.azure.com)                               ║
// ║  Inspired by: https://azure.github.io/git-ape/                              ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string SERVER_NAME = "azure-platform-intelligence";
    private const string SERVER_VERSION = "1.0.0";
    private const string PROTOCOL_VERSION = "2025-11-25";
    private const string ARM_API_VERSION = "2021-04-01";
    private const string SECURITY_API_VERSION = "2021-06-01";
    private const string POLICY_API_VERSION = "2019-10-01";
    private const int MAX_RETRIES = 3;

    private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Route Swagger operations (non-MCP)
        if (!string.Equals(this.Context.OperationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            return await HandleSwaggerOperation(this.Context.OperationId).ConfigureAwait(false);

        // MCP handling below
        var correlationId = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow;

        try
        {
            var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject request;
            try
            {
                request = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return JsonRpcError(null, -32700, "Parse error", ex.Message);
            }

            var method = request["method"]?.ToString();
            var requestId = request["id"];
            var @params = request["params"] as JObject ?? new JObject();

            await LogToAppInsights("McpRequestReceived", new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Method", method ?? "" }
            }).ConfigureAwait(false);

            switch (method)
            {
                case "initialize":
                    return JsonRpcResult(requestId, BuildInitializeResponse());

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                    return JsonRpcResult(requestId, new JObject());

                case "tools/list":
                    return JsonRpcResult(requestId, new JObject
                    {
                        ["tools"] = BuildToolsList(),
                        ["nextCursor"] = null
                    });

                case "tools/call":
                    return await HandleToolsCallAsync(requestId, @params, correlationId).ConfigureAwait(false);

                case "resources/list":
                    return JsonRpcResult(requestId, new JObject { ["resources"] = new JArray() });

                case "prompts/list":
                    return JsonRpcResult(requestId, new JObject { ["prompts"] = new JArray() });

                case "ping":
                    return JsonRpcResult(requestId, new JObject());

                case "logging/setLevel":
                    return JsonRpcResult(requestId, new JObject());

                case "completion/complete":
                    return JsonRpcResult(requestId, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                default:
                    return JsonRpcError(requestId, -32601, "Method not found", method ?? "");
            }
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsights(ex, new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Operation", "InvokeMCP" }
            }).ConfigureAwait(false);

            return JsonRpcError(null, -32603, "Internal error", ex.Message);
        }
        finally
        {
            await LogToAppInsights("McpRequestCompleted", new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "DurationMs", (DateTime.UtcNow - start).TotalMilliseconds.ToString("F0") }
            }).ConfigureAwait(false);
        }
    }

    // ── MCP: initialize ──────────────────────────────────────────────────

    private JObject BuildInitializeResponse()
    {
        return new JObject
        {
            ["protocolVersion"] = PROTOCOL_VERSION,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = SERVER_NAME,
                ["version"] = SERVER_VERSION,
                ["title"] = "Azure Platform Intelligence",
                ["description"] = "Azure platform engineering tools: cost estimation, security analysis, drift detection, template validation, resource visualization, policy compliance, and infrastructure export."
            }
        };
    }

    // ── MCP: tools/list ──────────────────────────────────────────────────

    private JArray BuildToolsList()
    {
        return new JArray
        {
            ToolWithAnnotations("list_subscriptions",
                "List Azure subscriptions the connected user has access to. Use this first to discover available subscription IDs before calling other tools. No parameters required.",
                Props(),
                new string[0],
                readOnly: true, idempotent: true),

            ToolWithAnnotations("list_resource_groups",
                "List resource groups in an Azure subscription. Use this to discover resource group names before calling tools that require a resource group. Requires Reader role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true)
                ),
                new[] { "subscriptionId" },
                readOnly: true, idempotent: true),

            ToolWithAnnotations("estimate_cost",
                "Estimate monthly cost for Azure resources by querying the Azure Retail Prices API. Provide a resource type and SKU or pass an ARM template to estimate all resources. No Azure subscription required — uses public pricing data.",
                Props(
                    P("resourceType", "string", "Azure resource type for pricing lookup (e.g., 'Virtual Machines', 'Storage', 'Functions')", false),
                    P("skuName", "string", "SKU name (e.g., 'Standard_D2s_v3', 'Standard_LRS', 'Premium_P1')", false),
                    P("region", "string", "Azure region (default: westus2)", false),
                    P("quantity", "integer", "Number of units or hours per month (default: auto-detected from pricing unit)", false),
                    P("currencyCode", "string", "ISO currency code (default: USD)", false),
                    P("armTemplate", "object", "Full ARM template JSON — estimates cost for all resources in the template", false)
                ),
                new string[0],
                readOnly: true, idempotent: true),

            ToolWithAnnotations("analyze_security",
                "Analyze Azure subscription security posture using Microsoft Defender for Cloud assessments. Returns security findings with severity, status, and remediation guidance. Requires Security Reader role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true),
                    P("resourceGroup", "string", "Optional resource group name to filter findings (use list_resource_groups to find this)", false)
                ),
                new[] { "subscriptionId" },
                readOnly: true, idempotent: true),

            ToolWithAnnotations("detect_drift",
                "Detect configuration drift by comparing a desired ARM template against the live Azure state using the What-If API. Shows what would change (create, modify, delete, no change) if the template were deployed. Requires Contributor role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true),
                    P("resourceGroup", "string", "Resource group name (use list_resource_groups to find this)", true),
                    P("armTemplate", "object", "ARM template representing the desired state", true)
                ),
                new[] { "subscriptionId", "resourceGroup", "armTemplate" },
                readOnly: true, idempotent: true),

            ToolWithAnnotations("validate_template",
                "Validate an ARM template against Azure without deploying. Checks for schema errors, invalid resource types, naming conflicts, and permission issues. Returns validation result and a what-if preview. Requires Contributor role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true),
                    P("resourceGroup", "string", "Resource group name (use list_resource_groups to find this)", true),
                    P("armTemplate", "object", "ARM template to validate", true),
                    P("parameters", "object", "Template parameters (optional)", false)
                ),
                new[] { "subscriptionId", "resourceGroup", "armTemplate" },
                readOnly: true, idempotent: true),

            ToolWithAnnotations("visualize_resources",
                "Generate a Mermaid architecture diagram from an Azure resource group. Lists all resources, groups by type, and shows relationships. Useful for documentation and understanding existing infrastructure. Requires Reader role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true),
                    P("resourceGroup", "string", "Resource group name (use list_resource_groups to find this)", true)
                ),
                new[] { "subscriptionId", "resourceGroup" },
                readOnly: true, idempotent: true),

            ToolWithAnnotations("check_policy",
                "Check Azure Policy compliance for a subscription or resource group. Returns a summary of compliant and non-compliant policies with details on which resources are out of compliance. Requires Reader role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true),
                    P("resourceGroup", "string", "Optional resource group name to scope the check (use list_resource_groups to find this)", false)
                ),
                new[] { "subscriptionId" },
                readOnly: true, idempotent: true),

            ToolWithAnnotations("import_resources",
                "Export an Azure resource group as an ARM template. Reverse-engineers deployed resources into Infrastructure as Code. Useful for bringing existing infrastructure under template management. Requires Reader role.",
                Props(
                    P("subscriptionId", "string", "Azure subscription ID (use list_subscriptions to find this)", true),
                    P("resourceGroup", "string", "Resource group name (use list_resource_groups to find this)", true),
                    P("resources", "array", "Optional array of specific resource IDs to export (default: all resources in the group)", false)
                ),
                new[] { "subscriptionId", "resourceGroup" },
                readOnly: true, idempotent: true)
        };
    }

    // ── MCP: tools/call ──────────────────────────────────────────────────

    private async Task<HttpResponseMessage> HandleToolsCallAsync(JToken requestId, JObject @params, string correlationId)
    {
        var toolName = @params["name"]?.ToString();
        var arguments = @params["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return JsonRpcError(requestId, -32602, "Invalid params", "Tool name is required");

        var toolStart = DateTime.UtcNow;

        try
        {
            JObject result;
            switch (toolName)
            {
                case "list_subscriptions":
                    result = await ExecuteListSubscriptions(arguments).ConfigureAwait(false);
                    break;
                case "list_resource_groups":
                    result = await ExecuteListResourceGroups(arguments).ConfigureAwait(false);
                    break;
                case "estimate_cost":
                    result = await ExecuteEstimateCost(arguments).ConfigureAwait(false);
                    break;
                case "analyze_security":
                    result = await ExecuteAnalyzeSecurity(arguments).ConfigureAwait(false);
                    break;
                case "detect_drift":
                    result = await ExecuteDetectDrift(arguments).ConfigureAwait(false);
                    break;
                case "validate_template":
                    result = await ExecuteValidateTemplate(arguments).ConfigureAwait(false);
                    break;
                case "visualize_resources":
                    result = await ExecuteVisualizeResources(arguments).ConfigureAwait(false);
                    break;
                case "check_policy":
                    result = await ExecuteCheckPolicy(arguments).ConfigureAwait(false);
                    break;
                case "import_resources":
                    result = await ExecuteImportResources(arguments).ConfigureAwait(false);
                    break;
                default:
                    return JsonRpcError(requestId, -32601, "Tool not found", toolName);
            }

            await LogToAppInsights("ToolExecuted", new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Tool", toolName },
                { "DurationMs", (DateTime.UtcNow - toolStart).TotalMilliseconds.ToString("F0") },
                { "Success", "true" }
            }).ConfigureAwait(false);

            var resultText = result.ToString(Newtonsoft.Json.Formatting.None);

            // Truncate very large responses to stay within connector limits
            if (resultText.Length > 60000)
            {
                resultText = resultText.Substring(0, 60000);
                result = new JObject
                {
                    ["truncated"] = true,
                    ["message"] = "Response truncated to 60,000 characters. Use more specific filters to reduce result size.",
                    ["partialData"] = resultText
                };
                resultText = result.ToString(Newtonsoft.Json.Formatting.None);
            }

            return JsonRpcResult(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = resultText
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("ToolFailed", new Dictionary<string, string>
            {
                { "CorrelationId", correlationId },
                { "Tool", toolName },
                { "Error", ex.Message },
                { "DurationMs", (DateTime.UtcNow - toolStart).TotalMilliseconds.ToString("F0") }
            }).ConfigureAwait(false);

            return JsonRpcResult(requestId, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = "Error: " + ex.Message
                    }
                },
                ["isError"] = true
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: list_subscriptions
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteListSubscriptions(JObject args)
    {
        var url = "https://management.azure.com/subscriptions?api-version=2022-12-01";
        var allSubs = await SendAzureRequestPaginated(HttpMethod.Get, url).ConfigureAwait(false);

        var subscriptions = new JArray();
        foreach (var sub in allSubs)
        {
            subscriptions.Add(new JObject
            {
                ["subscriptionId"] = sub["subscriptionId"],
                ["displayName"] = sub["displayName"],
                ["state"] = sub["state"],
                ["tenantId"] = sub["tenantId"]
            });
        }

        return new JObject
        {
            ["subscriptions"] = subscriptions,
            ["count"] = subscriptions.Count
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: list_resource_groups
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteListResourceGroups(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");

        var url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourcegroups?api-version=2021-04-01";
        var allGroups = await SendAzureRequestPaginated(HttpMethod.Get, url).ConfigureAwait(false);

        var groups = new JArray();
        foreach (var rg in allGroups)
        {
            groups.Add(new JObject
            {
                ["name"] = rg["name"],
                ["location"] = rg["location"],
                ["provisioningState"] = rg["properties"]?["provisioningState"]
            });
        }

        return new JObject
        {
            ["subscriptionId"] = subscriptionId,
            ["resourceGroups"] = groups,
            ["count"] = groups.Count
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: estimate_cost
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteEstimateCost(JObject args)
    {
        var armTemplate = args["armTemplate"] as JObject;
        if (armTemplate != null)
            return await EstimateCostFromTemplate(armTemplate, args).ConfigureAwait(false);

        var resourceType = args.Value<string>("resourceType");
        if (string.IsNullOrWhiteSpace(resourceType))
            throw new ArgumentException("Either 'resourceType' or 'armTemplate' is required");

        var skuName = args.Value<string>("skuName") ?? "";
        var region = args.Value<string>("region") ?? "westus2";
        var quantity = args.Value<int?>("quantity") ?? 1;
        var currency = args.Value<string>("currencyCode") ?? "USD";

        var filter = $"serviceName eq '{resourceType}' and armRegionName eq '{region}' and currencyCode eq '{currency}'";
        if (!string.IsNullOrWhiteSpace(skuName))
            filter += $" and skuName eq '{skuName}'";

        var url = $"https://prices.azure.com/api/retail/prices?$filter={Uri.EscapeDataString(filter)}&$top=10";
        var response = await SendPublicRequest(HttpMethod.Get, url).ConfigureAwait(false);

        var items = response["Items"] as JArray ?? new JArray();
        var results = new JArray();
        decimal totalMonthly = 0;

        foreach (var item in items.Take(10))
        {
            var unitPrice = item.Value<decimal>("retailPrice");
            var effectiveQuantity = quantity;
            if (effectiveQuantity <= 1)
                effectiveQuantity = InferMonthlyQuantity(item.Value<string>("unitOfMeasure"));
            var monthly = unitPrice * effectiveQuantity;
            totalMonthly += monthly;

            results.Add(new JObject
            {
                ["skuName"] = item["skuName"],
                ["meterName"] = item["meterName"],
                ["unitOfMeasure"] = item["unitOfMeasure"],
                ["retailPrice"] = unitPrice,
                ["monthlyQuantity"] = effectiveQuantity,
                ["monthlyEstimate"] = Math.Round(monthly, 2),
                ["currencyCode"] = item["currencyCode"],
                ["productName"] = item["productName"],
                ["type"] = item["type"]
            });
        }

        return new JObject
        {
            ["query"] = new JObject
            {
                ["resourceType"] = resourceType,
                ["skuName"] = skuName,
                ["region"] = region,
                ["quantity"] = quantity
            },
            ["pricingItems"] = results,
            ["totalMonthlyEstimate"] = Math.Round(totalMonthly, 2),
            ["currency"] = currency,
            ["note"] = "Estimates based on Azure Retail Prices API. Actual costs may vary based on reservations, discounts, and consumption patterns."
        };
    }

    private async Task<JObject> EstimateCostFromTemplate(JObject template, JObject args)
    {
        var region = args.Value<string>("region") ?? "westus2";
        var currency = args.Value<string>("currencyCode") ?? "USD";
        var resources = template["resources"] as JArray;

        if (resources == null || resources.Count == 0)
            throw new ArgumentException("ARM template contains no resources");

        var estimates = new JArray();
        decimal grandTotal = 0;

        foreach (var resource in resources)
        {
            var type = resource.Value<string>("type") ?? "";
            var sku = resource["sku"]?.Value<string>("name") ?? "";
            var name = resource.Value<string>("name") ?? type;

            // Map ARM resource type to pricing service name
            var serviceName = MapResourceTypeToPricingService(type);
            if (string.IsNullOrWhiteSpace(serviceName)) continue;

            var filter = $"serviceName eq '{serviceName}' and armRegionName eq '{region}' and currencyCode eq '{currency}'";
            if (!string.IsNullOrWhiteSpace(sku))
                filter += $" and skuName eq '{sku}'";

            var url = $"https://prices.azure.com/api/retail/prices?$filter={Uri.EscapeDataString(filter)}&$top=3";

            try
            {
                var response = await SendPublicRequest(HttpMethod.Get, url).ConfigureAwait(false);
                var items = response["Items"] as JArray;
                if (items != null && items.Count > 0)
                {
                    var price = items[0].Value<decimal>("retailPrice");
                    var unitOfMeasure = items[0].Value<string>("unitOfMeasure") ?? "";
                    var monthly = price * InferMonthlyQuantity(unitOfMeasure);

                    grandTotal += monthly;
                    estimates.Add(new JObject
                    {
                        ["resourceName"] = name,
                        ["resourceType"] = type,
                        ["skuName"] = items[0]["skuName"],
                        ["unitOfMeasure"] = unitOfMeasure,
                        ["monthlyEstimate"] = Math.Round(monthly, 2)
                    });
                }
            }
            catch { /* Skip resources where pricing lookup fails */ }
        }

        return new JObject
        {
            ["resourceEstimates"] = estimates,
            ["totalMonthlyEstimate"] = Math.Round(grandTotal, 2),
            ["currency"] = currency,
            ["region"] = region,
            ["resourceCount"] = resources.Count,
            ["pricedCount"] = estimates.Count,
            ["note"] = "Estimates based on Azure Retail Prices API. Quantity auto-detected from pricing unit of measure."
        };
    }

    private string MapResourceTypeToPricingService(string armType)
    {
        if (string.IsNullOrWhiteSpace(armType)) return null;
        var lower = armType.ToLowerInvariant();
        if (lower.IndexOf("microsoft.compute/virtualmachines", StringComparison.Ordinal) >= 0) return "Virtual Machines";
        if (lower.IndexOf("microsoft.storage/storageaccounts", StringComparison.Ordinal) >= 0) return "Storage";
        if (lower.IndexOf("microsoft.web/serverfarms", StringComparison.Ordinal) >= 0) return "Azure App Service";
        if (lower.IndexOf("microsoft.web/sites", StringComparison.Ordinal) >= 0) return "Azure App Service";
        if (lower.IndexOf("microsoft.sql/servers", StringComparison.Ordinal) >= 0) return "SQL Database";
        if (lower.IndexOf("microsoft.documentdb", StringComparison.Ordinal) >= 0) return "Azure Cosmos DB";
        if (lower.IndexOf("microsoft.containerservice", StringComparison.Ordinal) >= 0) return "Azure Kubernetes Service";
        if (lower.IndexOf("microsoft.app/containerapps", StringComparison.Ordinal) >= 0) return "Azure Container Apps";
        if (lower.IndexOf("microsoft.insights/components", StringComparison.Ordinal) >= 0) return "Application Insights";
        if (lower.IndexOf("microsoft.keyvault", StringComparison.Ordinal) >= 0) return "Key Vault";
        if (lower.IndexOf("microsoft.network/publicipaddresses", StringComparison.Ordinal) >= 0) return "Virtual Network";
        if (lower.IndexOf("microsoft.network/virtualnetworks", StringComparison.Ordinal) >= 0) return "Virtual Network";
        if (lower.IndexOf("microsoft.network/loadbalancers", StringComparison.Ordinal) >= 0) return "Load Balancer";
        if (lower.IndexOf("microsoft.network/applicationgateways", StringComparison.Ordinal) >= 0) return "Application Gateway";
        if (lower.IndexOf("microsoft.cognitiveservices", StringComparison.Ordinal) >= 0) return "Cognitive Services";
        if (lower.IndexOf("microsoft.search", StringComparison.Ordinal) >= 0) return "Azure Cognitive Search";
        if (lower.IndexOf("microsoft.cache/redis", StringComparison.Ordinal) >= 0) return "Azure Cache for Redis";
        if (lower.IndexOf("microsoft.servicebus", StringComparison.Ordinal) >= 0) return "Service Bus";
        if (lower.IndexOf("microsoft.eventhub", StringComparison.Ordinal) >= 0) return "Event Hubs";
        if (lower.IndexOf("microsoft.signalrservice", StringComparison.Ordinal) >= 0) return "Azure SignalR Service";
        return null;
    }

    /// <summary>
    /// Infer how many units per month based on the pricing API's unitOfMeasure field.
    /// </summary>
    private int InferMonthlyQuantity(string unitOfMeasure)
    {
        if (string.IsNullOrWhiteSpace(unitOfMeasure)) return 1;
        var lower = unitOfMeasure.ToLowerInvariant().Trim();

        // Hourly pricing → 730 hours/month
        if (lower == "1 hour" || lower.IndexOf("/hour", StringComparison.Ordinal) >= 0 || lower.IndexOf("per hour", StringComparison.Ordinal) >= 0)
            return 730;

        // Daily pricing → ~30.4 days/month
        if (lower == "1 day" || lower.IndexOf("/day", StringComparison.Ordinal) >= 0)
            return 30;

        // Per-month pricing → 1
        if (lower.IndexOf("/month", StringComparison.Ordinal) >= 0 || lower.IndexOf("per month", StringComparison.Ordinal) >= 0)
            return 1;

        // 10K/100K/1M transaction units
        if (lower.IndexOf("10k", StringComparison.Ordinal) >= 0) return 100; // assume 1M transactions → 100 × 10K
        if (lower.IndexOf("10,000", StringComparison.Ordinal) >= 0) return 100;

        // GB-based (storage) → 100 GB default
        if (lower.IndexOf("gb", StringComparison.Ordinal) >= 0) return 100;

        // Default: 1 unit
        return 1;
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: analyze_security
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteAnalyzeSecurity(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var resourceGroup = args.Value<string>("resourceGroup");

        var url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/providers/Microsoft.Security/assessments?api-version={SECURITY_API_VERSION}";
        var assessments = await SendAzureRequestPaginated(HttpMethod.Get, url).ConfigureAwait(false);
        var findings = new JArray();
        int high = 0, medium = 0, low = 0, healthy = 0;

        foreach (var assessment in assessments)
        {
            var status = assessment["properties"]?["status"];
            var statusCode = status?["code"]?.ToString() ?? "";
            var severity = assessment["properties"]?["metadata"]?["severity"]?.ToString() ?? "Unknown";
            var resourceId = assessment["properties"]?["resourceDetails"]?["Id"]?.ToString() ?? "";

            // Filter by resource group if specified
            if (!string.IsNullOrWhiteSpace(resourceGroup))
            {
                if (resourceId.IndexOf("/resourceGroups/" + resourceGroup + "/", StringComparison.OrdinalIgnoreCase) < 0 &&
                    resourceId.IndexOf("/resourcegroups/" + resourceGroup + "/", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            if (string.Equals(statusCode, "Healthy", StringComparison.OrdinalIgnoreCase))
            {
                healthy++;
                continue;
            }

            switch (severity.ToLowerInvariant())
            {
                case "high": high++; break;
                case "medium": medium++; break;
                case "low": low++; break;
            }

            findings.Add(new JObject
            {
                ["displayName"] = assessment["properties"]?["displayName"],
                ["severity"] = severity,
                ["status"] = statusCode,
                ["description"] = assessment["properties"]?["metadata"]?["description"],
                ["remediation"] = assessment["properties"]?["metadata"]?["remediationDescription"],
                ["resourceId"] = resourceId
            });
        }

        // Sort by severity: high first
        var sorted = new JArray(findings.OrderBy(f =>
        {
            var s = f.Value<string>("severity")?.ToLowerInvariant() ?? "unknown";
            if (s == "high") return 0;
            if (s == "medium") return 1;
            if (s == "low") return 2;
            return 3;
        }));

        return new JObject
        {
            ["subscriptionId"] = subscriptionId,
            ["resourceGroup"] = resourceGroup ?? "(all)",
            ["summary"] = new JObject
            {
                ["high"] = high,
                ["medium"] = medium,
                ["low"] = low,
                ["healthy"] = healthy,
                ["totalFindings"] = findings.Count
            },
            ["findings"] = sorted.Count > 50 ? new JArray(sorted.Take(50)) : sorted,
            ["truncated"] = sorted.Count > 50
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: detect_drift
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteDetectDrift(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var resourceGroup = RequireArg(args, "resourceGroup");
        var armTemplate = args["armTemplate"] as JObject;
        if (armTemplate == null)
            throw new ArgumentException("'armTemplate' is required");

        var url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourcegroups/{Uri.EscapeDataString(resourceGroup)}/providers/Microsoft.Resources/deployments/drift-check/whatIf?api-version={ARM_API_VERSION}";

        var body = new JObject
        {
            ["properties"] = new JObject
            {
                ["mode"] = "Incremental",
                ["template"] = armTemplate
            }
        };

        var response = await SendAzureRequestWithPolling(HttpMethod.Post, url, body).ConfigureAwait(false);

        var changes = response["properties"]?["changes"] as JArray ?? new JArray();
        int create = 0, modify = 0, delete = 0, noChange = 0;
        var driftItems = new JArray();

        foreach (var change in changes)
        {
            var changeType = change.Value<string>("changeType") ?? "";
            switch (changeType.ToLowerInvariant())
            {
                case "create": create++; break;
                case "modify": modify++; break;
                case "delete": delete++; break;
                case "nochange": noChange++; break;
            }

            if (!string.Equals(changeType, "NoChange", StringComparison.OrdinalIgnoreCase))
            {
                var item = new JObject
                {
                    ["resourceId"] = change["resourceId"],
                    ["changeType"] = changeType,
                    ["resourceType"] = change["after"]?["type"] ?? change["before"]?["type"]
                };

                var delta = change["delta"] as JArray;
                if (delta != null && delta.Count > 0)
                {
                    var props = new JArray();
                    foreach (var d in delta.Take(20))
                    {
                        props.Add(new JObject
                        {
                            ["path"] = d["path"],
                            ["propertyChangeType"] = d["propertyChangeType"],
                            ["before"] = d["before"],
                            ["after"] = d["after"]
                        });
                    }
                    item["propertyChanges"] = props;
                }

                driftItems.Add(item);
            }
        }

        return new JObject
        {
            ["subscriptionId"] = subscriptionId,
            ["resourceGroup"] = resourceGroup,
            ["summary"] = new JObject
            {
                ["create"] = create,
                ["modify"] = modify,
                ["delete"] = delete,
                ["noChange"] = noChange,
                ["totalChanges"] = changes.Count
            },
            ["driftDetected"] = (create + modify + delete) > 0,
            ["changes"] = driftItems
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: validate_template
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteValidateTemplate(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var resourceGroup = RequireArg(args, "resourceGroup");
        var armTemplate = args["armTemplate"] as JObject;
        if (armTemplate == null)
            throw new ArgumentException("'armTemplate' is required");

        var templateParams = args["parameters"] as JObject;

        // Step 1: Validate
        var validateUrl = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourcegroups/{Uri.EscapeDataString(resourceGroup)}/providers/Microsoft.Resources/deployments/validate-check/validate?api-version={ARM_API_VERSION}";

        var validateBody = new JObject
        {
            ["properties"] = new JObject
            {
                ["mode"] = "Incremental",
                ["template"] = armTemplate
            }
        };
        if (templateParams != null)
            validateBody["properties"]["parameters"] = templateParams;

        JObject validateResponse;
        bool isValid;
        JToken validationError = null;

        try
        {
            validateResponse = await SendAzureRequest(HttpMethod.Post, validateUrl, validateBody).ConfigureAwait(false);
            isValid = true;
        }
        catch (AzureApiException ex)
        {
            isValid = false;
            try { validationError = JObject.Parse(ex.Message); }
            catch { validationError = new JValue(ex.Message); }
            validateResponse = new JObject();
        }

        // Step 2: What-If preview (only if validation passed)
        JToken whatIfSummary = null;
        if (isValid)
        {
            try
            {
                var whatIfUrl = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourcegroups/{Uri.EscapeDataString(resourceGroup)}/providers/Microsoft.Resources/deployments/validate-check/whatIf?api-version={ARM_API_VERSION}";
                var whatIfResponse = await SendAzureRequestWithPolling(HttpMethod.Post, whatIfUrl, validateBody).ConfigureAwait(false);
                var changes = whatIfResponse["properties"]?["changes"] as JArray ?? new JArray();

                whatIfSummary = new JObject
                {
                    ["create"] = changes.Count(c => string.Equals(c.Value<string>("changeType"), "Create", StringComparison.OrdinalIgnoreCase)),
                    ["modify"] = changes.Count(c => string.Equals(c.Value<string>("changeType"), "Modify", StringComparison.OrdinalIgnoreCase)),
                    ["delete"] = changes.Count(c => string.Equals(c.Value<string>("changeType"), "Delete", StringComparison.OrdinalIgnoreCase)),
                    ["noChange"] = changes.Count(c => string.Equals(c.Value<string>("changeType"), "NoChange", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch { /* What-if is optional — don't fail validation if it errors */ }
        }

        return new JObject
        {
            ["subscriptionId"] = subscriptionId,
            ["resourceGroup"] = resourceGroup,
            ["valid"] = isValid,
            ["error"] = validationError,
            ["whatIfPreview"] = whatIfSummary
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: visualize_resources
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteVisualizeResources(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var resourceGroup = RequireArg(args, "resourceGroup");

        var url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourceGroups/{Uri.EscapeDataString(resourceGroup)}/resources?api-version={ARM_API_VERSION}";
        var resources = await SendAzureRequestPaginated(HttpMethod.Get, url).ConfigureAwait(false);
        if (resources.Count == 0)
            return new JObject
            {
                ["resourceGroup"] = resourceGroup,
                ["resourceCount"] = 0,
                ["mermaid"] = "graph TD\n    empty[\"No resources found\"]"
            };

        // Group resources by type
        var byType = new Dictionary<string, List<JToken>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in resources)
        {
            var type = r.Value<string>("type") ?? "Unknown";
            var shortType = type.Split('/').LastOrDefault() ?? type;
            if (!byType.ContainsKey(shortType))
                byType[shortType] = new List<JToken>();
            byType[shortType].Add(r);
        }

        // Build Mermaid diagram
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");
        sb.AppendLine($"    RG[\"{resourceGroup}\"]");

        int nodeId = 0;
        var nodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in byType.OrderBy(g => g.Key))
        {
            var groupNodeId = $"type{nodeId++}";
            sb.AppendLine($"    {groupNodeId}[\"{group.Key} ({group.Value.Count})\"]");
            sb.AppendLine($"    RG --> {groupNodeId}");

            foreach (var r in group.Value)
            {
                var name = r.Value<string>("name") ?? "unnamed";
                var id = r.Value<string>("id") ?? "";
                var rNodeId = $"r{nodeId++}";
                sb.AppendLine($"    {rNodeId}[\"{name}\"]");
                sb.AppendLine($"    {groupNodeId} --> {rNodeId}");
                if (!string.IsNullOrWhiteSpace(id))
                    nodeMap[id] = rNodeId;
            }
        }

        // Detect cross-references between resources
        foreach (var r in resources)
        {
            var thisId = r.Value<string>("id") ?? "";
            if (!nodeMap.ContainsKey(thisId)) continue;
            var thisNode = nodeMap[thisId];

            var json = r.ToString();
            foreach (var kvp in nodeMap)
            {
                if (string.Equals(kvp.Key, thisId, StringComparison.OrdinalIgnoreCase)) continue;
                if (json.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.AppendLine($"    {thisNode} -.-> {kvp.Value}");
                }
            }
        }

        var resourceList = new JArray();
        foreach (var r in resources)
        {
            resourceList.Add(new JObject
            {
                ["name"] = r["name"],
                ["type"] = r["type"],
                ["location"] = r["location"]
            });
        }

        return new JObject
        {
            ["resourceGroup"] = resourceGroup,
            ["resourceCount"] = resources.Count,
            ["resources"] = resourceList,
            ["mermaid"] = sb.ToString().TrimEnd()
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: check_policy
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteCheckPolicy(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var resourceGroup = args.Value<string>("resourceGroup");

        string url;
        if (!string.IsNullOrWhiteSpace(resourceGroup))
            url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourceGroups/{Uri.EscapeDataString(resourceGroup)}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version={POLICY_API_VERSION}";
        else
            url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version={POLICY_API_VERSION}";

        var response = await SendAzureRequest(HttpMethod.Post, url).ConfigureAwait(false);

        var summaries = response["value"] as JArray;
        var summary = summaries?.FirstOrDefault() as JObject;

        if (summary == null)
            return new JObject
            {
                ["subscriptionId"] = subscriptionId,
                ["resourceGroup"] = resourceGroup ?? "(all)",
                ["message"] = "No policy summary data available"
            };

        var results = summary["results"] as JObject ?? new JObject();
        var policyAssignments = summary["policyAssignments"] as JArray ?? new JArray();

        var nonCompliant = new JArray();
        foreach (var assignment in policyAssignments)
        {
            var assignResults = assignment["results"] as JObject;
            var ncCount = assignResults?["nonCompliantResources"]?.Value<int>() ?? 0;
            if (ncCount == 0) continue;

            var policyDefs = assignment["policyDefinitions"] as JArray ?? new JArray();
            var ncDefs = new JArray();
            foreach (var def in policyDefs)
            {
                var defResults = def["results"] as JObject;
                var defNc = defResults?["nonCompliantResources"]?.Value<int>() ?? 0;
                if (defNc == 0) continue;
                ncDefs.Add(new JObject
                {
                    ["policyDefinitionId"] = def["policyDefinitionId"],
                    ["effect"] = def["effect"],
                    ["nonCompliantResources"] = defNc
                });
            }

            nonCompliant.Add(new JObject
            {
                ["policyAssignmentId"] = assignment["policyAssignmentId"],
                ["nonCompliantResources"] = ncCount,
                ["nonCompliantPolicies"] = assignResults?["nonCompliantPolicies"],
                ["policyDefinitions"] = ncDefs
            });
        }

        return new JObject
        {
            ["subscriptionId"] = subscriptionId,
            ["resourceGroup"] = resourceGroup ?? "(all)",
            ["overallCompliance"] = new JObject
            {
                ["nonCompliantResources"] = results["nonCompliantResources"],
                ["nonCompliantPolicies"] = results["nonCompliantPolicies"],
                ["totalResources"] = results["queryResultsCount"]
            },
            ["nonCompliantAssignments"] = nonCompliant,
            ["totalAssignments"] = policyAssignments.Count
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOOL: import_resources
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> ExecuteImportResources(JObject args)
    {
        var subscriptionId = RequireArg(args, "subscriptionId");
        var resourceGroup = RequireArg(args, "resourceGroup");
        var resourceIds = args["resources"] as JArray;

        var url = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourcegroups/{Uri.EscapeDataString(resourceGroup)}/exportTemplate?api-version={ARM_API_VERSION}";

        var body = new JObject
        {
            ["options"] = "IncludeParameterDefaultValue,IncludeComments"
        };

        if (resourceIds != null && resourceIds.Count > 0)
        {
            body["resources"] = resourceIds;
        }
        else
        {
            body["resources"] = new JArray { "*" };
        }

        var response = await SendAzureRequestWithPolling(HttpMethod.Post, url, body).ConfigureAwait(false);

        var template = response["template"] as JObject;
        var error = response["error"] as JObject;

        var result = new JObject
        {
            ["subscriptionId"] = subscriptionId,
            ["resourceGroup"] = resourceGroup,
            ["exportSuccess"] = template != null
        };

        if (template != null)
        {
            var resourceCount = (template["resources"] as JArray)?.Count ?? 0;
            result["resourceCount"] = resourceCount;

            // Truncate large templates to avoid exceeding connector response limits
            var templateJson = template.ToString(Newtonsoft.Json.Formatting.None);
            if (templateJson.Length > 50000)
            {
                result["template"] = "(template too large to return inline — " + templateJson.Length + " characters)";
                result["truncated"] = true;
                // Return just the resource list for reference
                var resourceSummary = new JArray();
                var templateResources = template["resources"] as JArray ?? new JArray();
                foreach (var r in templateResources)
                {
                    resourceSummary.Add(new JObject
                    {
                        ["type"] = r["type"],
                        ["name"] = r["name"],
                        ["location"] = r["location"]
                    });
                }
                result["resourceSummary"] = resourceSummary;
            }
            else
            {
                result["template"] = template;
                result["truncated"] = false;
            }
        }

        if (error != null)
        {
            result["error"] = error;
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Swagger Operation Routing
    // ══════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> HandleSwaggerOperation(string operationId)
    {
        try
        {
            JObject result;
            switch (operationId)
            {
                case "ListSubscriptions":
                    result = await ExecuteListSubscriptions(new JObject()).ConfigureAwait(false);
                    break;

                case "ListResourceGroups":
                    result = await ExecuteListResourceGroups(new JObject
                    {
                        ["subscriptionId"] = ExtractPathSegment("subscriptions")
                    }).ConfigureAwait(false);
                    break;

                case "EstimateCost":
                    var costBody = await ReadRequestBody().ConfigureAwait(false);
                    result = await ExecuteEstimateCost(costBody).ConfigureAwait(false);
                    break;

                case "AnalyzeSecurity":
                    result = await ExecuteAnalyzeSecurity(new JObject
                    {
                        ["subscriptionId"] = ExtractPathSegment("subscriptions"),
                        ["resourceGroup"] = GetQueryParam("resourceGroup")
                    }).ConfigureAwait(false);
                    break;

                case "DetectDrift":
                {
                    var driftBody = await ReadRequestBody().ConfigureAwait(false);
                    driftBody["subscriptionId"] = ExtractPathSegment("subscriptions");
                    driftBody["resourceGroup"] = ExtractPathSegment("resourcegroups");
                    result = await ExecuteDetectDrift(driftBody).ConfigureAwait(false);
                    break;
                }

                case "ValidateTemplate":
                {
                    var valBody = await ReadRequestBody().ConfigureAwait(false);
                    valBody["subscriptionId"] = ExtractPathSegment("subscriptions");
                    valBody["resourceGroup"] = ExtractPathSegment("resourcegroups");
                    result = await ExecuteValidateTemplate(valBody).ConfigureAwait(false);
                    break;
                }

                case "VisualizeResources":
                    result = await ExecuteVisualizeResources(new JObject
                    {
                        ["subscriptionId"] = ExtractPathSegment("subscriptions"),
                        ["resourceGroup"] = ExtractPathSegment("resourcegroups")
                    }).ConfigureAwait(false);
                    break;

                case "CheckPolicy":
                    result = await ExecuteCheckPolicy(new JObject
                    {
                        ["subscriptionId"] = ExtractPathSegment("subscriptions"),
                        ["resourceGroup"] = GetQueryParam("resourceGroup")
                    }).ConfigureAwait(false);
                    break;

                case "ImportResources":
                {
                    var importBody = await ReadRequestBody().ConfigureAwait(false) ?? new JObject();
                    importBody["subscriptionId"] = ExtractPathSegment("subscriptions");
                    importBody["resourceGroup"] = ExtractPathSegment("resourcegroups");
                    result = await ExecuteImportResources(importBody).ConfigureAwait(false);
                    break;
                }

                default:
                    return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }

            return OkJson(result);
        }
        catch (Exception ex)
        {
            var error = new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = "InternalError",
                    ["message"] = ex.Message
                }
            };
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(error.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
        }
    }

    // ── Swagger Parameter Helpers ────────────────────────────────────────

    private string ExtractPathSegment(string segmentPrefix)
    {
        var segments = this.Context.Request.RequestUri.AbsolutePath.Split('/');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], segmentPrefix, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(segments[i + 1]);
        }
        return null;
    }

    private string GetQueryParam(string name)
    {
        var query = this.Context.Request.RequestUri.Query;
        if (string.IsNullOrEmpty(query)) return null;
        var pairs = query.TrimStart('?').Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split(new[] { '=' }, 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private async Task<JObject> ReadRequestBody()
    {
        if (this.Context.Request.Content == null) return new JObject();
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body)) return new JObject();
        return JObject.Parse(body);
    }

    private HttpResponseMessage OkJson(JObject result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    // Azure API Helpers
    // ══════════════════════════════════════════════════════════════════════

    private async Task<JObject> SendAzureRequest(HttpMethod method, string url, JObject body = null, int retryCount = 0)
    {
        var request = new HttpRequestMessage(method, url);

        // Forward OAuth token from connector
        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null && (method == HttpMethod.Post || method.Method == "PATCH" || method.Method == "PUT"))
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Handle throttling (429)
        if (response.StatusCode == (HttpStatusCode)429 && retryCount < MAX_RETRIES)
        {
            var retryAfterSeconds = 5;
            IEnumerable<string> retryValues;
            if (response.Headers.TryGetValues("Retry-After", out retryValues))
            {
                var retryValue = retryValues.FirstOrDefault();
                int seconds;
                if (int.TryParse(retryValue, out seconds))
                    retryAfterSeconds = Math.Min(seconds, 30);
            }

            await Task.Delay(retryAfterSeconds * 1000, this.CancellationToken).ConfigureAwait(false);
            return await SendAzureRequest(method, url, body, retryCount + 1).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AzureApiException($"Azure API returned {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return new JObject();

        return JObject.Parse(content);
    }

    /// <summary>
    /// Send an Azure request that may return 202 Accepted with a Location header for async operations.
    /// Polls until the operation completes.
    /// </summary>
    private async Task<JObject> SendAzureRequestWithPolling(HttpMethod method, string url, JObject body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (this.Context.Request.Headers.Authorization != null)
        {
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);

        // If 202 Accepted, poll the Location header
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            string locationUrl = null;
            IEnumerable<string> locationValues;
            if (response.Headers.TryGetValues("Location", out locationValues))
                locationUrl = locationValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(locationUrl) && response.Headers.TryGetValues("Azure-AsyncOperation", out locationValues))
                locationUrl = locationValues.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(locationUrl))
            {
                for (int i = 0; i < 30; i++) // Max 30 polls (~5 minutes)
                {
                    await Task.Delay(10000, this.CancellationToken).ConfigureAwait(false); // 10 second intervals

                    var pollRequest = new HttpRequestMessage(HttpMethod.Get, locationUrl);
                    if (this.Context.Request.Headers.Authorization != null)
                        pollRequest.Headers.Authorization = this.Context.Request.Headers.Authorization;
                    pollRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var pollResponse = await this.Context.SendAsync(pollRequest, this.CancellationToken).ConfigureAwait(false);

                    if (pollResponse.StatusCode != HttpStatusCode.Accepted)
                    {
                        var pollContent = await pollResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!pollResponse.IsSuccessStatusCode)
                            throw new AzureApiException($"Azure async operation failed {(int)pollResponse.StatusCode}: {pollContent}");
                        return string.IsNullOrWhiteSpace(pollContent) ? new JObject() : JObject.Parse(pollContent);
                    }
                }

                throw new TimeoutException("Azure async operation did not complete within the polling window");
            }
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new AzureApiException($"Azure API returned {(int)response.StatusCode}: {content}");

        return string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
    }

    /// <summary>
    /// Send an Azure GET request and follow nextLink pagination to collect all items.
    /// Returns a JArray of all value items across all pages (max 10 pages / 5000 items).
    /// </summary>
    private async Task<JArray> SendAzureRequestPaginated(HttpMethod method, string url, int maxPages = 10)
    {
        var allItems = new JArray();
        var currentUrl = url;

        for (int page = 0; page < maxPages && !string.IsNullOrWhiteSpace(currentUrl); page++)
        {
            var response = await SendAzureRequest(method, currentUrl).ConfigureAwait(false);
            var items = response["value"] as JArray;
            if (items != null)
            {
                foreach (var item in items)
                    allItems.Add(item);
            }

            currentUrl = response.Value<string>("nextLink");
        }

        return allItems;
    }

    /// <summary>
    /// Send a request to a public API (no auth header).
    /// </summary>
    private async Task<JObject> SendPublicRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API returned {(int)response.StatusCode}: {content}");

        return JObject.Parse(content);
    }

    // ══════════════════════════════════════════════════════════════════════
    // JSON-RPC Helpers
    // ══════════════════════════════════════════════════════════════════════

    private HttpResponseMessage JsonRpcResult(JToken id, JObject result)
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

    private HttpResponseMessage JsonRpcError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null)
            error["data"] = data;

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

    // ══════════════════════════════════════════════════════════════════════
    // Tool Schema Helpers
    // ══════════════════════════════════════════════════════════════════════

    private JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required != null && required.Length > 0)
            schema["required"] = new JArray(required);

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema
        };
    }

    private JObject ToolWithAnnotations(string name, string description, JObject properties, string[] required, bool readOnly = false, bool idempotent = false)
    {
        var tool = Tool(name, description, properties, required);
        var annotations = new JObject();
        if (readOnly) annotations["readOnlyHint"] = true;
        if (idempotent) annotations["idempotentHint"] = true;
        if (annotations.Count > 0)
            tool["annotations"] = annotations;
        return tool;
    }

    private JObject Props(params JProperty[] properties)
    {
        var obj = new JObject();
        foreach (var p in properties)
            obj.Add(p);
        return obj;
    }

    private JProperty P(string name, string type, string description, bool required)
    {
        var prop = new JObject
        {
            ["type"] = type,
            ["description"] = description
        };
        return new JProperty(name, prop);
    }

    private string RequireArg(JObject args, string name)
    {
        var value = args.Value<string>(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Application Insights Telemetry
    // ══════════════════════════════════════════════════════════════════════

    private async Task LogToAppInsights(string eventName, IDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.Ordinal) >= 0)
            return;

        try
        {
            var telemetry = new JObject
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
                        ["properties"] = JObject.FromObject(properties ?? new Dictionary<string, string>())
                    }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                request.Content = new StringContent(telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false)) { }
            }
        }
        catch { }
    }

    private async Task LogExceptionToAppInsights(Exception ex, IDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_KEY) || APP_INSIGHTS_KEY.IndexOf("INSERT_YOUR", StringComparison.Ordinal) >= 0)
            return;

        try
        {
            var telemetry = new JObject
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
                                ["hasFullStack"] = true,
                                ["stack"] = ex.ToString()
                            }
                        },
                        ["properties"] = JObject.FromObject(properties ?? new Dictionary<string, string>())
                    }
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, APP_INSIGHTS_ENDPOINT))
            {
                request.Content = new StringContent(telemetry.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false)) { }
            }
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Custom Exception
    // ══════════════════════════════════════════════════════════════════════

    private class AzureApiException : Exception
    {
        public AzureApiException(string message) : base(message) { }
    }
}

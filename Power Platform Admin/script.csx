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
    private const string SERVER_VERSION = "1.4.0";

    // MCP tools that change tenant or environment configuration. An agent can act on
    // an ambiguous instruction, so each of these requires an explicit confirm flag
    // rather than relying on wording in the tool description. Typed Power Automate
    // operations are deliberately excluded: a maker who drops the action into a
    // designer has already made the decision explicitly, and adding a required body
    // property there would break every existing flow.
    private static readonly HashSet<string> CONFIRMATION_REQUIRED_TOOLS =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "admin_update_setting",
            "admin_update_copilot_governance",
            "admin_set_tenant_pool_draw",
            "admin_upsert_resource_threshold",
            "admin_update_environment_allocation",
            "admin_install_package"
        };

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

    // Licensing allocations (documented API on api.powerplatform.com, July 2026)
    private const string LICENSING_API_VERSION = "2024-10-01";
    private const string ALLOCATIONS_BY_ENVIRONMENT = "/licensing/allocationsByEnvironment";
    private const string ALLOCATIONS_V2 = "/licensing/allocationsV2";

    // MCSMessages and MCSSessions are ExternalCurrencyType values on the documented
    // allocation API. The retired tenant-routed endpoint modelled them as entitlement
    // IDs, so the wire shape differs even though the identifiers are spelled the same.
    private static readonly string[] TENANT_POOL_CURRENCIES = { "MCSMessages", "MCSSessions" };

    // Documented ExternalCurrencyType values for Update Allocations By Environment.
    // The service may add members without a version bump, so this list validates
    // input spelling only and unknown values from the service are passed through.
    private static readonly string[] EXTERNAL_CURRENCY_TYPES =
    {
        "AI", "AppPass", "AppPassForTeams", "Invoice", "MCSSessions", "MCSMessages",
        "PAHostedRPA", "PAUnattendedRPA", "PerFlowPlan", "PortalAddOns", "PortalLogins",
        "PortalViews", "PowerPagesAuthenticated", "PowerPagesAnonymous",
        "PowerAutomatePerProcess", "ProcessMiningDataStorage", "SCMessages", "VAConversations"
    };

    // Documented EnforcementRuleTypes for the by-environment allocation surface.
    private static readonly string[] ENFORCEMENT_RULE_TYPES = { "Alert", "PayGo", "TenantPool", "Deny" };

    // Resource thresholds (supported licensing API on api.powerplatform.com)
    private const string LICENSING_THRESHOLD_API_VERSION = "2024-10-01";
    private static readonly string[] THRESHOLD_WRITABLE_FIELDS =
    {
        "limit", "notificationThreshold", "notifyIfOverCapacity",
        "resourceConsumption", "stopIfOverCapacity", "stopResource"
    };

    // Application Insights
    private const string APP_INSIGHTS_CONNECTION_STRING = "[INSERT_YOUR_APP_INSIGHTS_CONNECTION_STRING]";
    private const string APP_INSIGHTS_ENDPOINT = "https://dc.applicationinsights.azure.com/v2/track";

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
                case "ListEnvironmentAllocations":
                    return await HandleTypedListEnvironmentAllocations().ConfigureAwait(false);
                case "GetEnvironmentAllocations":
                    return await HandleTypedGetEnvironmentAllocations().ConfigureAwait(false);
                case "UpdateEnvironmentAllocation":
                    return await HandleTypedUpdateEnvironmentAllocation().ConfigureAwait(false);
                case "GetAllocationAvailability":
                    return await HandleTypedGetAllocationAvailability().ConfigureAwait(false);
                case "GetReservedEntitlements":
                    return await HandleTypedGetReservedEntitlements().ConfigureAwait(false);
                case "ListResourceThresholds":
                    return await HandleTypedListResourceThresholds().ConfigureAwait(false);
                case "UpsertResourceThreshold":
                    return await HandleTypedUpsertResourceThreshold().ConfigureAwait(false);
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
                        },
                        ["confirm"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Must be true. Set it only after showing the caller the environment and the settings that will change."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "settings", "confirm" }
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
                        },
                        ["confirm"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Must be true. Set it only after showing the caller the scope and the settings that will change."
                        }
                    },
                    ["required"] = new JArray { "settings", "confirm" }
                }
            },
            new JObject
            {
                ["name"] = "admin_get_tenant_pool_draw",
                ["description"] = "Check whether an environment draws Copilot Studio message and session capacity (MCSMessages, MCSSessions) from the tenant pool. An environment with no allocation document draws from the pool by default. Read-only. Uses the documented Allocations By Environment API.",
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
                ["description"] = "Enable or disable whether an environment draws Copilot Studio message and session capacity (MCSMessages, MCSSessions) from the tenant pool. Reads the current allocation first, changes only the TenantPool enforcement rule, then reads the result back to confirm it. Changes capacity behavior — show the caller the current state first, then set confirm to true.",
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
                        },
                        ["confirm"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Must be true. Set it only after showing the caller which environment changes and in which direction."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "enabled", "confirm" }
                }
            },
            new JObject
            {
                ["name"] = "admin_list_environment_allocations",
                ["description"] = "List capacity allocations for every environment in the tenant. Each entry reports the allocated and auto-allocated amount per currency (AI, MCSMessages, MCSSessions, PowerPagesAuthenticated, PAHostedRPA, ProcessMiningDataStorage, and others) along with its Alert, PayGo, TenantPool, and Deny enforcement rules. Read-only. Use this to find allocation drift across the tenant.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "admin_get_environment_allocations",
                ["description"] = "Get the capacity allocation document for one environment, including every currency and its enforcement rules. Returns hasAllocationDocument false when the environment has never been allocated, which means it inherits tenant defaults rather than that the lookup failed. Read-only.",
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
                ["name"] = "admin_update_environment_allocation",
                ["description"] = "Set the allocated capacity and/or enforcement rules for a single currency on one environment. Reads the current allocation, changes only the named currency, writes the whole document back, then re-reads to verify. Other currencies are preserved. Deny stops consumption once the allocation is exhausted and can break running apps and agents — describe the exact effect before setting confirm to true.",
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
                        ["currencyType"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The capacity currency to change, for example MCSMessages, AI, PowerPagesAuthenticated, PAHostedRPA, or ProcessMiningDataStorage.",
                            ["enum"] = new JArray(EXTERNAL_CURRENCY_TYPES)
                        },
                        ["allocated"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional. The whole-number quantity to allocate to this environment. Omit to leave the current amount unchanged."
                        },
                        ["enforcementRules"] = new JObject
                        {
                            ["type"] = "array",
                            ["description"] = "Optional. Enforcement rules to set. Only the rules listed here change; any others already on the currency are preserved.",
                            ["items"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["ruleType"] = new JObject
                                    {
                                        ["type"] = "string",
                                        ["description"] = "Alert notifies, PayGo bills overage, TenantPool draws from the shared tenant pool, Deny stops consumption at the limit.",
                                        ["enum"] = new JArray(ENFORCEMENT_RULE_TYPES)
                                    },
                                    ["enabled"] = new JObject
                                    {
                                        ["type"] = "boolean",
                                        ["description"] = "true to enable the rule, false to disable it."
                                    }
                                },
                                ["required"] = new JArray { "ruleType", "enabled" }
                            }
                        },
                        ["confirm"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Must be true. Set it only after showing the caller the environment, currency, current value, and proposed value."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "currencyType", "confirm" }
                }
            },
            new JObject
            {
                ["name"] = "admin_get_allocation_availability",
                ["description"] = "Get how much of each entitlement is still available to allocate. Use this before increasing an environment allocation to check the tenant has unallocated capacity left. Read-only.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["filter"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional OData filter. The filterable fields are not published, so prefer omitting this and narrowing the result yourself."
                        }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "admin_get_reserved_entitlements",
                ["description"] = "Get the reserved quantity and unit for each entitlement in the tenant. Reserved capacity is committed to a scope and is not available for new allocations. This does not report enforcement rules — use admin_get_environment_allocations for those. Read-only.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["filter"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional OData filter. The filterable fields are not published, so prefer omitting this and narrowing the result yourself."
                        }
                    },
                    ["required"] = new JArray()
                }
            },
            new JObject
            {
                ["name"] = "admin_list_resource_thresholds",
                ["description"] = "List every resource threshold configured for a licensing entitlement, across all environments that have one. A threshold caps how much of an entitlement an environment resource may consume and controls notification and stop behavior. Returns the limit, current consumption, notification threshold percentage, and stop flags per resource. Read-only. Optionally pass environmentId to filter the result to a single environment.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["entitlementId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The entitlement ID to read thresholds for, for example MCSMessages or MCSSessions."
                        },
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional. Filter the result to a single environment (GUID). Omit to return thresholds for every environment."
                        }
                    },
                    ["required"] = new JArray { "entitlementId" }
                }
            },
            new JObject
            {
                ["name"] = "admin_upsert_resource_threshold",
                ["description"] = "Create or update the resource threshold for an environment resource on a licensing entitlement. Sets the consumption limit, the notification threshold percentage, and whether to notify or stop when capacity is exceeded. Reads the current threshold first and changes only the fields supplied, so unspecified fields keep their existing values. This is a destructive operation — confirm with the user before executing.",
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["environmentId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The environment ID (GUID) that owns the resource."
                        },
                        ["entitlementId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The entitlement ID, for example MCSMessages or MCSSessions."
                        },
                        ["resourceId"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "The resource ID the threshold applies to. Use admin_list_resource_thresholds to find existing resource IDs."
                        },
                        ["limit"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional. The limit set for resource consumption."
                        },
                        ["notificationThreshold"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional. Percentage of the limit (0-100) at which consumption notifications are sent."
                        },
                        ["notifyIfOverCapacity"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Optional. true to notify when consumption reaches the notification threshold."
                        },
                        ["resourceConsumption"] = new JObject
                        {
                            ["type"] = "number",
                            ["description"] = "Optional. Current consumption of the resource."
                        },
                        ["stopIfOverCapacity"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Optional. true to stop consuming capacity once the resource exceeds its limit."
                        },
                        ["stopResource"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Optional. true to explicitly stop the resource, putting it in a disabled state."
                        },
                        ["confirm"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Must be true. Set it only after showing the caller the resource and the fields that will change. Enabling stopIfOverCapacity or stopResource halts consumption and can break running workloads."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "entitlementId", "resourceId", "confirm" }
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
                        },
                        ["confirm"] = new JObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Must be true. Set it only after showing the caller which package installs into which environment. Package installs modify the environment and are not trivially reversible."
                        }
                    },
                    ["required"] = new JArray { "environmentId", "packageUniqueName", "confirm" }
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
            RequireConfirmation(toolName, arguments);

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
                case "admin_list_environment_allocations":
                    result = await HandleListEnvironmentAllocations(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_environment_allocations":
                    result = await HandleGetEnvironmentAllocations(arguments).ConfigureAwait(false);
                    break;
                case "admin_update_environment_allocation":
                    result = await HandleUpdateEnvironmentAllocation(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_allocation_availability":
                    result = await HandleGetAllocationAvailability(arguments).ConfigureAwait(false);
                    break;
                case "admin_get_reserved_entitlements":
                    result = await HandleGetReservedEntitlements(arguments).ConfigureAwait(false);
                    break;
                case "admin_list_resource_thresholds":
                    result = await HandleListResourceThresholds(arguments).ConfigureAwait(false);
                    break;
                case "admin_upsert_resource_threshold":
                    result = await HandleUpsertResourceThreshold(arguments).ConfigureAwait(false);
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

    // A missing, false, or non-boolean confirm is treated identically: the call is
    // refused. Accepting a string "true" keeps agents that stringify booleans working,
    // but nothing weaker than an explicit affirmative gets through.
    private static void RequireConfirmation(string toolName, JObject arguments)
    {
        if (string.IsNullOrEmpty(toolName) || !CONFIRMATION_REQUIRED_TOOLS.Contains(toolName))
            return;

        var confirm = arguments["confirm"];

        if (confirm != null && confirm.Type != JTokenType.Null)
        {
            if (confirm.Type == JTokenType.Boolean && confirm.Value<bool>())
                return;

            bool parsed;
            if (confirm.Type == JTokenType.String &&
                bool.TryParse(confirm.ToString(), out parsed) && parsed)
                return;
        }

        throw new ArgumentException(
            $"{toolName} changes tenant or environment configuration and cannot be run from an " +
            "implied instruction. Show the caller what will change, then set confirm to true.");
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

    // ─── Licensing Allocations (documented API) ─────────────────────────

    private async Task<JToken> HandleListEnvironmentAllocations(JObject arguments)
    {
        var response = await CallAdminApiRaw(
            HttpMethod.Get,
            $"{ALLOCATIONS_BY_ENVIRONMENT}?api-version={LICENSING_API_VERSION}"
        ).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException(DescribeAllocationFailure(response, null));

        var allocations = ReadAllocationCollection(response.ParseBody());

        var summary = new JArray();
        foreach (var item in allocations)
        {
            var model = item as JObject;
            if (model == null) continue;

            summary.Add(new JObject
            {
                ["environmentId"] = model["environmentId"],
                ["currencyCount"] = (model["currencyAllocations"] as JArray)?.Count ?? 0,
                ["currencies"] = SummarizeCurrencyAllocations(model)
            });
        }

        return new JObject
        {
            ["environmentCount"] = summary.Count,
            ["environments"] = summary
        };
    }

    private async Task<JToken> HandleGetEnvironmentAllocations(JObject arguments)
    {
        var envId = RequireEnvironmentGuid(arguments["environmentId"]?.ToString());

        var document = await FetchEnvironmentAllocation(envId).ConfigureAwait(false);

        return new JObject
        {
            ["environmentId"] = envId,
            ["hasAllocationDocument"] = document != null,
            ["currencies"] = document == null ? new JArray() : SummarizeCurrencyAllocations(document),
            ["allocation"] = document == null ? (JToken)JValue.CreateNull() : document,
            ["message"] = document == null
                ? "No allocation document exists for this environment. It inherits tenant defaults."
                : "Returned the current allocation document for this environment."
        };
    }

    private async Task<JToken> HandleUpdateEnvironmentAllocation(JObject arguments)
    {
        var envId = RequireEnvironmentGuid(arguments["environmentId"]?.ToString());
        var currencyType = RequireCurrencyType(arguments["currencyType"]?.ToString());

        var allocated = arguments["allocated"];
        var enforcementRules = arguments["enforcementRules"] as JArray;

        var hasAllocated = allocated != null && allocated.Type != JTokenType.Null;
        var hasRules = enforcementRules != null && enforcementRules.Count > 0;

        if (!hasAllocated && !hasRules)
            throw new ArgumentException(
                "Supply allocated, enforcementRules, or both. An update that changes nothing is rejected " +
                "so an empty call cannot silently rewrite the allocation document.");

        var before = await FetchEnvironmentAllocation(envId).ConfigureAwait(false);

        // The whole currencyAllocations array is sent back on every write. That makes the
        // result identical whether the service merges or replaces the collection, which is
        // the one behaviour the published contract does not state.
        var document = before != null
            ? (JObject)before.DeepClone()
            : new JObject { ["environmentId"] = envId, ["currencyAllocations"] = new JArray() };

        document["environmentId"] = envId;

        var currencies = document["currencyAllocations"] as JArray;
        if (currencies == null)
        {
            currencies = new JArray();
            document["currencyAllocations"] = currencies;
        }

        var target = FindCurrencyAllocation(currencies, currencyType);
        var created = target == null;

        if (created)
        {
            target = new JObject
            {
                ["currencyType"] = currencyType,
                ["allocated"] = 0,
                ["autoAllocated"] = 0,
                ["enforcementRules"] = new JArray()
            };
            currencies.Add(target);
        }

        var changes = new JArray();

        if (hasAllocated)
        {
            var previous = target["allocated"];
            var next = CoerceAllocatedQuantity(allocated);
            target["allocated"] = next;

            changes.Add(new JObject
            {
                ["field"] = "allocated",
                ["from"] = created ? (JToken)JValue.CreateNull() : previous,
                ["to"] = next
            });
        }

        if (hasRules)
        {
            var rules = target["enforcementRules"] as JArray;
            if (rules == null)
            {
                rules = new JArray();
                target["enforcementRules"] = rules;
            }

            foreach (var item in enforcementRules)
            {
                var requested = item as JObject;
                if (requested == null)
                    throw new ArgumentException("Each enforcementRules entry must be an object with ruleType and enabled.");

                var ruleType = RequireEnforcementRuleType(requested["ruleType"]?.ToString());
                var ruleEnabled = ReadRequiredBoolean(requested["enabled"], $"enforcementRules.{ruleType}.enabled");

                var existing = FindEnforcementRule(rules, ruleType);
                var previous = existing == null ? (JToken)JValue.CreateNull() : existing["enabled"];

                if (existing == null)
                    rules.Add(new JObject { ["ruleType"] = ruleType, ["enabled"] = ruleEnabled });
                else
                    existing["enabled"] = ruleEnabled;

                changes.Add(new JObject
                {
                    ["field"] = $"enforcementRules.{ruleType}.enabled",
                    ["from"] = previous,
                    ["to"] = ruleEnabled
                });
            }
        }

        var applied = await PatchEnvironmentAllocation(envId, document).ConfigureAwait(false);
        var after = await FetchEnvironmentAllocation(envId).ConfigureAwait(false);

        var verified = VerifyCurrencyMatches(after, currencyType, target);

        return new JObject
        {
            ["status"] = "success",
            ["environmentId"] = envId,
            ["currencyType"] = currencyType,
            ["createdCurrencyEntry"] = created,
            ["hadExistingDocument"] = before != null,
            ["changes"] = changes,
            ["verified"] = verified,
            ["before"] = before == null ? (JToken)JValue.CreateNull() : SummarizeCurrencyAllocations(before),
            ["after"] = after == null
                ? (applied ?? (JToken)JValue.CreateNull())
                : SummarizeCurrencyAllocations(after),
            ["message"] = verified
                ? $"Updated {currencyType} on environment {envId} and confirmed the change by reading it back."
                : $"Updated {currencyType} on environment {envId}, but the read-back did not match the requested " +
                  "values. Another administrator may have written concurrently — the service exposes no ETag. " +
                  "Re-read the allocation before making further changes."
        };
    }

    private async Task<JToken> HandleGetAllocationAvailability(JObject arguments)
    {
        var filter = arguments["filter"]?.ToString();

        var path = $"{ALLOCATIONS_V2}/availability?api-version={LICENSING_API_VERSION}";
        if (!string.IsNullOrWhiteSpace(filter))
            path += $"&$filter={Uri.EscapeDataString(filter.Trim())}";

        var response = await CallAdminApiRaw(HttpMethod.Get, path).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException(DescribeAllocationFailure(response, null));

        return new JObject
        {
            ["filter"] = string.IsNullOrWhiteSpace(filter) ? (JToken)JValue.CreateNull() : filter,
            ["availability"] = response.ParseBody() ?? new JObject(),
            ["note"] = "The filterable fields for this endpoint are not published. If a filter returns " +
                       "nothing unexpectedly, retry without one and narrow the result yourself."
        };
    }

    private async Task<JToken> HandleGetReservedEntitlements(JObject arguments)
    {
        var filter = arguments["filter"]?.ToString();

        var path = $"{ALLOCATIONS_V2}/entitlements/reserved?api-version={LICENSING_API_VERSION}";
        if (!string.IsNullOrWhiteSpace(filter))
            path += $"&$filter={Uri.EscapeDataString(filter.Trim())}";

        var response = await CallAdminApiRaw(HttpMethod.Get, path).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException(DescribeAllocationFailure(response, null));

        var reserved = ReadAllocationCollection(response.ParseBody());

        return new JObject
        {
            ["filter"] = string.IsNullOrWhiteSpace(filter) ? (JToken)JValue.CreateNull() : filter,
            ["entitlementCount"] = reserved.Count,
            ["entitlements"] = reserved,
            ["note"] = "Reserved quantities do not include enforcement rules. Use " +
                       "admin_get_environment_allocations to inspect TenantPool, Deny, PayGo, or Alert."
        };
    }

    // ─── Tenant Pool (thin wrapper over the documented allocation API) ──

    private async Task<JToken> HandleGetTenantPoolDraw(JObject arguments)
    {
        var envId = RequireEnvironmentGuid(arguments["environmentId"]?.ToString());

        var document = await FetchEnvironmentAllocation(envId).ConfigureAwait(false);

        var entitlements = new JObject();
        var allEnabled = true;
        foreach (var currencyType in TENANT_POOL_CURRENCIES)
        {
            var ruleEnabled = ReadTenantPoolEnabled(document, currencyType);
            entitlements[currencyType] = ruleEnabled;
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

        var enabled = ReadRequiredBoolean(arguments["enabled"], "enabled");

        var before = await FetchEnvironmentAllocation(envId).ConfigureAwait(false);

        var document = before != null
            ? (JObject)before.DeepClone()
            : new JObject { ["environmentId"] = envId, ["currencyAllocations"] = new JArray() };

        document["environmentId"] = envId;

        var currencies = document["currencyAllocations"] as JArray;
        if (currencies == null)
        {
            currencies = new JArray();
            document["currencyAllocations"] = currencies;
        }

        foreach (var currencyType in TENANT_POOL_CURRENCIES)
        {
            var allocation = FindCurrencyAllocation(currencies, currencyType);
            if (allocation == null)
            {
                allocation = new JObject
                {
                    ["currencyType"] = currencyType,
                    ["allocated"] = 0,
                    ["autoAllocated"] = 0,
                    ["enforcementRules"] = new JArray()
                };
                currencies.Add(allocation);
            }

            var rules = allocation["enforcementRules"] as JArray;
            if (rules == null)
            {
                rules = new JArray();
                allocation["enforcementRules"] = rules;
            }

            var rule = FindEnforcementRule(rules, "TenantPool");
            if (rule == null)
                rules.Add(new JObject { ["ruleType"] = "TenantPool", ["enabled"] = enabled });
            else
                rule["enabled"] = enabled;
        }

        await PatchEnvironmentAllocation(envId, document).ConfigureAwait(false);

        var after = await FetchEnvironmentAllocation(envId).ConfigureAwait(false);

        var verified = true;
        var affected = new JArray();
        foreach (var currencyType in TENANT_POOL_CURRENCIES)
        {
            affected.Add(currencyType);
            if (ReadTenantPoolEnabled(after, currencyType) != enabled) verified = false;
        }

        return new JObject
        {
            ["status"] = "success",
            ["environmentId"] = envId,
            ["enabled"] = enabled,
            ["entitlements"] = affected,
            ["verified"] = verified,
            ["message"] = verified
                ? (enabled
                    ? "This environment now draws Copilot Studio message and session capacity from the tenant pool."
                    : "This environment no longer draws Copilot Studio message and session capacity from the tenant pool.")
                : "The write was accepted but the read-back did not show the expected TenantPool state. " +
                  "Another administrator may have written concurrently — the service exposes no ETag. " +
                  "Re-read before making further changes."
        };
    }

    // ─── Allocation helpers ─────────────────────────────────────────────

    private async Task<JObject> FetchEnvironmentAllocation(string environmentId)
    {
        var response = await CallAdminApiRaw(
            HttpMethod.Get,
            $"{ALLOCATIONS_BY_ENVIRONMENT}/{Uri.EscapeDataString(environmentId)}?api-version={LICENSING_API_VERSION}"
        ).ConfigureAwait(false);

        // An environment that has never been allocated has no document. That is the
        // inherit-tenant-defaults case, not a failure, and it must stay distinct from
        // a 404 caused by a bad environment ID or a missing permission.
        if (response.Status == HttpStatusCode.NotFound) return null;
        if (response.Status == HttpStatusCode.NoContent) return null;

        if (!response.IsSuccess)
            throw new InvalidOperationException(DescribeAllocationFailure(response, environmentId));

        var parsed = response.ParseBody();
        if (parsed == null) return null;

        var model = parsed as JObject;
        if (model != null && model["currencyAllocations"] != null) return model;

        // Tolerate an envelope in case the service pages or wraps the single result.
        var collection = ReadAllocationCollection(parsed);
        foreach (var item in collection)
        {
            var candidate = item as JObject;
            if (candidate == null) continue;

            if (string.Equals(candidate["environmentId"]?.ToString(), environmentId, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return model;
    }

    private async Task<JToken> PatchEnvironmentAllocation(string environmentId, JObject document)
    {
        var response = await CallAdminApiRaw(
            new HttpMethod("PATCH"),
            $"{ALLOCATIONS_BY_ENVIRONMENT}?api-version={LICENSING_API_VERSION}",
            document.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException(DescribeAllocationFailure(response, environmentId));

        return response.ParseBody();
    }

    private static JArray ReadAllocationCollection(JToken parsed)
    {
        if (parsed == null) return new JArray();

        var array = parsed as JArray;
        if (array != null) return array;

        var envelope = parsed as JObject;
        if (envelope != null)
        {
            var value = envelope["value"] as JArray;
            if (value != null) return value;

            var collection = envelope["collection"] as JArray;
            if (collection != null) return collection;
        }

        return new JArray();
    }

    private static JArray SummarizeCurrencyAllocations(JObject document)
    {
        var summary = new JArray();
        var currencies = document?["currencyAllocations"] as JArray;
        if (currencies == null) return summary;

        foreach (var item in currencies)
        {
            var allocation = item as JObject;
            if (allocation == null) continue;

            var rules = new JObject();
            var ruleArray = allocation["enforcementRules"] as JArray;
            if (ruleArray != null)
            {
                foreach (var ruleItem in ruleArray)
                {
                    var rule = ruleItem as JObject;
                    var ruleType = rule?["ruleType"]?.ToString();
                    if (string.IsNullOrEmpty(ruleType)) continue;

                    rules[ruleType] = rule["enabled"];
                }
            }

            summary.Add(new JObject
            {
                ["currencyType"] = allocation["currencyType"],
                ["allocated"] = allocation["allocated"],
                ["autoAllocated"] = allocation["autoAllocated"],
                ["enforcementRules"] = rules
            });
        }

        return summary;
    }

    private static JObject FindCurrencyAllocation(JArray currencies, string currencyType)
    {
        if (currencies == null) return null;

        foreach (var item in currencies)
        {
            var allocation = item as JObject;
            if (allocation != null &&
                string.Equals(allocation["currencyType"]?.ToString(), currencyType, StringComparison.OrdinalIgnoreCase))
            {
                return allocation;
            }
        }

        return null;
    }

    private static JObject FindEnforcementRule(JArray rules, string ruleType)
    {
        if (rules == null) return null;

        foreach (var item in rules)
        {
            var rule = item as JObject;
            if (rule != null &&
                string.Equals(rule["ruleType"]?.ToString(), ruleType, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    // An absent currency or TenantPool rule means the environment draws from the pool.
    private static bool ReadTenantPoolEnabled(JObject document, string currencyType)
    {
        var currencies = document?["currencyAllocations"] as JArray;
        var allocation = FindCurrencyAllocation(currencies, currencyType);
        if (allocation == null) return true;

        var rule = FindEnforcementRule(allocation["enforcementRules"] as JArray, "TenantPool");
        if (rule == null) return true;

        var enabled = rule["enabled"];
        if (enabled == null || enabled.Type == JTokenType.Null) return true;

        return enabled.Value<bool>();
    }

    private static bool VerifyCurrencyMatches(JObject after, string currencyType, JObject expected)
    {
        if (after == null) return false;

        var actual = FindCurrencyAllocation(after["currencyAllocations"] as JArray, currencyType);
        if (actual == null) return false;

        var expectedAllocated = expected["allocated"];
        if (expectedAllocated != null && expectedAllocated.Type != JTokenType.Null)
        {
            var actualAllocated = actual["allocated"];
            if (actualAllocated == null || actualAllocated.Type == JTokenType.Null) return false;

            if (Math.Abs(actualAllocated.Value<double>() - expectedAllocated.Value<double>()) > 0.0001)
                return false;
        }

        var expectedRules = expected["enforcementRules"] as JArray;
        if (expectedRules != null)
        {
            var actualRules = actual["enforcementRules"] as JArray;

            foreach (var item in expectedRules)
            {
                var rule = item as JObject;
                var ruleType = rule?["ruleType"]?.ToString();
                if (string.IsNullOrEmpty(ruleType)) continue;

                var actualRule = FindEnforcementRule(actualRules, ruleType);
                if (actualRule == null) return false;

                var expectedEnabled = rule["enabled"];
                var actualEnabled = actualRule["enabled"];

                if (expectedEnabled == null || actualEnabled == null) return false;
                if (expectedEnabled.Value<bool>() != actualEnabled.Value<bool>()) return false;
            }
        }

        return true;
    }

    private static string RequireCurrencyType(string currencyType)
    {
        if (string.IsNullOrWhiteSpace(currencyType))
            throw new ArgumentException(
                "currencyType is required, for example MCSMessages, AI, or PowerPagesAuthenticated.");

        var trimmed = currencyType.Trim();

        foreach (var known in EXTERNAL_CURRENCY_TYPES)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase)) return known;
        }

        throw new ArgumentException(
            $"currencyType '{trimmed}' is not a documented ExternalCurrencyType. Expected one of: " +
            string.Join(", ", EXTERNAL_CURRENCY_TYPES) + ".");
    }

    private static string RequireEnforcementRuleType(string ruleType)
    {
        if (string.IsNullOrWhiteSpace(ruleType))
            throw new ArgumentException("Each enforcement rule requires a ruleType.");

        var trimmed = ruleType.Trim();

        foreach (var known in ENFORCEMENT_RULE_TYPES)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase)) return known;
        }

        throw new ArgumentException(
            $"ruleType '{trimmed}' is not a documented enforcement rule for the by-environment allocation " +
            "API. Expected one of: " + string.Join(", ", ENFORCEMENT_RULE_TYPES) + ".");
    }

    private static bool ReadRequiredBoolean(JToken value, string name)
    {
        if (value == null || value.Type == JTokenType.Null)
            throw new ArgumentException($"{name} is required and must be true or false.");

        if (value.Type == JTokenType.Boolean) return value.Value<bool>();

        bool parsed;
        if (value.Type == JTokenType.String && bool.TryParse(value.ToString(), out parsed)) return parsed;

        throw new ArgumentException($"{name} must be true or false.");
    }

    private static JToken CoerceAllocatedQuantity(JToken value)
    {
        if (value.Type == JTokenType.Integer) return value.Value<long>();

        double number;
        if (value.Type == JTokenType.Float)
        {
            number = value.Value<double>();
        }
        else if (!double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any,
                     System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            throw new ArgumentException("allocated must be a number.");
        }

        if (number < 0)
            throw new ArgumentException("allocated cannot be negative.");

        // The model declares allocated as int32, so an echoed 1000.0 can fail binding.
        if (Math.Abs(number - Math.Round(number)) < 0.0001) return (long)Math.Round(number);

        throw new ArgumentException("allocated must be a whole number.");
    }

    private static string DescribeAllocationFailure(AdminApiResponse response, string environmentId)
    {
        var scope = string.IsNullOrEmpty(environmentId) ? "" : $" for environment {environmentId}";

        if (response.Status == HttpStatusCode.Forbidden)
            return $"The licensing allocation API returned 403{scope}. This surface requires " +
                   "Licensing.Allocations.ReadWrite plus a Power Platform or Global administrator role.";

        if (response.Status == HttpStatusCode.NotFound)
            return $"The licensing allocation API returned 404{scope}. Verify the environment ID, and note " +
                   "that an environment with no allocation document cannot always be patched before one " +
                   "exists — create the allocation in the admin center first if this persists.";

        return $"The licensing allocation API returned {(int)response.Status} {response.ReasonPhrase}{scope}: " +
               TruncateForError(response.Body);
    }

    // ─── Resource Thresholds ────────────────────────────────────────────

    private async Task<JToken> HandleListResourceThresholds(JObject arguments)
    {
        var entitlementId = RequireEntitlementId(arguments["entitlementId"]?.ToString());
        var environmentFilter = NormalizeEnvironmentFilter(arguments["environmentId"]?.ToString());

        var thresholds = await FetchResourceThresholds(entitlementId).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(environmentFilter))
        {
            var filtered = new JArray();
            foreach (var threshold in thresholds)
            {
                if (string.Equals(threshold["environmentId"]?.ToString(), environmentFilter, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(threshold);
            }
            thresholds = filtered;
        }

        return new JObject
        {
            ["entitlementId"] = entitlementId,
            ["environmentId"] = string.IsNullOrEmpty(environmentFilter) ? (JToken)JValue.CreateNull() : environmentFilter,
            ["thresholdCount"] = thresholds.Count,
            ["thresholds"] = thresholds
        };
    }

    private async Task<JToken> HandleUpsertResourceThreshold(JObject arguments)
    {
        var envId = RequireEnvironmentGuid(arguments["environmentId"]?.ToString());
        var entitlementId = RequireEntitlementId(arguments["entitlementId"]?.ToString());
        var resourceId = arguments["resourceId"]?.ToString();
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("resourceId is required.");
        resourceId = resourceId.Trim();

        // The PUT replaces the whole threshold document, so seed the payload from the
        // current values and overlay only what the caller supplied.
        var existing = await TryFindResourceThreshold(entitlementId, envId, resourceId).ConfigureAwait(false);

        var payload = new JObject();
        if (existing != null)
        {
            foreach (var field in THRESHOLD_WRITABLE_FIELDS)
            {
                var current = existing[field];
                if (current != null && current.Type != JTokenType.Null)
                    payload[field] = NormalizeThresholdSeed(field, current);
            }
        }

        var applied = new JArray();
        foreach (var field in THRESHOLD_WRITABLE_FIELDS)
        {
            var supplied = arguments[field];
            if (supplied == null || supplied.Type == JTokenType.Null) continue;

            payload[field] = CoerceThresholdValue(field, supplied);
            applied.Add(field);
        }

        if (applied.Count == 0)
            throw new ArgumentException(
                "Supply at least one threshold field to set: limit, notificationThreshold, notifyIfOverCapacity, resourceConsumption, stopIfOverCapacity, or stopResource.");

        var response = await CallAdminApi(
            HttpMethod.Put,
            $"/licensing/environments/{envId}/entitlements/{Uri.EscapeDataString(entitlementId)}/resources/{Uri.EscapeDataString(resourceId)}/threshold?api-version={LICENSING_THRESHOLD_API_VERSION}",
            payload.ToString(Newtonsoft.Json.Formatting.None)
        ).ConfigureAwait(false);

        var threshold = string.IsNullOrWhiteSpace(response) ? null : JToken.Parse(response);

        return new JObject
        {
            ["status"] = "success",
            ["environmentId"] = envId,
            ["entitlementId"] = entitlementId,
            ["resourceId"] = resourceId,
            ["mergedWithExisting"] = existing != null,
            ["appliedFields"] = applied,
            ["threshold"] = threshold ?? payload,
            ["message"] = existing != null
                ? $"Updated the resource threshold for resource {resourceId} on entitlement {entitlementId}."
                : $"Created a resource threshold for resource {resourceId} on entitlement {entitlementId}. No previous threshold was found, so unspecified fields were left unset."
        };
    }

    private async Task<JArray> FetchResourceThresholds(string entitlementId)
    {
        var response = await CallAdminApi(
            HttpMethod.Get,
            $"/licensing/entitlements/{Uri.EscapeDataString(entitlementId)}/resourceThresholds?api-version={LICENSING_THRESHOLD_API_VERSION}"
        ).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response)) return new JArray();

        var parsed = JToken.Parse(response);

        var array = parsed as JArray;
        if (array != null) return array;

        var envelope = parsed as JObject;
        if (envelope != null)
        {
            var value = envelope["value"] as JArray;
            if (value != null) return value;
        }

        return new JArray();
    }

    private async Task<JObject> TryFindResourceThreshold(string entitlementId, string environmentId, string resourceId)
    {
        try
        {
            var thresholds = await FetchResourceThresholds(entitlementId).ConfigureAwait(false);

            foreach (var item in thresholds)
            {
                var candidate = item as JObject;
                if (candidate == null) continue;

                if (string.Equals(candidate["environmentId"]?.ToString(), environmentId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate["resourceId"]?.ToString(), resourceId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }
        catch (Exception ex)
        {
            // A caller that can write but not read must still be able to upsert,
            // so a failed read degrades to a create rather than failing the call.
            this.Context.Logger.LogInformation($"Could not read existing resource thresholds to merge: {ex.Message}");
            return null;
        }
    }

    private static string RequireEntitlementId(string entitlementId)
    {
        if (string.IsNullOrWhiteSpace(entitlementId))
            throw new ArgumentException("entitlementId is required, for example MCSMessages or MCSSessions.");

        var trimmed = entitlementId.Trim();
        foreach (var character in trimmed)
        {
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.')
                throw new ArgumentException("entitlementId may contain only letters, digits, hyphens, underscores, and periods.");
        }

        return trimmed;
    }

    // The read model returns limit and notificationThreshold as numbers with a decimal
    // component, while the write model declares them as int32, so an echoed 1000.0 can
    // fail model binding on the way back in.
    private static JToken NormalizeThresholdSeed(string field, JToken value)
    {
        if (field == "limit" || field == "notificationThreshold")
        {
            double number;
            if (TryReadDouble(value, out number) && number >= int.MinValue && number <= int.MaxValue)
                return (int)Math.Round(number);
        }

        return value.DeepClone();
    }

    private static JToken CoerceThresholdValue(string field, JToken value)    {
        switch (field)
        {
            case "limit":
            case "notificationThreshold":
            {
                int number;
                if (!TryReadInt(value, out number))
                    throw new ArgumentException($"{field} must be a whole number.");
                if (number < 0)
                    throw new ArgumentException($"{field} must be zero or greater.");
                if (field == "notificationThreshold" && number > 100)
                    throw new ArgumentException("notificationThreshold is a percentage and must be between 0 and 100.");
                return number;
            }

            case "resourceConsumption":
            {
                double number;
                if (!TryReadDouble(value, out number))
                    throw new ArgumentException("resourceConsumption must be a number.");
                if (number < 0)
                    throw new ArgumentException("resourceConsumption must be zero or greater.");
                return number;
            }

            default:
            {
                bool flag;
                if (!TryReadBool(value, out flag))
                    throw new ArgumentException($"{field} must be true or false.");
                return flag;
            }
        }
    }

    private static bool TryReadInt(JToken token, out int value)
    {
        value = 0;
        if (token == null) return false;

        if (token.Type == JTokenType.Integer)
        {
            value = token.Value<int>();
            return true;
        }

        if (token.Type == JTokenType.Float)
        {
            var raw = token.Value<double>();
            if (raw != Math.Floor(raw)) return false;
            value = (int)raw;
            return true;
        }

        if (token.Type == JTokenType.String)
        {
            return int.TryParse(
                token.ToString().Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }

    private static bool TryReadDouble(JToken token, out double value)
    {
        value = 0;
        if (token == null) return false;

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            value = token.Value<double>();
            return true;
        }

        if (token.Type == JTokenType.String)
        {
            return double.TryParse(
                token.ToString().Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }

    private static bool TryReadBool(JToken token, out bool value)
    {
        value = false;
        if (token == null) return false;

        if (token.Type == JTokenType.Boolean)
        {
            value = token.Value<bool>();
            return true;
        }

        if (token.Type == JTokenType.String)
            return bool.TryParse(token.ToString().Trim(), out value);

        return false;
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

    // Captures status and the long-running-operation headers alongside the body. The
    // previous body-only helper discarded Location and Retry-After, which made any
    // header-driven async contract impossible to implement on top of it.
    private class AdminApiResponse
    {
        public HttpStatusCode Status;
        public string ReasonPhrase;
        public string Body;
        public string Location;
        public string OperationLocation;
        public string AzureAsyncOperation;
        public int? RetryAfterSeconds;

        public bool IsSuccess { get { return (int)Status >= 200 && (int)Status < 300; } }

        public JToken ParseBody()
        {
            if (string.IsNullOrWhiteSpace(Body)) return null;
            try { return JToken.Parse(Body); } catch (JsonException) { return null; }
        }
    }

    private async Task<AdminApiResponse> CallAdminApiRaw(HttpMethod method, string path, string body = null)
    {
        var request = new HttpRequestMessage(method, $"{ADMIN_API_BASE}{path}");

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

        var result = new AdminApiResponse
        {
            Status = response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            Body = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false)
        };

        string header;
        if (TryReadHeader(response, "Location", out header)) result.Location = header;
        if (TryReadHeader(response, "Operation-Location", out header)) result.OperationLocation = header;
        if (TryReadHeader(response, "Azure-AsyncOperation", out header)) result.AzureAsyncOperation = header;

        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                result.RetryAfterSeconds = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
            }
            else if (response.Headers.RetryAfter.Date.HasValue)
            {
                result.RetryAfterSeconds =
                    (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }
        }

        if (!result.IsSuccess)
        {
            this.Context.Logger.LogError($"Admin API error {result.Status} for {path}: {TruncateForError(result.Body)}");
        }

        return result;
    }

    // Throwing wrapper kept for the many call sites that only need the body and treat
    // any non-success status as fatal.
    private async Task<string> CallAdminApi(HttpMethod method, string path, string body = null)
    {
        var response = await CallAdminApiRaw(method, path, body).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Power Platform Admin API returned {(int)response.Status} {response.ReasonPhrase}: {TruncateForError(response.Body)}"
            );
        }

        return response.Body;
    }

    private static bool TryReadHeader(HttpResponseMessage response, string name, out string value)
    {
        value = null;
        IEnumerable<string> values;

        if (response.Headers.TryGetValues(name, out values))
        {
            value = values.FirstOrDefault();
        }
        else if (response.Content != null && response.Content.Headers.TryGetValues(name, out values))
        {
            value = values.FirstOrDefault();
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string RequireEnvironmentGuid(string environmentId)
    {
        Guid parsed;
        if (string.IsNullOrEmpty(environmentId) || !Guid.TryParse(environmentId, out parsed))
            throw new ArgumentException("environmentId is required and must be a GUID.");
        return parsed.ToString();
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

    private async Task<HttpResponseMessage> HandleTypedListEnvironmentAllocations()
    {
        try
        {
            var result = await HandleListEnvironmentAllocations(new JObject()).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

    private async Task<HttpResponseMessage> HandleTypedGetEnvironmentAllocations()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrWhiteSpace(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        try
        {
            var result = await HandleGetEnvironmentAllocations(
                new JObject { ["environmentId"] = envId }).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

    private async Task<HttpResponseMessage> HandleTypedUpdateEnvironmentAllocation()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrWhiteSpace(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject bodyObj;
        try
        {
            bodyObj = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            return CreateTypedErrorResponse($"Request body is not valid JSON: {ex.Message}", 400);
        }

        var args = new JObject { ["environmentId"] = envId };

        // A maker who placed this action in a designer has already made the decision
        // explicitly, so the typed surface does not carry the MCP confirm gate.
        foreach (var field in new[] { "currencyType", "allocated", "enforcementRules" })
        {
            var supplied = bodyObj[field];
            if (supplied != null && supplied.Type != JTokenType.Null) args[field] = supplied;
        }

        try
        {
            var result = await HandleUpdateEnvironmentAllocation(args).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

    private async Task<HttpResponseMessage> HandleTypedGetAllocationAvailability()
    {
        var args = new JObject();

        var filter = GetQueryParam("filter");
        if (!string.IsNullOrWhiteSpace(filter)) args["filter"] = filter;

        try
        {
            var result = await HandleGetAllocationAvailability(args).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

    private async Task<HttpResponseMessage> HandleTypedGetReservedEntitlements()
    {
        var args = new JObject();

        var filter = GetQueryParam("filter");
        if (!string.IsNullOrWhiteSpace(filter)) args["filter"] = filter;

        try
        {
            var result = await HandleGetReservedEntitlements(args).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

    private async Task<HttpResponseMessage> HandleTypedListResourceThresholds()
    {
        var entitlementId = GetQueryParam("entitlementId");
        if (string.IsNullOrWhiteSpace(entitlementId))
            return CreateTypedErrorResponse("entitlementId query parameter is required.", 400);

        var args = new JObject { ["entitlementId"] = entitlementId };

        var envId = GetQueryParam("environmentId");
        if (!string.IsNullOrWhiteSpace(envId)) args["environmentId"] = envId;

        try
        {
            var result = await HandleListResourceThresholds(args).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
    }

    private async Task<HttpResponseMessage> HandleTypedUpsertResourceThreshold()
    {
        var envId = GetQueryParam("environmentId");
        if (string.IsNullOrWhiteSpace(envId))
            return CreateTypedErrorResponse("environmentId query parameter is required.", 400);

        var entitlementId = GetQueryParam("entitlementId");
        if (string.IsNullOrWhiteSpace(entitlementId))
            return CreateTypedErrorResponse("entitlementId query parameter is required.", 400);

        var resourceId = GetQueryParam("resourceId");
        if (string.IsNullOrWhiteSpace(resourceId))
            return CreateTypedErrorResponse("resourceId query parameter is required.", 400);

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        JObject bodyObj;
        try
        {
            bodyObj = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            return CreateTypedErrorResponse($"Request body is not valid JSON: {ex.Message}", 400);
        }

        var args = new JObject
        {
            ["environmentId"] = envId,
            ["entitlementId"] = entitlementId,
            ["resourceId"] = resourceId
        };

        foreach (var field in THRESHOLD_WRITABLE_FIELDS)
        {
            var supplied = bodyObj[field];
            if (supplied != null && supplied.Type != JTokenType.Null) args[field] = supplied;
        }

        try
        {
            var result = await HandleUpsertResourceThreshold(args).ConfigureAwait(false);
            return CreateTypedResponse(result);
        }
        catch (ArgumentException ex)
        {
            return CreateTypedErrorResponse(ex.Message, 400);
        }
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

    private static string TruncateForError(string text, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "... (truncated)";
    }
}




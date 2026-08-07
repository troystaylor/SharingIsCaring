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


// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Copilot Package Management MCP — Microsoft Graph beta Package Management  ║
// ║  API as MCP tools. Manage agents and apps in the M365 tenant catalog.      ║
// ║  Tool registration uses the fluent AddTool API.                            ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string GraphBaseUrl = "https://graph.microsoft.com";

    // List and get are GA on v1.0; block/unblock/reassign/update are beta-only.
    private const string ReadPackagesBasePath = "/v1.0/copilot/admin/catalog/packages";
    private const string PackagesBasePath = "/beta/copilot/admin/catalog/packages";
    private const string AgentRegistrationsBasePath = "/beta/copilot/agentRegistrations";
    private const string ReportsBasePath = "/v1.0/copilot/reports";
    private const string LimitedModePath = "/v1.0/copilot/admin/settings/limitedMode";

    private const int MaxPages = 50;

    private static readonly string[] ReportPeriods = { "D7", "D28", "D30", "D90", "D180", "ALL" };
    private static readonly string[] ReportVersions = { "v1", "v2" };

    // ── Server Configuration ─────────────────────────────────────────────

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "copilot-package-management-mcp",
            Version = "1.0.0",
            Title = "Copilot Package Management MCP",
            Description = "Manage Microsoft 365 Copilot agents and apps. List, inspect, block, unblock, update access, and reassign ownership of packages in the tenant catalog."
        },
        ProtocolVersion = "2025-11-25",
        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = false,
            Prompts = false,
            Logging = true,
            Completions = false
        },
        Instructions = "Use this server to manage Microsoft 365 Copilot agents and apps as an IT administrator. You can list all packages (agents and apps) in the tenant, get detailed information about a specific package, block or unblock packages, update access assignments, and reassign package ownership. This API requires a Microsoft Agent 365 license. All operations use the Microsoft Graph beta endpoint."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (this.Context.OperationId != "InvokeMCP")
            return await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);

        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        var handler = new McpRequestHandler(Options);
        RegisterTools(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(body, this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }


    // ── Tool Registration ────────────────────────────────────────────────

    private void RegisterTools(McpRequestHandler handler)
    {
        // ── List Packages ────────────────────────────────────────────────

        handler.AddTool("list_packages", "List all Copilot agents and apps in the tenant catalog, following pagination automatically. Optionally filter by supported host (Copilot, Outlook, Teams, M365), element type (Bots, DeclarativeAgent, CustomEngineAgent), platform (Copilot Studio, Microsoft 365 Copilot Agent Builder), publisher type, or last modified date.",
            schema: s => s
                .String("supportedHost", "Filter by host: Copilot, Outlook, Teams, or M365")
                .String("elementType", "Filter by element type: Bots, DeclarativeAgent, CustomEngineAgent, OfficeAddIns")
                .String("platform", "Filter by platform: 'Copilot Studio' or 'Microsoft 365 Copilot Agent Builder'")
                .String("lastModifiedAfter", "Filter packages modified after this ISO 8601 date (e.g. 2026-01-01T00:00:00Z)")
                .String("publisherType", "Comma-separated package types to keep, applied after retrieval because type is not filterable server-side: microsoft (built by Microsoft), external (built by partners), shared (shared in your organization), custom (built by your organization). Omit to return all."),
            handler: async (args, ct) =>
            {
                var filters = new List<string>();

                var host = GetArgument(args, "supportedHost");
                if (host != null)
                    filters.Add($"supportedHosts/any(h:h eq '{host}')");

                var elementType = GetArgument(args, "elementType");
                if (elementType != null)
                    filters.Add($"elementTypes/any(h:h eq '{elementType}')");

                var platform = GetArgument(args, "platform");
                if (platform != null)
                    filters.Add($"platform eq '{platform}'");

                var modifiedAfter = GetArgument(args, "lastModifiedAfter");
                if (modifiedAfter != null)
                    filters.Add($"lastModifiedDateTime gt {modifiedAfter}");

                var path = ReadPackagesBasePath;
                if (filters.Count > 0)
                    path += "?$filter=" + Uri.EscapeDataString(string.Join(" and ", filters));

                var packages = await GetAllPagesAsync(path);

                var publisherType = GetArgument(args, "publisherType");
                if (!string.IsNullOrWhiteSpace(publisherType))
                {
                    var wanted = publisherType.Split(',')
                        .Select(t => t.Trim())
                        .Where(t => t.Length > 0)
                        .ToList();

                    var kept = packages.Where(p =>
                        wanted.Any(w => string.Equals(p["type"]?.ToString(), w, StringComparison.OrdinalIgnoreCase)));

                    packages = new JArray(kept);
                }

                return new JObject { ["count"] = packages.Count, ["value"] = packages };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Get Package Details ──────────────────────────────────────────

        handler.AddTool("get_package_details", "Get detailed metadata for a specific Copilot package including element details, categories, sensitivity, and access assignments.",
            schema: s => s
                .String("packageId", "The unique identifier of the package", required: true),
            handler: async (args, ct) =>
            {
                var packageId = RequireArgument(args, "packageId");
                return await SendGraphRequestAsync("GET", $"{ReadPackagesBasePath}/{packageId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Block Package ────────────────────────────────────────────────

        handler.AddTool("block_package", "Block a Copilot package to prevent its usage across the organization.",
            schema: s => s
                .String("packageId", "The unique identifier of the package to block", required: true),
            handler: async (args, ct) =>
            {
                var packageId = RequireArgument(args, "packageId");
                return await SendGraphRequestAsync("POST", $"{PackagesBasePath}/{packageId}/block");
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        // ── Unblock Package ──────────────────────────────────────────────

        handler.AddTool("unblock_package", "Unblock a Copilot package to allow its usage across the organization.",
            schema: s => s
                .String("packageId", "The unique identifier of the package to unblock", required: true),
            handler: async (args, ct) =>
            {
                var packageId = RequireArgument(args, "packageId");
                return await SendGraphRequestAsync("POST", $"{PackagesBasePath}/{packageId}/unblock");
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        // ── Reassign Package ─────────────────────────────────────────────

        handler.AddTool("reassign_package", "Reassign ownership of a Copilot package to a different user. Use when an employee leaves the organization.",
            schema: s => s
                .String("packageId", "The unique identifier of the package to reassign", required: true)
                .String("userId", "The user ID of the new package owner", required: true),
            handler: async (args, ct) =>
            {
                var packageId = RequireArgument(args, "packageId");
                var userId = RequireArgument(args, "userId");
                var body = new JObject { ["userId"] = userId };
                return await SendGraphRequestAsync("POST", $"{PackagesBasePath}/{packageId}/reassign", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── Update Package ───────────────────────────────────────────────

        handler.AddTool("update_package_access", "Update the allowed and acquired users and groups for a package to control availability and deployment scope.",
            schema: s => s
                .String("packageId", "The unique identifier of the package to update", required: true)
                .Array("allowedUsersAndGroups", "Users and groups for whom the package should be available. Each item needs resourceType (user or group) and resourceId.",
                    new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["resourceType"] = new JObject { ["type"] = "string", ["description"] = "Type: user or group" },
                            ["resourceId"] = new JObject { ["type"] = "string", ["description"] = "The ID of the user or group" }
                        },
                        ["required"] = new JArray { "resourceType", "resourceId" }
                    })
                .Array("acquireUsersAndGroups", "Users and groups for whom the package should be deployed. Each item needs resourceType (user or group) and resourceId.",
                    new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["resourceType"] = new JObject { ["type"] = "string", ["description"] = "Type: user or group" },
                            ["resourceId"] = new JObject { ["type"] = "string", ["description"] = "The ID of the user or group" }
                        },
                        ["required"] = new JArray { "resourceType", "resourceId" }
                    }),
            handler: async (args, ct) =>
            {
                var packageId = RequireArgument(args, "packageId");
                var body = new JObject();

                if (args["allowedUsersAndGroups"] != null)
                    body["allowedUsersAndGroups"] = args["allowedUsersAndGroups"];

                if (args["acquireUsersAndGroups"] != null)
                    body["acquireUsersAndGroups"] = args["acquireUsersAndGroups"];

                return await SendGraphRequestAsync("PATCH", $"{PackagesBasePath}/{packageId}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        // ── Create Agent Registration ────────────────────────────────────

        handler.AddTool("create_agent_registration", "Register an agent in the Agent 365 registry so administrators can discover and govern it. Supply an agent card manifest describing the agent's provider, capabilities, and skills.",
            schema: s => s
                .String("displayName", "Display name for the agent instance", required: true)
                .String("createdBy", "Identifier of the user or app creating the registration", required: true)
                .String("sourceCreatedDateTime", "ISO 8601 date the agent was created in the source system", required: true)
                .String("sourceLastModifiedDateTime", "ISO 8601 date the agent was last modified in the source system", required: true)
                .String("description", "Overview of the agent's purpose and capabilities")
                .Array("ownerIds", "Owner object IDs for the agent. Either this or managedByAppId is required.",
                    new JObject { ["type"] = "string" })
                .String("managedByAppId", "Application identifier managing this agent. Alternative to ownerIds.")
                .String("sourceAgentId", "Original agent identifier from the source system")
                .String("originatingStore", "Name of the store or system where the agent originated")
                .String("agentIdentityId", "Microsoft Entra agent identity identifier")
                .String("agentIdentityBlueprintId", "Agent identity blueprint identifier")
                .Object("agentCard", "Agent card manifest following the public manifest specification", nested => nested
                    .String("name", "Agent name")
                    .String("version", "Agent card version")
                    .String("description", "Agent description")
                    .String("provider", "Organization providing the agent")),
            handler: async (args, ct) =>
            {
                var body = BuildAgentRegistrationBody(args, requireCreateFields: true);
                return await SendGraphRequestAsync("POST", AgentRegistrationsBasePath, body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── Get Agent Registration ───────────────────────────────────────

        handler.AddTool("get_agent_registration", "Get the properties of a specific agent registration, including its agent card, owners, and Entra agent identity references.",
            schema: s => s
                .String("registrationId", "The unique identifier of the agent registration", required: true),
            handler: async (args, ct) =>
            {
                var registrationId = RequireArgument(args, "registrationId");
                return await SendGraphRequestAsync("GET", $"{AgentRegistrationsBasePath}/{registrationId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Update Agent Registration ────────────────────────────────────

        handler.AddTool("update_agent_registration", "Update an existing agent registration. Only the supplied properties are changed.",
            schema: s => s
                .String("registrationId", "The unique identifier of the agent registration", required: true)
                .String("displayName", "New display name for the agent instance")
                .String("description", "New description for the agent")
                .Array("ownerIds", "Replacement list of owner object IDs", new JObject { ["type"] = "string" })
                .String("managedByAppId", "Application identifier managing this agent")
                .String("sourceAgentId", "Original agent identifier from the source system")
                .String("originatingStore", "Name of the store or system where the agent originated")
                .String("agentIdentityId", "Microsoft Entra agent identity identifier")
                .String("agentIdentityBlueprintId", "Agent identity blueprint identifier")
                .String("sourceLastModifiedDateTime", "ISO 8601 date the agent was last modified in the source system"),
            handler: async (args, ct) =>
            {
                var registrationId = RequireArgument(args, "registrationId");
                var body = BuildAgentRegistrationBody(args, requireCreateFields: false);
                return await SendGraphRequestAsync("PATCH", $"{AgentRegistrationsBasePath}/{registrationId}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        // ── Delete Agent Registration ────────────────────────────────────

        handler.AddTool("delete_agent_registration", "Delete an agent registration that is no longer needed. This removes the agent from the Agent 365 registry.",
            schema: s => s
                .String("registrationId", "The unique identifier of the agent registration", required: true),
            handler: async (args, ct) =>
            {
                var registrationId = RequireArgument(args, "registrationId");
                return await SendGraphRequestAsync("DELETE", $"{AgentRegistrationsBasePath}/{registrationId}");
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        // ── Usage Reports ────────────────────────────────────────────────

        handler.AddTool("get_copilot_user_count_summary", "Get the aggregated number of active and enabled Microsoft 365 Copilot users for a time period, broken down by app.",
            schema: s => s
                .String("period", "Aggregation window: D7, D28, D30, D90, D180, or ALL. D30 is v1 only; D28 is v2 only.", enumValues: ReportPeriods)
                .String("version", "Report version: v1 or v2. v2 adds prompt counts and Copilot Chat breakdowns.", enumValues: ReportVersions),
            handler: async (args, ct) => await GetReportAsync("getMicrosoft365CopilotUserCountSummary", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_copilot_user_count_trend", "Get the daily trend in active and enabled Microsoft 365 Copilot users for a time period.",
            schema: s => s
                .String("period", "Aggregation window: D7, D28, D30, D90, D180, or ALL", enumValues: ReportPeriods)
                .String("version", "Report version: v1 or v2", enumValues: ReportVersions),
            handler: async (args, ct) => await GetReportAsync("getMicrosoft365CopilotUserCountTrend", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_copilot_usage_user_detail", "Get per-user Microsoft 365 Copilot activity. Use version v2 to include Copilot Agent Last Activity Date, the only per-user agent usage signal in Graph.",
            schema: s => s
                .String("period", "Aggregation window: D7, D28, D30, D90, D180, or ALL", enumValues: ReportPeriods)
                .String("version", "Report version: v1 or v2. Use v2 for agent activity.", enumValues: ReportVersions),
            handler: async (args, ct) => await GetReportAsync("getMicrosoft365CopilotUsageUserDetail", args),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Limited Mode Setting ─────────────────────────────────────────

        handler.AddTool("get_limited_mode", "Read whether Microsoft 365 Copilot in Teams meetings is restricted from responding to sentiment-related prompts.",
            schema: s => { },
            handler: async (args, ct) => await SendGraphRequestAsync("GET", LimitedModePath),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_limited_mode", "Enable or disable limited mode for Microsoft 365 Copilot in Teams meetings, scoped to a Microsoft Entra group.",
            schema: s => s
                .Boolean("isEnabledForGroup", "True to stop Copilot responding to prompts inferring emotions, behavior, or judgments", required: true)
                .String("groupId", "Microsoft Entra group the setting applies to. Required when enabling."),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["isEnabledForGroup"] = args["isEnabledForGroup"] };

                var groupId = GetArgument(args, "groupId");
                if (groupId != null)
                    body["groupId"] = groupId;

                return await SendGraphRequestAsync("PATCH", LimitedModePath, body);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });
    }


    // ── Agent Registration Helper ────────────────────────────────────────

    private static JObject BuildAgentRegistrationBody(JObject args, bool requireCreateFields)
    {
        var fields = new[]
        {
            "displayName", "description", "createdBy", "sourceCreatedDateTime", "sourceLastModifiedDateTime",
            "managedByAppId", "sourceAgentId", "originatingStore", "agentIdentityId", "agentIdentityBlueprintId"
        };

        var body = new JObject();

        foreach (var field in fields)
        {
            var value = GetArgument(args, field);
            if (value != null)
                body[field] = value;
        }

        if (args["ownerIds"] != null)
            body["ownerIds"] = args["ownerIds"];

        if (args["agentCard"] != null)
            body["agentCard"] = args["agentCard"];

        if (requireCreateFields)
        {
            foreach (var required in new[] { "displayName", "createdBy", "sourceCreatedDateTime", "sourceLastModifiedDateTime" })
                RequireArgument(args, required);

            if (body["ownerIds"] == null && body["managedByAppId"] == null)
                throw new ArgumentException("Either 'ownerIds' or 'managedByAppId' is required");
        }

        return body;
    }


    // ── Reports Helper ───────────────────────────────────────────────────

    private async Task<JObject> GetReportAsync(string function, JObject args)
    {
        var period = GetArgument(args, "period") ?? "D7";
        var version = GetArgument(args, "version") ?? "v1";

        var path = $"{ReportsBasePath}/{function}(period='{period}',version='{version}')?$format=application/json";
        return await SendGraphRequestAsync("GET", path);
    }


    // ── Graph API Helper ─────────────────────────────────────────────────

    private async Task<JArray> GetAllPagesAsync(string path)
    {
        var results = new JArray();
        var next = path;

        for (var page = 0; page < MaxPages && !string.IsNullOrEmpty(next); page++)
        {
            var response = await SendGraphRequestAsync("GET", next);

            var values = response["value"] as JArray;
            if (values != null)
                foreach (var item in values)
                    results.Add(item);

            next = response["@odata.nextLink"]?.ToString();
        }

        return results;
    }

    private async Task<JObject> SendGraphRequestAsync(string method, string path, JObject body = null)
    {
        // @odata.nextLink comes back absolute; everything else is a relative path.
        var url = path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? path : GraphBaseUrl + path;
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        if (body != null)
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var errorMsg = $"Graph API returned {statusCode}";
            try
            {
                var errorObj = JObject.Parse(content);
                var graphError = errorObj["error"]?["message"]?.ToString();
                if (!string.IsNullOrEmpty(graphError))
                    errorMsg = $"Graph API {statusCode}: {graphError}";
            }
            catch { }
            throw new McpException(McpErrorCode.InternalError, errorMsg);
        }

        if (string.IsNullOrWhiteSpace(content) || response.StatusCode == HttpStatusCode.NoContent)
            return new JObject { ["status"] = "success" };

        // Usage reports can come back as raw CSV rather than JSON.
        try
        {
            return JObject.Parse(content);
        }
        catch (JsonReaderException)
        {
            return new JObject { ["contentType"] = "text/csv", ["content"] = content };
        }
    }


    // ── Utility Helpers ──────────────────────────────────────────────────

    private static string RequireArgument(JObject args, string name)
    {
        var val = args[name]?.ToString();
        if (string.IsNullOrWhiteSpace(val))
            throw new ArgumentException($"'{name}' is required");
        return val;
    }

    private static string GetArgument(JObject args, string name)
    {
        return args[name]?.ToString();
    }


    // ── Application Insights Logging ─────────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK                                                    ║
// ║                                                                              ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power         ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section    ║
// ║  becomes a using statement instead of inline code.                           ║
// ║                                                                              ║
// ║  Spec coverage: MCP 2025-11-25                                               ║
// ║  Handles: initialize, ping, tools/*, resources/*, prompts/*,                 ║
// ║           completion/complete, logging/setLevel, all notifications           ║
// ║                                                                              ║
// ║  Stateless limitations (Power Platform cannot send async notifications):     ║
// ║   - Tasks (experimental, requires persistent state between requests)         ║
// ║   - Server→client requests (sampling, elicitation, roots/list)               ║
// ║   - Server→client notifications (progress, logging/message, list_changed)    ║
// ║                                                                              ║
// ║  Do not modify unless extending the framework itself.                        ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported in initialize response.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }
}

/// <summary>Capabilities declared during initialization.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Logging { get; set; }
    public bool Completions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();
    public string ProtocolVersion { get; set; } = "2025-11-25";
    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();
    public string Instructions { get; set; }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>Standard JSON-RPC 2.0 error codes used by MCP.</summary>
public enum McpErrorCode
{
    RequestTimeout = -32000,
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603
}

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>Fluent builder for JSON Schema objects used in tool inputSchema.</summary>
public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };
        if (_required.Count > 0) schema["required"] = _required;
        return schema;
    }
}

// ── Internal Tool Registration ───────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;
    private readonly Dictionary<string, McpToolDefinition> _tools;

    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    // ── Tool Registration ────────────────────────────────────────────────

    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchema = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObj = null;
        if (annotations != null)
        {
            annotationsObj = new JObject();
            annotations(annotationsObj);
        }

        JObject outputSchemaObj = null;
        if (outputSchema != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchema(outBuilder);
            outputSchemaObj = outBuilder.Build();
        }

        _tools[name] = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchemaObj,
            Annotations = annotationsObj,
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        return this;
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    public async Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

        var method = request.Value<string>("method") ?? string.Empty;
        var id = request["id"];

        Log("McpRequestReceived", new { Method = method, HasId = id != null });

        try
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, request);

                case "initialized":
                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return SerializeSuccess(id, new JObject());

                case "ping":
                    return SerializeSuccess(id, new JObject());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request, cancellationToken).ConfigureAwait(false);

                case "resources/list":
                    return SerializeSuccess(id, new JObject { ["resources"] = new JArray() });

                case "resources/templates/list":
                    return SerializeSuccess(id, new JObject { ["resourceTemplates"] = new JArray() });

                case "resources/read":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Resource not found");

                case "resources/subscribe":
                case "resources/unsubscribe":
                    return SerializeSuccess(id, new JObject());

                case "prompts/list":
                    return SerializeSuccess(id, new JObject { ["prompts"] = new JArray() });

                case "prompts/get":
                    return SerializeError(id, McpErrorCode.InvalidParams, "Prompt not found");

                case "completion/complete":
                    return SerializeSuccess(id, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                case "logging/setLevel":
                    return SerializeSuccess(id, new JObject());

                default:
                    Log("McpMethodNotFound", new { Method = method });
                    return SerializeError(id, McpErrorCode.MethodNotFound, "Method not found", method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = method, Error = ex.Message });
            return SerializeError(id, McpErrorCode.InternalError, ex.Message);
        }
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    private string HandleInitialize(JToken id, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString()
            ?? _options.ProtocolVersion;

        var capabilities = new JObject();
        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Resources)
            capabilities["resources"] = new JObject { ["subscribe"] = false, ["listChanged"] = false };
        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };
        if (_options.Capabilities.Logging)
            capabilities["logging"] = new JObject();
        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;

        var result = new JObject
        {
            ["protocolVersion"] = clientProtocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = serverInfo
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = clientProtocolVersion
        });

        return SerializeSuccess(id, result);
    }

    private string HandleToolsList(JToken id)
    {
        var toolsArray = new JArray();
        foreach (var tool in _tools.Values)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _tools.Count });
        return SerializeSuccess(id, new JObject { ["tools"] = toolsArray });
    }

    private async Task<string> HandleToolsCallAsync(JToken id, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            JObject callResult;

            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };
                if (jobj["structuredContent"] is JObject structured)
                    callResult["structuredContent"] = structured;
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(id, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    // ── Content Helpers ──────────────────────────────────────────────────

    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    public static JObject ToolResult(JArray content, JObject structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(JToken id, JObject result)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, string data = null)
    {
        return SerializeError(id, (int)code, message, data);
    }

    private string SerializeError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrWhiteSpace(data))
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

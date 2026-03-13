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
// ║  SECTION 1: CONNECTOR ENTRY POINT                                            ║
// ║                                                                              ║
// ║  Cloud Licensing MCP — all Graph cloudLicensing API beta endpoints as        ║
// ║  MCP tools for allotments, assignments, usage rights, errors, and            ║
// ║  waiting members.                                                            ║
// ║  Tool registration uses the fluent AddTool API.                              ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string GraphBaseUrl = "https://graph.microsoft.com";

    // ── Server Configuration ─────────────────────────────────────────────

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "cloud-licensing-mcp",
            Version = "1.0.0",
            Title = "Cloud Licensing MCP",
            Description = "Manage Microsoft Cloud Licensing — allotments, assignments, usage rights, assignment errors, and waiting members via the Graph cloudLicensing API (preview)."
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
        Instructions = "Use this server to manage Microsoft Cloud Licensing. You can list and inspect allotments (license pools), create/update/delete assignments, check usage rights for users and groups, troubleshoot assignment errors, and view waiting members. All endpoints use the Graph beta API (preview). Permissions required: CloudLicensing.ReadWrite.All, User.Read.All, Group.Read.All, Directory.Read.All."
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
        // ── Allotments ──────────────────────────────────────────────────

        handler.AddTool("list_allotments", "List all license allotments in the organization. Returns allotted/consumed units, SKU info, services, and subscriptions.",
            schema: s =>
            {
                s.Integer("top", "Maximum number of allotments to return");
                s.String("filter", "OData filter expression (e.g., skuId eq 'GUID')");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top", "filter");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/allotments{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_allotment", "Get details of a specific license allotment including allotted/consumed units, services, SKU, and subscriptions.",
            schema: s => s.String("allotmentId", "The unique identifier of the allotment", required: true),
            handler: async (args, ct) =>
            {
                var allotmentId = RequireArgument(args, "allotmentId");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/allotments/{allotmentId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Allotment Assignments ───────────────────────────────────────

        handler.AddTool("list_allotment_assignments", "List license assignments that consume licenses from a specific allotment.",
            schema: s =>
            {
                s.String("allotmentId", "The unique identifier of the allotment", required: true);
                s.Integer("top", "Maximum number of assignments to return");
            },
            handler: async (args, ct) =>
            {
                var allotmentId = RequireArgument(args, "allotmentId");
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/allotments/{allotmentId}/assignments{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("create_allotment_assignment", "Create a new license assignment for an allotment. Assigns licenses to a user or group.",
            schema: s =>
            {
                s.String("allotmentId", "The unique identifier of the allotment", required: true);
                s.String("assignedToUri", "The Graph URI of the user or group (e.g., https://graph.microsoft.com/beta/users/{userId})", required: true);
                s.Array("disabledServicePlanIds", "List of service plan GUIDs to disable. Use empty array to enable all.",
                    new JObject { ["type"] = "string" });
            },
            handler: async (args, ct) =>
            {
                var allotmentId = RequireArgument(args, "allotmentId");
                var assignedToUri = RequireArgument(args, "assignedToUri");
                var disabled = args["disabledServicePlanIds"] as JArray ?? new JArray();
                var body = new JObject
                {
                    ["@odata.type"] = "#microsoft.graph.cloudLicensing.assignment",
                    ["assignedTo@odata.bind"] = assignedToUri,
                    ["disabledServicePlanIds"] = disabled
                };
                return await SendGraphRequestAsync("POST", $"/beta/admin/cloudLicensing/allotments/{allotmentId}/assignments", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── Allotment Waiting Members ───────────────────────────────────

        handler.AddTool("list_allotment_waiting_members", "List over-assigned users in the waiting room for an allotment due to license capacity limits.",
            schema: s =>
            {
                s.String("allotmentId", "The unique identifier of the allotment", required: true);
                s.Integer("top", "Maximum number of waiting members to return");
            },
            handler: async (args, ct) =>
            {
                var allotmentId = RequireArgument(args, "allotmentId");
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/allotments/{allotmentId}/waitingMembers{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_allotment_waiting_member", "Get details of a specific waiting member for an allotment.",
            schema: s =>
            {
                s.String("allotmentId", "The unique identifier of the allotment", required: true);
                s.String("waitingMemberId", "The unique identifier of the waiting member", required: true);
            },
            handler: async (args, ct) =>
            {
                var allotmentId = RequireArgument(args, "allotmentId");
                var waitingMemberId = RequireArgument(args, "waitingMemberId");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/allotments/{allotmentId}/waitingMembers/{waitingMemberId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Org-Level Assignments ───────────────────────────────────────

        handler.AddTool("list_assignments", "List all license assignments in the organization.",
            schema: s =>
            {
                s.Integer("top", "Maximum number of assignments to return");
                s.String("filter", "OData filter expression");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top", "filter");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/assignments{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_assignment", "Get details of a specific license assignment including disabled service plans.",
            schema: s => s.String("assignmentId", "The unique identifier of the assignment", required: true),
            handler: async (args, ct) =>
            {
                var assignmentId = RequireArgument(args, "assignmentId");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/assignments/{assignmentId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("update_assignment", "Update an assignment to enable or disable services. Services not in disabledServicePlanIds are enabled.",
            schema: s =>
            {
                s.String("assignmentId", "The unique identifier of the assignment", required: true);
                s.Array("disabledServicePlanIds", "List of service plan GUIDs to disable. Empty array enables all.",
                    new JObject { ["type"] = "string" }, required: true);
            },
            handler: async (args, ct) =>
            {
                var assignmentId = RequireArgument(args, "assignmentId");
                var disabled = args["disabledServicePlanIds"] as JArray ?? new JArray();
                var body = new JObject
                {
                    ["@odata.type"] = "#microsoft.graph.cloudLicensing.assignment",
                    ["disabledServicePlanIds"] = disabled
                };
                return await SendGraphRequestAsync("PATCH", $"/beta/admin/cloudLicensing/assignments/{assignmentId}", body);
            },
            annotations: a => { a["readOnlyHint"] = false; });

        handler.AddTool("delete_assignment", "Delete a license assignment.",
            schema: s => s.String("assignmentId", "The unique identifier of the assignment", required: true),
            handler: async (args, ct) =>
            {
                var assignmentId = RequireArgument(args, "assignmentId");
                return await SendGraphRequestAsync("DELETE", $"/beta/admin/cloudLicensing/assignments/{assignmentId}");
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── Assignment Errors ───────────────────────────────────────────

        handler.AddTool("list_assignment_errors", "List assignment synchronization errors in the organization.",
            schema: s => s.Integer("top", "Maximum number of errors to return"),
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/assignmentErrors{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_assignment_error", "Get details of a specific assignment error including error code, message, and occurrence time.",
            schema: s => s.String("assignmentErrorId", "The unique identifier of the assignment error", required: true),
            handler: async (args, ct) =>
            {
                var assignmentErrorId = RequireArgument(args, "assignmentErrorId");
                return await SendGraphRequestAsync("GET", $"/beta/admin/cloudLicensing/assignmentErrors/{assignmentErrorId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Usage Rights (User) ─────────────────────────────────────────

        handler.AddTool("list_user_usage_rights", "List usage rights granted to a specific user. Shows which SKUs and services the user has access to.",
            schema: s =>
            {
                s.String("userId", "The ID or userPrincipalName of the user", required: true);
                s.Integer("top", "Maximum number of usage rights to return");
                s.String("filter", "OData filter expression (e.g., skuId eq 'GUID')");
            },
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "userId");
                var qs = BuildQueryString(args, "top", "filter");
                return await SendGraphRequestAsync("GET", $"/beta/users/{userId}/cloudLicensing/usageRights{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_user_usage_right", "Get a specific usage right for a user including SKU info and services.",
            schema: s =>
            {
                s.String("userId", "The ID or userPrincipalName of the user", required: true);
                s.String("usageRightId", "The unique identifier of the usage right", required: true);
            },
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "userId");
                var usageRightId = RequireArgument(args, "usageRightId");
                return await SendGraphRequestAsync("GET", $"/beta/users/{userId}/cloudLicensing/usageRights/{usageRightId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Usage Rights (Group) ────────────────────────────────────────

        handler.AddTool("list_group_usage_rights", "List usage rights granted to a specific group.",
            schema: s =>
            {
                s.String("groupId", "The unique identifier of the group", required: true);
                s.Integer("top", "Maximum number of usage rights to return");
                s.String("filter", "OData filter expression (e.g., skuId eq 'GUID')");
            },
            handler: async (args, ct) =>
            {
                var groupId = RequireArgument(args, "groupId");
                var qs = BuildQueryString(args, "top", "filter");
                return await SendGraphRequestAsync("GET", $"/beta/groups/{groupId}/cloudLicensing/usageRights{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("get_group_usage_right", "Get a specific usage right for a group including SKU info and services.",
            schema: s =>
            {
                s.String("groupId", "The unique identifier of the group", required: true);
                s.String("usageRightId", "The unique identifier of the usage right", required: true);
            },
            handler: async (args, ct) =>
            {
                var groupId = RequireArgument(args, "groupId");
                var usageRightId = RequireArgument(args, "usageRightId");
                return await SendGraphRequestAsync("GET", $"/beta/groups/{groupId}/cloudLicensing/usageRights/{usageRightId}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── User Assignments ────────────────────────────────────────────

        handler.AddTool("list_user_assignments", "List license assignments for a specific user.",
            schema: s =>
            {
                s.String("userId", "The ID or userPrincipalName of the user", required: true);
                s.Integer("top", "Maximum number of assignments to return");
            },
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "userId");
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/users/{userId}/cloudLicensing/assignments{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Group Assignments ───────────────────────────────────────────

        handler.AddTool("list_group_assignments", "List license assignments for a specific group.",
            schema: s =>
            {
                s.String("groupId", "The unique identifier of the group", required: true);
                s.Integer("top", "Maximum number of assignments to return");
            },
            handler: async (args, ct) =>
            {
                var groupId = RequireArgument(args, "groupId");
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/groups/{groupId}/cloudLicensing/assignments{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── User Assignment Errors ──────────────────────────────────────

        handler.AddTool("list_user_assignment_errors", "List assignment synchronization errors affecting a specific user.",
            schema: s =>
            {
                s.String("userId", "The ID or userPrincipalName of the user", required: true);
                s.Integer("top", "Maximum number of errors to return");
            },
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "userId");
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/users/{userId}/cloudLicensing/assignmentErrors{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── User Waiting Members ────────────────────────────────────────

        handler.AddTool("list_user_waiting_members", "List allotments that a specific user is waiting for due to license capacity limits.",
            schema: s =>
            {
                s.String("userId", "The ID or userPrincipalName of the user", required: true);
                s.Integer("top", "Maximum number of waiting members to return");
            },
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "userId");
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/users/{userId}/cloudLicensing/waitingMembers{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        // ── Reprocess Assignments ───────────────────────────────────────

        handler.AddTool("reprocess_user_assignments", "Reprocess existing license assignments for a user to resolve synchronization issues.",
            schema: s => s.String("userId", "The ID or userPrincipalName of the user", required: true),
            handler: async (args, ct) =>
            {
                var userId = RequireArgument(args, "userId");
                return await SendGraphRequestAsync("POST", $"/beta/users/{userId}/cloudLicensing/assignments/reprocessAssignments");
            },
            annotations: a => { a["readOnlyHint"] = false; });

        // ── My Licensing (Signed-In User) ───────────────────────────────

        handler.AddTool("list_my_usage_rights", "List usage rights granted to the signed-in user.",
            schema: s =>
            {
                s.Integer("top", "Maximum number of usage rights to return");
                s.String("filter", "OData filter expression (e.g., skuId eq 'GUID')");
            },
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top", "filter");
                return await SendGraphRequestAsync("GET", $"/beta/me/cloudLicensing/usageRights{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_my_assignments", "List license assignments for the signed-in user.",
            schema: s => s.Integer("top", "Maximum number of assignments to return"),
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/me/cloudLicensing/assignments{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_my_waiting_members", "List allotments that the signed-in user is waiting for.",
            schema: s => s.Integer("top", "Maximum number of waiting members to return"),
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/me/cloudLicensing/waitingMembers{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        handler.AddTool("list_my_assignment_errors", "List assignment synchronization errors affecting the signed-in user.",
            schema: s => s.Integer("top", "Maximum number of errors to return"),
            handler: async (args, ct) =>
            {
                var qs = BuildQueryString(args, "top");
                return await SendGraphRequestAsync("GET", $"/beta/me/cloudLicensing/assignmentErrors{qs}");
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }


    // ── Graph API Helper ─────────────────────────────────────────────────

    private async Task<JObject> SendGraphRequestAsync(string method, string path, JObject body = null)
    {
        var url = GraphBaseUrl + path;
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

        return JObject.Parse(content);
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

    private static string BuildQueryString(JObject args, params string[] paramNames)
    {
        var parts = new List<string>();
        foreach (var p in paramNames)
        {
            var val = args[p]?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
            {
                var key = p == "filter" ? "$filter" : p == "top" ? "$top" : p;
                parts.Add($"{key}={Uri.EscapeDataString(val)}");
            }
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
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

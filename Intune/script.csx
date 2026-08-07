using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  Microsoft Intune — dual-purpose Power Platform custom connector.          ║
// ║                                                                            ║
// ║   1. MCP server  — POST /mcp (InvokeMCP) speaks JSON-RPC 2.0 and exposes   ║
// ║      105 Intune tools to Copilot Studio, Agent Framework, and any MCP      ║
// ║      client. Dual-era: MCP 2026-07-28 and the legacy initialize handshake. ║
// ║   2. REST actions — every other operationId is a plain Microsoft Graph     ║
// ║      call forwarded verbatim, for Power Automate and Logic Apps.           ║
// ║                                                                            ║
// ║  Both share one connection and one set of delegated Graph permissions.     ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    /// <summary>Use the stable Graph endpoint whenever Microsoft publishes the operation there.</summary>
    private const string GraphBeta = "https://graph.microsoft.com/beta";
    private const string GraphV1 = "https://graph.microsoft.com/v1.0";

    private static readonly HashSet<string> BetaRestOperationIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ListDetectedApps",
        "GetNonCompliantSettings",
        "GetDeviceSyncStatus",
        "ListManagedDeviceEncryptionStates",
        "ListComanagedDevices",
        "EnableLostMode",
        "RotateBitLockerKeys",
        "RotateFileVaultKey",
        "RotateLocalAdminPassword",
        "SendCustomNotification",
        "CreateDeviceLogCollectionRequest",
        "InitiateOnDemandProactiveRemediation",
        "BulkDeviceAction",
        "ListRemoteActionAudits",
        "GetLocalAdminPassword",
        "GetFileVaultKey",
        "ListAutopilotEvents",
        "ListConfigurationPolicies",
        "CreateConfigurationPolicy",
        "GetConfigurationPolicySettings",
        "AssignConfigurationPolicy",
        "GetAppInstallSummary",
        "GetAppDeviceStatuses",
        "ListAutopilotProfiles",
        "AssignAutopilotProfile",
        "ListDeviceManagementScripts",
        "GetDeviceManagementScript",
        "AssignDeviceManagementScript",
        "GetScriptDeviceRunStates",
        "ListDeviceShellScripts",
        "ListDeviceHealthScripts",
        "GetRemediationSummary",
        "ListFeatureUpdateProfiles",
        "ListQualityUpdatePolicies",
        "ListDriverUpdateProfiles",
        "ListElevationRequests",
        "ApproveElevationRequest",
        "DenyElevationRequest",
        "ListOperationApprovalRequests",
        "ApproveOperationRequest",
        "RejectOperationRequest",
        "ListAssignmentFilters",
        "GetAssignmentFilter",
        "ValidateAssignmentFilter",
        "ListRoleScopeTags",
        "ListDepOnboardingSettings",
        "RunIntuneReport",
        "ListAlertRecords"
    };

    private static readonly string[] BetaGraphRoutes =
    {
        "GET /deviceManagement/managedDevices/{managedDeviceId}/detectedApps",
        "GET /deviceManagement/managedDevices/{managedDeviceId}/getNonCompliantSettings",
        "GET /deviceManagement/managedDevices/{managedDeviceId}/getSyncStatus",
        "GET /deviceManagement/managedDeviceEncryptionStates",
        "GET /deviceManagement/managedDeviceEncryptionStates/{managedDeviceEncryptionStateId}",
        "GET /deviceManagement/comanagedDevices",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/enableLostMode",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/rotateBitLockerKeys",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/rotateFileVaultKey",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/rotateLocalAdminPassword",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/sendCustomNotificationToCompanyPortal",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/createDeviceLogCollectionRequest",
        "POST /deviceManagement/managedDevices/{managedDeviceId}/initiateOnDemandProactiveRemediation",
        "POST /deviceManagement/managedDevices/executeAction",
        "GET /deviceManagement/remoteActionAudits",
        "GET /deviceManagement/managedDevices/{managedDeviceId}/retrieveDeviceLocalAdminAccountDetail",
        "GET /deviceManagement/managedDevices/{managedDeviceId}/retrieveMacOSManagedDeviceLocalAdminAccountDetail",
        "GET /deviceManagement/managedDevices/{managedDeviceId}/getFileVaultKey",
        "GET /deviceManagement/autopilotEvents",
        "GET /deviceManagement/configurationPolicies",
        "POST /deviceManagement/configurationPolicies",
        "GET /deviceManagement/configurationPolicies/{configurationPolicyId}/settings",
        "POST /deviceManagement/configurationPolicies/{configurationPolicyId}/assign",
        "GET /deviceAppManagement/mobileApps/{mobileAppId}/installSummary",
        "GET /deviceAppManagement/mobileApps/{mobileAppId}/deviceStatuses",
        "GET /deviceAppManagement/mobileApps/{mobileAppId}/userStatuses",
        "GET /deviceManagement/windowsAutopilotDeploymentProfiles",
        "POST /deviceManagement/windowsAutopilotDeploymentProfiles/{windowsAutopilotDeploymentProfileId}/assign",
        "GET /deviceManagement/deviceManagementScripts",
        "GET /deviceManagement/deviceManagementScripts/{deviceManagementScriptId}",
        "POST /deviceManagement/deviceManagementScripts/{deviceManagementScriptId}/assign",
        "GET /deviceManagement/deviceManagementScripts/{deviceManagementScriptId}/{resultType}",
        "GET /deviceManagement/deviceShellScripts",
        "GET /deviceManagement/deviceShellScripts/{deviceShellScriptId}",
        "POST /deviceManagement/deviceShellScripts/{deviceShellScriptId}/assign",
        "GET /deviceManagement/deviceShellScripts/{deviceShellScriptId}/{resultType}",
        "GET /deviceManagement/deviceCustomAttributeShellScripts",
        "GET /deviceManagement/deviceCustomAttributeShellScripts/{deviceCustomAttributeShellScriptId}",
        "POST /deviceManagement/deviceCustomAttributeShellScripts/{deviceCustomAttributeShellScriptId}/assign",
        "GET /deviceManagement/deviceCustomAttributeShellScripts/{deviceCustomAttributeShellScriptId}/{resultType}",
        "GET /deviceManagement/deviceHealthScripts",
        "GET /deviceManagement/deviceHealthScripts/{deviceHealthScriptId}/{resultType}",
        "GET /deviceManagement/windowsFeatureUpdateProfiles",
        "GET /deviceManagement/windowsQualityUpdatePolicies",
        "GET /deviceManagement/windowsQualityUpdateProfiles",
        "GET /deviceManagement/windowsDriverUpdateProfiles",
        "GET /deviceManagement/windowsDriverUpdateProfiles/{windowsDriverUpdateProfileId}/driverInventories",
        "GET /deviceManagement/elevationRequests",
        "POST /deviceManagement/elevationRequests/{elevationRequestId}/approve",
        "POST /deviceManagement/elevationRequests/{elevationRequestId}/deny",
        "GET /deviceManagement/operationApprovalRequests",
        "POST /deviceManagement/operationApprovalRequests/{operationApprovalRequestId}/approve",
        "POST /deviceManagement/operationApprovalRequests/{operationApprovalRequestId}/reject",
        "GET /deviceManagement/assignmentFilters",
        "GET /deviceManagement/assignmentFilters/{assignmentFilterId}",
        "POST /deviceManagement/assignmentFilters/validateFilter",
        "GET /deviceManagement/roleScopeTags",
        "GET /deviceManagement/depOnboardingSettings",
        "POST /deviceManagement/reports/{reportName}",
        "GET /deviceManagement/monitoring/alertRecords"
    };

    private static readonly string[] StableGraphRouteOverrides =
    {
        "POST /deviceManagement/reports/exportJobs",
        "GET /deviceManagement/reports/exportJobs/{exportJobId}",
        "POST /deviceManagement/reports/getReportFilters"
    };

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "intune-mcp-server",
            Version = "1.0.0",
            Title = "Microsoft Intune",
            Description = "Microsoft Intune device management through Microsoft Graph — devices, compliance, configuration, apps, enrollment, scripts, updates, reporting, and every remaining Intune endpoint through a guarded passthrough."
        },

        ProtocolVersion = "2026-07-28",
        SupportedProtocolVersions = new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" },

        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            Prompts = true,
            Completions = true
        },

        ListCacheTtlMs = 300000,
        ListCacheScope = "public",
        ResourceCacheTtlMs = 60000,
        ResourceCacheScope = "private",
        DiscoverCacheTtlMs = 3600000,

        Instructions =
            "Use this server to manage Microsoft Intune through Microsoft Graph.\n\n" +
            "Start with `list_managed_devices` or `search_devices` to find a device, then use its `id` with the " +
            "inspection tools (`get_managed_device`, `get_device_compliance_states`, `get_noncompliant_settings`) " +
            "before taking any remote action.\n\n" +
            "Remote actions are irreversible. `wipe_device` and `retire_device` destroy data or unenroll the device — " +
            "confirm the device identity with the user before calling them.\n\n" +
            "Reporting: call `list_intune_reports` first to discover the 70+ available report names, then `run_intune_report` " +
            "for synchronous results or `export_intune_report` plus `get_export_job_status` for large exports.\n\n" +
            "This server covers the highest-value Intune surface with named tools. Intune exposes roughly 1,650 Graph " +
            "operations in total, so for anything not covered: use `intune_find_endpoint` to locate the exact path, " +
            "`intune_describe_entity` to learn its fields, and `intune_graph_request` to call it. Use `intune_graph_batch` " +
            "to combine up to 20 calls into one round trip when auditing many devices."
    };

    // ── Entry Point ──────────────────────────────────────────────────────
    //
    //    InvokeMCP  -> the MCP JSON-RPC server.
    //    Everything else -> a direct Microsoft Graph call, forwarded as-is.
    //

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var operationId = this.Context.OperationId ?? string.Empty;

        try
        {
            HttpResponseMessage response;

            if (string.Equals(operationId, "InvokeMCP", StringComparison.OrdinalIgnoreCase))
            {
                response = await HandleMcpAsync(correlationId).ConfigureAwait(false);
            }
            else
            {
                var graphVersion = BetaRestOperationIds.Contains(operationId) ? "beta" : "v1.0";
                SetGraphVersion(this.Context.Request, graphVersion);
                ApplyGraphHeaders(this.Context.Request);
                response = await this.Context.SendAsync(this.Context.Request, this.CancellationToken).ConfigureAwait(false);
            }

            var elapsed = DateTime.UtcNow - startTime;
            this.Context.Logger.LogInformation($"[{correlationId}] {operationId} -> {(int)response.StatusCode} in {elapsed.TotalMilliseconds}ms");
            _ = LogToAppInsights("OperationCompleted", new { OperationId = operationId, Status = (int)response.StatusCode, DurationMs = elapsed.TotalMilliseconds }, correlationId);

            return response;
        }
        catch (Exception ex)
        {
            this.Context.Logger.LogError($"[{correlationId}] {operationId} failed: {ex.Message}");
            _ = LogToAppInsights("OperationError", new { OperationId = operationId, Error = ex.Message }, correlationId);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    new JObject { ["error"] = new JObject { ["message"] = ex.Message } }.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private async Task<HttpResponseMessage> HandleMcpAsync(string correlationId)
    {
        var handler = new McpRequestHandler(Options);
        RegisterCapabilities(handler);

        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        var body = this.Context.Request.Content == null
            ? string.Empty
            : await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);

        var result = await handler.HandleAsync(
            body,
            McpTransportHeaders.FromRequest(this.Context.Request),
            this.CancellationToken).ConfigureAwait(false);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        RegisterDeviceInventoryTools(handler);   //  9
        RegisterDeviceActionTools(handler);      // 16
        RegisterBulkTools(handler);              //  2
        RegisterDiagnosticsTools(handler);       //  4
        RegisterTroubleshootingTools(handler);   //  3
        RegisterSecretsTools(handler);           //  3
        RegisterComplianceTools(handler);        //  7
        RegisterConfigurationTools(handler);     // 10
        RegisterAppTools(handler);               //  8
        RegisterEnrollmentTools(handler);        //  7
        RegisterScriptTools(handler);            //  6
        RegisterUpdateTools(handler);            //  3
        RegisterApprovalTools(handler);          //  4
        RegisterCloudPcTools(handler);           //  4
        RegisterAssignmentFilterTools(handler);  //  2
        RegisterRbacTools(handler);              //  3
        RegisterTenantTools(handler);            //  3
        RegisterReportingTools(handler);         //  4
        RegisterMonitoringTools(handler);        //  3
        RegisterUniversalTools(handler);         //  4
                                                 // ---
                                                 // 105

        RegisterResources(handler);
        RegisterPrompts(handler);
    }


    // ── A. Device Inventory (9) ──────────────────────────────────────────

    private void RegisterDeviceInventoryTools(McpRequestHandler h)
    {
        h.AddTool("list_managed_devices",
            "List Intune managed devices in the tenant. Supports OData filtering, field selection, sorting, and paging. Use this to enumerate the device fleet or to narrow it by platform, compliance state, ownership, or last check-in time.",
            schema: s => s
                .String("filter", "OData $filter, for example \"complianceState eq 'noncompliant'\", \"operatingSystem eq 'iOS'\", or \"lastSyncDateTime le 2026-01-01T00:00:00Z\"")
                .String("select", "Comma-separated fields to return, for example \"id,deviceName,userPrincipalName,complianceState,osVersion\"")
                .String("orderby", "Sort expression, for example \"lastSyncDateTime desc\"")
                .Integer("top", "Maximum devices to return (default 25, Graph page maximum 1000)", defaultValue: 25)
                .Integer("skip", "Number of devices to skip for paging")
                .Boolean("count", "Return the total matching device count alongside the page"),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/managedDevices" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; a["openWorldHint"] = true; });

        h.AddTool("get_managed_device",
            "Get the full record for one Intune managed device, including hardware detail, OS version, ownership, encryption state, enrollment date, and the assigned user.",
            schema: s => s
                .String("device_id", "The managedDevice id (a GUID)", required: true)
                .String("select", "Comma-separated fields to return; omit for the full record")
                .String("expand", "Navigation properties to expand, for example \"detectedApps\" or \"users\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("search_devices",
            "Find managed devices by a free-text term matched against device name, user principal name, serial number, IMEI, or primary user display name. Use this when the user names a device or person rather than supplying a GUID.",
            schema: s => s
                .String("query", "Search term, for example a device name, serial number, or user email", required: true)
                .Integer("top", "Maximum devices to return (default 25)", defaultValue: 25)
                .String("select", "Comma-separated fields to return"),
            handler: async (args, ct) =>
            {
                var q = RequireArgument(args, "query").Replace("'", "''");
                var filter =
                    $"contains(deviceName,'{q}') or contains(userPrincipalName,'{q}') or " +
                    $"contains(serialNumber,'{q}') or contains(imei,'{q}') or contains(userDisplayName,'{q}')";
                var top = args.Value<int?>("top") ?? 25;
                var select = GetArgument(args, "select");
                var path = $"/deviceManagement/managedDevices?$filter={Uri.EscapeDataString(filter)}&$top={top}";
                if (!string.IsNullOrWhiteSpace(select)) path += "&$select=" + Uri.EscapeDataString(select);
                return await GraphGetAsync(path, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("delete_managed_device",
            "Delete a managed device record from Intune. This removes the Intune object only; it does not wipe or retire the physical device. Use retire_device or wipe_device to act on the device itself.",
            schema: s => s.String("device_id", "The managedDevice id to delete", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/deviceManagement/managedDevices/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_device_detected_apps",
            "List the applications Intune has detected installed on a device. Use this for software inventory, license reconciliation, or locating machines running a specific application version.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .Integer("top", "Maximum applications to return (default 50)", defaultValue: 50)
                .String("filter", "OData $filter over detected apps, for example \"contains(displayName,'Chrome')\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/detectedApps" + BuildQuery(args, 50), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_device_compliance_states",
            "List the compliance policy states for a device — which compliance policies apply to it and whether each one passes. Use this as the first step when diagnosing why a device is marked noncompliant.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .Integer("top", "Maximum policy states to return (default 50)", defaultValue: 50),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/deviceCompliancePolicyStates" + BuildQuery(args, 50), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_device_configuration_states",
            "List the device configuration profile states for a device — which configuration profiles are targeted at it and whether each applied successfully, is pending, or errored.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .Integer("top", "Maximum configuration states to return (default 50)", defaultValue: 50),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/deviceConfigurationStates" + BuildQuery(args, 50), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_device_encryption_state",
            "Get disk encryption state for devices — BitLocker on Windows, FileVault on macOS — including encryption readiness and any blocking policy errors. Omit device_id to list encryption state across the fleet.",
            schema: s => s
                .String("device_id", "A managedDeviceEncryptionState id to fetch a single record; omit to list all")
                .String("filter", "OData $filter, for example \"encryptionState eq 'notEncrypted'\"")
                .Integer("top", "Maximum records to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = GetArgument(args, "device_id");
                var path = string.IsNullOrWhiteSpace(id)
                    ? "/deviceManagement/managedDeviceEncryptionStates" + BuildQuery(args)
                    : $"/deviceManagement/managedDeviceEncryptionStates/{Esc(id)}";
                return await GraphGetAsync(path, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_device_categories",
            "List the Intune device categories defined in the tenant. Categories are assigned to devices at enrollment and are commonly used as dynamic group and assignment criteria.",
            schema: s => s.Integer("top", "Maximum categories to return (default 50)", defaultValue: 50),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/deviceCategories" + BuildQuery(args, 50), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── B. Device Remote Actions (16) ────────────────────────────────────

    private void RegisterDeviceActionTools(McpRequestHandler h)
    {
        h.AddTool("sync_device",
            "Force a device to check in with Intune immediately and pull down pending policy, app, and configuration assignments. This is the safe first remediation for most 'policy has not applied' problems.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "syncDevice", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("retire_device",
            "Retire a device: remove company data, apps, and management policy, and unenroll it from Intune while leaving personal data intact. Prefer this over wipe_device for BYOD devices. This action cannot be undone.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "retire", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("wipe_device",
            "Factory reset a device, erasing all data on it. This is destructive and irreversible — confirm the exact device with the user first. Use keep_enrollment_data and keep_user_data for a lighter reset, or use_protected_wipe on Windows to make the wipe resistant to interruption.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .Boolean("keep_enrollment_data", "Preserve Intune enrollment through the wipe (Windows and iOS)")
                .Boolean("keep_user_data", "Preserve user data, performing a reset rather than a full erase")
                .Boolean("use_protected_wipe", "Windows only: run a protected wipe that resists interruption")
                .String("mac_os_unlock_code", "macOS only: the six-digit unlock code required to complete the wipe")
                .String("persist_esim_data_plan", "Set to true to keep the eSIM data plan on the device"),
            handler: async (args, ct) =>
            {
                var body = new JObject();
                if (args["keep_enrollment_data"] != null) body["keepEnrollmentData"] = args.Value<bool?>("keep_enrollment_data") ?? false;
                if (args["keep_user_data"] != null) body["keepUserData"] = args.Value<bool?>("keep_user_data") ?? false;
                if (args["use_protected_wipe"] != null) body["useProtectedWipe"] = args.Value<bool?>("use_protected_wipe") ?? false;
                var mac = GetArgument(args, "mac_os_unlock_code");
                if (!string.IsNullOrWhiteSpace(mac)) body["macOsUnlockCode"] = mac;
                var esim = GetArgument(args, "persist_esim_data_plan");
                if (!string.IsNullOrWhiteSpace(esim)) body["persistEsimDataPlan"] = esim.Equals("true", StringComparison.OrdinalIgnoreCase);
                return await DeviceActionAsync(args, "wipe", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("reboot_device",
            "Restart a device remotely. Use this after applying configuration that requires a reboot, or as a first-line remediation for an unresponsive agent.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "rebootNow", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("shutdown_device",
            "Power off a device remotely. The device cannot be powered back on remotely, so use this only when physical access is available or the device must be taken offline immediately.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "shutDown", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("remote_lock_device",
            "Lock a device remotely so it requires the passcode to unlock. This is the standard immediate response to a reported lost or stolen device.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "remoteLock", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("reset_passcode",
            "Reset the device passcode. On iOS the passcode is removed; on Android a new temporary passcode is generated and returned in the device record. Use recover_passcode semantics via get_managed_device afterwards to read it.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "resetPasscode", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("locate_device",
            "Request the current geographic location of a supervised iOS or Android device. The location is returned asynchronously — read the deviceActionResults on the device record shortly afterwards to retrieve the coordinates.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "locateDevice", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("enable_lost_mode",
            "Put a supervised iOS or iPadOS device into Lost Mode, locking it and displaying a recovery message and phone number on the lock screen.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .String("message", "Message shown on the lock screen", required: true)
                .String("phone_number", "Contact phone number shown on the lock screen")
                .String("footer", "Additional footer text shown on the lock screen"),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["message"] = RequireArgument(args, "message") };
                var phone = GetArgument(args, "phone_number");
                if (!string.IsNullOrWhiteSpace(phone)) body["phoneNumber"] = phone;
                var footer = GetArgument(args, "footer");
                if (!string.IsNullOrWhiteSpace(footer)) body["footer"] = footer;
                return await DeviceActionAsync(args, "enableLostMode", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("disable_lost_mode",
            "Take a supervised iOS or iPadOS device out of Lost Mode and return it to normal operation.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "disableLostMode", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("rotate_bitlocker_keys",
            "Rotate the BitLocker recovery keys on a Windows device. Do this after a recovery key has been disclosed or used, so the exposed key no longer unlocks the drive.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "rotateBitLockerKeys", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("rotate_filevault_key",
            "Rotate the FileVault personal recovery key on a macOS device. Do this after the existing key has been disclosed. Retrieve the new key with get_filevault_key.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "rotateFileVaultKey", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("rotate_local_admin_password",
            "Rotate the Windows LAPS local administrator password on a device. Retrieve the new password with get_local_admin_password.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "rotateLocalAdminPassword", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("defender_scan",
            "Run a Microsoft Defender antivirus scan on a Windows device. A quick scan checks the common infection points; a full scan examines the entire disk and takes considerably longer.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .Boolean("quick_scan", "True for a quick scan (default), false for a full scan"),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["quickScan"] = args.Value<bool?>("quick_scan") ?? true };
                return await DeviceActionAsync(args, "windowsDefenderScan", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("defender_update_signatures",
            "Force a Windows device to update its Microsoft Defender malware signature definitions immediately, rather than waiting for the next scheduled update.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) => await DeviceActionAsync(args, "windowsDefenderUpdateSignatures", null, ct),
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("send_custom_notification",
            "Send a custom push notification to the Company Portal app on one or more devices. Use this to tell users about required action such as an upcoming compliance deadline or a mandatory restart.",
            schema: s => s
                .Array("device_ids", "The managedDevice ids to notify", new JObject { ["type"] = "string" }, required: true)
                .String("title", "Notification title", required: true)
                .String("body", "Notification body text", required: true),
            handler: async (args, ct) =>
            {
                var ids = RequireArray(args, "device_ids");
                var payload = new JObject
                {
                    ["notificationTitle"] = RequireArgument(args, "title"),
                    ["notificationBody"] = RequireArgument(args, "body")
                };

                var results = new JArray();
                foreach (var idToken in ids)
                {
                    var id = idToken.ToString();
                    try
                    {
                        await GraphSendAsync(HttpMethod.Post,
                            $"/deviceManagement/managedDevices/{Esc(id)}/sendCustomNotificationToCompanyPortal",
                            payload, ct).ConfigureAwait(false);
                        results.Add(new JObject { ["deviceId"] = id, ["sent"] = true });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new JObject { ["deviceId"] = id, ["sent"] = false, ["error"] = ex.Message });
                    }
                }

                return new JObject { ["results"] = results, ["notified"] = results.Count(r => r["sent"].Value<bool>()) };
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });
    }

    // ── C. Bulk and Audit (2) ────────────────────────────────────────────

    private void RegisterBulkTools(McpRequestHandler h)
    {
        h.AddTool("bulk_device_action",
            "Run one remote action against many devices in a single call. This is far more efficient than looping a per-device tool when acting on a fleet. Destructive actions such as wipe and retire affect every listed device — confirm the list with the user first.",
            schema: s => s
                .String("action", "The action to run on every device", required: true,
                    enumValues: new[] { "syncDevice", "retire", "wipe", "rebootNow", "shutDown", "remoteLock", "windowsDefenderScan", "windowsDefenderUpdateSignatures", "rotateBitLockerKeys", "deleteDevice" })
                .Array("device_ids", "The managedDevice ids to act on (maximum 100 per call)", new JObject { ["type"] = "string" }, required: true)
                .Boolean("keep_enrollment_data", "For wipe: preserve Intune enrollment")
                .Boolean("keep_user_data", "For wipe: preserve user data"),
            handler: async (args, ct) =>
            {
                var action = RequireArgument(args, "action");
                var ids = RequireArray(args, "device_ids");
                if (ids.Count > 100)
                    throw new ArgumentException("A maximum of 100 device ids may be supplied in one call");

                var payload = new JObject
                {
                    ["action"] = action,
                    ["deviceIds"] = new JArray(ids.Select(i => i.ToString()))
                };
                if (args["keep_enrollment_data"] != null) payload["keepEnrollmentData"] = args.Value<bool?>("keep_enrollment_data") ?? false;
                if (args["keep_user_data"] != null) payload["keepUserData"] = args.Value<bool?>("keep_user_data") ?? false;

                return await GraphSendAsync(HttpMethod.Post, "/deviceManagement/managedDevices/executeAction", payload, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("get_remote_action_audits",
            "List the audit trail of remote actions performed on devices — which action was run, against which device, by which administrator, and whether it succeeded. Use this to answer 'who wiped this device'.",
            schema: s => s
                .String("filter", "OData $filter, for example \"action eq 'wipe'\" or \"deviceOwnerUserPrincipalName eq 'user@contoso.com'\"")
                .String("orderby", "Sort expression, for example \"requestDateTime desc\"")
                .Integer("top", "Maximum audit entries to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/remoteActionAudits" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── D. Diagnostics and Log Collection (4) ────────────────────────────

    private void RegisterDiagnosticsTools(McpRequestHandler h)
    {
        h.AddTool("collect_device_logs",
            "Request diagnostic log collection from a Windows device. Collection runs asynchronously — poll list_log_collection_requests for completion, then call download_device_logs for the download URL.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .String("template_type", "Log template to collect", enumValues: new[] { "predefined", "custom" }),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                var body = new JObject
                {
                    ["templateType"] = GetArgument(args, "template_type", "predefined")
                };
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/managedDevices/{Esc(id)}/createDeviceLogCollectionRequest", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("list_log_collection_requests",
            "List diagnostic log collection requests for a device, with their status, size, and expiry. Use this to check whether a collection started with collect_device_logs has finished.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .Integer("top", "Maximum requests to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/logCollectionRequests" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("download_device_logs",
            "Get a time-limited download URL for a completed device log collection. Call list_log_collection_requests first to obtain the request id and confirm the collection succeeded.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .String("request_id", "The deviceLogCollectionResponse id from list_log_collection_requests", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                var reqId = RequireArgument(args, "request_id");
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/managedDevices/{Esc(id)}/logCollectionRequests/{Esc(reqId)}/createDownloadUrl", new JObject(), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("get_device_sync_status",
            "Get the check-in and synchronization status of a device — when it last contacted Intune and whether the most recent sync succeeded. Use this before concluding that a policy failed to apply.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/getSyncStatus", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── E. Troubleshooting (3) ───────────────────────────────────────────

    private void RegisterTroubleshootingTools(McpRequestHandler h)
    {
        h.AddTool("get_noncompliant_settings",
            "List the exact settings causing a device to be noncompliant, with the current value and the expected value for each. This is the most direct answer to 'why is this device noncompliant'.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/getNonCompliantSettings", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_troubleshooting_events",
            "List Intune troubleshooting events — enrollment failures, policy application errors, and app install failures — optionally scoped to one user. Use this to diagnose an enrollment or policy problem end to end.",
            schema: s => s
                .String("user_id", "Filter to a single user by object id or user principal name")
                .String("filter", "OData $filter, for example \"eventDateTime ge 2026-01-01T00:00:00Z\"")
                .Integer("top", "Maximum events to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var user = GetArgument(args, "user_id");
                var path = string.IsNullOrWhiteSpace(user)
                    ? "/deviceManagement/troubleshootingEvents" + BuildQuery(args)
                    : $"/users/{Esc(user)}/deviceManagementTroubleshootingEvents" + BuildQuery(args);
                return await GraphGetAsync(path, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_autopilot_events",
            "List Windows Autopilot deployment events, showing each provisioning attempt with its outcome, duration, enrollment state, and any failure detail. Use this to diagnose Autopilot deployments that fail or hang.",
            schema: s => s
                .String("filter", "OData $filter, for example \"deviceSerialNumber eq '1234567'\" or \"enrollmentState eq 'failed'\"")
                .String("orderby", "Sort expression, for example \"deploymentStartDateTime desc\"")
                .Integer("top", "Maximum events to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/autopilotEvents" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── F. Secrets and Recovery (3) ──────────────────────────────────────

    private void RegisterSecretsTools(McpRequestHandler h)
    {
        h.AddTool("get_local_admin_password",
            "Retrieve the current Windows LAPS local administrator password for a device, or the macOS local administrator account detail. This returns a live credential — disclose it only to an authorized administrator and rotate it afterwards with rotate_local_admin_password.",
            schema: s => s
                .String("device_id", "The managedDevice id", required: true)
                .String("platform", "Which credential to retrieve", enumValues: new[] { "windows", "macos" }),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                var platform = GetArgument(args, "platform", "windows").ToLowerInvariant();
                var op = platform == "macos"
                    ? "retrieveMacOSManagedDeviceLocalAdminAccountDetail"
                    : "retrieveDeviceLocalAdminAccountDetail";
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/{op}", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_filevault_key",
            "Retrieve the FileVault personal recovery key for a macOS device. This is a live recovery secret — disclose it only to an authorized administrator and rotate it afterwards with rotate_filevault_key.",
            schema: s => s.String("device_id", "The managedDevice id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "device_id");
                return await GraphGetAsync($"/deviceManagement/managedDevices/{Esc(id)}/getFileVaultKey", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_bitlocker_recovery_key",
            "Retrieve BitLocker recovery keys from Microsoft Entra ID. Omit key_id to list the recovery keys for a device; supply key_id to read the actual key value. Reading a key value is audited and requires the BitlockerKey.Read.All permission.",
            schema: s => s
                .String("key_id", "The bitlockerRecoveryKey id; supply this to return the key value itself")
                .String("device_id", "Filter the key list to one Entra device id")
                .Integer("top", "Maximum keys to return when listing (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var keyId = GetArgument(args, "key_id");
                if (!string.IsNullOrWhiteSpace(keyId))
                    return await GraphGetAsync($"/informationProtection/bitlocker/recoveryKeys/{Esc(keyId)}?$select=key,createdDateTime,deviceId,volumeType", ct);

                var deviceId = GetArgument(args, "device_id");
                var path = "/informationProtection/bitlocker/recoveryKeys";
                if (!string.IsNullOrWhiteSpace(deviceId))
                    path += "?$filter=" + Uri.EscapeDataString($"deviceId eq '{deviceId.Replace("'", "''")}'");
                return await GraphGetAsync(path, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }


    // ── G. Compliance Policies (7) ───────────────────────────────────────

    private void RegisterComplianceTools(McpRequestHandler h)
    {
        h.AddTool("list_compliance_policies",
            "List Intune device compliance policies. Compliance policies define the rules a device must satisfy — encryption, OS version, password strength, jailbreak detection — before it is considered compliant for Conditional Access.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(displayName,'iOS')\"")
                .String("select", "Comma-separated fields to return")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum policies to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/deviceCompliancePolicies" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_compliance_policy",
            "Get one compliance policy with its full rule set and, optionally, its group assignments and scheduled non-compliance actions.",
            schema: s => s
                .String("policy_id", "The deviceCompliancePolicy id", required: true)
                .String("expand", "Navigation properties to expand, for example \"assignments,scheduledActionsForRule\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                return await GraphGetAsync($"/deviceManagement/deviceCompliancePolicies/{Esc(id)}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_compliance_policy",
            "Create a device compliance policy. The policy body must include an @odata.type discriminator naming the platform, for example \"#microsoft.graph.iosCompliancePolicy\", \"#microsoft.graph.windows10CompliancePolicy\", or \"#microsoft.graph.androidWorkProfileCompliancePolicy\".",
            schema: s => s
                .String("odata_type", "Platform discriminator, for example \"#microsoft.graph.windows10CompliancePolicy\"", required: true)
                .String("display_name", "Policy display name", required: true)
                .String("description", "Policy description")
                .String("settings_json", "JSON object of platform-specific compliance settings, for example {\"passwordRequired\":true,\"osMinimumVersion\":\"10.0.19041\"}"),
            handler: async (args, ct) =>
            {
                var body = ParseJsonArgument(args, "settings_json") ?? new JObject();
                body["@odata.type"] = RequireArgument(args, "odata_type");
                body["displayName"] = RequireArgument(args, "display_name");
                var desc = GetArgument(args, "description");
                if (!string.IsNullOrWhiteSpace(desc)) body["description"] = desc;
                return await GraphSendAsync(HttpMethod.Post, "/deviceManagement/deviceCompliancePolicies", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_compliance_policy",
            "Update an existing compliance policy. Supply only the properties to change; the @odata.type discriminator matching the policy platform is required by Graph on every PATCH.",
            schema: s => s
                .String("policy_id", "The deviceCompliancePolicy id", required: true)
                .String("odata_type", "Platform discriminator matching the existing policy, for example \"#microsoft.graph.windows10CompliancePolicy\"", required: true)
                .String("updates_json", "JSON object of properties to change", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                var body = ParseJsonArgument(args, "updates_json") ?? new JObject();
                body["@odata.type"] = RequireArgument(args, "odata_type");
                return await GraphSendAsync(new HttpMethod("PATCH"), $"/deviceManagement/deviceCompliancePolicies/{Esc(id)}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_compliance_policy",
            "Delete a compliance policy. Devices previously evaluated by it stop being assessed against its rules, which can change their Conditional Access posture.",
            schema: s => s.String("policy_id", "The deviceCompliancePolicy id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/deviceManagement/deviceCompliancePolicies/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("assign_compliance_policy",
            "Assign a compliance policy to Entra groups. Each assignment targets a group by id, or targets all devices or all licensed users. This replaces the complete set of existing assignments.",
            schema: s => s
                .String("policy_id", "The deviceCompliancePolicy id", required: true)
                .Array("group_ids", "Entra group object ids to include", new JObject { ["type"] = "string" })
                .Array("exclude_group_ids", "Entra group object ids to exclude", new JObject { ["type"] = "string" })
                .Boolean("all_devices", "Assign to all devices instead of specific groups")
                .Boolean("all_users", "Assign to all licensed users instead of specific groups"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                var body = new JObject { ["assignments"] = BuildAssignments(args) };
                return await GraphSendAsync(HttpMethod.Post, $"/deviceManagement/deviceCompliancePolicies/{Esc(id)}/assign", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("get_compliance_policy_statuses",
            "Get per-device or per-user status for a compliance policy, showing which devices pass, fail, are in error, or have not yet reported. Use this to measure rollout of a policy.",
            schema: s => s
                .String("policy_id", "The deviceCompliancePolicy id", required: true)
                .String("scope", "Whether to report by device, by user, or as an overview", enumValues: new[] { "device", "user", "overview" })
                .String("filter", "OData $filter, for example \"status eq 'error'\"")
                .Integer("top", "Maximum status rows to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                var scope = GetArgument(args, "scope", "device").ToLowerInvariant();
                string leaf;
                if (scope == "user") leaf = "userStatuses";
                else if (scope == "overview") leaf = "deviceStatusOverview";
                else leaf = "deviceStatuses";
                var query = scope == "overview" ? string.Empty : BuildQuery(args);
                return await GraphGetAsync($"/deviceManagement/deviceCompliancePolicies/{Esc(id)}/{leaf}" + query, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── H. Device Configuration (10) ─────────────────────────────────────

    private void RegisterConfigurationTools(McpRequestHandler h)
    {
        h.AddTool("list_device_configurations",
            "List classic Intune device configuration profiles — the template-based profiles for Wi-Fi, VPN, certificates, email, restrictions, and endpoint protection. For settings-catalog policies use list_configuration_policies instead.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(displayName,'VPN')\"")
                .String("select", "Comma-separated fields to return")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum profiles to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/deviceConfigurations" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_device_configuration",
            "Get one classic device configuration profile with its complete settings payload and, optionally, its assignments.",
            schema: s => s
                .String("configuration_id", "The deviceConfiguration id", required: true)
                .String("expand", "Navigation properties to expand, for example \"assignments\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "configuration_id");
                return await GraphGetAsync($"/deviceManagement/deviceConfigurations/{Esc(id)}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_device_configuration",
            "Create a classic device configuration profile. The body must include an @odata.type discriminator naming the profile type, for example \"#microsoft.graph.windows10GeneralConfiguration\" or \"#microsoft.graph.iosDeviceFeaturesConfiguration\".",
            schema: s => s
                .String("odata_type", "Profile type discriminator, for example \"#microsoft.graph.windows10GeneralConfiguration\"", required: true)
                .String("display_name", "Profile display name", required: true)
                .String("description", "Profile description")
                .String("settings_json", "JSON object of profile-specific settings"),
            handler: async (args, ct) =>
            {
                var body = ParseJsonArgument(args, "settings_json") ?? new JObject();
                body["@odata.type"] = RequireArgument(args, "odata_type");
                body["displayName"] = RequireArgument(args, "display_name");
                var desc = GetArgument(args, "description");
                if (!string.IsNullOrWhiteSpace(desc)) body["description"] = desc;
                return await GraphSendAsync(HttpMethod.Post, "/deviceManagement/deviceConfigurations", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_device_configuration",
            "Update a classic device configuration profile. Supply only the properties to change; Graph requires the @odata.type discriminator matching the existing profile on every PATCH.",
            schema: s => s
                .String("configuration_id", "The deviceConfiguration id", required: true)
                .String("odata_type", "Profile type discriminator matching the existing profile", required: true)
                .String("updates_json", "JSON object of properties to change", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "configuration_id");
                var body = ParseJsonArgument(args, "updates_json") ?? new JObject();
                body["@odata.type"] = RequireArgument(args, "odata_type");
                return await GraphSendAsync(new HttpMethod("PATCH"), $"/deviceManagement/deviceConfigurations/{Esc(id)}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_device_configuration",
            "Delete a classic device configuration profile. Settings it applied are removed from targeted devices at their next check-in.",
            schema: s => s.String("configuration_id", "The deviceConfiguration id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "configuration_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/deviceManagement/deviceConfigurations/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("assign_device_configuration",
            "Assign a classic device configuration profile to Entra groups. This replaces the complete set of existing assignments on the profile.",
            schema: s => s
                .String("configuration_id", "The deviceConfiguration id", required: true)
                .Array("group_ids", "Entra group object ids to include", new JObject { ["type"] = "string" })
                .Array("exclude_group_ids", "Entra group object ids to exclude", new JObject { ["type"] = "string" })
                .Boolean("all_devices", "Assign to all devices instead of specific groups")
                .Boolean("all_users", "Assign to all licensed users instead of specific groups"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "configuration_id");
                var body = new JObject { ["assignments"] = BuildAssignments(args) };
                return await GraphSendAsync(HttpMethod.Post, $"/deviceManagement/deviceConfigurations/{Esc(id)}/assign", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("list_configuration_policies",
            "List settings-catalog configuration policies. The settings catalog is the modern replacement for template-based profiles and covers the full set of platform settings surfaced by Intune.",
            schema: s => s
                .String("filter", "OData $filter, for example \"platforms has 'windows10'\" or \"contains(name,'Baseline')\"")
                .String("expand", "Navigation properties to expand, for example \"assignments\" or \"settings\"")
                .Integer("top", "Maximum policies to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/configurationPolicies" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_configuration_policy_settings",
            "Get the individual settings inside a settings-catalog policy, each with its setting definition id and configured value. Use this to audit exactly what a modern policy configures.",
            schema: s => s
                .String("policy_id", "The deviceManagementConfigurationPolicy id", required: true)
                .Integer("top", "Maximum settings to return (default 100)", defaultValue: 100),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                return await GraphGetAsync($"/deviceManagement/configurationPolicies/{Esc(id)}/settings" + BuildQuery(args, 100), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_configuration_policy",
            "Create a settings-catalog configuration policy. Supply the settings array in Graph settings-catalog shape, where each entry has a settingInstance with its settingDefinitionId and value.",
            schema: s => s
                .String("name", "Policy name", required: true)
                .String("description", "Policy description")
                .String("platforms", "Target platform", required: true, enumValues: new[] { "windows10", "macOS", "iOS", "android", "linux", "unknown" })
                .String("technologies", "Management technology, for example \"mdm\", \"mdm,windows10XManagement\", or \"configManager\"")
                .String("settings_json", "JSON array of settings-catalog setting instances", required: true),
            handler: async (args, ct) =>
            {
                var settings = ParseJsonArrayArgument(args, "settings_json");
                var body = new JObject
                {
                    ["name"] = RequireArgument(args, "name"),
                    ["platforms"] = RequireArgument(args, "platforms"),
                    ["technologies"] = GetArgument(args, "technologies", "mdm"),
                    ["settings"] = settings
                };
                var desc = GetArgument(args, "description");
                if (!string.IsNullOrWhiteSpace(desc)) body["description"] = desc;
                return await GraphSendAsync(HttpMethod.Post, "/deviceManagement/configurationPolicies", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("assign_configuration_policy",
            "Assign a settings-catalog configuration policy to Entra groups. This replaces the complete set of existing assignments on the policy.",
            schema: s => s
                .String("policy_id", "The deviceManagementConfigurationPolicy id", required: true)
                .Array("group_ids", "Entra group object ids to include", new JObject { ["type"] = "string" })
                .Array("exclude_group_ids", "Entra group object ids to exclude", new JObject { ["type"] = "string" })
                .Boolean("all_devices", "Assign to all devices instead of specific groups")
                .Boolean("all_users", "Assign to all licensed users instead of specific groups"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "policy_id");
                var body = new JObject { ["assignments"] = BuildAssignments(args) };
                return await GraphSendAsync(HttpMethod.Post, $"/deviceManagement/configurationPolicies/{Esc(id)}/assign", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });
    }

    // ── I. Applications (8) ──────────────────────────────────────────────

    private void RegisterAppTools(McpRequestHandler h)
    {
        h.AddTool("list_mobile_apps",
            "List applications published in Intune across every platform and package type — store apps, Win32 LOB apps, web links, Microsoft 365 apps, and VPP apps.",
            schema: s => s
                .String("filter", "OData $filter, for example \"isof('microsoft.graph.win32LobApp')\" or \"contains(displayName,'Teams')\"")
                .String("select", "Comma-separated fields to return")
                .String("expand", "Navigation properties to expand, for example \"assignments\" or \"categories\"")
                .String("orderby", "Sort expression, for example \"displayName asc\"")
                .Integer("top", "Maximum applications to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceAppManagement/mobileApps" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_mobile_app",
            "Get one application with its full package metadata, detection rules, requirements, and optionally its assignments and content versions.",
            schema: s => s
                .String("app_id", "The mobileApp id", required: true)
                .String("expand", "Navigation properties to expand, for example \"assignments,categories\""),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "app_id");
                return await GraphGetAsync($"/deviceAppManagement/mobileApps/{Esc(id)}" + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("create_mobile_app",
            "Create an application in Intune. The body must include an @odata.type discriminator naming the app type, for example \"#microsoft.graph.win32LobApp\", \"#microsoft.graph.iosStoreApp\", \"#microsoft.graph.webApp\", or \"#microsoft.graph.officeSuiteApp\". Win32 LOB apps additionally require a content version and file upload, which is not performed by this tool.",
            schema: s => s
                .String("odata_type", "Application type discriminator, for example \"#microsoft.graph.webApp\"", required: true)
                .String("display_name", "Application display name", required: true)
                .String("publisher", "Application publisher", required: true)
                .String("description", "Application description")
                .String("properties_json", "JSON object of type-specific application properties, for example {\"appUrl\":\"https://contoso.com\"}"),
            handler: async (args, ct) =>
            {
                var body = ParseJsonArgument(args, "properties_json") ?? new JObject();
                body["@odata.type"] = RequireArgument(args, "odata_type");
                body["displayName"] = RequireArgument(args, "display_name");
                body["publisher"] = RequireArgument(args, "publisher");
                var desc = GetArgument(args, "description");
                if (!string.IsNullOrWhiteSpace(desc)) body["description"] = desc;
                return await GraphSendAsync(HttpMethod.Post, "/deviceAppManagement/mobileApps", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("update_mobile_app",
            "Update an application's metadata, detection rules, or requirements. Graph requires the @odata.type discriminator matching the existing application on every PATCH.",
            schema: s => s
                .String("app_id", "The mobileApp id", required: true)
                .String("odata_type", "Application type discriminator matching the existing app", required: true)
                .String("updates_json", "JSON object of properties to change", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "app_id");
                var body = ParseJsonArgument(args, "updates_json") ?? new JObject();
                body["@odata.type"] = RequireArgument(args, "odata_type");
                return await GraphSendAsync(new HttpMethod("PATCH"), $"/deviceAppManagement/mobileApps/{Esc(id)}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_mobile_app",
            "Delete an application from Intune. Devices with a required assignment may uninstall it at their next check-in, depending on the assignment intent.",
            schema: s => s.String("app_id", "The mobileApp id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "app_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/deviceAppManagement/mobileApps/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("assign_mobile_app",
            "Assign an application to Entra groups with an install intent. Intent 'required' installs it silently, 'available' publishes it to the Company Portal, and 'uninstall' removes it. This replaces the complete set of existing assignments.",
            schema: s => s
                .String("app_id", "The mobileApp id", required: true)
                .String("intent", "Install intent for the included groups", required: true,
                    enumValues: new[] { "available", "required", "uninstall", "availableWithoutEnrollment" })
                .Array("group_ids", "Entra group object ids to include", new JObject { ["type"] = "string" })
                .Array("exclude_group_ids", "Entra group object ids to exclude", new JObject { ["type"] = "string" })
                .Boolean("all_devices", "Assign to all devices instead of specific groups")
                .Boolean("all_users", "Assign to all licensed users instead of specific groups"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "app_id");
                var intent = RequireArgument(args, "intent");
                var assignments = BuildAssignments(args);
                foreach (var assignment in assignments.OfType<JObject>())
                {
                    assignment["@odata.type"] = "#microsoft.graph.mobileAppAssignment";
                    assignment["intent"] = assignment["target"]?["@odata.type"]?.ToString() ==
                        "#microsoft.graph.exclusionGroupAssignmentTarget" ? "required" : intent;
                }
                var body = new JObject { ["mobileAppAssignments"] = assignments };
                return await GraphSendAsync(HttpMethod.Post, $"/deviceAppManagement/mobileApps/{Esc(id)}/assign", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("get_app_install_status",
            "Get installation status for an application — per-device results, per-user results, or the tenant-wide summary of installed, failed, pending, and not-applicable counts.",
            schema: s => s
                .String("app_id", "The mobileApp id", required: true)
                .String("scope", "Whether to report by device, by user, or as a summary", enumValues: new[] { "device", "user", "summary" })
                .Integer("top", "Maximum status rows to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "app_id");
                var scope = GetArgument(args, "scope", "device").ToLowerInvariant();
                string leaf;
                if (scope == "user") leaf = "userStatuses";
                else if (scope == "summary") leaf = "installSummary";
                else leaf = "deviceStatuses";
                var query = scope == "summary" ? string.Empty : BuildQuery(args);
                return await GraphGetAsync($"/deviceAppManagement/mobileApps/{Esc(id)}/{leaf}" + query, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_app_protection_policies",
            "List Intune app protection (MAM) policies, which control data handling inside managed applications on iOS, Android, and Windows — including copy and paste restrictions, save-as blocking, and PIN requirements.",
            schema: s => s
                .String("platform", "Which platform's policies to list", enumValues: new[] { "ios", "android", "windows", "all" })
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum policies to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var platform = GetArgument(args, "platform", "all").ToLowerInvariant();
                string path;
                if (platform == "ios") path = "/deviceAppManagement/iosManagedAppProtections";
                else if (platform == "android") path = "/deviceAppManagement/androidManagedAppProtections";
                else if (platform == "windows") path = "/deviceAppManagement/windowsManagedAppProtections";
                else path = "/deviceAppManagement/managedAppPolicies";
                return await GraphGetAsync(path + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }


    // ── J. Enrollment and Autopilot (7) ──────────────────────────────────

    private void RegisterEnrollmentTools(McpRequestHandler h)
    {
        h.AddTool("list_autopilot_devices",
            "List Windows Autopilot device identities registered in the tenant, with their hardware hash registration, group tag, assigned profile, and enrollment state.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(serialNumber,'ABC')\" or \"groupTag eq 'Kiosk'\"")
                .String("select", "Comma-separated fields to return")
                .String("orderby", "Sort expression, for example \"lastContactedDateTime desc\"")
                .Integer("top", "Maximum devices to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/windowsAutopilotDeviceIdentities" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_autopilot_device",
            "Get one Autopilot device identity, including its serial number, model, group tag, assigned deployment profile, and current deployment profile assignment status.",
            schema: s => s.String("autopilot_device_id", "The windowsAutopilotDeviceIdentity id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "autopilot_device_id");
                return await GraphGetAsync($"/deviceManagement/windowsAutopilotDeviceIdentities/{Esc(id)}", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("assign_autopilot_user",
            "Assign or unassign the user who will be pre-provisioned on an Autopilot device. Supply user_principal_name to assign a user, or omit it to remove the current assignment.",
            schema: s => s
                .String("autopilot_device_id", "The windowsAutopilotDeviceIdentity id", required: true)
                .String("user_principal_name", "User principal name to assign; omit to unassign the current user")
                .String("display_name", "Friendly name shown on the Autopilot sign-in screen"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "autopilot_device_id");
                var upn = GetArgument(args, "user_principal_name");
                if (string.IsNullOrWhiteSpace(upn))
                {
                    return await GraphSendAsync(HttpMethod.Post,
                        $"/deviceManagement/windowsAutopilotDeviceIdentities/{Esc(id)}/unassignUserFromDevice", new JObject(), ct);
                }

                var body = new JObject { ["userPrincipalName"] = upn };
                var display = GetArgument(args, "display_name");
                if (!string.IsNullOrWhiteSpace(display)) body["addressableUserName"] = display;
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/windowsAutopilotDeviceIdentities/{Esc(id)}/assignUserToDevice", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("delete_autopilot_device",
            "Deregister a device from Windows Autopilot. The device will no longer receive an Autopilot deployment profile and must be re-registered by hardware hash to return to Autopilot.",
            schema: s => s.String("autopilot_device_id", "The windowsAutopilotDeviceIdentity id", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "autopilot_device_id");
                return await GraphSendAsync(HttpMethod.Delete, $"/deviceManagement/windowsAutopilotDeviceIdentities/{Esc(id)}", null, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_autopilot_profiles",
            "List Windows Autopilot deployment profiles, which define the out-of-box experience applied to a device during provisioning — join type, naming template, and OOBE settings.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(displayName,'Standard')\"")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum profiles to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/windowsAutopilotDeploymentProfiles" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("assign_autopilot_profile",
            "Assign an Autopilot deployment profile to Entra groups. Devices in those groups receive this out-of-box experience at their next provisioning. This replaces the complete set of existing assignments.",
            schema: s => s
                .String("profile_id", "The windowsAutopilotDeploymentProfile id", required: true)
                .Array("group_ids", "Entra group object ids to include", new JObject { ["type"] = "string" })
                .Array("exclude_group_ids", "Entra group object ids to exclude", new JObject { ["type"] = "string" })
                .Boolean("all_devices", "Assign to all devices instead of specific groups"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "profile_id");
                var body = new JObject { ["assignments"] = BuildAssignments(args) };
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/windowsAutopilotDeploymentProfiles/{Esc(id)}/assign", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("list_enrollment_configurations",
            "List device enrollment configurations — enrollment restrictions, the Enrollment Status Page, Windows Hello for Business settings, and enrollment limits — in the priority order Intune evaluates them.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(displayName,'ESP')\"")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum configurations to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/deviceEnrollmentConfigurations" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── K. Scripts and Remediations (6) ──────────────────────────────────

    private void RegisterScriptTools(McpRequestHandler h)
    {
        h.AddTool("list_device_scripts",
            "List management scripts deployed through Intune — Windows PowerShell scripts, macOS shell scripts, or macOS custom attribute scripts. Choose the script type with the script_type parameter.",
            schema: s => s
                .String("script_type", "Which script family to list", enumValues: new[] { "powershell", "shell", "customAttribute" })
                .String("filter", "OData $filter, for example \"contains(displayName,'Install')\"")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum scripts to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync(ScriptCollection(args) + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_device_script",
            "Get one management script including its full script content, execution context, signature check setting, and retry behaviour.",
            schema: s => s
                .String("script_id", "The script id", required: true)
                .String("script_type", "Which script family the id belongs to", enumValues: new[] { "powershell", "shell", "customAttribute" }),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "script_id");
                return await GraphGetAsync($"{ScriptCollection(args)}/{Esc(id)}", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("assign_device_script",
            "Assign a management script to Entra groups so it runs on their devices. This replaces the complete set of existing assignments on the script.",
            schema: s => s
                .String("script_id", "The script id", required: true)
                .String("script_type", "Which script family the id belongs to", enumValues: new[] { "powershell", "shell", "customAttribute" })
                .Array("group_ids", "Entra group object ids to include", new JObject { ["type"] = "string" })
                .Array("exclude_group_ids", "Entra group object ids to exclude", new JObject { ["type"] = "string" })
                .Boolean("all_devices", "Assign to all devices instead of specific groups"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "script_id");
                var assignments = new JArray();
                foreach (var assignment in BuildAssignments(args).OfType<JObject>())
                    assignments.Add(new JObject { ["target"] = assignment["target"] });
                var body = new JObject { ["deviceManagementScriptAssignments"] = assignments };
                return await GraphSendAsync(HttpMethod.Post, $"{ScriptCollection(args)}/{Esc(id)}/assign", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = true; });

        h.AddTool("get_script_run_states",
            "Get the execution results of a management script — per-device success and failure state, the last run time, and the captured script output. Use this to confirm a script actually ran.",
            schema: s => s
                .String("script_id", "The script id", required: true)
                .String("script_type", "Which script family the id belongs to", enumValues: new[] { "powershell", "shell", "customAttribute" })
                .String("scope", "Whether to report per device, per user, or as a run summary", enumValues: new[] { "device", "user", "summary" })
                .String("filter", "OData $filter, for example \"runState eq 'fail'\"")
                .Integer("top", "Maximum result rows to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "script_id");
                var scope = GetArgument(args, "scope", "device").ToLowerInvariant();
                string leaf;
                if (scope == "user") leaf = "userRunStates";
                else if (scope == "summary") leaf = "runSummary";
                else leaf = "deviceRunStates";
                var query = scope == "summary" ? string.Empty : BuildQuery(args);
                return await GraphGetAsync($"{ScriptCollection(args)}/{Esc(id)}/{leaf}" + query, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_health_scripts",
            "List proactive remediation scripts (device health scripts), each pairing a detection script with a remediation script that runs when the detection reports a problem.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(displayName,'Disk')\"")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum scripts to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/deviceHealthScripts" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_remediation_summary",
            "Get aggregate results for a proactive remediation — how many devices had the issue detected, how many were remediated successfully, and how many failed. Set history to true for the trend over time.",
            schema: s => s
                .String("script_id", "The deviceHealthScript id", required: true)
                .Boolean("history", "Return the historical trend instead of the current summary"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "script_id");
                var history = args.Value<bool?>("history") ?? false;
                var leaf = history ? "getRemediationHistory" : "getRemediationSummary";
                return await GraphGetAsync($"/deviceManagement/deviceHealthScripts/{Esc(id)}/{leaf}", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── L. Windows Update and Servicing (3) ──────────────────────────────

    private void RegisterUpdateTools(McpRequestHandler h)
    {
        h.AddTool("list_feature_update_profiles",
            "List Windows feature update profiles, which pin devices to a specific Windows feature release and control the rollout schedule for major version upgrades.",
            schema: s => s
                .String("filter", "OData $filter, for example \"contains(displayName,'23H2')\"")
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum profiles to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/windowsFeatureUpdateProfiles" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_quality_update_policies",
            "List Windows quality update policies and profiles, which control monthly security and reliability update approval, expedited rollout, and deployment deferral.",
            schema: s => s
                .String("kind", "Whether to list the newer quality update policies or the classic profiles", enumValues: new[] { "policies", "profiles" })
                .String("expand", "Navigation properties to expand, for example \"assignments\"")
                .Integer("top", "Maximum items to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var kind = GetArgument(args, "kind", "policies").ToLowerInvariant();
                var path = kind == "profiles"
                    ? "/deviceManagement/windowsQualityUpdateProfiles"
                    : "/deviceManagement/windowsQualityUpdatePolicies";
                return await GraphGetAsync(path + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_driver_update_profiles",
            "List Windows driver update profiles and, when profile_id is supplied, the individual driver update inventory entries awaiting approval or already approved within that profile.",
            schema: s => s
                .String("profile_id", "A windowsDriverUpdateProfile id; supply this to list the drivers inside one profile")
                .String("filter", "OData $filter, for example \"approvalStatus eq 'needsReview'\"")
                .Integer("top", "Maximum items to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var id = GetArgument(args, "profile_id");
                var path = string.IsNullOrWhiteSpace(id)
                    ? "/deviceManagement/windowsDriverUpdateProfiles"
                    : $"/deviceManagement/windowsDriverUpdateProfiles/{Esc(id)}/driverInventories";
                return await GraphGetAsync(path + BuildQuery(args), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── M. Elevation and Operation Approvals (4) ─────────────────────────

    private void RegisterApprovalTools(McpRequestHandler h)
    {
        h.AddTool("list_elevation_requests",
            "List Endpoint Privilege Management elevation requests — cases where a standard user asked to run an application with administrator rights — with the requester, the application, the justification, and the review state.",
            schema: s => s
                .String("filter", "OData $filter, for example \"status eq 'pending'\"")
                .String("orderby", "Sort expression, for example \"requestCreatedDateTime desc\"")
                .Integer("top", "Maximum requests to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/elevationRequests" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("approve_elevation_request",
            "Approve or deny an Endpoint Privilege Management elevation request. Approving grants the requesting user administrator rights for the named application under the configured elevation rules.",
            schema: s => s
                .String("request_id", "The elevation request id", required: true)
                .String("decision", "Whether to approve or deny the request", required: true, enumValues: new[] { "approve", "deny" })
                .String("justification", "Reviewer justification recorded with the decision"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "request_id");
                var decision = RequireArgument(args, "decision").ToLowerInvariant();
                var body = new JObject { ["justification"] = GetArgument(args, "justification", string.Empty) };
                var leaf = decision == "deny" ? "deny" : "approve";
                return await GraphSendAsync(HttpMethod.Post, $"/deviceManagement/elevationRequests/{Esc(id)}/{leaf}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("list_approval_requests",
            "List Intune operation approval requests. When multiple administrator approval is enabled, privileged changes such as script deployment or device wipe are held here until a second administrator reviews them.",
            schema: s => s
                .String("filter", "OData $filter, for example \"status eq 'needsApproval'\"")
                .String("orderby", "Sort expression, for example \"requestDateTime desc\"")
                .Integer("top", "Maximum requests to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/operationApprovalRequests" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("decide_operation_request",
            "Approve, reject, or cancel an Intune operation approval request. Approving releases the held privileged change for execution, so confirm what the request contains before deciding.",
            schema: s => s
                .String("request_id", "The operationApprovalRequest id", required: true)
                .String("decision", "The decision to record", required: true, enumValues: new[] { "approve", "reject", "cancel" })
                .String("justification", "Reviewer justification recorded with the decision"),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "request_id");
                var decision = RequireArgument(args, "decision").ToLowerInvariant();
                var body = new JObject { ["justification"] = GetArgument(args, "justification", string.Empty) };
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/operationApprovalRequests/{Esc(id)}/{decision}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });
    }

    // ── N. Cloud PC and Windows 365 (4) ──────────────────────────────────

    private void RegisterCloudPcTools(McpRequestHandler h)
    {
        h.AddTool("list_cloud_pcs",
            "List Windows 365 Cloud PCs provisioned in the tenant, with their assigned user, provisioning policy, service plan, status, and last connection time.",
            schema: s => s
                .String("filter", "OData $filter, for example \"status eq 'failed'\" or \"userPrincipalName eq 'user@contoso.com'\"")
                .String("select", "Comma-separated fields to return")
                .Integer("top", "Maximum Cloud PCs to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/virtualEndpoint/cloudPCs" + BuildQuery(args), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("reprovision_cloud_pc",
            "Reprovision a Cloud PC, rebuilding it from its provisioning policy. All data stored on the Cloud PC is destroyed, so confirm with the user before running this.",
            schema: s => s
                .String("cloud_pc_id", "The cloudPC id", required: true)
                .String("os_version", "Windows version to reprovision to, for example \"windows11\"")
                .String("user_account_type", "Local account type on the rebuilt Cloud PC", enumValues: new[] { "standardUser", "administrator" }),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "cloud_pc_id");
                var body = new JObject();
                var os = GetArgument(args, "os_version");
                if (!string.IsNullOrWhiteSpace(os)) body["osVersion"] = os;
                var accountType = GetArgument(args, "user_account_type");
                if (!string.IsNullOrWhiteSpace(accountType)) body["userAccountType"] = accountType;
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/virtualEndpoint/cloudPCs/{Esc(id)}/reprovision", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("resize_cloud_pc",
            "Resize a Cloud PC to a different service plan, changing its vCPU, RAM, and storage. User data on the OS disk is preserved through the resize.",
            schema: s => s
                .String("cloud_pc_id", "The cloudPC id", required: true)
                .String("target_service_plan_id", "The cloudPcServicePlan id to resize to", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "cloud_pc_id");
                var body = new JObject { ["targetServicePlanId"] = RequireArgument(args, "target_service_plan_id") };
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/virtualEndpoint/cloudPCs/{Esc(id)}/resize", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["idempotentHint"] = false; });

        h.AddTool("restore_cloud_pc",
            "Restore a Cloud PC to an earlier snapshot. Every change made after the chosen restore point is lost, so confirm the snapshot with the user first.",
            schema: s => s
                .String("cloud_pc_id", "The cloudPC id", required: true)
                .String("restore_point_id", "The cloudPcRestorePoint id to restore to", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "cloud_pc_id");
                var body = new JObject { ["cloudPcSnapshotId"] = RequireArgument(args, "restore_point_id") };
                return await GraphSendAsync(HttpMethod.Post,
                    $"/deviceManagement/virtualEndpoint/cloudPCs/{Esc(id)}/restore", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = false; a["destructiveHint"] = true; a["idempotentHint"] = false; });
    }


    // ── O. Assignment Filters (2) ────────────────────────────────────────

    private void RegisterAssignmentFilterTools(McpRequestHandler h)
    {
        h.AddTool("list_assignment_filters",
            "List Intune assignment filters. Filters narrow a group assignment by device properties such as model, OS version, or ownership, so one group can receive different policy on different hardware.",
            schema: s => s
                .String("filter", "OData $filter, for example \"platform eq 'windows10AndLater'\"")
                .Integer("top", "Maximum filters to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/assignmentFilters" + BuildQuery(args, 25), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("evaluate_assignment_filter",
            "Evaluate an assignment filter rule to see which devices it matches before you use it in an assignment. Supply rule text to validate a candidate rule, or filter_id to inspect an existing filter's current match state.",
            schema: s => s
                .String("filter_id", "An existing assignmentFilter id to evaluate; omit when validating rule text")
                .String("rule", "Filter rule expression to validate, for example \"(device.operatingSystem -eq \\\"Windows\\\")\"")
                .String("platform", "Platform the rule targets, for example \"windows10AndLater\", \"iOS\", \"macOS\", or \"androidWorkProfile\"")
                .Integer("top", "Maximum matching devices to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var rule = GetArgument(args, "rule");
                if (!string.IsNullOrWhiteSpace(rule))
                {
                    var body = new JObject
                    {
                        ["rule"] = rule,
                        ["platform"] = GetArgument(args, "platform", "windows10AndLater")
                    };
                    return await GraphSendAsync(HttpMethod.Post, "/deviceManagement/assignmentFilters/validateFilter", body, ct);
                }

                var id = RequireArgument(args, "filter_id");
                return await GraphGetAsync(
                    $"/deviceManagement/assignmentFilters/{Esc(id)}?$expand=payloads", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── P. Role-Based Access Control (3) ─────────────────────────────────

    private void RegisterRbacTools(McpRequestHandler h)
    {
        h.AddTool("list_role_definitions",
            "List Intune RBAC role definitions, both the Microsoft built-in roles and any custom roles, with the resource operations each role permits.",
            schema: s => s
                .String("filter", "OData $filter, for example \"isBuiltIn eq false\"")
                .Integer("top", "Maximum roles to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/roleDefinitions" + BuildQuery(args, 25), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_role_assignments",
            "List Intune RBAC role assignments, showing which administrators or groups hold which role over which scope tags. Supply role_id to list only the assignments of one role.",
            schema: s => s
                .String("role_id", "A roleDefinition id to list only that role's assignments")
                .Integer("top", "Maximum assignments to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var roleId = GetArgument(args, "role_id");
                var path = string.IsNullOrWhiteSpace(roleId)
                    ? "/deviceManagement/roleAssignments"
                    : $"/deviceManagement/roleDefinitions/{Esc(roleId)}/roleAssignments";
                return await GraphGetAsync(path + BuildQuery(args, 25), ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_effective_permissions",
            "Get the effective Intune permissions of the signed-in administrator, including the resource operations they may perform and the scope tags they are limited to. Use this to explain an authorization failure.",
            schema: s => s.String("scope", "Optional resource scope to evaluate, for example \"ManagedDevices\""),
            handler: async (args, ct) =>
            {
                var scope = GetArgument(args, "scope");
                var path = "/deviceManagement/getEffectivePermissions";
                if (!string.IsNullOrWhiteSpace(scope))
                    path += "(scope='" + Uri.EscapeDataString(scope) + "')";
                return await GraphGetAsync(path, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── Q. Tenant Certificates and Tokens (3) ────────────────────────────

    private void RegisterTenantTools(McpRequestHandler h)
    {
        h.AddTool("get_apple_push_certificate",
            "Get the Apple MDM Push Certificate (APNs) for the tenant, including its expiration date and Apple ID. If this certificate expires, every managed Apple device loses management, so check the expiry proactively.",
            schema: s => { },
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/applePushNotificationCertificate", ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_vpp_tokens",
            "List Apple Volume Purchase Program tokens with their expiry, last sync time, and state. An expired VPP token stops app license assignment for Apple devices.",
            schema: s => s.Integer("top", "Maximum tokens to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceAppManagement/vppTokens" + BuildQuery(args, 25), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_dep_tokens",
            "List Apple Automated Device Enrollment (DEP or ABM) onboarding tokens with their expiry and last sync time. An expired token stops new Apple devices from enrolling automatically.",
            schema: s => s.Integer("top", "Maximum tokens to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/depOnboardingSettings" + BuildQuery(args, 25), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── R. Reporting (4) ─────────────────────────────────────────────────

    private void RegisterReportingTools(McpRequestHandler h)
    {
        h.AddTool("list_intune_reports",
            "List every Intune report available through the Graph reporting endpoint, optionally narrowed by a search term. Call this first to discover the exact report name to pass to run_intune_report or export_intune_report.",
            schema: s => s.String("search", "Optional term to narrow the list, for example \"compliance\", \"malware\", \"update\", or \"app\""),
            handler: async (args, ct) =>
            {
                var search = GetArgument(args, "search");
                var names = ReportNames.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(search))
                    names = names.Where(n => n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

                var list = names.ToList();
                return new JObject
                {
                    ["count"] = list.Count,
                    ["totalAvailable"] = ReportNames.Length,
                    ["reports"] = new JArray(list),
                    ["usage"] = "Pass one of these names as report_name to run_intune_report for synchronous results, or to export_intune_report for a downloadable file."
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("run_intune_report",
            "Run an Intune report synchronously and return its rows. Reports return a schema plus a values array of positional rows. Use list_intune_reports to discover report names, and export_intune_report instead when the result set is large.",
            schema: s => s
                .String("report_name", "The report to run, for example \"getDeviceNonComplianceReport\"", required: true)
                .String("filter", "Report filter expression, for example \"(ComplianceState eq '1')\"")
                .String("select", "Comma-separated columns to return")
                .String("orderby", "Comma-separated columns to sort by")
                .Integer("skip", "Rows to skip for paging")
                .Integer("top", "Maximum rows to return (default 50)", defaultValue: 50)
                .String("search", "Free-text search across the report rows")
                .String("group_by", "Comma-separated columns to group by, where the report supports grouping"),
            handler: async (args, ct) =>
            {
                var name = RequireArgument(args, "report_name");
                if (!ReportNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"'{name}' is not a known Intune report. Call list_intune_reports to see the {ReportNames.Length} available names.");

                var body = new JObject();
                var filter = GetArgument(args, "filter");
                if (!string.IsNullOrWhiteSpace(filter)) body["filter"] = filter;
                var select = GetArgument(args, "select");
                if (!string.IsNullOrWhiteSpace(select)) body["select"] = new JArray(SplitList(select));
                var orderBy = GetArgument(args, "orderby");
                if (!string.IsNullOrWhiteSpace(orderBy)) body["orderBy"] = new JArray(SplitList(orderBy));
                var groupBy = GetArgument(args, "group_by");
                if (!string.IsNullOrWhiteSpace(groupBy)) body["groupBy"] = new JArray(SplitList(groupBy));
                var search = GetArgument(args, "search");
                if (!string.IsNullOrWhiteSpace(search)) body["search"] = search;
                body["skip"] = args.Value<int?>("skip") ?? 0;
                body["top"] = args.Value<int?>("top") ?? 50;

                return await GraphSendAsync(HttpMethod.Post, $"/deviceManagement/reports/{name}", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; a["openWorldHint"] = true; });

        h.AddTool("export_intune_report",
            "Start an asynchronous export of an Intune report to a downloadable CSV or JSON file. Exports handle far larger result sets than run_intune_report. Poll get_export_job_status with the returned job id until the status is completed, then download the returned URL.",
            schema: s => s
                .String("report_name", "The report to export, for example \"DeviceCompliance\" or \"getDeviceNonComplianceReport\"", required: true)
                .String("filter", "Report filter expression")
                .String("select", "Comma-separated columns to include")
                .String("format", "Output file format", enumValues: new[] { "csv", "json" })
                .String("localization_type", "How enum values are rendered in the export",
                    enumValues: new[] { "localizedValuesAsAdditionalColumn", "replaceLocalizableValues" }),
            handler: async (args, ct) =>
            {
                var body = new JObject { ["reportName"] = RequireArgument(args, "report_name") };
                var filter = GetArgument(args, "filter");
                if (!string.IsNullOrWhiteSpace(filter)) body["filter"] = filter;
                var select = GetArgument(args, "select");
                if (!string.IsNullOrWhiteSpace(select)) body["select"] = new JArray(SplitList(select));
                body["format"] = GetArgument(args, "format", "csv");
                body["localizationType"] = GetArgument(args, "localization_type", "localizedValuesAsAdditionalColumn");

                return await GraphSendAsync(HttpMethod.Post, "/deviceManagement/reports/exportJobs", body, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = false; });

        h.AddTool("get_export_job_status",
            "Check the status of a report export job started by export_intune_report. When status is 'completed' the response contains a time-limited download URL for the exported file.",
            schema: s => s.String("job_id", "The exportJob id returned by export_intune_report", required: true),
            handler: async (args, ct) =>
            {
                var id = RequireArgument(args, "job_id");
                return await GraphGetAsync($"/deviceManagement/reports/exportJobs/{Uri.EscapeDataString(id)}", ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── S. Monitoring and Analytics (3) ──────────────────────────────────

    private void RegisterMonitoringTools(McpRequestHandler h)
    {
        h.AddTool("list_audit_events",
            "List Intune audit events — every administrative change made in the tenant, with the actor, the activity, the affected object, and the result. Use this to answer who changed a policy and when.",
            schema: s => s
                .String("filter", "OData $filter, for example \"activityDateTime ge 2026-01-01T00:00:00Z\" or \"actor/userPrincipalName eq 'admin@contoso.com'\"")
                .String("orderby", "Sort expression, for example \"activityDateTime desc\"")
                .Integer("top", "Maximum events to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/auditEvents" + BuildQuery(args, 25), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("list_alert_records",
            "List Intune monitoring alert records raised against configured alert rules, such as Cloud PC provisioning failures or Autopilot deployment problems, with their severity and current state.",
            schema: s => s
                .String("filter", "OData $filter, for example \"status eq 'active'\" or \"severity eq 'critical'\"")
                .String("orderby", "Sort expression, for example \"detectedDateTime desc\"")
                .Integer("top", "Maximum alert records to return (default 25)", defaultValue: 25),
            handler: async (args, ct) => await GraphGetAsync("/deviceManagement/monitoring/alertRecords" + BuildQuery(args, 25), ct),
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("get_ux_analytics_overview",
            "Get Endpoint Analytics results — the tenant endpoint analytics score and baseline, per-device experience scores, startup performance, application reliability, battery health, or work-from-anywhere readiness.",
            schema: s => s
                .String("area", "Which analytics area to return", required: true,
                    enumValues: new[] { "overview", "baselines", "deviceScores", "devicePerformance", "appHealth", "batteryHealth", "workFromAnywhere", "remoteConnection", "resourcePerformance" })
                .String("filter", "OData $filter over the selected area")
                .String("orderby", "Sort expression, for example \"endpointAnalyticsScore asc\"")
                .Integer("top", "Maximum rows to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var area = RequireArgument(args, "area");
                string path;
                switch (area)
                {
                    case "baselines": path = "/deviceManagement/userExperienceAnalyticsBaselines"; break;
                    case "deviceScores": path = "/deviceManagement/userExperienceAnalyticsDeviceScores"; break;
                    case "devicePerformance": path = "/deviceManagement/userExperienceAnalyticsDevicePerformance"; break;
                    case "appHealth": path = "/deviceManagement/userExperienceAnalyticsAppHealthApplicationPerformance"; break;
                    case "batteryHealth": path = "/deviceManagement/userExperienceAnalyticsBatteryHealthDevicePerformance"; break;
                    case "workFromAnywhere": path = "/deviceManagement/userExperienceAnalyticsWorkFromAnywhereMetrics"; break;
                    case "remoteConnection": path = "/deviceManagement/userExperienceAnalyticsRemoteConnection"; break;
                    case "resourcePerformance": path = "/deviceManagement/userExperienceAnalyticsResourcePerformance"; break;
                    default: path = "/deviceManagement/userExperienceAnalyticsOverview"; break;
                }
                var query = area == "overview" ? string.Empty : BuildQuery(args, 25);
                return await GraphGetAsync(path + query, ct);
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });
    }

    // ── T. Universal Access (4) ──────────────────────────────────────────
    //
    //    Intune exposes roughly 1,650 Graph operations. The named tools above
    //    cover the highest-value surface; these four reach everything else.
    //

    private void RegisterUniversalTools(McpRequestHandler h)
    {
        h.AddTool("intune_find_endpoint",
            "Search the complete Intune Graph endpoint catalog by keyword. Returns matching paths with their HTTP methods. Use this whenever no named tool covers what you need, then call the result with intune_graph_request.",
            schema: s => s
                .String("query", "Keywords to search for, for example \"tunnel\", \"terms and conditions\", \"zebra\", or \"group policy\"", required: true)
                .Integer("top", "Maximum matches to return (default 25)", defaultValue: 25),
            handler: async (args, ct) =>
            {
                var query = RequireArgument(args, "query");
                var terms = query.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var top = Math.Max(1, Math.Min(args.Value<int?>("top") ?? 25, 200));

                var scored = new List<KeyValuePair<int, string>>();
                foreach (var entry in EndpointIndex)
                {
                    var score = terms.Count(t => entry.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (score > 0) scored.Add(new KeyValuePair<int, string>(score, entry));
                }

                var matches = scored
                    .OrderByDescending(kv => kv.Key)
                    .ThenBy(kv => kv.Value.Length)
                    .Take(top)
                    .Select(kv => kv.Value)
                    .ToList();

                return new JObject
                {
                    ["query"] = query,
                    ["matchCount"] = matches.Count,
                    ["catalogSize"] = EndpointIndex.Length,
                    ["endpoints"] = new JArray(matches),
                    ["usage"] = "Each entry is \"METHODS /path\". Pass the path to intune_graph_request, replacing {id} with a real identifier."
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("intune_describe_entity",
            "Discover the fields available on any Intune Graph collection by sampling one live record and reporting its property names and value types. Use this before filtering or selecting on a collection you have not worked with before.",
            schema: s => s
                .String("path", "The Intune Graph collection path to sample, for example \"/deviceManagement/termsAndConditions\"", required: true),
            handler: async (args, ct) =>
            {
                var path = NormalizeIntunePath(RequireArgument(args, "path"));
                var separator = path.Contains("?") ? "&" : "?";
                var sample = await GraphGetAsync(path + separator + "$top=1", ct).ConfigureAwait(false);

                var record = sample["value"] is JArray array
                    ? array.FirstOrDefault() as JObject
                    : sample;

                if (record == null)
                    return new JObject
                    {
                        ["path"] = path,
                        ["fields"] = new JArray(),
                        ["note"] = "The collection returned no records, so no field shape could be inferred."
                    };

                var fields = new JArray();
                foreach (var property in record.Properties())
                {
                    fields.Add(new JObject
                    {
                        ["name"] = property.Name,
                        ["type"] = property.Value.Type.ToString(),
                        ["example"] = property.Value.Type == JTokenType.Object || property.Value.Type == JTokenType.Array
                            ? property.Value.ToString(Newtonsoft.Json.Formatting.None)
                            : property.Value.ToString()
                    });
                }

                return new JObject
                {
                    ["path"] = path,
                    ["fieldCount"] = fields.Count,
                    ["fields"] = fields,
                    ["note"] = "Fields are inferred from one live record. Properties that are null on this record may not appear."
                };
            },
            annotations: a => { a["readOnlyHint"] = true; a["idempotentHint"] = true; });

        h.AddTool("intune_graph_request",
            "Call any Microsoft Intune Graph endpoint directly. The connector uses v1.0 when available and falls back to beta only when necessary. This reaches the full Intune surface, including every endpoint without a dedicated tool. Use intune_find_endpoint to locate the correct path first. Requests are restricted to Intune management namespaces.",
            schema: s => s
                .String("path", "Graph path beginning with /deviceManagement, /deviceAppManagement, /informationProtection/bitlocker, or /admin/windows/updates. Include any query string.", required: true)
                .String("method", "HTTP method", enumValues: new[] { "GET", "POST", "PATCH", "PUT", "DELETE" })
                .String("body_json", "JSON request body for POST, PATCH, and PUT")
                .String("api_version", "Graph version to call. Auto prefers v1.0 and falls back to beta when the operation is unavailable.", enumValues: new[] { "auto", "v1.0", "beta" }),
            handler: async (args, ct) =>
            {
                var path = NormalizeIntunePath(RequireArgument(args, "path"));
                var method = new HttpMethod(GetArgument(args, "method", "GET").ToUpperInvariant());
                var body = ParseJsonArgument(args, "body_json");
                var version = GetArgument(args, "api_version", "auto");
                return await GraphSendAsync(method, path, body, ct, version);
            },
            annotations: a => { a["readOnlyHint"] = false; a["openWorldHint"] = true; });

        h.AddTool("intune_graph_batch",
            "Run up to 20 Intune Graph requests. Requests are grouped into v1.0 and beta batches automatically so each operation uses the most stable available API.",
            schema: s => s
                .Array("requests", "The requests to run, each an object with 'method' and 'path', and optionally 'body' and 'api_version'",
                    new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["method"] = new JObject { ["type"] = "string", ["description"] = "HTTP method, for example GET" },
                            ["path"] = new JObject { ["type"] = "string", ["description"] = "Intune Graph path, for example /deviceManagement/managedDevices/{id}" },
                            ["body"] = new JObject { ["type"] = "object", ["description"] = "Optional JSON request body" },
                            ["api_version"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "Optional version override; auto prefers v1.0",
                                ["enum"] = new JArray("auto", "v1.0", "beta")
                            }
                        },
                        ["required"] = new JArray("method", "path")
                    },
                    required: true),
            handler: async (args, ct) =>
            {
                var requests = RequireArray(args, "requests");
                if (requests.Count > 20)
                    throw new ArgumentException("A maximum of 20 requests may be batched in one call");

                var stableBatch = new JArray();
                var betaBatch = new JArray();
                var entriesById = new Dictionary<string, JObject>(StringComparer.Ordinal);
                var automaticIds = new HashSet<string>(StringComparer.Ordinal);
                var index = 1;
                foreach (var item in requests.OfType<JObject>())
                {
                    var path = NormalizeIntunePath(item.Value<string>("path") ?? string.Empty);
                    var method = new HttpMethod((item.Value<string>("method") ?? "GET").ToUpperInvariant());
                    var requestedVersion = item.Value<string>("api_version") ?? "auto";
                    var version = ResolveGraphVersion(method, path, requestedVersion);
                    var id = index.ToString();
                    var entry = new JObject
                    {
                        ["id"] = id,
                        ["method"] = method.Method,
                        ["url"] = path
                    };

                    if (item["body"] is JObject requestBody)
                    {
                        entry["body"] = requestBody;
                        entry["headers"] = new JObject { ["Content-Type"] = "application/json" };
                    }

                    entriesById[id] = entry;
                    if (string.Equals(requestedVersion, "auto", StringComparison.OrdinalIgnoreCase))
                        automaticIds.Add(id);

                    if (string.Equals(version, "beta", StringComparison.OrdinalIgnoreCase))
                        betaBatch.Add(entry);
                    else
                        stableBatch.Add(entry);
                    index++;
                }

                var responses = new JArray();
                if (stableBatch.Count > 0)
                {
                    var stableResult = await GraphSendAsync(
                        HttpMethod.Post,
                        "/$batch",
                        new JObject { ["requests"] = stableBatch },
                        ct,
                        "v1.0").ConfigureAwait(false);
                    if (!(stableResult["responses"] is JArray stableResponses))
                        throw new InvalidOperationException("Microsoft Graph returned a v1.0 batch response without a responses array.");

                    foreach (var stableResponse in stableResponses.OfType<JObject>())
                    {
                        var id = stableResponse.Value<string>("id");
                        var status = stableResponse.Value<int?>("status");
                        var content = stableResponse["body"]?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(id)
                            && status.HasValue
                            && automaticIds.Contains(id)
                            && ShouldRetryWithBeta((HttpStatusCode)status.Value, content))
                        {
                            betaBatch.Add(entriesById[id].DeepClone());
                        }
                        else
                        {
                            responses.Add(stableResponse.DeepClone());
                        }
                    }
                }
                if (betaBatch.Count > 0)
                    AddBatchResponses(
                        responses,
                        await GraphSendAsync(
                            HttpMethod.Post,
                            "/$batch",
                            new JObject { ["requests"] = betaBatch },
                            ct,
                            "beta").ConfigureAwait(false));

                return new JObject
                {
                    ["responses"] = new JArray(
                        responses
                            .OfType<JObject>()
                            .OrderBy(response => int.Parse(response.Value<string>("id"))))
                };
            },
            annotations: a => { a["readOnlyHint"] = false; a["openWorldHint"] = true; });
    }

    // ── Resources ────────────────────────────────────────────────────────

    private void RegisterResources(McpRequestHandler handler)
    {
        handler.AddResource("intune://endpoints/catalog", "Intune Endpoint Catalog",
            "The complete catalog of Intune Graph endpoint paths and bound operations reachable through this connector.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "intune://endpoints/catalog",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["count"] = EndpointIndex.Length,
                        ["endpoints"] = new JArray(EndpointIndex)
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("intune://reports/catalog", "Intune Report Catalog",
            "Every report name accepted by run_intune_report and export_intune_report.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "intune://reports/catalog",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["count"] = ReportNames.Length,
                        ["reports"] = new JArray(ReportNames)
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });

        handler.AddResource("intune://permissions/scopes", "Required Graph Permissions",
            "The delegated Microsoft Graph permission scopes this connector uses, and what each one unlocks.",
            handler: async (ct) => new JArray
            {
                new JObject
                {
                    ["uri"] = "intune://permissions/scopes",
                    ["mimeType"] = "application/json",
                    ["text"] = new JObject
                    {
                        ["DeviceManagementManagedDevices.ReadWrite.All"] = "Read and manage managed devices, and run remote actions",
                        ["DeviceManagementManagedDevices.PrivilegedOperations.All"] = "Wipe, retire, reset passcode, and other privileged device actions",
                        ["DeviceManagementConfiguration.ReadWrite.All"] = "Read and manage compliance policies, configuration profiles, and the settings catalog",
                        ["DeviceManagementApps.ReadWrite.All"] = "Read and manage applications, app protection, and app configuration",
                        ["DeviceManagementServiceConfig.ReadWrite.All"] = "Read and manage enrollment, Autopilot, tokens, and tenant service configuration",
                        ["DeviceManagementRBAC.ReadWrite.All"] = "Read and manage Intune roles, assignments, and scope tags",
                        ["DeviceManagementScripts.ReadWrite.All"] = "Read and manage PowerShell scripts, shell scripts, and proactive remediations",
                        ["CloudPC.ReadWrite.All"] = "Read and manage Windows 365 Cloud PCs",
                        ["BitlockerKey.Read.All"] = "Read BitLocker recovery keys from Microsoft Entra ID",
                        ["User.Read"] = "Sign in and read the signed-in administrator's profile",
                        ["offline_access"] = "Maintain the connection with a refresh token"
                    }.ToString(Newtonsoft.Json.Formatting.Indented)
                }
            });
    }

    // ── Prompts ──────────────────────────────────────────────────────────

    private void RegisterPrompts(McpRequestHandler handler)
    {
        handler.AddPrompt("diagnose_device",
            "Walk through a structured diagnosis of a problem device, from identity through compliance, configuration, and sync state.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "device", Description = "Device name, serial number, or user email", Required = true },
                new McpPromptArgument { Name = "symptom", Description = "What the user reports is wrong", Required = false }
            },
            handler: async (args, ct) =>
            {
                var device = args.Value<string>("device") ?? string.Empty;
                var symptom = args.Value<string>("symptom");
                var symptomLine = string.IsNullOrWhiteSpace(symptom)
                    ? string.Empty
                    : $"\n\nThe reported symptom is: {symptom}";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Diagnose the Intune device \"{device}\".{symptomLine}\n\n" +
                                "Work through these steps and report what you find at each one:\n" +
                                "1. Use search_devices to resolve the device to a single managedDevice id. If more than one matches, list them and stop.\n" +
                                "2. Use get_managed_device to read its platform, OS version, ownership, and enrollment date.\n" +
                                "3. Use get_device_sync_status to confirm it is checking in. A device that has not synced recently explains most policy problems.\n" +
                                "4. If it is noncompliant, use get_noncompliant_settings to list the exact failing settings and their expected values.\n" +
                                "5. Use get_device_configuration_states to find configuration profiles in an error or conflict state.\n" +
                                "6. Summarize the root cause and recommend a remediation. Do not run any remote action without asking first."
                        }
                    }
                };
            });

        handler.AddPrompt("offboard_device",
            "Safely remove a device from management, choosing between retire and wipe based on ownership.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "device", Description = "Device name, serial number, or user email", Required = true },
                new McpPromptArgument { Name = "reason", Description = "Why the device is being offboarded, for example lost, stolen, or employee departure", Required = false }
            },
            handler: async (args, ct) =>
            {
                var device = args.Value<string>("device") ?? string.Empty;
                var reason = args.Value<string>("reason") ?? "not stated";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Offboard the Intune device \"{device}\". Reason: {reason}.\n\n" +
                                "Follow this sequence and confirm with me before any destructive step:\n" +
                                "1. Use search_devices to resolve the device, then get_managed_device to confirm its owner, ownership type, and platform.\n" +
                                "2. State clearly which device you are about to act on, including its serial number and assigned user.\n" +
                                "3. If the device is lost or stolen, propose remote_lock_device first, and enable_lost_mode for supervised Apple devices.\n" +
                                "4. Recommend retire_device for personally owned devices and wipe_device for corporate owned devices, and explain the difference.\n" +
                                "5. If the device holds recovery secrets, note that rotate_bitlocker_keys or rotate_filevault_key should follow.\n" +
                                "6. Ask for explicit confirmation before running the destructive action."
                        }
                    }
                };
            });

        handler.AddPrompt("compliance_audit",
            "Produce a tenant compliance audit summarizing noncompliant devices and the settings driving the failures.",
            arguments: new List<McpPromptArgument>
            {
                new McpPromptArgument { Name = "scope", Description = "Optional platform or group to scope the audit to", Required = false }
            },
            handler: async (args, ct) =>
            {
                var scope = args.Value<string>("scope");
                var scopeLine = string.IsNullOrWhiteSpace(scope)
                    ? "Cover the whole tenant."
                    : $"Scope the audit to: {scope}.";

                return new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                $"Produce an Intune compliance audit. {scopeLine}\n\n" +
                                "1. Use run_intune_report with getDeviceNonComplianceReport to get the current noncompliant device set.\n" +
                                "2. Use run_intune_report with getDevicesWithoutCompliancePolicyReport to find devices no policy targets at all — these are often the real gap.\n" +
                                "3. Use list_compliance_policies to see which policies exist and how they are assigned.\n" +
                                "4. For the largest failure clusters, use get_compliance_policy_statuses to quantify each policy's failure rate.\n" +
                                "5. Report the top failing settings, the number of devices affected by each, and a prioritized remediation plan."
                        }
                    }
                };
            });
    }


    // ── Graph Helpers ────────────────────────────────────────────────────

    /// <summary>Intune namespaces this connector is permitted to call.</summary>
    private static readonly string[] AllowedPathPrefixes =
    {
        "/deviceManagement",
        "/deviceAppManagement",
        "/informationProtection/bitlocker",
        "/admin/windows/updates",
        "/$batch"
    };

    /// <summary>
    /// Validate and normalize a caller-supplied Graph path. Rejects absolute URLs and
    /// anything outside the Intune management namespaces so the passthrough tools
    /// cannot be steered at unrelated Graph data.
    /// </summary>
    private static string NormalizeIntunePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("'path' is required");

        var trimmed = path.Trim();

        // Accept a full Graph URL by reducing it to its path and query.
        if (trimmed.StartsWith("https://graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);
            trimmed = uri.PathAndQuery;
            if (trimmed.StartsWith("/beta", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(5);
            else if (trimmed.StartsWith("/v1.0", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(5);
        }

        if (!trimmed.StartsWith("/")) trimmed = "/" + trimmed;

        if (trimmed.Contains(".."))
            throw new ArgumentException("'path' must not contain relative segments");

        var allowed = AllowedPathPrefixes.Any(prefix =>
            trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            throw new ArgumentException(
                $"'{trimmed}' is outside the Intune surface of this connector. Paths must begin with one of: {string.Join(", ", AllowedPathPrefixes)}.");

        return trimmed;
    }

    /// <summary>Issue a GET against Microsoft Graph and return the parsed response.</summary>
    private async Task<JObject> GraphGetAsync(string path, CancellationToken ct, string version = "auto")
    {
        return await GraphSendAsync(HttpMethod.Get, path, null, ct, version).ConfigureAwait(false);
    }

    /// <summary>
    /// Issue a request against Microsoft Graph, forwarding the connector's bearer token.
    /// Non-success responses are raised as exceptions so the MCP framework converts them
    /// into tool errors the model can read and correct.
    /// </summary>
    private async Task<JObject> GraphSendAsync(
        HttpMethod method, string path, JObject body, CancellationToken ct, string version = "auto")
    {
        var automatic = string.IsNullOrWhiteSpace(version)
            || string.Equals(version, "auto", StringComparison.OrdinalIgnoreCase);
        var selectedVersion = ResolveGraphVersion(method, path, version);
        var response = await SendGraphRequestAsync(method, path, body, ct, selectedVersion).ConfigureAwait(false);
        var content = response.Content == null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (automatic
            && string.Equals(selectedVersion, "v1.0", StringComparison.OrdinalIgnoreCase)
            && ShouldRetryWithBeta(response.StatusCode, content))
        {
            response.Dispose();
            selectedVersion = "beta";
            response = await SendGraphRequestAsync(method, path, body, ct, selectedVersion).ConfigureAwait(false);
            content = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = content;
            try
            {
                var error = JObject.Parse(content)["error"];
                if (error != null)
                    detail = $"{error["code"]}: {error["message"]}";
            }
            catch { }

            throw new Exception(
                $"Graph {selectedVersion} {method.Method} {path} failed ({(int)response.StatusCode}): {detail}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return new JObject { ["success"] = true, ["status"] = (int)response.StatusCode };

        try
        {
            var parsed = JToken.Parse(content);
            return parsed as JObject ?? new JObject { ["value"] = parsed };
        }
        catch
        {
            return new JObject { ["text"] = content };
        }
    }

    private async Task<HttpResponseMessage> SendGraphRequestAsync(
        HttpMethod method, string path, JObject body, CancellationToken ct, string version)
    {
        var baseUrl = string.Equals(version, "v1.0", StringComparison.OrdinalIgnoreCase) ? GraphV1 : GraphBeta;
        var request = new HttpRequestMessage(method, baseUrl + path);

        if (this.Context.Request.Headers.Authorization != null)
            request.Headers.Authorization = this.Context.Request.Headers.Authorization;

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "include-unknown-enum-members");

        if (body != null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(
                body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await this.Context.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static string ResolveGraphVersion(HttpMethod method, string path, string requestedVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion)
            && !string.Equals(requestedVersion, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(requestedVersion, "v1.0", StringComparison.OrdinalIgnoreCase))
                return "v1.0";
            if (string.Equals(requestedVersion, "beta", StringComparison.OrdinalIgnoreCase))
                return "beta";

            throw new ArgumentException(
                $"'version' must be 'auto', 'v1.0', or 'beta', not '{requestedVersion}'.");
        }

        var route = method.Method.ToUpperInvariant() + " " + StripQuery(path);
        if (StableGraphRouteOverrides.Any(template => GraphRouteMatches(route, template)))
            return "v1.0";

        return BetaGraphRoutes.Any(template => GraphRouteMatches(route, template))
            ? "beta"
            : "v1.0";
    }

    private static bool GraphRouteMatches(string route, string template)
    {
        var routeSegments = route.Split('/');
        var templateSegments = template.Split('/');
        if (routeSegments.Length != templateSegments.Length) return false;

        for (var index = 0; index < routeSegments.Length; index++)
        {
            var expected = templateSegments[index];
            var actual = routeSegments[index];
            var placeholderStart = expected.IndexOf('{');
            var placeholderEnd = expected.IndexOf('}');

            if (placeholderStart >= 0 && placeholderEnd > placeholderStart)
            {
                var prefix = expected.Substring(0, placeholderStart);
                var suffix = expected.Substring(placeholderEnd + 1);
                if (!actual.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !actual.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    || actual.Length < prefix.Length + suffix.Length)
                {
                    return false;
                }
            }
            else if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string StripQuery(string path)
    {
        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path.Substring(0, queryIndex) : path;
    }

    private static bool ShouldRetryWithBeta(HttpStatusCode statusCode, string content)
    {
        if (statusCode == HttpStatusCode.NotFound || statusCode == HttpStatusCode.MethodNotAllowed)
            return true;

        if (statusCode != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(content))
            return false;

        return content.IndexOf("Resource not found for the segment", StringComparison.OrdinalIgnoreCase) >= 0
            || content.IndexOf("No OData route exists", StringComparison.OrdinalIgnoreCase) >= 0
            || content.IndexOf("not supported in this version", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddBatchResponses(JArray destination, JObject batchResult)
    {
        if (!(batchResult?["responses"] is JArray responses))
            throw new InvalidOperationException("Microsoft Graph returned a batch response without a responses array.");

        foreach (var response in responses)
            destination.Add(response.DeepClone());
    }

    /// <summary>Run a bound managedDevice action such as syncDevice, retire, or wipe.</summary>
    private async Task<JObject> DeviceActionAsync(JObject args, string action, JObject body, CancellationToken ct)
    {
        var id = RequireArgument(args, "device_id");
        var result = await GraphSendAsync(HttpMethod.Post,
            $"/deviceManagement/managedDevices/{Esc(id)}/{action}", body ?? new JObject(), ct).ConfigureAwait(false);

        if (result["success"] != null || result.Count == 0)
        {
            return new JObject
            {
                ["deviceId"] = id,
                ["action"] = action,
                ["accepted"] = true,
                ["note"] = "Intune queued the action. The device performs it at its next check-in; poll get_managed_device to observe the result."
            };
        }

        result["deviceId"] = id;
        result["action"] = action;
        return result;
    }

    /// <summary>Add the Intune interop headers to a passthrough REST request.</summary>
    private void ApplyGraphHeaders(HttpRequestMessage request)
    {
        if (!request.Headers.Contains("Prefer"))
            request.Headers.TryAddWithoutValidation("Prefer", "include-unknown-enum-members");
    }

    private static void SetGraphVersion(HttpRequestMessage request, string version)
    {
        if (request?.RequestUri == null || !request.RequestUri.IsAbsoluteUri)
            throw new InvalidOperationException("The Graph request URI must be absolute before selecting an API version.");

        var builder = new UriBuilder(request.RequestUri);
        var updatedPath = Regex.Replace(
            builder.Path,
            @"^/(?:beta|v1\.0)(?=/|$)",
            "/" + version,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (string.Equals(updatedPath, builder.Path, StringComparison.Ordinal)
            && !builder.Path.StartsWith("/" + version + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The Graph request path '{builder.Path}' does not contain a supported API version.");
        }

        builder.Path = updatedPath;
        request.RequestUri = builder.Uri;
    }

    // ── Argument Helpers ─────────────────────────────────────────────────

    /// <summary>Build an OData query string from the standard paging and shaping arguments.</summary>
    private static string BuildQuery(JObject args, int defaultTop = 0)
    {
        if (args == null) return string.Empty;

        var parts = new List<string>();

        var filter = args.Value<string>("filter");
        if (!string.IsNullOrWhiteSpace(filter)) parts.Add("$filter=" + Uri.EscapeDataString(filter));

        var select = args.Value<string>("select");
        if (!string.IsNullOrWhiteSpace(select)) parts.Add("$select=" + Uri.EscapeDataString(select));

        var expand = args.Value<string>("expand");
        if (!string.IsNullOrWhiteSpace(expand)) parts.Add("$expand=" + Uri.EscapeDataString(expand));

        var orderby = args.Value<string>("orderby");
        if (!string.IsNullOrWhiteSpace(orderby)) parts.Add("$orderby=" + Uri.EscapeDataString(orderby));

        var search = args.Value<string>("search");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add("$search=" + Uri.EscapeDataString("\"" + search + "\""));

        var top = args.Value<int?>("top");
        if (top.HasValue && top.Value > 0) parts.Add("$top=" + top.Value);
        else if (defaultTop > 0) parts.Add("$top=" + defaultTop);

        var skip = args.Value<int?>("skip");
        if (skip.HasValue && skip.Value > 0) parts.Add("$skip=" + skip.Value);

        if (args.Value<bool?>("count") == true) parts.Add("$count=true");

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    /// <summary>Build a Graph assignment array from the shared assignment arguments.</summary>
    private static JArray BuildAssignments(JObject args)
    {
        var assignments = new JArray();

        if (args.Value<bool?>("all_devices") == true)
        {
            assignments.Add(new JObject
            {
                ["target"] = new JObject { ["@odata.type"] = "#microsoft.graph.allDevicesAssignmentTarget" }
            });
        }

        if (args.Value<bool?>("all_users") == true)
        {
            assignments.Add(new JObject
            {
                ["target"] = new JObject { ["@odata.type"] = "#microsoft.graph.allLicensedUsersAssignmentTarget" }
            });
        }

        if (args["group_ids"] is JArray includes)
        {
            foreach (var groupId in includes)
            {
                assignments.Add(new JObject
                {
                    ["target"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget",
                        ["groupId"] = groupId.ToString()
                    }
                });
            }
        }

        if (args["exclude_group_ids"] is JArray excludes)
        {
            foreach (var groupId in excludes)
            {
                assignments.Add(new JObject
                {
                    ["target"] = new JObject
                    {
                        ["@odata.type"] = "#microsoft.graph.exclusionGroupAssignmentTarget",
                        ["groupId"] = groupId.ToString()
                    }
                });
            }
        }

        if (assignments.Count == 0)
            throw new ArgumentException("Supply at least one of group_ids, exclude_group_ids, all_devices, or all_users");

        return assignments;
    }

    /// <summary>Resolve which script collection a script tool is addressing.</summary>
    private static string ScriptCollection(JObject args)
    {
        var type = (args.Value<string>("script_type") ?? "powershell").ToLowerInvariant();
        if (type == "shell") return "/deviceManagement/deviceShellScripts";
        if (type == "customattribute") return "/deviceManagement/deviceCustomAttributeShellScripts";
        return "/deviceManagement/deviceManagementScripts";
    }

    /// <summary>Escape a path segment so an identifier cannot break out of its position.</summary>
    private static string Esc(string segment)
    {
        return Uri.EscapeDataString(segment ?? string.Empty);
    }

    /// <summary>Split a comma-separated argument into trimmed, non-empty values.</summary>
    private static string[] SplitList(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToArray();
    }

    /// <summary>Get a required array argument; throws ArgumentException if absent or empty.</summary>
    private static JArray RequireArray(JObject args, string name)
    {
        var array = args?[name] as JArray;
        if (array == null || array.Count == 0)
            throw new ArgumentException($"'{name}' is required and must contain at least one entry");
        return array;
    }

    /// <summary>Parse a JSON object argument that may arrive as a string or an object.</summary>
    private static JObject ParseJsonArgument(JObject args, string name)
    {
        var token = args?[name];
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token is JObject obj) return obj;

        var text = token.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        try { return JObject.Parse(text); }
        catch (JsonException ex) { throw new ArgumentException($"'{name}' is not valid JSON: {ex.Message}"); }
    }

    /// <summary>Parse a JSON array argument that may arrive as a string or an array.</summary>
    private static JArray ParseJsonArrayArgument(JObject args, string name)
    {
        var token = args?[name];
        if (token == null || token.Type == JTokenType.Null)
            throw new ArgumentException($"'{name}' is required");
        if (token is JArray array) return array;

        var text = token.ToString();
        try { return JArray.Parse(text); }
        catch (JsonException ex) { throw new ArgumentException($"'{name}' is not a valid JSON array: {ex.Message}"); }
    }

    /// <summary>Get a required string argument; throws ArgumentException if missing.</summary>
    private static string RequireArgument(JObject args, string name)
    {
        var value = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"'{name}' is required");
        return value;
    }

    /// <summary>Get an optional string argument with a default fallback.</summary>
    private static string GetArgument(JObject args, string name, string defaultValue = null)
    {
        var value = args?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    // ── Report Catalog ───────────────────────────────────────────────────
    //
    //    Every report function bound to /deviceManagement/reports, taken from the
    //    Microsoft Graph beta metadata document.
    //

    private static readonly string[] ReportNames =
    {
        "getActiveMalwareReport",
        "getActiveMalwareSummaryReport",
        "getAllCertificatesReport",
        "getAppStatusOverviewReport",
        "getAppsInstallSummaryReport",
        "getCachedReport",
        "getCertificatesReport",
        "getCompliancePoliciesReportForDevice",
        "getCompliancePolicyDeviceSummaryReport",
        "getCompliancePolicyDevicesReport",
        "getCompliancePolicyNonComplianceReport",
        "getCompliancePolicyNonComplianceSummaryReport",
        "getComplianceSettingDetailsReport",
        "getComplianceSettingNonComplianceReport",
        "getComplianceSettingsReport",
        "getConfigManagerDevicePolicyStatusReport",
        "getConfigurationPoliciesReportForDevice",
        "getConfigurationPolicyDeviceSummaryReport",
        "getConfigurationPolicyDevicesReport",
        "getConfigurationPolicyNonComplianceReport",
        "getConfigurationPolicyNonComplianceSummaryReport",
        "getConfigurationPolicySettingsDeviceSummaryReport",
        "getConfigurationSettingDetailsReport",
        "getConfigurationSettingNonComplianceReport",
        "getConfigurationSettingsReport",
        "getDeviceConfigurationPolicySettingsSummaryReport",
        "getDeviceManagementIntentSettingsReport",
        "getDeviceNonComplianceReport",
        "getDevicePoliciesComplianceReport",
        "getDevicePolicySettingsComplianceReport",
        "getDeviceStatusByCompliacePolicyReport",
        "getDeviceStatusByCompliancePolicySettingReport",
        "getDeviceStatusSummaryByCompliacePolicyReport",
        "getDeviceStatusSummaryByCompliancePolicySettingsReport",
        "getDevicesStatusByPolicyPlatformComplianceReport",
        "getDevicesStatusBySettingReport",
        "getDevicesWithoutCompliancePolicyReport",
        "getEncryptionReportForDevices",
        "getFailedMobileAppsReport",
        "getFailedMobileAppsSummaryReport",
        "getGroupPolicySettingsDeviceSettingsReport",
        "getHistoricalReport",
        "getMalwareSummaryReport",
        "getMobileApplicationManagementAppConfigurationReport",
        "getMobileApplicationManagementAppRegistrationSummaryReport",
        "getNoncompliantDevicesAndSettingsReport",
        "getPolicyNonComplianceReport",
        "getPolicyNonComplianceSummaryReport",
        "getQuietTimePolicyUserSummaryReport",
        "getQuietTimePolicyUsersReport",
        "getRelatedAppsStatusReport",
        "getRemoteAssistanceSessionsReport",
        "getReportFilters",
        "getSettingNonComplianceReport",
        "getUnhealthyDefenderAgentsReport",
        "getUnhealthyFirewallReport",
        "getUnhealthyFirewallSummaryReport",
        "getUserInstallStatusReport",
        "getWindowsDriverUpdateAlertSummaryReport",
        "getWindowsDriverUpdateAlertsPerPolicyPerDeviceReport",
        "getWindowsQualityUpdateAlertSummaryReport",
        "getWindowsQualityUpdateAlertsPerPolicyPerDeviceReport",
        "getWindowsUpdateAlertSummaryReport",
        "getWindowsUpdateAlertsPerPolicyPerDeviceReport",
        "retrieveAssignedApplicationsReport",
        "retrieveCloudPkiLeafCertificateReport",
        "retrieveCloudPkiLeafCertificateSummaryReport",
        "retrieveDeviceAppInstallationStatusReport",
        "retrieveEnrollmentTimeGroupingFailureReport",
        "retrieveSecurityTaskAppsReport",
        "retrieveWin32CatalogAppsUpdateReport"
    };

    // ── Endpoint Catalog ─────────────────────────────────────────────────
    //
    //    Every Intune collection path and bound operation, generated from the
    //    Microsoft Graph beta metadata document. Powers intune_find_endpoint so
    //    an agent can locate any endpoint this connector does not name directly.
    //

    private static readonly string[] EndpointIndex =
    {
        "GET,POST /deviceAppManagement/androidManagedAppProtections",
        "GET,POST /deviceAppManagement/defaultManagedAppProtections",
        "GET,POST /deviceAppManagement/deviceAppManagementTasks",
        "GET,POST /deviceAppManagement/deviceAppManagementTasks/{id}/managedDevices",
        "GET,POST /deviceAppManagement/deviceAppManagementTasks/{id}/mobileApps",
        "GET,POST /deviceAppManagement/enterpriseCodeSigningCertificates",
        "GET,POST /deviceAppManagement/iosLobAppProvisioningConfigurations",
        "GET,POST /deviceAppManagement/iosManagedAppProtections",
        "GET,POST /deviceAppManagement/managedAppPolicies",
        "GET,POST /deviceAppManagement/managedAppPolicies/{id}/apps",
        "GET,POST /deviceAppManagement/managedAppPolicies/{id}/assignments",
        "GET,POST /deviceAppManagement/managedAppPolicies/{id}/exemptAppLockerFiles",
        "GET,POST /deviceAppManagement/managedAppPolicies/{id}/protectedAppLockerFiles",
        "GET,POST /deviceAppManagement/managedAppPolicies/{id}/settings",
        "GET,POST /deviceAppManagement/managedAppRegistrations",
        "GET,POST /deviceAppManagement/managedAppStatuses",
        "GET,POST /deviceAppManagement/managedEBookCategories",
        "GET,POST /deviceAppManagement/managedEBooks",
        "GET,POST /deviceAppManagement/mdmWindowsInformationProtectionPolicies",
        "GET,POST /deviceAppManagement/mobileAppCatalogPackages",
        "GET,POST /deviceAppManagement/mobileAppCategories",
        "GET,POST /deviceAppManagement/mobileAppConfigurations",
        "GET,POST /deviceAppManagement/mobileAppRelationships",
        "GET,POST /deviceAppManagement/mobileApps",
        "GET,POST /deviceAppManagement/mobileApps/{id}/assignedLicenses",
        "GET,POST /deviceAppManagement/mobileApps/{id}/containedApps",
        "GET,POST /deviceAppManagement/mobileApps/{id}/contentVersions",
        "GET,POST /deviceAppManagement/policySets",
        "GET,POST /deviceAppManagement/targetedManagedAppConfigurations",
        "GET,POST /deviceAppManagement/vppTokens",
        "GET,POST /deviceAppManagement/wdacSupplementalPolicies",
        "GET,POST /deviceAppManagement/windowsInformationProtectionDeviceRegistrations",
        "GET,POST /deviceAppManagement/windowsInformationProtectionPolicies",
        "GET,POST /deviceAppManagement/windowsInformationProtectionWipeActions",
        "GET,POST /deviceAppManagement/windowsManagedAppProtections",
        "GET,POST /deviceManagement/androidDeviceOwnerEnrollmentProfiles",
        "GET,POST /deviceManagement/androidForWorkAppConfigurationSchemas",
        "GET,POST /deviceManagement/androidForWorkEnrollmentProfiles",
        "GET,POST /deviceManagement/androidManagedStoreAppConfigurationSchemas",
        "GET,POST /deviceManagement/appleUserInitiatedEnrollmentProfiles",
        "GET,POST /deviceManagement/assignmentFilters",
        "GET,POST /deviceManagement/auditEvents",
        "GET,POST /deviceManagement/autopilotEvents",
        "GET,POST /deviceManagement/cartToClassAssociations",
        "GET,POST /deviceManagement/categories",
        "GET,POST /deviceManagement/categories/{id}/recommendedSettings",
        "GET,POST /deviceManagement/categories/{id}/settings",
        "GET,POST /deviceManagement/certificateConnectorDetails",
        "GET,POST /deviceManagement/chromeOSOnboardingSettings",
        "GET,POST /deviceManagement/cloudCertificationAuthority",
        "GET,POST /deviceManagement/cloudCertificationAuthorityLeafCertificate",
        "GET,POST /deviceManagement/cloudPCConnectivityIssues",
        "GET,POST /deviceManagement/comanagedDevices",
        "GET,POST /deviceManagement/comanagementEligibleDevices",
        "GET,POST /deviceManagement/complianceCategories",
        "GET,POST /deviceManagement/complianceManagementPartners",
        "GET,POST /deviceManagement/compliancePolicies",
        "GET,POST /deviceManagement/complianceSettings",
        "GET,POST /deviceManagement/configManagerCollections",
        "GET,POST /deviceManagement/configurationCategories",
        "GET,POST /deviceManagement/configurationPolicies",
        "GET,POST /deviceManagement/configurationPolicyTemplates",
        "GET,POST /deviceManagement/configurationSettings",
        "GET,POST /deviceManagement/dataSharingConsents",
        "GET,POST /deviceManagement/depOnboardingSettings",
        "GET,POST /deviceManagement/derivedCredentials",
        "GET,POST /deviceManagement/detectedApps",
        "GET,POST /deviceManagement/deviceCategories",
        "GET,POST /deviceManagement/deviceCompliancePolicies",
        "GET,POST /deviceManagement/deviceCompliancePolicySettingStateSummaries",
        "GET,POST /deviceManagement/deviceComplianceScripts",
        "GET,POST /deviceManagement/deviceConfigurationConflictSummary",
        "GET,POST /deviceManagement/deviceConfigurationRestrictedAppsViolations",
        "GET,POST /deviceManagement/deviceConfigurations",
        "GET,POST /deviceManagement/deviceConfigurations/{id}/identityCertificate/managedDeviceCertificateStates",
        "GET,POST /deviceManagement/deviceConfigurations/{id}/identityCertificateForClientAuthentication/managedDeviceCertificateStates",
        "GET,POST /deviceManagement/deviceConfigurations/{id}/networkAccessConfigurations",
        "GET,POST /deviceManagement/deviceConfigurations/{id}/privacyAccessControls",
        "GET,POST /deviceManagement/deviceConfigurations/{id}/rootCertificatesForClientValidation",
        "GET,POST /deviceManagement/deviceConfigurations/{id}/rootCertificatesForServerValidation",
        "GET,POST /deviceManagement/deviceConfigurationsAllManagedDeviceCertificateStates",
        "GET,POST /deviceManagement/deviceCustomAttributeShellScripts",
        "GET,POST /deviceManagement/deviceEnrollmentConfigurations",
        "GET,POST /deviceManagement/deviceHealthScripts",
        "GET,POST /deviceManagement/deviceManagementPartners",
        "GET,POST /deviceManagement/deviceManagementScripts",
        "GET,POST /deviceManagement/deviceShellScripts",
        "GET,POST /deviceManagement/domainJoinConnectors",
        "GET,POST /deviceManagement/elevationRequests",
        "GET,POST /deviceManagement/embeddedSIMActivationCodePools",
        "GET,POST /deviceManagement/exchangeConnectors",
        "GET,POST /deviceManagement/exchangeOnPremisesPolicies",
        "GET,POST /deviceManagement/groupPolicyCategories",
        "GET,POST /deviceManagement/groupPolicyConfigurations",
        "GET,POST /deviceManagement/groupPolicyDefinitionFiles",
        "GET,POST /deviceManagement/groupPolicyDefinitionFiles/{id}/groupPolicyOperations",
        "GET,POST /deviceManagement/groupPolicyDefinitions",
        "GET,POST /deviceManagement/groupPolicyMigrationReports",
        "GET,POST /deviceManagement/groupPolicyObjectFiles",
        "GET,POST /deviceManagement/groupPolicyUploadedDefinitionFiles",
        "GET,POST /deviceManagement/hardwareConfigurations",
        "GET,POST /deviceManagement/hardwarePasswordDetails",
        "GET,POST /deviceManagement/hardwarePasswordInfo",
        "GET,POST /deviceManagement/importedDeviceIdentities",
        "GET,POST /deviceManagement/importedWindowsAutopilotDeviceIdentities",
        "GET,POST /deviceManagement/intents",
        "GET,POST /deviceManagement/intuneBrandingProfiles",
        "GET,POST /deviceManagement/iosUpdateStatuses",
        "GET,POST /deviceManagement/macOSSoftwareUpdateAccountSummaries",
        "GET,POST /deviceManagement/managedDeviceCleanupRules",
        "GET,POST /deviceManagement/managedDeviceEncryptionStates",
        "GET,POST /deviceManagement/managedDeviceWindowsOSImages",
        "GET,POST /deviceManagement/managedDevices",
        "GET,POST /deviceManagement/microsoftTunnelConfigurations",
        "GET,POST /deviceManagement/microsoftTunnelHealthThresholds",
        "GET,POST /deviceManagement/microsoftTunnelServerLogCollectionResponses",
        "GET,POST /deviceManagement/microsoftTunnelSites",
        "GET,POST /deviceManagement/mobileAppTroubleshootingEvents",
        "GET,POST /deviceManagement/mobileThreatDefenseConnectors",
        "GET,POST /deviceManagement/monitoring/alertRecords",
        "GET,POST /deviceManagement/monitoring/alertRules",
        "GET,POST /deviceManagement/ndesConnectors",
        "GET,POST /deviceManagement/notificationMessageTemplates",
        "GET,POST /deviceManagement/operationApprovalPolicies",
        "GET,POST /deviceManagement/operationApprovalRequests",
        "GET,POST /deviceManagement/privilegeManagementElevations",
        "GET,POST /deviceManagement/remoteActionAudits",
        "GET,POST /deviceManagement/remoteAssistancePartners",
        "GET,POST /deviceManagement/resourceAccessProfiles",
        "GET,POST /deviceManagement/resourceOperations",
        "GET,POST /deviceManagement/reusablePolicySettings",
        "GET,POST /deviceManagement/reusableSettings",
        "GET,POST /deviceManagement/roleAssignments",
        "GET,POST /deviceManagement/roleDefinitions",
        "GET,POST /deviceManagement/roleScopeTags",
        "GET,POST /deviceManagement/serviceNowConnections",
        "GET,POST /deviceManagement/settingDefinitions",
        "GET,POST /deviceManagement/templateInsights",
        "GET,POST /deviceManagement/templateSettings",
        "GET,POST /deviceManagement/templates",
        "GET,POST /deviceManagement/templates/{id}/categoryDeviceStateSummaries",
        "GET,POST /deviceManagement/templates/{id}/deviceStates",
        "GET,POST /deviceManagement/termsAndConditions",
        "GET,POST /deviceManagement/troubleshootingEvents",
        "GET,POST /deviceManagement/troubleshootingEvents/{id}/appLogCollectionRequests",
        "GET,POST /deviceManagement/userExperienceAnalyticsAnomaly",
        "GET,POST /deviceManagement/userExperienceAnalyticsAnomalyCorrelationGroupOverview",
        "GET,POST /deviceManagement/userExperienceAnalyticsAnomalyDevice",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthApplicationPerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthApplicationPerformanceByAppVersion",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthApplicationPerformanceByAppVersionDetails",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthApplicationPerformanceByAppVersionDeviceId",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthApplicationPerformanceByOSVersion",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthDeviceModelPerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthDevicePerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthDevicePerformanceDetails",
        "GET,POST /deviceManagement/userExperienceAnalyticsAppHealthOSVersionPerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsBaselines",
        "GET,POST /deviceManagement/userExperienceAnalyticsBatteryHealthAppImpact",
        "GET,POST /deviceManagement/userExperienceAnalyticsBatteryHealthDeviceAppImpact",
        "GET,POST /deviceManagement/userExperienceAnalyticsBatteryHealthDevicePerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsBatteryHealthDeviceRuntimeHistory",
        "GET,POST /deviceManagement/userExperienceAnalyticsBatteryHealthModelPerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsBatteryHealthOsPerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsCategories",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceMetricHistory",
        "GET,POST /deviceManagement/userExperienceAnalyticsDevicePerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceScopes",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceScores",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceStartupHistory",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceStartupProcessPerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceStartupProcesses",
        "GET,POST /deviceManagement/userExperienceAnalyticsDeviceTimelineEvent",
        "GET,POST /deviceManagement/userExperienceAnalyticsDevicesWithoutCloudIdentity",
        "GET,POST /deviceManagement/userExperienceAnalyticsImpactingProcess",
        "GET,POST /deviceManagement/userExperienceAnalyticsMetricHistory",
        "GET,POST /deviceManagement/userExperienceAnalyticsModelScores",
        "GET,POST /deviceManagement/userExperienceAnalyticsNotAutopilotReadyDevice",
        "GET,POST /deviceManagement/userExperienceAnalyticsRemoteConnection",
        "GET,POST /deviceManagement/userExperienceAnalyticsResourcePerformance",
        "GET,POST /deviceManagement/userExperienceAnalyticsScoreHistory",
        "GET,POST /deviceManagement/userExperienceAnalyticsWorkFromAnywhereMetrics",
        "GET,POST /deviceManagement/userExperienceAnalyticsWorkFromAnywhereModelPerformance",
        "GET,POST /deviceManagement/userPfxCertificates",
        "GET,POST /deviceManagement/windowsAutopilotDeploymentProfiles",
        "GET,POST /deviceManagement/windowsAutopilotDeviceIdentities",
        "GET,POST /deviceManagement/windowsDriverUpdateProfiles",
        "GET,POST /deviceManagement/windowsFeatureUpdateProfiles",
        "GET,POST /deviceManagement/windowsInformationProtectionAppLearningSummaries",
        "GET,POST /deviceManagement/windowsInformationProtectionNetworkLearningSummaries",
        "GET,POST /deviceManagement/windowsMalwareInformation",
        "GET,POST /deviceManagement/windowsQualityUpdatePolicies",
        "GET,POST /deviceManagement/windowsQualityUpdateProfiles",
        "GET,POST /deviceManagement/windowsUpdateCatalogItems",
        "GET,POST /deviceManagement/zebraFotaArtifacts",
        "GET,POST /deviceManagement/zebraFotaDeployments",
        "POST /deviceAppManagement/androidManagedAppProtections/{id}/hasPayloadLinks",
        "POST /deviceAppManagement/deviceAppManagementTasks/{id}/updateStatus",
        "POST /deviceAppManagement/iosLobAppProvisioningConfigurations/{id}/assign",
        "POST /deviceAppManagement/iosLobAppProvisioningConfigurations/{id}/hasPayloadLinks",
        "POST /deviceAppManagement/iosManagedAppProtections/{id}/hasPayloadLinks",
        "POST /deviceAppManagement/managedAppPolicies/{id}/targetApps",
        "GET /deviceAppManagement/managedAppRegistrations/{id}/getUserIdsWithFlaggedAppRegistration",
        "POST /deviceAppManagement/managedEBooks/{id}/assign",
        "POST /deviceAppManagement/mdmWindowsInformationProtectionPolicies/{id}/hasPayloadLinks",
        "POST /deviceAppManagement/mobileAppConfigurations/{id}/assign",
        "POST /deviceAppManagement/mobileApps/{id}/assign",
        "GET /deviceAppManagement/mobileApps/{id}/convertFromMobileAppCatalogPackage",
        "POST /deviceAppManagement/mobileApps/{id}/hasPayloadLinks",
        "POST /deviceAppManagement/mobileApps/{id}/updateRelationships",
        "POST /deviceAppManagement/mobileApps/{id}/validateXml",
        "POST /deviceAppManagement/policySets/{id}/getPolicySets",
        "POST /deviceAppManagement/policySets/{id}/update",
        "POST /deviceAppManagement/targetedManagedAppConfigurations/{id}/assign",
        "POST /deviceAppManagement/targetedManagedAppConfigurations/{id}/changeSettings",
        "POST /deviceAppManagement/targetedManagedAppConfigurations/{id}/hasPayloadLinks",
        "POST /deviceAppManagement/targetedManagedAppConfigurations/{id}/targetApps",
        "GET /deviceAppManagement/vppTokens/{id}/getLicensesForApp",
        "POST /deviceAppManagement/vppTokens/{id}/revokeLicenses",
        "POST /deviceAppManagement/vppTokens/{id}/syncLicenseCounts",
        "POST /deviceAppManagement/vppTokens/{id}/syncLicenses",
        "POST /deviceAppManagement/wdacSupplementalPolicies/{id}/assign",
        "POST /deviceAppManagement/windowsInformationProtectionDeviceRegistrations/{id}/wipe",
        "POST /deviceAppManagement/windowsManagedAppProtections/{id}/assign",
        "POST /deviceAppManagement/windowsManagedAppProtections/{id}/targetApps",
        "POST /deviceAppManagement/windowsManagementApp/setAsManagedInstaller",
        "POST /deviceManagement/androidAppConfigurationSchema/retrieveSchema",
        "POST /deviceManagement/androidDeviceOwnerEnrollmentProfiles/{id}/clearEnrollmentTimeDeviceMembershipTarget",
        "POST /deviceManagement/androidDeviceOwnerEnrollmentProfiles/{id}/createToken",
        "POST /deviceManagement/androidDeviceOwnerEnrollmentProfiles/{id}/retrieveEnrollmentTimeDeviceMembershipTarget",
        "POST /deviceManagement/androidDeviceOwnerEnrollmentProfiles/{id}/revokeToken",
        "POST /deviceManagement/androidDeviceOwnerEnrollmentProfiles/{id}/setEnrollmentTimeDeviceMembershipTarget",
        "POST /deviceManagement/androidForWorkEnrollmentProfiles/{id}/createToken",
        "POST /deviceManagement/androidForWorkEnrollmentProfiles/{id}/revokeToken",
        "POST /deviceManagement/androidForWorkSettings/completeSignup",
        "POST /deviceManagement/androidForWorkSettings/requestSignupUrl",
        "POST /deviceManagement/androidForWorkSettings/syncApps",
        "POST /deviceManagement/androidForWorkSettings/unbind",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/addApps",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/approveApps",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/completeSignup",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/createGooglePlayWebToken",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/createZeroTouchWebToken",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/requestEnterpriseUpgradeUrl",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/requestSignupUrl",
        "GET /deviceManagement/androidManagedStoreAccountEnterpriseSettings/retrieveStoreLayout",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/setAndroidDeviceOwnerFullyManagedEnrollmentState",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/setStoreLayout",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/syncApps",
        "POST /deviceManagement/androidManagedStoreAccountEnterpriseSettings/unbind",
        "GET /deviceManagement/applePushNotificationCertificate/downloadApplePushNotificationCertificateSigningRequest",
        "POST /deviceManagement/applePushNotificationCertificate/generateApplePushNotificationCertificateSigningRequest",
        "POST /deviceManagement/appleUserInitiatedEnrollmentProfiles/{id}/setPriority",
        "POST /deviceManagement/assignmentFilters/{id}/enable",
        "GET /deviceManagement/assignmentFilters/{id}/getPlatformSupportedProperties",
        "GET /deviceManagement/assignmentFilters/{id}/getState",
        "GET /deviceManagement/assignmentFilters/{id}/getSupportedProperties",
        "POST /deviceManagement/assignmentFilters/{id}/validateFilter",
        "GET /deviceManagement/auditEvents/{id}/getAuditActivityTypes",
        "GET /deviceManagement/auditEvents/{id}/getAuditCategories",
        "POST /deviceManagement/certificateConnectorDetails/{id}/getHealthMetricTimeSeries",
        "POST /deviceManagement/certificateConnectorDetails/{id}/getHealthMetrics",
        "POST /deviceManagement/chromeOSOnboardingSettings/{id}/connect",
        "POST /deviceManagement/chromeOSOnboardingSettings/{id}/disconnect",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/activate",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/changeCloudCertificationAuthorityStatus",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/getAllCloudCertificationAuthority",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/getAllCloudCertificationAuthorityLeafCertificates",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/getCloudCertificationAuthority",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/getCloudCertificationAuthorityVersion",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/getCloudCertificationAuthorityVersions",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/patchCloudCertificationAuthority",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/postCloudCertificationAuthority",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/renew",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/revokeCloudCertificationAuthorityCertificate",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/revokeLeafCertificate",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/revokeLeafCertificateBySerialNumber",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/searchCloudCertificationAuthorityLeafCertificateBySerialNumber",
        "POST /deviceManagement/cloudCertificationAuthority/{id}/uploadExternallySignedCertificationAuthorityCertificate",
        "POST /deviceManagement/compliancePolicies/{id}/assign",
        "POST /deviceManagement/compliancePolicies/{id}/setScheduledActions",
        "GET /deviceManagement/configManagerCollections/{id}/getPolicySummary",
        "POST /deviceManagement/configurationPolicies/{id}/assign",
        "POST /deviceManagement/configurationPolicies/{id}/clearEnrollmentTimeDeviceMembershipTarget",
        "POST /deviceManagement/configurationPolicies/{id}/createCopy",
        "POST /deviceManagement/configurationPolicies/{id}/reorder",
        "POST /deviceManagement/configurationPolicies/{id}/retrieveEnrollmentTimeDeviceMembershipTarget",
        "GET /deviceManagement/configurationPolicies/{id}/retrieveLatestUpgradeDefaultBaselinePolicy",
        "POST /deviceManagement/configurationPolicies/{id}/setEnrollmentTimeDeviceMembershipTarget",
        "POST /deviceManagement/dataSharingConsents/{id}/consentToDataSharing",
        "POST /deviceManagement/depOnboardingSettings/{id}/generateEncryptionPublicKey",
        "GET /deviceManagement/depOnboardingSettings/{id}/getEncryptionPublicKey",
        "GET /deviceManagement/depOnboardingSettings/{id}/getExpiringVppTokenCount",
        "POST /deviceManagement/depOnboardingSettings/{id}/releaseAppleDevices",
        "POST /deviceManagement/depOnboardingSettings/{id}/shareForSchoolDataSyncService",
        "POST /deviceManagement/depOnboardingSettings/{id}/syncWithAppleDeviceEnrollmentProgram",
        "POST /deviceManagement/depOnboardingSettings/{id}/unshareForSchoolDataSyncService",
        "POST /deviceManagement/depOnboardingSettings/{id}/uploadDepToken",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/assign",
        "GET /deviceManagement/deviceCompliancePolicies/{id}/getDevicesScheduledToRetire",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/getNoncompliantDevicesToRetire",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/hasPayloadLinks",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/refreshDeviceComplianceReportSummarization",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/scheduleActionsForRules",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/setScheduledRetireState",
        "POST /deviceManagement/deviceCompliancePolicies/{id}/validateComplianceScript",
        "POST /deviceManagement/deviceComplianceScripts/{id}/assign",
        "POST /deviceManagement/deviceConfigurations/{id}/assign",
        "POST /deviceManagement/deviceConfigurations/{id}/assignedAccessMultiModeProfiles",
        "GET /deviceManagement/deviceConfigurations/{id}/getIosAvailableUpdateVersions",
        "GET /deviceManagement/deviceConfigurations/{id}/getOmaSettingPlainTextValue",
        "POST /deviceManagement/deviceConfigurations/{id}/getTargetedUsersAndDevices",
        "POST /deviceManagement/deviceConfigurations/{id}/hasPayloadLinks",
        "POST /deviceManagement/deviceConfigurations/{id}/windowsPrivacyAccessControls",
        "POST /deviceManagement/deviceCustomAttributeShellScripts/{id}/assign",
        "POST /deviceManagement/deviceEnrollmentConfigurations/{id}/assign",
        "POST /deviceManagement/deviceEnrollmentConfigurations/{id}/createEnrollmentNotificationConfiguration",
        "POST /deviceManagement/deviceEnrollmentConfigurations/{id}/hasPayloadLinks",
        "POST /deviceManagement/deviceEnrollmentConfigurations/{id}/setPriority",
        "GET /deviceManagement/deviceHealthScripts/{id}/areGlobalScriptsAvailable",
        "POST /deviceManagement/deviceHealthScripts/{id}/assign",
        "POST /deviceManagement/deviceHealthScripts/{id}/enableGlobalScripts",
        "POST /deviceManagement/deviceHealthScripts/{id}/getGlobalScriptHighestAvailableVersion",
        "GET /deviceManagement/deviceHealthScripts/{id}/getRemediationHistory",
        "GET /deviceManagement/deviceHealthScripts/{id}/getRemediationSummary",
        "POST /deviceManagement/deviceHealthScripts/{id}/updateGlobalScript",
        "POST /deviceManagement/deviceManagementPartners/{id}/terminate",
        "POST /deviceManagement/deviceManagementScripts/{id}/assign",
        "POST /deviceManagement/deviceManagementScripts/{id}/hasPayloadLinks",
        "POST /deviceManagement/deviceShellScripts/{id}/assign",
        "POST /deviceManagement/elevationRequests/{id}/approve",
        "POST /deviceManagement/elevationRequests/{id}/deny",
        "POST /deviceManagement/elevationRequests/{id}/getAllElevationRequests",
        "POST /deviceManagement/elevationRequests/{id}/revoke",
        "POST /deviceManagement/embeddedSIMActivationCodePools/{id}/assign",
        "POST /deviceManagement/exchangeConnectors/{id}/sync",
        "POST /deviceManagement/groupPolicyConfigurations/{id}/assign",
        "POST /deviceManagement/groupPolicyConfigurations/{id}/updateDefinitionValues",
        "POST /deviceManagement/groupPolicyMigrationReports/{id}/createMigrationReport",
        "POST /deviceManagement/groupPolicyMigrationReports/{id}/updateScopeTags",
        "POST /deviceManagement/groupPolicyUploadedDefinitionFiles/{id}/addLanguageFiles",
        "POST /deviceManagement/groupPolicyUploadedDefinitionFiles/{id}/remove",
        "POST /deviceManagement/groupPolicyUploadedDefinitionFiles/{id}/removeLanguageFiles",
        "POST /deviceManagement/groupPolicyUploadedDefinitionFiles/{id}/updateLanguageFiles",
        "POST /deviceManagement/groupPolicyUploadedDefinitionFiles/{id}/uploadNewVersion",
        "POST /deviceManagement/hardwareConfigurations/{id}/assign",
        "POST /deviceManagement/importedDeviceIdentities/{id}/importDeviceIdentityList",
        "POST /deviceManagement/importedDeviceIdentities/{id}/searchExistingIdentities",
        "POST /deviceManagement/importedWindowsAutopilotDeviceIdentities/{id}/import",
        "POST /deviceManagement/intents/{id}/assign",
        "GET /deviceManagement/intents/{id}/compare",
        "POST /deviceManagement/intents/{id}/createCopy",
        "GET /deviceManagement/intents/{id}/getCustomizedSettings",
        "POST /deviceManagement/intents/{id}/migrateToTemplate",
        "POST /deviceManagement/intents/{id}/updateSettings",
        "POST /deviceManagement/intuneBrandingProfiles/{id}/assign",
        "GET /deviceManagement/managedDeviceWindowsOSImages/{id}/getAllManagedDeviceWindowsOSImages",
        "POST /deviceManagement/managedDevices/{id}/activateDeviceEsim",
        "GET /deviceManagement/managedDevices/{id}/appDiagnostics",
        "POST /deviceManagement/managedDevices/{id}/bypassActivationLock",
        "POST /deviceManagement/managedDevices/{id}/cancelEnhancedLogCollection",
        "POST /deviceManagement/managedDevices/{id}/changeAssignments",
        "POST /deviceManagement/managedDevices/{id}/cleanWindowsDevice",
        "POST /deviceManagement/managedDevices/{id}/createDeviceLogCollectionRequest",
        "POST /deviceManagement/managedDevices/{id}/deleteUserFromSharedAppleDevice",
        "POST /deviceManagement/managedDevices/{id}/deprovision",
        "POST /deviceManagement/managedDevices/{id}/disable",
        "POST /deviceManagement/managedDevices/{id}/disableLostMode",
        "POST /deviceManagement/managedDevices/{id}/downloadAppDiagnostics",
        "POST /deviceManagement/managedDevices/{id}/downloadPowerliftAppDiagnostic",
        "POST /deviceManagement/managedDevices/{id}/enableLostMode",
        "POST /deviceManagement/managedDevices/{id}/enrollNowAction",
        "POST /deviceManagement/managedDevices/{id}/executeAction",
        "GET /deviceManagement/managedDevices/{id}/getFileVaultKey",
        "GET /deviceManagement/managedDevices/{id}/getNonCompliantSettings",
        "GET /deviceManagement/managedDevices/{id}/getSyncStatus",
        "POST /deviceManagement/managedDevices/{id}/initiateDeviceAttestation",
        "POST /deviceManagement/managedDevices/{id}/initiateMobileDeviceManagementKeyRecovery",
        "POST /deviceManagement/managedDevices/{id}/initiateOnDemandProactiveRemediation",
        "POST /deviceManagement/managedDevices/{id}/locateDevice",
        "POST /deviceManagement/managedDevices/{id}/logoutSharedAppleDeviceActiveUser",
        "POST /deviceManagement/managedDevices/{id}/moveDevicesToOU",
        "POST /deviceManagement/managedDevices/{id}/overrideComplianceState",
        "POST /deviceManagement/managedDevices/{id}/pauseConfigurationRefresh",
        "POST /deviceManagement/managedDevices/{id}/playLostModeSound",
        "POST /deviceManagement/managedDevices/{id}/rebootNow",
        "POST /deviceManagement/managedDevices/{id}/recoverPasscode",
        "POST /deviceManagement/managedDevices/{id}/reenable",
        "POST /deviceManagement/managedDevices/{id}/remoteLock",
        "POST /deviceManagement/managedDevices/{id}/removeDeviceEsim",
        "POST /deviceManagement/managedDevices/{id}/removeDeviceFirmwareConfigurationInterfaceManagement",
        "POST /deviceManagement/managedDevices/{id}/requestRemoteAssistance",
        "POST /deviceManagement/managedDevices/{id}/resetPasscode",
        "POST /deviceManagement/managedDevices/{id}/restoreManagedHomeScreen",
        "POST /deviceManagement/managedDevices/{id}/retire",
        "GET /deviceManagement/managedDevices/{id}/retrieveDeviceLocalAdminAccountDetail",
        "GET /deviceManagement/managedDevices/{id}/retrieveMacOSManagedDeviceLocalAdminAccountDetail",
        "GET /deviceManagement/managedDevices/{id}/retrievePowerliftAppDiagnosticsDetails",
        "GET /deviceManagement/managedDevices/{id}/retrieveRecoveryLockPasscode",
        "POST /deviceManagement/managedDevices/{id}/revokeAppleVppLicenses",
        "POST /deviceManagement/managedDevices/{id}/rotateBitLockerKeys",
        "POST /deviceManagement/managedDevices/{id}/rotateFileVaultKey",
        "POST /deviceManagement/managedDevices/{id}/rotateLocalAdminPassword",
        "POST /deviceManagement/managedDevices/{id}/rotateRecoveryLockPasscode",
        "POST /deviceManagement/managedDevices/{id}/sendCustomNotificationToCompanyPortal",
        "POST /deviceManagement/managedDevices/{id}/setDeviceName",
        "POST /deviceManagement/managedDevices/{id}/shutDown",
        "POST /deviceManagement/managedDevices/{id}/suspendManagedHomeScreen",
        "POST /deviceManagement/managedDevices/{id}/syncDevice",
        "POST /deviceManagement/managedDevices/{id}/triggerConfigurationManagerAction",
        "POST /deviceManagement/managedDevices/{id}/triggerEnhancedLogCollection",
        "POST /deviceManagement/managedDevices/{id}/updateWindowsDeviceAccount",
        "POST /deviceManagement/managedDevices/{id}/windowsDefenderScan",
        "POST /deviceManagement/managedDevices/{id}/windowsDefenderUpdateSignatures",
        "POST /deviceManagement/managedDevices/{id}/wipe",
        "POST /deviceManagement/microsoftTunnelServerLogCollectionResponses/{id}/createDownloadUrl",
        "POST /deviceManagement/microsoftTunnelServerLogCollectionResponses/{id}/generateDownloadUrl",
        "POST /deviceManagement/microsoftTunnelSites/{id}/requestUpgrade",
        "POST /deviceManagement/monitoring/alertRecords/{id}/changeAlertRecordsPortalNotificationAsSent",
        "GET /deviceManagement/monitoring/alertRecords/{id}/getPortalNotifications",
        "POST /deviceManagement/monitoring/alertRecords/{id}/setPortalNotificationAsSent",
        "POST /deviceManagement/notificationMessageTemplates/{id}/sendTestMessage",
        "GET /deviceManagement/operationApprovalPolicies/{id}/retrieveApprovableOperations",
        "GET /deviceManagement/operationApprovalPolicies/{id}/retrieveOperationsRequiringApproval",
        "POST /deviceManagement/operationApprovalRequests/{id}/approve",
        "POST /deviceManagement/operationApprovalRequests/{id}/cancelApproval",
        "POST /deviceManagement/operationApprovalRequests/{id}/cancelMyRequest",
        "POST /deviceManagement/operationApprovalRequests/{id}/reject",
        "GET /deviceManagement/operationApprovalRequests/{id}/retrieveMyRequestById",
        "GET /deviceManagement/operationApprovalRequests/{id}/retrieveMyRequests",
        "POST /deviceManagement/operationApprovalRequests/{id}/retrieveRequestStatus",
        "POST /deviceManagement/remoteAssistancePartners/{id}/beginOnboarding",
        "POST /deviceManagement/remoteAssistancePartners/{id}/disconnect",
        "POST /deviceManagement/reports/getActiveMalwareReport",
        "POST /deviceManagement/reports/getActiveMalwareSummaryReport",
        "POST /deviceManagement/reports/getAllCertificatesReport",
        "POST /deviceManagement/reports/getAppStatusOverviewReport",
        "POST /deviceManagement/reports/getAppsInstallSummaryReport",
        "POST /deviceManagement/reports/getCachedReport",
        "POST /deviceManagement/reports/getCertificatesReport",
        "POST /deviceManagement/reports/getCompliancePoliciesReportForDevice",
        "POST /deviceManagement/reports/getCompliancePolicyDeviceSummaryReport",
        "POST /deviceManagement/reports/getCompliancePolicyDevicesReport",
        "POST /deviceManagement/reports/getCompliancePolicyNonComplianceReport",
        "POST /deviceManagement/reports/getCompliancePolicyNonComplianceSummaryReport",
        "POST /deviceManagement/reports/getComplianceSettingDetailsReport",
        "POST /deviceManagement/reports/getComplianceSettingNonComplianceReport",
        "POST /deviceManagement/reports/getComplianceSettingsReport",
        "POST /deviceManagement/reports/getConfigManagerDevicePolicyStatusReport",
        "POST /deviceManagement/reports/getConfigurationPoliciesReportForDevice",
        "POST /deviceManagement/reports/getConfigurationPolicyDeviceSummaryReport",
        "POST /deviceManagement/reports/getConfigurationPolicyDevicesReport",
        "POST /deviceManagement/reports/getConfigurationPolicyNonComplianceReport",
        "POST /deviceManagement/reports/getConfigurationPolicyNonComplianceSummaryReport",
        "POST /deviceManagement/reports/getConfigurationPolicySettingsDeviceSummaryReport",
        "POST /deviceManagement/reports/getConfigurationSettingDetailsReport",
        "POST /deviceManagement/reports/getConfigurationSettingNonComplianceReport",
        "POST /deviceManagement/reports/getConfigurationSettingsReport",
        "POST /deviceManagement/reports/getDeviceConfigurationPolicySettingsSummaryReport",
        "POST /deviceManagement/reports/getDeviceConfigurationPolicyStatusSummary",
        "POST /deviceManagement/reports/getDeviceManagementIntentPerSettingContributingProfiles",
        "POST /deviceManagement/reports/getDeviceManagementIntentSettingsReport",
        "POST /deviceManagement/reports/getDeviceNonComplianceReport",
        "POST /deviceManagement/reports/getDevicePoliciesComplianceReport",
        "POST /deviceManagement/reports/getDevicePolicySettingsComplianceReport",
        "POST /deviceManagement/reports/getDeviceStatusByCompliacePolicyReport",
        "POST /deviceManagement/reports/getDeviceStatusByCompliancePolicySettingReport",
        "POST /deviceManagement/reports/getDeviceStatusSummaryByCompliacePolicyReport",
        "POST /deviceManagement/reports/getDeviceStatusSummaryByCompliancePolicySettingsReport",
        "POST /deviceManagement/reports/getDevicesStatusByPolicyPlatformComplianceReport",
        "POST /deviceManagement/reports/getDevicesStatusBySettingReport",
        "POST /deviceManagement/reports/getDevicesWithoutCompliancePolicyReport",
        "POST /deviceManagement/reports/getEncryptionReportForDevices",
        "POST /deviceManagement/reports/getEnrollmentConfigurationPoliciesByDevice",
        "POST /deviceManagement/reports/getFailedMobileAppsReport",
        "POST /deviceManagement/reports/getFailedMobileAppsSummaryReport",
        "POST /deviceManagement/reports/getGroupPolicySettingsDeviceSettingsReport",
        "POST /deviceManagement/reports/getHistoricalReport",
        "POST /deviceManagement/reports/getMalwareSummaryReport",
        "POST /deviceManagement/reports/getMobileApplicationManagementAppConfigurationReport",
        "POST /deviceManagement/reports/getMobileApplicationManagementAppRegistrationSummaryReport",
        "POST /deviceManagement/reports/getNoncompliantDevicesAndSettingsReport",
        "POST /deviceManagement/reports/getPolicyNonComplianceMetadata",
        "POST /deviceManagement/reports/getPolicyNonComplianceReport",
        "POST /deviceManagement/reports/getPolicyNonComplianceSummaryReport",
        "POST /deviceManagement/reports/getQuietTimePolicyUserSummaryReport",
        "POST /deviceManagement/reports/getQuietTimePolicyUsersReport",
        "POST /deviceManagement/reports/getRelatedAppsStatusReport",
        "POST /deviceManagement/reports/getRemoteAssistanceSessionsReport",
        "POST /deviceManagement/reports/getReportFilters",
        "POST /deviceManagement/reports/getSettingNonComplianceReport",
        "POST /deviceManagement/reports/getUnhealthyDefenderAgentsReport",
        "POST /deviceManagement/reports/getUnhealthyFirewallReport",
        "POST /deviceManagement/reports/getUnhealthyFirewallSummaryReport",
        "POST /deviceManagement/reports/getUserInstallStatusReport",
        "POST /deviceManagement/reports/getWindowsDriverUpdateAlertSummaryReport",
        "POST /deviceManagement/reports/getWindowsDriverUpdateAlertsPerPolicyPerDeviceReport",
        "POST /deviceManagement/reports/getWindowsQualityUpdateAlertSummaryReport",
        "POST /deviceManagement/reports/getWindowsQualityUpdateAlertsPerPolicyPerDeviceReport",
        "POST /deviceManagement/reports/getWindowsUpdateAlertSummaryReport",
        "POST /deviceManagement/reports/getWindowsUpdateAlertsPerPolicyPerDeviceReport",
        "POST /deviceManagement/reports/retrieveAndroidWorkProfileDeviceMigrationStatuses",
        "POST /deviceManagement/reports/retrieveAppleDeviceOSUpdateStatus",
        "POST /deviceManagement/reports/retrieveAppleOSUpdateFailures",
        "POST /deviceManagement/reports/retrieveAssignedApplicationsReport",
        "POST /deviceManagement/reports/retrieveCloudPkiLeafCertificateReport",
        "POST /deviceManagement/reports/retrieveCloudPkiLeafCertificateSummaryReport",
        "POST /deviceManagement/reports/retrieveDeviceAppInstallationStatusReport",
        "POST /deviceManagement/reports/retrieveEnrollmentTimeGroupingFailureReport",
        "POST /deviceManagement/reports/retrieveSecurityTaskAppsReport",
        "POST /deviceManagement/reports/retrieveWin32CatalogAppsUpdateReport",
        "POST /deviceManagement/resourceAccessProfiles/{id}/assign",
        "POST /deviceManagement/resourceAccessProfiles/{id}/queryByPlatformType",
        "GET /deviceManagement/resourceOperations/{id}/getScopesForUser",
        "POST /deviceManagement/reusablePolicySettings/{id}/clone",
        "POST /deviceManagement/roleScopeTags/{id}/assign",
        "POST /deviceManagement/roleScopeTags/{id}/getRoleScopeTagsById",
        "GET /deviceManagement/roleScopeTags/{id}/hasCustomRoleScopeTag",
        "GET /deviceManagement/templates/{id}/compare",
        "POST /deviceManagement/templates/{id}/createInstance",
        "POST /deviceManagement/templates/{id}/importOffice365DeviceConfigurationPolicies",
        "POST /deviceManagement/tenantAttachRBAC/enable",
        "GET /deviceManagement/tenantAttachRBAC/getState",
        "POST /deviceManagement/troubleshootingEvents/{id}/appLogCollectionRequests/{id}/createDownloadUrl",
        "GET /deviceManagement/userExperienceAnalyticsDevicePerformance/{id}/summarizeDevicePerformanceDevices",
        "POST /deviceManagement/userExperienceAnalyticsDeviceScope/triggerDeviceScopeAction",
        "GET /deviceManagement/userExperienceAnalyticsRemoteConnection/{id}/summarizeDeviceRemoteConnection",
        "GET /deviceManagement/userExperienceAnalyticsResourcePerformance/{id}/summarizeDeviceResourcePerformance",
        "GET /deviceManagement/virtualEndpoint/getEffectivePermissions",
        "POST /deviceManagement/virtualEndpoint/organizationAction",
        "GET /deviceManagement/virtualEndpoint/retrieveOrganizationActionDetail",
        "GET /deviceManagement/virtualEndpoint/retrieveScopedPermissions",
        "GET /deviceManagement/virtualEndpoint/retrieveTenantEncryptionSetting",
        "POST /deviceManagement/windowsAutopilotDeploymentProfiles/{id}/assign",
        "POST /deviceManagement/windowsAutopilotDeploymentProfiles/{id}/hasPayloadLinks",
        "POST /deviceManagement/windowsAutopilotDeviceIdentities/{id}/allowNextEnrollment",
        "POST /deviceManagement/windowsAutopilotDeviceIdentities/{id}/assignResourceAccountToDevice",
        "POST /deviceManagement/windowsAutopilotDeviceIdentities/{id}/assignUserToDevice",
        "POST /deviceManagement/windowsAutopilotDeviceIdentities/{id}/unassignResourceAccountFromDevice",
        "POST /deviceManagement/windowsAutopilotDeviceIdentities/{id}/unassignUserFromDevice",
        "POST /deviceManagement/windowsAutopilotDeviceIdentities/{id}/updateDeviceProperties",
        "POST /deviceManagement/windowsAutopilotSettings/sync",
        "POST /deviceManagement/windowsDriverUpdateProfiles/{id}/assign",
        "POST /deviceManagement/windowsDriverUpdateProfiles/{id}/executeAction",
        "POST /deviceManagement/windowsDriverUpdateProfiles/{id}/syncInventory",
        "POST /deviceManagement/windowsFeatureUpdateProfiles/{id}/assign",
        "POST /deviceManagement/windowsQualityUpdatePolicies/{id}/assign",
        "POST /deviceManagement/windowsQualityUpdatePolicies/{id}/bulkAction",
        "GET /deviceManagement/windowsQualityUpdatePolicies/{id}/retrieveWindowsQualityUpdateCatalogItemDetails",
        "POST /deviceManagement/windowsQualityUpdateProfiles/{id}/assign",
        "POST /deviceManagement/zebraFotaConnector/approveFotaApps",
        "POST /deviceManagement/zebraFotaConnector/connect",
        "POST /deviceManagement/zebraFotaConnector/disconnect",
        "POST /deviceManagement/zebraFotaConnector/hasActiveDeployments",
        "GET /deviceManagement/zebraFotaConnector/retrieveZebraFotaDeviceModels",
        "POST /deviceManagement/zebraFotaDeployments/{id}/cancel"
    };

    // ── Application Insights (Optional) ──────────────────────────────────

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
// ║  SECTION 2: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section   ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                            ║
// ║  Spec coverage: MCP 2026-07-28, dual-era                                   ║
// ║                                                                            ║
// ║  2026-07-28 removed the initialize handshake and made MCP stateless: every ║
// ║  request carries its own protocol version, identity, and capabilities in   ║
// ║  _meta. Power Platform connectors are stateless by construction, so the    ║
// ║  runtime and the protocol now agree rather than fight.                     ║
// ║                                                                            ║
// ║  This handler serves BOTH eras from one endpoint:                          ║
// ║    modern (2026-07-28)  server/discover, per-request _meta, resultType,    ║
// ║                         ttlMs/cacheScope, MRTR input_required              ║
// ║    legacy (2025-11-25-) initialize, ping, logging/setLevel,                ║
// ║                         resources/subscribe                                ║
// ║                                                                            ║
// ║  The era is chosen per request. Copilot Studio is a legacy client and is   ║
// ║  answered exactly as it was before this revision.                          ║
// ║                                                                            ║
// ║  Out of reach for a request/response connector (no open stream):           ║
// ║   - subscriptions/listen and every server-to-client notification           ║
// ║   - the io.modelcontextprotocol/tasks extension (needs durable state)      ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Protocol Constants ───────────────────────────────────────────────────────

/// <summary>Reserved _meta keys, extension identifiers, and URI schemes defined by MCP.</summary>
public static class McpKeys
{
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    public const string ClientInfo = "io.modelcontextprotocol/clientInfo";
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    public const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    public const string LogLevel = "io.modelcontextprotocol/logLevel";
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    // Official extensions, advertised through capabilities.extensions.
    public const string ExtensionTasks = "io.modelcontextprotocol/tasks";
    public const string ExtensionUi = "io.modelcontextprotocol/ui";

    // Agent Skills over MCP.
    public const string SkillScheme = "skill://";
    public const string SkillIndexUri = "skill://index.json";
}

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported by server/discover and in each result's _meta.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>Optional icons array, per the icons field in the MCP base spec.</summary>
    public JArray Icons { get; set; }
}

/// <summary>Capabilities advertised by server/discover and initialize.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Completions { get; set; }

    /// <summary>Deprecated in 2026-07-28. Advertised to legacy clients only.</summary>
    public bool Logging { get; set; }

    /// <summary>
    /// Optional extension map keyed by extension identifier, for example
    /// { "io.modelcontextprotocol/ui": { "mimeTypes": [ "text/html;profile=mcp-app" ] } }.
    /// </summary>
    public JObject Extensions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();

    /// <summary>Preferred revision. Listed first by server/discover.</summary>
    public string ProtocolVersion { get; set; } = "2026-07-28";

    /// <summary>Every revision this server accepts. Must contain ProtocolVersion.</summary>
    public List<string> SupportedProtocolVersions { get; set; } =
        new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" };

    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();

    /// <summary>Natural-language guidance for the model, returned by server/discover.</summary>
    public string Instructions { get; set; }

    // ── Cache hints (2026-07-28 CacheableResult) ──────────────────────────

    /// <summary>Freshness hint in milliseconds for tools/prompts/resources list results.</summary>
    public int ListCacheTtlMs { get; set; } = 300000;

    /// <summary>"public" when list results are identical for every caller, otherwise "private".</summary>
    public string ListCacheScope { get; set; } = "public";

    public int ResourceCacheTtlMs { get; set; } = 60000;

    /// <summary>"private" by default: resource content commonly varies by authenticated user.</summary>
    public string ResourceCacheScope { get; set; } = "private";

    public int DiscoverCacheTtlMs { get; set; } = 3600000;

    /// <summary>
    /// Compare the Mcp-Method and Mcp-Name headers against the request body and reject a
    /// mismatch with HeaderMismatch. Absent headers are ignored, because the Power Platform
    /// gateway makes no guarantee that it forwards them.
    /// </summary>
    public bool ValidateRequestHeaders { get; set; } = true;
}

// ── Transport Headers ────────────────────────────────────────────────────────

/// <summary>
/// The Streamable HTTP headers the framework cares about. Captured in the connector
/// entry point and handed to HandleAsync so the framework never touches ScriptBase.
/// </summary>
public class McpTransportHeaders
{
    public string ProtocolVersion { get; set; }
    public string Method { get; set; }
    public string Name { get; set; }

    public static McpTransportHeaders FromRequest(HttpRequestMessage request)
    {
        var headers = new McpTransportHeaders();
        if (request == null) return headers;

        headers.ProtocolVersion = FirstOrNull(request, "MCP-Protocol-Version");
        headers.Method = FirstOrNull(request, "Mcp-Method");
        headers.Name = FirstOrNull(request, "Mcp-Name");
        return headers;
    }

    private static string FirstOrNull(HttpRequestMessage request, string headerName)
    {
        IEnumerable<string> values;
        if (!request.Headers.TryGetValues(headerName, out values)) return null;
        foreach (var value in values) return value;
        return null;
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>JSON-RPC 2.0 and MCP error codes.</summary>
public enum McpErrorCode
{
    // JSON-RPC 2.0.
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    // Legacy implementation-defined range (-32000 to -32019). 2026-07-28 forbids
    // allocating new codes here; this one predates the policy.
    RequestTimeout = -32000,

    // Reserved for the MCP specification (-32020 to -32099).
    HeaderMismatch = -32020,
    MissingRequiredClientCapability = -32021,
    UnsupportedProtocolVersion = -32022
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

/// <summary>
/// Fluent builder for the JSON Schema objects used in tool inputSchema and outputSchema.
///
/// Deliberately narrower than JSON Schema 2020-12 permits. Copilot Studio drops any tool
/// whose schema contains a reference type, and truncates schemas that use multi-type
/// arrays, so this builder never emits $ref, $defs, or "type": [...]. Nested objects are
/// inlined instead. If you hand-write a schema, keep to the same rules — and if you add
/// numeric bounds, note that Copilot Studio expects the draft-04 boolean form of
/// exclusiveMinimum, not the 2020-12 numeric form.
/// </summary>
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

        // Recommended empty-schema form for a tool that takes no parameters.
        if (_properties.Count == 0) schema["additionalProperties"] = false;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
    }
}

// ── Request Context ──────────────────────────────────────────────────────────

/// <summary>
/// Everything the framework knows about one request. This replaces the connection
/// state that 2026-07-28 removed: era, negotiated version, client identity, and the
/// answers a client attaches when it retries a multi round-trip request.
/// </summary>
public class McpRequestContext
{
    public JToken Id { get; set; }
    public string Method { get; set; }

    /// <summary>True when the caller speaks 2026-07-28 or later.</summary>
    public bool IsModern { get; set; }

    public string ProtocolVersion { get; set; }
    public JObject ClientInfo { get; set; }
    public JObject ClientCapabilities { get; set; }
    public string LogLevel { get; set; }

    /// <summary>Answers supplied on an MRTR retry, keyed by input request name.</summary>
    public JObject InputResponses { get; set; }

    /// <summary>Opaque state handed back with a previous input_required result.</summary>
    public string RequestState { get; set; }

    /// <summary>True when the client declared the given extension identifier.</summary>
    public bool SupportsExtension(string extensionId)
    {
        var extensions = ClientCapabilities?["extensions"] as JObject;
        return extensions != null && extensions[extensionId] != null;
    }
}

// ── Agent Skills ─────────────────────────────────────────────────────────────

/// <summary>One resource file bundled with a skill.</summary>
internal class McpSkillResource
{
    public string Path { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public Func<string> Content { get; set; }
}

/// <summary>Fluent collector for a skill's sibling resource files.</summary>
public class McpSkillResourceBuilder
{
    internal readonly List<McpSkillResource> Resources = new List<McpSkillResource>();

    /// <summary>
    /// Add a file served alongside SKILL.md. Use the relative path the skill body
    /// references, for example "references/policy.md" or "assets/template.md".
    /// </summary>
    public McpSkillResourceBuilder Resource(string path, string description, Func<string> content, string mimeType = "text/markdown")
    {
        Resources.Add(new McpSkillResource
        {
            Path = path,
            Description = description,
            MimeType = mimeType,
            Content = content
        });
        return this;
    }
}

internal class McpSkillDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Instructions { get; set; }
    public string License { get; set; }
    public string Compatibility { get; set; }
    public JObject Metadata { get; set; }
    public List<McpSkillResource> Resources { get; set; } = new List<McpSkillResource>();

    public string SkillUri { get { return McpKeys.SkillScheme + Name + "/SKILL.md"; } }

    public string ResourceUri(string path) { return McpKeys.SkillScheme + Name + "/" + path; }

    /// <summary>Render SKILL.md: YAML frontmatter followed by the markdown body.</summary>
    public string RenderSkillMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Name).Append('\n');
        sb.Append("description: ").Append(EscapeYaml(Description)).Append('\n');

        if (!string.IsNullOrWhiteSpace(License))
            sb.Append("license: ").Append(EscapeYaml(License)).Append('\n');

        if (!string.IsNullOrWhiteSpace(Compatibility))
            sb.Append("compatibility: ").Append(EscapeYaml(Compatibility)).Append('\n');

        if (Metadata != null && Metadata.Count > 0)
        {
            sb.Append("metadata:\n");
            foreach (var prop in Metadata.Properties())
                sb.Append("  ").Append(prop.Name).Append(": ").Append(EscapeYaml(prop.Value?.ToString())).Append('\n');
        }

        sb.Append("---\n\n");
        sb.Append(Instructions ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>Quote any scalar that would otherwise break the frontmatter block.</summary>
    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var flattened = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return "\"" + flattened.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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
    public Func<JObject, CancellationToken, Task<JObject>> Handler { get; set; }
}

// ── Internal Resource Registration ───────────────────────────────────────────

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── Internal Prompt Registration ─────────────────────────────────────────────

/// <summary>Describes a single prompt argument.</summary>
public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────
//
//    The core bridge class. Stateless, no DI, no ASP.NET Core.
//    Takes a JSON-RPC string in → returns a JSON-RPC string out.
//    This is the class that does not exist in the official SDK today.
//

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// 
/// Handles all JSON-RPC 2.0 routing, protocol negotiation, tool discovery,
/// parameter binding, and response formatting internally.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;

    // Dictionaries give O(1) dispatch. The parallel lists preserve registration order,
    // which 2026-07-28 asks for so clients can cache list results reliably.
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly List<McpToolDefinition> _toolOrder;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceDefinition> _resourceOrder;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;
    private readonly List<McpPromptDefinition> _promptOrder;
    private readonly List<McpSkillDefinition> _skills;

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _toolOrder = new List<McpToolDefinition>();
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceOrder = new List<McpResourceDefinition>();
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
        _promptOrder = new List<McpPromptDefinition>();
        _skills = new List<McpSkillDefinition>();
    }

    // ── Tool Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a tool using the fluent API.
    /// Define the schema with McpSchemaBuilder, provide a handler, and optionally set annotations.
    /// </summary>
    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObject = null;
        if (annotations != null)
        {
            annotationsObject = new JObject();
            annotations(annotationsObject);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        var definition = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Handler = handler
        };

        McpToolDefinition previous;
        if (_tools.TryGetValue(name, out previous)) _toolOrder.Remove(previous);

        _tools[name] = definition;
        _toolOrder.Add(definition);

        return this;
    }

    // ── Resource Registration ─────────────────────────────────────────────

    /// <summary>
    /// Register a static resource. The handler returns the resource contents
    /// as a JArray of {uri, text, mimeType} or {uri, blob, mimeType} objects.
    /// </summary>
    public McpRequestHandler AddResource(
        string uri,
        string name,
        string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        var definition = new McpResourceDefinition
        {
            Uri = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        };

        McpResourceDefinition previous;
        if (_resources.TryGetValue(uri, out previous)) _resourceOrder.Remove(previous);

        _resources[uri] = definition;
        _resourceOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Register a resource template. The handler receives the resolved URI
    /// and returns the resource contents as a JArray.
    /// </summary>
    public McpRequestHandler AddResourceTemplate(
        string uriTemplate,
        string name,
        string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        });

        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    /// <summary>
    /// Register a prompt. The handler receives the argument values as a JObject
    /// and returns a JArray of message objects ({role, content: {type, text}}).
    ///
    /// Copilot Studio does not consume MCP prompts. Register them for clients that do.
    /// </summary>
    public McpRequestHandler AddPrompt(
        string name,
        string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        var definition = new McpPromptDefinition
        {
            Name = name,
            Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };

        McpPromptDefinition previous;
        if (_prompts.TryGetValue(name, out previous)) _promptOrder.Remove(previous);

        _prompts[name] = definition;
        _promptOrder.Add(definition);

        return this;
    }

    // ── Skill Registration ───────────────────────────────────────────────

    /// <summary>
    /// Publish an Agent Skill over MCP.
    ///
    /// The skill is exposed as ordinary MCP resources under the skill:// scheme, and a
    /// skill://index.json discovery document is generated from every registered skill.
    /// Agent Framework (.NET UseMcpSkills, Python MCPSkillsSource) and Microsoft Foundry
    /// Toolbox read that index and fetch SKILL.md on demand. Only the skill-md
    /// distribution type is produced; archives are not supported.
    /// </summary>
    /// <param name="name">Lowercase letters, digits, and single hyphens. Max 64 characters.</param>
    /// <param name="description">What the skill does and when to use it. Max 1024 characters.</param>
    /// <param name="instructions">The markdown body of SKILL.md. Keep it under 500 lines.</param>
    /// <param name="resources">Sibling files the instructions reference.</param>
    public McpRequestHandler AddSkill(
        string name,
        string description,
        string instructions,
        Action<McpSkillResourceBuilder> resources = null,
        string license = null,
        string compatibility = null,
        JObject metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name is required.");
        if (name.Length > 64)
            throw new ArgumentException($"Skill name '{name}' exceeds 64 characters.");
        if (!Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ArgumentException($"Skill name '{name}' must use lowercase letters, digits, and single hyphens, with no leading or trailing hyphen.");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException($"Skill '{name}' requires a description.");
        if (description.Length > 1024)
            throw new ArgumentException($"Skill '{name}' description exceeds 1024 characters.");
        if (!string.IsNullOrEmpty(compatibility) && compatibility.Length > 500)
            throw new ArgumentException($"Skill '{name}' compatibility exceeds 500 characters.");

        var resourceBuilder = new McpSkillResourceBuilder();
        resources?.Invoke(resourceBuilder);

        var skill = new McpSkillDefinition
        {
            Name = name,
            Description = description,
            Instructions = instructions,
            License = license,
            Compatibility = compatibility,
            Metadata = metadata,
            Resources = resourceBuilder.Resources
        };

        _skills.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _skills.Add(skill);

        // The index handler runs at request time, so it always sees every skill
        // registered so far regardless of registration order.
        if (!_resources.ContainsKey(McpKeys.SkillIndexUri))
        {
            AddResource(McpKeys.SkillIndexUri, "Agent Skills index",
                "Discovery document listing the Agent Skills this server publishes.",
                handler: (ct) => Task.FromResult(BuildSkillIndexContents()));
        }

        AddResource(skill.SkillUri, name, description,
            handler: (ct) => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = skill.SkillUri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = skill.RenderSkillMarkdown()
                }
            }),
            mimeType: "text/markdown");

        foreach (var resource in skill.Resources)
        {
            var uri = skill.ResourceUri(resource.Path);
            var captured = resource;

            AddResource(uri, resource.Path, resource.Description ?? resource.Path,
                handler: (ct) => Task.FromResult(new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = captured.MimeType,
                        ["text"] = captured.Content != null ? captured.Content() : string.Empty
                    }
                }),
                mimeType: resource.MimeType);
        }

        return this;
    }

    /// <summary>Build the skill://index.json contents from every registered skill.</summary>
    private JArray BuildSkillIndexContents()
    {
        var entries = new JArray();

        foreach (var skill in _skills)
        {
            var entry = new JObject
            {
                ["type"] = "skill-md",
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["uri"] = skill.SkillUri
            };

            if (skill.Resources.Count > 0)
            {
                var resourceArray = new JArray();
                foreach (var resource in skill.Resources)
                {
                    resourceArray.Add(new JObject
                    {
                        ["path"] = resource.Path,
                        ["uri"] = skill.ResourceUri(resource.Path),
                        ["description"] = resource.Description,
                        ["mimeType"] = resource.MimeType
                    });
                }
                entry["resources"] = resourceArray;
            }

            entries.Add(entry);
        }

        var index = new JObject { ["skills"] = entries };

        return new JArray
        {
            new JObject
            {
                ["uri"] = McpKeys.SkillIndexUri,
                ["mimeType"] = "application/json",
                ["text"] = index.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// This is the single method that bridges the gap.
    /// </summary>
    public Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        return HandleAsync(body, null, cancellationToken);
    }

    /// <summary>
    /// Process a request, using the Streamable HTTP headers to detect the protocol era
    /// and to check Mcp-Method / Mcp-Name against the body.
    /// </summary>
    public async Task<string> HandleAsync(string body, McpTransportHeaders headers, CancellationToken cancellationToken)
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

        var ctx = BuildContext(request, headers);

        Log("McpRequestReceived", new
        {
            Method = ctx.Method,
            HasId = ctx.Id != null,
            Era = ctx.IsModern ? "modern" : "legacy",
            ProtocolVersion = ctx.ProtocolVersion
        });

        // 2026-07-28 negotiates per request rather than once per session.
        if (ctx.IsModern && !IsSupportedVersion(ctx.ProtocolVersion))
        {
            Log("McpUnsupportedProtocolVersion", new { Requested = ctx.ProtocolVersion });
            return SerializeError(ctx.Id, McpErrorCode.UnsupportedProtocolVersion,
                "Unsupported protocol version",
                new JObject
                {
                    ["supported"] = new JArray(_options.SupportedProtocolVersions),
                    ["requested"] = ctx.ProtocolVersion
                });
        }

        var headerError = ValidateHeaders(ctx, request, headers);
        if (headerError != null) return headerError;

        try
        {
            switch (ctx.Method)
            {
                // Discovery — servers MUST implement this in 2026-07-28.
                case "server/discover":
                    return HandleDiscover(ctx);

                // The legacy handshake. A client that opens this way is asking for legacy
                // semantics even if it also sent a version marker, so this never 404s.
                case "initialize":
                    return HandleInitialize(ctx, request);

                case "initialized":
                case "notifications/initialized":
                    return SerializeSuccess(ctx, new JObject());

                case "notifications/roots/list_changed":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Removed in 2026-07-28: ping and logging/setLevel are gone, and
                // resource subscriptions moved to the subscriptions/listen stream,
                // which a request/response connector cannot hold open.
                case "ping":
                case "logging/setLevel":
                case "resources/subscribe":
                case "resources/unsubscribe":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Valid in both eras. Copilot Studio expects a JSON-RPC body for every
                // request including notifications, so this always answers.
                case "notifications/cancelled":
                    return SerializeSuccess(ctx, new JObject());

                // Tools
                case "tools/list":
                    return HandleToolsList(ctx);

                case "tools/call":
                    return await HandleToolsCallAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Resources
                case "resources/list":
                    return HandleResourcesList(ctx);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(ctx);

                case "resources/read":
                    return await HandleResourcesReadAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Prompts
                case "prompts/list":
                    return HandlePromptsList(ctx);

                case "prompts/get":
                    return await HandlePromptsGetAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Completions
                case "completion/complete":
                    return SerializeSuccess(ctx, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                default:
                    Log("McpMethodNotFound", new { Method = ctx.Method });
                    return SerializeError(ctx.Id, McpErrorCode.MethodNotFound, "Method not found", ctx.Method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = ctx.Method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(ctx.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = ctx.Method, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    // ── Era Detection ────────────────────────────────────────────────────

    /// <summary>
    /// Decide which protocol era this request belongs to and capture its metadata.
    ///
    /// The spec picks the era from how the client opens the conversation, so an
    /// initialize-style request is always legacy no matter what else it carries — a client
    /// mid-upgrade might send MCP-Protocol-Version while still using the handshake, and
    /// rejecting its initialize would break the connection for no benefit.
    ///
    /// Otherwise a request is modern when it carries a protocol version in _meta or in the
    /// MCP-Protocol-Version header, or when it calls server/discover. Everything else,
    /// Copilot Studio included, is served under legacy semantics.
    /// </summary>
    private McpRequestContext BuildContext(JObject request, McpTransportHeaders headers)
    {
        var paramsObj = request["params"] as JObject;
        var meta = paramsObj?["_meta"] as JObject;
        var method = request.Value<string>("method") ?? string.Empty;

        var metaVersion = meta?[McpKeys.ProtocolVersion]?.ToString();
        var version = !string.IsNullOrWhiteSpace(metaVersion) ? metaVersion : headers?.ProtocolVersion;

        var opensLegacy = method == "initialize"
            || method == "initialized"
            || method == "notifications/initialized";

        var isModern = !opensLegacy
            && (!string.IsNullOrWhiteSpace(version) || method == "server/discover");

        return new McpRequestContext
        {
            Id = request["id"],
            Method = method,
            IsModern = isModern,
            ProtocolVersion = !string.IsNullOrWhiteSpace(version) ? version : _options.ProtocolVersion,
            ClientInfo = meta?[McpKeys.ClientInfo] as JObject,
            ClientCapabilities = meta?[McpKeys.ClientCapabilities] as JObject,
            LogLevel = meta?[McpKeys.LogLevel]?.ToString(),
            InputResponses = paramsObj?["inputResponses"] as JObject,
            RequestState = paramsObj?["requestState"]?.ToString()
        };
    }

    private bool IsSupportedVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;

        var supported = _options.SupportedProtocolVersions;
        if (supported == null || supported.Count == 0)
            return string.Equals(version, _options.ProtocolVersion, StringComparison.Ordinal);

        foreach (var candidate in supported)
            if (string.Equals(candidate, version, StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>
    /// 2026-07-28 requires Mcp-Method and Mcp-Name on Streamable HTTP so gateways can route
    /// without parsing the body. A header that disagrees with the body is a HeaderMismatch.
    /// An absent header is ignored: the Power Platform gateway makes no guarantee that it
    /// forwards custom headers, and failing closed would break every deployment.
    /// </summary>
    private string ValidateHeaders(McpRequestContext ctx, JObject request, McpTransportHeaders headers)
    {
        if (!ctx.IsModern || !_options.ValidateRequestHeaders || headers == null) return null;

        if (!string.IsNullOrWhiteSpace(headers.Method) &&
            !string.Equals(headers.Method, ctx.Method, StringComparison.Ordinal))
        {
            Log("McpHeaderMismatch", new { Header = headers.Method, Body = ctx.Method });
            return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                "Mcp-Method header does not match the request method",
                new JObject { ["header"] = headers.Method, ["body"] = ctx.Method });
        }

        if (!string.IsNullOrWhiteSpace(headers.Name))
        {
            var bodyName = (request["params"] as JObject)?.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(bodyName) &&
                !string.Equals(headers.Name, bodyName, StringComparison.Ordinal))
            {
                Log("McpHeaderMismatch", new { Header = headers.Name, Body = bodyName });
                return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                    "Mcp-Name header does not match the request target",
                    new JObject { ["header"] = headers.Name, ["body"] = bodyName });
            }
        }

        return null;
    }

    /// <summary>Report a method that 2026-07-28 removed, rather than a bare not-found.</summary>
    private string MethodRemoved(McpRequestContext ctx, string method)
    {
        Log("McpMethodRemoved", new { Method = method, ProtocolVersion = ctx.ProtocolVersion });
        return SerializeError(ctx.Id, McpErrorCode.MethodNotFound,
            $"'{method}' was removed in MCP 2026-07-28", method);
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    /// <summary>
    /// server/discover — mandatory in 2026-07-28. Reports supported versions,
    /// capabilities, and identity in a single round trip.
    /// </summary>
    private string HandleDiscover(McpRequestContext ctx)
    {
        var result = new JObject
        {
            ["supportedVersions"] = new JArray(_options.SupportedProtocolVersions),
            ["capabilities"] = BuildCapabilities(isModern: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpDiscovered", new
        {
            Server = _options.ServerInfo.Name,
            Versions = _options.SupportedProtocolVersions.Count
        });

        return SerializeSuccess(ctx, result, McpCacheKind.Discover);
    }

    private JObject BuildCapabilities(bool isModern)
    {
        var capabilities = new JObject();

        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Resources)
        {
            // subscribe is meaningless in the modern era without a subscriptions/listen stream.
            capabilities["resources"] = isModern
                ? new JObject { ["listChanged"] = false }
                : new JObject { ["subscribe"] = false, ["listChanged"] = false };
        }

        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        // Logging is deprecated in 2026-07-28, so it is offered to legacy clients only.
        if (_options.Capabilities.Logging && !isModern)
            capabilities["logging"] = new JObject();

        if (_options.Capabilities.Extensions != null && _options.Capabilities.Extensions.Count > 0)
            capabilities["extensions"] = _options.Capabilities.Extensions;

        return capabilities;
    }

    private JObject BuildServerInfo(bool includeDetail)
    {
        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };

        if (!includeDetail) return serverInfo;

        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;
        if (_options.ServerInfo.Icons != null && _options.ServerInfo.Icons.Count > 0)
            serverInfo["icons"] = _options.ServerInfo.Icons;

        return serverInfo;
    }

    /// <summary>The legacy handshake, for clients on 2025-11-25 and earlier.</summary>
    private string HandleInitialize(McpRequestContext ctx, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString();
        var fallbackLegacyVersion = _options.SupportedProtocolVersions?
            .FirstOrDefault(version => !string.Equals(
                version,
                _options.ProtocolVersion,
                StringComparison.Ordinal))
            ?? _options.ProtocolVersion;
        var negotiated = !string.IsNullOrWhiteSpace(clientProtocolVersion)
            && !string.Equals(clientProtocolVersion, _options.ProtocolVersion, StringComparison.Ordinal)
            && IsSupportedVersion(clientProtocolVersion)
                ? clientProtocolVersion
                : fallbackLegacyVersion;

        var result = new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = BuildCapabilities(isModern: false),
            ["serverInfo"] = BuildServerInfo(includeDetail: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = negotiated
        });

        return SerializeSuccess(ctx, result);
    }

    private string HandleToolsList(McpRequestContext ctx)
    {
        var toolsArray = new JArray();
        foreach (var tool in _toolOrder)
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

        Log("McpToolsListed", new { Count = _toolOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["tools"] = toolsArray }, McpCacheKind.List);
    }

    private string HandleResourcesList(McpRequestContext ctx)
    {
        var resourcesArray = new JArray();
        foreach (var res in _resourceOrder)
        {
            var obj = new JObject
            {
                ["uri"] = res.Uri,
                ["name"] = res.Name
            };
            if (!string.IsNullOrWhiteSpace(res.Description))
                obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType))
                obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0)
                obj["annotations"] = res.Annotations;
            resourcesArray.Add(obj);
        }

        Log("McpResourcesListed", new { Count = _resourceOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["resources"] = resourcesArray }, McpCacheKind.List);
    }

    private string HandleResourceTemplatesList(McpRequestContext ctx)
    {
        var templatesArray = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject
            {
                ["uriTemplate"] = tmpl.UriTemplate,
                ["name"] = tmpl.Name
            };
            if (!string.IsNullOrWhiteSpace(tmpl.Description))
                obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType))
                obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0)
                obj["annotations"] = tmpl.Annotations;
            templatesArray.Add(obj);
        }

        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(ctx, new JObject { ["resourceTemplates"] = templatesArray }, McpCacheKind.List);
    }

    private async Task<string> HandleResourcesReadAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Resource URI is required");

        // 1. Try exact match on registered static resources
        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);
                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
            }
        }

        // 2. Try matching against registered resource templates
        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }

        // 2026-07-28 moved resource-not-found from -32002 to -32602 (Invalid Params).
        return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    /// <summary>
    /// Simple URI template matcher. Checks if a concrete URI matches a template
    /// with {param} placeholders (e.g., "data://records/{id}" matches "data://records/123").
    /// </summary>
    private static bool MatchesUriTemplate(string template, string uri)
    {
        // Split both on '/' and compare segments
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return false;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue; // wildcard
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Extract named parameters from a URI given a template pattern.
    /// E.g., template "data://records/{id}" with uri "data://records/123" returns { "id": "123" }.
    /// </summary>
    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return result;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
            {
                var paramName = seg.Substring(1, seg.Length - 2);
                result[paramName] = uriParts[i];
            }
        }
        return result;
    }

    private string HandlePromptsList(McpRequestContext ctx)
    {
        var promptsArray = new JArray();
        foreach (var prompt in _promptOrder)
        {
            var obj = new JObject
            {
                ["name"] = prompt.Name
            };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                obj["description"] = prompt.Description;

            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description))
                        argObj["description"] = arg.Description;
                    if (arg.Required)
                        argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }

            promptsArray.Add(obj);
        }

        Log("McpPromptsListed", new { Count = _promptOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["prompts"] = promptsArray }, McpCacheKind.List);
    }

    private async Task<string> HandlePromptsGetAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Prompt name is required");

        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");

        Log("McpPromptGetStarted", new { Prompt = promptName });

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });

            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                result["description"] = prompt.Description;

            return SerializeSuccess(ctx, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private async Task<string> HandleToolsCallAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName, IsRetry = ctx.InputResponses != null });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            // Multi round-trip request: the tool needs more input before it can finish.
            if (result != null && result.Value<bool?>("__mcpInputRequired") == true)
                return SerializeInputRequired(ctx, toolName, result);

            JObject callResult;

            // Support pre-formatted MCP tool results with rich content types
            // (image, audio, resource, or mixed content arrays).
            // If the handler returns { "content": [ { "type": "..." } ], ... },
            // pass it through directly instead of wrapping in text.
            if (result != null && result["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = result.Value<bool?>("isError") ?? false
                };

                // structuredContent may be any JSON value as of 2026-07-28.
                if (result["structuredContent"] != null)
                    callResult["structuredContent"] = result["structuredContent"];
            }
            else
            {
                var text = result == null
                    ? "{}"
                    : result.ToString(Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(ctx, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(ctx, new JObject
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
            return SerializeSuccess(ctx, new JObject
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

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    /// <summary>
    /// Emit an input_required result. Legacy clients cannot interpret resultType, so they
    /// receive a tool error naming what the tool still needs rather than a silent failure.
    /// </summary>
    private string SerializeInputRequired(McpRequestContext ctx, string toolName, JObject marker)
    {
        var inputRequests = marker["inputRequests"] as JObject ?? new JObject();

        if (!ctx.IsModern)
        {
            var needed = string.Join(", ", inputRequests.Properties().Select(p => p.Name));
            Log("McpInputRequiredUnsupported", new { Tool = toolName, Needed = needed });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    TextContent($"'{toolName}' needs additional input ({needed}). That requires an MCP client on 2026-07-28 or later; supply the values as tool arguments instead.")
                },
                ["isError"] = true
            });
        }

        var result = new JObject { ["inputRequests"] = inputRequests };

        var requestState = marker.Value<string>("requestState");
        if (!string.IsNullOrWhiteSpace(requestState))
            result["requestState"] = requestState;

        Log("McpInputRequired", new { Tool = toolName, Count = inputRequests.Count });

        return SerializeSuccess(ctx, result, McpCacheKind.None, "input_required");
    }

    // ── Content Helpers ────────────────────────────────────────────────
    //
    //    Use these to build rich tool results with image, audio, or resource
    //    content. Return McpRequestHandler.ToolResult(...) from your handler
    //    to bypass automatic text wrapping.
    //

    /// <summary>Create a text content item.</summary>
    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>Create an image content item (base64-encoded).</summary>
    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an audio content item (base64-encoded).</summary>
    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an embedded resource content item.</summary>
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    /// <summary>
    /// Create a resource link. Points the client at a resource it can fetch itself,
    /// instead of embedding the bytes in the tool result.
    /// </summary>
    public static JObject ResourceLinkContent(string uri, string name, string description = null, string mimeType = null)
    {
        var link = new JObject { ["type"] = "resource_link", ["uri"] = uri, ["name"] = name };
        if (!string.IsNullOrWhiteSpace(description)) link["description"] = description;
        if (!string.IsNullOrWhiteSpace(mimeType)) link["mimeType"] = mimeType;
        return link;
    }

    /// <summary>
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JToken structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── Multi Round-Trip Requests ──────────────────────────────────────

    /// <summary>
    /// Ask the client for more input before the tool can finish. Return this from a tool
    /// handler; the client answers by retrying the same tools/call with inputResponses
    /// attached, which arrive on McpRequestContext.InputResponses.
    ///
    /// This is how a stateless server performs elicitation as of 2026-07-28. Because the
    /// server holds no state between the two calls, encode anything you need to resume in
    /// requestState and read it back off the retry.
    /// </summary>
    public static JObject InputRequired(JObject inputRequests, string requestState = null)
    {
        var result = new JObject
        {
            ["__mcpInputRequired"] = true,
            ["inputRequests"] = inputRequests ?? new JObject()
        };
        if (!string.IsNullOrWhiteSpace(requestState)) result["requestState"] = requestState;
        return result;
    }

    /// <summary>Build a single elicitation entry for an InputRequired result.</summary>
    public static JObject ElicitationRequest(string message, Action<McpSchemaBuilder> schemaConfig, string mode = "form")
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        return new JObject
        {
            ["method"] = "elicitation/create",
            ["params"] = new JObject
            {
                ["mode"] = mode,
                ["message"] = message,
                ["requestedSchema"] = builder.Build()
            }
        };
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(McpRequestContext ctx, JObject result)
    {
        return SerializeSuccess(ctx, result, McpCacheKind.None, "complete");
    }

    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache)
    {
        return SerializeSuccess(ctx, result, cache, "complete");
    }

    /// <summary>
    /// Envelope a successful result. Modern callers additionally get resultType, server
    /// identity in _meta, and cache hints. Legacy callers get exactly the shape they got
    /// before 2026-07-28, which is what keeps Copilot Studio working unchanged.
    /// </summary>
    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache, string resultType)
    {
        result = result ?? new JObject();

        if (ctx != null && ctx.IsModern)
        {
            result["resultType"] = resultType;

            var meta = result["_meta"] as JObject ?? new JObject();
            meta[McpKeys.ServerInfo] = BuildServerInfo(includeDetail: false);
            result["_meta"] = meta;

            // Only complete results are cacheable; input_required is interim.
            if (resultType == "complete") ApplyCacheHints(result, cache);
        }

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ctx?.Id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void ApplyCacheHints(JObject result, McpCacheKind cache)
    {
        switch (cache)
        {
            case McpCacheKind.List:
                result["ttlMs"] = Math.Max(0, _options.ListCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;

            case McpCacheKind.Resource:
                result["ttlMs"] = Math.Max(0, _options.ResourceCacheTtlMs);
                result["cacheScope"] = _options.ResourceCacheScope ?? "private";
                break;

            case McpCacheKind.Discover:
                result["ttlMs"] = Math.Max(0, _options.DiscoverCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;
        }
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, JToken data = null)
    {
        var error = new JObject
        {
            ["code"] = (int)code,
            ["message"] = message
        };

        if (data != null && data.Type != JTokenType.Null)
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

/// <summary>Which cache hints a result should carry, per the 2026-07-28 CacheableResult rules.</summary>
public enum McpCacheKind
{
    None,
    List,
    Resource,
    Discover
}

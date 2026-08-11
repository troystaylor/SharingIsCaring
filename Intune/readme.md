# Microsoft Intune

A dual-purpose Power Platform custom MCP connector for Microsoft Intune. It uses Microsoft Graph v1.0 whenever an operation is available there and beta only for APIs that Microsoft has not promoted.

One connector, two ways to use it:

1. **MCP server** — the `InvokeMCP` operation speaks JSON-RPC 2.0 and exposes **120 Intune tools**, 3 resources, and 3 prompts to any Model Context Protocol client, including Copilot Studio, Microsoft Foundry, and Agent Framework.
2. **REST actions** — **118 named operations** call Microsoft Graph directly for use in Power Automate flows and Logic Apps.

Both share one connection and one set of delegated Graph permissions.

## Why a passthrough is included

Intune is large. Parsed from the live Graph beta metadata document, the Intune surface is:

| Metric | Count |
|---|---|
| Collections under `/deviceManagement` | 173 |
| Collections under `/deviceAppManagement` | 27 |
| Distinct URL path shapes | 378 |
| CRUD operations | 930 |
| Bound actions and functions | 724 |
| **Total operations** | **~1,654** |

Naming all of them would produce an unusable connector. Instead this connector names the highest-value 120 tools and reaches the remaining surface through `intune_find_endpoint`, `intune_describe_entity`, `intune_graph_request`, and `intune_graph_batch`. Coverage is complete; the surface stays navigable.

## Prerequisites

1. An **active Microsoft Intune license** in the tenant. The Graph Intune APIs return 403 without one.
2. A **Microsoft Entra app registration** with the delegated permissions below.
3. **Admin consent** — every `DeviceManagement*` scope requires it.
4. The signed-in user must hold an **Intune administrator role** with the relevant RBAC permissions.

## Setup

### 1. Register an Entra application

1. Go to [Azure Portal](https://portal.azure.com) → Microsoft Entra ID → App registrations.
2. **New registration**, name it (for example "Intune Connector").
3. Supported account types: **Accounts in any organizational directory**.
4. Redirect URI (type Web): `https://global.consent.azure-apim.net/redirect`
5. **Register**.

### 2. Add API permissions

Add these **delegated** Microsoft Graph permissions:

| Scope | Unlocks |
|---|---|
| `DeviceManagementManagedDevices.ReadWrite.All` | Devices, inventory, detected apps, most remote actions |
| `DeviceManagementManagedDevices.PrivilegedOperations.All` | Wipe, retire, reset passcode, remote lock |
| `DeviceManagementConfiguration.ReadWrite.All` | Compliance policies, configuration profiles, settings catalog |
| `DeviceManagementApps.ReadWrite.All` | Applications, app protection, app configuration |
| `DeviceManagementServiceConfig.ReadWrite.All` | Enrollment, Autopilot, APNs/VPP/DEP tokens, tenant config |
| `DeviceManagementRBAC.ReadWrite.All` | Roles, role assignments, scope tags |
| `DeviceManagementScripts.ReadWrite.All` | PowerShell scripts, shell scripts, proactive remediations |
| `CloudPC.ReadWrite.All` | Windows 365 Cloud PCs |
| `BitlockerKey.Read.All` | BitLocker recovery keys from Entra ID |
| `User.Read` | Sign in and read the administrator profile |
| `offline_access` | Refresh token for a durable connection |

Then click **Grant admin consent**.

> Use the `.Read.All` variants instead of `.ReadWrite.All` for a read-only deployment. The write tools will then fail with 403, which is the intended behavior.

### 3. Create a client secret

**Certificates & secrets** → **New client secret** → copy the **Value**.

### 4. Deploy the connector

```powershell
# First deployment
pac connector create `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx

# Subsequent updates
pac connector update `
    --connector-id <CONNECTOR_ID> `
    --api-definition-file apiDefinition.swagger.json `
    --api-properties-file apiProperties.json `
    --script-file script.csx
```

Replace `[YOUR_CLIENT_ID]` and `[YOUR_CLIENT_SECRET]` in `apiProperties.json` with the Entra application credentials before creating connections.

To validate before deploying:

```powershell
npm install -g ppcv
ppcv .
```

## Using it as an MCP server

Point any MCP client at the `InvokeMCP` operation. The connector declares `x-ms-agentic-protocol: mcp-streamable-1.0`, so Copilot Studio discovers it automatically when the connector is added as a tool.

The server is **dual-era**: it answers the MCP `2026-07-28` stateless protocol and the older `initialize` handshake from the same endpoint, choosing per request. Clients that use the legacy handshake are served without modern-only response fields.

### Tool catalog (120)

| Group | Count | Tools |
|---|---|---|
| Device inventory | 9 | `list_managed_devices`, `get_managed_device`, `search_devices`, `delete_managed_device`, `list_device_detected_apps`, `get_device_compliance_states`, `get_device_configuration_states`, `get_device_encryption_state`, `list_device_categories` |
| Remote actions | 16 | `sync_device`, `retire_device`, `wipe_device`, `reboot_device`, `shutdown_device`, `remote_lock_device`, `reset_passcode`, `locate_device`, `enable_lost_mode`, `disable_lost_mode`, `rotate_bitlocker_keys`, `rotate_filevault_key`, `rotate_local_admin_password`, `defender_scan`, `defender_update_signatures`, `send_custom_notification` |
| Bulk and audit | 2 | `bulk_device_action`, `get_remote_action_audits` |
| Diagnostics | 4 | `collect_device_logs`, `list_log_collection_requests`, `download_device_logs`, `get_device_sync_status` |
| Troubleshooting | 3 | `get_noncompliant_settings`, `list_troubleshooting_events`, `list_autopilot_events` |
| Secrets and recovery | 3 | `get_local_admin_password`, `get_filevault_key`, `get_bitlocker_recovery_key` |
| Compliance | 7 | `list_compliance_policies`, `get_compliance_policy`, `create_compliance_policy`, `update_compliance_policy`, `delete_compliance_policy`, `assign_compliance_policy`, `get_compliance_policy_statuses` |
| Configuration | 14 | `list_device_configurations`, `get_device_configuration`, `create_device_configuration`, `update_device_configuration`, `delete_device_configuration`, `assign_device_configuration`, `list_configuration_policies`, `get_configuration_policy`, `get_configuration_policy_settings`, `create_configuration_policy`, `update_configuration_policy`, `delete_configuration_policy`, `list_configuration_policy_assignments`, `assign_configuration_policy` |
| Applications | 8 | `list_mobile_apps`, `get_mobile_app`, `create_mobile_app`, `update_mobile_app`, `delete_mobile_app`, `assign_mobile_app`, `get_app_install_status`, `list_app_protection_policies` |
| Enrollment and Autopilot | 7 | `list_autopilot_devices`, `get_autopilot_device`, `assign_autopilot_user`, `delete_autopilot_device`, `list_autopilot_profiles`, `assign_autopilot_profile`, `list_enrollment_configurations` |
| Scripts and remediations | 6 | `list_device_scripts`, `get_device_script`, `assign_device_script`, `get_script_run_states`, `list_health_scripts`, `get_remediation_summary` |
| Windows Update | 3 | `list_feature_update_profiles`, `list_quality_update_policies`, `list_driver_update_profiles` |
| Elevation and approvals | 4 | `list_elevation_requests`, `approve_elevation_request`, `list_approval_requests`, `decide_operation_request` |
| Cloud PC | 4 | `list_cloud_pcs`, `reprovision_cloud_pc`, `resize_cloud_pc`, `restore_cloud_pc` |
| Assignment filters | 2 | `list_assignment_filters`, `evaluate_assignment_filter` |
| RBAC | 3 | `list_role_definitions`, `list_role_assignments`, `get_effective_permissions` |
| Tenant tokens | 3 | `get_apple_push_certificate`, `list_vpp_tokens`, `list_dep_tokens` |
| Reporting | 4 | `list_intune_reports`, `run_intune_report`, `export_intune_report`, `get_export_job_status` |
| Monitoring | 3 | `list_audit_events`, `list_alert_records`, `get_ux_analytics_overview` |
| Universal access | 4 | `intune_find_endpoint`, `intune_describe_entity`, `intune_graph_request`, `intune_graph_batch` |
| Agent workflows | 11 | `inventory_all_policies`, `analyze_policy_hygiene`, `list_setting_definitions`, `compare_setting_catalog`, `assess_policy_rollout`, `assess_policy_change_risk`, `run_device_hygiene_check`, `get_device_update_failures`, `get_device_policy_failures`, `get_device_app_and_script_failures`, `get_device_security_health` |

### Agent authorization phases

**Phase 1 — authorized IT users**

- Enable the complete read surface, policy create/update/delete/assignment tools, and approved device remediation actions.
- Compliance policies, classic device configurations, and Settings Catalog policies all have complete named lifecycle and assignment coverage.
- Require explicit user confirmation for destructive policy deletion, device deletion, retire, wipe, and other operations marked with `destructiveHint`.
- Keep `intune_graph_request` and `intune_graph_batch` restricted to this phase because they support `POST`, `PATCH`, `PUT`, and `DELETE`.
- Use `run_device_hygiene_check` and the focused failure tools before proposing a remediation action.

**Phase 2 — end users**

- Allow only tools marked `readOnlyHint: true`.
- Exclude all create, update, delete, assignment, secret/recovery, approval, remote-action, Cloud PC mutation, `intune_graph_request`, and `intune_graph_batch` tools.
- `intune_find_endpoint` and `intune_describe_entity` may remain available because they are read-only.
- Use a separate connector connection with read-only Microsoft Graph delegated permissions. Tool allowlisting does not reduce the permissions carried by an OAuth token.

### Targeted policy CRUD coverage

| Policy family | Create | Read | Update | Delete | Assign |
|---|---:|---:|---:|---:|---:|
| Compliance policies | Yes | Yes | Yes | Yes | Yes |
| Classic device configurations | Yes | Yes | Yes | Yes | Yes |
| Settings Catalog policies | Yes | Yes | Yes | Yes | Yes |

The phase-one device-remediation surface includes sync, restart/shutdown, lock, passcode reset, locate/lost mode, key rotation, Defender scan/signature update, notifications, retire, and wipe. These are action operations rather than CRUD on the physical device.

### Completed agent diagnostics

- **Policy change risk**: `assess_policy_change_risk` resolves Microsoft Settings Catalog definition metadata and produces a transparent technical rollout score from Microsoft's `riskLevel`, dependencies, unresolved definitions, and assignment blast radius. An existing Settings Catalog policy can be supplied to include current assignments and rollout evidence. The result includes required deployment controls and explicitly remains subject to customer change approval and current Microsoft Learn guidance.
- **Failed configuration policies**: `get_device_policy_failures` uses `getConfigurationPoliciesReportForDevice` with the managed-device id and normalizes status codes across classic configuration, Settings Catalog, imported ADMX, and endpoint-security intent policies. Error and conflict rows are returned separately while compliance states and exact noncompliant settings remain included.
- **Script and remediation failures**: `get_device_app_and_script_failures` correlates Windows PowerShell, macOS shell, and custom-attribute script device run states through batched Graph requests. It also reads the managed device's `deviceHealthScriptStates` relationship for detection failures, remediation failures, script errors, and captured output.

### Reporting is data-driven

Intune exposes **71** report functions. Rather than spend 71 tools on them, three tools cover all of them:

```
list_intune_reports(search: "compliance")   → discover the exact report name
run_intune_report(report_name, filter, top) → synchronous rows
export_intune_report + get_export_job_status → large async CSV/JSON export
```

### Reaching an endpoint with no named tool

```
intune_find_endpoint(query: "microsoft tunnel")
  → "GET,POST /deviceManagement/microsoftTunnelSites"
    "GET,POST /deviceManagement/microsoftTunnelConfigurations"
    ...

intune_describe_entity(path: "/deviceManagement/microsoftTunnelSites")
  → field names and types sampled from a live record

intune_graph_request(method: "GET", path: "/deviceManagement/microsoftTunnelSites")
  → the actual data
```

`intune_graph_request` defaults to `api_version: "auto"`. Auto mode selects known beta-only routes directly, tries v1.0 for all other routes, and retries beta only when Graph reports that the route is unavailable in v1.0. Set `api_version` explicitly only when testing a version-specific contract.

The catalog embedded in the connector holds **556** endpoint entries covering every Intune collection and bound operation.

### Resources and prompts

Resources: `intune://endpoints/catalog`, `intune://reports/catalog`, `intune://permissions/scopes`

Prompts: `diagnose_device`, `offboard_device`, `compliance_audit`

## Using it in Power Automate

The Swagger definition exposes 118 named actions to the flow designer, including **List managed devices**, **Wipe device**, **Assign compliance policy**, and **Run Intune report**.

Typical flow: a recurrence trigger → **List managed devices** with `$filter` of `complianceState eq 'noncompliant'` → apply to each → **Send custom notification**.

## Safety model

- The passthrough tools are **namespace-guarded**. Paths must begin with `/deviceManagement`, `/deviceAppManagement`, `/informationProtection/bitlocker`, `/admin/windows/updates`, or `/$batch`. Anything else is rejected before the request is sent, so the connector cannot be steered at unrelated Graph data such as `/users` or `/messages`.
- Destructive tools carry `destructiveHint: true` so MCP clients can require confirmation.
- Server instructions tell the agent to confirm device identity before running `wipe_device` or `retire_device`.
- Tool errors are returned as tool results rather than protocol errors, so a model can read the Graph error message and correct itself.

## Implementation notes

- **Stable-first routing**: the connector definition targets `https://graph.microsoft.com/v1.0`. An audit against Microsoft's official Graph v1.0 OpenAPI metadata and Microsoft Learn classified the 118 named REST operations as 66 v1.0 and 52 beta-only operations. Each operation records that result in `x-ms-graph-version`.
- **MCP version selection** defaults to `auto`: known beta-only routes go directly to beta, all other routes try v1.0 first, and route-not-available responses retry against beta. Explicit `v1.0` and `beta` overrides remain available.
- **Batch requests** are partitioned by API version and their responses are merged back into original request order.
- **`Prefer: include-unknown-enum-members`** is sent on every request. Intune adds enum members frequently, and without this header Graph fails requests whose responses contain a newer enum value.
- **Remote actions are asynchronous.** Graph returns `204 No Content`; the device performs the action at its next check-in. The action tools report this explicitly rather than implying completion.
- **Paging** uses `@odata.nextLink`, returned verbatim in responses.
- **Throttling**: Intune returns `429` with a `Retry-After` header. Use `intune_graph_batch` to reduce request volume when working across many devices.
- The MCP framework in Section 2 of `script.csx` is based on the Power MCP Template and includes connector-specific protocol negotiation and tool-result handling.

## Files

| File | Purpose |
|---|---|
| `apiDefinition.swagger.json` | 119 operations — `InvokeMCP` plus 118 named REST actions |
| `apiProperties.json` | Entra OAuth configuration and the 119 script operations |
| `script.csx` | Connector logic: MCP server with 120 tools, plus REST passthrough |
| `readme.md` | This file |

## Verification performed

- `ppcv` validates the three deployable connector artifacts (`apiDefinition.swagger.json`, `apiProperties.json`, and `script.csx`) — 119 operations, 0 errors, 0 warnings.
- `script.csx` reports no workspace diagnostics, and MCP tool handlers preserve their `JObject` result type end to end.
- Structural checks confirm: 120 tools, 3 resources, and 3 prompts are registered; tool schemas contain no `$ref` or `$defs` constructs that Copilot Studio would silently drop.
- Legacy `initialize` response verified free of `resultType`, `ttlMs`, and `cacheScope`, which would break Copilot Studio.
- Legacy `initialize` negotiates only configured legacy protocol versions and falls back to the preferred legacy version for unsupported requests.
- Modern `server/discover` verified working.
- Namespace guard verified to reject `/users`.
- `apiDefinition.swagger.json` validates against the OpenAPI 2.0 schema.
- Swagger operation IDs and `scriptOperations` verified to match exactly, both directions.
- All 118 REST operations have version metadata: 66 v1.0 and 52 beta-only, matching Microsoft Graph's official v1.0 OpenAPI metadata and Microsoft Learn.

## References

- [Intune Graph API overview](https://learn.microsoft.com/intune/intune-service/developer/intune-graph-apis)
- [Intune permission scopes](https://learn.microsoft.com/intune/intune-service/developer/intune-graph-apis#intune-permission-scopes)
- [Microsoft Graph v1.0 Intune reference](https://learn.microsoft.com/graph/api/resources/intune-graph-overview?view=graph-rest-1.0)
- [Graph beta reference for Intune](https://learn.microsoft.com/graph/api/resources/intune-graph-overview)
- [Get an Intune report export job in v1.0](https://learn.microsoft.com/graph/api/intune-reporting-devicemanagementexportjob-get?view=graph-rest-1.0)
- [Export Intune reports with Graph](https://learn.microsoft.com/intune/intune-service/fundamentals/reports-export-graph-apis)
- [Model Context Protocol](https://modelcontextprotocol.io)

# Power Platform Admin

MCP server for cross-environment Power Platform administration through natural language in Copilot Studio. Manages environment settings, capacity allocations, Copilot governance, security recommendations, connectors, apps, and application packages via the Power Platform Admin API.

## Overview

This connector exposes 22 admin tools across 4 categories through a single MCP endpoint, letting Copilot Studio agents perform cross-environment administration without opening the Power Platform admin center.

| Category | Tools | Purpose |
|----------|-------|---------|
| Environment Management | 5 | List environments, get details, read/update PPAC settings, compare settings across environments |
| Governance & Security | 13 | Copilot governance, capacity allocations and enforcement, Copilot Studio tenant pool draw, resource thresholds, security recommendations, cross-tenant connection audit |
| Resource Inventory | 3 | List connectors and Power Apps per environment, inventory agents across the tenant |
| Application Lifecycle | 1 | Install Microsoft application packages |

## Mutating tools require explicit confirmation

Every tool that changes tenant or environment configuration refuses to run unless the caller passes `confirm: true`. A description that merely says "confirm with the user" is not a control — an agent resolving an ambiguous instruction can skip it. The gate is enforced in the tool router, so it cannot be bypassed by calling the tool directly.

Gated tools: `admin_update_setting`, `admin_update_copilot_governance`, `admin_set_tenant_pool_draw`, `admin_upsert_resource_threshold`, `admin_update_environment_allocation`, and `admin_install_package`.

The typed Power Automate operations are deliberately **not** gated. A maker who drops the action into a designer has already made the decision explicitly, and adding a required body property would break every existing flow.

## Prerequisites

1. **Azure AD App Registration** with the following API permissions for `Power Platform API` (resource ID `8578e004-a5c6-46e7-913e-12f58912df43`):
   - `EnvironmentManagement.Environments.Read`
   - `EnvironmentManagement.Settings.Read`
   - `EnvironmentManagement.Settings.ReadWrite`
   - `CopilotGovernance.Features.Read`
   - `CopilotGovernance.Settings.Read`
   - `CopilotGovernance.Settings.Write`
   - `Licensing.Allocations.Read`
   - `Licensing.Allocations.ReadWrite`
   - `Security.Recommendations.Read`
   - `Analytics.AdvisorRecommendations.Read`
   - `Governance.CrossTenantConnectionReports.Read`
   - `Connectivity.Connectors.Read`
   - `PowerApps.Apps.Read`
   - `AppManagement.ApplicationPackages.Install`
   - `AppManagement.ApplicationPackages.Read`

   `admin_list_agents` needs no additional permission — the `resourcequery` endpoint it calls was verified to work with the delegated scopes above plus a Power Platform admin role. There is no documented `resourcequery` scope to grant.

2. **Power Platform admin role** (System Administrator, Power Platform Administrator, or Dynamics 365 Administrator)

   The allocation and tenant pool tools additionally require the caller to hold **Power Platform Administrator** or **Global Administrator** — the same tenant admin roles that gate licensing and capacity settings in PPAC. The `Licensing.Allocations.*` scopes are documented for the Currency Allocation API that these tools call. The endpoint reference itself advertises only the `.default` scope, so confirm with a read before relying on a write.

   The resource threshold tools call the supported `licensing/entitlements/.../resourceThresholds` and `licensing/.../threshold` routes. No threshold-specific scope is published, so the same `Licensing.Allocations.*` scopes plus a tenant admin role are what to grant; verify with `admin_list_resource_thresholds` before relying on the write.

3. **Copilot Studio** license for MCP integration

## Setup

1. Register an Azure AD application and grant the permissions listed above.
2. Update `apiProperties.json` with your app's `clientId`.
3. Import the connector into Power Platform:
   ```
   pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx
   ```
4. Create a connection using OAuth and sign in with an admin account.
5. Add the connector as an action in your Copilot Studio agent.

## Tools (Copilot Studio)

These are exposed through the single `/mcp` endpoint for agent use. If you are building a flow rather than an agent, see **Power Automate Operations** below instead.

### Environment Management

| Tool | Description |
|------|-------------|
| `admin_list_environments` | List all environments with capacity metrics, types, states, and Dataverse URLs |
| `admin_get_environment` | Full details of a specific environment including runtime endpoints and protection status |
| `admin_get_settings` | Get PPAC management settings (SAS IP rules, audit logging, etc.) |
| `admin_update_setting` | Update management settings on an environment |
| `admin_compare_settings` | Compare a setting value across all environments |

### Governance & Security

| Tool | Description |
|------|-------------|
| `admin_get_copilot_governance` | Get Copilot governance features and settings (tenant or environment scope) |
| `admin_update_copilot_governance` | Update Copilot governance settings |
| `admin_list_environment_allocations` | List capacity allocations for every environment, with enforcement rules per currency |
| `admin_get_environment_allocations` | Get the allocation document for one environment |
| `admin_update_environment_allocation` | Set the allocated amount and/or enforcement rules for one currency on one environment |
| `admin_get_allocation_availability` | Check how much of each entitlement is still available to allocate |
| `admin_get_reserved_entitlements` | Get reserved quantity per entitlement |
| `admin_get_tenant_pool_draw` | Check whether an environment draws Copilot Studio message and session capacity from the tenant pool |
| `admin_set_tenant_pool_draw` | Enable or disable drawing Copilot Studio capacity from the tenant pool |
| `admin_list_resource_thresholds` | List the resource thresholds configured for a licensing entitlement, with limits, consumption, and stop flags |
| `admin_upsert_resource_threshold` | Create or update the consumption threshold for an environment resource on an entitlement |
| `admin_get_security_recommendations` | Get security recommendations from Power Platform Advisor |
| `admin_get_cross_tenant_connections` | Cross-tenant connection reports for compliance auditing |

#### Capacity allocations

The allocation tools call the documented **Allocations By Environment** API (`GET`/`PATCH /licensing/allocationsByEnvironment`, api-version `2024-10-01`) added in July 2026. They cover every documented `ExternalCurrencyType` — AI, Copilot Studio messages and sessions, Power Pages authenticated and anonymous, hosted and unattended RPA, Power Automate per-process, Process Mining storage, and the rest — with the `Alert`, `PayGo`, `TenantPool`, and `Deny` enforcement rules.

Earlier versions of this connector reached a tenant-routed host (`{tenant}.tenant.api.powerplatform.com/licensing/allocations`) that was never part of the published REST reference. That path is gone. Nothing in the connector now calls an undocumented licensing route.

> **Writes are read-modify-write and verified.** `admin_update_environment_allocation` and `admin_set_tenant_pool_draw` read the current allocation, change only the currency you named, send the **whole** `currencyAllocations` array back, then re-read and compare. Sending the full array makes the outcome identical whether the service merges or replaces the collection — the one behaviour the published contract does not state.
>
> The response carries `verified`. When it is `false` the write was accepted but the read-back did not match, which almost always means a concurrent edit from PPAC or another caller. The service exposes no ETag, so this is detection rather than prevention: re-read before making further changes, and avoid running these tools on a fan-out loop.

> **`Deny` stops consumption.** Enabling `Deny` on a currency halts consumption once the allocation is exhausted, which can break running apps and agents. `Throttle` appears on the `allocationsV2` surface but not on the by-environment API, so the connector rejects it rather than sending a value the endpoint does not document.

> **Availability and reserved filters are unspecified.** `admin_get_allocation_availability` and `admin_get_reserved_entitlements` accept an OData `$filter`, but Microsoft does not publish the filterable fields. Prefer calling them unfiltered and narrowing the result yourself. Reserved quantities do not include enforcement rules — use `admin_get_environment_allocations` for those.

> **Resource thresholds are documented but replace-on-write.** `admin_upsert_resource_threshold` calls the published `licensing/.../threshold` `PUT`, which replaces the whole threshold document. The connector reads the current threshold for the environment and resource first and merges only the fields you supply, so an update that sets `limit` alone will not clear `notificationThreshold` or the stop flags. If the read fails — for example, when the caller can write but not read licensing data — the call degrades to a create and unspecified fields are left unset. There is no ETag, so a concurrent edit can still be lost.
>
> `entitlementId` is the licensing entitlement the resource consumes, such as `MCSMessages` or `MCSSessions`. Run `admin_list_resource_thresholds` first to discover the `resourceId` values that already have a threshold.

### Resource Inventory

| Tool | Description |
|------|-------------|
| `admin_list_connectors` | List connectors in an environment (certified, custom, virtual, MCP) |
| `admin_list_apps` | List Power Apps in an environment with owner and sharing status |
| `admin_list_agents` | Inventory Copilot Studio and Agent Builder agents with environment, sharing, identity, and provenance detail, plus a computed risk level |

> **Agent inventory reads undocumented properties.** `admin_list_agents` calls `resourcequery`, which returns the resource provider's raw property bag. Fields such as `isCLIAgent` are not in the published reference and may change shape or disappear without notice. Use them for reporting and triage, not as a hard enforcement gate.
>
> `isCLIAgent` is returned as `"true"`, `"false"`, or `"unknown"` rather than a boolean. `"unknown"` means the platform did not report the field at all, which is not the same as `false` — an undocumented field may simply be absent on older agents.
>
> `riskLevel` (None/Low/Medium/High) is computed from an additive `riskScore`, and every agent carries the `riskSignals` that produced it so the judgment stays auditable. Signals are `quarantined` (+3), `sharedWithEntireTenant` (+2), `broadEditorAccess` (+2, 10 or more editor users — editors can rewrite instructions and tools, so breadth here matters more than viewer sharing), `cliAuthoredAndTenantWide` (+2), `missingEntraAgentIdPostMandate` (+2, no Entra Agent ID on an agent created after the July 2026 mandate), `noEntraAgentId` (+1, same gap on an older agent), `createdOutsideMakerPortal` (+1), `defaultEnvironment` (+1), `unmanagedEnvironment` (+1, non-default environments only), `tenantWideInNonProductionEnvironment` (+1), and `neverPublished` (−1, mitigating — an unpublished agent has no runtime exposure). Score floors at 0.
>
> The default environment is scored as `defaultEnvironment` rather than `unmanagedEnvironment` because it can never be a Managed Environment — treating it as unmanaged would fire on nearly every agent in most tenants and flatten the risk distribution.
>
> Omitting `environmentId` inventories the whole tenant. The connector pages internally with `Skip` offsets and stops after 10 pages (10,000 agents), setting `truncated: true` rather than silently returning a partial estate. Scope to an environment if you hit that ceiling.
>
> **Paging notes (verified against the live API).** `SkipToken` is returned but does **not** advance between calls — requesting the next page with the token alone replays the same rows. `Skip` is the only working pager. Because `orderby createdAt` alone is not a total order (ties are common; agents provisioned by a template share a timestamp to the second), the sort carries `name` as a unique tiebreaker — without it the skip window shifts between requests and silently drops rows. Results are also de-duplicated by resource name as a safety net.

### Application Lifecycle

| Tool | Description |
|------|-------------|
| `admin_install_package` | Install a Microsoft application package in an environment |

## Power Automate Operations

The connector is dual-mode. The same capabilities are also exposed as typed REST operations with full request and response schemas, so they appear as ordinary actions in the Power Automate and Logic Apps designers with IntelliSense and dynamic dropdowns — no MCP knowledge required.

| Action | Method | Inputs |
|--------|--------|--------|
| **List Environments** | GET | none |
| **Get Environment** | GET | `environmentId` (required) |
| **Get Settings** | GET | `environmentId` (required), `select` (optional, advanced) |
| **Update Settings** | POST | `environmentId` (required), body `{ "settings": { "Name": value } }` |
| **Get Draw From Tenant Pool** | GET | `environmentId` (required) |
| **Set Draw From Tenant Pool** | POST | `environmentId` (required), body `{ "enabled": true }` |
| **List Environment Allocations** | GET | none |
| **Get Environment Allocations** | GET | `environmentId` (required) |
| **Update Environment Allocation** | POST | `environmentId` (required), body `{ "currencyType": "AI", "allocated": 250, "enforcementRules": [{ "ruleType": "Deny", "enabled": true }] }` |
| **Get Allocation Availability** | GET | `filter` (optional, advanced) |
| **Get Reserved Entitlements** | GET | `filter` (optional, advanced) |
| **List Resource Thresholds** | GET | `entitlementId` (required), `environmentId` (optional filter) |
| **Upsert Resource Threshold** | POST | `entitlementId`, `environmentId`, `resourceId` (all required), body with any of `limit`, `notificationThreshold`, `notifyIfOverCapacity`, `resourceConsumption`, `stopIfOverCapacity`, `stopResource` |
| **List Agents** | GET | `environmentId` (optional — leave blank to inventory the whole tenant) |

Every `environmentId` field renders as a picker of environment display names, populated at design time by an internal `Get Environment List` operation, so you never have to paste a GUID.

`Invoke Power Platform Admin MCP` also appears in the action list. Ignore it in a flow — it is the JSON-RPC endpoint for Copilot Studio and expects an MCP protocol body, not flow inputs.

### Notes for flow authors

- **List Agents returns one object, not a paged list.** The shape is `agentCount`, `truncated`, `environmentId`, and an `agents` array — apply-to-each over `agents`. Check `truncated` before treating the result as a complete inventory; `true` means the page ceiling was reached and agents are missing.
- **`isCLIAgent` is a string, not a boolean.** Compare against `'true'`, `'false'`, or `'unknown'`. A condition that tests it as a boolean will silently never match, and `'unknown'` must not be treated as `'false'`.
- **`riskSignals` and `channels` are string arrays.** Flatten with `join(item()?['riskSignals'], ', ')` for a table or email body.
- **Update Settings, Set Draw From Tenant Pool, Update Environment Allocation, and Upsert Resource Threshold are writes.** The allocation writes read the current document, change only the currency you named, write the whole document back, and re-read to verify — check the `verified` field on the response. A `false` there means a concurrent edit landed between the write and the read-back; the service offers no ETag. Upsert Resource Threshold merges into the current threshold, but the underlying call is still a full replace with no ETag. Read first, and avoid running any of them on a fan-out loop.
- **Update Environment Allocation changes one currency per call.** Other currencies on the environment are preserved. Omit `allocated` to change only enforcement rules, or omit `enforcementRules` to change only the amount; a call that supplies neither is rejected rather than silently rewriting the document.
- **Upsert Resource Threshold only sends the body fields you supply.** Leave a property out of the body to keep its current value; sending `null` is treated the same as omitting it.

## Example Prompts

```
List all my Power Platform environments
What are the PPAC settings on my production environment?
Is IP-based SAS restriction enabled on all my environments?
Compare EnableIpBasedStorageAccessSignatureRule across all environments
Enable SAS IP restrictions on environment [ID]
What Copilot governance settings are configured for my tenant?
Does my sandbox environment draw Copilot Studio capacity from the tenant pool?
Stop the sandbox environments from drawing from the Copilot Studio tenant pool
Show me capacity allocations across every environment
What AI capacity is allocated to my production environment?
How much AI capacity is still unallocated in the tenant?
Allocate 250 AI capacity to my sandbox and deny overage
Which environments have Deny enabled on any currency?
Which environments draw from the tenant pool but have no resource threshold?
What resource thresholds are set on the MCSMessages entitlement?
Cap Copilot Studio messages at 50,000 for this environment and notify me at 80%
Stop the resource once it goes over its message limit
Show me security recommendations for my environments
Are there any cross-tenant connections I should review?
What connectors are available in my dev environment?
List all Power Apps in my production environment
Inventory every Copilot Studio agent in my tenant
Which agents were created outside the maker portal?
Show me agents with a Medium or High risk level and explain why
Which agents are missing a Microsoft Entra Agent ID?
List the agents in my default environment and who can edit them
Install the Customer Service package in my sandbox environment
```

## Architecture

```
┌─────────────────────┐     ┌──────────────────────────┐
│   Copilot Studio     │────▶│  Power Platform Admin    │
│   Agent              │◀────│  (MCP) script.csx        │
└─────────────────────┘     └──────────┬───────────────┘
                                       │
                                       ▼
                            ┌──────────────────────────┐
                            │  api.powerplatform.com    │
                            │  Power Platform Admin API │
                            │  (OAuth2 delegated auth)  │
                            └──────────────────────────┘
```

Every operation targets `api.powerplatform.com`. Earlier versions routed the tenant pool tools to a tenant-derived host (`{prefix}.{suffix}.tenant.api.powerplatform.com`) built from the `tid` claim of the access token. That host and the undocumented `licensing/allocations` route it served are no longer used — capacity work now goes through the documented Allocations By Environment API on the same host as everything else.

This connector is part of the Dataverse connector family:

| Connector | Target API | Purpose |
|-----------|-----------|---------|
| **Power Platform Admin** (this) | `api.powerplatform.com` | Cross-environment platform administration |
| Dataverse Power Agent | `org.crm.dynamics.com` | Data operations (CRUD, bulk, relationships) |
| Dataverse Power Orchestration Tools | `org.crm.dynamics.com` | Dynamic tool discovery with orchestration |
| Dataverse Custom API | `org.crm.dynamics.com` | Custom API lifecycle management |

## Application Insights (Optional)

To enable telemetry, replace the placeholder in `script.csx`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=your-key-here;IngestionEndpoint=https://...";
```

## API Reference

- [Power Platform API Reference](https://learn.microsoft.com/power-platform/admin/programmability-and-extensibility/powerplatform-api-reference)
- [Permissions Reference](https://learn.microsoft.com/power-platform/admin/programmability-permission-reference)
- [Get Allocations By Environment](https://learn.microsoft.com/rest/api/power-platform/licensing/allocations-by-environment/get-allocations-by-environment)
- [Update Allocations By Environment](https://learn.microsoft.com/rest/api/power-platform/licensing/allocations-by-environment/update-allocations-by-environment)
- [List Allocations By Environment](https://learn.microsoft.com/rest/api/power-platform/licensing/allocations-by-environment/list-allocations-by-environment)
- [Get Allocations Availability V2](https://learn.microsoft.com/rest/api/power-platform/licensing/allocation/get-allocations-availability-v2)
- [Get Many Entitlements Reserved V2](https://learn.microsoft.com/rest/api/power-platform/licensing/allocation/get-many-entitlements-reserved-v2)
- [Environment Management Settings Tutorial](https://learn.microsoft.com/power-platform/admin/programmability-tutorial-environmentmanagement-settings)
- [Tenant settings — required admin roles](https://learn.microsoft.com/power-platform/admin/tenant-settings)
- [Currency Allocation — the supported licensing allocation API](https://learn.microsoft.com/rest/api/power-platform/licensing/currency-allocation)
- [FAQ for Copilot Studio billing and licensing](https://learn.microsoft.com/microsoft-copilot-studio/faq-billing-licensing)

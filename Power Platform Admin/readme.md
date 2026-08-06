# Power Platform Admin

MCP server for cross-environment Power Platform administration through natural language in Copilot Studio. Manages environment settings, Copilot governance, security recommendations, connectors, apps, and application packages via the Power Platform Admin API.

## Overview

This connector exposes 15 admin tools across 4 categories through a single MCP endpoint, letting Copilot Studio agents perform cross-environment administration without opening the Power Platform admin center.

| Category | Tools | Purpose |
|----------|-------|---------|
| Environment Management | 5 | List environments, get details, read/update PPAC settings, compare settings across environments |
| Governance & Security | 6 | Copilot governance, Copilot Studio tenant pool draw, security recommendations, cross-tenant connection audit |
| Resource Inventory | 3 | List connectors and Power Apps per environment, inventory agents across the tenant |
| Application Lifecycle | 1 | Install Microsoft application packages |

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

   The tenant pool tools additionally require the caller to hold **Power Platform Administrator** or **Global Administrator** — the same tenant admin roles that gate licensing and capacity settings in PPAC. The `Licensing.Allocations.*` scopes are documented against the supported Currency Allocation API; the unsupported allocations route shares that namespace and resource type, so grant both and verify with a read before relying on the write.

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
| `admin_get_tenant_pool_draw` | Check whether an environment draws Copilot Studio message and session capacity from the tenant pool |
| `admin_set_tenant_pool_draw` | Enable or disable drawing Copilot Studio capacity from the tenant pool |
| `admin_get_security_recommendations` | Get security recommendations from Power Platform Advisor |
| `admin_get_cross_tenant_connections` | Cross-tenant connection reports for compliance auditing |

> **Draw from Tenant Pool is unsupported.** The two `*_tenant_pool_draw` tools call `licensing/allocations`, which is not part of the published Power Platform REST reference and may change or stop working without notice. Treat it as a temporary mitigation for bulk changes that PPAC only exposes one environment at a time.
>
> `admin_set_tenant_pool_draw` reads the current allocation document and rewrites only the `TenantPool` enforcement rule. The underlying `PUT` replaces the entire document and the service offers no ETag, so concurrent edits from PPAC or another caller can still be lost.

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
| **List Agents** | GET | `environmentId` (optional — leave blank to inventory the whole tenant) |

Every `environmentId` field renders as a picker of environment display names, populated at design time by an internal `Get Environment List` operation, so you never have to paste a GUID.

`Invoke Power Platform Admin MCP` also appears in the action list. Ignore it in a flow — it is the JSON-RPC endpoint for Copilot Studio and expects an MCP protocol body, not flow inputs.

### Notes for flow authors

- **List Agents returns one object, not a paged list.** The shape is `agentCount`, `truncated`, `environmentId`, and an `agents` array — apply-to-each over `agents`. Check `truncated` before treating the result as a complete inventory; `true` means the page ceiling was reached and agents are missing.
- **`isCLIAgent` is a string, not a boolean.** Compare against `'true'`, `'false'`, or `'unknown'`. A condition that tests it as a boolean will silently never match, and `'unknown'` must not be treated as `'false'`.
- **`riskSignals` and `channels` are string arrays.** Flatten with `join(item()?['riskSignals'], ', ')` for a table or email body.
- **Update Settings and Set Draw From Tenant Pool are writes.** The latter rewrites an entire allocation document and the service offers no ETag, so a concurrent edit from PPAC can be lost. Read first, and avoid running it on a fan-out loop.

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

The tenant pool tools target a different, tenant-routed host derived from the `tid` claim of the access token: the tenant GUID lowercased and stripped of dashes, split into the first 30 hex characters and the last 2, as `{prefix}.{suffix}.tenant.api.powerplatform.com`. No extra connection parameter is needed because the tenant is read from the token itself.

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
- [Environment Management Settings Tutorial](https://learn.microsoft.com/power-platform/admin/programmability-tutorial-environmentmanagement-settings)
- [Tenant settings — required admin roles](https://learn.microsoft.com/power-platform/admin/tenant-settings)
- [Currency Allocation — the supported licensing allocation API](https://learn.microsoft.com/rest/api/power-platform/licensing/currency-allocation)
- [FAQ for Copilot Studio billing and licensing](https://learn.microsoft.com/microsoft-copilot-studio/faq-billing-licensing)

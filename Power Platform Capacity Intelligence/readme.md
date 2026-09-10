# Power Platform Capacity Intelligence

Tenant-wide licensing, entitlement, and consumption intelligence for Power Platform. Answers how much capacity a tenant holds, how much it has consumed, which environments and resources consumed it, and how much headroom is left — through typed Power Automate actions and an MCP endpoint for Copilot Studio agents.

Built on the [licensing APIs added in July 2026](https://learn.microsoft.com/power-platform/admin/programmability-whats-new-changed). Every operation is read-only. Capacity is changed through the [Power Platform Admin](../Power%20Platform%20Admin/) connector, which owns the allocation writes.

## Overview

| Category | Tools | Purpose |
|----------|-------|---------|
| Tenant capacity | 3 | Entitlement capacity, combined posture, Finance and Operations license summary |
| Environment scope | 2 | Entitlements available to an environment, per-resource consumption inside it |
| Tenant scope | 2 | Per-resource consumption estate-wide, licence trends over time |
| Headroom and limits | 2 | Unallocated capacity, configured consumption thresholds |
| User attribution | 3 | Who consumed an entitlement, what one user consumed, who consumed one resource |

**12 MCP tools**, **12 typed REST actions**, 2 internal dropdown sources, and the MCP endpoint — **15 operations** in the OpenAPI definition.

> The connector deploys under the title **Power Platform Capacity**. The folder keeps the longer name, but `pac connector create` enforces a 30-character title limit — see [Deployment](#deployment).

## Built against the live API, not just the reference

Every contract here was verified against a live tenant before the connector was written. Several published details did not survive contact:

| Reference says | Live service does | Consequence |
|---|---|---|
| `$filter` is optional on allocation availability | Returns `400 Invalid filter options` without one | The connector requires `entitlementId` and builds the filter itself |
| Paged responses carry `@odata.nextLink` and `@odata.count` | Only `value` and a lowercase `continuationtoken` | Both spellings are accepted; the lowercase one is what actually arrives |
| `fromDate`/`toDate` are untyped strings | ISO `yyyy-MM-dd` works | Dates are normalized to ISO before sending |
| `EntitlementUnit` is `NotSpecified`, `MB`, `Count`, or `Hour` | Also returns values such as `Messages` | `unit` is passed through as a string, never validated against the enum |
| Per-resource endpoints are documented like their siblings | Return `403` with an **empty body**, even for a tenant admin whose sibling reads succeed | The connector supplies the diagnosis the service withholds |

## Prerequisites

1. **App registration** with the delegated **Power Platform API** permission `Licensing.Allocations.Read` (resource `8578e004-a5c6-46e7-913e-12f58912df43`).

   ```powershell
   az ad app permission add --id <appId> `
     --api 8578e004-a5c6-46e7-913e-12f58912df43 `
     --api-permissions 73cf5c38-5257-4f28-8bbb-f78acf3290a4=Scope   # Licensing.Allocations.Read
   az ad app permission admin-consent --id <appId>
   ```

   The permission reference does not publish these IDs. This one was read from the service directly; confirm it in your own tenant with:

   ```powershell
   az ad sp show --id 8578e004-a5c6-46e7-913e-12f58912df43 `
     --query "oauth2PermissionScopes[?starts_with(value,'Licensing')].{scope:value,id:id}" -o table
   ```

2. **Power Platform Administrator or Global Administrator.** Licensing reads are tenant-scoped.

3. Replace `[INSERT_YOUR_CLIENT_ID]` in `apiProperties.json` with the application ID.

> **Some endpoints stay denied even for a tenant administrator.** In testing, `Get Environment Resources` and `Get Tenant Resources` returned `403` with an empty body while every sibling entitlement read on the same token succeeded. That is a separate authorization gate on per-resource consumption, not a missing permission. If the other tools work and these two do not, the app registration is configured correctly.

## MCP Tools

### Tenant capacity

| Tool | Description |
|------|-------------|
| `capacity_get_entitlement` | Entitled, consumed, allocated, available, and overage status for one entitlement |
| `capacity_get_entitlement_posture` | Capacity, headroom, and thresholds in one call, with percent used |
| `capacity_get_finops_license_summary` | Finance and Operations users: total, unlicensed, under-licensed, over-licensed |

### Environment and tenant scope

| Tool | Description |
|------|-------------|
| `capacity_list_environment_entitlements` | Every entitlement available to an environment, with enforcement rules. **Start here to discover entitlement IDs** |
| `capacity_get_environment_resources` | Which resources consumed capacity inside one environment |
| `capacity_get_tenant_resources` | Which resources consumed capacity estate-wide |
| `capacity_get_license_trends` | Licence counts over a date range, by model and tier |

### Headroom and limits

| Tool | Description |
|------|-------------|
| `capacity_get_allocation_availability` | How much of an entitlement is still unallocated |
| `capacity_list_resource_thresholds` | Configured consumption caps. An empty result means uncapped |

### User attribution

| Tool | Description |
|------|-------------|
| `capacity_list_tenant_users` | Users consuming an entitlement, with how much each consumed |
| `capacity_get_user_consumption` | What one user consumed, by resource |
| `capacity_get_resource_consumers` | Which users consumed one resource |

> **The user tools return data about identifiable individuals.** Responses carry a `privacyNote`, and telemetry records only the tool name and entitlement — never user IDs or consumption figures. Handle and retain the output according to your privacy obligations, and prefer resource-level attribution over user-level when it answers the question.

## Power Automate Actions

The connector is dual-mode. Every MCP tool is also a typed REST action with full request and response schemas, so it appears as an ordinary action in the Power Automate and Logic Apps designers with IntelliSense and dropdowns — no MCP knowledge required.

| Action | MCP tool | Required inputs |
|--------|----------|-----------------|
| **Get Entitlement** | `capacity_get_entitlement` | `entitlementId` |
| **Get Entitlement Posture** | `capacity_get_entitlement_posture` | `entitlementId` |
| **Get Fin Ops License Summary** | `capacity_get_finops_license_summary` | none |
| **List Environment Entitlements** | `capacity_list_environment_entitlements` | `environmentId` |
| **Get Environment Resources** | `capacity_get_environment_resources` | `entitlementId`, `environmentId` |
| **Get Tenant Resources** | `capacity_get_tenant_resources` | `entitlementId` |
| **Get License Trends** | `capacity_get_license_trends` | `entitlementId` |
| **Get Allocation Availability** | `capacity_get_allocation_availability` | `entitlementId` |
| **List Resource Thresholds** | `capacity_list_resource_thresholds` | `entitlementId` |
| **List Tenant Users** | `capacity_list_tenant_users` | `entitlementId` |
| **Get User Consumption** | `capacity_get_user_consumption` | `entitlementId`, `userId` |
| **Get Resource Consumers** | `capacity_get_resource_consumers` | `entitlementId`, `resourceId` |

Every `environmentId` field renders as a picker of environment display names, populated at design time by an internal `Get Environment List` operation, so you never have to paste a GUID.

`Invoke Power Platform Capacity Intelligence MCP` also appears in the action list. Ignore it in a flow — it is the JSON-RPC endpoint for Copilot Studio and expects an MCP protocol body, not flow inputs.

Two further operations are marked `x-ms-visibility: internal` and back the dropdowns rather than appearing as actions: `Get Environment List` and `Get Entitlement List`.

### Notes for flow authors

- **`entitlementId` is not a dropdown on most actions.** The entitlement list is environment-scoped, and most of these actions are tenant-scoped, so there is no environment to cascade from. Run **List Environment Entitlements** once against any environment to discover the IDs, then type them.
- **Every paged action returns one page.** Read `hasMore` and loop on `continuationToken` — see [Paging](#paging). Treating the first page as the whole result will silently under-report a large tenant.
- **`resources[]` and `users[]` are already flattened.** The service nests them one level deeper as `value[].resources[]`; the connector unwraps that, so apply-to-each directly over `resources` or `users`.
- **Check `hasData` on the Fin Ops summary before charting it.** A tenant with no Finance and Operations estate returns zeros, and a chart of zeros looks like a measurement rather than an absence.
- **Check `complete` and `problems` on Get Entitlement Posture.** It reads three sources and returns what it can, so a partial result is normal rather than a failure.
- **`unit` is a display string, not an enum.** The service returns values outside the documented set, such as `Messages`. Do not switch on it.

## Reading the numbers honestly

**`consumed` is not always a running total.** Check `consumptionType`. `MonthToDate` resets at the start of each month, so a low figure early in the month is not evidence of low usage. `Snapshot` is a point-in-time reading.

**`percentUsed` is computed by this connector**, as `consumed / entitled × 100`, from figures the service reports. It is a measurement of the past, not a forecast. The connector does not extrapolate.

**An empty result is not always zero.** Several tools distinguish absence from measurement:

- `capacity_get_finops_license_summary` returns `hasData: false` when the tenant has never produced a Finance and Operations report, rather than reporting zero users.
- `capacity_list_resource_thresholds` says consumption is *uncapped* rather than returning a bare empty list.
- `capacity_get_license_trends` explains that capacity-model entitlements with no assigned licences report nothing.

**`capacity_get_entitlement_posture` degrades in parts.** It reads capacity, availability, and thresholds separately. If one is denied, the others still return and the failure is named in `problems`. Check `complete` before treating the posture as whole.

## Paging

Paged tools return **one page** and the service's `continuationToken`. The connector does not walk the whole collection internally: a tenant-wide sweep can exceed the two-minute custom-code limit, and a timeout is harder to diagnose than an explicit next-page token.

In a flow, loop while `hasMore` is true, passing `continuationToken` back each time:

```
1. Get Tenant Resources           (entitlementId: MCSMessages)
2. Do Until:  hasMore is false
   3. Append resources[] to an array variable
   4. Get Tenant Resources        (continuationToken: from the previous response)
```

`pageSize` defaults to 100 and is clamped to 1000.

## Dates

`fromDate` and `toDate` accept ISO `yyyy-MM-dd`. Omit both for **month-to-date**, which matches how the service reports capacity consumption.

An inverted or unreadable range is refused before the call, so a typo produces a clear message rather than an opaque service error.

## Usage Examples

**Scenario: monthly capacity posture report**

```
1. Trigger: Scheduled (monthly, first of the month)
2. List Environment Entitlements   (your production environment)
3. Filter array: status is not equal to 'WithinCapacity'
4. For Each remaining entitlement:
   5. Get Entitlement Posture      (entitlementId: item()?['entitlementId'])
   6. Append to an HTML table: entitlementId, percentUsed, status, consumed, entitled
7. Send the table to the platform owners
```

**Scenario: find what is consuming Copilot Studio capacity**

```
1. Get Entitlement                 (MCSMessages)  -> how much is consumed overall
2. Get Tenant Resources            (MCSMessages)  -> which agents consumed it
3. Sort by consumed descending, take the top 10
4. Get Allocation Availability     (MCSMessages)  -> how much headroom remains
```

**Scenario: check headroom before allocating**

```
1. Get Allocation Availability     (entitlementId: AI)
2. Condition: availableQuantity is greater than the amount you intend to allocate
   3. If yes: Update Environment Allocation  (Power Platform Admin connector)
   4. If no:  notify that the tenant has no unallocated AI capacity
```

## Example Prompts

```
How much Copilot Studio message capacity has the tenant used this month?
Show me the capacity posture for AI
Which agents consumed the most MCSMessages capacity in August?
How much AI capacity is still unallocated?
Which entitlements are in overage?
What entitlements does my production environment have?
Are there any consumption thresholds configured for MCSMessages?
Who consumed the most Copilot Studio capacity last month?
Is our Finance and Operations licensing over- or under-allocated?
```

## Relationship to the other connectors

| Connector | Owns |
|-----------|------|
| **Power Platform Capacity Intelligence** (this) | Reading capacity, consumption, and headroom |
| [Power Platform Admin](../Power%20Platform%20Admin/) | Changing allocations and enforcement rules, resource thresholds, environment settings |
| [Copilot Licensing](../Copilot%20Licensing/) | Copilot Studio credits via an undocumented `v0.1-alpha` API |
| [AI Builder Credits](../AI%20Builder%20Credits/) | AI Builder credit consumption from the Dataverse AI Event table |

Read here, write there. The split is deliberate: an agent that can only read cannot change a tenant's capacity, so this connector is safe to give a broader audience than Power Platform Admin.

### On replacing the Copilot Licensing connector

`Copilot Licensing` reads `licensing.powerplatform.microsoft.com/v0.1-alpha`, which is undocumented and can change without notice. This connector is the supported replacement path, and the pieces are promising: `capacity_list_tenant_users` and the resource tools return a `metadata.NonBillableQuantity` field that looks equivalent to the alpha API's non-billed credits.

**That parity is not yet proven.** It needs a side-by-side comparison over the same environment and date range, confirming that `consumed` means the same thing as the alpha API's `billedCredits`. Until that comparison is done, keep both. See the migration gate in the session findings.

## Deployment

```powershell
# Validate all three files against the official schemas
ppcv "./Power Platform Capacity Intelligence"

# Deploy. --script-file is mandatory: apiProperties.json declares scriptOperations,
# and the service rejects the deployment without a script to route them to.
pac connector create `
  --api-definition-file ./apiDefinition.swagger.json `
  --api-properties-file ./apiProperties.json `
  --script-file ./script.csx
```

`pac connector create` prints the new connector ID — save it for redeploys:

```powershell
pac connector update `
  --connector-id <CONNECTOR_ID> `
  --api-definition-file ./apiDefinition.swagger.json `
  --api-properties-file ./apiProperties.json `
  --script-file ./script.csx
```

> **The connector title is `Power Platform Capacity`, not the folder name.** `pac connector create` rejects a title longer than 30 characters, and "Power Platform Capacity Intelligence" is 35. Search for **Power Platform Capacity** in the connector list. `ppcv` does not check this limit, so a connector can validate cleanly and still fail to deploy.

### Register the generated redirect URL

**This step is required and easy to miss.** On deploy the platform rewrites `redirectMode` to `GlobalPerConnector` and replaces `redirectUrl` with a value it generates. That generated URL must be registered on the Entra app registration or OAuth consent fails.

```powershell
pac connector download --connector-id <CONNECTOR_ID> --outputDirectory ./verify
# read properties.connectionParameters.token.oAuthSettings.redirectUrl

az ad app update --id <appId> --web-redirect-uris `
    "https://global.consent.azure-apim.net/redirect" "<generated-url>"
```

The trailing segment of that URL identifies the **environment** and is shared by every connector in it; only the name portion differs. Deploying to a second environment produces a different URL, and both must be registered. If you reuse an app registration across connectors, **add** the new URL rather than replacing the existing ones.

> Never commit a real client ID or secret. `apiProperties.json` ships with `[INSERT_YOUR_CLIENT_ID]`; supply the real value through a local copy passed to `pac connector update`, or set it in the portal **Security** tab. The client ID deploys verbatim, so a connector created with the placeholder will not authenticate until you replace it.

## Troubleshooting

| Status | Meaning | What to do |
|--------|---------|------------|
| 400 | Malformed request | On availability, the connector builds the filter itself, so a 400 here usually means a bad `entitlementId`. On date-ranged actions, check `fromDate` and `toDate` are ISO `yyyy-MM-dd`. |
| 401 | Token invalid or expired | Reauthorize the connection; confirm the app registration has the Power Platform API permission |
| 403 **with a body** | Missing permission | Grant `Licensing.Allocations.Read` and consent |
| 403 **with an empty body** | Endpoint-level restriction | Seen on Get Environment Resources and Get Tenant Resources. If sibling reads succeed on the same connection, the permission is present and this endpoint is gated separately — not something you can fix by granting more |
| 404 | Entitlement or environment not found | Verify the IDs. Run List Environment Entitlements to see what exists |
| 429 | Throttled | Back off and retry. Paged sweeps over a large tenant are the usual cause — raise `pageSize` to make fewer calls rather than retrying faster |

**An empty result is not an error.** `capacity_get_license_trends` returns nothing for capacity-model entitlements, and `capacity_list_resource_thresholds` returns nothing when consumption is uncapped. Both say so in `message`.

## Application Insights (Optional)

Telemetry is off by default. To enable it, replace the placeholder in `script.csx`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=your-key-here;IngestionEndpoint=https://...";
```

Two events are emitted: `ToolCall` and `ToolCallError`, each carrying the tool name and the entitlement ID.

> **Consumption figures and user identifiers are deliberately never logged.** These tools return data about identifiable individuals, so sending their output to a telemetry endpoint would move personal data outside the boundary the caller expects. If you need per-user auditing, do it inside your own tenant against the connector's response, not through Application Insights.

Telemetry failure never fails the call — a broken instrumentation key silently degrades to no telemetry.

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI definition for the 12 typed actions, 2 internal dropdowns, and the MCP endpoint |
| `apiProperties.json` | OAuth 2.0 configuration and script operation registration |
| `script.csx` | MCP JSON-RPC 2.0 handler, response flattening, paging, date handling, and optional telemetry |
| `readme.md` | This document |

## Known Limitations

- **Two endpoints may return 403 regardless of permission.** `Get Environment Resources` and `Get Tenant Resources` were denied in testing while sibling reads on the same token succeeded. The empty response body carries no diagnosis, so the connector supplies one.
- **`unit` is not a closed set.** The reference documents four values; the service returns others, such as `Messages`. Treat it as a display string.
- **The environment entitlement list is large.** A single environment returned 35 entitlements, including internal ones such as `Internal_OmniChannelVoice`. Filter to what you care about.
- **Licence trends can be empty for capacity entitlements.** No licences are assigned to a capacity-model entitlement, so there is nothing to trend. The tool says so rather than returning a bare empty array.
- **Telemetry is deliberately sparse.** Only the tool name and entitlement ID are recorded. Consumption figures and user identifiers never leave the tenant through telemetry.

## API Reference

- [Get Entitlement](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement/get-entitlement)
- [Get Many Environment Entitlements](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement/get-many-environment-entitlements)
- [Get Environment Resources](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement-insight/get-environment-resources)
- [Get Tenant Resources Across Environments](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement-insight/get-tenant-resources-across-environments)
- [Get Tenant Users](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement-insight/get-tenant-users)
- [Get Tenant Resource Consumption By User](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement-insight/get-tenant-resource-consumption-by-user)
- [Get Tenant User Consumption By Resource](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement-insight/get-tenant-user-consumption-by-resource)
- [Get Tenant License Trends](https://learn.microsoft.com/rest/api/power-platform/licensing/entitlement-insight/get-tenant-license-trends)
- [Get All Resource Thresholds](https://learn.microsoft.com/rest/api/power-platform/licensing/resource-threshold/get-all-resource-thresholds)
- [Get Allocations Availability V2](https://learn.microsoft.com/rest/api/power-platform/licensing/allocation/get-allocations-availability-v2)
- [Get Fin Ops License Summary V2](https://learn.microsoft.com/rest/api/power-platform/licensing/fin-ops-licensing/get-fin-ops-license-summary-v2)

## History

### 1.0

Initial release. Twelve read-only operations across tenant capacity, environment and tenant consumption, headroom, thresholds, and user attribution, with a composite posture tool.

Contracts were verified against a live tenant before implementation, which changed five design decisions — see [Built against the live API](#built-against-the-live-api-not-just-the-reference).

Two things surfaced only at deploy and review time, both now documented above:

- `pac connector create` enforces a 30-character title limit that `ppcv` does not check, so the connector deploys as **Power Platform Capacity**.
- The `Licensing.Allocations.Read` permission ID in the first draft of this document was wrong. It has been replaced with the value read from the service, and the readme now shows the query to confirm it in your own tenant rather than asking you to trust a pasted GUID.

## Author

**Troy Taylor**

- Email: troy@troystaylor.com
- GitHub: [troystaylor](https://github.com/troystaylor)

# Copilot Package Management

Manage Microsoft 365 Copilot agents and apps across the Microsoft Graph admin settings APIs. This connector covers the Agent 365 Package Management API (inventory and governance), the Agent Registration API (registering agents into the Agent 365 registry), the Copilot usage reports API, and the Copilot limited mode setting.

## Publisher: Troy Taylor

## Prerequisites

- A [Microsoft Agent 365](https://www.microsoft.com/microsoft-agent-365) license (package operations only)
- The AI Administrator or Global Administrator role for package operations. Usage reports additionally accept Global Reader, Reports Reader, and Usage Summary Reports Reader.
- An Entra ID app registration with the following **delegated** Microsoft Graph permissions:
  - `CopilotPackages.ReadWrite.All` — package inventory and governance
  - `AgentRegistration.ReadWrite.All` — agent registration
  - `Reports.Read.All` — Copilot usage reports
  - `CopilotSettings-LimitedMode.ReadWrite` — limited mode setting
  - `offline_access` — refresh tokens

  The `ReadWrite` variants are requested throughout because block, unblock, update, reassign, and the limited mode setting have no read-only equivalent. If you only need inventory, `CopilotPackages.Read.All` is the least-privileged permission for **List Packages** and **Get Package Details**.

## Supported Operations

### Standard Operations (Power Automate)

**Package Management** — inventory and govern agents already in the catalog

| Operation | Description |
|-----------|-------------|
| **List Packages** | Retrieve all Copilot agents and apps in the tenant catalog. Supports `$filter` on `supportedHosts`, `elementTypes`, `platform`, and `lastModifiedDateTime`, and `$skiptoken` for paging. |
| **Get Package Details** | Get detailed metadata for a specific package including element details, categories, ownership, and access information. |
| **Update Package** | Update the allowed and acquired users/groups for a package. |
| **Block Package** | Block a package to prevent usage across the organization. |
| **Unblock Package** | Unblock a package to allow usage. |
| **Reassign Package** | Reassign package ownership to a different user. |

**Agent Registration** — put agents into the Agent 365 registry

| Operation | Description |
|-----------|-------------|
| **Create Agent Registration** | Register an agent with metadata and an agent card manifest (provider, capabilities, skills). |
| **Get Agent Registration** | Retrieve a registration including its agent card, owners, and Entra agent identity references. |
| **Update Agent Registration** | Update display name, description, owners, identity references, or agent card. |
| **Delete Agent Registration** | Remove a registration that is no longer needed. |

**Usage Reports and Settings**

| Operation | Description |
|-----------|-------------|
| **Get Copilot User Count Summary** | Aggregated active and enabled Copilot users for a period, broken down by app. |
| **Get Copilot User Count Trend** | Daily trend in active and enabled Copilot users. |
| **Get Copilot Usage by User** | Per-user Copilot activity. Report version `v2` includes **Copilot Agent Last Activity Date**. |
| **Get Limited Mode Setting** | Read whether Copilot in Teams meetings answers sentiment-related prompts. |
| **Update Limited Mode Setting** | Enable or disable limited mode for a Microsoft Entra group. |

### MCP Tools (Copilot Studio)

The connector exposes an MCP endpoint with 15 tools:

| Tool | Description |
|------|-------------|
| `list_packages` | List all packages, following `@odata.nextLink` automatically. Filters for host, element type, platform, last modified date, and publisher type. |
| `get_package_details` | Get detailed metadata for a specific package. |
| `block_package` | Block a package to prevent usage. |
| `unblock_package` | Unblock a package to allow usage. |
| `update_package_access` | Update allowed and acquired users/groups for availability and deployment control. |
| `reassign_package` | Reassign package ownership to a new user. |
| `create_agent_registration` | Register an agent in the Agent 365 registry with an agent card. |
| `get_agent_registration` | Get a specific agent registration. |
| `update_agent_registration` | Update an existing agent registration. |
| `delete_agent_registration` | Delete an agent registration. |
| `get_copilot_user_count_summary` | Aggregated Copilot user counts by app. |
| `get_copilot_user_count_trend` | Daily Copilot user count trend. |
| `get_copilot_usage_user_detail` | Per-user Copilot activity, including agent activity with `version=v2`. |
| `get_limited_mode` | Read the Teams meeting limited mode setting. |
| `update_limited_mode` | Enable or disable limited mode for a group. |

## Reading the Results

The API returns raw enum values rather than the admin center labels. Map them as follows:

| Field | API value | Meaning |
|-------|-----------|---------|
| `type` | `microsoft` | Built by Microsoft |
| `type` | `external` | Built by partners |
| `type` | `shared` | Shared in your organization |
| `type` | `custom` | Built by your organization |
| `availableTo` | `all` / `some` / `none` | Available to all users / some users or groups / no users |
| `deployedTo` | `all` / `some` / `none` | Deployed to all users / some users or groups / no users |

Both enums are evolvable and can return `unknownFutureValue`, so treat unrecognized values as unknown rather than failing.

To inventory only agents built or shared inside the tenant, keep `type` values of `custom` and `shared` — the MCP `list_packages` tool does this with its `publisherType` argument, applied client-side because `type` is not a filterable property.

**Element type casing differs between request and response.** The `$filter` values are `Bots`, `DeclarativeAgent`, `CustomEngineAgent`, and `OfficeAddIns`, but the `elementTypes` array comes back camel-cased (`bot`, `declarativeAgent`, `customEngineAgent`, `officeAddIn`). Compare case-insensitively. The same applies to `supportedHosts`, which the docs show as both `Word` and `word`.

## Obtaining Credentials

1. Go to [Entra ID App Registrations](https://entra.microsoft.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Create a new registration (or use an existing one)
3. Under **API Permissions**, add Microsoft Graph delegated permissions:
   - `CopilotPackages.ReadWrite.All`
   - `AgentRegistration.ReadWrite.All`
   - `Reports.Read.All`
   - `CopilotSettings-LimitedMode.ReadWrite`
   - `offline_access`
4. Grant admin consent for the permissions. This is required, not optional — see On-Behalf-Of below.
5. Under **Authentication**, add the redirect URI: `https://global.consent.azure-apim.net/redirect`
6. Note the **Application (client) ID** for connector configuration

## On-Behalf-Of Authentication

`apiProperties.json` sets `enableOnbehalfOfLogin` to `true`, which is the supported way to call an MCP endpoint from Copilot Studio without a second sign-in — the agent's user token is exchanged for a Microsoft Graph token at request time.

Two consequences:

- **Admin consent is mandatory.** The `aad` identity provider has no scope parameter and hardcodes `scope=openid` in the on-behalf-of exchange, so the effective permissions come entirely from the pre-consented delegated permissions on the app registration. Without admin consent the exchange succeeds but Graph calls return `403`.
- **No token is stored.** The exchange happens per request, so there is no refresh or expiry to manage.

## Getting Started

1. Replace `UPDATE_WITH_YOUR_CLIENT_ID` in `apiProperties.json` with your app registration Client ID
2. Create the connector using [PAC CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction):
   ```
   pac connector create --settings-file apiProperties.json --api-definition-file apiDefinition.swagger.json --script-file script.csx
   ```
3. Add the client secret on the connector's **Security** tab, then create a connection using your Microsoft 365 admin account

### Filter Examples

List only Copilot agents:
```
$filter=supportedHosts/any(h:h eq 'Copilot')
```

List packages with declarative agents:
```
$filter=elementTypes/any(h:h eq 'DeclarativeAgent')
```

List Office add-ins:
```
$filter=elementTypes/any(h:h eq 'OfficeAddIns')
```

List Copilot Studio agents:
```
$filter=platform eq 'Copilot Studio'
```

List recently modified packages:
```
$filter=lastModifiedDateTime gt 2026-01-01T00:00:00Z
```

### Paging

**List Packages** returns a `@odata.nextLink` when more results exist. Pass the `skiptoken` value from that link back into the `$skiptoken` parameter to fetch the next page, and repeat until no next link is returned. The MCP `list_packages` tool follows the links for you and returns the aggregated set.

## Application Insights Logging

To enable Application Insights telemetry, edit `script.csx` and set the `APP_INSIGHTS_CONNECTION_STRING` constant to your Application Insights connection string:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=your-key;IngestionEndpoint=https://dc.services.visualstudio.com/";
```

## Known Issues and Limitations

- **List Packages**, **Get Package Details**, the usage reports, and the limited mode setting use the GA `v1.0` endpoint. **Update**, **Block**, **Unblock**, **Reassign**, and all **Agent Registration** operations are preview and use `/beta`, which is subject to change and not supported in production.
- Only available in the Global service cloud (not US Government or China).
- The package read operations and agent registration support both delegated and application permissions, but the package write operations (block, unblock, update, reassign) and the limited mode setting are **delegated only** — so this connector's OAuth flow is delegated throughout.
- There is no list operation for agent registrations. You can only retrieve one by ID.
- The usage reports natively return CSV on `v1.0`. The connector requests `$format=application/json`; if the service still returns CSV, the MCP tools wrap it as `{ "contentType": "text/csv", "content": "..." }`.
- Period values differ by report version: `D30` is v1 only, `D28` is v2 only.
- Package operations require a Microsoft Agent 365 license.
- Agent **request** approval/rejection and MCP tool/server approval are not exposed in Microsoft Graph. Those remain Microsoft 365 admin center operations.

## API Documentation

- [Agent 365 Package Management API overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/overview)
- [Agent Registration API overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/agent-registration/overview)
- [copilotReportRoot resource](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/reports/resources/copilotreportroot)
- [copilotAdminLimitedMode resource](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/resources/copilotadminlimitedmode)
- [copilotPackage resource](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/resources/copilotpackage)
- [copilotPackageDetail resource](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/resources/copilotpackagedetail)

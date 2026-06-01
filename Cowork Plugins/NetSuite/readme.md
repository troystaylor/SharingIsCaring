# NetSuite for Copilot Cowork

Cowork plugin that brings Oracle NetSuite workflows into Copilot Cowork via an in-tenant MCP server.

- Setup checklist: [SETUP-CHECKLIST.md](SETUP-CHECKLIST.md)
- Cutover runbook: [CUTOVER-RUNBOOK.md](CUTOVER-RUNBOOK.md)

## Current scope

- SuiteQL queries against any NetSuite table
- Full CRUD on any NetSuite record type (customer, vendor, salesorder, invoice, etc.)
- Sublist line management on transactional records
- Record metadata discovery (record types and field schemas)
- Sales/finance scenario skills: customer briefings, open sales orders, AR aging, vendor lookup, recent transactions

## Skills

| Skill | Intent | Mode |
|---|---|---|
| [customer-briefing](skills/customer-briefing/SKILL.md) | Build a customer snapshot before account reviews | Read |
| [open-sales-orders-review](skills/open-sales-orders-review/SKILL.md) | Review open sales orders by customer or status | Read |
| [ar-aging-snapshot](skills/ar-aging-snapshot/SKILL.md) | Pull open AR invoices and surface aging | Read |
| [vendor-lookup](skills/vendor-lookup/SKILL.md) | Find vendor records and contact info | Read |
| [recent-transactions](skills/recent-transactions/SKILL.md) | List recent transactions for an entity | Read |
| [run-suiteql-query](skills/run-suiteql-query/SKILL.md) | Execute an arbitrary SuiteQL query | Read |
| [list-records](skills/list-records/SKILL.md) | List/filter records of a given type | Read |
| [get-record-details](skills/get-record-details/SKILL.md) | Retrieve a single record by id | Read |
| [get-record-metadata](skills/get-record-metadata/SKILL.md) | Discover record types and field schemas | Read |
| [create-record](skills/create-record/SKILL.md) | Create a new record of any type | Write |
| [update-record](skills/update-record/SKILL.md) | Patch fields on an existing record | Write |
| [delete-record](skills/delete-record/SKILL.md) | Permanently delete a record | Write |
| [manage-sublist-lines](skills/manage-sublist-lines/SKILL.md) | Add, update, or remove sublist lines | Write |

## MCP tools

The server exposes two endpoints:

- `/mcp/full` — all 16 tools (read + write). This is what the plugin's `mcpServerUrl` points at.
- `/mcp/federated` — 10 read-only tools, for federated/agent-to-agent scenarios where writes are not desired.

Reads (10): `run_suiteql`, `list_records`, `get_record`, `list_record_types`, `get_record_metadata`, `get_sublist`, `search_customers`, `search_vendors`, `get_open_sales_orders`, `get_open_invoices`

Writes (6): `create_record`, `update_record`, `delete_record`, `add_sublist_line`, `update_sublist_line`, `delete_sublist_line`

## Manifest schema

This plugin uses the `vDevPreview` Teams manifest schema (`manifestVersion: "devPreview"`), not `v1.28`. In Cowork's current runtime, only the devPreview path actually binds the MCP connector — v1.28 manifests load skills but silently drop the connector, so the agent never invokes any tools. devPreview also requires `packageName` (reverse-DNS form) and omits `mcpToolDescription`; Cowork discovers tools dynamically via MCP `tools/list`.

## Deploy

1. Provision the Azure infra and MCP server:
   ```powershell
   cd "Cowork Plugins/NetSuite"
   azd up
   ```
   You will be prompted for:
   - `NETSUITE_ACCOUNT_ID` — your NetSuite account id (e.g. `1234567` or `TSTDRV1234567_SB1`)
2. **Bind the container app to ACR with its system identity** (one-time, after first `azd up`):
   ```powershell
   az containerapp registry set `
     -g <resource-group> -n <container-app-name> `
     --server <acr-name>.azurecr.io --identity system
   ```
   The Bicep ships with `registries: []` to avoid a first-deploy chicken-and-egg (the AcrPull role isn't assigned yet when the container app first tries to bind). Run `azd deploy` again after binding — subsequent deploys work without this step.
3. Register an OAuth client in the Teams Developer Portal pointing at your NetSuite Integration Record:
   - Authorization endpoint: `https://<account>.app.netsuite.com/app/login/oauth2/authorize.nl`
   - Token endpoint: `https://<account>.suitetalk.api.netsuite.com/services/rest/auth/oauth2/v1/token`
   - Scope: `rest_webservices`
   Capture the OAuth registration `referenceId`.
4. In [manifest.json](manifest.json), replace:
   - `id` (`{{GUID}}`) with a fresh GUID
   - `agentConnectors[0].toolSource.remoteMcpServer.mcpServerUrl` (`<YOUR-CONTAINER-APP-FQDN>`) with the deployed Container App FQDN
   - `agentConnectors[0].toolSource.remoteMcpServer.authorization.referenceId` (`{{OAUTH_REFERENCE_ID}}`) with the OAuth registration ID
5. Validate and package:
   ```powershell
   ./preflight.ps1
   ./package.ps1 -SkipIcons
   ```
6. Smoke-test the deployed MCP endpoint:
   ```powershell
   ./smoke.ps1 -Fqdn <container-app-fqdn>
   ```
   Expect `200 Healthy` on `/health/live` and `/health/ready`, plus a `tools/list` response listing all 16 tools.
7. Upload `NetSuite.zip` in the M365 Admin Center, publish to test users, then connect in a fresh Cowork session.

For a step-by-step value-mapping checklist see [SETUP-CHECKLIST.md](SETUP-CHECKLIST.md); for re-cutover after redeploys see [CUTOVER-RUNBOOK.md](CUTOVER-RUNBOOK.md).

## NetSuite prerequisites

1. **NetSuite Account** with REST Web Services enabled (Setup > Company > Enable Features > SuiteTalk)
2. **OAuth 2.0 Integration Record** in NetSuite:
   - Setup > Integration > Manage Integrations > New
   - Enable **OAuth 2.0**
   - Redirect URI: the Teams Developer Portal OAuth callback (`https://teams.microsoft.com/api/platform/v1.0/oauthRedirect`)
   - Scope: `rest_webservices`
   - Capture the **Client ID** and **Client Secret** (shown once)
3. **Account ID**: Setup > Company > Company Information. Replace any hyphen with an underscore (e.g. sandbox `TSTDRV1234567_SB1`).

## Pre-upload checks

```powershell
cd "Cowork Plugins/NetSuite"
./preflight.ps1
./package.ps1 -SkipIcons
```

## Helper scripts

| Script | Purpose |
|---|---|
| [preflight.ps1](preflight.ps1) | Validate manifest, skills, icons, and connector wiring before packaging |
| [package.ps1](package.ps1) | Build `NetSuite.zip` from manifest + skills + icons |
| [generate-icons.ps1](generate-icons.ps1) | Download source PNG and produce 192×192 `color.png` + 32×32 `outline.png` |
| [fix-outline.ps1](fix-outline.ps1) | Rebuild `outline.png` as a pure-white silhouette (required by M365 Admin Center) |
| [smoke.ps1](smoke.ps1) | Post-deploy smoke test: hits `/health/live`, `/health/ready`, `/status`, and runs MCP `initialize` + `tools/list` against `/mcp/full` |

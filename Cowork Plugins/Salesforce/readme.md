# Salesforce for Copilot Cowork

Cowork plugin that brings Salesforce account and opportunity workflows into Copilot Cowork via an in-tenant MCP server.

- Setup checklist: [SETUP-CHECKLIST.md](SETUP-CHECKLIST.md)
- Cutover runbook: [CUTOVER-RUNBOOK.md](CUTOVER-RUNBOOK.md)

## Current scope

- Account briefings and full CRUD on accounts
- Opportunity health, risk summaries, and full CRUD on opportunities
- Next-best-action recommendations
- Contact discovery and full CRUD on contacts
- Task review and call-note logging

## Skills

| Skill | Intent | Mode |
|---|---|---|
| [account-briefing](skills/account-briefing/SKILL.md) | Build an account snapshot before customer meetings | Read |
| [opportunity-health-summary](skills/opportunity-health-summary/SKILL.md) | Assess pipeline quality and stage health | Read |
| [next-best-action](skills/next-best-action/SKILL.md) | Recommend concrete next steps per deal | Read |
| [open-risks-and-blockers](skills/open-risks-and-blockers/SKILL.md) | Surface stalled deals and blockers | Read |
| [find-contacts](skills/find-contacts/SKILL.md) | Search for contacts by name, account, or email | Read |
| [review-tasks](skills/review-tasks/SKILL.md) | List and review open tasks across deals | Read |
| [create-account](skills/create-account/SKILL.md) | Create a new account record | Write |
| [update-account](skills/update-account/SKILL.md) | Edit account fields | Write |
| [create-opportunity](skills/create-opportunity/SKILL.md) | Create a new opportunity tied to an account | Write |
| [update-opportunity](skills/update-opportunity/SKILL.md) | Change stage, amount, close date, owner, and fields | Write |
| [add-contact](skills/add-contact/SKILL.md) | Create a new contact under an account | Write |
| [update-contact](skills/update-contact/SKILL.md) | Edit contact fields | Write |
| [log-call-notes](skills/log-call-notes/SKILL.md) | Add structured call outcomes and follow-ups | Write |

## MCP tools

The server exposes two endpoints:

- `/mcp/full` — all 16 tools (read + write). This is what the plugin's `mcpServerUrl` points at.
- `/mcp/federated` — 9 read-only tools, for federated/agent-to-agent scenarios where writes are not desired.

Reads (9): `search_accounts`, `get_account`, `search_opportunities`, `get_opportunity`, `search_contacts`, `get_contact`, `list_tasks`, `get_task`, `list_recent_activities`

Writes (7): `create_account`, `update_account`, `create_opportunity`, `update_opportunity`, `create_contact`, `update_contact`, `create_task`

## Manifest schema

This plugin uses the `vDevPreview` Teams manifest schema (`manifestVersion: "devPreview"`), not `v1.28`. In Cowork's current runtime, only the devPreview path actually binds the MCP connector — v1.28 manifests load skills but silently drop the connector, so the agent never invokes any tools. devPreview also requires `packageName` (reverse-DNS form) and omits `mcpToolDescription`; Cowork discovers tools dynamically via MCP `tools/list`.

## Deploy

1. Provision the Azure infra and MCP server:
   ```powershell
   cd "Cowork Plugins/Salesforce"
   azd up
   ```
2. Register an OAuth client in the Teams Developer Portal pointing at your Salesforce connected app, capture the OAuth registration `referenceId`.
3. In [manifest.json](manifest.json), replace:
   - `id` (`{{GUID}}`) with a fresh GUID
   - `agentConnectors[0].toolSource.remoteMcpServer.mcpServerUrl` (`<YOUR-CONTAINER-APP-FQDN>`) with the deployed Container App FQDN
   - `agentConnectors[0].toolSource.remoteMcpServer.authorization.referenceId` (`{{OAUTH_REFERENCE_ID}}`) with the OAuth registration ID
4. Validate and package:
   ```powershell
   ./preflight.ps1
   ./package.ps1 -SkipIcons
   ```
5. Upload `Salesforce.zip` in the M365 Admin Center, publish to test users, then connect in a fresh Cowork session.

For a step-by-step value-mapping checklist see [SETUP-CHECKLIST.md](SETUP-CHECKLIST.md); for re-cutover after redeploys see [CUTOVER-RUNBOOK.md](CUTOVER-RUNBOOK.md).

## Pre-upload checks

```powershell
cd "Cowork Plugins/Salesforce"
./preflight.ps1
./package.ps1 -SkipIcons
```

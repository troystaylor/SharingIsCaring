# Planner for Copilot Cowork

Cowork plugin for Microsoft Planner using Microsoft Graph and the same in-tenant MCP architecture used by other plugins in this repo.

## Current scope

- Plan health summaries and risk detection
- Individual workload snapshots
- Personal/private task lists from Microsoft To Do and personal Planner plans
- Task triage recommendations
- Controlled task updates with user confirmation
- Team capacity monitoring

## Skills

| Skill | Intent | Mode |
|---|---|---|
| [plan-health-summary](skills/plan-health-summary/SKILL.md) | Summarize plan status, deadlines, and execution risk | Read |
| [my-workload-snapshot](skills/my-workload-snapshot/SKILL.md) | Show assigned tasks, due windows, and blockers | Read |
| [personal-task-list](skills/personal-task-list/SKILL.md) | List tasks from personal/private Planner plans | Read |
| [personal-weekly-delta](skills/personal-weekly-delta/SKILL.md) | Summarize weekly movement across personal/private tasks | Read |
| [task-triage](skills/task-triage/SKILL.md) | Prioritize tasks by urgency, impact, and dependency | Read |
| [update-task](skills/update-task/SKILL.md) | Apply controlled updates to Planner tasks and details | Write |
| [team-capacity-watch](skills/team-capacity-watch/SKILL.md) | Detect overloaded teammates and rebalance opportunities | Read |
| [plan-activity-recap](skills/plan-activity-recap/SKILL.md) | Recap recent plan changes and execution trends | Read |

## MCP tools

- `list_group_plans`
- `list_plan_tasks`
- `list_plan_buckets`
- `list_my_tasks`
- `list_my_private_tasks`
- `list_my_personal_tasks`
- `my_personal_tasks_weekly_delta`
- `list_user_tasks`
- `get_task`
- `get_task_details`
- `create_task`
- `update_task`
- `update_task_details`
- `plan_health_summary`

## Microsoft Graph Planner endpoints (v1.0)

- `GET /groups/{group-id}/planner/plans`
- `GET /planner/plans/{plan-id}/tasks`
- `GET /planner/plans/{plan-id}/buckets`
- `GET /me/planner/plans`
- `GET /me/planner/tasks`
- `GET /me/todo/lists`
- `GET /me/todo/lists/{todoTaskListId}/tasks`
- `GET /users/{id}/planner/tasks`
- `GET /planner/tasks/{id}`
- `GET /planner/tasks/{id}/details`
- `POST /planner/tasks`
- `PATCH /planner/tasks/{id}` (requires `If-Match`)
- `PATCH /planner/tasks/{id}/details` (requires `If-Match`)

## Manifest schema

This plugin uses the `devPreview` Teams manifest schema. The v1.28 schema's `agentConnectors.remoteMcpServer` path requires a static `mcpToolDescription.file`, and in practice Cowork loads the skills but does not bind the MCP connector for runtime invocation. `devPreview` omits `mcpToolDescription` and Cowork discovers tools dynamically via MCP `tools/list`, which is the working path.

## Deploy to Azure

```powershell
cd "Cowork Plugins/Planner"

# set once per environment
azd env set AZURE_LOCATION westus2
azd env set DEPLOYER_PRINCIPAL_ID <your-entra-object-id>

# validate template then deploy
azd provision --preview
azd up
```

After deploy, copy the `MCP_FULL_URL` output and update `manifest.json` `mcpServerUrl`. Register the OAuth Plugin Vault entry in Cowork to obtain the `referenceId` and set it in `manifest.json` `authorization.referenceId`.

## Package during development

```powershell
cd "Cowork Plugins/Planner"
./package.ps1 -SkipIcons
```

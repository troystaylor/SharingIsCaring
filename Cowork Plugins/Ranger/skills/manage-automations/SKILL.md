---
name: manage-automations
description: |
  Create, list, pause, resume, and delete scheduled automations that run tool sequences
  on a cron schedule. Use when the user wants recurring tasks, background monitoring,
  or scheduled reports.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: create-and-update
---

# Manage Automations

## When to Activate

- "Every [schedule], do [task]"
- "Check [URL] daily and tell me if it changes"
- "Run this report every Monday"
- "List my automations", "pause [automation]", "delete [automation]"
- "What did my automations find?"

## Workflow

### Create
1. Parse schedule into cron: "every Monday at 9am" → `0 9 * * 1`
2. Define action sequence (which tools to call and with what args)
3. `create_automation(name, schedule, actions: [{tool, args}])`
4. Confirm: "Created automation '[name]' running [schedule]. Next run: [time]."

### List
1. `list_automations(user_id)`
2. Present as table with name, schedule, enabled status, last run

### Review Results
1. `get_automation_history(automation_id)`
2. Present last run results and any pending items

### Pause/Resume/Delete
1. `pause_automation(automation_id)` / `resume_automation(automation_id)` / `delete_automation(automation_id)`

## Common Cron Patterns

| Schedule | Cron |
|----------|------|
| Every Monday 9am | `0 9 * * 1` |
| Every weekday 8am | `0 8 * * 1-5` |
| Every hour | `0 * * * *` |
| Daily at midnight | `0 0 * * *` |
| Every 15 minutes | `*/15 * * * *` |

## Automation Results + M365

When an automation produces results and the user wants to act on them:
1. Show the results from `get_automation_history`
2. Ask "Want me to email this?" or "Save to OneDrive?"
3. Use cross-m365-workflow pattern (requires Work IQ)

## Missing Tools

Automations that only use browser/code/memory work without any Work IQ.
For automations that need M365 actions, use the deferred execution pattern:
- Automation runs browser/code portion on schedule
- Results saved to memory
- Next time user opens Cowork: "Your automation '[name]' found results. Want me to act on them?"

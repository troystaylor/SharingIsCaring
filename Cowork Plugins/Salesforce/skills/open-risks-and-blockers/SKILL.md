---
name: open-risks-and-blockers
description: |
  Finds at-risk and blocked Salesforce opportunities across the pipeline.
  Use when the user asks "show risky Salesforce deals", "what Salesforce
  deals are blocked", "which Salesforce opportunities are stalled", "what
  should I escalate in Salesforce this week", or "find open risks on the
  Salesforce pipeline".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
cowork.category: Sales
cowork.icon: Warning
---

# Open Risks and Blockers

1. Pull open opportunities with `search_opportunities`.
2. Use `get_opportunity` for detailed risk signals.
3. For each candidate, pull open tasks with `list_tasks` (`whatId`, `openOnly=true`). Zero open tasks plus a stale stage is a strong stall signal.
4. Flag stale stages, overdue next steps, close-date slippage, and missing follow-up activity.
5. Rank blockers by urgency and revenue impact.
6. Propose escalation or recovery actions for the top risks and offer to create the matching follow-up task via `create_task`.

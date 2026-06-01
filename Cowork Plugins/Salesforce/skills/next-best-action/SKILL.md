---
name: next-best-action
description: |
  Recommends next best actions for Salesforce opportunities by analyzing
  pipeline data. Use when the user asks "what should I do next on this
  Salesforce deal", "what action unblocks this Salesforce opportunity",
  "suggest next steps for [opportunity] in Salesforce", or "how do I move
  this Salesforce deal to the next stage".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: analysis
cowork.category: Sales
cowork.icon: Lightbulb
---

# Next Best Action

1. Pull opportunity details with `get_opportunity`.
2. Review recent deal activity using `list_recent_activities`.
3. List existing open tasks with `list_tasks` (`whatId`, `openOnly=true`) so recommendations do not duplicate work already in flight.
4. List key contacts at the account with `search_contacts` to identify stakeholder engagement gaps.
5. Identify stage gaps, missing stakeholders, and stale tasks.
6. Recommend the top three next actions with owner, timing, and which stakeholder to engage.
7. Ask whether to create follow-up tasks via `create_task` and whether to update the opportunity next step via `update_opportunity`.

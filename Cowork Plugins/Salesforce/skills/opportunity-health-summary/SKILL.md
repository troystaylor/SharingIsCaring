---
name: opportunity-health-summary
description: |
  Reviews opportunity health in Salesforce using stage, amount, close date,
  and activity signals. Use when the user asks "how healthy is this
  Salesforce deal", "summarize this Salesforce opportunity", "what is at
  risk on [opportunity] in Salesforce", "give me Salesforce pipeline
  quality for this account", or "how is [opportunity] tracking in
  Salesforce".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
cowork.category: Sales
cowork.icon: Pulse
---

# Opportunity Health Summary

1. Find target opportunities with `search_opportunities`.
2. Retrieve full detail for each item with `get_opportunity`.
3. Pull open follow-ups for each opportunity with `list_tasks` using `whatId` and `openOnly=true` to gauge activity health.
4. Check stakeholder coverage with `search_contacts` filtered to the related Account Id.
5. Evaluate stage age, close-date confidence, open task count, contact coverage, and blockers.
6. Return a concise health score per opportunity with rationale and the single biggest gap.
7. Highlight deals needing immediate owner action.

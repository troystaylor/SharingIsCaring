---
name: update-opportunity
description: |
  Updates key Salesforce opportunity fields including stage, amount, close
  date, next step, probability, and owner. Use when the user asks "update
  this Salesforce opportunity", "change stage in Salesforce", "adjust
  amount on [opportunity] in Salesforce", "move close date in Salesforce",
  or "reassign this Salesforce deal".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Sales
cowork.icon: Edit
---

# Update Opportunity

1. Confirm the target opportunity using `search_opportunities` if needed.
2. Summarize current values and proposed field changes.
3. Ask for explicit user confirmation before writing.
4. Apply changes via `update_opportunity`.
5. If `nextStep` or `stageName` changed, offer to create a matching follow-up task via `create_task` linked to the opportunity.
6. Return a before/after summary and list any follow-up tasks created.

---
name: review-tasks
description: |
  Lists and reviews Salesforce tasks for an account, opportunity, or owner,
  and pulls full detail when needed. Use when the user asks "show my
  Salesforce tasks", "list open Salesforce tasks for [account]", "what
  Salesforce tasks are on [opportunity]", "review my Salesforce
  follow-ups", or "get details on this Salesforce task".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
cowork.category: Sales
cowork.icon: TaskListSquareLtr
---

# Review Tasks

1. If a specific account or opportunity is mentioned, resolve it with `search_accounts` or `search_opportunities` to get its Id.
2. Use `list_tasks` with the relevant filter (`whatId`, `whoId`, `ownerId`, `status`, or `openOnly=true`) to retrieve candidate tasks.
3. For any task the user wants more detail on, pull it with `get_task`.
4. For tasks tied to an opportunity, pull the opportunity context with `get_opportunity` so each recommendation knows the deal stage and amount.
5. Summarize tasks by status and due date, highlighting overdue and high-priority items.
6. Suggest concrete next actions for each open task (complete, defer, reassign, or log a call via `create_task`).
7. If a task indicates a change in deal posture (new next step, slipping close date), offer to update the opportunity via `update_opportunity`.

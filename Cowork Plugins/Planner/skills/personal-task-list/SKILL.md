---
name: personal-task-list
description: |
  Lists the caller's personal/private Planner tasks.
  Use when the user asks "show my personal planner tasks", "list my private planner tasks",
  "retrieve my private planner tasks", or "show tasks from my personal plans".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: discovery
---

# Personal Task List

1. Call `list_my_private_tasks` first to retrieve Exchange-backed private tasks from Microsoft To Do.
2. If no tasks are returned, call `list_my_personal_tasks` for Planner plan-backed personal/private candidates.
3. If still empty, clarify that some personal/premium task types might not be exposed via current Graph Planner endpoints and offer `list_my_tasks` as a secondary view.
4. Present tasks grouped by source list or plan title with due date and completion state.
5. End with optional follow-up: "Do you want weekly movement as well?" and, if yes, call `my_personal_tasks_weekly_delta`.

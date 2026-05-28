---
name: my-workload-snapshot
description: |
  Produces a workload snapshot of the caller's Planner tasks. Use when the user asks
  "what do I need to do today", "show my planner workload", or
  "what tasks are due this week".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: discovery
---

# My Workload Snapshot

1. Pull assigned tasks via `list_my_tasks` and pull personal/private plan tasks via `list_my_personal_tasks`.
2. Group tasks by due window, priority, and completion state.
3. Highlight overdue items, due-soon tasks, and dependency risk.
4. Recommend a prioritized next-action list for the current work window.

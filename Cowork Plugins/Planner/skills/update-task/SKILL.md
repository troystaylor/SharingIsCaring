---
name: update-task
description: |
  Applies controlled updates to Planner tasks. Use when the user asks
  "update this task", "mark complete", "change due date", or
  "edit task notes".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: action
---

# Update Task

1. Resolve the task via `get_task` and `get_task_details`.
2. Read back current values and proposed change set.
3. Ask for explicit confirmation before writing.
4. Execute `update_task` and/or `update_task_details` with the latest `etag`.
5. Return a before/after summary.

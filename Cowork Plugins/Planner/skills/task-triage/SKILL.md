---
name: task-triage
description: |
  Prioritizes Planner tasks for execution triage. Use when the user asks
  "triage these tasks", "what should we do first", or
  "prioritize work for this plan".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: analysis
---

# Task Triage

1. Pull target plan tasks with `list_plan_tasks`.
2. Pull bucket context with `list_plan_buckets`.
3. Score urgency by due date, age, and completion progress.
4. Return a ranked queue with rationale and recommended owners.

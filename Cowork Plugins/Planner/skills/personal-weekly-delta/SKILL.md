---
name: personal-weekly-delta
description: |
  Summarizes what changed in the caller's personal/private Planner tasks over a recent window.
  Use when the user asks "what changed in my personal tasks this week", "give me my weekly task delta",
  or "what moved in my private planner plans".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: reporting
---

# Personal Weekly Delta

1. Call `my_personal_tasks_weekly_delta` with `daysBack` defaulting to 7 unless the user specifies a different range.
2. Summarize key movement: changed tasks, newly created tasks, completed tasks, overdue open tasks, and due-soon workload.
3. Present top impacted plans using the `byPlan` section.
4. Include notable changed tasks from `changedTaskSample` and end with a concise next-step recommendation.

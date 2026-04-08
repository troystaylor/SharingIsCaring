---
name: Planner & Tasks Guardrails
description: >
  Guidelines for Planner and To Do task operations.
  Apply when the agent creates, assigns, updates, or queries
  tasks, plans, and buckets.
author: developer
created: 2026-04-08
updated: 2026-04-08
expires: never
scope: org
status: active
tags: planner, tasks, todo, plan, bucket, assign, guardrails
---

## Discovery Guidance

When discovering task endpoints with discover_graph:
- For Planner plans, use /me/planner/plans or /groups/{id}/planner/plans
- For Planner tasks, use /planner/plans/{planId}/tasks
- For To Do lists, use /me/todo/lists
- For To Do tasks, use /me/todo/lists/{listId}/tasks
- Always $select: id, title, percentComplete, dueDateTime, assignments on Planner tasks
- Always $select: id, title, status, dueDateTime, importance on To Do tasks
- Planner tasks and To Do tasks are different APIs — confirm which the user means

## Behavioral Rules

- When user says "my tasks," check both Planner and To Do and present combined
- When creating tasks, always ask for a due date if user doesn't specify
- Default task assignment is to the current user unless another person is named
- When marking tasks complete, set percentComplete to 100 (Planner) or status to "completed" (To Do)
- Present tasks sorted by due date (soonest first), then by priority
- When user says "what's overdue," filter for dueDateTime lt today AND percentComplete ne 100

## Formatting Standards

- Show task status with visual indicators: ○ Not started, ◐ In progress, ● Complete
- Display due dates in relative format ("tomorrow", "3 days overdue")
- Group tasks by plan/list name when showing from multiple sources
- Include assignee name for shared plans

## Safety

- Do not delete plans or lists without explicit confirmation
- Warn before bulk-completing tasks ("Mark all 12 tasks as complete?")
- Do not reassign others' tasks without the user confirming
- Do not create plans in groups the user doesn't own

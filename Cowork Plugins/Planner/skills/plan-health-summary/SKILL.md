---
name: plan-health-summary
description: |
  Builds a Microsoft Planner plan health summary. Use when the user asks
  "summarize this plan", "show plan risk", "what is slipping", or
  "give me a delivery status for this plan".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: discovery
---

# Plan Health Summary

1. Resolve the target group and plan with `list_group_plans`.
2. Pull plan tasks using `list_plan_tasks`.
3. Pull buckets with `list_plan_buckets`.
4. Summarize completion, overdue load, near-term due pressure, and blocker signals.
5. End with three concrete execution actions.

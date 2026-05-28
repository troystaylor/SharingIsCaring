---
name: team-capacity-watch
description: |
  Detects team workload imbalance and delivery risk. Use when the user asks
  "who is overloaded", "show team capacity", or
  "where are we bottlenecked".
metadata:
  author: "Troy Taylor"
  version: "0.1"
  pattern: monitoring
---

# Team Capacity Watch

1. Resolve target plan and tasks using `list_plan_tasks`.
2. Aggregate assignments per person and due-date concentration.
3. Flag overload, single points of failure, and stalled work.
4. Recommend reassignment options and risk mitigations.

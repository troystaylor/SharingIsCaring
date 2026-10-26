---
name: consumption-review
description: Report Copilot Studio credit consumption by agent or workflow over time and compare it with the environment's current credit capacity. Use when the user asks what agents or workflows cost, which ones use the most credits, whether consumption is trending up, or how much capacity is left.
---

# Consumption Review

## When to use this skill

- "How many credits did my agents use?", "what's my most expensive workflow", "are we close to our capacity", "why did consumption spike".

## How to do it

1. Apply `copilot-studio-access`. Pick the environment first, and confirm production before building in it. Use `describe_actions` on `get_consumption` to confirm its actions, filters and time ranges.
2. Confirm the scope: agents, workflows or both, and a time window. If unstated, use the last 30 days and say so.
3. Read historical consumption and, if the question is about headroom, current environment capacity. These are reads and need no confirmation.
4. Present a short table: item, credits used, share of total, and change from the previous comparable period when the data supports it. Give the top items first.
5. Report only figures the tool returned. If the data has gaps, a lag or a limit on history, say so. Never estimate or extrapolate cost, and do not convert credits to currency unless the user supplies a rate.
6. For a spike, name the item and the dates it began. Suggest checking that item's recent changes or run diagnostics (`workflow-authoring-and-diagnostics`), and label any cause as a hypothesis.

## What to watch for

- Credits shown are for the one environment you chose. Say so if the user may be thinking of the whole tenant, and offer to repeat the review for another environment.
- Consumption is a read-only topic. Do not change agents or workflows to reduce cost without a separate request that goes through `write-safety`.

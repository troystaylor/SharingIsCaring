---
name: pre-mortem
description: |
  Runs a pre-mortem: imagine the project/plan has failed 6–12 months from
  now, then work backward to find what caused the failure. Use when the user
  says "let's pre-mortem this", "imagine this failed", "what's the worst case
  6 months out", "before we commit, let's stress test", or wants to surface
  failure modes before launch. Calls the Decision Duck `pre_mortem` MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.1"
  pattern: tool-call
---

# Pre-Mortem

## What This Skill Does

Flips a forward-looking plan into a backward-looking failure analysis:
"It's 6 months from now and this plan failed. Why?" The framing surfaces
risks that a normal risk register tends to miss — political, narrative,
team-dynamic, slow-burn risks — because the question shifts from "what
might go wrong" to "what already did go wrong".

Implemented by calling the dedicated `pre_mortem` tool, which has the
pre-mortem framing and system prompt built in.

## When to Activate

- "Let's pre-mortem this" / "run a pre-mortem"
- "Imagine this failed in 6 months — why?"
- "What's the worst case 6 months / 12 months out?"
- "Before we commit, stress test the plan"
- User is about to launch something and wants a final failure check

## When NOT to Activate

- User wants a forward-looking risk register → use `risk-analysis`
- User wants to imagine adversaries attacking the plan → use `red-team`

## Workflow

1. **Capture plan and horizon.** Confirm:
   - **Plan:** the thing being committed to, one paragraph
   - **Horizon:** when "failure" is measured (default: 6 months)
   - **What "failure" means:** explicit success criteria they'd fail
     to hit

2. **Call the tool.**

   ```
   pre_mortem(
     plan: "<the plan being committed to>",
     horizon: "<time horizon, e.g. '6 months' or '1 year'>",
     failure_criteria: "<what failure looks like>"
   )
   ```

3. **Present the output.** The tool returns failure modes, root causes,
   missed warning signs, and preventive actions in bullet-point format.
   Relabel for the user:
   - Lead items as "Failure modes" not "Risks"
   - Highlight the single most likely root cause
   - End with the one highest-leverage change to make this week

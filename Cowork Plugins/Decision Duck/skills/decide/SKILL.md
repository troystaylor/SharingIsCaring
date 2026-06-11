---
name: decide
description: |
  End-to-end decision support orchestrator. Compares options, runs risk
  analysis on the recommended path, runs a bias check on the user's stated
  reasoning, and produces a single decision brief. Use when the user says
  "help me decide", "I need to make a call on this", "walk me through this
  decision", "I'm stuck between these", or shares a real decision they're
  about to commit to. Calls `comparative_analysis`, `analyze_risk`, and
  `identify_cognitive_biases` in sequence.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: orchestrator
---

# Decide

## What This Skill Does

Runs the full Decision Duck pipeline for a real decision: compare options →
risk-check the recommended path → bias-check the user's reasoning →
assemble a one-page decision brief. Use this when the user is about to
commit, not when they're brainstorming.

## When to Activate

- "Help me decide between …"
- "I need to make a call on …"
- "Walk me through this decision"
- "I'm leaning toward X — what should I check before committing"
- User shares 2+ options AND mentions an upcoming deadline or decision point

## When NOT to Activate

- Just one tool's worth of value is needed → use the focused skill
- User wants to imagine the project failing → use `pre-mortem`
- User wants an adversarial critique → use `red-team`

## Workflow

1. **Gather the four inputs.** Decision, options, criteria, current lean and why.

2. **Step 1 — Compare.**

   ```
   comparative_analysis(
     context: "<decision in one paragraph>",
     options: [...],
     criteria: [...]
   )
   ```

3. **Step 2 — Risk the recommended path.**

   ```
   analyze_risk(
     scenario: "Committing to <recommended option> for <decision>",
     context: "<one-paragraph decision background>"
   )
   ```

4. **Step 3 — Bias-check the user's reasoning.**

   ```
   identify_cognitive_biases(
     analysis: "<the user's current lean and why, in their own words>"
   )
   ```

5. **Step 4 — Assemble the decision brief.** One page. Lead with the
   recommendation; the user should be able to skim the top half and decide.

6. **Close with one decision-quality check.** Pin the question that
   sharpens the call.

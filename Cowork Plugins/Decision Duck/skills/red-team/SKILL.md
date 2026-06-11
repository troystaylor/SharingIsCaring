---
name: red-team
description: |
  Adversarial review of a plan, pitch, proposal, or memo. Steelmen the
  strongest case against, identify weak assumptions and cognitive biases.
  Use when the user says "red team this", "tear this apart", "be the
  skeptic", "what's the strongest argument against", "what would a critic
  say", or wants adversarial critique of reasoning. Calls the Decision Duck
  `red_team` MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.1"
  pattern: tool-call
---

# Red Team

## What This Skill Does

Stands up as the strongest, most charitable opponent of the user's plan
or argument. The `red_team` tool attacks assumptions, identifies weak
points, surfaces cognitive biases, and ends with the single weakest
assumption to defend first.

## When to Activate

- "Red team this" / "tear this apart"
- "Be the skeptic" / "play devil's advocate" (when scoped to a written
  plan or pitch, not a casual question)
- "What's the strongest argument against this"
- "What would a critic / reviewer / our board say"
- User shares a memo, pitch, or proposal and asks for adversarial review

## When NOT to Activate

- User wants a balanced second opinion, not adversarial → use `second-opinion`
- User wants failure modes after launch → use `pre-mortem`
- User wants a decision package → use `decide`

## Workflow

1. **Capture the thing being red-teamed.** Confirm:
   - **What:** plan / pitch / memo / proposal in one paragraph
   - **Stakes:** who decides and what happens if they say no
   - **Optional:** who the imagined critic is

2. **Call the tool.**

   ```
   red_team(
     plan: "<the plan / pitch / proposal in one paragraph>",
     stakes: "<who decides and what happens if they say no>",
     critic_perspective: "<imagined critic's lens>"
   )
   ```

3. **Present the output.** Format as a red-team report: strongest attack,
   attacks worth taking seriously, biases identified, weakest assumption
   to defend first.

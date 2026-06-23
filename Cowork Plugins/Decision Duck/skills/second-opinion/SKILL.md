---
name: second-opinion
description: |
  Gives a structured second opinion on a decision, plan, or analysis the user
  is weighing. Use when the user asks to "get a second opinion", "stress test
  this", "challenge my thinking", "play devil's advocate", "is this a good
  idea", or shares a draft conclusion and wants a critical read before
  committing. Calls the Decision Duck `get_second_opinion` MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: analysis
---

# Second Opinion

## What This Skill Does

Returns a structured alternative perspective on the user's decision or analysis:
assumptions worth checking, risks they might be skipping, tradeoffs they're
implicitly making, and recommended next steps. Wraps the `get_second_opinion`
tool on the Decision Duck MCP server.

## When to Activate

- User asks for a "second opinion", "another perspective", or "outside view"
- User says "stress test this", "challenge me", "play devil's advocate", "be skeptical"
- User shares a decision they've already made and asks if it's right
- User shares draft reasoning and wants a sanity check before sending it on

## When NOT to Activate

- User wants to compare specific options → use `compare-options`
- User wants risks only → use `risk-analysis`
- User wants to spot biases in their reasoning → use `bias-check`
- User wants a full decision package → use `decide`

## Workflow

1. **Capture the decision in one paragraph.** If the user dropped a long doc,
   restate it back in 3–5 sentences and confirm: "Here's what I'm reviewing —
   is that the version you want a second opinion on?"

2. **Ask for depth and focus, but don't block on it.** Default to `balanced`.
   Offer:
   - `quick` — one-screen response
   - `balanced` — default
   - `deep` — full breakdown with multiple framings
   Optional: a `focus_area` ("financial impact", "team morale", "competitive
   response", etc.).

3. **Call the tool.**

   ```
   get_second_opinion(
     prompt: "<the decision in plain English>",
     analysis_depth: "balanced",
     focus_area: "<optional>"
   )
   ```

4. **Present the result with structure.** Don't dump the raw response — pull
   out the four parts and label them clearly: **Assumptions to check**,
   **Risks you may have skipped**, **Tradeoffs being made**, **Recommended
   next steps**.

5. **Close with a single sharpest question.** Pick the one thing the user
   should answer before moving forward. Phrase it as a question, not advice.

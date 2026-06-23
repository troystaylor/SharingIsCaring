---
name: compare-options
description: |
  Compares two or more options against criteria and recommends one with
  rationale. Use when the user says "compare these options", "which should
  we pick", "give me a tradeoff matrix", "X vs Y", "help me choose between",
  or lists multiple alternatives and wants a structured comparison. Calls
  the Decision Duck `comparative_analysis` MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: analysis
---

# Compare Options

## What This Skill Does

Takes two or more options, optional decision criteria, and the context they
sit in, then returns a side-by-side comparison, an explicit tradeoff
discussion, and a recommendation with rationale. Wraps the
`comparative_analysis` tool.

## When to Activate

- User says "compare A vs B" or "which should we pick"
- User asks for a "tradeoff matrix", "decision matrix", or "pros and cons"
- User lists 2+ alternatives in one message and asks for help choosing
- User asks "what's the right path forward" with multiple paths on the table

## When NOT to Activate

- Only one option is on the table → use `second-opinion` or `risk-analysis`
- User wants a full decision package with bias check too → use `decide`

## Workflow

1. **Confirm context, options, and criteria.** Repeat back.

2. **Call the tool.**

   ```
   comparative_analysis(
     context: "<one-paragraph background>",
     options: ["Option A", "Option B", …],
     criteria: ["cost", "speed", "risk", …]
   )
   ```

3. **Present a comparison table first, then the recommendation.**

4. **Call out the killer tradeoff explicitly.** One sentence.

5. **Recommend one option and the reasoning.** Don't hedge with "it depends".

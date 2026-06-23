---
name: risk-analysis
description: |
  Produces a structured risk analysis with probability/impact estimates,
  mitigations, and leading indicators. Use when the user asks "what could go
  wrong", "what are the risks", "give me a risk register", "what should I
  watch out for", "are there risks in this plan", or shares a scenario and
  wants the failure modes mapped out. Calls the Decision Duck `analyze_risk`
  MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: analysis
---

# Risk Analysis

## What This Skill Does

Turns a scenario or plan into a usable risk register: key risks with
probability and impact, suggested mitigations, and leading indicators that
will signal each risk is materializing. Wraps the `analyze_risk` tool.

## When to Activate

- User asks "what could go wrong" / "what are the risks" / "what's the
  worst case"
- User wants a risk register, risk assessment, or risk matrix
- User shares a plan, launch, migration, deal, or hire and asks what to
  worry about
- User wants "leading indicators" or "tripwires" for a plan

## When NOT to Activate

- User wants to imagine the project failing and work backward → use `pre-mortem`
- User wants an adversarial critique of their reasoning → use `red-team`
- User wants to compare options → use `compare-options`

## Workflow

1. **Restate the scenario.** Confirm what you're analyzing in one line.

2. **Probe for context and prioritized categories.** Ask (optional, don't
   block) whether they want to prioritize specific categories.

3. **Call the tool.**

   ```
   analyze_risk(
     scenario: "<one-paragraph scenario>",
     context: "<optional background>",
     risk_categories: ["<optional prioritization>", …]
   )
   ```

4. **Present as a table.** Five columns: Risk, Probability, Impact,
   Mitigation, Leading indicator.

5. **Call out top 3.** After the table, name the top 3 to act on this week.

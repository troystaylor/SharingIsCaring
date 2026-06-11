---
name: stakeholder-lens
description: |
  Analyzes a decision through specific stakeholder perspectives (CFO,
  engineering lead, customer, legal, frontline manager, board member, etc.)
  and synthesizes where they agree and where they conflict. Use when the
  user says "how would [role] see this", "from finance's perspective", "what
  would our customers think", "walk this past each stakeholder", or needs to
  socialize a decision across multiple audiences. Calls the Decision Duck
  `stakeholder_analysis` MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.1"
  pattern: tool-call
---

# Stakeholder Lens

## What This Skill Does

Takes a decision or plan and runs it through 2–5 stakeholder perspectives
in a single tool call. The `stakeholder_analysis` tool evaluates each
stakeholder's likely position, top concern, and what they need to hear,
then synthesizes agreements, conflicts, and the riskiest stakeholder.

## When to Activate

- "How would <role> see this" / "what would <role> say"
- "From finance / engineering / legal / customer / board perspective"
- "Walk this past each stakeholder"
- "Who's going to push back on this and why"
- User is preparing to socialize a decision and needs to anticipate reactions

## When NOT to Activate

- Single perspective only → use `second-opinion`
- User wants the strongest attack regardless of audience → use `red-team`
- User wants a decision package → use `decide`

## Workflow

1. **Capture the decision and the stakeholders.** Confirm:
   - **Decision:** one paragraph
   - **Stakeholders:** 2–5 roles. Cap at 5.

2. **Call the tool.**

   ```
   stakeholder_analysis(
     decision: "<the decision in one paragraph>",
     stakeholders: ["CFO", "engineering lead", "customer"]
   )
   ```

3. **Present the output.** Format as a stakeholder table with positions,
   agreements, conflicts, and the riskiest stakeholder.

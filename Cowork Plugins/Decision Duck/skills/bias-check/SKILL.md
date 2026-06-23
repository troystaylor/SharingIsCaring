---
name: bias-check
description: |
  Reviews the user's reasoning for cognitive biases (sunk cost, confirmation
  bias, anchoring, recency, availability, status quo, etc.) and suggests
  practical corrections. Use when the user asks for a "bias check", "what
  biases am I missing", "review my reasoning", "am I being biased", "am I
  fooling myself", or shares an argument and wants the flaws called out.
  Calls the Decision Duck `identify_cognitive_biases` MCP tool.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: analysis
---

# Bias Check

## What This Skill Does

Takes the user's reasoning and flags likely cognitive biases — sunk cost,
confirmation bias, anchoring, recency, availability, status quo, IKEA effect,
narrative fallacy, and others — then suggests concrete ways to correct for
each. Wraps the `identify_cognitive_biases` tool.

## When to Activate

- User asks "what biases am I missing" / "is my reasoning sound" / "am I
  rationalizing"
- User wants a "bias check" or "cognitive review" of their thinking
- User shares an argument or justification and asks for critique
- User says "am I just talking myself into this"

## When NOT to Activate

- User wants an alternative perspective on a decision → use `second-opinion`
- User wants adversarial critique of a plan (not just reasoning) → use `red-team`

## Workflow

1. **Restate the reasoning being checked.** One paragraph, in their words.

2. **Ask if they want to focus on specific biases (optional).**

3. **Call the tool.**

   ```
   identify_cognitive_biases(
     analysis: "<the reasoning to check>",
     focus_biases: ["<optional>", …]
   )
   ```

4. **Present in a table.** Three columns: Bias, Why it likely applies here,
   Concrete correction.

5. **End with a reframe.** One sentence: how would the user state their
   decision if they corrected for the strongest bias on the list?

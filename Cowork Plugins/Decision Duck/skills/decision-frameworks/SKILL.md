---
name: decision-frameworks
description: |
  Lists and explains available decision-making frameworks (RICE, OODA loop,
  Eisenhower matrix, Bezos one-way/two-way doors, Cynefin, WRAP, and others
  on the server). Use when the user asks "what framework should I use",
  "explain RICE", "what's a one-way door decision", "give me a decision
  framework for this", "what frameworks do you have". Calls the
  `list_frameworks` and `get_framework` MCP tools.
metadata:
  author: "Troy Taylor"
  version: "1.0"
  pattern: knowledge
---

# Decision Frameworks

## What This Skill Does

Surfaces the decision-framework library hosted on the Decision Duck MCP
server and helps the user pick or apply one. Frameworks are static knowledge
resources — fast, deterministic, no LLM call.

## When to Activate

- User asks "what framework should I use for this decision"
- User asks to explain a specific framework by name
- User wants to see what frameworks are available
- User describes a decision and asks "is there a framework that fits this"

## Workflow

1. **Detect the ask.**
   - "List frameworks" → call `list_frameworks`.
   - "Explain X" → call `get_framework(framework_id: "<id>")`.

2. **For "which framework fits my decision":** ask one clarifying question
   to characterize the decision shape, then recommend a framework.

3. **Tool calls.**

   ```
   list_frameworks()
   get_framework(framework_id: "rice")
   ```

4. **Present the framework with structure.** Pull out: **what it is**,
   **when to use it**, **steps**, **example**.

5. **Offer the next move.** If the user picked a framework, offer to run
   their decision through it now.

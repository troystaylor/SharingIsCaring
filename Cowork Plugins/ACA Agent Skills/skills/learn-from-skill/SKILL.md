---
name: learn-from-skill
description: |
  Retrieves and presents agentskills.io skill content as structured guidance.
  Use when the user asks to "learn about", "explain how to", "what's the best
  practice for", "guide me through", "teach me", or wants knowledge from a
  skill without executing commands. Reads the skill and presents its guidance
  in a conversational format.
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: aggregation
---

# Learn From a Skill

## What This Skill Does

Loads an agentskills.io skill and presents its guidance — decision frameworks,
best practices, code patterns, validation checklists — in a conversational
format. No code execution needed. The skill content enriches the response
with curated expert knowledge.

## When to Activate

- User asks "how should I" or "what's the best approach for"
- User asks to "explain" or "teach me" a topic
- User says "load that skill" after a discovery step
- User wants guidance/knowledge without running commands
- User asks about architecture decisions, patterns, or best practices

## Workflow

1. **Identify which skill to load.** Either from a prior discovery step
   or by searching:

   > `search_skills(query: "AI technology selection .NET")`

2. **Load the full skill content.** Use `get_skill` to fetch the SKILL.md:

   > `get_skill(owner: "dotnet", repo: "skills", skill_path: "plugins/dotnet-ai/skills/technology-selection")`

3. **Read and internalize the skill.** Parse the returned content for:
   - Decision trees or frameworks
   - Step-by-step workflows
   - Code examples and patterns
   - Validation checklists
   - Anti-patterns to avoid

4. **Respond using the skill's guidance.** Don't just dump the raw content.
   Apply it to the user's specific question:
   - If they asked "which AI library should I use?" → walk them through
     the skill's decision tree with their specific inputs
   - If they asked "how do I design a database?" → present the schema
     design workflow adapted to their scenario

5. **Cite the source.** Include a link to the skill on GitHub so the user
   can bookmark it or explore further.

## Output Format

### For decision guidance

Based on the [technology-selection](https://github.com/dotnet/skills/...) skill:

**For your scenario** (document classification on structured data):

→ **Use ML.NET** — not an LLM.

| Your requirement | Recommendation | Reason |
|-----------------|----------------|--------|
| Structured tabular data | ML.NET | Faster, cheaper, deterministic |
| Classification task | ML.NET AutoML | Purpose-built for this |
| .NET 8+ project | Compatible | Full support |

**Key guardrails from the skill:**
- Always set `MLContext(seed: 42)` for reproducibility
- Use `TrainTestSplit` — never evaluate on training data
- Start with AutoML, then refine manually

**Source:** [dotnet/skills — technology-selection](https://github.com/dotnet/skills/...)

### For process guidance

Based on the [requirements-clarity](https://github.com/softaworks/...) skill:

Before implementing, ask these two questions:
1. **Why?** (YAGNI check) — Is this actually needed?
2. **Simpler?** (KISS check) — Is there a simpler approach?

[continued workflow steps from the skill...]

## Handling Edge Cases

- **Skill not found:** Suggest searching with different keywords or
  browsing the curated registry.
- **Skill too long:** Summarize the key sections and offer to deep-dive
  into any specific part.
- **Skill requires execution:** Note that the skill has executable steps
  and offer to switch to apply-skill mode.

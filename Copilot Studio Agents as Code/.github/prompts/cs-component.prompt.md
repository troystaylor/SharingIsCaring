---
agent: agent
description: "Author a new Copilot Studio component — a topic on the standard harness or a behavior on the GitHub Copilot harness — with a description the planner will actually match."
---

# Add a component to this agent

Create one new component in the cloned agent in this workspace. Do not apply it — I will review and sync separately.

## 1. Identify the harness before writing anything

Run the schema script and use its output:

```powershell
.github/skills/copilot-studio-agent/scripts/Get-AgentSchema.ps1 <agent folder>
```

It reports the harness, the components already present, and the **verified node kinds for this specific agent**. Use only kinds from that list. If you need something not in it, say so and stop rather than guessing.

| Harness reported | Write | Location |
|---|---|---|
| GitHub Copilot harness | `kind: InlineAgentSkill` | `behaviors/` |
| Standard harness | `kind: AdaptiveDialog` | `topics/` |

Never write the other harness's primitive. Nothing will invoke it.

## 2. Ask me what the component is for

If I have not already told you, ask for the job it does and when it should fire. One round of questions, all at once.

## 3. Write the description first

The description is the only thing the planner reads when deciding whether to invoke this component — `mcs.metadata.description` on a behavior, `modelDescription` on a topic. Everything else is wasted if this is weak.

Write it as the **when**, not the **what**, and show it to me before writing the body:

- Good: "Use when the user is asking whether a purchase requires competitive bidding or can be a direct award."
- Bad: "Bidding logic."

If it overlaps an existing component's description, tell me — two components competing for the same intent means neither fires reliably.

## 4. Write the file

Open with the `mcs.metadata` block. Match the file naming already used in the folder.

For a **behavior**, the body is markdown under `content:` — one job, concrete steps, no preamble.

For a **topic**, the body is a node list under `beginDialog.actions`. Use `kind: OnRecognizedIntent` with `intent: {}` so the planner selects it generatively from `modelDescription` rather than from trigger phrases.

Reuse an existing component in the same folder as a shape reference before inventing structure.

## 5. Check and hand back

Confirm the Problems pane is clean, show me the file, and remind me to run `/cs-sync` when I want it live. Do not apply.

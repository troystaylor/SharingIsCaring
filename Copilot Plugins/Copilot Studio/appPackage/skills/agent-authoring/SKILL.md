---
name: agent-authoring
description: Create or change a Copilot Studio agent as a draft - clarify the purpose, read the Copilot Studio authoring guidance and schema, build the complete draft, preview it, and only then publish. Use when the user asks to create, build, edit, improve or review the instructions, tools, knowledge or triggers of a Copilot Studio agent.
---

# Agent Authoring

## When to use this skill

- "Build an agent that...", "create a Copilot Studio agent for...", "add a tool or knowledge source to my agent", "tighten my agent's instructions", "publish my agent".
- For testing and scoring an agent, use `agent-evaluation`. For workflows or automations, use `workflow-authoring-and-diagnostics`.

## How to do it

1. Apply `copilot-studio-access`. Call `get_copilot_studio_skills`, read the core skill for agent authoring, then read its `schema.json`. Author against that schema, not memory.
2. Clarify only what is missing: who uses the agent, what it should and should not do, what systems or knowledge it needs, and how it is triggered or shared. Ask at most a few questions at once.
3. Check what exists: use `manage_agents` to list agents and, if the user is changing one, get its current draft. Avoid creating a duplicate.
4. If the agent needs connectors, use `connector-discovery` to find real operations and existing connections before adding them.
5. Build the complete draft in one pass (name, description, instructions, tools, knowledge, triggers) rather than a sequence of partial edits.
6. Follow `write-safety`: preview the draft, get confirmation, save it as a draft, then re-read it and report what was saved.
7. Use the saved-draft preview to try one or two realistic prompts. Report what the agent did and where it fell short. Suggest running an evaluation suite (`agent-evaluation`) before publishing.
8. Publish or share only when asked, with its own preview and confirmation under `write-safety`.

## What to watch for

- Instructions should say what the agent does, what it must not do, and when to hand off or decline. Keep them specific; vague instructions are the most common cause of weak agents.
- Do not add tools or knowledge the user did not ask for.
- Credits are consumed when agents run. If the user asks about cost, use `consumption-review`.
- If an action or field is not in the schema or the `describe_actions` contract, say it is not supported here instead of improvising.

---
name: copilot-studio-access
description: Shared rules for every call through the Copilot Studio connector - confirm the environment, read Copilot Studio guidance before authoring, discover action contracts with describe_actions, handle denied or empty results, and cite what was read. Use whenever a task touches Copilot Studio agents, workflows, connections or consumption, before running any other Copilot Studio skill.
---

# Copilot Studio Access

Apply this skill before any Copilot Studio call. The other Copilot Studio skills assume it.

## What the connector exposes

The connector is a gateway that reaches Copilot Studio in any Power Platform environment the signed-in user can access, and runs as that user. It offers `list_environments` plus five Copilot Studio tools:

- `list_environments` - the environments the user can access, with their IDs.
- `get_copilot_studio_skills` - Copilot Studio guidance. Start here for any authoring task.
- `manage_agents` - agent inventory, draft authoring, lifecycle, sharing of published agents, saved-draft previews, and evaluation suites and runs.
- `manage_workflows` - workflow inventory, draft authoring, lifecycle, real tests and run diagnostics.
- `discover_resources` - connector operations and schemas, existing connections, dependent parameter values and attachable resources.
- `get_consumption` - historical agent or workflow credit consumption and current environment credit capacity.

Every tool except `list_environments` requires `environmentId`. `manage_agents`, `manage_workflows`, `discover_resources` and `get_consumption` each also take an `action` and a `parameters` object.

## Bootstrap (always first)

1. Choose the environment. If the user named one, call `list_environments` and match it by display name. If they did not, call `list_environments`, show the names and types, and ask which to use. Never guess an environment, and never invent an ID. Prefer a non-production environment for building and testing, and say so if the user picked production.
2. Use that one `environmentId` for the whole task. Agents, workflows, connections and credit figures belong to a single environment, so do not mix IDs from different environments. If the user changes environment, start the lookup again.
3. For any authoring task, call `get_copilot_studio_skills` first, read the core skill that matches the task, then read its `schema.json` before building anything.
4. Before using an action you have not used in this conversation, call `discover_resources` with `action` set to `describe_actions` to read its contract and side effects. Do not guess action names or parameter names from memory.
5. Never invent an agent ID, workflow ID, connection ID, connector name or parameter value. Resolve each from tool output.

## Reads before writes

- List or get the current state first (inventory, then the specific agent or workflow) so you can show what exists and avoid creating duplicates.
- Any action whose described side effects change state goes through `write-safety`.

## When access is denied or empty

- A 401, 403, "not found" or empty result can mean the user lacks a Copilot Studio or Power Platform role in this environment, sign-in expired, a tenant or data policy blocks the action, or the environment has no Copilot Studio capacity. Say which it could be and that you cannot tell from here.
- If `list_environments` is denied, the plugin's environment-discovery permission may not have admin consent. Say so and point the user to an admin.
- Do not retry with guessed identifiers or switch to a different action to work around a denial. Report the limit and say who can grant access (an environment admin or Power Platform admin).
- If setup or connection fails, report the error as shown. Do not ask for or repeat credentials or configuration JSON.

## Output

- Name the environment, and the agent or workflow, by display name and ID so the user can find it in Copilot Studio.
- Distinguish a saved draft from a published agent or workflow, and say which one an action affected.
- Also apply `write-safety` for any change.

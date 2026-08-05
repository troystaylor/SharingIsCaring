---
applyTo: "**/*.mcs.yaml,**/*.mcs.yml"
description: "Hard rules for editing cloned Microsoft Copilot Studio agent definition files (.mcs.yaml / .mcs.yml) managed by the Copilot Studio VS Code extension."
---

# Copilot Studio agent definition files

These files are a **cloned agent definition** owned by the Copilot Studio VS Code extension. They round-trip to a live agent in a Dataverse environment.

## Never

- **Never rename or move these files.** The extension maps them to remote components by path and name. `settings.mcs.yml` and `connectionreferences.mcs.yml` are fixed. Ignore any advice to rename to `.topic.yaml` / `.tool.yaml` — that contradicts what clone emits.
- **Never invent a `kind:` value.** The skill carries the verified vocabulary extracted from real agents. If a kind is not in it, verify with `Ctrl+Space` IntelliSense or the Problems pane (`Ctrl+Shift+M`) before writing it. A guessed kind fails on apply and costs a round trip.
- **Never mix harness primitives.** `InlineAgentSkill` belongs to the GitHub Copilot harness (`behaviors/`); `AdaptiveDialog` belongs to the standard harness (`topics/`). Check `template` in `settings.mcs.yml` — `cliagent-1.0.0` or `default-2.1.0` — before adding a component.
- **Never hand-edit `.mcs/botdefinition.json`.** It is generated state and git-ignored. Read it for analysis only.
- **Never edit while remote changes are pending.** Run `Copilot Studio: Preview` first. Apply is blocked until outstanding remote changes are retrieved with `Copilot Studio: Get Changes`.

## Always

- **Open every component file with the `mcs.metadata` block.** It carries `componentName` and, for behaviors, `description`. Microsoft's documented examples omit it; the product requires it.
- **Treat the description as the discovery surface.** `modelDescription` on a topic and `mcs.metadata.description` on a behavior are what the planner reads to decide whether to invoke the component. Write the *when*, not the *what*: "Use when the user asks whether a purchase needs competitive bidding" fires; "Bidding logic" does not.
- Power Fx expressions start with `=` — `condition: =Topic.Continue = true`. Equality is a single `=`, never `==`.
- Declare a variable on first write with the `init:` scope prefix — `variable: init:Topic.OrderNumber`.
- Every node needs an `id` unique within its file. Generated forms (`sendActivity_wz3qj9`) and GUIDs are both valid.
- Interpolate system variables in braces — `{System.Bot.Name}`.
- Strip zero-width characters (`U+200B`, `U+200C`) when editing instructions. Text pasted through the portal carries them; they are invisible in the UI but pollute diffs.
- Check the Problems pane before proposing an apply. Zero errors is the gate.

## Apply is not publish

`Copilot Studio: Apply Changes` updates the agent in the environment. It does **not** publish it. Testing happens in the Copilot Studio test pane after apply; end users see nothing until a separate publish.

For the full schema, folder layout, and sync workflow, load the `copilot-studio-agent` skill.

---
name: copilot-studio-agent
description: "Author and edit Microsoft Copilot Studio agents as code in VS Code using the Copilot Studio extension. Load when working with a cloned agent definition: editing topics, tools, triggers, knowledge sources, or settings in .mcs.yaml/.mcs.yml files; writing AdaptiveDialog topic YAML; using Power Fx conditions or Topic/Global variables; or running the clone, preview, get, and apply sync workflow. Trigger phrases: Copilot Studio agent, agent definition, clone agent, apply changes, topic YAML, AdaptiveDialog, mcs.yaml, Copilot Studio extension."
---

# Copilot Studio agents as code

The Copilot Studio VS Code extension clones a live agent to disk as YAML, lets you edit it locally under Git, and applies changes back to the environment. This skill is the schema and workflow reference so you write valid YAML on the first attempt instead of discovering it through failed applies.

Everything here was verified against real cloned agents, not taken from documentation. Where the two disagree, the observed behaviour is recorded and the discrepancy noted. See **Provenance** at the end for what was tested and when.

## The round trip is confirmed

Local edits reach the live agent. Verified by editing a projected topic and applying:

| Change | Result |
|---|---|
| `modelDescription` rewritten | Applied, and the planner routes on the new text |
| Existing `activity` string changed | Applied |
| **New node added with a hand-written `id`** | **Applied** |

The third matters most: ids do not have to be server-generated. You can author new structure locally rather than only editing what the portal created. Match the observed format — `<nodeKindCamel>_<6 chars>`, e.g. `sendActivity_local1`.

**Scope note:** Microsoft's extension documentation is published against the **standard harness**, but the extension clones **GitHub Copilot harness** agents too. The two produce different on-disk layouts. Identify which you have before editing.

## Identify the authoring shape first

Run the bundled script rather than reading files by hand — it reports the harness, the projected files, and the node kinds actually present in that agent:

```powershell
scripts/Get-AgentSchema.ps1 <agent folder>       # full report
scripts/Get-AgentSchema.ps1 <agent folder> -Kinds # vocabulary only
```

It accepts either the folder containing `.mcs` or its parent, since clone nests a folder named after the agent. Requires PowerShell 7.

| Signal | Standard harness | GitHub Copilot harness |
|---|---|---|
| `.mcs/conn.json` → `AuthoringShape` | `1` | `2` |
| `settings.mcs.yml` → `template` | `default-2.1.0` | `cliagent-1.0.0` |
| `configuration.recognizer.kind` | `GenerativeAIRecognizer` | `CLICopilotRecognizer` |
| Editable components land in | `topics/`, `actions/` | `behaviors/` |
| Instructions live in | `.mcs/botdefinition.json` (git-ignored) | `settings.mcs.yml` (tracked) |
| Model selection | `aISettings.model.modelNameHint` | `agentSettings.model.series` |

The practical consequence: on the GitHub Copilot harness the instructions and behaviors are **projected into tracked YAML**, so Git diffs and pull requests work. On the standard harness they may not project at all, leaving the definition only in the git-ignored blob.

### What actually projects to disk

Projection emits **custom components only**. An agent built entirely from system topics produces no `topics/` folder — that is correct behaviour, not a failed clone. Add one custom topic and the folder appears.

| | Standard harness | GitHub Copilot harness |
|---|---|---|
| Custom topics | Yes — `topics/*.mcs.yml` | n/a — no topics |
| Behaviors / skills | n/a | Yes — `behaviors/*.mcs.yml` |
| Agent instructions | **No** — stays in `botdefinition.json` | Yes — in `settings.mcs.yml` |
| System topics | No | n/a |
| Connection references | Yes | Yes |

So a rule-and-topic agent can be authored as code on the standard harness, and a reasoning agent can be authored as code on the GitHub Copilot harness. What you cannot currently do is edit a standard-harness agent's generative instructions locally.

## Folder layout

The documented full structure is below. **Verify against what clone actually produced** — observed output can be far sparser, with the real definition held only in `.mcs/`.

```text
my-agent/
├── .mcs/                         # Extension state — git-ignored via `*`
│   ├── botdefinition.json        # The complete agent definition (can be ~750KB)
│   ├── changetoken.txt           # Sync watermark
│   ├── conn.json                 # Environment, tenant, AgentId, AuthoringShape
│   └── .connectors-download.json
├── actions/                      # Connector actions
├── knowledge/files/              # Knowledge sources and uploaded files
├── topics/                       # Conversation topics
├── workflows/                    # Power Automate flows used as tools
│   └── GetDevOpsItems/
│       ├── metadata.yaml
│       └── workflow.json
├── trigger/                      # Event triggers
├── agent.mcs.yaml                # Main agent definition
├── icon.png
├── settings.mcs.yml
└── connectionreferences.mcs.yml
```

Clone creates a folder **named after the agent inside the folder you select**, so picking an identically named folder yields a nested duplicate.

Both `.yaml` and `.yml` appear in clone output. Match whatever is already on disk rather than normalizing. Microsoft's documentation spells the last file `connectioreferences.mcs.yml`; the product writes `connectionreferences.mcs.yml`.

### When only `.mcs/` has content

If clone yields `settings.mcs.yml`, `connectionreferences.mcs.yml`, and `icon.png` but no `agent.mcs.yaml` or `topics/`, the YAML projection did not run for that agent. Everything is still in `.mcs/botdefinition.json`, but **that path is git-ignored**, so nothing meaningful is under source control. Read the JSON for analysis; do not hand-edit it as a way to change the agent.

Parse it with `ConvertFrom-Json -AsHashtable` — it contains keys differing only by case (`id` and `Id`), which the default parser rejects.

Top level is `$kind: BotDefinition` with `components`, `connectionReferences`, `connectorDefinitions`, `entity`, and `environmentVariables`. Components are `DialogComponent` (topics and actions) or `GptComponent` (instructions, model hint, capabilities), named `{publisher}_{agent}.{topic|action|gpt}.{Name}`.

## GitHub Copilot harness layout

Agents with `AuthoringShape: 2` clone to a different and much smaller structure. None of it appears in Microsoft's documentation.

```text
my-agent/
├── .mcs/                              # Same git-ignored state folder
├── behaviors/                         # Modular skills — the editable surface
│   ├── Procurement_Intake.mcs.yml
│   └── Policy_Escalation.mcs.yml
├── workflows/
├── agent.sync.yaml                    # Layout marker only — `layoutVersion: 1`
└── settings.mcs.yml                   # Identity, model, AND full instructions
```

There is no `agent.mcs.yaml`, no `topics/`, and no system topics at all — which is why `botdefinition.json` is roughly thirty times smaller than a standard-harness equivalent.

`agent.sync.yaml` is a sync overlay and is never MCS-parsed. Identity lives in `settings.mcs.yml`.

### Instructions and model

Both sit in `settings.mcs.yml` under `configuration.agentSettings`:

```yaml
configuration:
  recognizer:
    kind: CLICopilotRecognizer
  agentSettings:
    model:
      series: Sonnet46
    instructions:
      segments:
        - kind: StaticSegment
          value: |-
            # Role
            ...
```

`model.series` is a one-line edit and a direct cost lever — this harness bills per token, so the model choice is a budgeting decision, not just a quality one.

Instructions are a list of `segments`, so a `StaticSegment` is one of several possible kinds. Verify others against IntelliSense before using them.

### Behaviors are skills

Each file in `behaviors/` is an `InlineAgentSkill` — metadata plus a markdown body:

```yaml
mcs.metadata:
  componentName: Policy Escalation
  description: Escalates to the procurement team when policy is ambiguous, a scenario is not clearly covered, or proceeding would risk bypassing a procurement control.
kind: InlineAgentSkill
content: |-
  ## Policy Escalation

  Use this skill when:
  - ...
```

The `description` is the discovery surface — the planner selects a behavior by matching intent against it, exactly like a VS Code `SKILL.md`. A vague description means the behavior never fires. Put the trigger conditions in it.

Keep each behavior to one job. Natural-language agent creation decomposes a prose instruction block into several behaviors automatically, which is the shape to preserve when editing by hand.

### Watch for invisible characters

Instructions pasted into the portal can carry zero-width characters (`U+200B`, `U+200C`) into the YAML. They are invisible in the Copilot Studio UI but present in the file and will show in diffs. Strip them when editing locally.

## Sync workflow

| Command | Direction | Effect |
|---|---|---|
| `Copilot Studio: Clone Agent` | Cloud → Local | Downloads the full agent definition |
| `Copilot Studio: Preview` | Cloud → Local | Shows remote changes without applying |
| `Copilot Studio: Get Changes` | Cloud → Local | Applies remote changes; overwrites uncommitted local edits |
| `Copilot Studio: Apply Changes` | Local → Cloud | Updates the live agent — **does not publish** |
| `Copilot Studio: Reattach Agent` | — | Reconnects a Git-cloned folder to an environment |

The safe order is **Preview → Get → edit → validate → Apply**. Apply is blocked outright if remote changes exist that you have not retrieved. Commit to Git before Get, because Get overwrites uncommitted work.

## Topic anatomy

Every projected component file opens with an `mcs.metadata` block, then `kind`, then the body. This wrapper is consistent across topics and behaviors, and Microsoft's documented examples omit it — include it.

A topic is an `AdaptiveDialog`. `beginDialog` holds the entry trigger and an ordered `actions` list.

```yaml
mcs.metadata:
  componentName: Test
kind: AdaptiveDialog
modelDescription: This is a test
beginDialog:
  kind: OnRecognizedIntent
  id: main
  intent: {}
  actions:
    - kind: SendActivity
      id: sendActivity_wz3qj9
      activity: Testing

inputType: {}
outputType: {}
```

`modelDescription` is the **discovery surface** when generative orchestration is on — the planner reads it to decide whether to invoke the topic. A vague or empty one means the topic never fires. It is the topic equivalent of a behavior's `description`.

`inputType` and `outputType` declare the topic's input/output contract and appear at root level. Empty maps mean no parameters.

Ids observed take the form `<nodeKindCamel>_<random>` — `sendActivity_wz3qj9`. GUIDs are also valid.

### Verified node kinds

Extracted from a real agent definition, not from documentation. These are safe to use.

| Group | Kinds |
|---|---|
| Dialog roots | `AdaptiveDialog`, `TaskDialog`, `InlineAgentSkill` (GitHub Copilot harness only) |
| Triggers | `OnConversationStart`, `OnRecognizedIntent`, `OnUnknownIntent`, `OnSelectIntent`, `OnSystemRedirect`, `OnEscalate`, `OnSignIn`, `OnError` |
| Messaging | `SendActivity`, `Question`, `CSATQuestion` |
| Flow control | `ConditionGroup`, `BeginDialog`, `ReplaceDialog`, `EndDialog`, `EndConversation`, `CancelAllDialogs` |
| Variables and data | `SetVariable`, `SetTextVariable`, `ClearAllVariables`, `EditTable` |
| Knowledge | `SearchAndSummarizeContent` |
| Connectors | `InvokeConnectorTaskAction` |
| Authentication | `OAuthInput` |
| Entities | `DynamicClosedListEntity` |
| Telemetry | `LogCustomTelemetryEvent` |

To refresh this list from any agent, extract `kind:` from the dialog strings in `.mcs/botdefinition.json`. Anything outside this table is unverified — check IntelliSense before writing it.

### SendActivity has two forms

Scalar, when you just need text:

```yaml
- kind: SendActivity
  id: sendMessage_4eOE6h
  activity: Go ahead. I'm listening.
```

Object, when you need multiple variants or a separate spoken form:

```yaml
- kind: SendActivity
  id: sendMessage_M0LuhV
  activity:
    text:
      - Hello, I'm {System.Bot.Name}. How can I help?
    speak:
      - Hello and thank you for calling {System.Bot.Name}.
```

### Question

```yaml
- kind: Question
  id: question_1
  alwaysPrompt: true
  variable: init:Topic.Continue
  prompt: Can I help with anything else?
  entity: BooleanPrebuiltEntity
```

`conversationOutcome: ResolvedImplied` is an optional sibling used to mark the turn as a resolution for analytics.

### ConditionGroup

```yaml
- kind: ConditionGroup
  id: condition-1
  conditions:
    - id: condition-1-item-0
      condition: =Topic.Continue = true
      actions:
        - kind: SendActivity
          id: sendMessage_4eOE6h
          activity: Go ahead. I'm listening.
```

## Behavior-style components on the standard harness

The standard harness has no `InlineAgentSkill`, but when `GenerativeActionsEnabled` is true and the recognizer is `GenerativeAIRecognizer`, **topics are selected the same way behaviors are** — by a natural-language description, not by trained intent phrases. The pattern transfers; only the primitive changes.

| GitHub Copilot harness | Standard harness |
|---|---|
| `behaviors/*.mcs.yml` | `topics/*.mcs.yml` |
| `kind: InlineAgentSkill` | `kind: AdaptiveDialog` |
| `mcs.metadata.description` | `modelDescription` |
| `content:` markdown body | `beginDialog.actions` node list |

The hinge is an **empty intent**. A topic written as:

```yaml
beginDialog:
  kind: OnRecognizedIntent
  id: main
  intent: {}
```

has no trigger phrases, so under generative orchestration the planner selects it purely from `modelDescription`. That is functionally the same selection mechanism a behavior uses.

So one behavior maps to one topic: put the behavior's trigger conditions in `modelDescription`, and express its body as nodes — `SendActivity` for guidance, `Question` to gather inputs, `ConditionGroup` to branch, `SearchAndSummarizeContent` to answer from knowledge, `InvokeConnectorTaskAction` to call a connector.

Write `modelDescription` as the *when*, not the *what*: "Use when the user is asking whether a purchase requires competitive bidding" beats "Bidding logic." It is the only thing the planner reads when deciding.

### What this does and does not buy you

It does give you modular, description-selected generative components authored entirely in projected, diffable YAML — on the harness that does not bill until publish.

It does not give you the agent's top-level instructions, which stay unprojected on this harness. Push logic down into topics so less has to live in that unreachable prose.

Copying `behaviors/` wholesale into a standard-harness agent folder is not expected to work. Behaviors and topics share the `DialogComponent` envelope, so a component may well upload, but the standard runtime has no `InlineAgentSkill` handler and nothing would invoke it. Convert the pattern rather than moving the files.


## Variables, Power Fx, entities

- **Scopes:** `Topic.` for topic-local, `Global.` for conversation-wide, `System.` for built-ins.
- **Declaration:** prefix with `init:` the first time you write to a variable — `variable: init:Topic.OrderNumber`. Reading needs no prefix.
- **Power Fx:** every expression starts with `=`. Equality is a single `=`, so `=Topic.Continue = true` is correct, not `==`.
- **Interpolation:** `{System.Bot.Name}` inside activity text.
- **Entities:** prebuilt entities follow the `<Type>PrebuiltEntity` pattern, e.g. `BooleanPrebuiltEntity`. Confirm the exact name with IntelliSense rather than guessing the type prefix.

## Triggers

Triggers live in `trigger/` and usually reference a workflow.

```yaml
kind: ExternalTriggerConfiguration
externalTriggerSource:
  kind: WorkflowExternalTrigger
```

## Tools

Tools are what the agent can do: prompts, Power Automate workflows, custom connectors, REST APIs, MCP connectors, and CUA tools. Connector actions land in `actions/`; workflows get their own folder under `workflows/` with a `metadata.yaml` and a `workflow.json`.

## Knowledge

Files dropped into `knowledge/files/` are uploaded on apply. Files already uploaded through the portal are **not** downloaded automatically — fetch them explicitly from the Remote Knowledge Files pane if you need them locally.

## Editing checklist

1. `Copilot Studio: Preview` — is anything pending remotely?
2. `Copilot Studio: Get Changes` if so, resolving conflicts deliberately.
3. Make the edit. Reuse an existing node in the file as a shape reference before writing a new kind.
4. `Ctrl+Shift+M` — Problems pane must be clean.
5. Commit to Git.
6. `Copilot Studio: Apply Changes`.
7. Test in the Copilot Studio test pane. Publish separately when satisfied.

## Naming

Clone output governs file names. For values you author, use camelCase for ids and variables (`userOrderNumber`, not `check1`), and keep names descriptive enough to find with `Ctrl+F` across the agent.

## Promotion between environments

Use solutions for dev → test → prod. `Reattach Agent` can point a local folder at a different environment, but that bypasses the audit trail — prefer solution export/import or pipelines for anything shipping to users.

### Check the publisher before promoting

`entity.schemaName` in `botdefinition.json` carries the publisher prefix, and the two creation paths pick different publishers even within the same environment:

| Created via | Publisher | Prefix |
|---|---|---|
| Standard harness | Copilot Studio publisher, e.g. `Cr12201` | `cra4a_` |
| GitHub Copilot harness | Org default, e.g. `DefaultPublisherorg<id>` | `Default_` |

An agent on the org default publisher is not namespaced to your team. Create it inside a custom solution with your own publisher before it leaves the tenant it was built in — the prefix is baked into component schema names and is not something you rename afterward.

## Provenance

Verified August 2026 against `CopilotStudioSolutionVersion 2026.6.3.20581040`, using two agents in one Dataverse environment — one per harness.

Confirmed by direct observation: the harness discriminators; the two folder layouts; the `mcs.metadata` wrapper; `modelDescription`, `OnRecognizedIntent`, `intent: {}`, `inputType`/`outputType`; the `behaviors/` folder and `InlineAgentSkill`; instructions living in `settings.mcs.yml` on the GitHub Copilot harness only; projection emitting custom components only; the 29-kind vocabulary; the publisher difference; and the round trip including hand-written ids.

Treat as provisional: `cliagent-1.0.0` is a 1.0 template and `behaviors/` is undocumented, so both may change. The node vocabulary is per-agent — run `scripts/Get-AgentSchema.ps1` against your own rather than trusting the list here. Anything sourced from Microsoft Learn rather than observation is marked where it appears.

# Copilot Studio Agents as Code

Field notes on building **Microsoft Copilot Studio agents as code**, plus a VS Code customization pack. Written from cloning and editing one agent on each harness in a live tenant, using the [Copilot Studio extension](https://learn.microsoft.com/microsoft-copilot-studio/visual-studio-code-extension-overview).

Three things here, in order of how much they should change your decisions:

1. **Which harness to pick.** They are a price class apart — roughly an order of magnitude per interaction — and employee-facing standard harness agents are *free* for Microsoft 365 Copilot licensed users. The GitHub Copilot harness has no such inclusion.
2. **Corrections to Microsoft's documentation.** Nine of them, found by comparing the docs to real clone output. Listed under [Corrections](#corrections).
3. **A working setup.** Instructions, a skill, two prompts, and a script that reports what your agent actually contains.

Everything was verified against live agents. See [Limits](#limits) for what was tested and when.

## What projects to disk

The round trip is confirmed: edit a projected file, apply, and the change reaches the live agent — including entirely new nodes with hand-written ids. Verified on the standard harness.

What you get to edit differs by harness:

| | Standard harness | GitHub Copilot harness |
|---|---|---|
| Authoring cost | Free until publish | Metered from first build action |
| Topics as code | Yes | No topics at all |
| Behaviors as code | n/a | Files project; apply **untested** |
| **Instructions as code** | **No** | **Yes** |

So a rule-and-topic agent can be built *and iterated* as code for nothing until publish. A reasoning agent projects more — its instructions and behaviors both land on disk — but every test run is billed, and testing is the inner loop of agent development, not a final step.


## The harnesses are a price class apart

At $0.01 per Copilot Credit:

| | Credits | Cost |
|---|---|---|
| Standard — classic answer | 1 | $0.01 |
| Standard — generative answer | 2 | $0.02 |
| Standard — agent action | 5 | $0.05 |
| Standard — tenant graph grounding | 10 | $0.10 |
| Standard — multi-step run (4 agent actions) | 20 | $0.20 |
| GitHub Copilot — light task | 100–300 | $1.00–$3.00 |
| GitHub Copilot — medium task | 300–500 | $3.00–$5.00 |
| GitHub Copilot — heavy task | >500 | >$5.00 |

A *light* GitHub Copilot task costs 5–15× a complete standard harness multi-step run.

**Those tiers are runtime figures, and development is additional.** Microsoft's "How Copilot credits are measured" section frames consumption entirely around "how often customers interact" — it contains no development term, no worked example, and no guidance on expected iteration counts. The page states elsewhere that building, previewing, testing, and generating evaluations all consume credits, but never quantifies them, and never confirms whether a test run bills as a full task. Budget development separately as *iterations × tier cost*, and measure your own iteration count because none is published.

**And employee-facing standard harness usage is free for Microsoft 365 Copilot licensed users** — every billable feature carries a "no charge" inclusion when the agent runs under the authenticated licensed user's identity. Microsoft's own worked example bills 100 of 150 users for exactly this reason. The GitHub Copilot harness billing page has no equivalent exclusion.

For an internal agent at 100,000 interactions a year:

| | Annual |
|---|---|
| Standard, M365 Copilot licensed employees | **$0** |
| Standard, unlicensed, ~20 credits per run | ~$20,000 |
| GitHub Copilot harness, medium tier | ~$400,000 |

Caveats on the inclusion: business-to-employee only, subject to unstated fair-usage limits Microsoft can revise, excludes Computer-Using Agents, and for agent flows applies only to the *"When an agent calls the flow"* trigger. Also note reasoning models **double-bill** — feature rate plus premium AI tools at 10 credits per 1K tokens — and enforcement at 125% of prepaid capacity *disables* agents rather than throttling them.

Rates: [standard harness](https://learn.microsoft.com/microsoft-copilot-studio/requirements-messages-management#copilot-credits-billing-rates) · [GitHub Copilot harness](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/billing-credit-overview#how-copilot-credits-are-measured) (tiers published only as an image).

## Choosing

Pick the GitHub Copilot harness when you genuinely need goal decomposition, native Word/Excel/PDF generation, memory, or recovery from failed steps. Nothing else does those.

Pick the standard harness for threshold lookups, approval routing, and structured Q&A — which is most of what agents get asked to do. Deterministic logic in a `ConditionGroup` cannot hallucinate a policy number, costs a fraction to run, iterates free, and is reviewable in Git.

The harness question is really: *how much of this agent actually has to sit on the expensive one?*

What you cannot currently do is edit a standard harness agent's generative instructions as code. Push logic into topics instead.

## What's in it

| File | Kind | Loads |
|---|---|---|
| `.github/instructions/copilot-studio-agent.instructions.md` | Instructions | Automatically, for `*.mcs.yaml` and `*.mcs.yml` |
| `.github/skills/copilot-studio-agent/SKILL.md` | Skill | On demand, when you're authoring |
| `.github/skills/copilot-studio-agent/scripts/Get-AgentSchema.ps1` | Script | When the skill or a prompt calls it |
| `.github/prompts/cs-component.prompt.md` | Prompt | When you run `/cs-component` |
| `.github/prompts/cs-sync.prompt.md` | Prompt | When you run `/cs-sync` |

The split is deliberate. The instructions file is short and always-on for agent definition files — the handful of rules that prevent a failed apply. The skill carries the full schema and folder layout and only enters context when you actually need it. Under usage-based billing, a fat always-on instruction file is a charge you pay on every turn of every session.

## Using it

Copy `.github/` into the workspace where you cloned your agent, or clone the agent into this folder. Then:

```
Ctrl+Shift+P → Copilot Studio: Clone Agent
```

Once files are on disk, the instructions apply themselves whenever you open a `.mcs.yaml`. Ask for the skill by name, or just start editing a topic and it will be pulled in. `/cs-sync` runs the preview → get → validate → commit → apply ritual.

## The two harnesses

Microsoft's extension documentation is banner-marked **standard harness** throughout, which left open whether GitHub Copilot harness agents could be cloned at all. They can, and they project more.

| | Standard harness | GitHub Copilot harness |
|---|---|---|
| `AuthoringShape` | `1` | `2` |
| `template` | `default-2.1.0` | `cliagent-1.0.0` |
| `recognizer.kind` | `GenerativeAIRecognizer` | `CLICopilotRecognizer` |
| Editable files | `topics/*.mcs.yml` | `behaviors/*.mcs.yml` |
| Instructions | git-ignored `botdefinition.json` | tracked `settings.mcs.yml` |
| `botdefinition.json` | ~750KB | ~23KB |
| Publisher observed | Copilot Studio (`cra4a_`) | org default (`Default_`) |

Run the bundled script to identify which you have, what projected, and the node kinds valid for that specific agent:

```powershell
.github/skills/copilot-studio-agent/scripts/Get-AgentSchema.ps1 <agent folder>
```

## Corrections

Where the product disagrees with Microsoft Learn. This is the part worth your time.

| Docs say | Product does |
|---|---|
| `connectioreferences.mcs.yml` | `connectionreferences.mcs.yml` |
| Name files `.topic.yaml` / `.tool.yaml` | Clone emits `.mcs.yaml`; renaming breaks component mapping |
| Topic examples with no `mcs.metadata` | Required on every component file |
| Five node kinds | 29 in one ordinary agent |
| — | `modelDescription` is the discovery surface, undocumented |
| — | `OnRecognizedIntent`, `intent: {}`, `inputType`/`outputType`, undocumented |
| — | `behaviors/` and `agent.sync.yaml`, undocumented |
| One folder layout | Two, keyed on `AuthoringShape` |

Three behaviours nobody documents:

- **Projection emits custom components only.** An agent of nothing but system topics produces no `topics/` folder. That is correct, not a failed clone.
- **`.mcs/.gitignore` contains `*`.** The definition is deliberately excluded from source control, so whatever fails to project is not versioned at all.
- **`intent: {}` plus `modelDescription`** gives behaviour-style generative selection on the standard harness.

## Limits

Verified August 2026 against `CopilotStudioSolutionVersion 2026.6.3.20581040`, using two agents in a single Dataverse environment — one per harness.

`cliagent-1.0.0` is a 1.0 template and `behaviors/` is undocumented, so both should be expected to move. The node vocabulary is per-agent: run the script against your own rather than trusting the count above.

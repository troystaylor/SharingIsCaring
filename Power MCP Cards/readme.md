# Power MCP Cards

Shows interactive cards in Copilot Studio agents. The agent gets each card from a skill and passes it to the connector.

This connector shows interactive cards to your agent as [MCP Apps](https://modelcontextprotocol.io/extensions/apps/overview), and **the cards are not in the connector**. It has two tools. `show_card` draws whatever card the agent passes it, and `run_action` runs whatever action a card's button asks for. Each card lives in a skill on the agent, so you change a card by editing its skill: you don't generate or redeploy the connector, and a card can use any action without the connector changing.

## How it works

1. A skill tells the agent when to show a card and holds the card's JSON.
2. The agent calls `show_card` with that JSON as a string in `card`, and the values the card shows as a string in `data`.
3. The connector checks the card against its rules (below). If it passes, the card is drawn. If not, the tool says why and nothing is shown.
4. When a button runs an action, the page calls `run_action` with the action's name and its arguments.

## Skills

| File | Card | When the agent shows it |
|------|------|-------------------------|
| `skills/power-mcp-cards/SKILL.md` | All 4 cards: Approval card, Form card, Table card, Status card | Use when the user asks to show, display, or collect something: an approval request, a form, a table or list of records, or job status. They need not say "card". |

## Tools

| Tool | What it does |
|------|--------------|
| `show_card` | Draws the card the agent passes it |
| `run_action` | Runs an action a card's button asked for. The button names the action (`submit_form`, `refresh_status`, anything), and the page passes the name and its arguments here |

Because every action goes through `run_action`, a card can use any action name and the connector's tool list never changes. Until you add a case for an action in `RunAction` in `script.csx`, the action is recorded and its input is echoed back with a confirmation ID, so a card runs end to end. Add a `case` for each action that should do real work (call your own system, then return what it answers). Adding one changes the script, so update the connector then. The script comes with one sample case, `refresh_status`, that moves a job on by 25 points each time it runs, so the status card's Refresh button visibly works. Replace it with a call to your own system.

The cards in this project use these actions:

| Action | Kind | What it does |
|--------|------|--------------|
| `submit_decision` | Records a result | Records the user's approval decision. Call it only after the user has clearly said approve or reject in chat. Do not call it if their message already contains a confirmation ID. The approval card also calls this when its buttons are used. |
| `submit_form` | Records a result | Records the values a user entered in a form. Call it only after the user has given the values in chat. Do not call it if their message already contains a confirmation ID. The form card also calls this when it is submitted. |
| `refresh_status` | Read-only | Gets the latest status of a job. Call it only when the user asks for an update. The status card also calls this from its Refresh button. |

## Rules

Because the card passes through the model, the connector decides what a card may do. These rules are in `CardRules` in `script.csx`; to change one, edit it there and update the connector.

| Rule | Setting |
|------|---------|
| Actions a card's buttons may run | Any well-formed action name, except `show_card` and `run_action`. What an action does is decided by `RunAction` in the script, not by the card. |
| Hosts a card may link to | `learn.microsoft.com` |
| Where images and media may load from | Nowhere. The host blocks them, so a card can't load an image or video from another site. |
| Only the project's cards | No. Any card that passes the other rules is shown. |
| Longest card | 24000 characters |

A card's own `csp` and `sample` are dropped. Links are checked wherever they appear in the card, and again when a user selects one.

**Rules version: `26ed3b5e`.** It is also in the first lines of `script.csx`. It changes only when the script does: a change to the link, image, or size settings above, or your own cases in `RunAction`. Adding, changing, or removing a card, or a new action in a card, never changes it. If you change the script, update the connector.

## Where cards render

| Surface | Renders the card? |
|---------|-------------------|
| Microsoft Copilot declarative agents | Yes |
| Copilot Studio agents | Yes, in the Copilot Studio preview chat, as of [Use MCP apps in Copilot Studio](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/mcp-apps). Not in Teams or web chat. |
| Any client that does not support MCP Apps | No. Every tool result carries a plain-text version, so the conversation still works. |

## Prerequisites

- A Power Platform environment where you can create custom connectors
- A Copilot Studio agent that supports skills ([Add and manage an uploaded skill](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/skills-add-existing), preview) in an environment and tenant where MCP Apps rendering is available
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`)

## Set up

1. In `apiDefinition.swagger.json`, replace `your-api-host.com` with your own host name for the connector. The script handles the MCP request itself, so the host does not need to serve anything.
2. In `apiProperties.json`, set `publisher` to your name.
3. Deploy the connector:

   ```powershell
   pac connector create `
       --api-definition-file apiDefinition.swagger.json `
       --api-properties-file apiProperties.json `
       --script-file script.csx `
       --environment <YOUR_ENVIRONMENT_ID>
   ```

4. In Copilot Studio, open your agent, go to **Tools**, select **Add a tool**, choose **Connector**, and pick **Power MCP Cards**. Add all the tools.
5. Open the agent's **Build** tab, select **Skills**, then **Add skill**, then **Upload a skill**, and upload each `SKILL.md` from the `skills` folder. (A declarative agent takes a `.zip` with the `SKILL.md` at its root.)
6. Ask the agent for something a skill covers, and check the card appears in the Copilot Studio preview chat.

## Change a card

All the cards are in one skill. In Copilot Studio, open the skill and download it, edit the file, then replace the skill with the edited file. Replacing the skill replaces every card in it, so keep a copy first. The connector does not change.

## Security

- The agent writes the `show_card` arguments, and what it writes can be influenced by what a user types. The rules above are the protection: they are enforced by the connector, not requested of the agent.
- A card's buttons run only when the user selects them. A button can run any well-formed action name, so the script's `RunAction` is what limits them: give a real case only to actions you mean a card to trigger, and check what a button sends before you trust it.
- The connector has no authentication. Add it before you put real data behind it.
- Form values are sent to the agent as a chat message. Do not collect secrets in a form card.

## Limits

- The agent copies the card JSON on every call. That costs tokens and the copy can be wrong. Test your cards in the agent, and keep each card small.
- Copilot Studio recommends skills under 3,000 characters for agents built on the Customer Conversation harness; larger skills work but use more of the conversation's context.
- Hosts support different parts of MCP Apps. Copilot Studio provides only theme, language and region, and display mode as host context, and doesn't support tool cancellation, partial tool input, file downloads, or MCP logging. See [Use MCP apps in Copilot Studio](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/mcp-apps).
- MCP cards are an alternative to [Adaptive Cards](https://learn.microsoft.com/adaptive-cards/) with limits, not a replacement. Adaptive Cards render in Teams, Outlook, and web chat, which MCP Apps do not.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| The agent says the card was not shown | The connector refused it, and the message says which rule. Fix the card or the rule. |
| The card was changed and is refused as "not one of the approved cards" | The agent altered the JSON. Tighten the skill ("exactly as written"), or shorten the card. |
| The agent writes its own card | Make the skill's description match the request more closely, and keep the instruction not to write card JSON. |
| The agent answers in text and no card appears | You're testing in a surface that doesn't render MCP Apps. Cards render in the Copilot Studio preview chat and in Microsoft Copilot, not in Teams or web chat. |
| The agent says it has no `show_card` tool | The agent keeps the tool list from when you added the connector. Remove the connector's tools from the agent and add them again. |

# Power MCP Cards Template

Interactive cards for your agent, delivered as [MCP Apps](https://modelcontextprotocol.io/extensions/apps/overview) from a Power Platform custom connector script. Use it where you would otherwise build an Adaptive Card: approvals, forms, tables, and job status.

One connector serves the MCP endpoint, the tools, and the card views. The script hosts the HTML for each card as a `ui://` resource, so there is no web app, storage account, or CDN to deploy.

Built on the [Power MCP Template](../Power%20MCP%20Template/). For a larger set of reference views, see the [Power MCP Apps Template](../Power%20MCP%20Apps%20Template/).

## Design

The cards follow [Fluent 2](https://fluent2.microsoft.design/), the design system Copilot Studio and Microsoft 365 Copilot use, and the [UX guidelines for MCP apps in Copilot](https://learn.microsoft.com/microsoft-365/copilot/extensibility/plugin-mcp-apps-ui-guidelines).

| Area | What the cards do |
|------|-------------------|
| Tokens | Fluent web tokens for color, type (Segoe UI, 14 px body, 16 px semibold title, 12 px captions), radius (4 px controls, 8 px card), spacing (4, 8, 12, 16, 24), and 24 px card padding, in light and dark. When the host sends `--color-text-primary`, `--color-background-primary`, `--color-border-primary`, or `--font-sans`, the card uses them. |
| Components | Fluent-style Button (primary and secondary, 32 px), Input, Select, Textarea, Checkbox, MessageBar, Badge, ProgressBar, Table, and Skeleton. No external fonts or icon files; the status, sort, and expand icons are inline SVG from the Fluent System Icons. |
| Actions | At most two per inline card, at the bottom, with the primary action first. Approval has Approve and Reject. Form has Submit. Status has Refresh. Table has Expand and Send selection. |
| Density | Inline cards are previews. The table shows five rows and no pagination, filters, or internal scrolling. Search, sorting, and the full list open in the expanded (full-screen) view. |
| State | Every action shows a loading state (spinner in the button, other controls disabled), a success message, and an error message with the action available again to retry. The first paint is a skeleton, not a blank frame. |
| Accessibility | Labels on every control, `aria-live` regions for messages, `aria-invalid` on fields, `aria-sort` on table headers, a visible focus ring, and reduced-motion support. |
| Chat | The cards don't repeat chat features. They send the agent a message and leave the reply to the model. |

The table asks the host for full-screen with `ui/request-display-mode`. Microsoft 365 Copilot supports full-screen only. If a host refuses, the table opens its expanded layout inline, with a height limit, so search and sorting still work.

## Where cards render

| Surface | Renders the card? |
|---------|-------------------|
| Microsoft 365 Copilot declarative agents | Yes |
| Copilot Studio agents | Yes, in the Copilot Studio preview chat, as of [Use MCP apps in Copilot Studio](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/mcp-apps) (November 16, 2026). Not in Teams or web chat. |
| Any client that does not support MCP Apps | No. The tool result carries a plain-text version, so the conversation still works. |

Every `show_*` tool returns a text summary alongside the card data. The text tells the agent that the card is shown, to keep its reply short and not repeat the card, and to fall back to chat only if the user says the card didn't appear: present the details, collect the answer, and record it with the `submit_*` tool. So the card is the primary experience, and the conversation still completes on a host that can't render it.

## Adaptive Cards compared

| | Adaptive Cards | MCP Apps cards |
|---|---|---|
| Layout and styling | Fixed element set, host-controlled | Full HTML and CSS, themed from the host |
| Inputs | `Input.*` elements, limited validation | Any HTML input, your own validation |
| Submit | `Action.Submit` posts back to the agent | The card calls a tool on your server, then sends the agent a message |
| Sorting, filtering, paging | Not available | Preview inline; sort and search in the expanded table |
| Refresh in place | Not available | The status card calls a tool and redraws |
| Where it is defined | JSON the agent or flow builds | HTML your script serves, filled from tool data |
| Host support | Broad | Growing; see the table above |

## Cards and tools

| Tool | Shown to the model | What it does |
|------|--------------------|--------------|
| `show_approval` | Yes | Approve and Reject buttons, request details, optional comment, optional required comment on reject |
| `show_form` | Yes | Text, email, number, date, multi-line, choice, and checkbox fields with required-field and format validation |
| `show_table` | Yes | Inline preview of the first rows. Expand to search, sort, and see every row (up to 500), and optionally select rows to send back. |
| `show_status` | Yes | Status badge, progress bar, and step list, with an optional Refresh button |
| `submit_decision` | Yes | Called by the approval card to record the decision, or by the agent when the user answers in chat |
| `submit_form` | Yes | Called by the form card to record the values, or by the agent when the user gives them in chat |
| `refresh_status` | Yes | Called by the status card to fetch the latest status, or by the agent when the user asks for an update |

The `submit_*` and `refresh_status` tools are listed to the model so the connector works on hosts that can't draw cards. Their descriptions tell the model to skip the call when the card already recorded the answer. To hide them from the model on a host that honors `_meta.ui.visibility`, set `AppToolsVisibleToModel` to `false` in `script.csx`.

## How a card works

1. The agent calls a `show_*` tool. The tool declares `_meta.ui.resourceUri`, which points at the card's `ui://` view.
2. The host reads the view from the connector with `resources/read` and renders it in a sandboxed frame.
3. The host sends the tool result to the view. The view draws itself from `structuredContent`.
4. When the user acts, the view calls the matching card-only tool and then sends the agent a message describing what the user did. The agent continues from that message.

If the host can't let a card call tools, the card skips step 4's tool call and the message asks the agent to record the result instead. See Troubleshooting.

## Prerequisites

- A Power Platform environment where you can create custom connectors
- A Copilot Studio agent in an environment and tenant where MCP Apps rendering is available
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) (`pac`)

## Set up

1. Copy this folder.
2. In `apiDefinition.swagger.json`, replace `your-api-host.com` with your own host name for the connector. The script handles the MCP request itself, so the host does not need to serve anything.
3. In `apiProperties.json`, set `publisher` to your name.
4. Deploy the connector:

   ```powershell
   pac connector create `
       --api-definition-file apiDefinition.swagger.json `
       --api-properties-file apiProperties.json `
       --script-file script.csx `
       --environment <YOUR_ENVIRONMENT_ID>
   ```

5. In Copilot Studio, open your agent, go to **Tools**, select **Add a tool**, choose **Connector**, and pick **Power MCP Cards**.
6. Add all the tools, including the `submit_*` and `refresh_status` tools.
7. Ask the agent something that needs a card, such as "Ask me to approve purchase order PO-7731 for $12,400."

## Make it yours

Each card-only tool has a `TODO` where the script echoes its input. Replace it with a call to your own system, using `SendExternalRequestAsync` in the script:

| Tool | Replace the TODO with |
|------|-----------------------|
| `submit_decision` | A call that records the approval in your approval system |
| `submit_form` | A call that saves or forwards the submitted values |
| `refresh_status` | A lookup of the job's current state. The sample advances progress by 25 each time, so you can see the card redraw. |

To add a card:

1. Add its HTML as a new method in `CardViews`. Start from the closest existing card. Use the `start(name, sample, render)` helper, which connects to the host and calls `render` with the tool's `structuredContent`. Reuse the shared pieces: the `msg`, `icon`, and `setBusy` helpers and the Fluent classes in `BaseCss` (`.card`, `.btn`, `.msg`, `.badge`, `.cap`, `.actions`). Keep to two actions.
2. Register it in `RegisterCardViews` with `AddUiResource`.
3. Add a tool in `RegisterCardTools` with `uiResourceUri` set to the view's URI. Return `CardResult(fallbackText, data)`.

To test a view without a host, save the HTML that `resources/read` returns and open it in a browser. A view that is not inside a host frame draws its built-in sample data.

### Design cards without writing HTML

[Power MCP Cards](../Power%20MCP%20Cards/) describes a card as JSON (text, forms, tables, charts, images, icons, tabs, and more), checks it, and generates a connector script like this one. Its Fluent 2 runtime draws the cards, so you don't write HTML or CSS. Use it when you want more than these four cards. Its [language reference](../Power%20MCP%20Cards/core/README.md) lists every element.

## Converting an Adaptive Card

| Adaptive Card | MCP Apps card |
|---------------|---------------|
| `Input.Text`, `Input.Number`, `Input.Date`, `Input.ChoiceSet`, `Input.Toggle` | A `show_form` field of type `text`, `number`, `date`, `choice`, `checkbox` |
| `Action.Submit` with a decision | `show_approval`, or a new card that calls its own tool |
| `FactSet` | The `facts` rows on `show_approval` |
| `Table` or repeated `ColumnSet` | `show_table` |
| `ProgressBar` with status text | `show_status` |
| `Action.OpenUrl` | `App.openLink(url)` in a card's script, which asks the host to open the link |

[Power MCP Cards](../Power%20MCP%20Cards/) can import an Adaptive Card as JSON and report what carried over, what changed, and what was left out.

## Security

- The views declare no content security policy, so the host applies its restrictive default: no network access and no external scripts or fonts. The views load nothing from a CDN.
- Data from tool results reaches the page through `textContent`. Nothing is written with `innerHTML`.
- Form values and table selections are sent to the agent as a chat message. Do not collect secrets in a form card.
- Add authentication to the connector before you put real approvals behind it.

## Limits

- The connector is request and response. It cannot push updates to a card, so the status card refreshes only when the user selects Refresh.
- Hosts support different parts of MCP Apps. These cards use `ui/initialize`, tool input and results, `tools/call`, `ui/message`, `ui/open-link`, `ui/update-model-context`, display-mode requests (full-screen), and size changes. Copilot Studio's table lists each of these as supported except display modes, which it supports with limits (inline, and full-screen in the side pane). It provides only theme, language and region, and display mode as host context, so the cards use their built-in Fluent colors there instead of host style variables. The table also lists what it doesn't support: tool cancellation, partial tool input, file downloads, and MCP logging. See [Use MCP apps in Copilot Studio](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/mcp-apps). Microsoft 365 Copilot's supported set is listed in [MCP apps in Microsoft 365 Copilot](https://learn.microsoft.com/microsoft-365/copilot/extensibility/plugin-mcp-apps).
- The Fluent colors are the published web token values for the Copilot theme. If Copilot Studio ships different values, the card follows the host variables listed under Design; update `BaseCss` for anything else.
- Table data travels in the tool result, so keep it to what a person will read. The tool rejects more than 500 rows.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| The agent answers in text and no card appears | You're testing in a surface that doesn't render MCP Apps. Cards render in the Copilot Studio preview chat and in Microsoft 365 Copilot, not in Teams or web chat. If you're in the preview chat, check that the tool result still arrives and that the `ui://` resource read isn't failing. A resource-read failure hides the card without changing the tool result. |
| The card says it needs a host that supports MCP Apps | The host did not answer `ui/initialize`. The card is running somewhere other than an MCP Apps host. |
| The card shows after Approve, but nothing is recorded and the agent is told to call `submit_decision` | The host does not let cards call tools. The card then asks the agent to record it, and the agent calls `submit_decision` itself. Make sure `AppToolsVisibleToModel` is `true`. |
| The agent calls `submit_decision` or `refresh_status` before the user has answered | Tighten the connector's `Instructions` and the tool descriptions in `script.csx`, or set `AppToolsVisibleToModel` to `false` if your host renders cards and honors visibility. |
| The agent says to use buttons on a card, but no card appears | The host isn't rendering MCP Apps. The tool results tell the agent to offer the chat fallback if the card doesn't appear; reply that you don't see it and it will take your answer in chat. |
| The agent says it has no `submit_decision` tool | The agent keeps the tool list from when you added the connector. Remove the connector's tools from the agent and add them again so it picks up `submit_decision`, `submit_form`, and `refresh_status`. |
| A tool is missing in Copilot Studio | Copilot Studio drops tools whose schema has `$ref` or multi-type arrays. Keep custom schemas flat, as the existing tools do. |
| You updated the connector, but the card or tool list looks old | The script caches tool lists for five minutes and card HTML for one minute (`ListCacheTtlMs` and `ResourceCacheTtlMs`). Wait, then reload the card. If the tool list is still old, remove the connector's tools from the agent and add them again. |
| The card is the wrong height or has a scrollbar | The host sets the frame height from the card's size notifications. Reload the card, and keep custom cards free of fixed heights. |
| Expand does nothing, or the table opens in place with a scrollbar | The host doesn't offer full-screen. The table falls back to an inline expanded layout. |

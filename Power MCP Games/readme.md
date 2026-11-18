# Power MCP Games

Four playable games for a Copilot Studio agent, delivered as [MCP Apps](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/mcp-apps): Snake, 2048, Minesweeper, and Tetris. Ask the agent to play one and the game appears in the conversation.

One connector serves the MCP endpoint, the tools, and the game views. The script hosts the HTML for each game as a `ui://` resource, so there is no web app, storage account, or CDN to deploy. It is a companion to [Power MCP Cards Template](../Connector-Code/Power%20MCP%20Cards%20Template/) and shares its MCP framework, Fluent styles, and host bridge.

## Games and tools

| Tool | Game | Argument | Ways to play |
|------|------|----------|--------------|
| `play_snake` | Steer the snake to the food without hitting a wall or yourself. | `speed`: `slow`, `normal`, `fast` | Arrow keys or WASD, swipe, on-screen arrows |
| `play_2048` | Slide tiles so matching numbers merge, up to 2048. | `gridSize`: 3 to 6 (default 4) | Arrow keys or WASD, swipe, on-screen arrows |
| `play_minesweeper` | Reveal every safe cell. Your first click is always safe. | `difficulty`: `easy` (9×9, 10 mines), `medium` (16×16, 40), `hard` (30×16, 99) | Click to reveal, right-click or flag mode to flag |
| `play_tetris` | Clear lines with falling blocks. A ghost piece shows where the block will land. | `level`: 1 to 20 (default 1) | Arrow keys, space to drop, `P` to pause, on-screen buttons |

Every tool returns a plain-text note alongside the game's settings. The note tells the agent that the game is shown, to keep its reply to one short sentence, and what to say if the user can't see the game. So a client that can't render MCP Apps still gets a sensible reply, and the agent doesn't try to play the game in text.

## How the games work

Each game follows the same pattern:

- A card with the game title, its score, the board, and two actions: a primary button and **Expand**. The primary button is **Start**, **Pause**, **Resume**, or **Restart** for Snake and Tetris, and **New game** for 2048 and Minesweeper. Minesweeper also has a smiley button that starts a new game.
- **More than one way to play.** A card in a sandboxed frame might never receive keyboard focus, so every game also works with on-screen buttons, and the board takes focus when you click it. Snake and 2048 also respond to swipes. Minesweeper has a flag-mode toggle for touch screens, where there is no right-click.
- **Pausing.** Snake and Tetris pause when the card is hidden or loses focus, so a game doesn't end while you type in the chat. A paused board shows **PAUSED**. The card shows no banners, so it keeps the same size. 2048 has one line of instruction text under its title. Results appear in the game itself: Snake and Tetris draw **GAME OVER** and the score on the board, 2048 shows an overlay on the board with **Try again** (or **Keep going** after you reach 2048), and Minesweeper's smiley changes face. A hidden announcement tells screen readers the result.
- **Expand.** The button asks the host for full screen and the board gets larger. If the host refuses, the button hides itself.
- **Theme.** Each game sits in its own frame and has its own look, drawn from a classic reference. Snake is a Nokia 3310 with a green LCD. 2048 is cream and orange with a yellow logo tile, inside a phone bezel. Minesweeper is a Windows 95 window with a mine counter, a smiley button, and a timer. Tetris is a Super NES-style screen: a purple starfield in a gold frame, silver-bordered panels, and lavender buttons, with Tetris Guideline piece colors and beveled blocks on a black board. The colors are fixed, so the games look the same in light and dark themes.
- **Reporting the result.** When a game ends, the card tells the agent with `ui/update-model-context`, for example "The user finished a game of Snake with a score of 12." The agent can congratulate the player or offer another round. The connector keeps no scores itself.

## Where games render

| Surface | Renders the game? |
|---------|-------------------|
| Copilot Studio preview chat | Yes. All four games have been played there. See [Use MCP apps in Copilot Studio](https://learn.microsoft.com/microsoft-copilot-studio/agents-experience/mcp-apps). |
| Microsoft 365 Copilot | Yes. |
| Teams and web chat | No. |
| Any client that does not support MCP Apps | No. The tool result carries a plain-text note instead. |

## Prerequisites

- A Power Platform environment where you can create custom connectors
- A Copilot Studio agent in a tenant where MCP Apps rendering is available
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

5. In Copilot Studio, open your agent, go to **Tools**, select **Add a tool**, choose **Connector**, and pick **Power MCP Games**.
6. Add all four tools.
7. Open the preview chat and try a prompt from the next section.

## Try it

| Prompt | What you should see |
|--------|---------------------|
| "Let's play Snake." | A board with a three-segment snake and a Start button. Pressing Start, an arrow key, or a swipe begins the game. |
| "Play Snake on fast." | The same game at a faster speed. |
| "Start a game of 2048 on a 5 by 5 grid." | A 5×5 board with two tiles. |
| "Play hard Minesweeper." | A 30×16 board with 99 mines. Use Expand for more room. |
| "Play Tetris starting at level 5." | A 10×20 board, a next-piece preview, and controls beside it. |
| After a game ends: "How did I do?" | The agent knows your score from the result the card reported. |

Click the board before you use the keyboard, so the card has focus.

## Make it yours

To add a game:

1. Add a `ui://` URI constant and register the view in `RegisterGameViews` with `handler.AddUiResource(...)`.
2. Add a `play_*` tool in `RegisterGameTools`. Return `GameResult(text, data)`, where `data` holds the settings the game needs. Keep the schema flat, with no `$ref` and no multi-type arrays, because Copilot Studio drops tools that use them. Clamp numeric arguments in the handler with `GetInt` instead of putting bounds in the schema.
3. Add a view method to `GameViews`, with a CSS constant and a JavaScript constant.
4. In the JavaScript, call `gameShell({ title, board, stats, controls })`. It builds the card and returns a small kit. The config also takes optional `cls` (a class added to the card, for per-game styling), `intro` (a line of text under the header), and `mid` (an element placed between the first and second stat), and `screen` (set to `true` to put the stats and board together in one bordered screen, as Snake does):
   - `kit.stat(id, value)` updates a score.
   - `kit.status(intent, title, body)` shows a banner message, or clears it when `intent` is `null`. The built-in games don't use it.
   - `kit.announce(text)` tells screen readers a result without changing the layout.
   - `kit.label(text)` and `kit.onPrimary(fn)` set up the primary button.
   - `kit.onKey`, `kit.onSwipe`, and `kit.onAway` (card hidden or unfocused) wire up input.
   - `kit.onStop(fn)` clears timers when the card is rebuilt.
   - `kit.report(text, data)` tells the agent the result.
5. End the script with `start('Name', sampleData, yourFunction, ['inline', 'fullscreen'])`. The sample data lets the page run on its own in a browser for development.

The JavaScript lives in C# verbatim strings, so use single quotes only. Show data with `textContent`, never `innerHTML`.

## Security

- The views declare no content security policy, so the host applies its restrictive default: no network access and no external scripts. The games load nothing from a CDN.
- Games run entirely in the card. The connector keeps no state and stores no scores. It makes no outbound calls unless you set `APP_INSIGHTS_CONNECTION_STRING` in `script.csx`, which turns on optional Application Insights telemetry. It is empty by default.
- Add authentication to the connector before you expose it beyond a test environment.

## Limits

- The connector is request and response. It can't push updates to a card, and it doesn't keep scores between games.
- Games run in the card, not on the server, so a high score lasts only as long as the card is open.
- Hosts support different parts of MCP Apps. These games use `ui/initialize`, tool results, `ui/update-model-context`, display-mode requests, and size changes. Copilot Studio provides only theme, language and region, and display mode as host context, so the games use their built-in Fluent colors for anything else. It doesn't support tool cancellation, partial tool input, file downloads, or MCP logging.
- Minesweeper's hard board is about 690 pixels wide and scrolls sideways in a narrow card. Use Expand.

## Troubleshooting

| Symptom | Cause and fix |
|---------|---------------|
| The agent answers in text and no game appears | You're in a surface that doesn't render MCP Apps. Games render in the Copilot Studio preview chat and Microsoft 365 Copilot, not in Teams or web chat. If you are in the preview chat, check that the `resources/read` for the game isn't failing, because that hides the card without changing the tool result. |
| The game appears but the keyboard does nothing | The card doesn't have focus. Click the board, or use the on-screen buttons or a swipe. |
| The game paused by itself | Snake and Tetris pause when the card is hidden or loses focus. Select Resume. |
| Right-click doesn't flag a cell | Some hosts block the context menu. Turn on **Flag mode** under the board. |
| Expand does nothing and then disappears | The host doesn't offer full screen. The board stays inline. |
| A tool is missing in Copilot Studio | Copilot Studio drops tools whose schema has `$ref` or multi-type arrays. Keep custom schemas flat. |
| The agent says it can't see a tool you added | The agent keeps the tool list from when you added the connector. Remove the connector's tools from the agent and add them again. |
| The card shows "needs a host that supports MCP Apps" | The host didn't answer `ui/initialize`. The card is running somewhere other than an MCP Apps host. |

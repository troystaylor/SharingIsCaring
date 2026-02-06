# Power Agent Tray

A lightweight Electron system tray application that exposes Copilot Studio agents via the [Model Context Protocol (MCP)](https://modelcontextprotocol.io). Runs quietly in the background, providing AI agent capabilities to any MCP-compatible client.

## Features

- **System tray integration** — runs unobtrusively in the taskbar
- **MCP server** — exposes Copilot Studio agents as MCP tools (stdio transport)
- **MSAL authentication** — PKCE OAuth flow for secure Entra ID login
- **Secure token storage** — OS-level credential persistence via `@azure/msal-node-extensions` (DPAPI on Windows)
- **File-based logging** — rotating log files in the app data directory
- **Auto-start** — optional Windows startup registration from the tray menu
- **Dual mode** — run as a tray app or as a headless MCP stdio server (`--stdio`)

## Prerequisites

- Node.js 20+
- npm 10+
- Azure Entra ID app registration (with redirect URI `http://localhost:3847/auth/callback`)
- Copilot Studio agent (published, with Direct Connect URL)

## Azure App Registration

1. Go to [Azure Portal](https://portal.azure.com) → **App registrations** → your app
2. Navigate to **Authentication** → **Add a platform** → **Mobile and desktop applications**
3. Add a custom redirect URI: `http://localhost:3847/auth/callback`
4. Save

This redirect URI is the same for all users — the app runs a temporary local server on port 3847 during login.

## Setup

```bash
npm install
```

Copy `.env.example` to `.env` and fill in your values:

```env
CLIENT_ID=your-azure-app-client-id
TENANT_ID=your-azure-tenant-id
DIRECT_CONNECT_URL=https://your-environment.api.powerplatform.com/copilotstudio/...
```

Get the **Direct Connect URL** from Copilot Studio → Channels → Web app.

## Build & Run

```bash
npm run build       # Compile TypeScript
npm run start       # Build and launch tray app
npm run dev         # Build and launch (development)
npm run pack        # Build unpacked distributable
npm run dist        # Build installer (.exe)
```

## MCP Client Configuration

To connect from an MCP client (VS Code, Claude Desktop, etc.), add to your MCP config:

```json
{
  "mcpServers": {
    "power-agent-tray": {
      "type": "stdio",
      "command": "<path-to>/node_modules/electron/dist/electron.exe",
      "args": ["dist/main.js", "--stdio"],
      "cwd": "<path-to>/Power Agent Tray"
    }
  }
}
```

> **Tip:** Use the **Copy VS Code MCP config** option in the tray menu to copy a ready-to-paste config with correct paths.

### Available MCP Tools

| Tool | Description |
|---|---|
| `chat_with_agent` | Send a message and get a response from the Copilot Studio agent |
| `start_conversation` | Begin a new conversation session |
| `end_conversation` | End the current conversation |
| `get_auth_status` | Check authentication state and current user |
| `login` | Trigger browser-based PKCE login |
| `logout` | Sign out and clear cached tokens |

## Architecture

```
Power Agent Tray/
├── src/
│   ├── main.ts            # Electron main process, tray menu, entry point
│   ├── auth-service.ts    # MSAL PKCE auth + persistent token cache
│   ├── agent-client.ts    # Copilot Studio client wrapper
│   ├── mcp-server.ts      # MCP protocol server (tool definitions)
│   ├── logger.ts          # Rotating file logger
│   └── ui/
│       ├── chat-app.html  # MCP Apps interactive UI template
│       └── chat-app.ts    # MCP Apps client for rendering responses
├── assets/
│   └── electron.ico       # Tray icon
├── .env.example           # Environment variable template
├── mcp-config.json        # Sample MCP client configuration
├── vite.config.ts         # Bundles UI into single HTML file
├── package.json
└── tsconfig.json
```

## Tray Menu

When running as a tray app, the context menu provides:

- **Agent name** — shows the configured Copilot Studio agent
- **Sign in / Sign out** — manage authentication
- **Start with Windows** — toggle auto-start on login
- **Open log folder** — view rotating log files
- **Copy VS Code MCP config** — copy a ready-to-paste MCP server entry to clipboard
- **Quit** — exit the application

## License

MIT

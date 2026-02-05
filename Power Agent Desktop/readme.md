# Power Agent Desktop

A native Windows desktop client for Microsoft Copilot Studio agents with voice-enabled AI interactions. Built with Electron and the M365 Agents SDK, it provides a rich conversational experience with adaptive cards, speech-to-text input, and text-to-speech responses. Supports the Model Context Protocol (MCP) for integration with other local AI clients and can be packaged as MSIX for enterprise deployment.

![App Screenshot](docs/images/app-screenshot.png)

## Features

- ✅ **Fluent 2 UI Design** - Native Windows look with light/dark theme toggle
- ✅ **M365 Agents SDK** - Official Microsoft SDK for Copilot Studio
- ✅ **PKCE Authentication** - Secure authorization code flow with token persistence
- ✅ **MCP Protocol** - Expose agent to other local clients
- ✅ **MCP Apps** - Render interactive UI components from MCP servers in sandboxed iframes
- ✅ **Adaptive Cards** - Rich UI rendering (Microsoft SDK v3.0.5)
- ✅ **Voice I/O** - Azure Speech SDK for speech-to-text and text-to-speech
- ✅ **Smart Mic Selection** - Automatically prefers raw microphones over virtual audio devices
- ✅ **Popup Auth** - Secure interactive authentication
- ✅ **Markdown Rendering** - Full markdown support with inline citations
- ✅ **Conversation History** - IndexedDB persistence with export
- ✅ **Multiple Agents** - Switch between different Copilot Studio agents
- ✅ **Dynamic Agent Name** - Automatically retrieves and displays agent name from Copilot Studio
- ✅ **System Tray** - Minimize to tray with quick actions
- ✅ **Auto-Update** - electron-updater for seamless updates
- ✅ **Accessibility** - WCAG 2.0 compliant with screen reader support, keyboard navigation, and high contrast mode
- ✅ **Settings Panel** - Keyboard shortcuts, app settings, and MCP connection info with `Alt+S` toggle
- ✅ **Keyboard Shortcuts** - Quick access: `Alt+M` (mic), `Alt+T` (TTS), `Alt+W` (wake word), `Alt+S` (settings)

## Accessibility

Built to [Microsoft Accessibility Guidelines](https://learn.microsoft.com/en-us/style-guide/accessibility/accessibility-guidelines-requirements) and [Inclusive Design](https://inclusive.microsoft.design/) principles.

### Keyboard Navigation
- **Skip Link** - Press Tab on page load to skip to message input
- **Escape Key** - Close settings panel
- **Tab Navigation** - Full keyboard access to all controls
- **Focus Indicators** - Visible 2px outline on all focused elements

### Keyboard Shortcuts
| Shortcut | Action |
|----------|--------|
| `Alt+M` | Toggle microphone on/off |
| `Alt+T` | Toggle text-to-speech on/off |
| `Alt+S` | Open/close settings panel |
| `Alt+W` | Toggle wake word listening |
| `Escape` | Close settings panel |

### Screen Reader Support
- **ARIA Landmarks** - Semantic regions (banner, main, navigation, contentinfo)
- **ARIA Labels** - Descriptive labels on all buttons and controls
- **Live Regions** - Real-time announcements for status changes (aria-live="polite")
- **Toggle States** - aria-pressed on mic, TTS, and theme buttons
- **Dialog Support** - Settings panel with aria-expanded and focus management

### Visual Accessibility
- **High Contrast Mode** - Full support for Windows forced-colors mode
- **Color + Text** - Status uses both color dot AND text description
- **Link Underlines** - Links use both color and underline (redundant visual cues)
- **Reduced Motion** - Respects prefers-reduced-motion system setting

### Input Flexibility
- **Voice Input** - Speech-to-text with Azure Speech SDK
- **Wake Word** - "Hey Copilot" hands-free activation
- **Text-to-Speech** - Agent responses read aloud

## Settings Panel

Access settings via the gear icon in the header or press `Alt+S`.

### Keyboard Shortcuts
Displays the available keyboard shortcuts with their current key bindings.

### App Settings
| Setting | Description | Default |
|---------|-------------|---------|
| Auto-save Conversation | Automatically save conversations to history | On |
| Force Login on Start | Require authentication on every app launch | Off |
| Enable App Logging | Log app events to console for debugging | Off |

### MCP Connection Info
Shows the current MCP server connection details:
- **Host**: Server address
- **Port**: Connection port
- **Status**: Connection state (Connected/Disconnected)

## Quick Start

### 1. Prerequisites

```powershell
# Node.js 18+ required
node --version

# Optional: Install WinApp CLI for MSIX packaging
winget install Microsoft.WinAppCli
```

### 2. Configure Environment

Copy the template and fill in your values:

```powershell
cd src
cp .env.example .env
```

Then edit `src/.env` with your configuration:

```env
# ===== Copilot Studio Connection =====
# Option 1: Direct Connect URL (from Copilot Studio > Channels > Native app)
# directConnectUrl=https://YOUR_REGION.api.powerplatform.com/...

# Option 2: Manual Configuration (from Settings > Advanced > Metadata)
# environmentId=your-environment-id
# schemaName=your-agent-schema-name

# ===== Azure Entra ID App Registration =====
AZURE_TENANT_ID=your-tenant-id
AZURE_CLIENT_ID=your-client-id
# COPILOT_AUTH_METHOD=entraId

# ===== Voice I/O (Optional) =====
# AZURE_SPEECH_RESOURCE_ID=/subscriptions/xxx/resourceGroups/xxx/providers/Microsoft.CognitiveServices/accounts/xxx
# AZURE_SPEECH_REGION=eastus

# ===== Settings =====
# COPILOT_REQUIRE_SIGNIN=true
# COPILOT_USE_LOCAL_AI=true
```

### 3. Install & Build

```powershell
cd src

# Install dependencies
npm install

# Build all (MCP server + Electron)
npm run build:all
```

### 4. Run

```powershell
npm run start:desktop
```

The app will:
1. Launch the Electron window
2. Start the MCP server as a child process
3. Open authentication popup for Microsoft sign-in
4. Auto-start conversation once authenticated

---

## Azure App Registration Setup

### 1. Create App Registration

1. Go to [Azure Portal](https://portal.azure.com) > **App registrations** > **New registration**
2. Name: `Power Agent Desktop`
3. Supported account types: **Single tenant** (your org only)
4. Redirect URI: **Public client/native** > `http://localhost`

### 2. Configure API Permissions

Add these permissions:

| API | Permission | Type |
|-----|------------|------|
| Microsoft Graph | `User.Read` | Delegated |
| Power Platform API | `CopilotStudio.Copilots.Invoke` | Delegated |
| Microsoft Cognitive Services | `user_impersonation` | Delegated |

Grant admin consent for your organization.

### 3. Enable Public Client

**Authentication** > **Advanced settings** > **Allow public client flows** > **Yes**

### 4. Copy IDs

- **Application (client) ID** → `appClientId`
- **Directory (tenant) ID** → `tenantId`

---

## Authentication

This app uses **PKCE authorization code flow** with a popup window:

1. App opens a secure Electron window for Microsoft login
2. User signs in with their Microsoft account
3. Auth code is exchanged for tokens via PKCE
4. Tokens are stored in localStorage with refresh token
5. Subsequent launches use cached tokens (auto-refresh when expired)

### OAuth Scopes Requested

The app requests tokens for different resources separately (Azure AD requires this):

**Initial Auth (Copilot Studio)**
| Scope | Purpose |
|-------|---------|
| `https://api.powerplatform.com/CopilotStudio.Copilots.Invoke` | Invoke Copilot Studio agents |
| `openid` | OpenID Connect authentication |
| `profile` | User profile information (name) |
| `email` | User email address |
| `offline_access` | Refresh token for silent token renewal |

**On-demand (via refresh token)**
| Scope | Purpose |
|-------|---------|
| `https://graph.microsoft.com/User.Read` | Fetch user profile photo |
| `https://cognitiveservices.azure.com/.default` | Azure Speech TTS/STT |

### Environment Variables

```env
# Required
appClientId=your-client-id
tenantId=your-tenant-id

# Connection (choose one)
directConnectUrl=connection-string-from-copilot-studio
# OR
environmentId=env-id
schemaName=schema-name

# Settings
COPILOT_REQUIRE_SIGNIN=true
COPILOT_USE_LOCAL_AI=true
```

---

## Voice I/O

Voice input and text-to-speech are powered by **Azure Speech SDK**.

### Features

- 🎤 **Speech-to-Text**: Click the microphone button to dictate messages
- 🗣️ **Wake Word**: Say "Hey Copilot" to activate voice input hands-free
- 🔊 **Text-to-Speech**: Toggle TTS to hear agent responses read aloud
- 🔐 **Azure Speech Auth**: Uses Entra ID auth tokens for Speech SDK
- 🎛️ **Smart Mic Selection**: Automatically prefers raw microphones over virtual audio devices (Elgato Wave Link, VB-Audio, etc.)

### Setup

1. **Create Azure Speech Resource**
   - Go to [Azure Portal](https://portal.azure.com) > Create a resource > "Speech"
   - Choose a region (e.g., `eastus`)
   - Note the **Resource ID** (from Overview > JSON View)

2. **Add API Permissions**
   In your Azure App Registration, add:
   | API | Permission | Type | Purpose |
   |-----|------------|------|---------|
   | Microsoft Power Platform | `CopilotStudio.Copilots.Invoke` | Delegated | Copilot Studio agent communication |
   | Azure Cognitive Services | `user_impersonation` | Delegated | Speech-to-text and text-to-speech |
   | Dynamics CRM | `user_impersonation` | Delegated | Dataverse access (agent icon, metadata) |
   | Microsoft Graph | `User.Read` | Delegated | User profile and photo |

3. **Assign RBAC Role**
   The signed-in user needs the **Cognitive Services Speech User** role on the Speech resource:
   ```bash
   az role assignment create \
     --assignee "user@example.com" \
     --role "Cognitive Services Speech User" \
     --scope "/subscriptions/xxx/resourceGroups/xxx/providers/Microsoft.CognitiveServices/accounts/your-speech-resource"
   ```

4. **Configure Environment**
   ```env
   AZURE_SPEECH_RESOURCE_ID=/subscriptions/xxx/resourceGroups/xxx/providers/Microsoft.CognitiveServices/accounts/your-speech-resource
   AZURE_SPEECH_REGION=eastus
   ```

### Usage

- **Mic Button**: Click to start voice input, click again to stop (or press `Alt+M`)
- **Wake Word**: Enable in Settings, then say "Hey Copilot" - mic button shows a blue dot when listening
- **TTS Toggle**: Click speaker button to enable/disable speech synthesis (or press `Alt+T`)
- Agent responses are automatically read aloud when TTS is enabled
- Buttons show a slash indicator when disabled (Teams-style)

---

## MCP Tools

The MCP server exposes these tools:

| Tool | Description |
|------|-------------|
| `chat_with_agent` | Send messages to the Copilot Studio agent |
| `get_agent_capabilities` | Query what the agent can do |
| `start_conversation` | Begin a new conversation session (returns agent name) |
| `sign_in` | Initiate authentication flow |
| `clear_credentials` | Sign out and clear cached tokens |
| `get_agent_details` | Fetch agent name/icon from Dataverse |
| `render_adaptive_card` | Render Microsoft Adaptive Cards |
| `render_product_grid` | Display products in a visual card grid |
| `render_widget` | Render custom HTML in a sandboxed widget |

### Agent Name Resolution

The app automatically retrieves the agent's display name from conversation activities. Internal schema names like `tst_computerPartsStoreAssistant` are formatted to readable names like "Computer Parts Store Assistant".

### Use with External MCP Clients

Add to Claude Desktop's `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "power-agent": {
      "command": "node",
      "args": ["C:/path/to/src/dist/mcp-server/index.js"]
    }
  }
}
```

---

## Packaging Options

### Option A: Electron Builder (Recommended)

```powershell
# Build installer
npm run build:installer

# Creates: dist/Power Agent Desktop Setup.exe
```

### Option B: WinApp CLI (MSIX)

Use WinApp CLI when you need:
- **Package Identity** for Windows AI APIs
- **Microsoft Store** distribution
- **Enterprise sideloading** with MSIX

```powershell
# Initialize WinApp
npx winapp init

# Create debug identity for testing
npx winapp create-debug-identity

# Build MSIX package
npx winapp pack
```

---

## Project Structure

```
src/
├── package.json              # Dependencies
├── tsconfig.json             # TypeScript (ESM)
├── tsconfig.desktop.json     # TypeScript (Electron/CommonJS)
├── .env                      # Environment variables
├── mcp-server/
│   └── index.ts              # MCP server entry point
├── agent/
│   ├── copilot-agent.ts      # M365 Agents SDK client
│   └── auth-service.ts       # Device code auth + keytar
├── desktop/
│   ├── main.ts               # Electron main process
│   ├── preload.ts            # Secure IPC bridge
│   ├── index.html            # Fluent UI chat interface
│   └── package.json          # CommonJS config
├── ui/
│   └── agent-ui.ts           # MCP Apps UI components
├── assets/                   # App icons
└── dist/                     # Build output
```

---

## Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Desktop Framework | Electron | 33.x |
| UI Design | Fluent UI Web Components | 2.6.1 |
| Cards Rendering | Microsoft Adaptive Cards SDK | 3.0.5 |
| Agent SDK | @microsoft/agents-copilotstudio-client | 1.2.3 |
| Authentication | Custom PKCE (OAuth 2.0) | - |
| Voice I/O | Azure Speech SDK | latest |
| MCP Protocol | @modelcontextprotocol/sdk | 1.1.0 |
| Auto-Update | electron-updater | 6.x |
| Markdown | marked.js | latest |

---

## Troubleshooting

### "Authentication failed"
- Verify `appClientId` and `tenantId` in `.env`
- Ensure **Power Platform API** permission is granted
- Check that "Allow public client flows" is enabled

### "MCP server not connecting"
- Run `npm run build:all` to rebuild
- Check that `.env` is in the `src/` directory

### "Voice recognition not working"
- If using virtual audio devices (Elgato Wave Link, VB-Audio Cable), the app automatically selects raw microphones
- Check that Azure Speech permissions are granted in your app registration
- Verify `AZURE_SPEECH_RESOURCE_ID` and `AZURE_SPEECH_REGION` in `.env`

### "Adaptive Cards not rendering"
- Cards must include `"type": "AdaptiveCard"` and `"version": "1.5"`
- Check browser console for SDK errors

### Clear cached credentials
```powershell
# Clear tokens from browser localStorage (via DevTools)
# Or sign out from the app settings
```

---

## Learn More

- [Microsoft Adaptive Cards](https://adaptivecards.io/)
- [M365 Agents SDK](https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk)
- [Fluent UI Web Components](https://github.com/microsoft/fluentui)
- [Copilot Studio Documentation](https://learn.microsoft.com/microsoft-copilot-studio/)
- [WinApp CLI](https://github.com/microsoft/WinAppCli)
- [Electron Documentation](https://www.electronjs.org/docs)


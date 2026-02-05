# Power Agent Desktop Architecture

This document outlines how to build a **Windows-native Copilot Studio agent desktop application** that connects to Microsoft's cloud-hosted agents, featuring Fluent UI design, Adaptive Cards rendering, and MCP protocol support. The app can be packaged as a Windows MSIX application using WinApp CLI for enterprise distribution.

## What This Is / What This Isn't

### ✅ What This Architecture Provides

| Capability | Description |
|------------|-------------|
| **Electron Desktop App** | Native Windows desktop experience with Fluent UI design |
| **M365 Agents SDK** | Official Microsoft SDK for Copilot Studio integration |
| **Adaptive Cards** | Rich UI rendering using Microsoft's official Adaptive Cards SDK |
| **Voice I/O** | Azure Speech SDK for speech-to-text and text-to-speech |
| **Wake Word** | "Hey Copilot" hands-free activation with smart mic selection |
| **MCP Integration** | Exposes your agent to MCP-compatible clients |
| **System Tray** | Minimize to tray with quick actions and auto-start |
| **Federated Auth** | PKCE authorization code flow with persistent token storage |
| **Auto-Update** | Seamless updates via electron-updater |
| **Windows-Native Packaging** | MSIX packaging via WinApp CLI for clean install/uninstall |

### ⚠️ Important Clarification

**The Copilot Studio agent logic runs in Microsoft's cloud, not locally.** This desktop app is a **native client** that:
1. Provides a rich chat UI with Fluent UI design
2. Authenticates users via Microsoft Entra ID device code flow
3. Communicates with Copilot Studio via **M365 Agents SDK**
4. Renders responses with Markdown and Adaptive Cards
5. Exposes the agent via MCP protocol for integration with other tools

```
┌─────────────────────┐      ┌─────────────────────┐      ┌─────────────────────┐
│  Electron Desktop   │      │   MCP Server        │      │   Copilot Studio    │
│    Application      │─────▶│  (Child Process)    │─────▶│     (Cloud)         │
│  - Fluent UI        │ IPC  │  - M365 Agents SDK  │ API  │  - Runs AI logic    │
│  - Adaptive Cards   │◀─────│  - Auth Service     │◀─────│  - Stores topics    │
│  - Chat Interface   │      │  - Token Management │      │  - Processes NLP    │
└─────────────────────┘      └─────────────────────┘      └─────────────────────┘
        Local                       Local                      Microsoft Cloud
```

### 🔮 Future Potential

This architecture positions you for future capabilities:
- **Local AI augmentation** via Windows AI APIs (Phi Silica) when Package Identity is available
- **Hybrid processing** - local preprocessing before sending to cloud agent
- **Offline fallback** - cached responses or local LLM when disconnected
- **On-device execution** - if Microsoft enables local Copilot Studio runtime

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                         Electron Desktop Application                            │
│  ┌───────────────────────────────────────────────────────────────────────────┐  │
│  │                              Main Process                                  │  │
│  │  ┌─────────────────┐  ┌────────────────────┐  ┌────────────────────────┐  │  │
│  │  │  Window Manager │  │  MCP Server Child  │  │  Protocol Handlers     │  │  │
│  │  │  (BrowserWindow)│  │  Process Spawner   │  │  (poweragent://)       │  │  │
│  │  └─────────────────┘  └────────────────────┘  └────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────────────────┘  │
│                                      │ IPC                                      │
│  ┌───────────────────────────────────┴───────────────────────────────────────┐  │
│  │                           Renderer Process                                 │  │
│  │  ┌──────────────────────────────────────────────────────────────────────┐ │  │
│  │  │                         index.html (Fluent UI)                        │ │  │
│  │  │  ┌────────────────┐  ┌────────────────┐  ┌─────────────────────────┐ │ │  │
│  │  │  │ Chat Messages  │  │ Quick Actions  │  │ Adaptive Cards Renderer │ │ │  │
│  │  │  │ (Markdown)     │  │ (Dynamic)      │  │ (Microsoft SDK v3.0.5)  │ │ │  │
│  │  │  └────────────────┘  └────────────────┘  └─────────────────────────┘ │ │  │
│  │  │  ┌────────────────┐  ┌────────────────┐  ┌─────────────────────────┐ │ │  │
│  │  │  │ Auth Alert     │  │ Product Cards  │  │ MCP Widget (iframe)     │ │ │  │
│  │  │  │ (Device Code)  │  │ (Grid Layout)  │  │ (Custom HTML)           │ │ │  │
│  │  │  └────────────────┘  └────────────────┘  └─────────────────────────┘ │ │  │
│  │  └──────────────────────────────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────────┘
                                        │
┌───────────────────────────────────────┴───────────────────────────────────────┐
│                            MCP Server (Node.js)                                │
│  ┌──────────────────────┐  ┌──────────────────────┐  ┌─────────────────────┐  │
│  │  M365 Agents SDK     │  │  Authentication      │  │  MCP Tools          │  │
│  │  @microsoft/agents-  │  │  - PKCE Auth Flow    │  │  - chat_with_agent  │  │
│  │  copilotstudio-client│  │  - Token Persistence │  │  - render_card      │  │
│  │                      │  │  - Refresh Tokens    │  │  - render_products  │  │
│  └──────────────────────┘  └──────────────────────┘  └─────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────────┘
                                        │ HTTPS
                                        ▼
┌───────────────────────────────────────────────────────────────────────────────┐
│                              Microsoft Cloud                                   │
│  ┌─────────────────────────────────────────────────────────────────────────┐  │
│  │                         Copilot Studio Agent                             │  │
│  │  • AI/NLP processing        • Topic management                          │  │
│  │  • Knowledge bases          • Power Platform integration                │  │
│  │  • Dataverse connections    • Custom actions/flows                      │  │
│  └─────────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────────┘
```

## Technology Stack

### Core Technologies

| Component | Technology | Version |
|-----------|------------|---------|
| Desktop Framework | Electron | 33.x |
| UI Design | Fluent UI Web Components | 2.6.1 |
| Cards Rendering | Microsoft Adaptive Cards SDK | 3.0.5 |
| Agent SDK | @microsoft/agents-copilotstudio-client | 1.2.3 |
| Voice I/O | Azure Speech SDK | 1.42.x |
| Authentication | Custom PKCE (OAuth 2.0) | - |
| MCP Protocol | @modelcontextprotocol/sdk | 1.1.0 |
| Auto-Update | electron-updater | 6.x |
| Markdown | marked.js | latest |
| Packaging | electron-builder + WinApp CLI | - |

### UI Features

| Feature | Implementation |
|---------|----------------|
| Light/Dark Theme | Fluent UI design tokens with toggle |
| Chat Messages | Markdown rendering with citation links |
| Rich Cards | Microsoft Adaptive Cards SDK v3 |
| Product Grids | Custom card components |
| Custom Widgets | Sandboxed iframes |
| Quick Actions | Dynamic buttons parsed from responses |
| Status Indicator | Connection state with glow effects |
| Voice Input | Mic button + wake word ("Hey Copilot") |
| Text-to-Speech | Toggle for reading agent responses aloud |
| Conversation History | IndexedDB persistence with export (MD/TXT) |
| System Tray | Minimize to tray, quick actions menu |

---

## Integration Approach

The **M365 Agents SDK** is Microsoft's preferred method for integrating with Copilot Studio agents. Direct Line is the legacy approach.

### Integration Methods Comparison

| Method | Recommendation | Features |
|--------|----------------|----------|
| **M365 Agents SDK** | ✅ Preferred | Modern SDK, connection strings, streaming support |
| **Direct Line** | ⚠️ Legacy | Use only where SDK doesn't support your scenario |

See: [Integrate with web or native apps using Microsoft 365 Agents SDK](https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk)

### Getting the Connection String

1. In Copilot Studio, open your agent
2. Go to **Settings** > **Channels** > **Web app** or **Native app**
3. Copy the **connection string** under "Microsoft 365 Agents SDK"
4. Set as `directConnectUrl` in your `.env` file

### Alternative: Manual Configuration

If not using the connection string, get these from **Settings** > **Advanced** > **Metadata**:
- `environmentId`
- `schemaName`
- `tenantId`

### Required API Permissions

Add to your Azure app registration:
1. Go to **API permissions** > **Add permissions**
2. Select **APIs my organization uses** > search "Power Platform API"
3. Add **delegated permission**: `Copilot Studio.Copilots.Invoke`

---

## Authentication Best Practices

Authentication is critical for securing Copilot Studio agents. This section outlines Microsoft's recommended approaches based on [official documentation](https://learn.microsoft.com/microsoft-copilot-studio/configuration-end-user-authentication).

### Authentication Options Overview

| Option | Use Case | SSO Support | Token Access |
|--------|----------|-------------|--------------|
| **No Authentication** | Public FAQ bots only | ❌ | ❌ |
| **Authenticate with Microsoft** | Teams/M365 channels | ✅ (automatic) | ❌ |
| **Authenticate Manually** | Custom apps, MCP servers | ✅ (configurable) | ✅ |

### Recommended: Microsoft Entra ID V2 with Federated Credentials

For MCP server packages, use **Authenticate Manually** with **Microsoft Entra ID V2 with federated credentials** (no secrets to manage):

```
┌──────────────────┐     ┌─────────────────────┐     ┌──────────────────┐
│   MCP Client     │────▶│   MCP Server       │────▶│  Copilot Studio  │
│  (Chat Client)   │     │  (This Package)     │     │     Agent        │
└──────────────────┘     └─────────────────────┘     └──────────────────┘
                                   │
                                   ▼
                         ┌─────────────────────┐
                         │  Microsoft Entra ID │
                         │  (Authentication)   │
                         └─────────────────────┘
```

**Why Federated Credentials?**
- No client secrets to rotate or manage
- Workload identity federation for service-to-service auth
- Reduced risk of credential exposure

### Direct Line Channel Security

When connecting to Copilot Studio via Direct Line API, follow these security best practices:

#### 1. Enable Secured Access
```
Copilot Studio → Settings → Security → Web channel security → Require secured access: ON
```

#### 2. Token vs Secret Usage

| Scenario | Approach |
|----------|----------|
| Server-to-server | Secret in Authorization header (protected backend) |
| Client-facing app | Exchange secret for token server-side |
| Web/mobile client | **Never expose secrets** - use token exchange |

#### 3. Token Exchange Pattern (Recommended)

```typescript
// Server-side: Exchange secret for conversation token
async function getConversationToken(secret: string): Promise<string> {
  const response = await fetch(
    'https://directline.botframework.com/v3/directline/tokens/generate',
    {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${secret}`,
        'Content-Type': 'application/json'
      }
    }
  );
  const data = await response.json();
  return data.token; // Use this token, not the secret
}
```

#### 4. Token Refresh Strategy

Tokens expire (typically 30 minutes). Implement proactive refresh:

```typescript
// Refresh before expiration (e.g., at 80% of lifetime)
const REFRESH_THRESHOLD = 0.8;

async function refreshTokenIfNeeded(token: string, expiresIn: number, elapsed: number) {
  if (elapsed > expiresIn * REFRESH_THRESHOLD) {
    return await refreshToken(token);
  }
  return token;
}
```

### Authentication Architecture

This app uses a two-tier authentication model:

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                          Authentication Layers                                    │
├──────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  Layer 1: Client Authentication (Current - PKCE)                                 │
│  ════════════════════════════════════════════════                                │
│  Desktop App → Entra ID (PKCE) → Copilot Studio API                              │
│  • Authenticates user to invoke the agent                                        │
│  • Scope: Power Platform API (Copilot Studio.Copilots.Invoke)                    │
│  • No secrets in client code                                                     │
│                                                                                  │
│  Layer 2: Knowledge Source Access (Future - Federated Credentials)               │
│  ══════════════════════════════════════════════════════════════════              │
│  Copilot Studio → Azure Bot Token Service → Graph/SharePoint/Dataverse           │
│  • Agent accesses M365 data on behalf of user                                    │
│  • Requires Azure Bot OAuth Connection                                           │
│  • Federated Identity Credential (no shared secrets)                             │
│                                                                                  │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### Single Sign-On (SSO) Configuration

For seamless user experience, configure SSO with Microsoft Entra ID:

#### Prerequisites
1. Enable manual authentication in Copilot Studio
2. Create app registration for your desktop app canvas
3. Define custom scope in Entra ID
4. (Future) Configure token exchange URL for knowledge sources

#### Current SSO Flow (PKCE)

```
User → Desktop App → Entra ID (PKCE) → Access Token → Copilot Studio API
```

#### Future: OBO Flow for Knowledge Sources

When Copilot Studio agent needs to access SharePoint/Graph/Dataverse on the user's behalf:

```
User Token → Copilot Studio → Azure Bot Token Service → OBO Token → Graph API
                                      ↑
                          Federated Identity Credential
                          (configured in Azure Portal)
```

#### Required Scopes

| Data Source | Required Scopes | When Needed |
|-------------|-----------------|-------------|
| Base authentication | `profile openid` | Always |
| Power Platform API | `Copilot Studio.Copilots.Invoke` | Always |
| SharePoint | `Sites.Read.All Files.Read.All` | Knowledge sources |
| Graph Connector | `ExternalItem.Read.All` | Graph connectors |
| Dataverse | `https://[OrgURL]/user_impersonation` | Dataverse access |

### Environment Variables

Store credentials securely using environment variables:

```env
# M365 Agents SDK Connection (preferred)
# Option 1: Connection string from Copilot Studio Channels page
directConnectUrl=https://YOUR_REGION.api.powerplatform.com/...

# Option 2: Individual settings from Settings > Advanced > Metadata
environmentId=your-environment-id
schemaName=your-agent-schema-name

# Entra ID (for user authentication)
appClientId=your-client-id
tenantId=your-tenant-id

# Client secret (if using client credentials flow)
AZURE_CLIENT_SECRET=your-client-secret

# Legacy: Token endpoint (Direct Line fallback)
COPILOT_STUDIO_TOKEN_ENDPOINT=https://...
```

### Security Checklist

- [ ] **Enable secured access** on Direct Line channel
- [ ] **Never expose secrets** in client-side code
- [ ] **Use token exchange** for web/mobile clients
- [ ] **Implement token refresh** before expiration
- [ ] **Store credentials** in environment variables or secure vault
- [ ] **Enable SSO** for seamless user experience
- [ ] **Configure least-privilege scopes** only request needed permissions
- [ ] **Rotate secrets** periodically (Direct Line provides two secrets for zero-downtime rotation)
- [ ] **Grant admin consent** for API permissions to avoid per-user consent prompts

### References

- [Integrate with web or native apps using M365 Agents SDK](https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk) ⭐
- [M365 Agents SDK JavaScript Sample](https://github.com/microsoft/Agents/tree/main/samples/nodejs/copilotstudio-client)
- [Configure user authentication with Microsoft Entra ID](https://learn.microsoft.com/microsoft-copilot-studio/configuration-authentication-azure-ad)
- [Configure single sign-on with Microsoft Entra ID](https://learn.microsoft.com/microsoft-copilot-studio/configure-sso)
- [Configure web and Direct Line channel security](https://learn.microsoft.com/microsoft-copilot-studio/configure-web-security)
- [Direct Line API Authentication](https://learn.microsoft.com/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-authentication)

---

## Implementation Phases

### Phase 1: Core Application ✅
- [x] Create Electron desktop app with Fluent UI design
- [x] Implement MCP server with M365 Agents SDK
- [x] Configure custom PKCE authorization code flow for Electron
- [x] Add persistent token storage with refresh token support

### Phase 2: Rich UI Features ✅
- [x] Markdown rendering for agent responses
- [x] Microsoft Adaptive Cards SDK integration (v3.0.5)
- [x] Dynamic suggested action buttons
- [x] Inline citation links
- [x] Product card grids
- [x] Custom MCP widgets (sandboxed iframes)
- [x] Voice I/O with Azure Speech SDK
- [x] Wake word activation ("Hey Copilot")
- [x] Smart mic selection (prefers raw mics over virtual devices)
- [x] System tray with minimize-to-tray
- [x] Auto-updater (electron-updater)
- [x] Conversation history with IndexedDB
- [x] Export conversations (Markdown/Text)
- [x] Light/dark theme toggle

### Phase 3: WinApp Packaging (Optional)
- Initialize WinApp project (`winapp init`)
- Configure appxmanifest.xml with MCP capabilities
- Generate development certificate
- Add package identity for debugging (`winapp create-debug-identity`)
- Build MSIX package (`winapp pack`)

### Phase 4: Distribution
**Option A: Electron Builder (Current)**
- Build with `npm run build:installer`
- Creates standalone .exe or NSIS installer
- No Package Identity required

**Option B: MSIX via WinApp CLI**
- Provides Package Identity for Windows AI APIs
- Enables Store distribution
- Clean install/uninstall experience

### Phase 5: Federated Credentials & Knowledge Sources (Future)

When your Copilot Studio agent needs to access user data from Microsoft 365 services (SharePoint, Graph, Dataverse), configure Azure Bot User Authorization with Federated Identity Credentials:

**When Federated Credentials are needed:**
- Agent has SharePoint knowledge sources
- Agent queries Microsoft Graph on user's behalf
- Agent accesses Dataverse with user impersonation
- Power Automate flows in agent need delegated permissions

**Setup steps:**
1. Register OAuth Connection on Azure Bot resource
2. Configure Federated Identity Credential in Entra ID app
3. Expose API endpoint (`api://botid-{appId}` for Teams, `api://{appId}` otherwise)
4. Add delegated Graph/SharePoint scopes
5. Configure Token Exchange URL in Copilot Studio

See: [Azure Bot User Authorization with Federated Credentials](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/azure-bot-user-authorization-federated-credentials)

### Phase 6: Advanced Enhancements (Optional)
- Add local AI augmentation via Windows AI APIs
- Implement offline caching/fallback
- Build hybrid cloud/local processing

## Project Structure

```
WinApp Copilot Studio/
├── winapp.yaml                    # WinApp CLI configuration
├── appxmanifest.xml               # Windows package manifest
├── package.json                   # Root package config (build scripts)
├── mcp-config.json                # MCP server configuration
├── readme.md                      # Project documentation
├── .github/
│   └── copilot-instructions.md   # Copilot coding guidelines
├── docs/
│   ├── integration-architecture.md
│   └── images/
├── scripts/
│   └── build-package.ps1          # MSIX packaging script
└── src/
    ├── package.json               # App dependencies
    ├── tsconfig.json              # TypeScript config
    ├── tsconfig.desktop.json      # Electron-specific TS config
    ├── .env                       # Environment variables (git-ignored)
    ├── .env.example               # Environment template
    ├── agent/
    │   ├── copilot-agent.ts       # M365 Agents SDK client
    │   └── auth-service.ts        # Device code auth + keytar
    ├── desktop/
    │   ├── main.ts                # Electron main process
    │   ├── preload.ts             # Secure IPC bridge
    │   ├── index.html             # Fluent UI chat interface
    │   └── package.json           # CommonJS config for Electron
    ├── mcp-server/
    │   └── index.ts               # MCP server entry point
    ├── ui/
    │   └── agent-ui.ts            # MCP Apps UI components
    ├── assets/                    # App icons
    ├── certs/                     # Code signing certificates
    └── dist/                      # Build output (git-ignored)
```

## MCP Tools Available

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
| `render_mcp_app` | Render interactive MCP App UI in sandboxed iframe |

## MCP Apps Support

This app implements the [MCP Apps protocol](https://github.com/modelcontextprotocol/ext-apps) to render interactive UI components from MCP servers.

### How It Works

```
MCP Server (Tool Call)           Electron Host              Sandboxed Iframe
        │                              │                           │
        │  render_mcp_app({           │                           │
        │    name: "My App",          │                           │
        │    html: "<div>...</div>",  │                           │
        │    data: {...}              │                           │
        │  })                         │                           │
        │ ──────────────────────────► │                           │
        │                              │ Create blob URL           │
        │                              │ ─────────────────────────►│
        │                              │                           │
        │                              │◄──── mcpApp.ready() ─────│
        │                              │                           │
        │                              │◄─ mcpApp.callTool(...) ──│
        │◄─────── Tool Result ─────────│                           │
        │                              │── Tool Result ───────────►│
```

### MCP App Bridge API

Inside an MCP App iframe, the `window.mcpApp` object provides:

```javascript
// Get initial data passed to the app
const data = mcpApp.getData();

// Call an MCP tool and get result
const result = await mcpApp.callTool('chat_with_agent', { message: 'Hello' });

// Request to close this app
mcpApp.close();
```

### Security

- Apps run in sandboxed iframes with `allow-scripts allow-forms allow-popups allow-same-origin`
- CSP restricts script sources to self, blob URLs, and trusted CDNs
- Fluent UI theme tokens are injected for consistent styling
- Tool calls are proxied through the Electron host

## Development Commands

```powershell
# Install dependencies
npm install

# Build all (MCP server + Electron)
npm run build:all

# Start desktop app (development)
npm run start:desktop

# Build installer (production)
npm run build:installer
```

## Next Steps

1. **Clone** the repository
2. **Configure** `.env` with your Copilot Studio connection
3. **Install** dependencies: `npm install`
4. **Build**: `npm run build:all`
5. **Run**: `npm run start:desktop`
6. **Authenticate** via device code flow
7. **Package** with electron-builder or WinApp CLI

## References

- [WinApp CLI Documentation](https://github.com/microsoft/WinAppCli/blob/main/docs/usage.md)
- [Microsoft Adaptive Cards](https://github.com/microsoft/AdaptiveCards)
- [M365 Agents SDK](https://github.com/microsoft/Agents)
- [MCP Apps Protocol](https://github.com/modelcontextprotocol/ext-apps)
- [Fluent UI Web Components](https://github.com/microsoft/fluentui)
- [Copilot Studio Documentation](https://learn.microsoft.com/microsoft-copilot-studio/)
- [Electron Documentation](https://www.electronjs.org/docs)

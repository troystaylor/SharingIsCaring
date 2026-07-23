# Copilot Studio Agent in Salesforce — Lightning Web Component

Surface a Microsoft Copilot Studio agent inside Salesforce using a Lightning Web Component, the [M365 Agents SDK](https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk), and an Azure Function middleware. No Einstein Bots license required.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Salesforce Lightning Experience                                │
│                                                                 │
│  ┌─────────────────────┐    ┌──────────────────────────────┐    │
│  │  copilotChat LWC    │    │  CopilotStudioController     │    │
│  │  (Chat UI)          │──▶│  (Apex server-side proxy)    │    │
│  │                     │    │                              │    │
│  │  OAuth popup ─ ─ ─ ─│─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐            │    │
│  └─────────────────────┘    └──────┬───────────────────────┘    │
└─────────────────────────────────────┼──────────┼────────────────┘
                                      │ HTTPS    │
                                      ▼          ▼
┌─────────────────────────────────────────────────────────────────┐
│  Azure Functions Middleware (Node.js 20)                        │
│                                                                 │
│  GET  /api/auth/login     → Entra ID sign-in redirect           │
│  GET  /api/auth/callback  → Exchange code, create session  ◀ ┘ │
│  POST /api/conversations  → Start conversation (delegated)      │
│  POST /api/conversations/{id}/activities → Send message         │
│  DELETE /api/conversations/{id}          → End conversation     │
│                                                                 │
│  ┌───────────────────────────┐                                  │
│  │ @azure/msal-node          │ ── Delegated OAuth2 code flow    │
│  │ @microsoft/agents-        │                                  │
│  │   copilotstudio-client    │ ── Direct Connect (SSE)          │
│  └───────────────────────────┘                                  │
└──────────────────────────────┬──────────────────────────────────┘
                               │ Authenticated Direct Connect
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│  Copilot Studio Agent (Power Platform)                          │
│  Authenticated endpoint via M365 Agents SDK                     │
│  User's delegated token → CopilotStudio.Copilots.Invoke         │
└─────────────────────────────────────────────────────────────────┘
```

### Authentication Flow

1. LWC opens a popup to the middleware's `/api/auth/login` endpoint
2. Middleware redirects to Entra ID for interactive user sign-in
3. After sign-in, Entra ID redirects back to `/api/auth/callback` with an authorization code
4. Middleware exchanges the code for a **delegated** access token (via MSAL) and stores it in an in-memory session
5. Callback page posts the session ID back to the LWC via `window.postMessage`
6. All subsequent Apex → middleware calls include the session ID in an `X-Auth-Session` header
7. Middleware uses the user's delegated token when calling the Copilot Studio Direct Connect API

> **Why delegated auth?** The M365 Agents SDK's authenticated endpoint requires a **user-delegated** token with the `CopilotStudio.Copilots.Invoke` scope. Service principal (S2S) tokens are not supported.

The Azure Function middleware is **required** — the M365 Agents SDK runs on Node.js and cannot execute in Apex or the browser. It handles the OAuth2 authorization code flow, manages auth + conversation sessions, and communicates with Copilot Studio using the official SDK. Apex acts as a lightweight relay, forwarding requests with the function key and auth session header.

## Prerequisites

- **Salesforce**: Enterprise Edition or higher with Lightning Experience enabled
- **Microsoft**: Copilot Studio license with a published agent
- **Azure**: Azure subscription for the Function App + Entra ID app registration
- **Dev Tools**: Salesforce CLI (`sf`) and Azure Functions Core Tools (`func`) installed locally

## Setup Instructions

### Quick Start (scripted)

Two PowerShell helpers in [scripts/](scripts/) automate the two most error-prone steps — the Entra ID app registration and the Azure Function deployment. They require **Azure CLI** (`az login`) and **Azure Functions Core Tools** (`func`).

```powershell
# 1. Register the Entra ID app (creates app, adds the delegated permission,
#    grants admin consent, sets the redirect URI, and creates a secret)
$app = ./scripts/Register-EntraApp.ps1 -FunctionAppName my-copilot-middleware

# 2. Provision + deploy the middleware (creates the Function App, sets app
#    settings and CORS, publishes the code, and prints the host key)
./scripts/Deploy-Middleware.ps1 `
    -ResourceGroup rg-copilot `
    -FunctionAppName my-copilot-middleware `
    -TenantId $app.TenantId -ClientId $app.ClientId -ClientSecret $app.ClientSecret `
    -DirectConnectUrl '<from-copilot-studio-channels-native-app>' `
    -SalesforceOrgDomains 'https://YOUR-ORG.my.salesforce.com','https://YOUR-ORG.lightning.force.com'
```

Then complete the Salesforce side — steps **4** (deploy source), **5** (Custom Metadata: paste the URL + host key the script prints), **6** (Remote Site), and **7** (add the component) below. The steps below also document the equivalent manual portal flow if you prefer not to use the scripts.

### 1. Register an Entra ID Application

> Skip this if you ran `scripts/Register-EntraApp.ps1`.

1. In [Azure Portal](https://portal.azure.com/) → **App registrations** → **New registration**
2. Name: `Copilot Studio Salesforce Middleware`
3. Account type: **Single tenant**
4. Redirect URI: **Web** → `https://<your-function-app>.azurewebsites.net/api/auth/callback`
5. Under **API permissions**, add **Power Platform API** → **Delegated** → `CopilotStudio.Copilots.Invoke`
6. Click **Grant admin consent**
7. Under **Authentication** → **Implicit grant and hybrid flows**, enable **ID tokens**
8. Under **Certificates & secrets**, create a client secret
9. Note the **Application (client) ID**, **Directory (tenant) ID**, and **client secret value**

### 2. Get the Direct Connect URL

1. Open your agent in [Copilot Studio](https://copilotstudio.microsoft.com)
2. Go to **Channels** → **Native app**
3. Copy the **Direct Connect URL**
4. Ensure the agent is published

### 3. Deploy the Azure Function Middleware

> Skip this if you ran `scripts/Deploy-Middleware.ps1` — it provisions the Function App, sets these variables, configures CORS, and publishes the code for you. Use the steps below for **local development** or a manual deploy.

```bash
cd streaming-middleware
cp local.settings.json.example local.settings.json
# Fill in:
#   AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET
#   COPILOT_DIRECT_CONNECT_URL
#   MIDDLEWARE_BASE_URL  (e.g., http://localhost:7071 or your deployed URL)
npm install
npm start    # http://localhost:7071
```

Environment variables required:

| Variable | Description |
|---|---|
| `AZURE_TENANT_ID` | Entra ID tenant ID |
| `AZURE_CLIENT_ID` | App registration client ID |
| `AZURE_CLIENT_SECRET` | App registration client secret |
| `COPILOT_DIRECT_CONNECT_URL` | Direct Connect URL from Copilot Studio |
| `MIDDLEWARE_BASE_URL` | The public URL of the deployed Function App |

For Azure deployment, see [streaming-middleware/README.md](streaming-middleware/README.md).

### 4. Deploy to Salesforce

```bash
# Authenticate to your Salesforce org
sf org login web --alias myorg

# Deploy the source
sf project deploy start --target-org myorg
```

### 5. Configure the Middleware Endpoint

After deployment, set the Azure Function URL:

1. In Salesforce **Setup**, search for **Custom Metadata Types**
2. Click **Manage Records** next to **Copilot Studio Settings**
3. Edit the **Default** record
4. Set **Token Endpoint** to your Azure Function URL
   (e.g., `https://my-copilot-middleware.azurewebsites.net`)
5. Set **Function Key** to your Azure Function's host key
   (found in Azure Portal → Function App → App keys → default)
6. Save

### 6. Update the Remote Site Setting

1. In **Setup** → **Remote Site Settings**, find **StreamingMiddleware**
2. Update the URL to your deployed Azure Function domain
3. Ensure it is **Active**

### 7. Add the Component to a Lightning Page

**Option A — Utility Bar (recommended for org-wide access):**

1. **Setup** → **App Manager** → Edit your Lightning App
2. Go to **Utility Items (Desktop Only)**
3. Click **Add Utility Item** → select **copilotChat**
4. Set the panel height (recommended: 520px) and width (380px)
5. Enable **Start automatically**
6. Save

**Option B — Lightning Record / Home / App Page:**

1. Open **Lightning App Builder**
2. Edit (or create) the desired page
3. Drag **copilotChat** from the component palette onto the page
4. Save and activate

## Project Structure

```
force-app/main/default/
├── classes/
│   ├── CopilotStudioController.cls            # Apex proxy for Agent Middleware
│   ├── CopilotStudioController.cls-meta.xml
│   ├── CopilotStudioControllerTest.cls        # Apex test class (HttpCalloutMock)
│   └── CopilotStudioControllerTest.cls-meta.xml
├── cspTrustedSites/
│   ├── AgentMiddleware.cspTrustedSite-meta.xml       # Middleware Function App
│   ├── CopilotStudioToken.cspTrustedSite-meta.xml    # Deprecated (inactive)
│   ├── DirectLineAPI.cspTrustedSite-meta.xml          # Deprecated (inactive)
│   └── EntraIdLogin.cspTrustedSite-meta.xml           # Entra ID OAuth popup
├── customMetadata/
│   └── CopilotStudio_Settings.Default.md-meta.xml
├── lwc/
│   └── copilotChat/
│       ├── copilotChat.html                   # Chat UI with card rendering
│       ├── copilotChat.js                     # Conversation logic (sync)
│       ├── copilotChat.css                    # Fluent 2 (light + dark) styles
│       ├── copilotChat.js-meta.xml            # Component metadata
│       └── cardRenderer.js                    # Adaptive / Hero card parser
├── objects/
│   ├── Copilot_Interaction__c/                 # Custom object for deflection / interaction tracking
│   │   ├── Copilot_Interaction__c.object-meta.xml
│   │   └── fields/ ...
│   ├── Copilot_Message__e/                    # Platform Event (optional, for future streaming)
│   │   ├── Copilot_Message__e.object-meta.xml
│   │   └── fields/ ...
│   └── CopilotStudio_Settings__mdt/
│       ├── CopilotStudio_Settings__mdt.object-meta.xml
│       └── fields/
│           ├── Function_Key__c.field-meta.xml        # Azure Function key
│           ├── Streaming_Endpoint__c.field-meta.xml  # Deprecated
│           └── Token_Endpoint__c.field-meta.xml      # Stores middleware URL
├── permissionsets/
│   └── Copilot_Chat_User.permissionset-meta.xml
└── remoteSiteSettings/
    ├── CopilotStudioToken.remoteSite-meta.xml   # Deprecated (inactive)
    ├── DirectLineAPI.remoteSite-meta.xml         # Deprecated (inactive)
    └── StreamingMiddleware.remoteSite-meta.xml   # Agent Middleware endpoint

streaming-middleware/                              # Azure Function (REQUIRED)
├── src/functions/conversations.js                 # M365 Agents SDK proxy
├── host.json
├── package.json
├── local.settings.json.example
└── README.md

scripts/                                           # PowerShell setup helpers
├── Register-EntraApp.ps1                           # Entra ID app registration
└── Deploy-Middleware.ps1                           # Provision + deploy the Function App
```

## How It Works

1. **Authenticate**: LWC opens a popup to middleware `/api/auth/login` → user signs in at Entra ID → callback exchanges code for delegated token → session ID posted back to LWC via `postMessage`
2. **Start conversation**: LWC calls Apex → Apex POSTs to middleware `/api/conversations` with `X-Auth-Session` header → middleware creates a `CopilotStudioClient` with the user's delegated token, calls `startConversationAsync()`, returns greeting activities + agent name
3. **Send message**: User types → LWC calls Apex → Apex POSTs to middleware `/api/conversations/{id}/activities` with `X-Auth-Session` header → middleware calls `client.sendActivity()`, returns response activities **synchronously**
4. **Markdown → HTML**: Middleware converts markdown (bold, headers, bullets, numbered lists, links) to HTML before returning activities to the LWC
5. **Display response**: LWC processes returned activities — renders HTML via `lightning-formatted-rich-text`, cards, and suggested actions
6. **End conversation**: On component unload → Apex DELETEs to middleware `/api/conversations/{id}` → middleware cleans up session

No polling, no WebSocket management. The middleware handles all M365 Agents SDK complexity and OAuth token lifecycle (including refresh via `offline_access` scope).

## Security Notes

- **No secrets in Salesforce** — Entra ID client credentials live only in the Azure Function's environment variables
- **Delegated auth** — the user signs in interactively; the middleware never holds service principal tokens for Copilot Studio
- **Server-side relay** — all external API calls go through Apex → middleware; no Copilot Studio endpoints are exposed to the browser
- **Function key protection** — conversation and activity endpoints use Azure Functions `authLevel: 'function'`; the key is stored in Salesforce Custom Metadata (`Function_Key__c`)
- **Auth endpoints are anonymous** — `/api/auth/login` and `/api/auth/callback` must be anonymous to allow the browser OAuth redirect flow
- **DLP control** — use Power Platform DLP policies to restrict agent channels to Direct Connect only
- Store the middleware URL and function key in **Custom Metadata Types** (deployed here) or **Named Credentials** — never hardcode

## LWC Features

| Feature | Details |
|---|---|
| **Delegated OAuth** | Popup-based Entra ID sign-in; session persists across conversations |
| **Auto-connect** | Conversation starts after auth; greeting activities arrive immediately |
| **Synchronous responses** | No polling — `sendMessage` returns activities directly from the M365 Agents SDK |
| **Suggested actions** | Copilot Studio quick-reply buttons render as clickable `lightning-button` chips |
| **Record context** | On Record Pages, `recordId` and `objectApiName` are sent as `channelData` |
| **Session persistence** | Auth session ID stored in `sessionStorage`; survives page refreshes without re-login |
| **Reconnect** | Disconnected state shows a Reconnect link; refresh icon starts a new session |
| **Typing indicator** | Animated dots display while waiting for the agent response |
| **Rich text + Markdown** | Middleware converts markdown (bold, lists, headers, links) to HTML; rendered via `lightning-formatted-rich-text` |
| **Adaptive Cards** | Hero Cards, Thumbnail Cards, and Adaptive Cards are parsed and rendered natively |
| **Dark mode** | Automatic OS detection (`prefers-color-scheme`) plus manual toggle; Fluent 2 dark tokens |
| **Fluent 2 styling** | Custom chat surfaces follow [Fluent 2](https://fluent2.microsoft.design/) design tokens |
| **Conversation cleanup** | Component unload sends DELETE to clean up middleware sessions |

## Fluent 2 Alignment

The component's custom CSS uses [Fluent 2 design tokens](https://fluent2.microsoft.design/design-tokens) defined as CSS custom properties on `:host`. SLDS base components (`lightning-card`, `lightning-button`, `lightning-input`, etc.) retain platform styling.

| Token area | Values applied |
|---|---|
| **Brand** | `#0F6CBD` (Brand80) for user bubbles |
| **Neutral backgrounds** | `#FFFFFF` (Background1), `#F5F5F5` (Background3) |
| **Neutral foreground** | `#242424` (Foreground1), `#616161` (Foreground3) |
| **Stroke** | `#E0E0E0` (NeutralStroke2) |
| **Corner radius** | 8px (Large) with 2px (Small) tail on bubbles |
| **Elevation** | shadow4 on bot bubbles — `0 2px 4px rgba(0,0,0,.14)` |
| **Typography** | Segoe UI stack; Body 1 (14/20), Caption 1 (12/16), Caption 2 (10/14) |
| **Spacing** | 4px base grid (size40, size80, size120) |

## Adaptive Card Rendering

The component parses attachment types from M365 Agents SDK activity responses:

| Card Type | Content Type | Rendered Elements |
|---|---|---|
| **Hero Card** | `application/vnd.microsoft.card.hero` | Title, subtitle, body text, images, action buttons |
| **Thumbnail Card** | `application/vnd.microsoft.card.thumbnail` | Same as Hero, compact layout |
| **Adaptive Card** | `application/vnd.microsoft.card.adaptive` | TextBlock, Image, FactSet, ColumnSet, Container, ActionSet |
| **Sign-In Card** | `application/vnd.microsoft.card.signin` | Prompt text + sign-in action button |

Card actions:
- **Action.OpenUrl** — opens a link in a new tab
- **Action.Submit / imBack / postBack** — sends the value as a user message

Adaptive Card body supports one level of nesting inside `ColumnSet` and `Container` elements. The parser (`cardRenderer.js`) sanitizes all image URLs to `https://` only.

## Dark Mode

The component supports two modes:

- **Automatic**: On load, checks `prefers-color-scheme: dark` and applies Fluent 2 dark tokens
- **Manual**: The light-bulb toggle in the header switches between light and dark themes

| Token | Light | Dark |
|---|---|---|
| BrandBackground | `#0F6CBD` | `#115EA3` |
| NeutralBackground1 | `#FFFFFF` | `#292929` |
| NeutralBackground3 | `#F5F5F5` | `#1F1F1F` |
| NeutralForeground1 | `#242424` | `#FFFFFF` |
| NeutralForeground3 | `#616161` | `#ADADAD` |
| NeutralStroke2 | `#E0E0E0` | `#404040` |

## Permission Set

The deployment includes a **Copilot Chat User** permission set that grants access to the `CopilotStudioController` Apex class:

```bash
sf org assign permset --name Copilot_Chat_User --target-org myorg
```

## Passing Salesforce Context

When placed on a Record Page, the component passes the record ID and object name to Copilot Studio as `channelData`. In your Copilot Studio topics, access this via `Activity.ChannelData` to provide context-aware responses.

## Copilot Studio Agent Instructions

A sample `AGENT-INSTRUCTIONS.md` is provided as a starting point for the text to paste into the Copilot Studio agent's **Instructions** field. Adapt it to your own knowledge sources and processes. It covers:

- Markdown formatting rules
- Knowledge sources (Salesforce Knowledge + your product documentation)
- Knowledge article display template
- Channel-aware Salesforce record links (`sf://` scheme)
- Case and query result templates

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Failed to connect to assistant" | Verify token endpoint URL and function key in Custom Metadata; check Remote Site Settings are active |
| Auth popup doesn't appear | Browser may be blocking popups — add the Salesforce domain to popup allow list |
| Auth popup shows error | Verify the Entra ID app's redirect URI matches `https://<func-app>.azurewebsites.net/api/auth/callback` exactly |
| AADSTS54005 "code already redeemed" | Idempotency issue — middleware includes a guard; redeploy if using an older version |
| 401 from `/api/conversations` | Auth session expired or missing — the LWC will re-trigger the OAuth popup automatically |
| 403 from Copilot Studio | Admin consent not granted for `CopilotStudio.Copilots.Invoke` delegated permission |
| No response from agent | Confirm the Copilot Studio agent is published and the Direct Connect URL is correct |
| Component not visible | Ensure meta XML has the correct `<target>` and the page/app is activated |

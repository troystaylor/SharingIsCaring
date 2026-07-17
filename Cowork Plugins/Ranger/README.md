# Ranger

A Scout-alternative Copilot Cowork plugin that provides browser automation, code execution, document generation, persistent memory, M365 integration (OneDrive, Email, Teams, Calendar), internet search, parallel execution, and scheduled automations — all running in isolated Azure Container Apps Sandbox microVMs. Uses existing M365 Copilot credits instead of separate Scout licensing.

## What is this?

Ranger delivers Microsoft Scout-equivalent functionality through the Copilot Cowork ecosystem at a fraction of the cost. It connects to Cowork as a custom MCP plugin using plain JSON-RPC over HTTPS, with OAuth authentication via the Teams Developer Portal.

## Scout Parity

| Scout Feature | Ranger | How |
|---|---|---|
| Browser automation (Playwright) | Done | `tools/browser.js` — 9 Playwright tools |
| Code/shell execution | Done | `tools/code.js` — Python, bash, JS, TS, .NET, R |
| Document creation (Word/Excel/PPT) | Done | `tools/documents.js` via python-docx/openpyxl/python-pptx |
| Persistent memory | Done | `tools/memory.js` — in-memory (Cosmos with private endpoint for prod) |
| Scheduled automations | Done | `tools/automations.js` — in-process cron |
| Email | Done | `tools/m365.js` — Graph sendMail via OBO |
| OneDrive (save/read/list/delete) | Done | `tools/m365.js` — full CRUD via Graph OBO |
| Teams messaging | Done | `tools/m365.js` — Graph channel messages via OBO |
| Calendar (events/free-busy) | Done | `tools/m365.js` — Graph calendarView + events via OBO |
| Internet search | Done | `internet-search` skill — browser-based Bing search |
| Web research | Done | `web-research` skill |
| Parallel execution | Done | `run_parallel` tool — concurrent Promise.all |
| Sub-agents / delegation | Partial | Parallel tool + skills guide orchestration |

## Architecture

```
Copilot Cowork
├── Ranger Plugin (this package)
│   ├── 1 MCP Connector (plain JSON-RPC, OAuth via Teams Dev Portal)
│   └── 9 Skills (orchestration workflows)
└── Work IQ servers (user enables independently, optional)
    ├── Mail, Calendar, Teams
    ├── OneDrive, SharePoint, Word
    └── Skills reference these for additional context
```

## Plugin Structure

```
Ranger/
├── manifest.json                  # Cowork plugin manifest (devPreview, 1 connector, 8 skills)
├── color.png / outline.png        # Icons
├── skills/
│   ├── browse-and-extract/        # Navigate, interact, extract from websites
│   ├── web-research/              # Multi-site research with synthesis
│   ├── execute-code/              # Python/bash/.NET/R code execution (31 pre-installed libs)
│   ├── screenshot-and-pdf/        # Visual captures and PDF export
│   ├── create-document/           # Word, Excel, PowerPoint generation
│   ├── cross-m365-workflow/       # Chain browser+code+M365 actions
│   ├── manage-automations/        # Scheduled task management
│   ├── memory/                    # Persistent key-value storage
│   └── internet-search/           # Browser-based web search via Bing
├── server/
│   ├── index.js                   # Express + plain JSON-RPC MCP handler
│   ├── package.json               # Dependencies
│   ├── Dockerfile                 # Container image (Node 22 slim)
│   ├── widgets/
│   │   └── chart.html             # Inline chart widget (MCP Apps)
│   └── tools/
│       ├── sandbox-client.js      # ACA Sandbox data plane client (MSI auth)
│       ├── browser.js             # 9 Playwright tools
│       ├── code.js                # Code execution (6 languages, auto-import warmup)
│       ├── documents.js           # Word/Excel/PPT generation
│       ├── memory.js              # In-memory store (Cosmos with private endpoint for prod)
│       ├── m365.js                # OneDrive, Email, Teams (Graph OBO)
│       ├── session.js             # Sandbox lifecycle
│       └── automations.js         # Cron scheduler
└── sandbox-images/
    ├── code/Dockerfile            # Custom sandbox image (31 Python packages + R)
    └── setup-sandbox-image.ps1    # Build + register disk image script
```

## Prerequisites

- Azure subscription with ACA Sandboxes enabled (preview)
- Azure Container Registry
- M365 Copilot license (for Cowork access)
- Entra app registration with Graph delegated permissions (Files.ReadWrite.All, Mail.Send, ChannelMessage.Send, Channel.ReadBasic.All, Team.ReadBasic.All)
- OAuth client registration in Teams Developer Portal

## Deployment

### 1. Build and deploy the MCP server

```powershell
cd server

# Build in ACR
az acr build --registry <your-acr> --image ranger-mcp:prod .

# Deploy as Container App
az containerapp create --name ranger-mcp `
    --resource-group <your-rg> `
    --environment <your-env> `
    --image <your-acr>.azurecr.io/ranger-mcp:prod `
    --target-port 8080 --ingress external `
    --min-replicas 1 `
    --system-assigned `
    --env-vars `
        AZURE_SUBSCRIPTION_ID=<sub-id> `
        AZURE_RESOURCE_GROUP=<sandbox-rg> `
        SANDBOX_GROUP_NAME=<sandbox-group> `
        SANDBOX_REGION=<region> `
        CODE_DISK_IMAGE=<disk-image-id> `
        BROWSER_DISK_IMAGE=<browser-disk-image-id> `
        OAUTH_CLIENT_ID=<entra-app-id> `
        ENTRA_CLIENT_SECRET=<entra-secret> `
        ENTRA_AUDIENCE=<app-id-uri>
```

### 2. Assign RBAC roles

```powershell
$principalId = az containerapp show --name ranger-mcp --resource-group <rg> --query "identity.principalId" -o tsv

# Sandbox execution
az role assignment create --assignee $principalId --role "Container Apps SandboxGroup Data Owner" --scope <sandbox-group-resource-id>
az role assignment create --assignee $principalId --role "Azure ContainerApps Session Executor" --scope <sandbox-group-resource-id>
```

### 3. Register OAuth in Teams Developer Portal

1. Go to [dev.teams.microsoft.com/tools](https://dev.teams.microsoft.com/tools) → **OAuth client registration**
2. Fill in: Base URL, Client ID/Secret, Entra auth/token/refresh endpoints, Scope
3. Copy the **registration ID** → use as `referenceId` in manifest.json

### 4. Build sandbox disk image

```powershell
cd sandbox-images
pip install azure-containerapps-sandbox

# Build the code image in ACR
az acr build --registry <your-acr> --image ranger-sandbox-code:latest --file code/Dockerfile code/

# Create disk image via SDK (requires Container Apps SandboxGroup Data Owner role)
python -c "
from azure.identity import DefaultAzureCredential
from azure.containerapps.sandbox import SandboxGroupClient
from azure.containerapps.sandbox._models import RegistryCredentials

client = SandboxGroupClient(
    endpoint='https://management.<region>.azuredevcompute.io',
    subscription_id='<sub-id>',
    resource_group='<sandbox-rg>',
    sandbox_group='<sandbox-group>',
    credential=DefaultAzureCredential()
)

acr_pass = '<acr-password>'  # from: az acr credential show --name <acr> --query passwords[0].value -o tsv
img = client.begin_create_disk_image(
    base_image='<acr>.azurecr.io/ranger-sandbox-code:latest',
    name='ranger-code',
    registry_credentials=RegistryCredentials(username='<acr>', token=acr_pass)
)
result = img.result()
print(f'Disk image ID: {result.id}')
"
```

### 5. Package and upload plugin

```powershell
cd ..  # back to Ranger root
Compress-Archive -Path manifest.json, color.png, outline.png, skills -DestinationPath ranger.zip

# Upload via M365 Admin Center → Manage Apps → Upload custom app
# Or via CLI:
npm install -g @microsoft/m365agentstoolkit-cli
atk auth login
atk install --file-path "./ranger.zip" --scope Personal
```

## Azure Resources

| Resource | Purpose | Cost |
|----------|---------|------|
| Container App (minReplicas: 1) | MCP server | ~$15-30/mo |
| ACA Sandboxes | Per-session compute (browser/code) | Pay-per-use |
| Entra App Registration | OAuth + OBO for Graph | Free |

## Tool Reference (28 tools)

### Browser (9 tools)
`create_browser_session`, `navigate`, `screenshot`, `click`, `fill`, `extract`, `generate_pdf`, `run_playwright_script`, `get_console_log`

### Code (4 tools)
`execute_code`, `upload_file`, `download_artifact`, `list_files`

### Documents (3 tools)
`create_word_doc`, `create_excel`, `create_powerpoint`

### Memory (3 tools)
`save_memory`, `recall_memory`, `list_memories`

### M365 — OneDrive (4 tools)
`save_to_onedrive`, `read_onedrive_file`, `list_onedrive_folder`, `delete_onedrive_file`

### M365 — Email & Teams (2 tools)
`send_email`, `create_teams_message`

### M365 — Calendar (4 tools)
`list_calendar_events`, `create_calendar_event`, `find_free_busy`, `delete_calendar_event`

### Parallel Execution (1 tool)
`run_parallel` — executes multiple tool calls concurrently via Promise.all

### Session (2 tools)
`destroy_session`, `get_session_status`

## Pre-installed Python Libraries (31 packages)

**Document formats:** python-docx, openpyxl, xlsxwriter, xlrd, python-pptx, pypdf, reportlab, extract-msg, olefile, mammoth, icalendar, vobject

**Data & analysis:** pandas, numpy, matplotlib, pillow, pydantic

**Productivity:** jinja2, python-dateutil, qrcode, humanize, tabulate, cryptography

**Web & parsing:** requests, beautifulsoup4, lxml, feedparser, pyyaml

**MCP & developer:** mcp, httpx, fastapi, uvicorn, jsonschema

**R:** tidyverse, jsonlite, openxlsx

## How It Compares to Scout

| | Microsoft Scout | Ranger |
|---|---|---|
| **Form factor** | Desktop app (Windows/macOS) | Cowork plugin (web, Teams, anywhere) |
| **Licensing** | Separate Scout license (Frontier) | M365 Copilot credits (already purchased) |
| **Browser** | Local Playwright | Cloud Playwright in ACA Sandboxes |
| **Code execution** | Local shell | Isolated microVMs (safer, 31 pre-installed libs) |
| **M365 access** | Built-in | Graph OBO (OneDrive, Email, Teams, Calendar) |
| **Calendar** | Built-in | Graph calendarView + events + free/busy |
| **Internet search** | Built-in | Browser-based Bing search (no API key) |
| **Memory** | Built-in | In-memory / Cosmos DB |
| **Automations** | Heartbeat + scheduled | In-process cron (24/7, no desktop needed) |
| **Delegation** | Sub-agents (parallel) | `run_parallel` (concurrent Promise.all) |
| **File access** | Local filesystem | OneDrive full CRUD via Graph (cloud, any device) |
| **Governance** | Admin via Intune | M365 Admin Center + Defender |
| **Multi-user** | Single desktop user | Any M365 Copilot user in the org |
| **MCP protocol** | StreamableHTTP (SSE) | Plain JSON-RPC (required for Cowork) |

## Key Technical Insights

1. **Cowork requires plain JSON-RPC** — the MCP SDK's `StreamableHTTPServerTransport` returns SSE which Cowork can parse for discovery but won't inject tools from. Must implement a raw JSON-RPC handler.
2. **OAuth client registration** (not SSO) in Teams Dev Portal is required for `tools/call` to work.
3. **devPreview schema** with no `mcpToolDescription` enables dynamic tool discovery.
4. **OBO with explicit scopes** — using `.default` only returns `User.Read`; must list individual scopes.
5. **readOnlyHint: true** on tools skips the user confirmation prompt in Cowork.
6. **Disk images** are created via the `azure-containerapps-sandbox` Python SDK with `registry_credentials`.

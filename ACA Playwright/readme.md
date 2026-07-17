# ACA Playwright

Automate browser interactions in isolated Azure Container Apps (ACA) Sandbox microVMs. This dual-mode connector exposes Playwright + Chromium capabilities through both typed Power Automate operations and an MCP endpoint for Copilot Studio.

Each session runs in a fresh sandbox with full egress (required for web browsing), persistent browser state across operations, and auto-suspend after 10 minutes idle.

## Prerequisites

- Azure subscription with ACA Dynamic Sessions enabled
- Entra ID app registration with `Sessions.ReadWrite.All` permission on the Azure ContainerApps Sessions resource (`2c7dd73f-7a21-485b-b97d-a2508fa152c3`)
- Admin consent granted for the app
- An ACA Sandbox Group with a registered Playwright disk image
- Azure Container Registry (ACR) with the Playwright container image

## Disk Image Setup

### 1. Build the Container Image

The included `Dockerfile` builds the Playwright sandbox image. Build it in ACR using remote build (no local Docker needed):

```powershell
az acr build `
    --registry <your-acr-name> `
    --resource-group <your-rg> `
    --image playwright-sandbox:latest `
    --file Dockerfile .
```

The image includes Ubuntu 22.04 + Node.js 22 + Playwright + Chromium (~2.4 GB).

### 2. Register as a Disk Image

ACA Sandboxes use microVM disk images, not container images directly. Register the container image as a disk image via the data plane API:

```powershell
$token = (az account get-access-token --resource "https://dynamicsessions.io" --query accessToken -o tsv)
$headers = @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" }
$body = @{
    image = @{ base = "<acr-name>.azurecr.io/playwright-sandbox:latest" }
    labels = @{ name = "aca-playwright" }
    registryCredentials = @{
        username = "<acr-admin-username>"
        token = "<acr-admin-password>"
    }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method PUT `
    -Uri "https://management.<region>.azuredevcompute.io/subscriptions/<sub>/resourceGroups/<rg>/sandboxGroups/<group>/diskImages" `
    -Headers $headers -Body $body
```

The response includes an `id` (GUID) — use this as `ACA_DISK_IMAGE` in `script.csx`. The image state transitions from Building to Ready.

## Operations

| Operation | Description |
|-----------|-------------|
| **Create Browser Session** | Boot a sandbox with custom viewport, user agent, locale, and HAR recording |
| **Navigate to URL** | Navigate with configurable wait conditions (load, DOMContentLoaded, networkidle) |
| **Take Screenshot** | Full page or element screenshot as PNG/JPEG |
| **Click Element** | Click by CSS, XPath, or Playwright locator with optional post-click wait |
| **Fill Form Fields** | Fill multiple inputs by selector/value pairs |
| **Extract Content** | Extract text, HTML, or attributes from elements |
| **Run Playwright Script** | Execute arbitrary Playwright JavaScript with full API access |
| **Generate PDF** | Export page as PDF with paper format, orientation, and background options |
| **Get Console Log** | Retrieve browser console messages from the session |
| **Download Artifact** | Download any file from the sandbox workspace |
| **Destroy Session** | Tear down sandbox and free resources |

## How It Works

Each operation generates a self-contained Node.js script that:

1. Launches Chromium headless with `ignoreHTTPSErrors: true`
2. Loads saved browser state (cookies, localStorage) from `/workspace/.browser-state.json`
3. Restores the last visited URL from `/workspace/.last-url.txt`
4. Performs the requested action
5. Saves browser state and URL back to disk
6. Closes the browser

Scripts are executed via the ACA Sandbox `executeShellCommand` API with `NODE_PATH=/usr/lib/node_modules` set so globally installed Playwright resolves correctly.

## Configuration

Update the constants in `script.csx`:

```csharp
private const string ACA_SUBSCRIPTION_ID = "your-subscription-id";
private const string ACA_RESOURCE_GROUP = "your-resource-group";
private const string ACA_SANDBOX_GROUP = "your-sandbox-group";
private const string ACA_REGION = "westus2";
private const string ACA_DATA_PLANE_BASE = "https://management.westus2.azuredevcompute.io";
private const string ACA_DISK_IMAGE = "your-disk-image-guid";
```

## Deployment

PAC CLI 2.8.1 has a bug where `pac connector create/update` with OAuth `connectionParameters` in apiProperties.json throws an unexpected error. Deploy in two steps:

1. **Deploy with stripped props** (omit OAuth connectionParameters):

```powershell
# Create a minimal apiProperties without OAuth for initial deploy
pac connector create `
    -df "apiDefinition.swagger.json" `
    -pf "apiProperties.json" `
    -sf "script.csx" `
    -env <environment-id>
```

2. **Configure OAuth in the portal**: Custom Connectors > ACA Playwright > Edit > Security tab:
   - Authentication: OAuth 2.0
   - Identity Provider: Azure Active Directory
   - Client ID: your Entra app client ID
   - Client Secret: your Entra app secret
   - Resource URL: `https://dynamicsessions.io`
   - Login URL: `https://login.microsoftonline.com`
   - Tenant ID: `common`

For updates after initial deploy:

```powershell
pac connector update `
    -id <connector-id> `
    -df "apiDefinition.swagger.json" `
    -sf "script.csx" `
    -env <environment-id>
```

## MCP Integration

The `/mcp` endpoint exposes all tools via MCP Streamable 1.0 for Copilot Studio. Add the connector as an action in your agent and it will discover all 11 tools automatically.

## Example: Copilot Studio Agent Workflow

```
User: "Take a screenshot of https://example.com"

Agent calls navigate(url="https://example.com")
Agent calls screenshot(session_id="abc123", full_page=true)
Agent returns the screenshot image to the user
Agent calls destroy_session(session_id="abc123")
```

## Resource Recommendations

| Use Case | CPU | Memory |
|----------|-----|--------|
| Simple screenshots/extraction | 2000m | 4096Mi |
| Complex SPAs, heavy JavaScript | 4000m | 8192Mi |

## Known Limitations

- Browser launches fresh on each operation (~1-2s overhead per call)
- No video recording (would require persistent process)
- Chromium only (Firefox/WebKit not pre-installed to save image size)
- Maximum session idle time before auto-suspend: configurable, default 10 minutes
- Sandbox Chromium does not trust system CA certs — `ignoreHTTPSErrors: true` is set on all browser contexts

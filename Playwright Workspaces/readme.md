# Playwright Workspaces

Full browser automation powered by Playwright Chromium sessions. Create sessions, navigate, click, type, scrape data, take screenshots, and automate multi-step web workflows.

This is a dual-mode connector:
- MCP endpoint for Copilot Studio agents
- Typed REST operations with full schemas for Power Automate flows

## What This Repo Contains

- Custom connector definition (`apiDefinition.swagger.json`, `apiProperties.json`, `script.csx`)
- Broker service (`broker-service/`) that executes browser operations
- Infra scaffolding (`broker-service/infra/main.bicep`)

## Architecture

```text
Power Automate / Copilot Studio
        |
        v
Custom Connector (script.csx)
        |
        v
Broker Service (Express.js on App Service)
        |
        v
Playwright Chromium (Docker container)
```

The broker manages a pool of browser sessions. Sessions auto-expire based on TTL.

## Prerequisites

- Azure subscription
- Azure Container Registry (Basic SKU or higher)
- Azure App Service (Linux, B1 or higher recommended)
- Power Platform environment for connector deployment

## Known Good Versions

Use these versions together for the validated deployment path in this repo:

| Component | Version |
|-----------|---------|
| `playwright-core` | `1.52.0` |
| Playwright Docker base image | `mcr.microsoft.com/playwright:v1.52.0-noble` |
| Docker builder image | `node:20-slim` |
| Azure App Service plan runtime (container host) | `NODE:22-lts` |
| PAC CLI (`paconn`) | `0.0.20` |

Version alignment rules:
- Keep `playwright-core` pinned to the same version family as the Playwright Docker base image tag.
- If you upgrade Playwright image version, upgrade `playwright-core` in `broker-service/package.json` and regenerate lockfile before build.
- Re-run `ppcv` and Azure route smoke tests after any version upgrade.

## Deploy Broker Service

The connector calls a broker service that runs in Azure App Service as a Docker container.

### Runtime Modes

- Remote mode (production): set `PLAYWRIGHT_SERVICE_URL` to provision sessions from Playwright Workspaces.
- Local mode (fallback): if `PLAYWRIGHT_SERVICE_URL` is not set, the broker launches local Chromium inside the container.

### 1. Build and Push Image

```bash
az acr build \
  --registry <your-acr-name> \
  --image pw-workspaces-broker:latest \
  --file "broker-service/Dockerfile" \
  "broker-service"
```

### 2. Create App Service

```bash
az webapp create \
  --name pw-workspaces-broker \
  --resource-group <your-rg> \
  --plan <your-plan> \
  --container-image-name <your-acr>.azurecr.io/pw-workspaces-broker:latest \
  --container-registry-url https://<your-acr>.azurecr.io \
  --container-registry-user <acr-username> \
  --container-registry-password <acr-password>
```

### 3. Configure App Settings

| Setting | Value | Description |
|---------|-------|-------------|
| `API_KEY` | generate a UUID | API key for authenticating requests |
| `MAX_SESSIONS` | `5` | Maximum concurrent browser sessions |
| `DEFAULT_SESSION_TTL_MINUTES` | `15` | Session timeout in minutes |
| `WEBSITES_PORT` | `3000` | Container port |
| `WEBSITES_CONTAINER_START_TIME_LIMIT` | `600` | Startup timeout for container pull |
| `PLAYWRIGHT_SERVICE_URL` | optional | Remote Playwright Workspaces endpoint; if omitted, broker uses local Chromium mode |

Notes:
- Do not set `PLAYWRIGHT_BROWSERS_PATH` when using the Playwright Docker base image.
- Set `PLAYWRIGHT_SERVICE_URL` for production remote-browser operation.

### 4. Verify Broker Health

```bash
curl https://<your-app>.azurewebsites.net/health
```

Expected response:

```json
{"status":"healthy","activeSessions":0,"browserPoolSize":0}
```

## Deploy Connector

Deploy the custom connector to your Power Platform environment:

```bash
paconn create \
  -e <environment-id> \
  --api-def apiDefinition.swagger.json \
  --api-prop apiProperties.json \
  --script script.csx
```

When creating a connection, provide:
- Broker Service URL: `https://<your-app>.azurewebsites.net`
- API Key: `API_KEY` from App Service settings

## Operations

### Session Management

| Operation | Description |
|-----------|-------------|
| Create Browser Session | Provision a browser session and optionally navigate to a URL |
| Get Session Status | Check session details, expiration, and action count |
| Close Session | Terminate a browser session and release resources |

### Browser Actions

| Operation | Description |
|-----------|-------------|
| Navigate to URL | Navigate browser to a new page |
| Click Element | Click an element by CSS selector or visible text |
| Type Text | Type text into an input field |
| Select Option | Select a dropdown option by value or label |
| Fill Form | Fill multiple form fields in one operation |
| Scroll Page | Scroll up, down, or to top/bottom |
| Wait for Element | Wait for an element to appear on the page |
| Evaluate JavaScript | Execute JavaScript in the browser context |

### Data Extraction

| Operation | Description |
|-----------|-------------|
| Get Page Content | Extract page content as text, HTML, or Markdown |
| Get Element Text | Get text or attribute values from matched elements |
| Scrape Data | Extract structured data using CSS selectors and field mappings |
| Take Screenshot | Capture screenshot as base64 PNG/JPEG |

### MCP

| Operation | Description |
|-----------|-------------|
| Invoke Playwright Workspaces MCP | JSON-RPC 2.0 endpoint for Copilot Studio agents |

MCP endpoint notes:
- Uses JSON-RPC 2.0 request envelopes.
- Swagger intentionally defines no explicit body schema on `InvokeMCP`.

## Request Shape Gotchas

- Evaluate JavaScript expects `script` in the request body, not `expression`.
- Fill Form expects `fields` as an object map, for example:

```json
{
  "fields": {
    "[name='custname']": "Zava Corp",
    "[name='custtel']": "555-1234"
  }
}
```

- Scrape Data expects field mappings as selector strings (optionally `selector@attribute`), for example:

```json
{
  "fields": {
    "name": "input[name='custname']@name",
    "type": "input[name='custname']@type"
  }
}
```

## Troubleshooting

### Connector Deployment Errors

- Error: `Ambiguous policy sections defined for policy template 'setheader'`
  - Cause: `setheader` policy conflicts with API key auth handling.
  - Fix: Remove explicit `setheader` policy from `apiProperties.json` and keep API key auth in Swagger `securityDefinitions`.

- Error: `Operation can have only one body parameter` on MCP import
  - Cause: MCP operation includes explicit body schema/parameters while Power Platform injects MCP request structure.
  - Fix: Keep `InvokeMCP` minimal in Swagger: no `parameters` array and no operation-level `consumes`/`produces`.

- Error: `Could not find member 'policyTemplateInstances' on object of type 'ApiDefinition'`
  - Cause: `policyTemplateInstances` misplaced in `apiProperties.json`.
  - Fix: Place `policyTemplateInstances` inside top-level `properties`.

### Broker Runtime Errors

- Error: Chromium `SIGBUS` / launch crash on App Service
  - Cause: Missing system dependencies in built-in runtime images.
  - Fix: Run broker as a container based on `mcr.microsoft.com/playwright` image.

- Error: `Executable doesn't exist at /home/playwright-browsers/...`
  - Cause: `PLAYWRIGHT_BROWSERS_PATH` overrides browser lookup path.
  - Fix: Remove `PLAYWRIGHT_BROWSERS_PATH` when using Playwright Docker images.

- Error: Playwright version mismatch between package and Docker image
  - Cause: `playwright-core` version does not match `mcr.microsoft.com/playwright:vX.Y.Z-*` tag.
  - Fix: Pin `playwright-core` to the same version as Docker image tag.

### PAC CLI Auth Issues

- Error: `Unexpected polling state code_expired`
  - Cause: Device-code login expired before completion.
  - Fix: Re-run `paconn login` and complete device-code authentication immediately.

## File Reference

| File | Description |
|------|-------------|
| `apiDefinition.swagger.json` | OpenAPI 2.0 definition with 16 operations |
| `apiProperties.json` | Connector metadata, connection parameters, and script operations |
| `script.csx` | C# transformation script with MCP protocol handler and App Insights logging |
| `broker-service/` | Express.js broker service (TypeScript) |
| `broker-service/Dockerfile` | Multi-stage build using `mcr.microsoft.com/playwright` base image |
| `broker-service/infra/main.bicep` | Infrastructure-as-code for Azure deployment |

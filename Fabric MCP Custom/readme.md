# Fabric MCP Custom Connector — Setup Guide

## Overview

The Fabric MCP (Model Context Protocol) connector in Copilot Studio may fail with a redirect URI error because the Microsoft-managed app registration doesn't include the Power Platform redirect URI. This guide creates a custom app registration and custom connector as a workaround.

## Prerequisites

- Azure CLI (`az`) installed and signed in as a tenant admin
- Access to [Power Apps](https://make.powerapps.com) maker portal
- Microsoft Fabric capacity (F2+ or trial) with at least one workspace

---

## Step 1: Create the Fabric MCP Service Principal

The Fabric MCP service principal may not exist in your tenant. Create it:

```powershell
az login
az ad sp create --id 3f021233-1542-41f0-8faa-ca8aca37ace4
```

Verify it was created:

```powershell
az ad sp show --id 3f021233-1542-41f0-8faa-ca8aca37ace4 --query "{name:displayName, appId:appId, enabled:accountEnabled}" -o table
```

Expected output:
```
Name                             AppId                                 Enabled
-------------------------------  ------------------------------------  ---------
Fabric Data Agent MCP Connector  3f021233-1542-41f0-8faa-ca8aca37ace4  True
```

> **Note:** If the built-in Fabric MCP connector in Copilot Studio works after this step, you do not need the custom connector below.

---

## Step 2: Create a Custom App Registration

If the built-in connector still fails with a redirect URI error, create your own app registration:

```powershell
# Create the app registration
az ad app create `
  --display-name "Fabric MCP Custom Connector" `
  --sign-in-audience "AzureADMyOrg" `
  --query "appId" -o tsv
```

Save the **App ID** from the output. Then:

```powershell
# Replace <APP_ID> with the App ID from above

# Add Fabric API delegated permission (user_impersonation)
az ad app permission add `
  --id <APP_ID> `
  --api 00000009-0000-0000-c000-000000000000 `
  --api-permissions "f3076109-ca66-412a-be10-d4ee1be95d47=Scope"

# Create a client secret (save the output — it won't be shown again)
az ad app credential reset `
  --id <APP_ID> `
  --display-name "Copilot Studio Connector" `
  --years 1 `
  --query "{appId:appId, secret:password, tenant:tenant}" -o table

# Grant admin consent
az ad app permission grant `
  --id <APP_ID> `
  --api 00000009-0000-0000-c000-000000000000 `
  --scope "user_impersonation"
```

Save these values — you'll need them in the next step:
- **App (Client) ID**
- **Client Secret**
- **Tenant ID**

---

## Step 3: Create the Custom Connector in Power Apps

1. Go to [make.powerapps.com](https://make.powerapps.com) → **Custom connectors** → **+ New** → **Create from blank**
2. Name: `Fabric MCP Custom Connector`

### General tab

| Setting | Value |
|---|---|
| Scheme | HTTPS |
| Host | `api.fabric.microsoft.com` |
| Base URL | `/v1/mcp` |

### Security tab

| Setting | Value |
|---|---|
| Authentication type | OAuth 2.0 |
| Identity provider | Azure Active Directory |
| Client ID | `<APP_ID>` from Step 2 |
| Client Secret | `<CLIENT_SECRET>` from Step 2 |
| Resource URL | `https://api.fabric.microsoft.com` |

3. Click **Create connector** (or **Update connector**)
4. **Copy the generated Redirect URL** from the security page

### Add the Redirect URI to Your App Registration

```powershell
# Replace <APP_ID> and <REDIRECT_URI> with your values
az ad app update `
  --id <APP_ID> `
  --web-redirect-uris "<REDIRECT_URI>"
```

Verify:

```powershell
az ad app show --id <APP_ID> --query "web.redirectUris" -o json
```

### Definition tab

Import the operation definition from the MCP Streamable HTTP swagger:

1. Click **Swagger Editor** toggle (top of the Definition tab)
2. Replace the contents with:

```json
{
  "swagger": "2.0",
  "info": {
    "title": "Fabric MCP Custom Connector",
    "description": "This MCP Server will work with Streamable HTTP and is meant to work with Microsoft Copilot Studio",
    "version": "1.0.0"
  },
  "host": "api.fabric.microsoft.com",
  "basePath": "/v1/mcp",
  "schemes": ["https"],
  "paths": {
    "/": {
      "post": {
        "summary": "MCP Server Streamable HTTP",
        "x-ms-agentic-protocol": "mcp-streamable-1.0",
        "operationId": "InvokeMCP",
        "responses": {
          "200": {
            "description": "Success"
          }
        }
      }
    }
  },
  "securityDefinitions": {}
}
```

> **Source:** [MCP-Streamable-HTTP swagger](https://github.com/troystaylor/PowerPlatformConnectors/blob/dev/custom-connectors/MCP-Streamable-HTTP/apiDefinition.swagger.json)

The `x-ms-agentic-protocol: mcp-streamable-1.0` property tells Copilot Studio to handle the connector as a native MCP tool — it will automatically discover and expose the Fabric MCP server's tools to the agent.

Click **Update connector** to save.

---

## Step 4: Test the Connector

1. Go to the **Test** tab in the custom connector
2. Click **+ New connection** → sign in with your Entra ID account
3. Test with this body:

**Initialize:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-03-26",
    "capabilities": {},
    "clientInfo": {
      "name": "CopilotStudio",
      "version": "1.0.0"
    }
  }
}
```

**List available tools:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list",
  "params": {}
}
```

**List workspaces:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "fabric_list_workspaces",
    "arguments": {}
  }
}
```

---

## Step 5: Add to Copilot Studio

1. In **Copilot Studio** → your agent → **Tools** → **+ Add a Tool**
2. Search for your custom connector name
3. Select the **Invoke MCP** action
4. Configure the connection
5. Add agent instructions describing when to use the tool
6. Enable **Generative AI orchestration** under Settings
7. **Publish** the agent

---

## Troubleshooting

| Error | Resolution |
|---|---|
| `AADSTS50011: redirect URI mismatch` | Copy the redirect URI from the custom connector security page and add it to the app registration with `az ad app update` |
| `AADSTS65001: consent required` | Run `az ad app permission grant` as shown in Step 2 |
| `401 Unauthorized` | Verify the Resource URL is `https://api.fabric.microsoft.com` and the API permission is granted |
| Service principal not found | Run `az ad sp create --id 3f021233-1542-41f0-8faa-ca8aca37ace4` |
| Connector times out | Ensure the Fabric MCP server is enabled in Fabric Admin Settings → Integration settings |

# Copilot Studio Agent Middleware

Azure Function proxy that connects Salesforce to Copilot Studio agents using the [M365 Agents SDK](https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk) (Direct Connect). Replaces Direct Line polling with synchronous request/response via the official SDK.

## Architecture

```
Salesforce LWC
    │ Apex callout
    ▼
Azure Function (this middleware)
    │ M365 Agents SDK
    ▼
Copilot Studio Agent (Direct Connect)
    │
    ▼
Response activities returned synchronously
    │
    ▼
Azure Function → Apex → LWC
```

## Prerequisites

- Node.js 18+
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4
- Azure subscription (for deployment)
- Entra ID app registration with **CopilotStudio.Copilots.Invoke** delegated permission

## Azure App Registration Setup

1. Go to [Azure Portal](https://portal.azure.com/) → **App registrations** → **New registration**
2. Name: `Copilot Studio Salesforce Middleware`
3. Supported account types: **Single tenant**
4. Redirect URI: **Web** → `https://<your-function-app>.azurewebsites.net/api/auth/callback`
5. Under **API permissions**, add:
   | API | Permission | Type |
   |-----|-----------|------|
   | Power Platform API | `CopilotStudio.Copilots.Invoke` | Delegated |
6. Click **Grant admin consent**
7. Under **Certificates & secrets**, create a client secret
8. Note the **Application (client) ID**, **Directory (tenant) ID**, and the **client secret value**

## Local Development

1. Copy settings:

   ```bash
   cp local.settings.json.example local.settings.json
   ```

2. Fill in your values:
   | Variable | Source |
   |----------|--------|
   | `AZURE_TENANT_ID` | App registration → Directory (tenant) ID |
   | `AZURE_CLIENT_ID` | App registration → Application (client) ID |
   | `AZURE_CLIENT_SECRET` | App registration → Client secret value |
   | `COPILOT_DIRECT_CONNECT_URL` | Copilot Studio → Channels → Native app → Direct Connect URL |

   **Alternative to Direct Connect URL:** set `COPILOT_ENVIRONMENT_ID` and `COPILOT_SCHEMA_NAME` instead (from Copilot Studio → Settings → Advanced → Metadata).

3. Install dependencies:

   ```bash
   npm install
   ```

4. Start:

   ```bash
   npm start
   ```

   The API is available at `http://localhost:7071/api/conversations`.

## API

### POST /api/conversations

Start a new agent conversation. Returns the greeting activities and agent name.

**Response** (200):

```json
{
    "conversationId": "conv_1709472000000_a1b2c3",
    "activities": [
        {
            "type": "message",
            "text": "Hello! How can I help you?",
            "from": { "id": "bot", "name": "MyAgent" }
        }
    ],
    "agentName": "My Agent"
}
```

### POST /api/conversations/{conversationId}/activities

Send a user message and receive response activities synchronously.

**Request:**

```json
{
    "text": "What is my order status?",
    "channelData": { "salesforceRecordId": "001..." }
}
```

**Response** (200):

```json
{
    "activities": [
        {
            "type": "message",
            "text": "Let me look that up for you...",
            "from": { "id": "bot", "name": "MyAgent" },
            "attachments": [],
            "suggestedActions": { "actions": [] }
        }
    ]
}
```

### DELETE /api/conversations/{conversationId}

End a conversation and free server-side resources.

**Response** (200):

```json
{ "ended": true, "conversationId": "conv_..." }
```

## Deploy to Azure

```bash
# Create a Function App (Node.js 18, Linux)
az functionapp create \
    --resource-group myRG \
    --consumption-plan-location eastus \
    --runtime node \
    --runtime-version 18 \
    --functions-version 4 \
    --name my-copilot-middleware \
    --storage-account mystorageacct

# Set environment variables
az functionapp config appsettings set \
    --name my-copilot-middleware \
    --resource-group myRG \
    --settings \
        AZURE_TENANT_ID=<tenant-id> \
        AZURE_CLIENT_ID=<client-id> \
        AZURE_CLIENT_SECRET=<secret> \
        COPILOT_DIRECT_CONNECT_URL=<url>

# Deploy
func azure functionapp publish my-copilot-middleware
```

After deployment, copy the Function App URL (e.g., `https://my-copilot-middleware.azurewebsites.net`) and the **host key** from the Azure Portal → Function App → App keys. You will need both when configuring the Salesforce Custom Metadata.

## Session Management

Conversation state (SDK client instances) is stored in-memory. This works for single-instance deployments. For production with multiple instances, replace the in-memory `Map` with Azure Cache for Redis or Cosmos DB. Sessions auto-expire after 1 hour of inactivity.

## Technology Stack

| Component | Package | Version |
|-----------|---------|---------|
| Runtime | Azure Functions | v4 |
| Agent SDK | @microsoft/agents-copilotstudio-client | ^1.2.3 |
| Auth | @azure/msal-node | ^2.16.3 |
| Node.js | — | 18+ |

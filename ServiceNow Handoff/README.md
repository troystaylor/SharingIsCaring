# ServiceNow Handoff Agent

Copilot Studio agent with bidirectional live agent handoff to ServiceNow, built on the Microsoft 365 Agents SDK.

## The Problem

In traditional ServiceNow + Copilot Studio integrations, the Azure Function relay uses a request-response polling pattern. When a live agent sends a reply in ServiceNow, there's no mechanism pushing that reply back to the user — they only receive it after sending another message. This creates a broken conversation experience.

## The Fix

This agent uses **proactive messaging** (`ProcessProactiveAsync`) to push ServiceNow agent replies to the user immediately via a webhook, without requiring user input. The pattern is based on the [GenesysHandoff sample](https://github.com/microsoft/Agents/tree/main/samples/dotnet/GenesysHandoff) and the [Copilot Studio skill-handoff sample](https://github.com/microsoft/CopilotStudioSamples/tree/main/contact-center/skill-handoff).

## Architecture

```
User (WebChat/Teams)
    ↕
Agents SDK Agent (Azure App Service)
    ├── /api/messages          ← Bot Framework channel
    ├── /api/servicenow/webhook ← ServiceNow agent replies (proactive push)
    │
    ├── Copilot Studio (Direct Connect via CopilotClient)
    │     Pre-handoff: user messages → Copilot Studio → responses back to user
    │
    ├── ServiceNow Live Agent (REST API)
    │     During handoff: user messages → ServiceNow agent
    │     Agent replies: ServiceNow webhook → proactive message → user
    │
    └── Fallback: Direct Line WebSocket streaming (Option 3)
```

### Message flow during handoff

1. User says "talk to a live agent"
2. Copilot Studio Escalate topic fires `handoff.initiate` event
3. Agent creates a ServiceNow interaction, stores conversation mapping
4. User messages are forwarded to ServiceNow via REST API
5. ServiceNow Business Rule fires on agent reply → POSTs to `/api/servicenow/webhook`
6. Webhook handler looks up the stored `ConversationReference` and calls `ProcessProactiveAsync` to push the reply to the user **immediately**
7. When the agent or user ends the chat, state is cleared and Copilot Studio resumes

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Azure subscription with permissions to create:
  - App Service
  - Azure Bot resource
  - Microsoft Entra ID app registration
- [Copilot Studio](https://copilotstudio.microsoft.com/) license
- ServiceNow instance (Yokohama or later) with:
  - OAuth application registered
  - Admin access to create Business Rules or Flows

## Project Structure

```
ServiceNowHandoff/
├── Program.cs                              # DI, endpoints, startup validation
├── ServiceNowHandoffAgent.cs               # Main agent — routes pre/during/post-handoff
├── appsettings.json                        # Configuration (placeholders)
├── Services/
│   ├── ConversationStateManager.cs         # Per-conversation state (escalation flag, IDs)
│   └── CopilotClientFactory.cs            # Copilot Studio Direct Connect client
├── ServiceNow/
│   ├── IServiceNowConnectionSettings.cs   # Config interface
│   ├── ServiceNowConnectionSettings.cs    # Config implementation
│   ├── ServiceNowTokenProvider.cs         # OAuth 2.0 token caching
│   ├── ServiceNowMessageSender.cs         # Send messages to ServiceNow + store ConversationReference
│   ├── ServiceNowWebhookHandler.cs        # THE FIX — receives agent replies, pushes proactively
│   ├── ConversationMappingStore.cs        # Bidirectional SN ↔ MCS ID mapping with registry
│   └── ServiceNowNotificationService.cs   # Background polling for agent disconnect
├── Fallback/
│   ├── DirectLineStreamingClient.cs       # WebSocket Direct Line fallback
│   └── DirectLineRelayService.cs          # Fallback lifecycle management
├── CopilotStudio/
│   └── EscalateTopic.yaml                 # Import into Copilot Studio for handoff trigger
└── infra/
    ├── main.bicep                         # Azure infrastructure (App Service + Bot + App Insights)
    └── main.bicepparam                    # Parameter file
```

## Setup

### 1. Create Azure Bot App Registration

1. Go to [Azure Portal](https://portal.azure.com/) > Microsoft Entra ID > App registrations > New registration
2. Name: `ServiceNowHandoffAgent`
3. Supported account type: Single tenant
4. Click Register
5. Copy the **Application (client) ID** — this is your `botAppId`
6. Go to Certificates & secrets > New client secret > Copy the **Value** — this is your `botAppSecret`

### 2. Deploy Azure Infrastructure

```bash
az login
az group create --name rg-servicenow-handoff --location eastus

az deployment group create \
  --resource-group rg-servicenow-handoff \
  --template-file infra/main.bicep \
  --parameters \
    botAppId=YOUR-APP-ID \
    botAppSecret=YOUR-APP-SECRET \
    serviceNowInstanceUrl=https://YOUR-INSTANCE.service-now.com
```

Note the outputs:
- `appServiceUrl` — your agent's base URL
- `serviceNowWebhookUrl` — give this to your ServiceNow admin
- `appInsightsConnectionString` — already wired into the App Service

### 3. Deploy Code

```bash
cd ServiceNowHandoff
dotnet publish -c Release -o ./publish
cd publish
zip -r ../publish.zip .
cd ..
az webapp deploy \
  --resource-group rg-servicenow-handoff \
  --name <appServiceName-from-output> \
  --src-path publish.zip
```

### 4. Configure Copilot Studio

1. Open your agent in [Copilot Studio](https://copilotstudio.microsoft.com/)
2. Go to **Settings > Channels > Web app** (or Native app)
3. Copy the **connection string** (this is the `DirectConnectUrl`)
4. Set it in the App Service configuration:
   ```bash
   az webapp config appsettings set \
     --resource-group rg-servicenow-handoff \
     --name <appServiceName> \
     --settings CopilotStudioClientSettings__DirectConnectUrl="YOUR-CONNECTION-STRING"
   ```
5. Import the Escalate topic:
   - Go to Topics > System > Escalate
   - Replace with the content from `CopilotStudio/EscalateTopic.yaml`
   - Save and Publish

### 5. Configure ServiceNow

#### OAuth App

1. In ServiceNow: **System OAuth > Application Registry**
2. Create an OAuth API endpoint for external clients
3. Copy the Client ID and Client Secret
4. Set in App Service:
   ```bash
   az webapp config appsettings set \
     --resource-group rg-servicenow-handoff \
     --name <appServiceName> \
     --settings \
       ServiceNow__ClientId="YOUR-SN-CLIENT-ID" \
       ServiceNow__ClientSecret="YOUR-SN-CLIENT-SECRET"
   ```
5. Ensure the OAuth app's user has these roles:
   - `itil` (interaction table access)
   - `sn_csm_ws.csm_ws_integration` (if using CSM Chat APIs)

#### Webhook for Agent Replies

Create a Business Rule in ServiceNow to POST agent replies to your webhook:

1. Go to **System Definition > Business Rules > New**
2. Table: `sys_cs_message` (or your chat message table)
3. When: **after insert**
4. Filter: `role = agent`
5. Script:
   ```javascript
   (function executeRule(current, previous) {
       var r = new sn_ws.RESTMessageV2();
       r.setEndpoint('https://YOUR-APP.azurewebsites.net/api/servicenow/webhook');
       r.setHttpMethod('POST');
       r.setRequestHeader('Content-Type', 'application/json');
       r.setRequestHeader('Authorization', 'Bearer YOUR-WEBHOOK-SECRET');
       r.setRequestBody(JSON.stringify({
           ConversationId: current.group.toString(),
           Message: current.message.toString(),
           AgentName: current.sys_created_by.toString(),
           EventType: 'agent_message',
           Timestamp: current.sys_created_on.toString()
       }));
       r.executeAsync();
   })(current, previous);
   ```
6. Set the webhook secret in App Service:
   ```bash
   az webapp config appsettings set \
     --resource-group rg-servicenow-handoff \
     --name <appServiceName> \
     --settings ServiceNow__WebhookSecret="YOUR-WEBHOOK-SECRET"
   ```

> **Important:** The field names in the Business Rule script (`group`, `message`, `sys_created_by`) must match your ServiceNow table schema. Verify with your ServiceNow admin. See the TODO comments in `ServiceNowMessageSender.cs` and `ServiceNowWebhookHandler.cs` for details.

### 6. Test

1. Open the Azure Bot resource in the portal
2. Click **Test in Web Chat**
3. Chat with the Copilot Studio agent
4. Say "talk to a live agent"
5. In ServiceNow Agent Workspace, pick up the conversation
6. Send a reply as the live agent → **verify the user receives it immediately without sending another message**
7. End the chat → verify the user is returned to Copilot Studio

## Configuration Reference

| Setting | Location | Description |
|---|---|---|
| `Connections:BotServiceConnection:Settings:ClientId` | appsettings.json | Azure Bot app registration ID |
| `Connections:BotServiceConnection:Settings:ClientSecret` | appsettings.json | Azure Bot app secret |
| `Connections:BotServiceConnection:Settings:TenantId` | appsettings.json | Entra tenant ID |
| `CopilotStudioClientSettings:DirectConnectUrl` | appsettings.json | From Copilot Studio > Channels > Copy connection string |
| `ServiceNow:InstanceUrl` | appsettings.json | `https://INSTANCE.service-now.com` |
| `ServiceNow:ClientId` | appsettings.json | ServiceNow OAuth app Client ID |
| `ServiceNow:ClientSecret` | appsettings.json | ServiceNow OAuth app Client Secret |
| `ServiceNow:WebhookSecret` | appsettings.json | Shared secret for webhook HMAC validation |
| `ServiceNow:QueueId` | appsettings.json | ServiceNow assignment group sys_id for routing |
| `ServiceNow:PollingIntervalSeconds` | appsettings.json | How often to check for agent disconnect (default: 15) |
| `ApplicationInsights:ConnectionString` | appsettings.json | App Insights connection string (auto-set by Bicep) |

## Production Considerations

- **Storage**: Replace `MemoryStorage` with Azure Blob Storage or Cosmos DB. MemoryStorage loses all state on restart and doesn't work with multiple instances.
- **Scaling**: The background polling service (`ServiceNowNotificationService`) runs on every instance. If scaling to multiple instances, use a distributed lock or move disconnect detection to the ServiceNow webhook.
- **Security**: Ensure `WebhookSecret` is set in production. Without it, the webhook accepts all requests.
- **Retry**: ServiceNow API calls use `Microsoft.Extensions.Http.Resilience` with exponential backoff, circuit breaker, and retry on transient errors (5xx, 408, 429).

## Key References

- [Copilot Studio: Hand off to ServiceNow](https://learn.microsoft.com/en-us/microsoft-copilot-studio/customer-copilot-servicenow)
- [Copilot Studio: Configure generic handoff](https://learn.microsoft.com/en-us/microsoft-copilot-studio/configure-generic-handoff)
- [M365 Agents SDK: GenesysHandoff sample](https://github.com/microsoft/Agents/tree/main/samples/dotnet/GenesysHandoff)
- [CopilotStudioSamples: Skill handoff](https://github.com/microsoft/CopilotStudioSamples/tree/main/contact-center/skill-handoff)
- [MCS CAT Blog: Handing Over to Live Agents](https://microsoft.github.io/mcscatblog/posts/copilot-studio-handover-live-agent/)
- [M365 Agents SDK documentation](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/)
- [ServiceNow Interact Data Instance API](https://www.servicenow.com/docs/r/yokohama/api-reference/developer-guides/mobsdk-and-interact_data_instance.html)

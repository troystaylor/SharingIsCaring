# ServiceNow Handoff Agent

Copilot Studio agent with bidirectional live agent handoff to ServiceNow, built on the Microsoft 365 Agents SDK.

## The Problem

In traditional ServiceNow + Copilot Studio integrations, the Azure Function relay uses a request-response polling pattern. When a live agent sends a reply in ServiceNow, there's no mechanism pushing that reply back to the user — they only receive it after sending another message. This creates a broken conversation experience.

## The Fix

This agent uses **proactive messaging** (`ProcessProactiveAsync`) to push ServiceNow agent replies to the user immediately via a webhook, without requiring user input. The pattern is based on the [GenesysHandoff sample](https://github.com/microsoft/Agents/tree/main/samples/dotnet/GenesysHandoff).

## Architecture

```
┌──────────────────┐     HTTPS (Bot Framework)     ┌─────────────────────────┐
│  User            │ ─────────────────────────────► │  Azure Bot              │
│  (WebChat/Teams) │ ◄───────────────────────────── │  snhandoff-bot-*        │
└──────────────────┘                                └───────────┬─────────────┘
                                                                │
                                                                │ /api/messages
                                                                ▼
                                                    ┌─────────────────────────┐
                                                    │  Azure App Service      │
                                                    │  (Agents SDK Agent)     │
                                                    │                         │
                                                    │  ServiceNowHandoffAgent │
                                                    │  ├── Direct Line ──────►├──► Copilot Studio
                                                    │  │   (pre-handoff)      │
                                                    │  │                      │
                                                    │  ├── ServiceNow REST ──►├──► ServiceNow
                                                    │  │   (during handoff)   │    (interaction table)
                                                    │  │                      │
                                                    │  └── /api/servicenow/ ◄─├─── ServiceNow
                                                    │      webhook            │    (Business Rule)
                                                    │      (proactive push)   │
                                                    └─────────────────────────┘
```

## Workflow

```
  User                    Agent (App Service)              Copilot Studio           ServiceNow
   │                            │                              │                       │
   │  1. "Hello"                │                              │                       │
   │ ──────────────────────────►│                              │                       │
   │                            │  2. Send via Direct Line     │                       │
   │                            │ ────────────────────────────►│                       │
   │                            │  3. Bot response             │                       │
   │                            │ ◄────────────────────────────│                       │
   │  4. "Hello, how can I..."  │                              │                       │
   │ ◄──────────────────────────│                              │                       │
   │                            │                              │                       │
   │  5. "Talk to a live agent" │                              │                       │
   │ ──────────────────────────►│                              │                       │
   │                            │  6. Send via Direct Line     │                       │
   │                            │ ────────────────────────────►│                       │
   │                            │  7. handoff.initiate event   │                       │
   │                            │ ◄────────────────────────────│                       │
   │                            │                              │                       │
   │  8. "Connecting you..."    │  9. Create interaction       │                       │
   │ ◄──────────────────────────│ ─────────────────────────────────────────────────────►│
   │                            │  10. Store mapping + ConvRef │                       │
   │                            │                              │                       │
   │  11. "How can I help?"     │  ◄─── Business Rule webhook (POST /api/servicenow/) │
   │ ◄──────────────────────────│       ProcessProactiveAsync  │                       │
   │   (proactive push —        │                              │                       │
   │    no user message needed!)│                              │                       │
   │                            │                              │                       │
   │  12. User replies          │  13. Forward to ServiceNow   │                       │
   │ ──────────────────────────►│ ─────────────────────────────────────────────────────►│
   │                            │                              │                       │
   │  14. "End chat with agent" │  15. Close interaction       │                       │
   │ ──────────────────────────►│ ─────────────────────────────────────────────────────►│
   │                            │  16. Clear state, resume     │                       │
   │  17. Back to Copilot Studio│                              │                       │
   │ ◄──────────────────────────│                              │                       │
```

> **Step 11 is THE FIX** — the agent reply arrives proactively via the webhook and `ProcessProactiveAsync`, without the user needing to send another message.

## Why Direct Line (not Direct Connect)

The Agents SDK `CopilotClient` (Direct Connect) [does not support server-to-server tokens](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk). The authenticated Direct Connect URL requires user-interactive sign-in, which isn't possible from a server app like Azure App Service. This agent uses the **Direct Line REST API** with a secret key instead, which works for server-to-server communication without user sign-in.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Azure subscription with permissions to create:
  - App Service
  - Azure Bot resource
  - Microsoft Entra ID app registration
- [Copilot Studio](https://copilotstudio.microsoft.com/) license
- ServiceNow instance (Yokohama or later) with:
  - OAuth application registered
  - Admin access to create Business Rules

## Project Structure

```
ServiceNowHandoff/
├── Program.cs                              # DI, endpoints, startup validation
├── ServiceNowHandoffAgent.cs               # Main agent — routes pre/during/post-handoff
├── appsettings.json                        # Configuration (placeholders)
├── Services/
│   ├── DirectLineCopilotService.cs         # Direct Line REST API client for Copilot Studio
│   ├── ConversationStateManager.cs         # Per-conversation state (escalation flag, IDs)
│   └── CopilotClientFactory.cs            # (Legacy) Direct Connect client — S2S not supported
├── ServiceNow/
│   ├── IServiceNowConnectionSettings.cs   # Config interface
│   ├── ServiceNowConnectionSettings.cs    # Config implementation
│   ├── ServiceNowTokenProvider.cs         # OAuth 2.0 token caching (password grant for dev)
│   ├── ServiceNowMessageSender.cs         # Send messages to ServiceNow + store ConversationReference
│   ├── ServiceNowWebhookHandler.cs        # THE FIX — receives agent replies, pushes proactively
│   ├── ConversationMappingStore.cs        # Bidirectional SN ↔ Direct Line ID mapping
│   └── ServiceNowNotificationService.cs   # Background polling for agent disconnect
├── Fallback/
│   ├── DirectLineStreamingClient.cs       # WebSocket Direct Line (alternative approach)
│   └── DirectLineRelayService.cs          # WebSocket lifecycle management
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
7. **Create a service principal** (required for SingleTenant bot auth):
   ```bash
   az ad sp create --id <botAppId>
   ```
8. **Add Copilot Studio API permission** (required for the `CopilotStudio.Copilots.Invoke` role):
   ```bash
   az ad app permission add --id <botAppId> \
     --api 8578e004-a5c6-46e7-913e-12f58912df43 \
     --api-permissions 38c13204-7d79-4d83-bdbb-b770e28400df=Role

   az ad app permission admin-consent --id <botAppId>
   ```

### 2. Deploy Azure Infrastructure

> **Note:** If the Azure Bot resource hangs during Bicep deployment, cancel and create it separately via CLI.

```bash
az login
az group create --name rg-servicenow-handoff --location westus2

az deployment group create \
  --resource-group rg-servicenow-handoff \
  --template-file infra/main.bicep \
  --parameters \
    botAppId=YOUR-APP-ID \
    botAppSecret=YOUR-APP-SECRET \
    serviceNowInstanceUrl=https://YOUR-INSTANCE.service-now.com
```

If the Bot Service resource gets stuck:
```bash
az bot create \
  --resource-group rg-servicenow-handoff \
  --name <baseName>-bot-<suffix> \
  --app-type SingleTenant \
  --appid <botAppId> \
  --tenant-id <tenantId> \
  --endpoint "https://<appServiceName>.azurewebsites.net/api/messages"
```

### 3. Deploy Code

```bash
cd ServiceNowHandoff
dotnet publish ServiceNowHandoff.csproj -c Release -o ./publish

# Windows (PowerShell):
Compress-Archive -Path "publish\*" -DestinationPath "publish.zip" -Force

# Mac/Linux:
# cd publish && zip -r ../publish.zip . && cd ..

az webapp deploy \
  --resource-group rg-servicenow-handoff \
  --name <appServiceName> \
  --src-path publish.zip --type zip
```

> **Startup time:** The app takes ~30–60 seconds to warm up. The deploy command may report a timeout, but the app will start successfully. Verify with `az webapp show --query state`.

### 4. Configure Copilot Studio

1. Open your agent in [Copilot Studio](https://copilotstudio.microsoft.com/)
2. **Authentication**: Go to **Settings > Security > Authentication** — select **"No authentication"** for unauthenticated website users
3. **Get the Direct Line secret**: Go to **Settings > Security > Web channel security** — copy a **secret key**
4. Set the Direct Line secret and Bot Framework scope in App Service:
   ```bash
   az webapp config appsettings set \
     --resource-group rg-servicenow-handoff \
     --name <appServiceName> \
     --settings \
       DirectLine__Secret="YOUR-DIRECT-LINE-SECRET" \
       Connections__BotServiceConnection__Settings__Scopes__0="https://api.botframework.com/.default"
   ```
5. **Configure the Escalate topic**: Go to **Topics > System > Escalate**
   - Add a **Send a message** node: `I understand you'd like to speak with a live agent. Let me connect you now.`
   - Add a **Transfer conversation** node (Topic management > Transfer conversation)
   - **Save and Publish**

### 5. Configure ServiceNow

#### OAuth App

1. In ServiceNow: **System OAuth > Application Registry**
2. Select **"[Deprecated UI] Create an OAuth API endpoint for external clients"**
3. Set **Client Type** to a service type (e.g., "Integration as a Service")
4. Add **Auth Scope**: `useraccount`
5. Copy the Client ID and Client Secret
6. Set in App Service (use a JSON file for special characters in secrets):
   ```json
   [
     {"name": "ServiceNow__ClientId", "value": "YOUR-CLIENT-ID", "slotSetting": false},
     {"name": "ServiceNow__ClientSecret", "value": "YOUR-CLIENT-SECRET", "slotSetting": false},
     {"name": "ServiceNow__Username", "value": "admin", "slotSetting": false},
     {"name": "ServiceNow__Password", "value": "YOUR-PASSWORD", "slotSetting": false},
     {"name": "ServiceNow__WebhookSecret", "value": "RANDOM-SECRET", "slotSetting": false}
   ]
   ```
   ```bash
   az webapp config appsettings set \
     --resource-group rg-servicenow-handoff \
     --name <appServiceName> \
     --settings "@sn-settings.json"
   ```
7. Ensure the admin user has these roles: `itil`, `rest_service`, `web_service_admin`

#### Webhook Business Rule

Create a Business Rule in ServiceNow to POST agent replies to your webhook:

1. Go to **System Definition > Business Rules > New**
2. Table: `sys_cs_message`
3. When: **after insert**
4. Filter: **Agent → is → True**
5. Check **Advanced**, then add this script:
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

> **Important:** The field names (`group`, `message`, `sys_created_by`) must match your ServiceNow table schema. Verify with your ServiceNow admin.

### 6. Configure ServiceNow Agent Workspace

1. **Activate plugins** (System Definition > Plugins):
   - `com.glide.awa` — Advanced Work Assignment
   - `com.snc.agent_workspace` — Agent Workspace
   - `com.sn_csm_ws` — CSM Configurable Workspace
2. **Create a queue**: AWA > Queues > New — copy the `sys_id` and set as `ServiceNow__QueueId`
3. **Create an assignment rule**: Table = `Chat Queue Entry`, Queue = your queue
4. **Add yourself as an agent** via `sys_awa_agent.list`
5. **Access agent inbox**: `https://YOUR-INSTANCE.service-now.com/now/cwf/agent/inbox`

### 7. Test

1. Open the Azure Bot resource → **Test in Web Chat**
2. Say "Hello" — Copilot Studio responds
3. Say "talk to a live agent" — escalation triggers
4. See "Connecting you to a live agent. Please wait..."
5. ServiceNow interaction is created (check `interaction.list`)
6. Agent replies (via Business Rule or webhook simulation) → **user receives reply immediately**
7. Say "End chat with agent" → returns to Copilot Studio

## Configuration Reference

| Setting | Description |
|---|---|
| `Connections:BotServiceConnection:Settings:ClientId` | Azure Bot app registration ID |
| `Connections:BotServiceConnection:Settings:ClientSecret` | Azure Bot app secret |
| `Connections:BotServiceConnection:Settings:TenantId` | Entra tenant ID |
| `Connections:BotServiceConnection:Settings:Scopes` | `https://api.botframework.com/.default` |
| `DirectLine:Secret` | From Copilot Studio > Security > Web channel security |
| `ServiceNow:InstanceUrl` | `https://INSTANCE.service-now.com` |
| `ServiceNow:ClientId` | ServiceNow OAuth app Client ID |
| `ServiceNow:ClientSecret` | ServiceNow OAuth app Client Secret |
| `ServiceNow:Username` | ServiceNow user for OAuth password grant |
| `ServiceNow:Password` | ServiceNow password (use Key Vault in production) |
| `ServiceNow:WebhookSecret` | Shared secret for webhook HMAC validation |
| `ServiceNow:QueueId` | ServiceNow queue sys_id for routing |
| `ServiceNow:PollingIntervalSeconds` | Agent disconnect check interval (default: 15) |
| `ApplicationInsights:ConnectionString` | App Insights (auto-set by Bicep) |

## Deployment Notes

- **Service Principal**: Required for SingleTenant bot auth (`az ad sp create --id <appId>`)
- **Direct Line vs Direct Connect**: The Agents SDK `CopilotClient` [does not support S2S tokens](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk) — use Direct Line REST API with a secret key instead
- **Direct Line Watermarks**: Track the `ActivitySet.Watermark` (not `activity.Id`) when polling to avoid duplicate messages
- **ServiceNow OAuth**: Dev instances may need `password` grant type. Production instances should use `client_credentials` with a Confidential OAuth app. Always add the `useraccount` Auth Scope
- **Region Quotas**: If deployment fails with `InternalSubscriptionIsOverQuotaForSku`, try `westus2` instead of `eastus`
- **Special Characters in Secrets**: Use JSON file with `az webapp config appsettings set --settings "@file.json"`
- **MemoryStorage**: The `ConversationMappingStore` uses an in-memory `ConcurrentDictionary`. Mappings are lost on restart. For production, replace with Cosmos DB or Azure Blob Storage

## Production Considerations

- **Storage**: Replace in-memory mapping and `MemoryStorage` with Azure Cosmos DB or Blob Storage
- **Credentials**: Store all secrets in Azure Key Vault and use Key Vault references in App Service
- **OAuth**: Use `client_credentials` grant (not `password` grant) with a properly configured ServiceNow OAuth app
- **Scaling**: The `ConversationMappingStore` uses `ConcurrentDictionary` which doesn't work across multiple instances. Use persistent storage
- **Security**: Ensure `WebhookSecret` is set — without it, the webhook accepts all requests
- **Retry**: ServiceNow API calls use `Microsoft.Extensions.Http.Resilience` with exponential backoff and circuit breaker

## Key References

- [Copilot Studio: Hand off to ServiceNow](https://learn.microsoft.com/en-us/microsoft-copilot-studio/customer-copilot-servicenow)
- [Copilot Studio: Integrate with M365 Agents SDK](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk)
- [Copilot Studio: Configure generic handoff](https://learn.microsoft.com/en-us/microsoft-copilot-studio/configure-generic-handoff)
- [M365 Agents SDK: GenesysHandoff sample](https://github.com/microsoft/Agents/tree/main/samples/dotnet/GenesysHandoff)
- [M365 Agents SDK: CopilotStudio Client sample](https://github.com/microsoft/Agents/tree/main/samples/dotnet/copilotstudio-client)
- [M365 Agents SDK documentation](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/)
- [ServiceNow Table API](https://www.servicenow.com/docs/r/yokohama/api-reference/developer-guides/mobsdk-and-interact_data_instance.html)

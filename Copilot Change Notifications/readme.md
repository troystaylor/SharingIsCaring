# Copilot Change Notifications

## Overview

The Copilot Change Notifications MCP connector enables real-time monitoring of Microsoft 365 Copilot activity through Microsoft Graph change notifications. Subscribe to Copilot AI interactions (queries, responses) and meeting AI insights (summaries) across the tenant or for specific users and meetings. Includes MCP protocol support for native Copilot Studio integration.

**Key Features:**
- Tenant-wide and per-user AI interaction monitoring
- Meeting AI insight change notifications
- Subscription management (create, list, get, renew, delete)
- Webhook validation before subscription creation
- Webhook payload processing and validation
- OData filtering for targeted monitoring
- 11 MCP tools for complete change notification workflows
- Built-in input validation with clear error messages

## Prerequisites

1. **Microsoft 365 Tenant** with Copilot features enabled
2. **Azure AD Application Registration** with appropriate permissions:
   - `AiEnterpriseInteraction.Read.All` (application) for tenant-wide interactions
   - `AiEnterpriseInteraction.Read` (delegated) for per-user interactions
   - `OnlineMeetingAiInsight.Read.All` for meeting insights
3. **Copilot Licensing**:
   - Microsoft 365 Copilot Chat (for interactions)
   - Power Platform connectors license (for Copilot Studio)
4. **HTTPS Webhook Endpoint** to receive notifications

## Connection Setup

The connector uses OAuth 2.0 client credentials (client ID and secret) to authenticate to Microsoft Graph.

| Parameter | Description | Example |
|---|---|---|
| **Tenant ID** | Your Azure AD tenant ID | `12345678-1234-1234-1234-123456789012` |
| **Client ID** | App Registration client ID | (from app registration) |
| **Client Secret** | App Registration client secret | (from app registration) |

### Setting Up the App Registration

1. In [Azure AD](https://portal.azure.com/), create a new app registration
2. Under **Certificates & secrets**, create a new **client secret** and copy the value
3. Under **API permissions**, add:
   - Microsoft Graph → Application → AiEnterpriseInteraction.Read.All
   - Microsoft Graph → Application → OnlineMeetingAiInsight.Read.All
4. Grant admin consent
5. Note the **Client ID**, **Client Secret**, and **Tenant ID**

### Configuring the Notification Webhook

Your notification endpoint should:
1. Listen on HTTPS (port 443, valid SSL certificate)
2. Accept POST requests
3. Validate the `clientState` header matches your subscription
4. Respond with 200 OK within 30 seconds
5. Parse the notification payload (see examples below)

Example webhook response:
```
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 0
```

## Operations

### Subscription Management

#### Create Subscription (MCP Tool: `create_tenant_interaction_subscription`)

Subscribe to tenant-wide Copilot AI interactions.

**Input:**
- `notification_url` (required): HTTPS webhook endpoint
- `expiration_minutes` (optional): Minutes until subscription expires. Default: 60. Max: 1440 (24 hours)
- `include_resource_data` (optional): Include full interaction data in notifications. Default: false
- `filter_app_class` (optional): Filter by appClass. Example: `IPM.SkypeTeams.Message.Copilot.Teams`

**Returns:**
- `id`: Subscription ID
- `resource`: The monitored resource path
- `expirationDateTime`: When subscription expires

**Example (MCP):**
```json
{
  "notification_url": "https://webhook.example.com/copilot-notifications",
  "expiration_minutes": 60,
  "include_resource_data": false,
  "filter_app_class": "IPM.SkypeTeams.Message.Copilot.Teams"
}
```

#### Create Per-User Subscription (MCP Tool: `create_user_interaction_subscription`)

Subscribe to interactions for a specific user.

**Input:**
- `user_id` (required): User's UPN or object ID
- `notification_url` (required): Webhook endpoint
- `expiration_minutes` (optional): Default: 60

#### Create Meeting Insight Subscription (MCP Tool: `create_meeting_insight_subscription`)

Subscribe to AI summary generation for a specific meeting.

**Input:**
- `user_id` (required): Meeting owner's user ID
- `meeting_id` (required): Meeting ID
- `notification_url` (required): Webhook endpoint
- `expiration_minutes` (optional): Default: 60

#### List Subscriptions (MCP Tool: `list_subscriptions`)

Retrieve all active subscriptions. No input required.

#### Get Subscription (MCP Tool: `get_subscription`)

Retrieve detailed information about a specific subscription.

**Input:**
- `subscription_id` (required): The subscription ID

**Returns:**
- `id`: Subscription ID
- `resource`: The monitored resource path
- `changeType`: Change types being monitored
- `notificationUrl`: Webhook endpoint URL
- `expirationDateTime`: When the subscription expires
- `includeResourceData`: Whether resource data is included in notifications
- `clientState`: Client state (masked for security)

**Use case**: Check subscription status, expiration date, and configuration before deciding to renew.

#### Test Webhook Connectivity

Test if a webhook endpoint is reachable and responding correctly before creating a subscription.

**Input:**
- `webhookUrl` (required): HTTPS endpoint URL to test
- `clientState` (optional): Client state to include in test payload

**Returns:**
- `success`: Boolean indicating if webhook is reachable
- `statusCode`: HTTP response code from webhook
- `responseTime`: Response time in milliseconds
- `message`: Descriptive message about the test result

**Use case**: Validate webhook configuration before creating a subscription to catch connectivity issues early.

#### Delete Subscription (MCP Tool: `delete_subscription`)

Delete an active subscription by ID.

**Input:**
- `subscription_id` (required): The subscription ID

#### Renew Subscription (MCP Tool: `renew_subscription`)

Extend the expiration of a subscription.

**Input:**
- `subscription_id` (required): The subscription ID
- `expiration_minutes` (required): New expiration time in minutes

### Notification Processing

#### Process Interaction Notification (MCP Tool: `process_interaction_notification`)

Parse and validate a webhook payload for AI interactions.

**Input:**
- `notification_payload` (required): The complete notification JSON as a string
- `client_state` (optional): Expected client state for validation

**Returns:**
```json
{
  "success": true,
  "notifications_count": 1,
  "notifications": [
    {
      "subscription_id": "10493aa0-4d29-4df5-bc0c-ef742cc6cd7f",
      "change_type": "created",
      "resource_id": "1731701801008",
      "resource_type": "#Microsoft.Graph.aiInteraction"
    }
  ]
}
```

#### Process Insight Notification (MCP Tool: `process_insight_notification`)

Parse and validate a webhook payload for meeting insights.

**Input:**
- `notification_payload` (required): The complete notification JSON as a string

### Data Retrieval

#### Get AI Interaction (MCP Tool: `get_interaction`)

Retrieve full details of an AI interaction.

**Input:**
- `interaction_id` (required): The interaction ID from a notification

**Returns:**
- `id`: Interaction ID
- `sessionId`: Session ID
- `appClass`: Application (e.g., `IPM.SkypeTeams.Message.Copilot.Teams`)
- `interactionType`: Type (aiResponse, aiPrompt)
- `conversationType`: Type (appchat, bizchat)
- `createdDateTime`: When created
- `from`: Who initiated (user, bot, application)
- `body`: Message content and type
- `contexts`: Context references (meeting, channel, etc.)
- `attachments`: Any attachments

#### Get Meeting Insight (MCP Tool: `get_meeting_insight`)

Retrieve a specific meeting AI insight.

**Input:**
- `user_id` (required): Meeting owner's user ID
- `meeting_id` (required): Meeting ID
- `insight_id` (required): Insight ID from notification

#### List Interactions (MCP Tool: `list_interactions`)

List recent AI interactions with optional filtering.

**Input:**
- `filter_expression` (optional): OData filter. Examples:
  - `appClass eq 'IPM.SkypeTeams.Message.Copilot.Teams'` - Teams interactions only
  - `conversationType ne 'bizchat'` - Exclude BizChat
  - `interactionType eq 'aiResponse'` - Responses only
- `max_results` (optional): Max interactions to return (1-100). Default: 10

## Webhook Notification Examples

### AI Interaction Notification (with resource data)

```json
{
  "value": [
    {
      "subscriptionId": "10493aa0-4d29-4df5-bc0c-ef742cc6cd7f",
      "changeType": "created",
      "clientState": "my-secret-state",
      "subscriptionExpirationDateTime": "2025-02-02T10:30:34.9097561-08:00",
      "resource": "copilot/interactionHistory/interactions('1731701801008')",
      "resourceData": {
        "id": "1731701801008",
        "@odata.type": "#Microsoft.Graph.aiInteraction",
        "@odata.id": "copilot/interactionHistory/interactions('1731701801008')"
      },
      "tenantId": "72f988bf-86f1-41af-91ab-2d7cd011db47"
    }
  ],
  "validationTokens": ["eyJ0eXAi..."]
}
```

**Processing Steps:**
1. Extract `value` array from webhook body
2. For each notification:
   - Validate `clientState` matches subscription
   - Extract `id` from `resourceData`
   - Call `get_interaction` with the ID to retrieve full data
   - Process the interaction (log, store, trigger workflows)

### Meeting AI Insight Notification

```json
{
  "value": [
    {
      "subscriptionId": "10493aa0...",
      "changeType": "created",
      "clientState": "<<--SpecifiedClientState-->>",
      "subscriptionExpirationDateTime": "2026-01-02T10:30:34.9097561-08:00",
      "resource": "copilot/users/b935e675.../onlineMeetings/YTc3OT.../aiInsights/Z2HWbT...",
      "resourceData": {
        "id": "Z2HWbT...",
        "@odata.type": "#Microsoft.Graph.callAiInsight",
        "@odata.id": "copilot/users/b935e675.../onlineMeetings/YTc3OT.../aiInsights/Z2HWbT..."
      }
    }
  ]
}
```

**Processing Steps:**
1. Extract insight ID from `resourceData.id`
2. Extract user ID and meeting ID from the resource path
3. Call `get_meeting_insight` with user_id, meeting_id, insight_id
4. Process the summary

## Application Insights Logging

The connector includes built-in telemetry logging to Application Insights.

### Configuring Telemetry

1. Edit `script.csx`
2. Replace placeholder:
   ```csharp
   private const string APP_INSIGHTS_KEY = "[INSERT_YOUR_APP_INSIGHTS_INSTRUMENTATION_KEY]";
   ```
   with your Application Insights instrumentation key
3. Redeploy the connector

### Logged Events

- `RequestReceived`: All incoming requests (operation, correlation ID)
- `RequestError`: Request failures with error details and stack trace
- `GraphRequestSuccess`: Successful Graph API calls (method, path, status)
- `GraphRequestError`: Failed Graph API calls with response
- `GraphRequestException`: Connection errors to Graph API
- `McpError`: MCP protocol handling errors
- `ToolError`: MCP tool execution failures

## Lifecycle Notifications

For subscriptions with `expirationDateTime` greater than 1 hour in the future, Microsoft Graph requires a separate `lifecycleNotificationUrl` to notify your application of subscription events.

### Lifecycle Event Types

| Event | Payload | Meaning |
|-------|---------|--------|
| **subscriptionRemoved** | Subscription ID | Subscription was deleted by Microsoft (typically due to 3 consecutive webhook failures) |
| **missed** | Count of missed notifications | Your webhook didn't respond for a brief period; notifications may have been lost |
| **reauthorizationRequired** | N/A | The app needs to re-authenticate (typically annual re-auth for delegated scenarios) |

### Example Lifecycle Notification Payload

```json
{
  "value": [
    {
      "subscriptionId": "10493aa0-4d29-4df5-bc0c-ef742cc6cd7f",
      "subscriptionExpirationDateTime": "2026-01-02T10:30:34.9097561-08:00",
      "lifecycleEvent": "subscriptionRemoved",
      "clientState": "my-secret-state",
      "tenantId": "72f988bf-86f1-41af-91ab-2d7cd011db47"
    }
  ]
}
```

### Handling Lifecycle Events

**subscriptionRemoved**: 
- Subscription was removed due to repeated webhook failures
- Re-create the subscription after fixing your webhook endpoint
- Check Application Insights for failure patterns

**missed**: 
- Brief outage detected; notification count indicates lost notifications
- Query recent interactions using `list_interactions` to catch up on missed activity
- Fix the underlying issue causing the outage

**reauthorizationRequired**: 
- Re-authorize the app by running the connector setup flow again
- Update OAuth tokens in Power Platform connection

### Lifecycle Notification Validation

Lifecycle notifications use the same `clientState` as your subscription. Always validate the `clientState` before processing lifecycle events:

```powershell
# PowerShell example
if ($lifecycle.clientState -ne $expectedClientState) {
    Write-Error "Invalid clientState in lifecycle notification"
    return 400
}
```

## Use Cases

### Monitor Copilot Usage in Teams

Subscribe to Teams-specific interactions:
```json
{
  "notification_url": "https://webhook.example.com/teams-copilot",
  "filter_app_class": "IPM.SkypeTeams.Message.Copilot.Teams"
}
```

Use in a Power Automate flow to:
- Log interactions to a SharePoint list
- Create audit records in a database
- Alert on high-volume usage
- Track response patterns

### Meeting Summary Automation

Subscribe to meeting insights:
```json
{
  "user_id": "user@contoso.com",
  "meeting_id": "AAMkADI0...AAAAAAB"
}
```

Use in Copilot Studio to:
- Retrieve meeting summaries automatically
- Post summaries to Teams channels
- Send emails with key takeaways
- Store insights in SharePoint

### Governance & Compliance

Subscribe to tenant-wide interactions without resource data, then query interactions matching compliance criteria:
```json
{
  "filter_expression": "appClass ne 'IPM.SkypeTeams.Message.Copilot.Teams'"
}
```

Use to:
- Audit non-Teams Copilot usage
- Generate usage reports
- Detect policy violations
- Track AI response quality

## Best Practices

### Input Validation

The connector includes built-in validation for all parameters:

| Parameter | Validation | Error |
|-----------|-----------|-------|
| `webhookUrl` / `notificationUrl` | Must be HTTPS, valid URI, max 2048 chars | "webhookUrl must use HTTPS protocol" |
| `expirationDateTime` | Min 1 min from now, max 4,230 min (70.5 hrs) | "expirationDateTime exceeds maximum subscription lifetime" |
| `userId` | Max 255 characters (UPN or object ID) | "userId is invalid" |
| `meetingId` | Max 255 characters | "meetingId is invalid" |
| `subscriptionId` | Max 100 characters, typically GUID | "subscriptionId is invalid" |

**Validation is applied before:**
- Creating or renewing subscriptions
- Testing webhook connectivity
- Retrieving subscription details

This prevents invalid requests from being sent to Microsoft Graph and provides clear error messages for correction.

## Best Practices

1. **Webhook Reliability**
   - Implement retry logic for failed webhook receipts
   - Log all incoming notifications
   - Respond quickly (< 30 seconds)

2. **Subscription Management**
   - Monitor subscription expiration dates
   - Implement automatic renewal 10 minutes before expiration
   - Re-create subscriptions if renewal fails

3. **Performance**
   - Use filtering (`$filter`) to reduce notification volume
   - Set appropriate `expiration_minutes` to balance freshness vs. renewal overhead
   - Process notifications asynchronously to avoid blocking the webhook

4. **Security**
   - Always validate `clientState` before processing
   - Use TLS 1.2+ for webhook endpoints
   - Store subscription IDs securely
   - Rotate client secrets regularly before expiry

5. **Error Handling**
   - Log all Graph API errors with full context
   - Implement exponential backoff for retries
   - Monitor Application Insights for patterns in failures

## Permissions Reference

| Permission | Type | Use Case |
|---|---|---|
| AiEnterpriseInteraction.Read.All | Application | Tenant-wide AI interaction subscriptions |
| AiEnterpriseInteraction.Read | Delegated | Per-user AI interaction subscriptions |
| OnlineMeetingAiInsight.Read.All | Application / Delegated | Meeting AI insight subscriptions |

## Licensing

Requires:
- **Copilot for Microsoft 365** license for the user whose interactions are monitored
- **Power Platform connectors in Copilot Studio** license to use in Copilot Studio
- **Azure subscription** for Application Insights (optional)

## Rate Limiting & Quotas

Microsoft Graph enforces throttling on change notification subscriptions and queries to ensure service health.

### Subscription Limits

| Resource | Limit | Notes |
|----------|-------|-------|
| Active subscriptions per app | 50,000 | Per application registration |
| Subscriptions per tenant | Unlimited | Total across all apps |
| Subscription lifetime | 4,230 minutes (70.5 hours) | Maximum expiration duration |
| Concurrent notification deliveries | 1,000 | Concurrent webhooks from Graph |

### Query Rate Limits (Graph API Calls)

| Endpoint | Limit | Window |
|----------|-------|--------|
| `/subscriptions` (POST, GET, PATCH, DELETE) | 1,000 | 10 seconds |
| `/copilot/interactionHistory/interactions` (GET) | 2,000 | 10 seconds |
| Individual interaction queries (GET) | 2,000 | 10 seconds |

### Handling Throttling (429 Too Many Requests)

Microsoft Graph returns:
```
HTTP/1.1 429 Too Many Requests
Retry-After: 30
```

**Recommended backoff strategy** (exponential with jitter):

```powershell
$retryCount = 0
$maxRetries = 3
$baseDelay = 1  # seconds

while ($retryCount -lt $maxRetries) {
    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $headers
        return $response
    } catch {
        if ($_.Exception.Response.StatusCode -eq 429) {
            $retryCount++
            $delaySeconds = $baseDelay * [Math]::Pow(2, $retryCount - 1)
            $jitter = Get-Random -Minimum 0 -Maximum 1000
            Start-Sleep -Milliseconds ($delaySeconds * 1000 + $jitter)
        } else {
            throw
        }
    }
}
```

### Notification Delivery Throttling

- **Per webhook**: Graph batches and throttles notifications to ~1 request per second per webhook URL
- **Per tenant**: Burst up to 10,000 notifications per minute across all subscriptions
- If your webhook can't keep up, notifications are queued (for ~24 hours) then discarded

**Best practices:**
1. Decouple webhook processing from notification reception (use async queues)
2. Monitor queue depth and scale webhook workers accordingly
3. Avoid calling `get_interaction` synchronously in your webhook handler—queue the IDs first

### Application Registration Quotas

| Quota | Limit |
|-------|-------|
| App roles assigned to service principal | 200 |
| Certificate credentials per app | 10 |
| Client secrets per app | 10 |

## Troubleshooting

### Subscription Creation Fails with 400 Bad Request

**Cause**: Missing or invalid parameter
- Verify `notification_url` is HTTPS with valid certificate
- Ensure `expirationDateTime` is in ISO 8601 format
- If expiration > 1 hour, `lifecycleNotificationUrl` is required

### Notifications Not Received

**Cause**: Webhook endpoint not responding or subscription expired
- Test webhook endpoint with curl: `curl -X POST https://your-webhook/endpoint`
- Check Application Insights logs for failures
- Use `list_subscriptions` to verify subscription is active
- Verify firewall/WAF allows Graph API traffic to your webhook

### Graph API Returns 401 Unauthorized

**Cause**: Authentication issue
- Verify app registration has required permissions
- Confirm admin consent was granted
- Check certificate hasn't expired
- Verify certificate is uploaded to app registration

## Support & Documentation

- [Microsoft Graph Change Notifications](https://learn.microsoft.com/en-us/graph/webhooks)
- [Copilot AI Interactions API](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/ai-services/interaction-export/resources/aiinteraction)
- [Meeting AI Insights API](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/ai-services/meeting-insights/callaiinsight-get)

## Author

Troy Taylor (troy@troystaylor.com)

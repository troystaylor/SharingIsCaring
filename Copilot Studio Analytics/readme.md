# Copilot Studio Analytics Connector

A custom connector for querying Copilot Studio analytics data directly from Dataverse. This connector provides operations to retrieve bot configurations, conversation transcripts, session data, aggregated analytics, and customer satisfaction metrics.

## Features

- **List and retrieve bots** - Query all Copilot Studio bots in your environment
- **Conversation transcripts** - Access full conversation history with parsed content
- **Bot conversation analytics** - Aggregated metrics for conversations over time
- **Topic analytics** - Track topic usage, completion rates, and escalations
- **Session analytics** - Session outcomes, durations, and trends
- **CSAT analytics** - Customer satisfaction scores and feedback
- **Transcript parsing** - Convert raw JSON transcripts to structured data
- **MCP Protocol** - Model Context Protocol support for Copilot Studio agents

## Prerequisites

- Power Platform environment with Copilot Studio
- Azure AD app registration with Dataverse permissions
- Appropriate security roles to access Copilot Studio data

## Dataverse Tables Used

| Table | Description |
|-------|-------------|
| `bot` | Copilot Studio bot configurations |
| `conversationtranscript` | Full conversation transcripts with message content |
| `botcomponent` | Bot components (topics, entities, variables) |
| `msdyn_botsession` | Bot session tracking (if available) |

## Setup Instructions

### 1. Azure AD App Registration

1. Go to **Azure Portal** > **Azure Active Directory** > **App registrations**
2. Create a new registration or use an existing one
3. Add the following API permission:
   - **Dynamics CRM** > `user_impersonation`
4. Grant admin consent for the permission
5. Note the **Application (client) ID** and **Directory (tenant) ID**

### 2. Configure the Connector

1. Import the connector to your Power Platform environment
2. Create a new connection with:
   - **Tenant**: Your Azure AD tenant ID (e.g., `contoso.onmicrosoft.com`)
   - **Domain**: Your Dataverse domain (e.g., `org12345`)
   - Sign in with your Azure AD credentials

### 3. Dataverse Security

Ensure your user account has the following security roles:
- **System Administrator** or **System Customizer** (full access)
- Or a custom role with read access to the bot and conversation tables

## Operations

### ListBots
Retrieves all Copilot Studio bots in the environment.

**Parameters:**
- `$select` (optional): Comma-separated list of columns to return
- `$filter` (optional): OData filter expression
- `$top` (optional): Maximum number of records to return

**Example Response:**
```json
{
  "value": [
    {
      "botid": "12345678-1234-1234-1234-123456789012",
      "name": "Customer Support Bot",
      "publishedby": "admin@contoso.com",
      "createdon": "2024-01-15T10:30:00Z"
    }
  ]
}
```

### GetBot
Retrieves a specific bot by ID.

**Parameters:**
- `botId` (required): The bot's unique identifier

### ListConversationTranscripts
Retrieves conversation transcripts with parsed content.

**Parameters:**
- `$select` (optional): Columns to return
- `$filter` (optional): OData filter (e.g., `_bot_value eq '12345678-...'`)
- `$top` (optional): Maximum records (default: 50)
- `$orderby` (optional): Sort order (e.g., `createdon desc`)

**Response includes:**
- Original transcript data
- `contentParsed`: Parsed JSON content as an object
- `activityCount`: Total number of activities in the conversation
- `userMessageCount`: Count of user messages
- `botMessageCount`: Count of bot responses

### GetConversationTranscript
Retrieves a single transcript by ID with full parsed content.

**Parameters:**
- `transcriptId` (required): The transcript's unique identifier

### ListBotSessions
Retrieves bot session data for engagement tracking.

**Parameters:**
- `$filter` (optional): OData filter expression
- `$top` (optional): Maximum records

### ListBotConversations
Retrieves individual bot conversation records.

**Parameters:**
- `$filter` (optional): OData filter expression
- `$top` (optional): Maximum records

### GetConversationAnalytics
Retrieves aggregated conversation statistics.

**Parameters:**
- `$filter` (optional): Filter by date range or bot
- `$top` (optional): Maximum records to analyze

**Response:**
```json
{
  "rawData": [...],
  "statistics": {
    "totalConversations": 150,
    "conversationsByBot": {
      "bot-id-1": 100,
      "bot-id-2": 50
    },
    "conversationsByDate": {
      "2024-01-15": 25,
      "2024-01-16": 30
    }
  }
}
```

### GetBotConversationAnalytics
Retrieves comprehensive conversation analytics for a specific bot.

**Parameters:**
- `botId` (required): The bot ID to analyze
- `startDate` (optional): Start of date range (defaults to 30 days ago)
- `endDate` (optional): End of date range (defaults to now)

**Response:**
```json
{
  "botId": "12345678-...",
  "botName": "Customer Support Bot",
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-01-31T23:59:59Z",
  "totalConversations": 150,
  "averageMessagesPerConversation": 8.5,
  "averageUserMessagesPerConversation": 4.2,
  "averageDurationSeconds": 245.5,
  "escalationRate": 12.5,
  "escalatedCount": 19,
  "conversationsByDay": [
    { "date": "2025-01-15", "count": 25 },
    { "date": "2025-01-16", "count": 30 }
  ]
}
```

### GetTopicAnalytics
Retrieves topic-level analytics including trigger counts and performance.

**Parameters:**
- `botId` (required): The bot ID to analyze
- `startDate` (optional): Start of date range
- `endDate` (optional): End of date range
- `top` (optional): Number of top topics to return (default: 10)

**Response:**
```json
{
  "botId": "12345678-...",
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-01-31T23:59:59Z",
  "totalTopicsAnalyzed": 25,
  "topics": [
    {
      "topicName": "Order Status",
      "triggerCount": 450,
      "completionRate": 92.5,
      "escalationRate": 5.2
    },
    {
      "topicName": "Returns",
      "triggerCount": 280,
      "completionRate": 85.0,
      "escalationRate": 15.0
    }
  ]
}
```

### GetSessionAnalytics
Retrieves session-level analytics including outcomes and durations.

**Parameters:**
- `botId` (required): The bot ID to analyze
- `startDate` (optional): Start of date range
- `endDate` (optional): End of date range

**Response:**
```json
{
  "botId": "12345678-...",
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-01-31T23:59:59Z",
  "totalSessions": 500,
  "averageSessionDurationSeconds": 180.5,
  "outcomeBreakdown": [
    { "outcome": "resolved", "count": 400, "percentage": 80.0 },
    { "outcome": "escalated", "count": 75, "percentage": 15.0 },
    { "outcome": "abandoned", "count": 25, "percentage": 5.0 }
  ],
  "sessionsByDay": [
    { "date": "2025-01-15", "count": 45 },
    { "date": "2025-01-16", "count": 52 }
  ]
}
```

### GetCSATAnalytics
Retrieves customer satisfaction analytics and feedback.

**Parameters:**
- `botId` (required): The bot ID to analyze
- `startDate` (optional): Start of date range
- `endDate` (optional): End of date range

**Response:**
```json
{
  "botId": "12345678-...",
  "startDate": "2025-01-01T00:00:00Z",
  "endDate": "2025-01-31T23:59:59Z",
  "totalResponses": 120,
  "averageRating": 4.2,
  "csatScore": 85.0,
  "ratingDistribution": [
    { "rating": 1, "count": 5, "percentage": 4.2 },
    { "rating": 2, "count": 8, "percentage": 6.7 },
    { "rating": 3, "count": 10, "percentage": 8.3 },
    { "rating": 4, "count": 42, "percentage": 35.0 },
    { "rating": 5, "count": 55, "percentage": 45.8 }
  ],
  "recentFeedback": [
    {
      "transcriptId": "abc123...",
      "rating": 5,
      "comment": "Very helpful!"
    }
  ]
}
```

### ParseTranscriptContent
Parses a conversation transcript into a structured format.

**Parameters:**
- `transcriptId` (required): The transcript ID to parse

**Response:**
```json
{
  "transcriptId": "abc123...",
  "botId": "12345678-...",
  "startTime": "2025-01-15T10:30:00Z",
  "endTime": "2025-01-15T10:35:00Z",
  "messageCount": 12,
  "userMessageCount": 5,
  "botMessageCount": 7,
  "topicsTriggered": ["Greeting", "Order Status"],
  "messages": [
    {
      "timestamp": "2025-01-15T10:30:00Z",
      "role": "user",
      "text": "Hi, I need to check my order",
      "topicName": "Greeting"
    }
  ],
  "wasEscalated": false,
  "sessionOutcome": "completed"
}
```

### InvokeMCP
Model Context Protocol endpoint for Copilot Studio agent integration. Provides analytics tools via JSON-RPC 2.0.

**Available MCP Tools:**

| Tool | Description |
|------|-------------|
| `list_bots` | List all Copilot Studio bots in the environment |
| `get_conversation_analytics` | Get aggregated conversation metrics for a bot |
| `get_topic_analytics` | Get topic-level analytics with trigger counts and escalation rates |
| `get_csat_analytics` | Get customer satisfaction scores and feedback |
| `get_session_analytics` | Get session outcomes and duration metrics |
| `get_recent_transcripts` | Get recent conversation transcripts with parsed content |
| `parse_transcript` | Parse a specific transcript into structured messages |

**Example - List Bots:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_bots",
    "arguments": { "top": 10 }
  },
  "id": "1"
}
```

**Example - Get Conversation Analytics:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "get_conversation_analytics",
    "arguments": {
      "bot_id": "12345678-1234-1234-1234-123456789012",
      "start_date": "2025-01-01T00:00:00Z",
      "end_date": "2025-01-31T23:59:59Z"
    }
  },
  "id": "2"
}
```

## Common OData Filters

### Filter by bot
```
_bot_value eq '12345678-1234-1234-1234-123456789012'
```

### Filter by date range
```
createdon ge 2024-01-01T00:00:00Z and createdon le 2024-01-31T23:59:59Z
```

### Filter by conversation state
```
statecode eq 0
```

## Power Automate Examples

### Get daily conversation count
1. Use **ListConversationTranscripts** with filter:
   ```
   createdon ge @{formatDateTime(utcNow(), 'yyyy-MM-dd')}T00:00:00Z
   ```
2. Use **Length** expression on the `value` array

### Export transcripts to SharePoint
1. **ListConversationTranscripts** for a specific bot
2. **Apply to each** on `value`
3. **Create file** in SharePoint with transcript content

### Send analytics summary email
1. **GetConversationAnalytics** with date filter
2. **Compose** HTML email body with statistics
3. **Send an email** with the summary

## Troubleshooting

### 401 Unauthorized
- Verify the OAuth connection is properly configured
- Check that admin consent was granted for Dynamics CRM permissions
- Ensure your user has access to the Dataverse environment

### 403 Forbidden
- Check security role assignments
- Verify table-level permissions for bot and conversation tables

### Empty Results
- Confirm Copilot Studio bots exist in the environment
- Check that conversations have been recorded
- Verify filter expressions are correct

## Application Insights Logging

To enable telemetry, update [script.csx](script.csx) with your Application Insights connection string:

```csharp
private static readonly string AppInsightsConnectionString = "InstrumentationKey=your-key-here;...";
```

Events logged:
- `CopilotStudioAnalytics_Request` - Incoming request details
- `CopilotStudioAnalytics_Response` - Response status
- `CopilotStudioAnalytics_Error` - Error details

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-01-27 | Initial release |
| 1.1.0 | 2026-02-09 | Added GetBotConversationAnalytics, GetTopicAnalytics, GetSessionAnalytics, GetCSATAnalytics, ParseTranscriptContent, InvokeMCP |

## Resources

- [Dataverse Web API Reference](https://docs.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [Copilot Studio Documentation](https://docs.microsoft.com/en-us/microsoft-copilot-studio/)
- [OData Query Options](https://docs.microsoft.com/en-us/power-apps/developer/data-platform/webapi/query-data-web-api)

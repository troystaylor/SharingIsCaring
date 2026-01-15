# Graph Power Orchestration

Power MCP tool server exposing Microsoft Graph operations via Model Context Protocol for Copilot Studio agents.

## Features

### Core Capabilities
- **Dynamic Discovery**: Uses MS Learn MCP Server to discover Graph API operations in real-time
- **Chained MCP Architecture**: Acts as both MCP Server (for Copilot Studio) and MCP Client (calling MS Learn MCP)
- **Delegated Authentication**: Uses OBO (On-Behalf-Of) with user's permissions
- **User-Friendly Error Handling**: Permission errors clearly distinguish connector issues from org policy issues
- **Comprehensive Graph Coverage**: Supports all Microsoft Graph v1.0 and beta endpoints

### Smart Defaults & Optimization
- **Response Summarization**: Automatically strips large HTML content from email/calendar bodies to reduce response size
- **Collection Limits**: Auto-adds `$top=25` to collection queries to prevent oversized responses
- **Calendar Intelligence**: Automatically adds date range defaults for calendarView queries (today + 7 days)
- **Permission Hints**: Discovered operations include required Graph permissions to help users understand access requirements
- **Discovery Caching**: Results are cached for 10 minutes to reduce redundant MS Learn MCP calls

### Reliability & Performance
- **Throttling Protection**: Handles 429 (Too Many Requests) with automatic retry using Retry-After headers
- **Batch Support**: Execute up to 20 Graph requests in a single call using the `$batch` API
- **Pagination Metadata**: Responses include `hasMore` and `nextLink` hints for paged results
- **Query Validation**: Catches common mistakes (unresolved placeholders, invalid paths) before calling Graph

### Telemetry (Optional)
- **Application Insights Integration**: Add your connection string to enable request/tool telemetry

## Tools

| Tool | Description |
|------|-------------|
| `discover_graph` | Search MS Learn documentation to find Graph API operations matching your intent. Returns endpoints with methods, descriptions, and required permissions. |
| `invoke_graph` | Execute any Microsoft Graph API request with the user's delegated permissions. Supports all OData query parameters. |
| `batch_invoke_graph` | Execute multiple Graph API requests in a single batch call (up to 20 requests). More efficient for multi-step workflows. |

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Copilot Studio Agent                         │
└─────────────────────────────────────────────────────────────────┘
                              │ MCP Protocol
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              Graph Power Orchestration Connector                │
│  ┌─────────────────┐              ┌──────────────────────────┐  │
│  │   MCP Server    │              │      MCP Client          │  │
│  │  (for Copilot)  │              │  (calls MS Learn MCP)    │  │
│  └────────┬────────┘              └────────────┬─────────────┘  │
│           │                                    │                │
│           ▼                                    ▼                │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                     script.csx                              ││
│  │  • discover_graph → MS Learn MCP → Parse Graph operations   ││
│  │  • invoke_graph → Microsoft Graph API → Return results      ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌─────────────────────────┐     ┌─────────────────────────────────┐
│   MS Learn MCP Server   │     │     Microsoft Graph API         │
│ learn.microsoft.com/api │     │    graph.microsoft.com          │
│     (No Auth Required)  │     │   (OBO Delegated Auth)          │
└─────────────────────────┘     └─────────────────────────────────┘
```

## Prerequisites

### App Registration Setup

1. **Create App Registration** in Microsoft Entra ID
2. **Add Delegated Permissions** for Microsoft Graph (see [Required Scopes](#required-scopes))
3. **Grant Admin Consent** for all permissions
4. **Configure Authentication**:
   - Redirect URI: `https://global.consent.azure-apim.net/redirect`
   - Enable ID tokens and Access tokens

### Custom Connector Setup

1. Import the connector using the files in this folder
2. Configure OAuth 2.0 with your app registration
3. Create a connection using your Microsoft account

## Required Scopes

Add these **delegated permissions** to your app registration. Admin consent is required once.

### Core Scopes (Recommended Minimum)
```
User.Read
User.ReadBasic.All
Mail.Read
Mail.ReadWrite
Mail.Send
Calendars.Read
Calendars.ReadWrite
Files.Read.All
Files.ReadWrite.All
Sites.Read.All
Sites.ReadWrite.All
```

### Extended Scopes (Full Coverage)
```
# Users & Groups
User.Read.All
User.ReadWrite.All
Group.Read.All
Group.ReadWrite.All
GroupMember.Read.All
GroupMember.ReadWrite.All
Directory.Read.All

# Teams
Team.ReadBasic.All
Team.Create
Channel.ReadBasic.All
Channel.Create
ChannelMessage.Read.All
ChannelMessage.Send
Chat.Read
Chat.ReadWrite
ChatMessage.Read
ChatMessage.Send

# Mail & Calendar
Mail.Read
Mail.ReadWrite
Mail.Send
Calendars.Read
Calendars.ReadWrite
Contacts.Read
Contacts.ReadWrite

# Files & Sites
Files.Read.All
Files.ReadWrite.All
Sites.Read.All
Sites.ReadWrite.All
Sites.Manage.All

# Tasks & Planner
Tasks.Read
Tasks.ReadWrite
Tasks.Read.Shared
Tasks.ReadWrite.Shared

# Security & Compliance
SecurityEvents.Read.All
AuditLog.Read.All

# Reports
Reports.Read.All
```

> **Note**: There is a limit of 400 permissions per app registration. See [Supported accounts validation](https://learn.microsoft.com/en-us/entra/identity-platform/supported-accounts-validation) for details.

## Zero Trust Compliance

This connector follows Microsoft Zero Trust principles:

| Principle | Implementation |
|-----------|----------------|
| **Verify explicitly** | OBO token validates user identity on every Graph request |
| **Least privilege** | User can only access resources they have Entra permissions for |
| **Assume breach** | Even if connector is compromised, attacker is limited to user's access |

**Important**: Granting broad scopes to the app registration does **not** grant users access to all resources. The user's actual Entra ID permissions determine what they can access. The app registration scopes define the *ceiling* of what the connector can do on behalf of users who already have those permissions.

## Error Handling

The connector distinguishes between different error types with user-friendly messaging:

| Error Type | HTTP Status | User Message |
|------------|-------------|--------------|
| `session_expired` | 401 | Your session has expired. Please reconnect. |
| `permission_denied` | 403 | You don't have permission. Contact your IT admin. |
| `not_found_or_no_access` | 404 | Resource not found or no permission to view it. |
| `service_error` | 5xx | Graph service error. Try again later. |

Example error response:
```json
{
  "success": false,
  "errorType": "permission_denied",
  "userMessage": "You don't have permission to access emails. This is controlled by your organization's Entra ID settings, not this connector.",
  "action": "Contact your IT administrator to request the necessary permissions.",
  "technicalDetails": {
    "httpStatus": 403,
    "graphError": "Authorization_RequestDenied",
    "resource": "/users/someone@contoso.com/messages"
  }
}
```

## Usage Examples

### Discover Graph Operations

```json
{
  "query": "list my calendar events for this week",
  "category": "calendar"
}
```

Response includes permission hints and caching:
```json
{
  "success": true,
  "operationCount": 3,
  "operations": [
    {
      "endpoint": "/me/calendar/events",
      "method": "GET",
      "description": "List events in the user's primary calendar",
      "requiredPermissions": ["Calendars.Read"]
    },
    {
      "endpoint": "/me/calendarView",
      "method": "GET",
      "description": "Get calendar view for a date range",
      "requiredPermissions": ["Calendars.Read"]
    }
  ]
}
```

### Invoke Graph API

The connector automatically optimizes requests:
- Adds `$top=25` for collection endpoints if not specified
- Adds date defaults for calendarView queries
- Orders calendar events by start time
- Strips HTML from response bodies

```json
{
  "endpoint": "/me/calendar/events",
  "method": "GET",
  "queryParams": {
    "$select": "subject,start,end,location"
  }
}
```

Response includes pagination metadata:
```json
{
  "success": true,
  "endpoint": "/me/calendar/events",
  "method": "GET",
  "hasMore": true,
  "nextLink": "https://graph.microsoft.com/v1.0/me/calendar/events?$skip=25",
  "nextPageHint": "To get more results, call invoke_graph again with the full nextLink URL as the endpoint",
  "data": { "value": [...] }
}
```

### Send an Email

```json
{
  "endpoint": "/me/sendMail",
  "method": "POST",
  "body": {
    "message": {
      "subject": "Meeting Tomorrow",
      "body": {
        "contentType": "Text",
        "content": "Don't forget our meeting at 2pm."
      },
      "toRecipients": [
        {
          "emailAddress": {
            "address": "colleague@contoso.com"
          }
        }
      ]
    }
  }
}
```

### Batch Multiple Requests

Execute multiple Graph operations in a single call for better efficiency:

```json
{
  "requests": [
    { "id": "profile", "endpoint": "/me", "method": "GET" },
    { "id": "emails", "endpoint": "/me/messages", "method": "GET" },
    { "id": "events", "endpoint": "/me/calendar/events", "method": "GET" }
  ]
}
```

Response:
```json
{
  "success": true,
  "batchSize": 3,
  "successCount": 3,
  "errorCount": 0,
  "responses": [
    { "id": "profile", "status": 200, "success": true, "data": {...} },
    { "id": "emails", "status": 200, "success": true, "data": {...} },
    { "id": "events", "status": 200, "success": true, "data": {...} }
  ]
}
```

## Files

| File | Description |
|------|-------------|
| `apiDefinition.swagger.json` | OpenAPI definition with MCP protocol declaration |
| `apiProperties.json` | Connector properties and capabilities |
| `script.csx` | C# script handling MCP protocol, Graph calls, and optimizations |
| `readme.md` | This documentation |

## Configuration

### Application Insights (Optional)

To enable telemetry, add your Application Insights connection string to the script:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=xxx;IngestionEndpoint=https://...";
```

This enables tracking of:
- Request received/completed events with duration
- MCP method calls
- Tool execution success/failure
- Error details

### Smart Defaults

The connector applies these defaults automatically (configurable in script.csx):

| Setting | Default | Description |
|---------|---------|-------------|
| `DEFAULT_TOP_LIMIT` | 25 | Max items for collection queries |
| `CACHE_EXPIRY_MINUTES` | 10 | Discovery cache duration |
| `MAX_BODY_LENGTH` | 500 | Truncation limit for HTML bodies |
| `MAX_RETRIES` | 3 | Retry attempts for throttled requests |

## Related Resources

- [Microsoft Graph API Reference](https://learn.microsoft.com/graph/api/overview)
- [MS Learn MCP Server](https://learn.microsoft.com/api/mcp)
- [Copilot Studio Custom Connectors](https://learn.microsoft.com/microsoft-copilot-studio/configure-custom-connector)
- [OBO Authentication](https://learn.microsoft.com/microsoft-copilot-studio/configure-enduser-authentication)

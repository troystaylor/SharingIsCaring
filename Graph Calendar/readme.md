# Graph Calendar MCP Connector

Microsoft Graph Calendar API connector for enterprise calendar management, optimized for leadership team scheduling and visibility.

## Purpose

Solves the US Leadership Team calendar coordination challenges:
- ✅ **Centralized visibility** - View calendars across 40+ leadership team members
- ✅ **Find meeting times** - AI-powered optimal time suggestions using `findMeetingTimes`
- ✅ **Free/busy status** - Check availability for multiple users simultaneously
- ✅ **Master calendar view** - Get aggregated calendar events across users
- ✅ **Automated scheduling** - Create meetings with Teams integration

## MCP Tools (25 total)

### Calendar View Tools
| Tool | Description |
|------|-------------|
| `getMyCalendarView` | Get my calendar events within a date range |
| `getUserCalendarView` | Get a specific user's calendar events |
| `getMultipleCalendarViews` | Get calendars for multiple users at once (leadership team view) |

### Availability Tools
| Tool | Description |
|------|-------------|
| `getSchedule` | Get free/busy status for multiple users |
| `findMeetingTimes` | Find optimal meeting times when multiple attendees are available |

### Event Management Tools
| Tool | Description |
|------|-------------|
| `createEvent` | Create a new calendar event with optional Teams meeting |
| `createEventForUser` | Create event on behalf of another user |
| `updateEvent` | Update an existing event in my calendar |
| `updateUserEvent` | Update an event in another user's calendar |
| `deleteEvent` | Delete a calendar event from my calendar |
| `deleteUserEvent` | Delete a calendar event from another user's calendar |
| `cancelEvent` | Cancel meeting and notify attendees |

### Event Response Tools
| Tool | Description |
|------|-------------|
| `acceptEvent` | Accept a meeting invitation |
| `declineEvent` | Decline a meeting invitation |
| `tentativelyAcceptEvent` | Tentatively accept a meeting |

### Event Retrieval Tools
| Tool | Description |
|------|-------------|
| `getEvent` | Get details of a specific event from my calendar |
| `getUserEvent` | Get details of a specific event from another user's calendar |
| `listMyEvents` | List my upcoming events |
| `listUserEvents` | List events from another user's calendar |

### User & Group Tools
| Tool | Description |
|------|-------------|
| `listUsers` | List organization users (filter by department for leadership) |
| `getUser` | Get user details |
| `listGroups` | List groups (find leadership team group) |
| `listGroupMembers` | Get members of a group |

### Calendar Tools
| Tool | Description |
|------|-------------|
| `listMyCalendars` | List my calendars |
| `listUserCalendars` | List a user's calendars |

## Setup Instructions

### 1. Register Azure AD Application

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Click **New registration**
   - Name: `Graph Calendar Connector`
   - Supported account types: **Accounts in this organizational directory only**
   - Redirect URI: **Web** → `https://global.consent.azure-apim.net/redirect`
3. After creation, note the **Application (client) ID**
4. Go to **Certificates & secrets** → **New client secret** → Note the secret value
5. Go to **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions**:
   - `Calendars.Read`
   - `Calendars.Read.Shared`
   - `Calendars.ReadWrite`
   - `Calendars.ReadWrite.Shared`
   - `User.Read`
   - `User.Read.All`
   - `Group.Read.All`
6. Click **Grant admin consent**

### 2. Update apiProperties.json

Replace `[[REPLACE_WITH_APP_ID]]` with your Application ID.

### 3. Create Custom Connector

1. Go to [Power Platform Maker Portal](https://make.powerapps.com)
2. Navigate to **Custom connectors** → **+ New custom connector** → **Import an OpenAPI file**
3. Upload `apiDefinition.swagger.json`
4. On the **Security** tab:
   - Authentication type: **OAuth 2.0**
   - Identity Provider: **Azure Active Directory**
   - Client ID: Your Application ID
   - Client Secret: Your secret
   - Resource URL: `https://graph.microsoft.com`
5. On the **Code** tab:
   - Enable **Code**
   - Upload `script.csx`
6. Click **Create connector**

### 4. Test Connection

1. Click **Test** tab → **+ New connection**
2. Sign in with your Microsoft account
3. Test the `InvokeMCP` operation with:
```json
{
  "jsonrpc": "2.0",
  "method": "initialize",
  "id": "1",
  "params": {
    "protocolVersion": "2025-12-01",
    "clientInfo": { "name": "test" }
  }
}
```

## Required Permissions

| Permission | Type | Purpose |
|------------|------|---------|
| `Calendars.Read` | Delegated | Read user's calendar |
| `Calendars.Read.Shared` | Delegated | Read shared/delegated calendars |
| `Calendars.ReadWrite` | Delegated | Create/update/delete events |
| `Calendars.ReadWrite.Shared` | Delegated | Manage shared calendars |
| `User.Read` | Delegated | Read current user profile |
| `User.Read.All` | Delegated | Read all users (for finding leadership team) |
| `Group.Read.All` | Delegated | Read groups and membership |

## Example Scenarios

### 1. Get Leadership Team Availability
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": "1",
  "params": {
    "name": "getSchedule",
    "arguments": {
      "schedules": ["ceo@company.com", "cfo@company.com", "coo@company.com"],
      "startDateTime": "2026-01-15T08:00:00",
      "endDateTime": "2026-01-15T18:00:00",
      "timeZone": "Pacific Standard Time"
    }
  }
}
```

### 2. Find Meeting Time for Multiple Leaders
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": "2",
  "params": {
    "name": "findMeetingTimes",
    "arguments": {
      "attendees": ["ceo@company.com", "cfo@company.com", "coo@company.com"],
      "meetingDuration": "PT1H",
      "startDateTime": "2026-01-15T08:00:00",
      "endDateTime": "2026-01-17T18:00:00",
      "timeZone": "Pacific Standard Time",
      "maxCandidates": 5
    }
  }
}
```

### 3. Schedule Meeting with Teams
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": "3",
  "params": {
    "name": "createEvent",
    "arguments": {
      "subject": "Leadership Quarterly Review",
      "startDateTime": "2026-01-15T14:00:00",
      "endDateTime": "2026-01-15T15:00:00",
      "timeZone": "Pacific Standard Time",
      "attendees": ["ceo@company.com", "cfo@company.com"],
      "isOnlineMeeting": true,
      "body": "Quarterly review meeting for US Leadership Team",
      "importance": "high"
    }
  }
}
```

### 4. View Master Calendar (Multiple Users)
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "id": "4",
  "params": {
    "name": "getMultipleCalendarViews",
    "arguments": {
      "userEmails": ["ceo@company.com", "cfo@company.com", "coo@company.com"],
      "startDateTime": "2026-01-15T00:00:00",
      "endDateTime": "2026-01-15T23:59:59",
      "top": 50
    }
  }
}
```

## Copilot Studio Integration

Add this connector as an MCP action in Copilot Studio:
1. Go to **Actions** → **+ Add action** → **Custom connector**
2. Select **Graph Calendar**
3. The agent will automatically discover all 25 tools

## Files

| File | Description |
|------|-------------|
| `apiDefinition.swagger.json` | OpenAPI 2.0 specification with all endpoints |
| `apiProperties.json` | Connector configuration with OAuth settings |
| `script.csx` | C# script implementing MCP protocol and 25 tools |
| `readme.md` | This documentation |

## Comparison: Bookings vs Graph Calendar

| Capability | Microsoft Bookings | Graph Calendar |
|------------|-------------------|----------------|
| View Outlook calendars | ❌ | ✅ |
| Find meeting times | ❌ | ✅ |
| Free/busy across users | ❌ | ✅ |
| Create Teams meetings | ❌ | ✅ |
| Master calendar view | ❌ | ✅ |
| Customer scheduling | ✅ | ❌ |

**Graph Calendar is the correct choice for enterprise calendar management.**

# Slack

## Overview

Power Platform MCP connector for Slack using Power Mission Control. Provides progressive API discovery with `scan_slack`, `launch_slack`, and `sequence_slack` tools plus 8 typed tools for high-value messaging operations.

## Tools

### Typed Tools (Direct Access)

| Tool | Description |
|------|-------------|
| `send_message` | Send a message to a channel or conversation |
| `search_messages` | Search for messages across the workspace |
| `list_channels` | List channels in the workspace |
| `get_channel_history` | Get recent messages from a channel |
| `get_user_info` | Get user profile information |
| `list_users` | List all workspace users |
| `add_reaction` | Add an emoji reaction to a message |
| `upload_file` | Upload a text-based file to Slack |

### Orchestration Tools (Scan → Launch → Sequence)

| Tool | Description |
|------|-------------|
| `scan_slack` | Discover available Slack API operations by intent |
| `launch_slack` | Execute any Slack API method |
| `sequence_slack` | Execute multiple Slack API methods in one call |

The capability index covers 70 Slack API methods across these domains: messaging, channels, users, reactions, pins, files, search, bookmarks, reminders, usergroups, emoji, dnd, stars, team, and auth.

## Prerequisites

1. A Slack workspace with admin access
2. A Slack app configured at [api.slack.com/apps](https://api.slack.com/apps)
3. OAuth 2.0 credentials (Client ID and Client Secret)

## Slack App Setup

1. Go to [api.slack.com/apps](https://api.slack.com/apps) and click **Create New App**
2. Choose **From scratch** and provide a name and workspace
3. Navigate to **OAuth & Permissions** in the sidebar
4. Under **Redirect URLs**, add: `https://global.consent.azure-apim.net/redirect`
5. Under **User Token Scopes**, add the following scopes:
   - `channels:read`
   - `channels:history`
   - `channels:write`
   - `chat:write`
   - `users:read`
   - `users:read.email`
   - `users.profile:read`
   - `files:read`
   - `files:write`
   - `reactions:read`
   - `reactions:write`
   - `pins:read`
   - `pins:write`
   - `search:read`
   - `groups:read`
   - `groups:history`
   - `im:read`
   - `im:history`
   - `mpim:read`
   - `mpim:history`
   - `reminders:read`
   - `reminders:write`
   - `bookmarks:read`
   - `bookmarks:write`
   - `usergroups:read`
   - `usergroups:write`
   - `emoji:read`
   - `dnd:read`
   - `dnd:write`
   - `team:read`
6. Note your **Client ID** and **Client Secret** from the **Basic Information** page

## Connector Setup

1. Import the connector into Power Platform using the PAC CLI or the custom connectors portal
2. During connection setup, enter your Slack app's Client ID and Client Secret
3. Authorize with Slack when prompted — this generates a user token (xoxp-)

## Authentication

This connector uses **OAuth 2.0 authorization code flow** with user tokens (`xoxp-`). The token acts on behalf of the authenticated user.

| Setting | Value |
|---------|-------|
| Authorization URL | `https://slack.com/oauth/v2/authorize` |
| Token URL | `https://slack.com/api/oauth.v2.access` |
| Refresh URL | `https://slack.com/api/oauth.v2.access` |
| Token type | User token (xoxp-) |

> **Token Rotation:** Slack user tokens (`xoxp-`) do not support refresh tokens unless [token rotation](https://api.slack.com/authentication/rotation) is explicitly enabled on your Slack app. Navigate to **OAuth & Permissions → Advanced token security** and enable **Token Rotation** for refresh tokens to work. Without this, tokens will eventually expire and users must re-authorize.

## Usage Examples

### Send a message

Use the `send_message` typed tool:
- **channel**: `C0123456789` (channel ID)
- **text**: `Hello from Power Platform!`

### Search messages

Use the `search_messages` typed tool:
- **query**: `project update from:@john`

### Discover and execute any Slack API method

1. Call `scan_slack` with **query**: `"create a reminder"`
2. Review the results to find `reminders.add`
3. Call `launch_slack` with **endpoint**: `reminders.add` and **body**: `{ "text": "Review PRs", "time": "in 30 minutes" }`

### Batch operations

Use `sequence_slack` to execute multiple operations:
```json
{
  "requests": [
    { "id": "1", "endpoint": "conversations.list", "body": { "limit": 5 } },
    { "id": "2", "endpoint": "users.list", "body": { "limit": 5 } }
  ]
}
```

## Slack API Notes

- All Slack API methods use **POST** (even read operations)
- API URL pattern: `https://slack.com/api/{method.name}`
- Responses always contain `"ok": true/false`
- Pagination uses the `cursor` / `next_cursor` pattern — pass the returned `nextCursor` value as the `cursor` parameter for the next page
- Rate limiting is tier-based (Tier 1–4) with HTTP 429 and `Retry-After` header (handled automatically with retry)

## REST Operations (Power Automate / Power Apps)

In addition to MCP tools for Copilot Studio, this connector exposes the following Swagger operations for use in Power Automate flows and Power Apps:

| Operation | Operation ID | Description |
|-----------|-------------|-------------|
| Send Message | `SendMessage` | Send a message to a channel or conversation |
| Search Messages | `SearchMessages` | Search for messages across the workspace |
| List Channels | `ListChannels` | List channels in the workspace |
| Get Channel History | `GetChannelHistory` | Get recent messages from a channel |
| Get User Info | `GetUserInfo` | Get user profile information |
| List Users | `ListUsers` | List all workspace users |
| Add Reaction | `AddReaction` | Add an emoji reaction to a message |
| Upload File | `UploadFile` | Upload a text-based file to Slack |

## Author

Troy Taylor — troy@troystaylor.com

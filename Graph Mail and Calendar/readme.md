# Graph Mail and Calendar

A Power Platform custom connector for Microsoft Graph Outlook mail and calendar operations. Combines comprehensive REST operations with MCP (Model Context Protocol) tools for Copilot Studio integration. Includes keyword search across all list queries.

## Overview

This connector provides unified access to Outlook mail and calendar through Microsoft Graph API v1.0, with:
- **76 REST operations** for Power Automate flows
- **77 MCP tools** for Copilot Studio agents (75 matching REST ops + 2 bonus composite tools)
- **Keyword search (`$search`)** on all list operations
- **KQL search** via Microsoft Graph Search API
- **Application Insights** telemetry

## Prerequisites

- Power Platform environment with custom connector support
- Azure AD app registration with the following **delegated** permissions:
  - `Mail.ReadWrite` — Read and write mail
  - `Mail.Send` — Send mail
  - `Calendars.ReadWrite` — Read and write calendars
  - `User.Read` — Sign in and read user profile
  - `User.ReadBasic.All` — Read all users' basic profiles
  - `MailboxSettings.ReadWrite` — Read and write mailbox rules
  - `People.Read` — Read relevant people
  - `offline_access` — Maintain access with refresh tokens
- Redirect URI: `https://global.consent.azure-apim.net/redirect`

## Setup

1. **Register an Azure AD application** in the Azure Portal
2. Add the five delegated permissions listed above
3. Generate a client secret
4. Update `apiProperties.json` with your `clientId`
5. Update `script.csx` with your Application Insights connection string (optional)
6. Grant admin consent for `MailboxSettings.ReadWrite` and `People.Read` scopes
7. Deploy using PAC CLI:
   ```
   pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx
   ```

## REST Operations (76)

### Mail Operations
| Operation | Method | Path | Search |
|-----------|--------|------|--------|
| List Messages | GET | /me/messages | ✅ `$search` |
| Get Message | GET | /me/messages/{id} | |
| Create Draft | POST | /me/messages | |
| Update Message | PATCH | /me/messages/{id} | |
| Delete Message | DELETE | /me/messages/{id} | |
| Send Mail | POST | /me/sendMail | |
| Get Mail Tips | POST | /me/getMailTips | |
| Send Draft | POST | /me/messages/{id}/send | |
| Reply | POST | /me/messages/{id}/reply | |
| Reply All | POST | /me/messages/{id}/replyAll | |
| Forward Message | POST | /me/messages/{id}/forward | |
| Move Message | POST | /me/messages/{id}/move | |
| Copy Message | POST | /me/messages/{id}/copy | |
| Create Reply Draft | POST | /me/messages/{id}/createReply | |
| Create Reply All Draft | POST | /me/messages/{id}/createReplyAll | |
| Create Forward Draft | POST | /me/messages/{id}/createForward | |
| List Attachments | GET | /me/messages/{id}/attachments | |
| Get Attachment | GET | /me/messages/{id}/attachments/{attachmentId} | |
| Get Attachment Content | GET | /me/messages/{id}/attachments/{attachmentId}/$value | |
| Create Attachment | POST | /me/messages/{id}/attachments | |
| Delete Attachment | DELETE | /me/messages/{id}/attachments/{attachmentId} | |
| Get Mail Folder | GET | /me/mailFolders/{folderId} | |
| Update Mail Folder | PATCH | /me/mailFolders/{folderId} | |
| Delete Mail Folder | DELETE | /me/mailFolders/{folderId} | |
| List Child Folders | GET | /me/mailFolders/{folderId}/childFolders | |
| List Folder Messages | GET | /me/mailFolders/{folderId}/messages | ✅ `$search` |
| Create Message in Folder | POST | /me/mailFolders/{folderId}/messages | |
| List Mail Folders | GET | /me/mailFolders | ✅ `$search` |
| Create Mail Folder | POST | /me/mailFolders | |
| List Message Rules | GET | /me/mailFolders/inbox/messageRules | |
| Create Message Rule | POST | /me/mailFolders/inbox/messageRules | |
| Get Message Rule | GET | /me/mailFolders/inbox/messageRules/{ruleId} | |
| Update Message Rule | PATCH | /me/mailFolders/inbox/messageRules/{ruleId} | |
| Delete Message Rule | DELETE | /me/mailFolders/inbox/messageRules/{ruleId} | |
| List Sent Messages | GET | /me/mailFolders/sentitems/messages | ✅ `$search` |
| Search Messages (KQL) | POST | /search/query | ✅ KQL |

### Calendar Operations
| Operation | Method | Path | Search |
|-----------|--------|------|--------|
| List Events | GET | /me/events | ✅ `$search` |
| Get Event | GET | /me/events/{id} | |
| Create Event | POST | /me/events | |
| Update Event | PATCH | /me/events/{id} | |
| Delete Event | DELETE | /me/events/{id} | |
| Accept Event | POST | /me/events/{id}/accept | |
| Decline Event | POST | /me/events/{id}/decline | |
| Tentatively Accept | POST | /me/events/{id}/tentativelyAccept | |
| Cancel Event | POST | /me/events/{id}/cancel | |
| Snooze Reminder | POST | /me/events/{id}/snoozeReminder | |
| Dismiss Reminder | POST | /me/events/{id}/dismissReminder | |
| List Event Instances | GET | /me/events/{id}/instances | (date range) |
| List Event Attachments | GET | /me/events/{id}/attachments | |
| Create Event Attachment | POST | /me/events/{id}/attachments | |
| Get Event Attachment | GET | /me/events/{id}/attachments/{attachmentId} | |
| Delete Event Attachment | DELETE | /me/events/{id}/attachments/{attachmentId} | |
| Calendar View | GET | /me/calendarView | (date range) |
| Find Meeting Times | POST | /me/findMeetingTimes | |
| Get Schedule | POST | /me/calendar/getSchedule | |
| List Calendars | GET | /me/calendars | ✅ `$search` |
| Create Calendar | POST | /me/calendars | |
| Get Calendar | GET | /me/calendars/{calendarId} | |
| Update Calendar | PATCH | /me/calendars/{calendarId} | |
| Delete Calendar | DELETE | /me/calendars/{calendarId} | |

### User-Scoped Operations
| Operation | Method | Path | Search |
|-----------|--------|------|--------|
| List User Messages | GET | /users/{userId}/messages | ✅ `$search` |
| Get User Message | GET | /users/{userId}/messages/{id} | |
| Send User Mail | POST | /users/{userId}/sendMail | |
| List User Events | GET | /users/{userId}/events | ✅ `$search` |
| Get User Event | GET | /users/{userId}/events/{id} | |
| User Calendar View | GET | /users/{userId}/calendarView | (date range) |

### User & Organization Operations
| Operation | Method | Path | Search |
|-----------|--------|------|--------|
| Get My Profile | GET | /me | |
| Get My Manager | GET | /me/manager | |
| Get My Direct Reports | GET | /me/directReports | |
| Get User Profile | GET | /users/{userIdentifier} | |
| Get User's Manager | GET | /users/{userIdentifier}/manager | |
| Get Direct Reports | GET | /users/{userIdentifier}/directReports | |
| Get My Photo Metadata | GET | /me/photo | |
| List People | GET | /me/people | ✅ `$search` |
| List Users | GET | /users | ✅ `$search` |

## MCP Tools (77)

### Mail Tools (38)
| Tool | Description |
|------|-------------|
| `list_messages` | List inbox messages with keyword search and OData filters |
| `search_messages` | KQL-powered search across subject, body, sender, attachments |
| `get_message` | Get full message by ID with optional HTML body |
| `send_mail` | Send email with recipients, subject, body, CC/BCC |
| `create_draft` | Create draft message for later sending |
| `update_message` | Update subject, body, categories, read status |
| `delete_message` | Delete a message |
| `send_draft` | Send an existing draft |
| `reply` | Reply to sender |
| `reply_all` | Reply to all recipients |
| `forward` | Forward to new recipients |
| `list_sent` | List sent items with search |
| `list_folders` | List mail folders with search |
| `move_message` | Move message to folder |
| `copy_message` | Copy message to folder |
| `list_attachments` | List message attachments |
| `get_attachment` | Get a specific attachment with metadata and base64 content |
| `get_attachment_content` | Download raw binary content of a file attachment |
| `create_attachment` | Add a file attachment to a draft message |
| `delete_attachment` | Delete an attachment from a draft message |
| `get_mail_folder` | Get folder properties by ID or well-known name |
| `list_folder_messages` | List messages in a specific mail folder with search and filters |
| `create_mail_folder` | Create a new mail folder or subfolder |
| `update_mail_folder` | Update/rename a mail folder |
| `delete_mail_folder` | Delete a mail folder and its contents |
| `list_child_folders` | List child (sub) folders of a mail folder |
| `create_draft_in_folder` | Create a draft message in a specific folder |
| `get_writing_samples` | Fetch recent sent emails to analyze writing voice and style for drafting |
| `draft_with_style_guide` | Combine a company style guide with sent email samples for voice-matched drafting |
| `create_reply_draft` | Create a reply draft message (without sending) |
| `create_reply_all_draft` | Create a reply-all draft message (without sending) |
| `create_forward_draft` | Create a forward draft with optional recipients and comment |
| `get_mail_tips` | Get mail tips (auto-reply status, mailbox full, delivery restrictions) |
| `list_message_rules` | List inbox message rules |
| `create_message_rule` | Create an inbox message rule |
| `get_message_rule` | Get a specific message rule |
| `update_message_rule` | Update a message rule |
| `delete_message_rule` | Delete a message rule |

### Calendar Tools (24)
| Tool | Description |
|------|-------------|
| `list_events` | List events with keyword search |
| `calendar_view` | Get events in date/time range (supports other users) |
| `get_event` | Get event by ID |
| `create_event` | Create event with Teams meetings, recurrence, attendees |
| `update_event` | Update event properties |
| `delete_event` | Delete event |
| `accept_event` | Accept meeting invitation |
| `decline_event` | Decline meeting invitation |
| `tentatively_accept` | Tentatively accept invitation |
| `cancel_event` | Cancel meeting and notify attendees |
| `find_meeting_times` | Find available meeting slots |
| `get_schedule` | Get free/busy availability |
| `list_calendars` | List user's calendars |
| `get_calendar` | Get specific calendar properties |
| `create_calendar` | Create a new secondary calendar |
| `update_calendar` | Update calendar name or color |
| `delete_calendar` | Delete a secondary calendar |
| `list_event_instances` | List occurrences of a recurring event |
| `list_event_attachments` | List attachments on a calendar event |
| `create_event_attachment` | Add a file attachment to a calendar event |
| `snooze_reminder` | Snooze a calendar event reminder to a new time |
| `dismiss_reminder` | Dismiss a calendar event reminder |
| `get_event_attachment` | Get a specific attachment from a calendar event |
| `delete_event_attachment` | Delete an attachment from a calendar event |

### User & Organization Tools (9)
| Tool | Description |
|------|-------------|
| `get_my_profile` | Get the signed-in user's profile (name, email, job title, department, office) |
| `get_my_manager` | Get the manager of the signed-in user |
| `get_my_direct_reports` | List the direct reports of the signed-in user |
| `get_user_profile` | Get a specific user's profile by ID or UPN |
| `get_users_manager` | Get the manager of a specified user |
| `get_direct_reports` | List the direct reports of a specified user |
| `list_users` | Search and list users in the organization with $search and automatic $filter fallback |
| `get_my_photo` | Get profile photo metadata (dimensions, content type) |
| `list_people` | List relevant people ordered by communication/collaboration frequency |

### Delegated Mailbox Tools (6)
These tools operate on another user's mailbox or calendar, requiring delegated permissions (e.g., shared mailbox access or application permissions).

| Tool | Description |
|------|-------------|
| `list_user_messages` | List messages from another user's mailbox |
| `get_user_message` | Get a specific message from another user's mailbox |
| `send_user_mail` | Send an email on behalf of another user |
| `list_user_events` | List calendar events from another user's calendar |
| `get_user_event` | Get a specific event from another user's calendar |
| `list_user_calendar_view` | Get events in a date range from another user's calendar |

### Bonus Composite Tools (2)
These MCP-only tools have no direct REST counterpart — they combine multiple Graph API calls and prompt-engineering patterns to enable advanced Copilot Studio agent capabilities.

| Tool | Description |
|------|-------------|
| `get_writing_samples` | Fetches recent sent emails to analyze writing voice and style for drafting |
| `draft_with_style_guide` | Combines a company style guide with sent email samples for voice-matched drafting |

## Voice-Matched Email Drafting

Two specialized tools enable Copilot Studio agents to draft emails that match a sender's personal writing voice, optionally combined with a company style guide.

### How It Works

1. **`get_writing_samples`** — Retrieves recent sent emails with full body content. The agent analyzes these for greeting/closing patterns, formality level, sentence structure, vocabulary, and tone. Supports filtering by recipient (for relationship-aware tone matching) and by topic keyword.

2. **`draft_with_style_guide`** — Combines a company writing style guide with sent email samples in one call. The agent uses both organizational standards AND the sender's personal voice to draft the email. When conflicts arise, the style guide takes precedence for formal or external communications, and the sender's voice takes precedence for casual or internal ones.

### Workflow Examples

**Draft a reply matching the sender's voice:**
> "Reply to this email in my voice" → Agent calls `get_writing_samples` → analyzes tone → drafts reply using `reply` or `create_draft`

**Draft using a company style guide:**
> "Write an email requesting Q3 budget approval, using our company writing guidelines" → Agent calls `draft_with_style_guide` with the style guide rules and purpose → drafts email using `send_mail` or `create_draft`

**Leader/executive scenario:**
> "Draft an all-hands email about the new initiative in my usual style" → Agent calls `get_writing_samples(search: "all-hands", count: 15)` → picks up the leader's characteristic directness, vision framing, and delegation language → drafts email

### Parameters

| Tool | Parameter | Description |
|------|-----------|-------------|
| `get_writing_samples` | `search` | Topic keyword to find relevant samples |
| `get_writing_samples` | `recipientEmail` | Filter to emails sent to a specific person |
| `get_writing_samples` | `count` | Number of samples (default 10, max 25) |
| `get_writing_samples` | `userId` | Analyze another user's sent mail |
| `draft_with_style_guide` | `styleGuide` | Company rules (tone, terminology, formatting) |
| `draft_with_style_guide` | `purpose` | What the email should accomplish |
| `draft_with_style_guide` | `audience` | Who the email is for |
| `draft_with_style_guide` | `tone` | Tone override (encouraging, urgent, diplomatic) |

## Keyword Search

Every list operation supports keyword search per the user's requirement:

- **`$search` parameter** — Available on ListMessages, ListSentMessages, ListMailFolders, ListEvents, ListUserMessages, ListUserEvents, ListCalendars
- **KQL search** — The SearchMessages operation uses Microsoft Graph Search API with full KQL support for advanced queries like `subject:quarterly AND from:john AND hasAttachment:true`
- **Calendar View** — Uses date range filtering (startDateTime/endDateTime) rather than $search, as Graph API does not support $search on calendarView

### Search Examples

**Simple keyword search:**
```
$search="budget report"
```

**Field-scoped search:**
```
$search="subject:quarterly"
$search="from:john@contoso.com"
```

**KQL search (SearchMessages):**
```
subject:quarterly AND from:john
hasAttachment:true AND received>=2025-01-01
"exact phrase match"
```

## Work IQ Comparison

This connector covers all operations from the Work IQ Mail and Calendar MCP server tools:

| Work IQ Tool | This Connector |
|-------------|----------------|
| createMessage | `create_draft` |
| deleteMessage | `delete_message` |
| getMessage | `get_message` |
| listSent | `list_sent` |
| reply | `reply` |
| replyAll | `reply_all` |
| searchMessages | `search_messages` (KQL) |
| sendDraft | `send_draft` |
| sendMail | `send_mail` |
| updateMessage | `update_message` |
| acceptEvent | `accept_event` |
| cancelEvent | `cancel_event` |
| createEvent | `create_event` |
| declineEvent | `decline_event` |
| deleteEvent | `delete_event` |
| findMeetingTimes | `find_meeting_times` |
| getEvent | `get_event` |
| getSchedule | `get_schedule` |
| listCalendarView | `calendar_view` |
| listEvents | `list_events` |
| updateEvent | `update_event` |
| getMyProfile | `get_my_profile` |
| getMyManager | `get_my_manager` |
| getUserProfile | `get_user_profile` |
| getUsersManager | `get_users_manager` |
| getDirectReports | `get_direct_reports` |
| listUsers | `list_users` |

**Additional tools** beyond Work IQ: `list_messages`, `forward`, `list_folders`, `move_message`, `copy_message`, `list_attachments`, `get_attachment`, `get_attachment_content`, `create_attachment`, `delete_attachment`, `get_mail_folder`, `list_folder_messages`, `create_mail_folder`, `update_mail_folder`, `delete_mail_folder`, `list_child_folders`, `create_draft_in_folder`, `tentatively_accept`, `list_calendars`, `get_calendar`, `create_calendar`, `update_calendar`, `delete_calendar`, `list_event_instances`, `list_event_attachments`, `create_event_attachment`, `get_my_direct_reports`, `get_writing_samples` ⭐, `draft_with_style_guide` ⭐, `create_reply_draft`, `create_reply_all_draft`, `create_forward_draft`, `get_mail_tips`, `list_message_rules`, `create_message_rule`, `get_message_rule`, `update_message_rule`, `delete_message_rule`, `snooze_reminder`, `dismiss_reminder`, `get_event_attachment`, `delete_event_attachment`, `get_my_photo`, `list_people`, `list_user_messages`, `get_user_message`, `send_user_mail`, `list_user_events`, `get_user_event`, `list_user_calendar_view`

> ⭐ = Bonus composite tool (MCP-only, no REST counterpart)

## Application Insights

Replace the `APP_INSIGHTS_CS` constant in `script.csx` with your Application Insights connection string to enable telemetry tracking for all operations.

## Files

| File | Description |
|------|-------------|
| `apiDefinition.swagger.json` | OpenAPI/Swagger 2.0 definition with 76 REST operations |
| `apiProperties.json` | Connector properties, OAuth config, script operations |
| `script.csx` | C# script with MCP handler, Graph helpers, App Insights |
| `readme.md` | This documentation |

## Author

- **Name**: Troy Taylor
- **Email**: troy@troystaylor.com
- **GitHub**: https://github.com/troystaylor

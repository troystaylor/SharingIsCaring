# Graph Meeting Transcripts

Microsoft Graph connector for Teams meeting transcripts, recordings, attendance, AI insights, and virtual events (webinars, town halls, sessions, presenters, registrations). MCP-enabled for Copilot Studio integration with Application Insights telemetry support.

## Prerequisites

- Microsoft 365 tenant with Teams enabled
- Azure AD app registration with the following delegated permissions:
  - `OnlineMeetingTranscript.Read.All` - for transcripts
  - `OnlineMeetingRecording.Read.All` - for recordings
  - `OnlineMeetingArtifact.Read.All` - for attendance reports and records
  - `OnlineMeetings.Read` - for meeting lookup
  - `OnlineMeetings.ReadWrite` - for creating, updating, and deleting meetings
  - `VirtualEvent.ReadWrite` - for virtual events (webinars, town halls, presenters, registrations)
  - `User.Read`
  - `offline_access`
- Transcription and/or recording must be enabled for meetings
- **AI Insights only:** Microsoft 365 Copilot license required for the target user

## Setup

1. Create a new custom connector in Power Platform
2. Import the `apiDefinition.swagger.json` file
3. Configure OAuth 2.0 authentication using your Azure AD app registration
4. Test the connection

## Operations

### Meeting Management

| Operation | Description |
|-----------|-------------|
| GetMyOnlineMeetingByJoinUrl | Find an online meeting by its join URL to get the meeting ID |
| GetMyOnlineMeeting | Get an online meeting by its meeting ID |
| CreateMyOnlineMeeting | Create a new online meeting on behalf of the signed-in user |
| UpdateMyOnlineMeeting | Update meeting properties (subject, start/end time, settings) |
| DeleteMyOnlineMeeting | Delete an online meeting |
| CreateOrGetMyOnlineMeeting | Create a meeting with external ID, or get existing one if it already exists |

### User-Scoped Meeting Management

| Operation | Description |
|-----------|-------------|
| GetUserOnlineMeetingByJoinUrl | Find a user's online meeting by its join URL |
| CreateUserOnlineMeeting | Create an online meeting on behalf of a specified user |
| GetUserOnlineMeeting | Get a user's online meeting by meeting ID |
| UpdateUserOnlineMeeting | Update a user's online meeting properties |
| DeleteUserOnlineMeeting | Delete a user's online meeting |
| CreateOrGetUserOnlineMeeting | Create or get a user's meeting by external ID |

### Transcripts

| Operation | Description |
|-----------|-------------|
| ListMyMeetingTranscripts | List all transcripts for my online meeting |
| GetMyMeetingTranscript | Get metadata for a specific transcript from my meeting |
| GetMyTranscriptContent | Get the VTT text content of my meeting transcript |
| GetMyTranscriptMetadata | Get time-aligned utterance metadata (speaker, text, language) |
| ListUserMeetingTranscripts | List all transcripts for a specific user's online meeting |
| GetUserMeetingTranscript | Get metadata for a specific transcript from a user's meeting |
| GetUserTranscriptContent | Get the VTT text content of a user's meeting transcript |
| GetUserTranscriptMetadata | Get time-aligned utterance metadata for a user's transcript |
| GetAllTranscriptsByOrganizer | Get all transcripts across meetings organized by a user |

### Recordings

| Operation | Description |
|-----------|-------------|
| ListMyMeetingRecordings | List all recordings for my online meeting |
| GetMyMeetingRecording | Get metadata for a specific recording from my meeting |
| GetMyRecordingContent | Get the download URL for my meeting recording (returned as JSON) |
| ListUserMeetingRecordings | List all recordings for a specific user's online meeting |
| GetUserMeetingRecording | Get metadata for a specific recording from a user's meeting |
| GetUserRecordingContent | Get the download URL for a user's meeting recording (returned as JSON) |
| GetAllRecordingsByOrganizer | Get all recordings across meetings organized by a user |

### Attendance Reports

| Operation | Description |
|-----------|-------------|
| ListMyAttendanceReports | List attendance reports for my online meeting |
| GetMyAttendanceReport | Get an attendance report with participant records for my meeting |
| ListUserAttendanceReports | List attendance reports for a specific user's online meeting |
| GetUserAttendanceReport | Get an attendance report with participant records for a user's meeting |
| ListMyAttendanceRecords | List individual attendance records for a specific report from my meeting |
| ListUserAttendanceRecords | List individual attendance records for a specific report from a user's meeting |

### AI Insights (Copilot License Required)

| Operation | Description |
|-----------|-------------|
| ListMeetingAIInsights | List AI-generated insights for an online meeting |
| GetMeetingAIInsight | Get detailed AI insight with notes, action items, and mentions |

### Change Tracking (Delta)

| Operation | Description |
|-----------|-------------|
| GetTranscriptsDelta | Incremental sync of new or updated transcripts across all meetings for a user |
| GetRecordingsDelta | Incremental sync of new or updated recordings across all meetings for a user |

### Virtual Events - Webinars

| Operation | Description |
|-----------|-------------|
| ListWebinars | List all webinars in the tenant |
| CreateWebinar | Create a new Teams webinar (draft status) |
| GetWebinar | Get webinar properties and details |
| UpdateWebinar | Update webinar properties (organizer/co-organizer only) |
| PublishWebinar | Publish a draft webinar to make it visible |
| CancelWebinar | Cancel a webinar permanently |
| ListWebinarsByUserRole | List webinars where signed-in user has a specified role |
| ListWebinarsByUserIdAndRole | List webinars where a specified user has a given role |

### Virtual Events - Town Halls

| Operation | Description |
|-----------|-------------|
| ListTownhalls | List all town halls in the tenant |
| CreateTownhall | Create a new Teams town hall (draft status) |
| GetTownhall | Get town hall properties and details |
| UpdateTownhall | Update town hall properties (organizer/co-organizer only) |
| PublishTownhall | Publish a draft town hall to make it visible |
| CancelTownhall | Cancel a town hall permanently |
| ListTownhallsByUserRole | List town halls where signed-in user has a specified role |
| ListTownhallsByUserIdAndRole | List town halls where a specified user has a given role |

### Virtual Event Sessions

| Operation | Description |
|-----------|-------------|
| ListWebinarSessions | List all sessions for a webinar |
| GetWebinarSession | Get full properties of a webinar session |
| ListTownhallSessions | List all sessions for a town hall |
| GetTownhallSession | Get full properties of a town hall session |

### Virtual Event Presenters

| Operation | Description |
|-----------|-------------|
| ListWebinarPresenters | List all presenters for a webinar |
| CreateWebinarPresenter | Add a presenter to a webinar |
| GetWebinarPresenter | Get presenter details for a webinar |
| UpdateWebinarPresenter | Update a webinar presenter's info |
| DeleteWebinarPresenter | Remove a presenter from a webinar |
| ListTownhallPresenters | List all presenters for a town hall |
| CreateTownhallPresenter | Add a presenter to a town hall |
| GetTownhallPresenter | Get presenter details for a town hall |
| UpdateTownhallPresenter | Update a town hall presenter's info |
| DeleteTownhallPresenter | Remove a presenter from a town hall |

### Webinar Registrations

| Operation | Description |
|-----------|-------------|
| ListWebinarRegistrations | List all registration records for a webinar |
| CreateWebinarRegistration | Register a user for a webinar |
| GetWebinarRegistration | Get a specific registration record |
| CancelWebinarRegistration | Cancel a registration for a webinar |
| GetWebinarRegistrationConfig | Get registration configuration (portal URL, capacity, questions) |

## Example Usage

### Create and configure a meeting

1. Use **CreateMyOnlineMeeting** with a subject, start/end time, and participants
2. The response includes the meeting ID, join URL, and all meeting settings
3. Use **UpdateMyOnlineMeeting** to enable transcription/recording or change lobby settings

### Get a meeting transcript

1. Use **GetMyOnlineMeetingByJoinUrl** with a filter like `JoinWebUrl eq 'https://teams.microsoft.com/l/meetup-join/...'` to find the meeting ID
2. Use **ListMyMeetingTranscripts** with the meeting ID to list available transcripts
3. Use **GetMyTranscriptContent** with the meeting ID and transcript ID to get the VTT text

### Get a meeting recording

1. Find the meeting ID using **GetMyOnlineMeetingByJoinUrl**
2. Use **ListMyMeetingRecordings** to list available recordings
3. Use **GetMyRecordingContent** to get the download URL (returns JSON with `downloadUrl` and `contentType`)

### Get meeting attendance

1. Find the meeting ID using **GetMyOnlineMeetingByJoinUrl**
2. Use **ListMyAttendanceReports** to list available reports
3. Use **GetMyAttendanceReport** with `$expand=attendanceRecords` to get the full report with individual participant join/leave times
4. Alternatively, use **ListMyAttendanceRecords** to retrieve records separately with pagination support

### Get AI-generated meeting summary

1. Find the meeting ID using **GetMyOnlineMeetingByJoinUrl**
2. Use **ListMeetingAIInsights** with the user ID and meeting ID to list available insights
3. Use **GetMeetingAIInsight** to get the detailed summary including meeting notes, action items, and participant mentions

### Get all transcripts for a user

1. Use **GetAllTranscriptsByOrganizer** with the user ID and a `$filter` of `MeetingOrganizer/User/Id eq '{userId}'`
2. Iterate results to get individual transcript content

### Incremental sync with delta queries

1. Call **GetTranscriptsDelta** with the user ID to get all current transcripts
2. Save the `@odata.deltaLink` from the final page of results
3. On subsequent calls, pass the delta token from the saved URL as the `$deltatoken` parameter
4. Only new or updated transcripts since the last sync are returned
5. The same pattern applies with **GetRecordingsDelta** for recordings

### Create and manage a webinar

1. Use **CreateWebinar** with display name, description, start/end times, and audience scope
2. Use **CreateWebinarPresenter** to add presenters (by user ID or email)
3. Use **PublishWebinar** to make the webinar visible and open for registration
4. Use **ListWebinarRegistrations** to monitor registrations
5. After the event, use **ListWebinarSessions** to access session details

### Manage town halls

1. Use **CreateTownhall** with display name, description, and audience scope
2. Use **CreateTownhallPresenter** to add presenters
3. Use **PublishTownhall** to make the town hall visible
4. Use **CancelTownhall** to cancel if needed (irreversible)

### Virtual event role-based queries

1. Use **ListWebinarsByUserRole** with role `organizer` to find all webinars you organize
2. Use **ListTownhallsByUserIdAndRole** to find town halls for a specific user by role

## MCP (Model Context Protocol)

This connector includes an MCP endpoint for Copilot Studio integration. The `InvokeMCP` operation exposes 30 tools that a Copilot Studio agent can discover and invoke via JSON-RPC 2.0.

### MCP Tools

| Category | Tools |
|----------|-------|
| Meetings | `find_meeting`, `get_meeting`, `create_meeting`, `update_meeting`, `delete_meeting` |
| Transcripts | `list_transcripts`, `get_transcript_content` |
| Recordings | `list_recordings` |
| Attendance | `list_attendance_reports`, `get_attendance_records` |
| AI Insights | `list_ai_insights`, `get_ai_insight` |
| Webinars | `list_webinars`, `create_webinar`, `get_webinar`, `publish_webinar`, `cancel_webinar` |
| Town Halls | `list_townhalls`, `create_townhall`, `get_townhall`, `publish_townhall`, `cancel_townhall` |
| Sessions | `list_sessions`, `get_session` |
| Presenters | `list_presenters`, `add_presenter`, `remove_presenter` |
| Registrations | `list_registrations`, `create_registration`, `cancel_registration` |

### Copilot Studio Setup

1. Create a new agent or open an existing agent in Copilot Studio
2. Go to **Actions** → **Add an action** → **Connector**
3. Search for **Graph Meeting Transcripts** and add the connector
4. The agent will automatically discover available tools via the MCP endpoint
5. Test by asking the agent questions like "What meetings do I have?" or "Get the transcript for my last meeting"

## Application Insights

The connector supports optional Application Insights telemetry for monitoring and troubleshooting.

### Setup

1. Create an Application Insights resource in the Azure portal
2. Copy the connection string from the resource overview page
3. Edit `script.csx` and set the `APP_INSIGHTS_CONNECTION_STRING` constant:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = "InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...";
```

### Tracked Events

| Event | Description |
|-------|-------------|
| `RequestReceived` | Every incoming request with operation ID |
| `RequestCompleted` | Successful completion with duration and status code |
| `RequestError` | Unhandled errors with exception details |
| `McpRequestReceived` | MCP JSON-RPC method received |
| `McpToolCallStarted` | MCP tool invocation started |
| `McpToolCallCompleted` | MCP tool completed with success/error status |
| `McpToolCallError` | MCP tool execution failure |

### Sample KQL Query

```kql
customEvents
| where customDimensions.ServerName == "graph-meeting-transcripts-mcp"
| where name == "ToolExecuted"
| summarize count() by tostring(customDimensions.Tool), bin(timestamp, 1h)
| render columnchart
```

## Notes

- Transcript content is returned in VTT (Web Video Text Tracks) format, wrapped in JSON by the script as `{ "content": "...", "contentType": "text/vtt" }`
- Metadata content includes JSON objects with `startDateTime`, `endDateTime`, `speakerName`, `spokenText`, and `spokenLanguage`
- Recording content operations return a JSON object with `{ "downloadUrl": "https://...", "contentType": "video/mp4" }` — the script intercepts Graph's 302 redirect and extracts the pre-authenticated download URL
- Recording download is only available to the meeting organizer by default
- Recordings and transcripts are [metered APIs](https://learn.microsoft.com/en-us/graph/teams-licenses#payment-models-for-meeting-apis) (paid per use)
- AI Insights require a Microsoft 365 Copilot license for the user and may take up to 4 hours to be available after the meeting ends
- AI Insights support private scheduled meetings, town halls, webinars, and Meet Now sessions (not channel meetings)
- Attendance reports include `totalParticipantCount`, per-participant `role`, `emailAddress`, `totalAttendanceInSeconds`, and join/leave intervals
- This API does not support meetings created via the create onlineMeeting API that are not associated with a calendar event
- This API does not support live events
- Meeting artifacts are only available if the meeting has not expired per [Teams limits](https://learn.microsoft.com/en-us/microsoftteams/limits-specifications-teams#meeting-expiration)
- The `GetAllTranscriptsByOrganizer` and `GetAllRecordingsByOrganizer` operations require the `meetingOrganizerUserId` function parameter
- The `CreateOrGetMyOnlineMeeting` operation requires an `externalId` — if a meeting with that ID already exists, it returns the existing meeting instead of creating a duplicate
- Delta queries return `@odata.deltaLink` when all current changes have been returned; use this token for incremental sync on subsequent calls
- Created meetings do not appear on the user's calendar; use the Calendar API if calendar integration is needed
- Virtual events (webinars/town halls) are created in draft status; use the Publish operation to make them visible
- The old `meetingRegistration` API (beta) is deprecated (stopped returning data July 2024); use the webinar registration APIs (`virtualEventRegistration`) instead
- Webinar registration is only available for webinars, not town halls
- Virtual event role-based queries support roles: `organizer`, `coOrganizer`, `presenter`, `attendee`
- `VirtualEvent.ReadWrite` permission is required for creating, updating, publishing, and canceling virtual events; `VirtualEvent.Read` is sufficient for read-only operations

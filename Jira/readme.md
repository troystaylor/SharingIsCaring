# Jira

Power Platform custom connector for Jira Cloud — dual-mode connector exposing 91 REST operations across Jira Platform (`/rest/api/3`) and Jira Agile (`/rest/agile/1.0`), plus 50 MCP tools for Copilot Studio agents.

## What It Does

- **Issues** — search (JQL), CRUD, bulk create/fetch, assign, delete, archive/unarchive, edit meta, notify
- **Comments / transitions / fields** — full lifecycle including get/update/delete single comment
- **Engagement** — watchers, votes, issue links, worklogs (CRUD), file attachments (multipart upload, metadata, delete)
- **Filters** — full CRUD, my filters, favourites
- **Projects / versions / components** — search, get, list and CRUD versions/components
- **Users / groups / statuses** — search, by-project, group membership, status catalog
- **Agile** — boards (list, configuration, issues, backlog, projects), sprints (CRUD + state transitions, move issues, backlog), epics (list, issues, move/remove)
- **Async tasks** — get/cancel long-running task IDs returned by archive/move/bulk APIs
- **MCP server** — single `/mcp` endpoint with 50 tools optimised for Copilot Studio
- Server-side auto-pagination for `SearchIssues` and `jira_list_comments` via opt-in `limit`
- Multipart attachment upload from base64 payload with 10 MB cap
- Per-site `cloudId` cached after first call
- Optional Application Insights telemetry

## Prerequisites

1. Jira Cloud site
2. Atlassian developer console app (OAuth 2.0 (3LO))

## Setup

### 1. Create an Atlassian OAuth 2.0 (3LO) App

1. Go to the Atlassian developer console
2. Create a new OAuth 2.0 (3LO) app
3. Add a redirect URL: `https://global.consent.azure-apim.net/redirect`
4. Copy the Client ID and Client Secret

### 2. Import the Connector

1. In Power Platform Maker portal, go to **Custom connectors > Import an OpenAPI file**
2. Import `apiDefinition.swagger.json`
3. On the **Code** tab, paste the contents of `script.csx` and toggle the code on
4. Save the connector

### 3. Create a Connection

When creating a connection use the OAuth 2.0 popup flow with the Client ID and Client Secret from your Atlassian developer console app.

### 4. Add to Copilot Studio

1. In Copilot Studio, open your agent
2. Add this connector as an action (MCP server)
3. Test with prompts like "List Jira projects", "Search Jira issues with JQL", or "Show me board 1's active sprint"

## MCP Tools (50)

### Discovery

- `jira_list_projects`, `jira_search_projects`, `jira_get_project`
- `jira_get_project_versions`, `jira_get_project_components`
- `jira_list_issue_types`, `jira_list_project_roles`, `jira_list_project_statuses`
- `jira_get_issue_link_types`
- `jira_search_users`, `jira_list_users_by_project`
- `jira_list_filters`
- `jira_list_accessible_resources`

### Issues

- `jira_search_issues`, `jira_get_issue`
- `jira_create_issue`, `jira_create_issue_simple`
- `jira_update_issue`, `jira_delete_issue`
- `jira_assign_issue`
- `jira_bulk_create_issues`, `jira_bulk_fetch_issues`

### Comments / transitions

- `jira_list_comments`, `jira_add_comment`
- `jira_get_transitions`, `jira_transition_issue`

### Engagement

- `jira_get_watchers`, `jira_add_watcher`, `jira_remove_watcher`
- `jira_link_issues`
- `jira_add_worklog`, `jira_get_issue_worklogs`
- `jira_upload_attachment`

### Filters

- `jira_get_filter`, `jira_create_filter`, `jira_update_filter`, `jira_delete_filter`

### Project versions / components

- `jira_create_version`, `jira_create_component`

### Async tasks

- `jira_get_task`, `jira_cancel_task`

### Agile (Jira Software)

- `jira_list_boards`, `jira_get_board_issues`, `jira_get_board_backlog`
- `jira_list_board_sprints`, `jira_list_board_epics`
- `jira_create_sprint`, `jira_update_sprint`, `jira_get_sprint_issues`, `jira_move_issues_to_sprint`

Tools that operate on a discoverable resource include a cross-reference in their description (Option A) so an agent always knows which discovery tool to call first. For example, `jira_get_issue` advises calling `jira_search_issues` first; `jira_update_sprint` advises calling `jira_list_board_sprints` first.

## REST Operations (91)

### Jira Platform (`/rest/api/3`)

| Group | Operations |
|-------|------------|
| Projects | `ListProjects`, `SearchProjects`, `GetProject`, `GetProjectVersions`, `CreateVersion`, `GetVersion`, `UpdateVersion`, `DeleteVersion`, `GetProjectComponents`, `CreateComponent`, `GetComponent`, `UpdateComponent`, `DeleteComponent`, `GetStatuses`, `GetProjectRoles`, `GetProjectStatuses` |
| Issues | `SearchIssues`, `GetIssue`, `CreateIssue`, `UpdateIssue`, `DeleteIssue`, `AssignIssue`, `GetEditIssueMeta`, `NotifyIssue`, `BulkCreateIssues`, `BulkFetchIssues`, `ArchiveIssues`, `UnarchiveIssues`, `ListIssueTypes` |
| Comments | `ListComments`, `AddComment`, `GetComment`, `UpdateComment`, `DeleteComment` |
| Transitions | `GetTransitions`, `TransitionIssue` |
| Watchers / votes | `GetIssueWatchers`, `AddWatcher`, `RemoveWatcher`, `GetVotes`, `AddVote`, `RemoveVote` |
| Issue links | `LinkIssues`, `GetIssueLink`, `DeleteIssueLink`, `GetIssueLinkTypes` |
| Worklogs | `AddWorklog`, `GetIssueWorklogs`, `UpdateWorklog`, `DeleteWorklog` |
| Attachments | `UploadAttachment`, `GetAttachmentMetadata`, `DeleteAttachment` |
| Filters | `ListFilters`, `GetFilter`, `CreateFilter`, `UpdateFilter`, `DeleteFilter`, `GetMyFilters`, `GetFavouriteFilters`, `SetFilterFavourite`, `DeleteFilterFavourite` |
| Users / groups / fields | `GetUser`, `SearchUsers`, `ListUsersByProject`, `FindGroups`, `GetGroupMembers`, `ListFields` |
| Async tasks | `GetTask`, `CancelTask` |
| OAuth | `ListAccessibleResources` |
| MCP | `InvokeMCP` |

### Jira Agile (`/rest/agile/1.0`)

| Group | Operations |
|-------|------------|
| Boards | `ListBoards`, `GetBoard`, `GetBoardConfiguration`, `GetBoardIssues`, `GetBoardBacklog`, `GetBoardProjects` |
| Sprints | `ListBoardSprints`, `GetSprint`, `CreateSprint`, `UpdateSprint`, `DeleteSprint`, `GetSprintIssues`, `MoveIssuesToSprint`, `MoveIssuesToBacklog` |
| Epics | `ListBoardEpics`, `GetEpic`, `GetEpicIssues`, `MoveIssuesToEpic`, `RemoveIssuesFromEpic` |

## Pagination

Jira caps results at 100 records per page. To retrieve more, the connector supports opt-in server-side auto-pagination.

### `SearchIssues` / `jira_search_issues`

Uses Jira's enhanced search endpoint `POST /rest/api/3/search/jql` (cursor-based). The legacy `/rest/api/3/search` endpoint is being removed by Atlassian.

Request fields:

- `jql` (required) - JQL query string
- `maxResults` (optional) - Records per Jira page (1-100)
- `limit` (optional) - **Total cap across pages.** When set, the connector loops through Jira pages until this many records are collected or no more pages remain. When omitted, only a single page is returned (backward compatible).
- `nextPageToken` (optional) - Cursor from a previous response. Use to manually resume paging when `limit` is not set.
- `fields` (optional) - Field names to return

Response:

```json
{
  "isLast": false,
  "nextPageToken": "CAEaAggB",
  "fetched": 500,
  "issues": [ ... ]
}
```

> The new endpoint does not return a `total` count. Use `POST /rest/api/3/search/approximate-count` if you need an issue count.

### `jira_list_comments`

Uses the classic `startAt` / `maxResults` paging model.

MCP tool arguments:

- `issueIdOrKey` (required)
- `startAt` (optional) - Index of first comment
- `maxResults` (optional) - Comments per Jira page (1-100)
- `limit` (optional) - **Total cap across pages.** When set, the tool auto-pages until this many comments are collected.

Response includes `startAt`, `maxResults`, `total`, `fetched`, and `comments`.

### Defaults

When `limit` is omitted, both operations return a single Jira page (current/legacy behavior). This keeps existing Power Automate flows working.

## Attachments

`UploadAttachment` (REST) and `jira_upload_attachment` (MCP) both accept a JSON body with:

```json
{
  "filename": "report.pdf",
  "contentBase64": "JVBERi0xLjQK...",
  "contentType": "application/pdf"
}
```

The connector:

1. Validates the base64 payload and enforces a 10 MB cap (decoded size).
2. Builds a `multipart/form-data` request with the file part named `file`.
3. Sets `X-Atlassian-Token: no-check` (required by the Jira attachments API to bypass XSRF check).
4. Forwards the user's OAuth Bearer token.
5. POSTs to `/rest/api/3/issue/{issueIdOrKey}/attachments`.

`contentType` defaults to `application/octet-stream` when omitted.

## OAuth Scopes

The connector requests the following scopes:

- `read:jira-work`
- `write:jira-work`
- `read:jira-user`

Adjust scopes in `apiProperties.json` if you add or remove operations.

> Jira Software (Agile) operations are gated by the same `read:jira-work` / `write:jira-work` scopes. If your Atlassian app needs finer-grained Agile scopes (e.g. `read:sprint:jira-software`), add them in the developer console and update `apiProperties.json` accordingly.

## Jira Document Format (ADF)

Jira Cloud uses Atlassian Document Format for rich text fields. MCP tools that accept plain text for descriptions, comments, or worklog notes convert the text into a simple ADF document before sending the request.

## Application Insights

To enable telemetry, set the `APP_INSIGHTS_INSTRUMENTATION_KEY` constant in `script.csx`. Telemetry is silently skipped when the placeholder value is unchanged.

Events tracked:

- `RequestReceived`
- `RequestCompleted`
- `RequestError`
- `McpRequestReceived`
- `McpToolCallStarted`
- `McpToolCallCompleted`
- `McpToolCallError`

## Version History

- **1.3.0** — Added Agile (boards / sprints / epics), bulk issues, watchers / votes / links / worklogs, attachments (multipart upload), filter CRUD, project versions / components CRUD, groups / status catalog, and 32 new MCP tools (total 50). Cross-reference suffixes (Option A) added to dependent tool descriptions.
- **1.2.0** — Added `GetIssueByKey` discovery, filters search, transitions, users-by-project, task get/cancel, accessible resources list.
- **1.1.0** — Enhanced search via `/rest/api/3/search/jql` with auto-pagination.
- **1.0.0** — Initial release.

# Graph Search and Intelligence

Microsoft Graph connector for cross-workload search (KQL), Copilot semantic search, document insights, people intelligence, and OneDrive file access. Designed for Power Platform custom connectors with full MCP (Model Context Protocol) support for Copilot Studio.

## Publisher: Troy Taylor

## Prerequisites

- A Microsoft 365 work or school account
- An Azure AD app registration with the following **delegated** permissions:
  - `Files.Read.All`
  - `Sites.Read.All`
  - `Mail.Read`
  - `Calendars.Read`
  - `Chat.Read`
  - `ChannelMessage.Read.All`
  - `People.Read`
  - `ExternalItem.Read.All`
- For **Semantic Search Files** (beta): Microsoft 365 Copilot license required

## Supported Operations

### MCP Tools (19 tools for Copilot Studio)

All MCP tools are available via the **Invoke Graph Search and Intelligence MCP** operation using JSON-RPC 2.0.

#### Category 1: Microsoft Graph Search API (KQL)

All search tools call `POST /v1.0/search/query` with entity-specific wrappers:

| # | Tool | Entity Type | Description |
|---|------|-------------|-------------|
| 1 | `search_messages` | message | Search Outlook email messages using KQL |
| 2 | `search_chat_messages` | chatMessage | Search Teams chat and channel messages |
| 3 | `search_events` | event | Search calendar events |
| 4 | `search_files` | driveItem | Search OneDrive/SharePoint files with sort support |
| 5 | `search_sites` | site | Search SharePoint sites |
| 6 | `search_list_items` | listItem | Search SharePoint list items with custom fields |
| 7 | `search_external` | externalItem | Search external content via Graph connectors |
| 8 | `search_interleaved` | message + chatMessage | Cross-search email and Teams in one call |

**KQL Examples:**
- `subject:quarterly AND from:john` (messages)
- `filetype:docx budget` (files)
- `IsMentioned:true` (chat messages)
- `organizer:cathy` (events)

#### Category 2: Copilot Semantic Search (Beta)

| # | Tool | Endpoint | Description |
|---|------|----------|-------------|
| 9 | `semantic_search_files` | POST /beta/copilot/search | Natural language semantic + lexical search across OneDrive for work or school |

> **Note:** Requires Microsoft 365 Copilot license. Beta endpoint — subject to change. Global service only (not available in sovereign clouds).

#### Category 3: Item Insights

| # | Tool | Endpoint | Description |
|---|------|----------|-------------|
| 10 | `list_trending_documents` | GET /v1.0/me/insights/trending | ML-powered trending documents from closest network |
| 11 | `list_shared_documents` | GET /v1.0/me/insights/shared | Documents shared with/by user, ordered by recency |
| 12 | `list_used_documents` | GET /v1.0/me/insights/used | Recently viewed or modified documents |

#### Category 4: People Intelligence

| # | Tool | Endpoint | Description |
|---|------|----------|-------------|
| 13 | `list_relevant_people` | GET /v1.0/me/people | Relevance-ranked people from communication patterns |

#### Category 5: OneDrive File Access

| # | Tool | Endpoint | Description |
|---|------|----------|-------------|
| 14 | `list_recent_files` | GET /v1.0/me/drive/recent | Recently accessed files (**deprecated**) |
| 15 | `list_shared_with_me` | GET /v1.0/me/drive/sharedWithMe | Files shared with the user (**deprecated**) |
| 16 | `get_file_metadata` | GET /v1.0/me/drive/items/{id} | File/folder metadata (name, size, dates, author) |
| 17 | `get_file_content` | GET /v1.0/me/drive/items/{id} | Pre-authenticated download URL for a file |
| 18 | `list_folder_children` | GET /v1.0/me/drive/items/{id}/children | List folder contents |
| 19 | `search_my_drive` | GET /v1.0/me/drive/root/search | Lightweight file search in user's own drive |

### REST Operations (20 operations for Power Automate)

| Operation | Method | Path | Script-Handled |
|-----------|--------|------|----------------|
| InvokeMCP | POST | /mcp | Yes |
| SearchMessages | POST | /v1.0/search/messages | Yes (virtual) |
| SearchChatMessages | POST | /v1.0/search/chat-messages | Yes (virtual) |
| SearchEvents | POST | /v1.0/search/events | Yes (virtual) |
| SearchFiles | POST | /v1.0/search/files | Yes (virtual) |
| SearchSites | POST | /v1.0/search/sites | Yes (virtual) |
| SearchListItems | POST | /v1.0/search/list-items | Yes (virtual) |
| SearchExternal | POST | /v1.0/search/external | Yes (virtual) |
| SearchInterleaved | POST | /v1.0/search/interleaved | Yes (virtual) |
| SemanticSearch | POST | /beta/copilot/search | Pass-through |
| ListTrendingDocuments | GET | /v1.0/me/insights/trending | Pass-through |
| ListSharedDocuments | GET | /v1.0/me/insights/shared | Pass-through |
| ListUsedDocuments | GET | /v1.0/me/insights/used | Pass-through |
| ListPeople | GET | /v1.0/me/people | Pass-through |
| ListRecentFiles | GET | /v1.0/me/drive/recent | Pass-through |
| ListSharedWithMe | GET | /v1.0/me/drive/sharedWithMe | Pass-through |
| GetFileMetadata | GET | /v1.0/me/drive/items/{itemId} | Pass-through |
| GetFileContent | GET | /v1.0/me/drive/items/{itemId}/content | Pass-through |
| ListFolderChildren | GET | /v1.0/me/drive/items/{itemId}/children | Pass-through |
| SearchDrive | GET | /v1.0/me/drive/root/search(q='{query}') | Pass-through |

**Virtual paths:** The 8 search operations (SearchMessages through SearchInterleaved) use virtual swagger paths. The script intercepts these and calls the real `POST /v1.0/search/query` Graph API endpoint with the correct entity type(s) and request body.

## Deprecation Notices

| Endpoint | Status | Sunset Date | Alternative |
|----------|--------|-------------|-------------|
| GET /me/drive/recent | **Deprecated** | November 2026 | `list_used_documents` (Insights API) |
| GET /me/drive/sharedWithMe | **Deprecated** | November 2026 | `list_shared_documents` (Insights API) |
| GET /me/people | Maintenance mode | None announced | Graph Search API with `person` entity type |

## API Version Strategy

This connector uses **both v1.0 and beta endpoints** with hardcoded version prefixes in each operation path. There is no connection parameter to switch between API versions:

- **v1.0** — Search API, Insights API, People API, OneDrive API (19 operations including InvokeMCP)
- **beta** — Copilot Semantic Search (1 operation)

## Authentication

OAuth 2.0 with Azure AD (delegated permissions). PKCE is enabled. Supports on-behalf-of login for Copilot Studio integration via `enableOnbehalfOfLogin: true`.

## Known Limitations

1. **Graph Search API entity combinations**: Not all entity types can be combined in a single request. Only `message` + `chatMessage` interleaving is supported. Other entity types must be searched individually.
2. **Event search max page size**: The `event` entity type has a maximum page size of 25.
3. **Copilot Semantic Search**: Requires M365 Copilot license, delegated permissions only (no app-only), not available in sovereign clouds.
4. **External Items search**: Requires `contentSources` to specify which Graph connector connections to search.
5. **File content**: `get_file_content` returns the `@microsoft.graph.downloadUrl` property from metadata rather than following the 302 redirect.

## Application Insights Logging

The script includes optional Azure Application Insights telemetry. To enable, set the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx` to your Application Insights connection string. Leave empty to disable.

### Events Tracked

| Event | Trigger | Properties |
|-------|---------|------------|
| `RequestReceived` | Every incoming request | CorrelationId, OperationId, Path, Method |
| `RequestCompleted` | Successful response | CorrelationId, OperationId, StatusCode, DurationMs |
| `RequestError` | Unhandled exception | CorrelationId, OperationId, ErrorMessage, ErrorType, DurationMs |
| `SearchDispatched` | Search operation intercepted | CorrelationId, OperationId, EntityTypes |
| `McpRequestReceived` | MCP JSON-RPC request | CorrelationId, McpMethod, ToolName |
| `McpRequestProcessed` | MCP response ready | CorrelationId, McpMethod, ToolName, IsError |


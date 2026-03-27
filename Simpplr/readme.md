# Simpplr

## Overview

Custom connector for [Simpplr](https://simpplr.com/) — the AI-powered employee experience platform. Works with both **Copilot Studio** (via MCP) and **Power Automate** (via REST operations) for managing content, feeds, sites, users, search, alerts, and service desk tickets.

## Strategy

This connector has **50+ endpoints** and ships with **two script variants**:

| Script | Pattern | Description |
|--------|---------|-------------|
| `script.csx` (default) | **Mission Command** | 3 orchestration tools — scan/launch/sequence across all 50+ operations |
| `script-per-tool.csx` | **Per-tool** | 15 curated tools for common scenarios |

To switch: rename `script-per-tool.csx` to `script.csx` (back up the original first).

> **SYNC:** When adding tools, update both scripts. Each script has a comment at the top pointing to its paired file.

## Prerequisites

- Simpplr tenant with Extensibility Center access
- Client Application configured with **Client Credentials** grant type
- Client ID and Client Secret from the Extensibility Center

## Setup

1. In Simpplr, go to **Extensibility Center** > **Client Applications**
2. Create a new Client Application with **Client Credentials** grant type
3. Note the Client ID and Client Secret
4. Import the connector files into Power Platform
5. Create a new connection using the Client ID and Client Secret
6. **Copilot Studio**: Add the connector to your agent — the MCP endpoint exposes tools automatically
7. **Power Automate**: Use the individual REST operations listed below

## Authentication

Uses OAuth 2.0 Client Credentials flow (B2B API). Both scripts automatically exchange the connection's `clientId`/`clientSecret` for a Bearer token via:

```
POST https://platform.app.simpplr.com/v1/b2b/identity/oauth/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=<id>&client_secret=<secret>
```

The token is cached per-request and used for all subsequent API calls.

## Operations

### MCP Endpoint (Copilot Studio)

| Operation | Description |
|-----------|-------------|
| `InvokeMCP` | MCP endpoint for Copilot Studio — exposes Mission Command or per-tool interface depending on active script |

### REST Operations (Power Automate)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| `SearchContent` | GET | `/content` | Search content with filters (type, site, status) |
| `GetContent` | GET | `/content/{contentId}` | Get a specific content item by ID |
| `DeleteContent` | DELETE | `/content/{contentId}` | Delete a content item |
| `AddPage` | POST | `/content/sites/{siteId}/pages` | Create a new page on a site |
| `AddEvent` | POST | `/content/sites/{siteId}/events` | Create a new event on a site |
| `CreateFeed` | POST | `/feed` | Create a new feed post |
| `GetFeeds` | GET | `/feed` | List feed posts with filters |
| `GetFeed` | GET | `/feed/{feedId}` | Get a specific feed post |
| `UpdateFeed` | PUT | `/feed/{feedId}` | Update an existing feed post |
| `DeleteFeed` | DELETE | `/feed/{feedId}` | Delete a feed post |
| `CreateComment` | POST | `/feed/{feedId}/comments` | Add a comment to a feed post |
| `GetComments` | GET | `/feed/{feedId}/comments` | List comments on a feed post |
| `GetSites` | GET | `/sites` | List sites with filters |
| `GetSiteMembers` | GET | `/sites/{siteId}/members` | Get members of a specific site |
| `GetUsers` | GET | `/users` | List users with search and filters |
| `GetUser` | GET | `/users/{userId}` | Get a specific user by ID |
| `EnterpriseSearch` | POST | `/search` | Full-text enterprise search across all content |
| `SmartAnswers` | POST | `/search/smart-answers` | AI-powered smart answers from content |
| `SearchAlerts` | GET | `/alerts` | List alerts with filters |
| `CreateAlert` | POST | `/alerts` | Create a new alert |
| `CreateTicket` | POST | `/service-desk/tickets` | Create a service desk ticket |
| `GetTicket` | GET | `/service-desk/tickets/{ticketId}` | Get a specific ticket by ID |

### MCP Tool ↔ REST Operation Mapping (Per-tool script)

| MCP Tool | REST Operation |
|----------|---------------|
| `enterprise_search` | `EnterpriseSearch` |
| `smart_answers` | `SmartAnswers` |
| `search_content` | `SearchContent` |
| `get_content` | `GetContent` |
| `create_page` | `AddPage` |
| `create_event` | `AddEvent` |
| `create_feed` | `CreateFeed` |
| `get_feeds` | `GetFeeds` |
| `create_comment` | `CreateComment` |
| `list_sites` | `GetSites` |
| `get_site_members` | `GetSiteMembers` |
| `list_users` | `GetUsers` |
| `search_alerts` | `SearchAlerts` |
| `create_alert` | `CreateAlert` |
| `create_ticket` | `CreateTicket` |

## API Base URL

All REST operations use: `https://platform.app.simpplr.com/v1/b2b`

## Example Prompts (Copilot Studio)

### Mission Command (default)
- "Scan for content operations" → triggers `scan_simpplr`
- "Search for all pages about onboarding" → scan then launch enterprise search
- "Create a new service desk ticket about laptop issues" → scan then launch create ticket

### Per-tool (alternative)
- "Search Simpplr for onboarding content" → `enterprise_search`
- "What are the smart answers for benefits enrollment?" → `smart_answers`
- "Create a feed post about the team offsite" → `create_feed`
- "Create a ticket for my broken monitor" → `create_ticket`

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI definition with MCP + 22 REST operations |
| `apiProperties.json` | Connector metadata and script operation routing |
| `script.csx` | Mission Command MCP handler + REST routing (default) |
| `script-per-tool.csx` | Per-tool MCP handler + REST routing (alternative) |
| `readme.md` | This file |

## Notes

- Scopes can be `read`, `write`, or `everything` — configure in the Client Application settings
- The capability index endpoint paths are inferred from the API reference — verify against your tenant during testing

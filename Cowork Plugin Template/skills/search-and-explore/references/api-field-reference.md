# {{Service Name}} API Field Reference

This reference helps the agent understand the data model, valid field values,
and query capabilities of the {{Service Name}} API. Skills reference this file
when they need to construct searches, validate input, or explain fields to users.

## Entities

### {{Entity 1}} (e.g., Tickets, Cases, Orders)

**Endpoint:** `{{base_url}}/api/v1/{{entity}}`

| Field | Type | Required | Filterable | Description |
|-------|------|----------|------------|-------------|
| id | string | auto | yes | Unique identifier (e.g., TKT-4421) |
| title | string | yes | yes | Short description of the record |
| status | enum | yes | yes | See valid values below |
| priority | enum | no | yes | See valid values below |
| assignee | string | no | yes | User ID or email of the assigned person |
| category | string | no | yes | Classification category |
| created_at | datetime | auto | yes | ISO 8601 creation timestamp |
| updated_at | datetime | auto | yes | ISO 8601 last modification timestamp |
| description | string | no | no | Full description or body text |

**Status values:**

| Value | Description | Can transition to |
|-------|-------------|-------------------|
| open | Newly created, not yet started | in_progress, closed |
| in_progress | Being actively worked on | open, resolved, closed |
| resolved | Work complete, pending verification | open, closed |
| closed | Finalized, no further action | open |

**Priority values:** `critical`, `high`, `medium`, `low`

**Category values:** `bug`, `feature_request`, `question`, `billing`, `access`

### {{Entity 2}} (e.g., Customers, Contacts, Accounts)

**Endpoint:** `{{base_url}}/api/v1/{{entity}}`

| Field | Type | Required | Filterable | Description |
|-------|------|----------|------------|-------------|
| id | string | auto | yes | Unique identifier |
| name | string | yes | yes | Display name |
| email | string | no | yes | Primary email address |
| status | enum | no | yes | active, inactive, archived |
| owner | string | no | yes | Account owner user ID |
| created_at | datetime | auto | yes | ISO 8601 creation timestamp |

## Query Capabilities

### Search syntax

```
search_{{entity}}(
    query: "free text search",     # Searches title and description
    status: "open",                # Exact match filter
    priority: "high,critical",     # Comma-separated for OR
    assignee: "jamie@zava.com",    # Exact match
    created_after: "2026-04-01",   # ISO 8601 date
    created_before: "2026-04-30",  # ISO 8601 date
    sort: "created_at",            # Field to sort by
    order: "desc",                 # asc or desc
    limit: 25                      # Max results (default 25, max 200)
)
```

### Pagination

The API returns paginated results. If `has_more` is true in the response,
use the `cursor` value in subsequent requests to retrieve additional pages.

## Error Codes

| Code | Meaning | User-friendly message |
|------|---------|----------------------|
| 400 | Invalid request parameters | "One of the values you provided isn't valid. Check the field reference for allowed values." |
| 401 | Authentication failed | "Your session has expired. Please reconnect the {{Service Name}} plugin." |
| 403 | Insufficient permissions | "You don't have permission to perform this action. Contact your admin." |
| 404 | Record not found | "I couldn't find that record. Double-check the ID or try searching by name." |
| 429 | Rate limited | "The API is temporarily throttled. I'll retry in a moment." |

## Notes for Skill Authors

- Always use filterable fields for searches — non-filterable fields require
  client-side filtering which is slow and incomplete
- Date fields accept ISO 8601 format (YYYY-MM-DD or full datetime)
- The `query` parameter searches title and description by default
- Enum values are case-sensitive — use lowercase
- Comma-separated enum values act as OR filters, not AND

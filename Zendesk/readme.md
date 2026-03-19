# Zendesk

## Overview

A Power Platform custom connector that integrates with the Zendesk Support API v2 and implements the Model Context Protocol (MCP) for use with Copilot Studio. This connector provides AI agents with full ticketing, user management, organization, search, and metrics capabilities through natural language interactions.

## Prerequisites

- A Zendesk Support account with API access
- An OAuth client configured in Zendesk Admin Center
- A Power Platform environment with custom connector support

## Zendesk OAuth Setup

1. In Zendesk Admin Center, go to **Apps and integrations > APIs > Zendesk API > OAuth Clients**
2. Click **Add OAuth Client**
3. Fill in the details:
   - **Client name**: Power Platform Connector
   - **Redirect URLs**: `https://global.consent.azure-apim.net/redirect`
   - **Description**: Power Platform custom connector integration
4. Save and note the **Client ID** and **Client Secret**

## Connector Setup

1. Import the connector files into Power Platform using the Power Platform CLI:
   ```
   paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx
   ```
2. In the connector settings, replace `yoursubdomain` with your Zendesk subdomain in:
   - `apiDefinition.swagger.json` — the `host` field
   - `apiProperties.json` — all OAuth URL templates
3. Enter your **Client ID** and **Client Secret** from the Zendesk OAuth client

## REST Operations (Power Automate / Power Apps)

The swagger definition exposes these operations for direct use in Power Automate flows and Power Apps:

### Tickets
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListTickets | GET | /tickets | List tickets with sorting and pagination |
| ShowTicket | GET | /tickets/{id} | Get a specific ticket |
| CreateTicket | POST | /tickets | Create a new ticket |
| UpdateTicket | PUT | /tickets/{id} | Update a ticket (status, priority, add comments, etc.) |
| DeleteTicket | DELETE | /tickets/{id} | Soft-delete a ticket |
| MergeTickets | POST | /tickets/{id}/merge | Merge source tickets into a target |
| ListTicketComments | GET | /tickets/{id}/comments | Get conversation thread |
| GetTicketMetrics | GET | /tickets/{id}/metrics | Get time metrics (reply, resolution, wait) |
| GetTicketRelated | GET | /tickets/{id}/related | Get related info (incidents, followups) |
| GetTicketAudits | GET | /tickets/{id}/audits | Get full audit trail |
| ListTicketFields | GET | /ticket_fields | List system and custom fields |

### Users
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListUsers | GET | /users | List users filtered by role |
| ShowUser | GET | /users/{id} | Get a specific user |
| CreateUser | POST | /users | Create end-user, agent, or admin |
| UpdateUser | PUT | /users/{id} | Update user profile |
| GetUserTickets | GET | /users/{id}/tickets/{type} | Get requested/ccd/assigned tickets |
| ListUserIdentities | GET | /users/{id}/identities | List email, phone, and social identities |

### Organizations
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListOrganizations | GET | /organizations | List organizations |
| ShowOrganization | GET | /organizations/{id} | Get a specific organization |
| CreateOrganization | POST | /organizations | Create an organization |
| GetOrganizationTickets | GET | /organizations/{id}/tickets | Get tickets for an org |
| ListOrganizationMemberships | GET | /organization_memberships | List user-org memberships |

### Groups
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListGroups | GET | /groups | List agent groups |
| ShowGroup | GET | /groups/{id} | Get a specific group |
| ListGroupMemberships | GET | /group_memberships | List agent-group memberships |

### Search
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| Search | GET | /search | Full-text search across all objects |

### Views
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListViews | GET | /views | List available views |
| GetViewTickets | GET | /views/{id}/tickets | Execute a view |
| GetViewCount | GET | /views/{id}/count | Get ticket count for a view |

### Satisfaction Ratings, Tags, Macros
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListSatisfactionRatings | GET | /satisfaction_ratings | List CSAT ratings |
| ListTags | GET | /tags | List popular tags |
| ListMacros | GET | /macros | List macros |

### Help Center
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListArticles | GET | /help_center/articles | List articles |
| ShowArticle | GET | /help_center/articles/{id} | Get article with body |
| SearchArticles | GET | /help_center/articles/search | Search articles by keyword |
| ListArticlesBySection | GET | /help_center/sections/{id}/articles | List articles in a section |
| CreateArticle | POST | /help_center/sections/{id}/articles | Create an article |
| ListSections | GET | /help_center/sections | List sections |
| ListCategories | GET | /help_center/categories | List categories |
| ListSectionsByCategory | GET | /help_center/categories/{id}/sections | List sections in a category |

### SLA Policies
| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| ListSLAPolicies | GET | /slas/policies | List SLA policies and targets |
| ShowSLAPolicy | GET | /slas/policies/{id} | Get a specific SLA policy |

## MCP Tools (Copilot Studio)

| Tool | Description |
|------|-------------|
| `search` | Search Zendesk using search syntax (tickets, users, organizations) |
| `list_tickets` | List tickets with sorting and pagination |
| `get_ticket` | Get a specific ticket by ID |
| `create_ticket` | Create a new ticket with subject, priority, type, requester, assignee, tags |
| `update_ticket` | Update ticket status, priority, assignee, add comments, modify tags |
| `delete_ticket` | Soft-delete a ticket |
| `merge_tickets` | Merge source tickets into a target ticket |
| `get_ticket_comments` | Get the full conversation thread on a ticket |
| `list_users` | List users filtered by role |
| `get_user` | Get a specific user by ID |
| `create_user` | Create a new end-user, agent, or admin |
| `update_user` | Update user profile details |
| `get_user_tickets` | Get tickets requested by, assigned to, or CC'd to a user |
| `list_organizations` | List organizations |
| `get_organization` | Get a specific organization by ID |
| `create_organization` | Create a new organization |
| `get_organization_tickets` | Get tickets for an organization |
| `list_groups` | List agent groups |
| `get_group` | Get a specific group by ID |
| `list_views` | List available views |
| `get_view_tickets` | Execute a view and return its tickets |
| `get_view_count` | Get ticket count for a view |
| `list_satisfaction_ratings` | List CSAT ratings with score and date filters |
| `list_tags` | List popular recent tags |
| `get_ticket_metrics` | Get time metrics (first reply, resolution, wait times) |
| `get_ticket_related` | Get related info (incidents, followups) |
| `get_ticket_audits` | Get full audit trail for a ticket |
| `list_macros` | List available macros |
| `list_ticket_fields` | List system and custom ticket fields |
| `list_articles` | List Help Center articles by section, category, or label |
| `get_article` | Get a specific Help Center article by ID |
| `search_articles` | Search Help Center articles by keyword |
| `create_article` | Create a new Help Center article in a section |
| `list_sections` | List Help Center sections (optionally by category) |
| `list_categories` | List Help Center categories |
| `list_sla_policies` | List all SLA policies and their targets |
| `get_sla_policy` | Get a specific SLA policy by ID |
| `list_ticket_attachments` | List all attachments on a ticket across comments |
| `list_user_identities` | List all identities (email, phone, etc.) for a user |
| `list_organization_memberships` | List org memberships (filter by user or org) |
| `list_group_memberships` | List group memberships (filter by user or group) |

## MCP Resources

| Resource | Description |
|----------|-------------|
| `zendesk://reference/search-syntax` | Comprehensive Zendesk search query syntax guide |
| `zendesk://reference/ticket-statuses` | Ticket statuses, transitions, priorities, and types reference |
| `zendesk://tickets/{id}` | Resource template — retrieve a ticket by ID |
| `zendesk://users/{id}` | Resource template — retrieve a user by ID |

## MCP Prompts

| Prompt | Description |
|--------|-------------|
| `triage_ticket` | Analyze a ticket and suggest priority, type, group, and next steps |
| `summarize_ticket` | Summarize a ticket conversation for handoff or escalation |

## Search Syntax Examples

```
type:ticket status:open priority:urgent assignee:me
type:ticket created>7days -status:closed
type:ticket tags:billing organization:"Acme Inc"
type:user role:agent email:@example.com
```

## Application Insights (Optional)

To enable telemetry, update the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx` with your Application Insights connection string.

## API Reference

- [Zendesk Support API Documentation](https://developer.zendesk.com/api-reference/)
- [Zendesk Search API](https://developer.zendesk.com/api-reference/ticketing/ticket-management/search/)
- [Zendesk OAuth Authentication](https://developer.zendesk.com/api-reference/ticketing/introduction/#oauth-authentication)

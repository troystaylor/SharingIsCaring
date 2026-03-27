# Planhat

## Overview

Custom connector for [Planhat](https://planhat.com/) — the Customer Success platform. Works with both **Copilot Studio** (via MCP) and **Power Automate** (via REST operations) for managing companies, contacts, licenses, notes, tasks, conversations, NPS, tickets, revenue, objectives, and assets.

## Strategy

This connector has **35+ endpoints** and ships with **two script variants**:

| Script | Pattern | Description |
|--------|---------|-------------|
| `script.csx` (default) | **Mission Command** | 3 orchestration tools — scan/launch/sequence across all 35+ operations |
| `script-per-tool.csx` | **Per-tool** | 15 curated tools for common CS scenarios |

To switch: rename `script-per-tool.csx` to `script.csx` (back up the original first).

> **SYNC:** When adding tools, update both scripts. Each script has a comment at the top pointing to its paired file.

## Prerequisites

- Planhat account with API access
- API token (tenant token) from Settings > Service Accounts

## Setup

1. In Planhat, go to **Settings** > **Service Accounts**
2. Generate or copy your API token
3. Import the connector files into Power Platform
4. Create a new connection using your API token
5. **Copilot Studio**: Add the connector to your agent — the MCP endpoint exposes tools automatically
6. **Power Automate**: Use the individual REST operations listed below

## Authentication

Uses API token authentication. The token is passed as `Authorization: Bearer <api_token>` header. Both scripts pull the token from the connection's `api_token` parameter.

## Operations

### MCP Endpoint (Copilot Studio)

| Operation | Description |
|-----------|-------------|
| `InvokeMCP` | MCP endpoint for Copilot Studio — exposes Mission Command or per-tool interface depending on active script |

### REST Operations (Power Automate)

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| `ListCompanies` | GET | `/companies` | List companies with optional limit and offset |
| `CreateCompany` | POST | `/companies` | Create a new company |
| `GetCompany` | GET | `/companies/{id}` | Get a company by ID |
| `UpdateCompany` | PUT | `/companies/{id}` | Update an existing company |
| `DeleteCompany` | DELETE | `/companies/{id}` | Delete a company |
| `ListEndusers` | GET | `/endusers` | List endusers (contacts) with optional company filter |
| `CreateEnduser` | POST | `/endusers` | Create a new enduser |
| `ListLicenses` | GET | `/licenses` | List licenses with optional company filter |
| `CreateLicense` | POST | `/licenses` | Create a new license |
| `ListNotes` | GET | `/notes` | List notes with optional company filter |
| `CreateNote` | POST | `/notes` | Create a new note |
| `ListTasks` | GET | `/tasks` | List tasks with optional status and company filter |
| `CreateTask` | POST | `/tasks` | Create a new task |
| `ListConversations` | GET | `/conversations` | List conversations with optional company filter |
| `CreateConversation` | POST | `/conversations` | Create a new conversation (email, call, meeting) |
| `ListNps` | GET | `/nps` | List NPS responses with optional company filter |
| `CreateNps` | POST | `/nps` | Create an NPS response |
| `ListTickets` | GET | `/tickets` | List tickets with optional status filter |
| `CreateTicket` | POST | `/tickets` | Create a new support ticket |
| `ListRevenue` | GET | `/revenues` | List revenue entries with optional company filter |
| `CreateRevenue` | POST | `/revenues` | Create a revenue entry (MRR/ARR) |
| `ListObjectives` | GET | `/objectives` | List objectives/success plans with optional company filter |
| `ListUsers` | GET | `/users` | List Planhat team members |
| `ListAssets` | GET | `/assets` | List assets with optional company filter |
| `CreateAsset` | POST | `/assets` | Create a new asset |
| `ListCustomFields` | GET | `/customfields` | List custom field definitions with optional model filter |

### MCP Tool ↔ REST Operation Mapping (Per-tool script)

| MCP Tool | REST Operation |
|----------|---------------|
| `list_companies` | `ListCompanies` |
| `get_company` | `GetCompany` |
| `create_company` | `CreateCompany` |
| `list_endusers` | `ListEndusers` |
| `create_enduser` | `CreateEnduser` |
| `list_licenses` | `ListLicenses` |
| `create_license` | `CreateLicense` |
| `list_notes` | `ListNotes` |
| `create_note` | `CreateNote` |
| `list_tasks` | `ListTasks` |
| `create_task` | `CreateTask` |
| `list_conversations` | `ListConversations` |
| `create_conversation` | `CreateConversation` |
| `list_nps` | `ListNps` |
| `create_nps` | `CreateNps` |

## API Hosts

| Host | Operations |
|------|------------|
| `api.planhat.com` | All REST and MCP operations |
| `analytics.planhat.com` | Activity tracking (Mission Command `track_activity` capability) |

## Example Prompts (Copilot Studio)

### Mission Command (default)
- "Scan for company operations" → triggers `scan_planhat`
- "List all companies with MRR over $10k" → scan then launch list companies with sort/filter
- "Create a task for Acme Corp to follow up on renewal" → scan then launch create task
- "Log a call with the VP of Engineering at TechCo" → scan then launch create conversation

### Per-tool (alternative)
- "List all companies" → `list_companies`
- "Get details for company abc123" → `get_company`
- "Create a note for Acme Corp about our QBR" → `create_note`
- "What are the open tasks for company xyz?" → `list_tasks`
- "Create a license for the Enterprise plan" → `create_license`

## Files

| File | Purpose |
|------|---------|
| `apiDefinition.swagger.json` | OpenAPI definition with MCP + 26 REST operations |
| `apiProperties.json` | Connector metadata and script operation routing |
| `script.csx` | Mission Command MCP handler + REST routing (default) |
| `script-per-tool.csx` | Per-tool MCP handler + REST routing (alternative) |
| `readme.md` | This file |

## Notes

- The API supports field selection via `select` query parameter
- Sorting uses field name prefix: `-fieldName` for descending
- Bulk upsert operations match on `externalId` for create-or-update behavior

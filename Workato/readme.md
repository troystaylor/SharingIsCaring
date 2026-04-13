# Workato

## Overview

The Workato connector provides access to the full [Workato Developer API](https://www.workato.com/developers), enabling management of automation recipes, connections, jobs, Agent Studio genies, MCP servers, data tables, deployments, and more from Power Automate flows and Copilot Studio agents.

This is a **Power Mission Control** (PMC) connector: 225 operations are available as standard Power Automate actions, and 46 high-value operations are exposed via MCP for AI agent use in Copilot Studio.

## Prerequisites

- A [Workato](https://www.workato.com) workspace (any plan)
- An API client token generated from **Workspace admin > API clients**
- Knowledge of which [data center](https://docs.workato.com/security/data-protection/data-center.html) your workspace is hosted on

## Obtaining Credentials

1. Sign in to your Workato workspace.
2. Navigate to **Workspace admin > API clients**.
3. Click **Create API client** (or select an existing client).
4. Assign the appropriate role with permissions for the APIs you need.
5. Generate and copy the **API token** — you'll need it when creating the connection.

## Supported Data Centers

| Region | Host |
|--------|------|
| US (default) | `www.workato.com` |
| EU | `app.eu.workato.com` |
| JP | `app.jp.workato.com` |
| SG | `app.sg.workato.com` |
| AU | `app.au.workato.com` |
| IL | `app.il.workato.com` |
| Developer Sandbox | `app.trial.workato.com` |

Select your data center when creating the connection. All API calls will be routed to the correct host automatically.

## Supported Operations

### Recipes (18 operations)
- List, create, get, update, delete, copy recipes
- Start, stop, reset trigger, update connection, poll now
- Get recipe versions, version details, update version comments
- Get recipe health, start health analysis

### Jobs (5 operations)
- List jobs, get job details, repeat jobs, get test cases, resume job

### Connections (5 operations)
- List, create, update, delete, disconnect connections

### Folders & Projects (7 operations)
- List, create, update, delete folders
- List, update, delete projects

### Lookup Tables (8 operations)
- List, create, batch delete lookup tables
- Lookup entry, list rows, add/get/update/delete rows

### Agent Studio (16 operations)
- List, create, get, update, delete genies
- Start/stop genies, assign/remove skills, knowledge bases, user groups
- List, create, get knowledge bases; list data sources, recipes
- List, create, get skills

### MCP Servers (14 operations)
- List, create, get, update, delete MCP servers
- Renew token, assign tools, assign/remove user groups, move server
- Get/update server policies, list/update/delete tools
- List MCP user groups

### Data Tables (6 operations)
- List, create, get, update, delete, truncate data tables

### API Platform (14 operations)
- List/create API collections, list API endpoints, enable/disable endpoints
- List/create/get/update/delete API clients, list/create/update/delete API keys
- Enable/disable/refresh API keys, list API portals

### Environment & Configuration (22 operations)
- Certificate bundles, developer API clients, collaborator groups
- Connectors, custom connectors, custom OAuth profiles
- Activity logs, tags, environment properties, environment roles
- Event stream topics, secrets cache

### Recipe Lifecycle & Deployments (21 operations)
- Export manifests, packages, project builds, deployments
- Deployment review workflow (assign, submit, approve, reject, reopen)
- Project grants, project roles, legacy roles, role migration

### Workspace Management (18 operations)
- Workspace details, tag assignments, test automation
- Members, member invitations, member privileges
- IAM users, user groups, app links

### MCP Endpoint
- `InvokeMCP` — Model Context Protocol endpoint for Copilot Studio agents (46 curated tools)

## MCP Capabilities (Copilot Studio)

When used as an MCP connector in Copilot Studio, the agent has access to 46 high-value tools across these domains:

| Domain | Tools | Examples |
|--------|-------|---------|
| Workspace | 1 | Get workspace details |
| Recipes | 7 | List, get, start, stop, health, versions |
| Jobs | 2 | List jobs, get job details |
| Connections | 1 | List connections |
| Folders & Projects | 2 | List folders, list projects |
| Data | 5 | Lookup tables, data tables |
| Agent Studio | 7 | Genies, knowledge bases, skills |
| MCP Servers | 3 | List, get, list tools |
| API Platform | 2 | Collections, endpoints |
| Environment | 5 | Activity logs, tags, properties, event topics |
| Deployments | 4 | List/get deployments, builds |
| Members | 2 | List, get members |
| Testing | 2 | Run, get test results |
| RLCM | 2 | Export/get packages |

## Known Issues and Limitations

- The Workato API has rate limits per endpoint category (typically 60-2000 requests/minute). See the [Workato API documentation](https://docs.workato.com/workato-api.html) for details.
- OAuth connection creation via the API requires pre-existing access/refresh tokens. Shell connections can be created instead.
- Starting May 7, 2026, `folder_id` becomes required for recipe and connection creation endpoints.

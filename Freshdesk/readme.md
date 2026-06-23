# Freshdesk

## Overview

A dual-mode Power Platform custom connector for Freshdesk customer support with 129 operations. Provides an MCP endpoint for Copilot Studio and typed operations with full schemas for Power Automate.

## Prerequisites

- A Freshdesk account (any plan)
- Your Freshdesk API key (Profile Settings > API Key)

## Setup

Before deploying, open `apiDefinition.swagger.json` and replace the `host` value:

```json
"host": "placeholder.freshdesk.com"
```

Change `placeholder` to your Freshdesk subdomain (e.g. `mycompany.freshdesk.com`).

## Obtaining Credentials

1. Log in to your Freshdesk portal
2. Click your profile picture (top right) > **Profile Settings**
3. Your API key is displayed below the Change Password section

## Connection Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| **API Key** | Your personal API key | `abcdefghij1234567890` |

## Supported Operations (129)

### Tickets (17)
- Create Ticket, View Ticket, List Tickets, Update Ticket, Delete Ticket, Restore Ticket
- Filter Tickets, List Ticket Fields, List Ticket Conversations, List Ticket Time Entries
- Forward Ticket, Merge Tickets, List Watchers, Add Watcher, Remove Watcher
- Create Outbound Email, View Archive Ticket

### Conversations (4)
- Create Reply, Create Note, Update Conversation, Delete Conversation

### Contacts (10)
- Create Contact, View Contact, List Contacts, Update Contact, Delete Contact
- Filter Contacts, Search Contacts, Restore Contact, Hard Delete Contact, Merge Contacts

### Companies (7)
- Create Company, View Company, List Companies, Update Company, Delete Company
- Filter Companies, Search Companies

### Agents (7)
- View Agent, List Agents, Get Current Agent
- Create Agent, Update Agent, Delete Agent, Search Agents

### Groups (5)
- View Group, List Groups, Create Group, Update Group, Delete Group

### Skills (5)
- List Skills, View Skill, Create Skill, Update Skill, Delete Skill

### Solutions / Knowledge Base (16)
- List/Create/View/Update/Delete Solution Categories
- List/Create/View/Update/Delete Solution Folders
- List/Create/View/Update/Delete Solution Articles
- Search Solution Articles

### Forums / Discussions (19)
- List/Create/View/Update/Delete Forum Categories
- List/Create/View/Update/Delete Forums
- List/Create/View/Update/Delete Topics
- List/Create/Update/Delete Comments

### Time Entries (5)
- List All Time Entries, Create Time Entry, Update Time Entry, Delete Time Entry, Toggle Timer

### Custom Objects (7)
- List Custom Object Schemas, View Custom Object Schema
- List/Create/View/Update/Delete Custom Object Records

### Bulk Operations (2)
- Bulk Update Tickets, Bulk Delete Tickets

### Ticket Extras (4)
- View Ticket Summary, Update Ticket Summary, List Ticket Forms, Bulk operations

### Canned Responses (5)
- List Canned Response Folders, List Canned Responses, View Canned Response
- Create Canned Response, Update Canned Response

### Admin / Configuration (9)
- List Roles, View Role
- List SLA Policies
- List Products, View Product
- List Business Hours, View Business Hour
- List Scenario Automations
- View Account

### Email (4)
- List Email Mailboxes, View Email Mailbox, List Email Configs

### Fields (2)
- List Contact Fields, List Company Fields

### CSAT (2)
- List Surveys, List Satisfaction Ratings

### MCP (1)
- Invoke Freshdesk MCP — MCP protocol endpoint for Copilot Studio

## Rate Limits

Freshdesk enforces per-account rate limits based on plan:

| Plan | Calls/Minute |
|------|-------------|
| Growth | 100 |
| Pro | 400 |
| Enterprise | 700 |

## Known Issues

- The `include` parameter on List Tickets consumes additional API credits per embedded resource
- Filter Tickets queries are limited to pages 1-10 (max 300 results)
- Tickets older than 30 days are only returned when using the `updated_since` filter
- Custom Object record IDs are strings (e.g. "BKG-1"), not integers

# Microsoft To Do

Power Platform custom connector for the Microsoft Graph To Do API. Manage task lists, tasks, checklist items, linked resources, and file attachments. Supports delta queries for sync scenarios and outlook categories for task tagging.

**Features:**
- **28 operations** covering task lists, tasks, checklist items, linked resources, attachments, delta sync, and categories
- **MCP-enabled** for Copilot Studio integration with AI agents
- **Recurrence support** for creating recurring tasks (daily, weekly, monthly, yearly)
- **Application Insights** telemetry for monitoring and debugging

## Prerequisites

- Microsoft 365 account with To Do access
- Azure AD app registration with the following:
  - **API permissions**: `Tasks.ReadWrite`, `MailboxSettings.Read`, `offline_access` (delegated)
  - **Redirect URI**: `https://global.consent.azure-apim.net/redirect`
  - **Platform**: Web

## Setup

1. Register an app in [Azure AD](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Add delegated permissions under Microsoft Graph: `Tasks.ReadWrite`, `MailboxSettings.Read`
3. Create a client secret
4. Update `apiProperties.json` with your `clientId`
5. Import the connector into Power Platform or deploy via PAC CLI

## Authentication

Uses Azure AD OAuth 2.0 with the `aad` identity provider. Scopes: `Tasks.ReadWrite MailboxSettings.Read offline_access`.

## Operations

### Task Lists

| Operation | Description |
|-----------|-------------|
| ListTaskLists | Get all task lists |
| CreateTaskList | Create a new task list |
| GetTaskList | Get a specific task list |
| UpdateTaskList | Update a task list name |
| DeleteTaskList | Delete a task list |

### Tasks

| Operation | Description |
|-----------|-------------|
| ListTasks | Get all tasks in a list (supports OData filter, top, skip, orderby) |
| CreateTask | Create a new task (with optional body, due date, recurrence, categories) |
| GetTask | Get a specific task |
| UpdateTask | Update a task |
| DeleteTask | Delete a task |

### Checklist Items

| Operation | Description |
|-----------|-------------|
| ListChecklistItems | Get all checklist items for a task |
| CreateChecklistItem | Create a new checklist item |
| GetChecklistItem | Get a specific checklist item |
| UpdateChecklistItem | Update a checklist item |
| DeleteChecklistItem | Delete a checklist item |

### Linked Resources

| Operation | Description |
|-----------|-------------|
| ListLinkedResources | Get all linked resources for a task |
| CreateLinkedResource | Create a linked resource |
| GetLinkedResource | Get a specific linked resource |
| UpdateLinkedResource | Update a linked resource |
| DeleteLinkedResource | Delete a linked resource |

### Attachments

| Operation | Description |
|-----------|-------------|
| ListTaskAttachments | Get all file attachments for a task |
| CreateTaskAttachment | Attach a file to a task (up to 3 MB, base64 encoded) |
| GetTaskAttachment | Get a specific attachment and its content |
| DeleteTaskAttachment | Delete a file attachment |

### Delta Queries

| Operation | Description |
|-----------|-------------|
| GetTasksDelta | Get tasks added, deleted, or updated since last sync |
| GetTaskListsDelta | Get task lists added, deleted, or updated since last sync |

### Categories

| Operation | Description |
|-----------|-------------|
| ListOutlookCategories | Get all outlook categories for tagging tasks |

## MCP Tools (Copilot Studio)

The `InvokeMCP` endpoint exposes 28 tools via MCP protocol (mcp-streamable-1.0):

| Tool | Description |
|------|-------------|
| `list_task_lists` | Get all task lists |
| `get_task_list` | Get a specific task list |
| `create_task_list` | Create a new task list |
| `update_task_list` | Update a task list name |
| `delete_task_list` | Delete a task list |
| `list_tasks` | Get all tasks in a list |
| `get_task` | Get a specific task |
| `create_task` | Create a new task |
| `update_task` | Update a task |
| `complete_task` | Mark a task as completed |
| `delete_task` | Delete a task |
| `list_checklist_items` | Get checklist items for a task |
| `get_checklist_item` | Get a specific checklist item |
| `create_checklist_item` | Create a checklist item |
| `update_checklist_item` | Update a checklist item |
| `delete_checklist_item` | Delete a checklist item |
| `list_linked_resources` | Get linked resources for a task |
| `get_linked_resource` | Get a specific linked resource |
| `create_linked_resource` | Create a linked resource |
| `update_linked_resource` | Update a linked resource |
| `delete_linked_resource` | Delete a linked resource |
| `list_task_attachments` | Get file attachments for a task |
| `get_task_attachment` | Get a specific attachment |
| `create_task_attachment` | Attach a file to a task |
| `delete_task_attachment` | Delete a file attachment |
| `get_tasks_delta` | Get tasks changed since last sync |
| `get_task_lists_delta` | Get task lists changed since last sync |
| `list_outlook_categories` | Get outlook categories for tagging |

## API Reference

- [To Do API overview](https://learn.microsoft.com/en-us/graph/api/resources/todo-overview?view=graph-rest-1.0)
- [todoTaskList](https://learn.microsoft.com/en-us/graph/api/resources/todotasklist?view=graph-rest-1.0)
- [todoTask](https://learn.microsoft.com/en-us/graph/api/resources/todotask?view=graph-rest-1.0)
- [checklistItem](https://learn.microsoft.com/en-us/graph/api/resources/checklistitem?view=graph-rest-1.0)
- [linkedResource](https://learn.microsoft.com/en-us/graph/api/resources/linkedresource?view=graph-rest-1.0)
- [taskFileAttachment](https://learn.microsoft.com/en-us/graph/api/resources/taskfileattachment?view=graph-rest-1.0)
- [delta query](https://learn.microsoft.com/en-us/graph/api/todotask-delta?view=graph-rest-1.0)
- [outlookCategory](https://learn.microsoft.com/en-us/graph/api/outlookuser-list-mastercategories?view=graph-rest-1.0)

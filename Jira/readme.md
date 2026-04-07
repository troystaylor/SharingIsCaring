# Jira

Power Platform custom connector for Jira Cloud with core REST operations and MCP tools for issues and projects.

## What It Does

- Lists projects, fields, users, and searches issues
- Creates and updates issues, comments, and transitions
- Exposes MCP tools for key operations (projects and issues)
- Includes optional Application Insights telemetry

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

When creating a connection, provide:

- **Client ID**: from your Atlassian OAuth app
- **Client Secret**: from your Atlassian OAuth app
- **Jira site URL**: `https://your-domain.atlassian.net`

### 4. Add to Copilot Studio

1. In Copilot Studio, open your agent
2. Add this connector as an action
3. Test with prompts like "List Jira projects" or "Search Jira issues with JQL"

## MCP Tools

- `jira_list_projects` - List visible projects
- `jira_list_issue_types` - List issue types
- `jira_list_project_roles` - List roles for a project
- `jira_list_project_statuses` - List statuses for a project
- `jira_search_issues` - Search issues using JQL
- `jira_get_issue` - Get an issue by ID or key
- `jira_create_issue` - Create a new issue
- `jira_create_issue_simple` - Create an issue from common fields
- `jira_update_issue` - Update an existing issue
- `jira_list_comments` - List comments for an issue
- `jira_add_comment` - Add a comment to an issue
- `jira_get_transitions` - List available transitions
- `jira_transition_issue` - Transition an issue to a new status

## REST Operations

| Operation | Description |
|----------|-------------|
| ListProjects | List projects |
| SearchIssues | Search issues using JQL |
| GetIssue | Get an issue by ID or key |
| CreateIssue | Create a new issue |
| UpdateIssue | Update an issue |
| ListIssueTypes | List issue types |
| GetProjectRoles | Get roles for a project |
| GetProjectStatuses | Get statuses for a project |
| ListComments | List comments for an issue |
| AddComment | Add a comment to an issue |
| GetTransitions | List available transitions |
| TransitionIssue | Transition an issue |
| ListFields | List issue fields |
| GetUser | Get a user by accountId |
| SearchUsers | Search users |

## OAuth Scopes

The connector requests the following scopes:

- `read:jira-work`
- `write:jira-work`
- `read:jira-user`

Adjust scopes in `apiProperties.json` if you add or remove operations.

## Jira Document Format (ADF)

Jira Cloud uses Atlassian Document Format for rich text fields. MCP tools that accept plain text for descriptions or comments convert the text into a simple ADF document before sending the request.

## Application Insights

To enable telemetry, set the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`.

Events tracked:

- `RequestReceived`
- `RequestCompleted`
- `RequestError`
- `McpRequestReceived`
- `McpToolCallStarted`
- `McpToolCallCompleted`
- `McpToolCallError`

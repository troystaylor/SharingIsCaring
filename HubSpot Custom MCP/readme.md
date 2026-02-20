# HubSpot Custom MCP

A custom MCP (Model Context Protocol) connector for Copilot Studio that provides CRUD operations against HubSpot CRM objects including companies, sales reps, emails, sequences, reminders, and contract lifecycles.

## Prerequisites

- A HubSpot account (Professional or Enterprise for sequences)
- A HubSpot public OAuth app with the following scopes:
  - `crm.objects.companies.read` / `crm.objects.companies.write`
  - `crm.objects.owners.read`
  - `crm.objects.deals.read` / `crm.objects.deals.write`
  - `sales-email-read`
  - `crm.objects.contacts.read`
  - `crm.schemas.custom.read`
  - `crm.objects.custom.read` / `crm.objects.custom.write`
  - `automation`
- Power Platform environment with custom connector support

## Setup

### 1. Create a HubSpot OAuth App

You can create the app using the HubSpot CLI or the developer portal.

**Option A — HubSpot CLI (hs):**

The `hubspot-app/` subfolder contains a pre-configured HubSpot project. Install the CLI, authenticate, and upload:

```powershell
npm install -g @hubspot/cli
cd hubspot-app
hs auth            # authenticate with your HubSpot account
hs project upload  # deploys the app with all scopes and redirect URL pre-configured
```

> **Note:** HubSpot CLI v8 uses the Ink library for terminal rendering and does **not** produce output in the VS Code integrated terminal. Run these commands in a standalone PowerShell or Windows Terminal window.

After uploading, go to **HubSpot Settings → Integrations → Private Apps → HubSpot Custom MCP** to copy the Client ID and Client Secret.

**Option B — Developer Portal:**

- Go to [app.hubspot.com](https://app.hubspot.com/) → **Settings** → **Account Setup** → **Integrations** → **Private Apps**
- Create a new app, select the scopes listed in Prerequisites
- Under **Auth → Redirect URLs**, add your connector's per-connector redirect URL (see step 3 below)
- Copy the **Client ID** and **Client Secret**

### 2. Update the connector files

In [apiProperties.json](apiProperties.json), replace `[[REPLACE_WITH_HUBSPOT_CLIENT_ID]]` with your app's Client ID.

### 3. Deploy the connector

**Initial deployment** (use `pac connector create`):

```powershell
pac connector create --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx --environment <your-environment-id>
```

> `pac connector create` does **not** support a `--secret` flag. After creating the connector, you must set the client secret separately (see step 4).

**Update an existing connector:**

```powershell
pac connector update --api-definition-file apiDefinition.swagger.json --api-properties-file apiProperties.json --script-file script.csx --connector-id <connector-id>
```

### 4. Set the OAuth Client Secret

The `pac` CLI does not support passing an OAuth client secret. Use one of these methods:

**Option A — Power Platform portal (easiest):**
1. Go to [make.powerapps.com](https://make.powerapps.com) → **Custom connectors**
2. Edit your connector → **Security** tab
3. Paste the Client Secret and click **Update connector**

**Option B — paconn CLI:**

```powershell
pip install paconn
paconn login
paconn update -e <environment-id> -c <connector-api-id> --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx --secret "<client-secret>"
```

> **Important:** `paconn` uses a different connector ID format than `pac`. The `pac` connector ID (a GUID like `c71508f3-...`) will **not** work with `paconn`. To find the correct ID, open the connector in the Power Platform portal — the URL contains the paconn-compatible ID after `customConnectorId=` (e.g., `shared_new-5fhubspot-20custom-20mcp-5f74dd22e1a08b664f`).

### 5. Configure the Redirect URL in HubSpot

After deploying the connector, copy the **per-connector redirect URL** from the connector's Security tab. It will look like:

```
https://global.consent.azure-apim.net/redirect/<connector-api-id>
```

Add this URL to your HubSpot OAuth app's **Redirect URLs** list. If you used the HubSpot CLI project, update the URL in `hubspot-app/src/app/app-hsmeta.json` and run `hs project upload`.

### 6. Create a connection

- In Power Platform, go to **Connections** → **New connection**
- Select **HubSpot Custom MCP**
- Enter your Client ID and Client Secret when prompted
- Sign in with your HubSpot account to authorize

### 7. Add to Copilot Studio

- Open your agent in Copilot Studio
- Go to **Actions** → **Add action**
- Search for "HubSpot Custom MCP" and add it

## Available Tools (36)

### Companies (6 tools)
| Tool | Description |
|------|-------------|
| `list_companies` | List companies with pagination and property selection |
| `get_company` | Get a single company by ID |
| `create_company` | Create a new company with properties like name, domain, industry |
| `update_company` | Update company properties |
| `delete_company` | Delete a company (moves to recycling bin) |
| `search_companies` | Search companies with filters, sorting, and query text |

### Sales Reps / Owners (2 tools)
| Tool | Description |
|------|-------------|
| `list_owners` | List all owners (sales reps) with optional email filter |
| `get_owner` | Get a single owner by ID |

### Emails (6 tools)
| Tool | Description |
|------|-------------|
| `list_emails` | List email engagements |
| `get_email` | Get a single email engagement by ID |
| `create_email` | Log a new email with subject, body, and direction |
| `update_email` | Update email engagement properties |
| `delete_email` | Delete an email engagement |
| `search_emails` | Search emails with filters |

### Sequences (3 tools)
| Tool | Description |
|------|-------------|
| `list_sequences` | List available sales sequences |
| `get_sequence` | Get sequence details by ID |
| `enroll_in_sequence` | Enroll a contact in a sequence |

### Tasks / Reminders (6 tools)
| Tool | Description |
|------|-------------|
| `list_tasks` | List tasks (reminders/to-dos) |
| `get_task` | Get a single task by ID |
| `create_task` | Create a task with subject, body, status, priority, and owner |
| `update_task` | Update task properties |
| `delete_task` | Delete a task |
| `search_tasks` | Search tasks with filters |

### Deals / Contract Lifecycles (6 tools)
| Tool | Description |
|------|-------------|
| `list_deals` | List deals with pipeline stage tracking |
| `get_deal` | Get a single deal by ID |
| `create_deal` | Create a deal with name, stage, pipeline, amount, close date |
| `update_deal` | Update deal properties (e.g., move through pipeline stages) |
| `delete_deal` | Delete a deal |
| `search_deals` | Search deals with filters |

### Custom Objects (8 tools)
| Tool | Description |
|------|-------------|
| `list_custom_object_schemas` | List all custom object schemas defined in HubSpot |
| `get_custom_object_schema` | Get full schema definition for a custom object type |
| `list_custom_objects` | List records of any custom object type |
| `get_custom_object` | Get a single custom object record by ID |
| `create_custom_object` | Create a new custom object record |
| `update_custom_object` | Update a custom object record |
| `delete_custom_object` | Delete a custom object record |
| `search_custom_objects` | Search custom object records with filters |

## Key Properties by Object

### Companies
`name`, `domain`, `industry`, `phone`, `city`, `state`, `country`, `numberofemployees`, `annualrevenue`, `hubspot_owner_id`

### Emails
`hs_email_subject`, `hs_email_text`, `hs_email_direction` (EMAIL_SENT / EMAIL_RECEIVED), `hs_timestamp`, `hs_email_status`

### Tasks
`hs_task_subject`, `hs_task_body`, `hs_task_status` (NOT_STARTED / IN_PROGRESS / COMPLETED / DEFERRED), `hs_task_priority` (LOW / MEDIUM / HIGH), `hs_timestamp`, `hubspot_owner_id`

### Deals
`dealname`, `dealstage`, `pipeline`, `amount`, `closedate`, `hubspot_owner_id`, `description`

## Search Operators

All search tools support these filter operators:
- `EQ` - Equal to
- `NEQ` - Not equal to
- `LT` / `LTE` - Less than / Less than or equal
- `GT` / `GTE` - Greater than / Greater than or equal
- `CONTAINS_TOKEN` - Contains token
- `NOT_CONTAINS_TOKEN` - Does not contain token

## Application Insights (Optional)

To enable telemetry, set the `APP_INSIGHTS_CONNECTION_STRING` constant in [script.csx](script.csx) to your Application Insights connection string. Events tracked:
- `McpRequestReceived` - Every incoming MCP request
- `McpRequestCompleted` - Successful completions with duration
- `McpRequestError` - Unhandled errors
- `ToolCallStarted` / `ToolCallFailed` - Individual tool execution

## API Reference

- [HubSpot CRM API](https://developers.hubspot.com/docs/api-reference/crm)
- [HubSpot Owners API](https://developers.hubspot.com/docs/api-reference/crm/owners)
- [HubSpot Sequences API](https://developers.hubspot.com/docs/api-reference/automation/sequences)

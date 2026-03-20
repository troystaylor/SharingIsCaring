# AI Builder Credits MCP

Monitor AI Builder credit consumption across your Power Platform environment using the Dataverse AI Event table. This connector provides MCP tools for Copilot Studio agents.

## Publisher: Troy Taylor

## Prerequisites

- A Power Platform environment with AI Builder usage
- An Azure AD app registration with **delegated** permissions:
  - Dataverse user access (via environment URL scope)
- Admin consent granted for the permissions

## Obtaining Credentials

1. Go to the [Azure portal](https://portal.azure.com) > **Microsoft Entra ID** > **App registrations**
2. Register a new application or select an existing one
3. Under **Authentication**, add `https://global.consent.azure-apim.net/redirect` as a redirect URI
4. Copy the **Application (client) ID** into the connector's `apiProperties.json` `clientId` field

## Supported MCP Tools

| Tool | Description |
|------|-------------|
| `list_ai_events` | List recent AI Builder events with credit consumption, model name, source, and status |
| `get_credit_summary` | Get credit consumption summary grouped by model and source for a date range |
| `get_daily_usage` | Get daily credit consumption for trend analysis |
| `get_ai_event` | Get details of a specific AI Builder event by ID |
| `list_ai_models` | List AI Builder models in the environment |

## Tool Details

### list_ai_events

List recent AI Builder events with credit consumption details.

**Parameters:**
- `top` (integer) - Maximum events to return (default: 25, max: 100)
- `source` (string) - Filter by source: PowerAutomate, PowerApps, API, or CopilotStudio
- `fromDate` (string) - Filter events from this date (ISO 8601)

**Returns:**
- Event ID, model name, credits consumed, date, source, status, automation name

### get_credit_summary

Get a summary of credit consumption grouped by model and source.

**Parameters:**
- `fromDate` (string) - Start date (defaults to first of current month)
- `toDate` (string) - End date (defaults to today)

**Returns:**
- Total events and credits
- Breakdown by AI model
- Breakdown by source (Power Automate, Power Apps, API, Copilot Studio)

### get_daily_usage

Get credit consumption grouped by day for trend analysis.

**Parameters:**
- `days` (integer) - Days to look back (default: 7, max: 30)

**Returns:**
- Daily usage with event count and credits per day

### get_ai_event

Get details of a specific AI Builder event.

**Parameters:**
- `eventId` (string, required) - The AI event GUID

**Returns:**
- Full event details including model template, automation link, data info

### list_ai_models

List AI Builder models in the environment.

**Parameters:**
- `top` (integer) - Maximum models to return (default: 50)

**Returns:**
- Model ID, name, template type, status, creation date

## Data Source

This connector queries the Dataverse `msdyn_aievents` table which stores:
- **msdyn_creditconsumed** - Credits used per action
- **msdyn_AIModelId** - Reference to the AI model
- **msdyn_processingdate** - When the action occurred
- **msdyn_consumptionsource** - 0=Power Automate, 1=Power Apps, 2=API, 3=Copilot Studio
- **msdyn_processingstatus** - 0=Processed
- **msdyn_automationname** - Name of the flow/app that triggered the action

## Known Limitations

- Data is environment-scoped — you need separate connections to monitor multiple environments
- Credit consumption data is computed periodically (typically daily) by the platform
- Historical data retention depends on your Dataverse capacity settings

## API Reference

- [AI Builder Activity Monitoring](https://learn.microsoft.com/ai-builder/activity-monitoring)
- [AI Event (msdyn_AIEvent) table](https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/msdyn_aievent)
- [AI Builder Credit Management](https://learn.microsoft.com/ai-builder/credit-management)

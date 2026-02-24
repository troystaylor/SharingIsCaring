# Gong

Connector for the [Gong](https://www.gong.io) Revenue Intelligence platform. Manage calls, users, stats, meetings, CRM data, permissions, engagement, and more.

## Prerequisites

- A Gong account with Technical Administrator access
- A registered OAuth app in Gong with a Client ID and Client Secret ([Create an OAuth app](https://help.gong.io/docs/create-an-app-for-gong))

## Setup

1. In Gong, navigate to **Admin > Settings > Ecosystem > API**
2. In the **Integrations** tab, click **Create Integration**
3. Fill in your integration name, description, and logos
4. Select the required authorization scopes for your use case
5. Set the **Redirect URI** to `https://global.consent.azure-apim.net/redirect`
6. Save to receive your **Client ID** and **Client Secret**
7. Create a new custom connector in Power Platform and import the `apiDefinition.swagger.json` file
8. In the connector security settings, enter your Client ID and Client Secret
9. Create a connection and authorize via the Gong OAuth consent screen

> **Note:** The `host` field in `apiDefinition.swagger.json` must be updated to match your Gong instance's API base URL (e.g., `us-12345.api.gong.io`). Each customer has a unique `api_base_url_for_customer` returned in the OAuth token response.

## MCP (Model Context Protocol)

This connector includes an MCP endpoint for use with Copilot Studio agents. The `InvokeMCP` operation exposes all 46 Gong API operations as MCP tools, enabling natural-language interaction with the Gong platform.

### MCP Setup

1. Deploy the connector with the included `script.csx`
2. In Copilot Studio, add the connector as an action
3. The agent will automatically discover available tools via the MCP `tools/list` method

### Application Insights (Optional)

To enable telemetry logging, set the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx` to your Application Insights connection string:

```
InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
```

This will log MCP events including tool calls, initialization, and errors to Application Insights.

## API Limits

Gong limits API access to 3 calls per second and 10,000 calls per day by default. If you receive HTTP 429 responses, wait for the duration specified in the `Retry-After` header before making additional requests.

## Operations

### Calls

| Operation | Description |
|-----------|-------------|
| **List Calls** | List calls within a date range |
| **Add Call** | Upload a new call to Gong |
| **Get Call** | Retrieve data for a specific call |
| **Add Call Recording** | Upload a media file for a call |
| **Get Calls Extensive** | Get detailed call info including participants, topics, trackers, briefs, highlights, and media |
| **Get Call Transcripts** | Retrieve transcripts for specific calls |
| **Give Users Access to Calls** | Grant specific users access to calls |
| **Remove Users Access from Calls** | Remove specific users' access from calls |

### Users

| Operation | Description |
|-----------|-------------|
| **List Users** | List all users in your Gong account |
| **Get User** | Retrieve data for a specific user |
| **Get User Settings History** | Retrieve settings change history for a user |
| **Get Users Extensive** | Get detailed user information |

### Stats

| Operation | Description |
|-----------|-------------|
| **Get Scorecard Stats** | Retrieve scorecard statistics for calls |
| **Get Activity Day by Day** | Get daily activity statistics |
| **Get Aggregate Activity** | Get aggregated activity stats for multiple users |
| **Get Aggregate Activity by Period** | Get aggregated activity broken down by time period |
| **Get Interaction Stats** | Get interaction metrics (talk ratio, interactivity, patience) |

### Settings

| Operation | Description |
|-----------|-------------|
| **List Scorecards** | List all configured scorecards |
| **List Workspaces** | List all workspaces |

### Library

| Operation | Description |
|-----------|-------------|
| **List Library Folders** | List all library folders |
| **Get Library Folder Content** | Get calls in a specific library folder |

### Meetings (Beta)

| Operation | Description |
|-----------|-------------|
| **Create Meeting** | Create a new meeting |
| **Update Meeting** | Update an existing meeting |
| **Delete Meeting** | Delete a meeting |
| **Validate Meeting Integration** | Validate meeting integration status |

### Data Privacy

| Operation | Description |
|-----------|-------------|
| **Get Data for Email Address** | Find all data associated with an email |
| **Get Data for Phone Number** | Find all data associated with a phone number |
| **Erase Data for Email Address** | Permanently purge data for an email (irreversible) |
| **Erase Data for Phone Number** | Permanently purge data for a phone number (irreversible) |

### CRM

| Operation | Description |
|-----------|-------------|
| **Upload CRM Entities** | Upload CRM entities via file (accounts, contacts, deals, etc.) |
| **Get CRM Objects** | Retrieve previously uploaded CRM objects |
| **Register CRM Integration** | Register or update a CRM integration |
| **Get CRM Integration** | Retrieve CRM integration details |
| **Delete CRM Integration** | Delete a CRM integration and all its data |
| **Set CRM Entity Schema** | Define the schema for a CRM entity type |
| **Get CRM Entity Schema** | Retrieve the schema for a CRM entity type |
| **Get CRM Request Status** | Check the status of a CRM upload request |

### Engagement (Beta)

| Operation | Description |
|-----------|-------------|
| **Report Content Shared** | Report a content shared event |
| **Report Content Viewed** | Report a content viewed event |
| **Report Custom Action** | Report a custom engagement action |

### Auditing

| Operation | Description |
|-----------|-------------|
| **List Logs** | Retrieve audit logs (access, activity, call play, etc.) |

### Permissions

| Operation | Description |
|-----------|-------------|
| **List Permission Profiles** | List all permission profiles for a workspace |
| **Create Permission Profile** | Create a new permission profile |
| **Get Permission Profile** | Retrieve a specific permission profile |
| **Update Permission Profile** | Update an existing permission profile |
| **List Permission Profile Users** | List users assigned to a permission profile |

### Engage Flows (Alpha)

| Operation | Description |
|-----------|-------------|
| **List Flows** | List Engage flows for a user |
| **Get Flows for Prospects** | Retrieve flows associated with prospects |
| **Assign Flow to Prospects** | Assign prospects to an Engage flow |

### Digital Interactions (Alpha)

| Operation | Description |
|-----------|-------------|
| **Add Digital Interaction** | Upload digital interaction events |

## Pagination

Many operations support cursor-based pagination. When a response includes a `cursor` value in the `records` object, pass it in the next request to retrieve the next page of results.

## Example Usage

### List recent calls

1. Add the **List Calls** action to your flow
2. Set **From Date** to the start of your date range (e.g., `2024-01-01T00:00:00Z`)
3. Set **To Date** to the end of your date range
4. The response includes call metadata and a cursor for pagination

### Get call transcript

1. First use **List Calls** to get call IDs
2. Add the **Get Call Transcripts** action
3. Pass the call IDs from the previous step
4. The transcript includes speaker identification and timestamps

### Upload a call

1. Use **Add Call** to create the call record with participants, start time, and direction
2. Use **Add Call Recording** with the returned Call ID to upload the media file

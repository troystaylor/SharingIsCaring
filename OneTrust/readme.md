# OneTrust

## Overview

Custom connector for integrating with OneTrust Risk Management and Incident Management APIs. Provides both direct REST endpoints for Power Automate flows and a Model Context Protocol (MCP) endpoint for Copilot Studio agents.

## Prerequisites

- An active [OneTrust](https://www.onetrust.com) subscription
- An OAuth application configured in OneTrust with the following scopes:
  - `RISK` — Risk management
  - `RISK_READ` — Read risk data
  - `INCIDENT` — Incident management
  - `INCIDENT_CREATE` — Incident creation
  - `INCIDENT_READ` — Read incident data
  - `INTEGRATION` — Integration scope for risk upsert and update
  - `ORGANIZATION` — Organization data access
  - `USER` — User data access
- Power Platform environment with custom connector permissions

## Setup

### 1. Create an OAuth Application in OneTrust

1. Log in to your OneTrust admin portal
2. Navigate to **Integration** > **Credentials**
3. Create a new OAuth application
4. Note the **Client ID** and **Client Secret**
5. Set the redirect URI to: `https://global.consent.azure-apim.net/redirect`
6. Assign the required scopes: `RISK`, `RISK_READ`, `INCIDENT`, `INCIDENT_CREATE`, `INCIDENT_READ`, `INTEGRATION`, `ORGANIZATION`, `USER`

### 2. Deploy the Connector

1. Import the connector into your Power Platform environment using PAC CLI or the maker portal
2. When creating a connection, provide:
   - **Client ID** — From your OneTrust OAuth application
   - **Client Secret** — From your OneTrust OAuth application

## Operations

| Operation | Method | Description |
|-----------|--------|-------------|
| Create Risk | POST | Create a new risk in the Risk Register |
| Create Incident | POST | Create a new incident in the Incident Register |
| Get Incident | GET | Retrieve details of a specific incident by ID |
| Search Incidents | POST | Search incidents with filters and full-text search |
| Update Incident | PUT | Update an existing incident |
| Update Incident Stage | POST | Move an incident to a different workflow stage |
| Link Incident to Inventory | POST | Link an incident to assets, processing activities, vendors, or entities |
| Get Organizations | GET | Retrieve the hierarchical list of organizations (needed to look up Organization Group IDs) |
| Upsert Risk | PUT | Create or update a risk based on matching attributes |
| Update Risk | PUT | Fully update an existing risk |
| Delete Risk | DELETE | Delete a risk from the Risk Register |
| Modify Risk | PATCH | Partially update specific fields of a risk |
| Update Risk Stage | POST | Move a risk to a different workflow stage |
| Get Risk Template | GET | Retrieve details of a risk template by ID |

### MCP Endpoint (Copilot Studio)

The `InvokeMCP` operation exposes all tools via the Model Context Protocol for use with Copilot Studio agents:

| Tool | Description |
|------|-------------|
| `createRisk` | Create a new risk in the Risk Register |
| `createIncident` | Create a new incident in the Incident Register |
| `getIncident` | Get details of a specific incident |
| `searchIncidents` | Search incidents with filters and full-text |
| `getOrganizations` | List all organizations in hierarchy |
| `upsertRisk` | Create or update a risk based on match attributes |
| `updateIncident` | Update an existing incident |
| `updateIncidentStage` | Move an incident to the next workflow stage |
| `updateRisk` | Fully update an existing risk |
| `deleteRisk` | Delete a risk |
| `updateRiskStage` | Move a risk to a different workflow stage |
| `linkIncidentToInventory` | Link an incident to inventory items |
| `getRiskTemplate` | Get risk template details |
| `modifyRisk` | Partially update specific fields of a risk |

## Application Insights

The connector includes Application Insights telemetry. To enable logging:

1. Create an Application Insights resource in Azure
2. Copy the connection string
3. Paste it into the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`

### Tracked Events

| Event | Description |
|-------|-------------|
| `RequestReceived` | Incoming request with operation ID and path |
| `RequestCompleted` | Request duration and operation ID |
| `RequestError` | Error details with correlation ID |
| `MCPMethodInvoked` | MCP JSON-RPC method name |
| `MCPError` | MCP protocol errors |
| `ToolCallStarted` | Tool execution begin |
| `ToolCallCompleted` | Successful tool execution |
| `ToolCallFailed` | Tool execution errors |
| `PassthroughCompleted` | Direct REST call status |

## Host Configuration

The connector is configured with `app.onetrust.com` as the host. If your OneTrust instance uses a different hostname (e.g., `app-eu.onetrust.com`, `app-de.onetrust.com`), update the `host` field in `apiDefinition.swagger.json` and the `BASE_URL` constant in `script.csx`.

## Author

Troy Taylor — [troy@troystaylor.com](mailto:troy@troystaylor.com)

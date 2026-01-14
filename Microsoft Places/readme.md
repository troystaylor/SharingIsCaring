# Microsoft Places Connector

Power Platform custom connector for Microsoft Places API with MCP integration for Copilot Studio agents.

## Overview

This connector provides comprehensive access to the Microsoft Places API, enabling complete management of physical spaces in your organization:

- **Buildings** - Physical buildings with address, coordinates, WiFi state
- **Floors** - Floors within buildings with sort order
- **Sections** - Zones or neighborhoods within floors
- **Rooms** - Meeting rooms with email, capacity, A/V devices, booking type
- **Workspaces** - Desk collections with booking modes
- **Desks** - Individual desks (reservable, drop-in, assigned, unavailable)
- **Room Lists** - Collections of rooms/workspaces for Room Finder

## Prerequisites

- Microsoft 365 tenant with Microsoft Places enabled
- Azure AD app registration with appropriate permissions:
  - `Place.Read.All` - Required for read operations
  - `Place.ReadWrite.All` - Required for create, update, and delete operations
- Exchange Admin role is required for write operations
- (Optional) Application Insights resource for telemetry

## Operations

### Read Operations (Place.Read.All)

| Operation | Method | Description |
|-----------|--------|-------------|
| ListBuildings | GET | Get all buildings in the tenant |
| ListFloors | GET | Get all floors in the tenant |
| ListSections | GET | Get all sections (neighborhoods) in the tenant |
| ListRooms | GET | Get all rooms in the tenant |
| ListWorkspaces | GET | Get all workspaces in the tenant |
| ListRoomLists | GET | Get all room lists in the tenant |
| ListDesks | GET | Get all desks in the tenant |
| GetPlace | GET | Get a specific place by ID or email |
| ListRoomsInRoomList | GET | Get rooms in a specific room list |
| ListWorkspacesInRoomList | GET | Get workspaces in a specific room list |

### Write Operations (Place.ReadWrite.All)

| Operation | Method | Description |
|-----------|--------|-------------|
| CreatePlace | POST | Create a new building, floor, section, desk, room, or workspace |
| UpdatePlace | PATCH | Update properties of any place type |
| DeletePlace | DELETE | Delete a building, floor, section, or desk (rooms/workspaces cannot be deleted) |

### MCP Protocol

| Operation | Description |
|-----------|-------------|
| MCP | Model Context Protocol endpoint for Copilot Studio agent integration |

## Place Hierarchy

Places follow a strict hierarchy:

```
Building
  └── Floor (parentId = building)
        └── Section (parentId = floor)
              ├── Desk (parentId = section)
              ├── Workspace (parentId = section)
              └── Room (parentId = floor or section)
```

## MCP Tools (for Copilot Studio)

When used with Copilot Studio agents, the connector exposes these natural language tools:

| Tool | Description |
|------|-------------|
| list_buildings | List all buildings in the organization |
| list_floors | List all floors, optionally filtered by building |
| list_sections | List all sections/neighborhoods |
| list_rooms | List all meeting rooms with capacity and A/V info |
| list_workspaces | List all workspaces |
| list_room_lists | List all room lists |
| list_desks | List all desks |
| list_rooms_in_room_list | List rooms in a specific room list by email |
| list_workspaces_in_room_list | List workspaces in a specific room list by email |
| get_place | Get details of a specific place by ID or email |
| create_place | Create a new place (building, floor, section, desk, room, workspace) |
| update_place | Update properties of an existing place |
| delete_place | Delete a place (building, floor, section, or desk only) |

## Setup

### 1. Create Azure AD App Registration

1. Go to Azure Portal → Microsoft Entra ID → App registrations
2. Click "New registration"
3. Name: `Microsoft Places Connector`
4. Supported account types: Accounts in this organizational directory only
5. Redirect URI: `https://global.consent.azure-apim.net/redirect`

### 2. Configure API Permissions

Add these Microsoft Graph permissions:

| Permission | Type | Description |
|------------|------|-------------|
| `Place.Read.All` | Delegated | Read all places (required) |
| `Place.ReadWrite.All` | Delegated | Read and write all places (for create/update/delete) |

Grant admin consent for your organization.

### 3. Create Client Secret

1. Go to Certificates & secrets
2. New client secret
3. Copy the secret value (you won't see it again)

### 4. Import Connector

1. Go to Power Platform maker portal
2. Navigate to Custom connectors
3. Import from OpenAPI file → select `apiDefinition.swagger.json`
4. On the "Code" tab, enable custom code and paste `script.csx`
5. Update the app ID in `apiProperties.json`
6. Create and test the connector

## Application Insights (Optional)

To enable telemetry tracking for requests, errors, and MCP tool usage:

1. Create an Application Insights resource in Azure
2. Copy the connection string
3. Update the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`:

```csharp
private const string APP_INSIGHTS_CONNECTION_STRING = 
    "InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/";
```

### Telemetry Tracked

- Operation name and duration
- Success/failure status
- Error messages and codes
- MCP tool invocations
- Correlation IDs for tracing

## Creating Places - Examples

### Create a Building

```json
{
  "@odata.type": "microsoft.graph.building",
  "displayName": "Headquarters"
}
```

### Create a Floor

```json
{
  "@odata.type": "microsoft.graph.floor",
  "displayName": "Floor 1",
  "parentId": "<building-id>",
  "sortOrder": 1
}
```

### Create a Section

```json
{
  "@odata.type": "microsoft.graph.section",
  "displayName": "Engineering Wing",
  "parentId": "<floor-id>"
}
```

### Create a Desk

```json
{
  "@odata.type": "microsoft.graph.desk",
  "displayName": "Desk A1",
  "parentId": "<section-id>",
  "mode": {
    "@odata.type": "microsoft.graph.reservablePlaceMode"
  }
}
```

### Create a Room

```json
{
  "@odata.type": "microsoft.graph.room",
  "displayName": "Conference Room Alpha",
  "parentId": "<floor-or-section-id>",
  "bookingType": "standard"
}
```

## Desk/Workspace Modes

| Mode Type | Description |
|-----------|-------------|
| `microsoft.graph.reservablePlaceMode` | Can be reserved/booked |
| `microsoft.graph.dropInPlaceMode` | First-come, first-served |
| `microsoft.graph.assignedPlaceMode` | Permanently assigned to a user |
| `microsoft.graph.unavailablePlaceMode` | Not available for use |

## Limitations

- Rooms, workspaces, and room lists cannot be deleted via the API
- Cannot update: `id`, `placeId`, `emailAddress`, `displayName`, or `bookingType`
- Exchange Admin role required for write operations
- Only delegated permissions supported (no application permissions for write)

## References

- [Places API Overview](https://learn.microsoft.com/graph/api/resources/places-api-overview)
- [Place Resource](https://learn.microsoft.com/graph/api/resources/place)
- [Building Resource](https://learn.microsoft.com/graph/api/resources/building)
- [Floor Resource](https://learn.microsoft.com/graph/api/resources/floor)
- [Section Resource](https://learn.microsoft.com/graph/api/resources/section)
- [Room Resource](https://learn.microsoft.com/graph/api/resources/room)
- [Workspace Resource](https://learn.microsoft.com/graph/api/resources/workspace)
- [Desk Resource](https://learn.microsoft.com/graph/api/resources/desk)
- [Create Place](https://learn.microsoft.com/graph/api/place-post)
- [Update Place](https://learn.microsoft.com/graph/api/place-update)
- [Delete Place](https://learn.microsoft.com/graph/api/place-delete)

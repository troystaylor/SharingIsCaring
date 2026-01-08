# Microsoft Bookings MCP Connector

A complete MCP-enabled Power Platform custom connector for Microsoft Bookings via Microsoft Graph API.

## Features

### MCP Tools (30 tools for Copilot Studio)

**Business Management**
- `listBookingBusinesses` - List all booking businesses in tenant
- `getBookingBusiness` - Get business details
- `createBookingBusiness` - Create new business
- `updateBookingBusiness` - Update business properties
- `deleteBookingBusiness` - Delete a business
- `publishBookingBusiness` - Make scheduling page public
- `unpublishBookingBusiness` - Hide scheduling page

**Service Management**
- `listServices` - List services for a business
- `getService` - Get service details
- `createService` - Create new service
- `updateService` - Update service
- `deleteService` - Delete service

**Staff Management**
- `listStaffMembers` - List staff members
- `getStaffMember` - Get staff details
- `createStaffMember` - Add staff member
- `updateStaffMember` - Update staff
- `deleteStaffMember` - Remove staff
- `getStaffAvailability` - Check availability

**Customer Management**
- `listCustomers` - List customers
- `getCustomer` - Get customer details
- `createCustomer` - Add customer
- `updateCustomer` - Update customer
- `deleteCustomer` - Remove customer

**Appointment Management**
- `listAppointments` - List appointments
- `getAppointment` - Get appointment details
- `createAppointment` - Book appointment
- `updateAppointment` - Reschedule appointment
- `deleteAppointment` - Delete appointment
- `cancelAppointment` - Cancel with message
- `getCalendarView` - View appointments in date range

### REST Operations (for Power Automate)

All 30+ standard Graph API operations available as direct actions.

## Required Permissions

| Permission | Type | Description |
|------------|------|-------------|
| Bookings.Read.All | Delegated | Read booking businesses |
| Bookings.ReadWrite.All | Delegated | Full access to bookings |
| Bookings.Manage.All | Delegated | Manage booking businesses |

## Setup

### Prerequisites
- Microsoft 365 Business Premium subscription
- Azure AD app registration with Bookings permissions
- Power Platform environment

### Installation

1. **Import the connector**
   - Go to Power Platform maker portal → Custom connectors
   - Import from OpenAPI file: `apiDefinition.swagger.json`
   - Enable custom code and paste `script.csx`

2. **Configure OAuth**
   - Update client ID in connector settings
   - Set redirect URI from connector to Azure AD app

3. **Create connection**
   - Test the connector with a new connection
   - Grant consent for Bookings permissions

## Usage Examples

### Copilot Studio (MCP)

The connector exposes 30 tools that Copilot Studio can invoke naturally:

- "List all my booking businesses"
- "Create a new service called 'Consultation' for 1 hour"
- "Book an appointment for John at 2pm tomorrow"
- "Check Dana's availability next week"
- "Cancel the appointment and notify the customer"

### Power Automate

Use the REST operations directly:

```
Trigger: When a new item is created in SharePoint
Action: Create Appointment
  - bookingBusinessId: [Business ID]
  - serviceId: [Service ID]
  - startDateTime: [Calculated time]
  - customerEmailAddress: [From SharePoint]
```

## API Reference

Base URL: `https://graph.microsoft.com/v1.0`

### Key Endpoints

| Endpoint | Description |
|----------|-------------|
| `/solutions/bookingBusinesses` | Business collection |
| `/solutions/bookingBusinesses/{id}/services` | Services |
| `/solutions/bookingBusinesses/{id}/staffMembers` | Staff |
| `/solutions/bookingBusinesses/{id}/customers` | Customers |
| `/solutions/bookingBusinesses/{id}/appointments` | Appointments |
| `/solutions/bookingBusinesses/{id}/calendarView` | Date range view |

## Connection Parameters

| Parameter | Description |
|-----------|-------------|
| useBeta | Use Graph beta endpoint for preview features |

## Application Insights Telemetry

The connector supports optional Application Insights telemetry for monitoring and debugging.

### Setup

1. Create an Application Insights resource in Azure Portal
2. Copy the Connection String from Overview → Connection String
3. Update `APP_INSIGHTS_CONNECTION_STRING` in script.csx

### Tracked Events

| Event | Description |
|-------|-------------|
| `RequestReceived` | Every incoming request |
| `RequestCompleted` | Successful request with duration |
| `RequestError` | Failed request with error details |
| `MCPMethod` | MCP protocol method invoked |
| `ToolExecuting` | Tool execution started |
| `ToolExecuted` | Tool completed with duration |
| `ToolError` | Tool failed with error details |

### Sample KQL Queries

```kusto
// Tool usage by name
customEvents
| where name == "ToolExecuted"
| extend Tool = tostring(customDimensions.Tool)
| summarize Count = count(), AvgDuration = avg(todouble(customDimensions.DurationMs)) by Tool
| order by Count desc

// Errors in last 24 hours
customEvents
| where name in ("RequestError", "ToolError", "MCPError")
| where timestamp > ago(24h)
| project timestamp, name, Error = customDimensions.ErrorMessage, Tool = customDimensions.Tool
```

### Notes

- Telemetry is **optional** - leave connection string empty to disable
- Telemetry errors are suppressed and won't affect connector operation
- CorrelationId links all events for a single request

## Troubleshooting

### Common Issues

**"Access denied"**
- Verify Bookings.* permissions are granted
- Ensure user has access to the booking business

**"Business not found"**
- Check the bookingBusinessId format (usually email format)
- Verify business exists: `GET /solutions/bookingBusinesses`

**"Service requires Microsoft 365 Business Premium"**
- Bookings API requires Business Premium license

## Links

- [Microsoft Bookings API Overview](https://learn.microsoft.com/en-us/graph/api/resources/booking-api-overview)
- [Bookings Business Rules](https://learn.microsoft.com/en-us/graph/bookingsbusiness-business-rules)
- [Graph Explorer](https://developer.microsoft.com/graph/graph-explorer)

## Author

Troy Taylor - troy@troystaylor.com

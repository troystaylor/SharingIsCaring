# UKG Pro

## Overview
A Power Platform custom connector with MCP for the UKG Pro HCM REST API. Provides access to employee data, company configuration, payroll details, time and attendance, and organizational information from UKG Pro (formerly UltiPro). MCP-enabled for Copilot Studio integration with Application Insights telemetry.

## Prerequisites
- A UKG Pro account with API access enabled
- A **Web Service Account** created in UKG Pro (System Configuration > Security > Web Service Accounts)
- The **US-Customer-Api-Key** (5-character key found in System Configuration > Security > Web Services)


## Setting Up a Web Service Account
1. Sign in to UKG Pro as a system administrator
2. Navigate to **Menu > System Configuration > Security > Web Service Accounts**
3. Click **Add** to create a new account
4. Provide a username and password
5. Assign the appropriate API permissions for the data you need to access
6. Save the account

## Finding Your Customer API Key
1. Navigate to **Menu > System Configuration > Security > Web Services**
2. The **US-Customer-Api-Key** is displayed on this page
3. This is a 5-character alphanumeric key unique to your UKG Pro instance

## Connector Setup
1. Import the connector into your Power Platform environment
2. Create a new connection with the following parameters:
   - **Username**: Your Web Service Account username
   - **Password**: Your Web Service Account password
   - **Customer API Key**: Your 5-character US-Customer-Api-Key

## Available Operations (32)

### Configuration (6)
| Operation | Description |
|-----------|-------------|
| Get Company Details | Returns company configuration details |
| Get All Jobs | Retrieve all job records |
| Get All Locations | Retrieve all location records |
| Get All Org Levels | Retrieve all organization level records |
| Get All Earnings | Retrieve all earnings codes |
| Get Positions | Retrieve all position records |

### Personnel (14)
| Operation | Description |
|-----------|-------------|
| Get All Person Details | Retrieve all person detail records |
| Get Person Details By Employee | Retrieve person details for a specific employee |
| Get All Employment Details | Retrieve all employment detail records |
| Get All Compensation Details | Retrieve all compensation detail records |
| Get Compensation Details By Employee | Retrieve compensation details for a specific employee |
| Get Employee Demographic Details | Retrieve demographic details for employees |
| Employee ID Lookup | Look up employee IDs using various identifiers |
| Get Employee Changes By Date | Retrieve employee changes within a date range |
| Get All PTO Plans | Retrieve all PTO plan records |
| Get Supervisor Details | Retrieve supervisor details for employees |
| Get Employee Contacts | Retrieve emergency contacts for employees |
| Get Employee Deductions | Retrieve deduction information for employees |
| Get Employee Job History | Retrieve job history details for employees |
| Get Employee Education | Retrieve education records for employees |

### Payroll (4)
| Operation | Description |
|-----------|-------------|
| Get Direct Deposit | Retrieve direct deposit information |
| Get Company Pay Statements | Get pay statements summary for a company by date range |
| Get Employee Pay Statements | Get pay statements for an employee by date range |
| Get Earnings History | Retrieve earnings history base elements |

### Time and Attendance (4)
| Operation | Description |
|-----------|-------------|
| Get Clock Transactions | Get processed clock transactions |
| Get Work Summaries | Obtain work summaries for employees |
| Get Time Off Requests | Retrieve all time off requests |
| Get Time Employees | Retrieve employees for time and attendance |

### Security (2)
| Operation | Description |
|-----------|-------------|
| Get Roles | Retrieve security roles |
| Get User Profile Details | Retrieve user profile information |

### Audit (1)
| Operation | Description |
|-----------|-------------|
| Get Audit Details | Retrieve audit trail details |

### MCP (1)
| Operation | Description |
|-----------|-------------|
| Invoke UKG Pro MCP | Model Context Protocol endpoint for Copilot Studio integration |

## Pagination
All list operations support pagination through the **Page** and **Per Page** parameters (available under advanced options). Use these to navigate through large result sets.

## Copilot Studio (MCP)
This connector includes an MCP (Model Context Protocol) endpoint that exposes all 31 UKG Pro operations as tools for AI agents in Copilot Studio. Add the connector as an action in your Copilot Studio agent to enable natural language queries against UKG Pro data.

MCP tools include: `get_employees`, `lookup_employee_id`, `get_compensation`, `get_clock_transactions`, `get_time_off_requests`, `get_pay_statements`, and more.

## Application Insights
The connector supports Azure Application Insights telemetry. To enable, set your connection string in the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`. Tracked events:
- **RequestReceived** / **RequestCompleted** — all operations with correlation IDs and timing
- **MCPRequest** / **MCPToolCall** — MCP-specific method and tool tracking
- **RequestError** / **MCPToolError** — error logging with exception details

## Webhook Triggers
UKG Pro supports outbound webhooks configured in the UKG Pro UI, but does **not** provide a REST API for managing webhook subscriptions. Power Platform webhook triggers cannot be implemented for this connector. To react to UKG events, consider polling with the **Get Employee Changes By Date** operation.

## Known Issues and Limitations
- The API returns data only for employees the Web Service Account has permission to access
- Some endpoints may not be available depending on your UKG Pro modules and licensing
- Rate limits may apply; consult UKG documentation for current limits

## References
- [UKG Developer Portal](https://developer.ukg.com)
- [UKG Pro HCM API Documentation](https://developer.ukg.com/hcm/reference)

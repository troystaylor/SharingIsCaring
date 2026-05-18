# UKG Pro WFM

## Overview
A Power Platform custom connector with MCP for the UKG Pro Workforce Management (WFM, formerly UKG Dimensions) REST API. Provides access to persons, schedules, timecards, punches, paycodes, time off, accruals, leave cases, hyperfinds, notifications, and tenant information. MCP-enabled for Copilot Studio integration with Application Insights telemetry.

This connector consolidates the surface covered by Microsoft's three preview connectors (`UKG Pro WFM Employee`, `UKG Pro WFM People`, `UKG Pro WFM Timekeeping`) into a single MCP-enabled connector.

## Prerequisites
- A UKG Pro WFM tenant with API access enabled.
- An **integration App Key** registered with UKG (used as the `client_id` and `appkey` header).
- A **service account** username and password authorized for the data and operations you intend to use.
- Knowledge of your tenant base URL (e.g. `https://yourtenant.mykronos.com`).

## Authentication

UKG Pro WFM uses an OAuth 2.0 **password grant** flow. The connector exchanges your service-account credentials for a bearer access token by calling `POST {tenantUrl}/api/authentication/access_token` with:

- Header: `appkey: {appKey}`
- Body (form-encoded): `username`, `password`, `client_id={appKey}`, `grant_type=password`

The bearer token is cached in memory inside `script.csx` and refreshed before expiry. The `appkey` header is sent on every outbound call alongside `Authorization: Bearer {token}`.

> Power Platform's OAuth `securityDefinition` cannot be combined with extra `connectionParameters`. Because WFM password-grant auth requires an additional App Key parameter, this connector ships with **no** `securityDefinition` and performs the token exchange entirely in `script.csx`.

## Setting Up an App Key
1. Submit a UKG Developer Hub request to register an integration application against your tenant.
2. UKG provisions an **App Key** for the integration. Store it securely.
3. Ensure the app key is enabled for the WFM Common Resources and any domain APIs (Timekeeping, Scheduling, Accruals, etc.) you intend to call.

## Setting Up a Service Account
1. Sign in to UKG Pro WFM as a tenant administrator.
2. Create or designate a service account user.
3. Grant the service account function access points (FAPs) covering the operations you plan to call — at minimum People, Schedule, Timecard, Punches, Time Off, Accruals.
4. Note the username and password.

## Connector Setup
1. Import the connector into your Power Platform environment.
2. Create a new connection with the following parameters:
   - **Tenant URL**: e.g. `https://yourtenant.mykronos.com` (no trailing slash)
   - **Username**: Your service account username
   - **Password**: Your service account password
   - **App Key**: The integration App Key issued by UKG

## Available Operations (32)

### Common Resources (8)
| Operation | Description |
|-----------|-------------|
| Retrieve Tenant Information | Get tenant-level configuration details |
| Get Current User | Get the person record for the authenticated user |
| Retrieve Persons | Retrieve all persons (paginated) |
| Retrieve Persons By Criteria | Retrieve persons matching filter criteria |
| Get Person By ID | Get a single person by person identity |
| Retrieve Locations | Retrieve organizational locations |
| Retrieve Jobs | Retrieve job definitions |
| Retrieve Cost Centers | Retrieve cost centers |

### Schedules (3)
| Operation | Description |
|-----------|-------------|
| Retrieve Employee Schedule | Retrieve schedules for an employee or set of employees |
| Retrieve Schedule | Retrieve manager-view schedules |
| Retrieve Employee Schedule Changes | Retrieve schedule change events in a date range |

### Timecards & Punches (6)
| Operation | Description |
|-----------|-------------|
| Retrieve Employee Timecard | Retrieve the authenticated employee's timecard |
| Retrieve Timecards | Retrieve timecards as a manager |
| Retrieve Punches | Retrieve punches in real time |
| Bulk Import Punches | Bulk import punches asynchronously |
| Add Timestamp | Punch in / out via timestamp |
| Retrieve Timestamp | Retrieve current timestamp state for an employee |

### Paycodes & Pay Code Edits (3)
| Operation | Description |
|-----------|-------------|
| Retrieve Paycodes | Retrieve configured pay codes |
| Create Pay Code Edit | Add a pay code edit to a timecard |
| Bulk Import Pay Code Edits | Bulk import pay code edits asynchronously |

### Time Off (4)
| Operation | Description |
|-----------|-------------|
| Retrieve Employee Time Off Requests | Retrieve the authenticated employee's time off requests |
| Retrieve Manager Time Off Requests | Retrieve time off requests as a manager |
| Create Employee Time Off Request | Submit a new time off request |
| Update Manager Time Off Request | Approve / reject / cancel a time off request |

### Accruals (2)
| Operation | Description |
|-----------|-------------|
| Retrieve Accrual Profiles | Retrieve all accrual profile definitions |
| Retrieve Accrual Balance | Retrieve accrual balances for an employee |

### Leave (2)
| Operation | Description |
|-----------|-------------|
| Retrieve Leave Cases | Retrieve all leave cases |
| Retrieve Leave Case By ID | Retrieve leave case details by case ID |

### Hyperfinds (2)
| Operation | Description |
|-----------|-------------|
| Retrieve Hyperfind Queries | Retrieve configured hyperfind queries |
| Execute Hyperfind Query | Execute a hyperfind by ID and return matching persons |

### Notifications (1)
| Operation | Description |
|-----------|-------------|
| Retrieve Notifications | Retrieve in-app notifications |

### MCP (1)
| Operation | Description |
|-----------|-------------|
| Invoke UKG Pro WFM MCP | Model Context Protocol endpoint for Copilot Studio integration |

## Copilot Studio (MCP)
This connector includes an MCP (Model Context Protocol) endpoint that exposes the WFM operations above as tools for AI agents in Copilot Studio. Add the connector as an action in your Copilot Studio agent to enable natural-language queries against WFM data.

MCP tools include: `retrieve_persons`, `get_current_user`, `retrieve_employee_timecard`, `retrieve_punches`, `create_employee_time_off_request`, `retrieve_accrual_balance`, `execute_hyperfind`, and more.

## Endpoint Path Verification
WFM REST endpoint paths in `apiDefinition.swagger.json` follow documented UKG Dimensions API conventions (`/api/v1/commons/*`, `/api/v1/timekeeping/*`, `/api/v1/scheduling/*`, `/api/v1/timeoff/*`, etc.). Before promoting beyond Power Platform Demo, validate each path against your tenant's published API reference in the UKG Developer Hub for your specific tenant version. Path differences can be patched in `script.csx` (the `ExecuteToolAsync` switch) without touching the Swagger surface.

## Application Insights Telemetry
The `script.csx` includes Application Insights instrumentation. To enable, set the `APP_INSIGHTS_CONNECTION_STRING` constant at the top of `script.csx` to your App Insights connection string. All MCP requests, tool calls, errors, and request/response durations are logged. Telemetry failures are silently suppressed.

## Deployment
Deploy via PAC CLI:
```powershell
pac connector create --api-definition-file ".\apiDefinition.swagger.json" --api-properties-file ".\apiProperties.json" --script ".\script.csx" --environment c4f149b0-9f42-e8c4-97d8-bc69b59f971c
```

## Validation
Run `ppcv ./` before deploying to catch Swagger / properties / script issues:
```powershell
ppcv "./UKG Pro WFM"
```

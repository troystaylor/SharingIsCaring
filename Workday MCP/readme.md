# Workday MCP

Power MCP tool server for Workday Web Services (v46.0 / 2026R1). Enables Copilot Studio agents to interact with Workday across 12 service modules via SOAP-based web services.

## Overview

This custom connector exposes ~55 MCP tools spanning Workday's core business services:

| Service | Tools | Type |
|---------|-------|------|
| Human Resources | 14 | Read + Write |
| Staffing | 3 | Read |
| Absence Management | 3 | Read |
| Compensation | 2 | Read |
| Recruiting | 2 | Read |
| Time Tracking | 2 | Read |
| Benefits Administration | 4 | Read |
| Performance Management | 6 | Read + Write |
| Talent | 7 | Read + Write |
| Learning | 4 | Read |
| Payroll Interface | 3 | Read |
| Financial Management | 6 | Read |
| Resource Management | 6 | Read |

## Prerequisites

### 1. Workday Admin Steps

A Workday administrator must complete the following:

#### Create an Integration System User (ISU)

1. In Workday, search for **Create Integration System User** and select it
2. Fill out each field (username, password) and select OK
3. This is a system user not associated with a real person

#### Create a Security Group

1. Search for **Create Security Group** and select it
2. Select **Integration System Security Group (Unconstrained)**
3. Add the Integration System User to this group
4. Search for **Maintain Permissions for Security Group**
5. Add **Get Only** permissions for the following Domain Security Policies:
   - Worker Data: Public Worker Reports
   - Worker Data: Organization Information
   - Person Data: Private Work Email Integration
   - Person Data: Skills
   - Worker Data: Current Staffing Information
   - (Add additional domains based on which services are needed)
6. Search for **Activate Pending Security Policy Changes** and confirm

#### Register an API Client

1. Search for **Register API Client** and select it
2. Fill out:
   - **Client Name**: e.g., "CopilotStudioConnector"
   - **Client Grant Type**: Authorization Code Grant
   - **Access Token Type**: Bearer
   - **Redirect URI**: `https://global.consent.azure-apim.net/redirect`
   - **Scope (Functional Areas)**: Select all functional areas needed (Staffing, Contact Information, Worker Profile and Skills, etc.)
3. Select OK and save the following values:
   - **Client ID**
   - **Client Secret**

### 2. Gather Workday Tenant Details

You need the following from your Workday environment:

| Value | Example | Description |
|-------|---------|-------------|
| Workday Hostname | `wd3-impl-services1` | Your Workday instance hostname |
| Tenant Name | `contoso4` | Your Workday tenant identifier |
| Client ID | `MjJhY2...` | From the API Client registration |
| Client Secret | `abcd1234...` | From the API Client registration |

**URL patterns:**
- Authorization: `https://<hostname>.workday.com/workday/<tenant>/authorize`
- Token: `https://<hostname>.workday.com/ccx/oauth2/<tenant>/token`
- SOAP: `https://<hostname>.workday.com/ccx/service/<tenant>/<service>/v46.0`

## Configuration

### Replace Placeholder Values

Before deploying, replace all placeholder values:

#### In `apiProperties.json`:
- `[WORKDAY_CLIENT_ID]` → Your OAuth Client ID
- `[WORKDAY_HOSTNAME]` → Your Workday hostname (e.g., `wd3-impl-services1`)
- `[TENANT_NAME]` → Your tenant name (e.g., `contoso4`)

#### In `apiDefinition.swagger.json`:
- `[WORKDAY_HOSTNAME]` → Your Workday hostname

#### In `script.csx`:
- `[WORKDAY_HOSTNAME]` → Your Workday hostname
- `[TENANT_NAME]` → Your tenant name

### Application Insights (Optional)

To enable telemetry, set the `APP_INSIGHTS_CONNECTION_STRING` constant in `script.csx`:

```
Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/
```

Get from: Azure Portal → Application Insights → Overview → Connection String

## Deployment

```powershell
# Install PAC CLI if not already installed
# https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction

# Create the connector
pac connector create --settings-file ./apiProperties.json --api-definition-file ./apiDefinition.swagger.json --script-file ./script.csx
```

## Available MCP Tools

### Human Resources

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_workers` | Get_Workers | Search and retrieve workers |
| `get_employee` | Get_Employee | Get full employee record |
| `get_employee_personal_info` | Get_Employee_Personal_Info | Get personal/biographic info |
| `get_employee_employment_info` | Get_Employee_Employment_Info | Get position/job/status info |
| `get_organizations` | Get_Organizations | List organizations by type |
| `get_organization` | Get_Organization | Get single organization detail |
| `get_locations` | Get_Locations | Get location data |
| `get_job_profiles` | Get_Job_Profiles | Get job profile data |
| `get_job_families` | Get_Job_Families | Get job family data |
| `get_server_timestamp` | Get_Server_Timestamp | Health check / connectivity test |
| `change_work_contact_info` | Change_Work_Contact_Information | Update work email/phone/address |
| `change_business_title` | Change_Business_Title | Update business title |
| `change_preferred_name` | Change_Preferred_Name | Update preferred name |
| `maintain_contact_info` | Maintain_Contact_Information | Create/update contact info |

### Staffing

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_positions` | Get_Positions | Get position management data |
| `get_applicants` | Get_Applicants | Get pre-hire data |
| `get_worker_documents` | Get_Worker_Documents | Get worker document data |

### Absence Management

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_absence_inputs` | Get_Absence_Inputs | Get absence inputs/accruals |
| `get_leave_requests` | Get_Leave_Requests | Get leave of absence requests |
| `get_time_off_plans` | Get_Time_Off_Plans | Get time off plan data |

### Compensation

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_compensation_plans` | Get_Compensation_Plans | Get compensation plan data |
| `get_compensation_packages` | Get_Compensation_Packages | Get compensation package data |

### Recruiting

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_job_postings` | Get_Job_Postings | Get job posting data |
| `get_job_applications` | Get_Job_Applications | Get job application data |

### Time Tracking

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_time_entries` | Get_Time_Entries | Get worker time entries |
| `get_work_schedules` | Get_Work_Schedule_Calendars | Get work schedule data |

### Benefits Administration

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_benefit_plans` | Get_Benefit_Plans | Get benefit plan data |
| `get_benefit_enrollments` | Get_Benefit_Annual_Rates | Get benefit enrollment data |
| `get_benefit_annual_rates` | Get_Benefit_Annual_Rates | Get benefit annual rates |
| `get_health_care_rates` | Get_Health_Care_Rates | Get health care rates |

### Performance Management

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_employee_reviews` | Get_Employee_Reviews | Get employee reviews |
| `get_competencies` | Get_Competencies | Get competency definitions |
| `get_organization_goals` | Get_Organization_Goals | Get organization goals |
| `get_check_ins` | Get_Check-Ins | Get check-in records |
| `manage_goals` | Manage_Goals | Add/edit worker goals |
| `give_feedback` | Give_Feedback | Add anytime feedback |

### Talent

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_skills` | Get_Skills | Get skill definitions |
| `get_manage_skills` | Get_Manage_Skills | Get worker skill assignments |
| `get_succession_plans` | Get_Succession_Plans | Get succession plans |
| `get_talent_pools` | Get_Talent_Pools | Get talent pool membership |
| `get_development_items` | Get_Development_Items | Get development items |
| `get_work_experiences` | Get_Work_Experiences | Get work experience records |
| `manage_skills` | Manage_Skills | Add/remove worker skills |

### Learning

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_learning_courses` | Get_Learning_Courses | Get learning courses |
| `get_learning_enrollments` | Get_Learning_Enrollments | Get enrollment records |
| `get_learning_course_offerings` | Get_Learning_Course_Offerings | Get course offerings |
| `get_learning_programs` | Get_Learning_Programs | Get learning programs |

### Payroll Interface

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_payees` | Get_Payees | Get payee data |
| `get_worker_costing_allocations` | Get_Worker_Costing_Allocations | Get costing allocations |
| `get_external_payroll_inputs` | Get_External_Payroll_Inputs | Get payroll inputs |

### Financial Management

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_journals` | Get_Journals | Get journal entries |
| `get_workday_companies` | Get_Workday_Companies | Get company data |
| `get_cost_centers` | Get_Cost_Centers | Get cost center data |
| `get_currency_conversion_rates` | Get_Currency_Conversion_Rates | Get FX rates |
| `get_projects` | Get_Basic_Projects | Get project data |
| `get_business_units` | Get_Business_Units | Get business unit data |

### Resource Management

| Tool | Operation | Description |
|------|-----------|-------------|
| `get_suppliers` | Get_Suppliers | Get supplier data |
| `get_expense_reports` | Get_Expense_Reports | Get expense reports |
| `get_purchase_orders` | Get_Purchase_Orders | Get purchase orders |
| `get_supplier_invoices` | Get_Supplier_Invoices | Get supplier invoices |
| `get_assets` | Get_Assets | Get business assets |
| `get_requisitions` | Get_Requisitions | Get requisitions |

## Authentication

This connector uses **OAuth 2.0 Authorization Code** flow via Workday's OAuth endpoints. The OAuth URLs are tenant-specific and hardcoded in the connector files.

- **Identity Provider**: `oauth2generic` (Power Platform generic OAuth 2.0)
- **Grant Type**: Authorization Code
- **Redirect URI**: `https://global.consent.azure-apim.net/redirect`

## Workday API Version

This connector targets **Workday Web Services v46.0 (2026R1)**. The API version is configurable via the `ApiVersion` constant in `script.csx`.

## Notes

- All SOAP operations use the `urn:com.workday/bsvc` XML namespace
- OAuth bearer tokens are passed via HTTP Authorization header (not in SOAP headers)
- Write operations (change_*, manage_*, give_*) route through Workday's business process framework and may require approvals
- The connector automatically converts Workday XML responses to JSON for Copilot consumption
